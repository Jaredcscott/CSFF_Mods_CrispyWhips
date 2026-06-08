using System.Diagnostics;
using System.Threading.Tasks;
using CSFFModFramework.Data;
using CSFFModFramework.Discovery;
using CSFFModFramework.Reflection;
using CSFFModFramework.Util;

namespace CSFFModFramework.Loading;

internal static class JsonDataLoader
{
    // Maps directory name to ScriptableObject type name
    private static readonly Dictionary<string, string> DirToTypeName = new(StringComparer.OrdinalIgnoreCase)
    {
        { "CardData", "CardData" },
        { "CharacterPerk", "CharacterPerk" },
        { "PerkGroup", "PerkGroup" },
        { "GameStat", "GameStat" },
        { "SpiceTag", "SpiceTag" },
    };

    /// <summary>
    /// Raw JSON content cached during load, keyed by UniqueID.
    /// Reused by WarpResolver and SpriteResolver to avoid redundant filesystem reads.
    /// </summary>
    internal static Dictionary<string, string> JsonByUniqueId { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Pre-parsed JSON trees cached during load, keyed by UniqueID.
    /// Eliminates redundant MiniJson.Parse calls in WarpResolver and SpriteResolver
    /// (previously each service parsed the same N files independently → 2N→0 extra parses).
    /// </summary>
    internal static Dictionary<string, Dictionary<string, object>> ParsedJsonByUniqueId { get; }
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// All UniqueIDs loaded from mod JSON files.
    /// Reused by ProducedCardService, PassiveEffectNormalizer, AlwaysUpdateService, PerkRelocationService.
    /// </summary>
    internal static HashSet<string> AllModUniqueIds { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Maps UniqueID → mod name. Used by WarpResolver to group unresolved refs by source mod.
    /// </summary>
    internal static Dictionary<string, string> UniqueIdToModName { get; } = new(StringComparer.OrdinalIgnoreCase);

    // Below this many files, parse serially — thread-pool spin-up isn't worth it.
    private const int ParallelParseThreshold = 24;

    // Track skipped types per mod for batched summary warning.
    private static readonly Dictionary<string, HashSet<string>> _skippedTypes = new();

    public static void LoadAll(List<ModManifest> mods)
    {
        JsonByUniqueId.Clear();
        ParsedJsonByUniqueId.Clear();
        AllModUniqueIds.Clear();
        UniqueIdToModName.Clear();
        ExtraDataStore.Clear();
        _skippedTypes.Clear();

        var sw = Stopwatch.StartNew();

        // ── Stage 1: discovery (serial) ──────────────────────────────────────────
        // Resolve each directory's SO Type ONCE here — ReflectionCache (FindType/GetMethod)
        // uses non-thread-safe dictionaries, so all of it stays on this thread. Build a flat
        // work list in the SAME deterministic order the old serial loader used (mod order →
        // DirToTypeName order → ScriptableObject subdirs; file order within each dir). That
        // order is what keeps the duplicate-UniqueID "second file wins" warning stable.
        var workItems = new List<WorkItem>();
        foreach (var mod in mods)
        {
            foreach (var kvp in DirToTypeName)
                CollectFiles(mod, ResolveDir(mod, kvp.Key), kvp.Value, workItems);

            var soDir = Path.Combine(mod.DirectoryPath, "ScriptableObject");
            if (Directory.Exists(soDir))
                foreach (var typeDir in Directory.GetDirectories(soDir))
                    CollectFiles(mod, typeDir, Path.GetFileName(typeDir), workItems);
        }
        var discoveryMs = sw.ElapsedMilliseconds;

        // ── Stage 2: read + parse (parallelizable) ───────────────────────────────
        // File.ReadAllText + QuickExtractString + MiniJson.Parse touch no Unity API and no
        // shared mutable state (MiniJson is a stateless static; each thread writes only its own
        // results slot). This lifts the per-file read + tree-build off the serial critical path.
        sw.Restart();
        var results = new ParseResult[workItems.Count];
        if (workItems.Count >= ParallelParseThreshold)
            Parallel.For(0, workItems.Count, i => results[i] = ParseFile(workItems[i]));
        else
            for (int i = 0; i < workItems.Count; i++) results[i] = ParseFile(workItems[i]);
        var parseMs = sw.ElapsedMilliseconds;

        // ── Stage 3: materialize (serial, main thread) ───────────────────────────
        // All Unity APIs (ScriptableObject.CreateInstance, JsonUtility.FromJsonOverwrite, Init),
        // the non-thread-safe framework caches, and the game-registry writes happen here, in the
        // original file order so dedup/registration semantics are byte-identical to the old loader.
        sw.Restart();
        long createTicks = 0, fromJsonTicks = 0, initTicks = 0, registerTicks = 0;
        var modCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        int totalLoaded = 0;

        for (int i = 0; i < results.Length; i++)
        {
            var item = workItems[i];
            var r = results[i];

            if (r.ReadOrParseError != null)
            {
                Log.Warn($"JsonDataLoader: failed to load {item.FilePath}: {r.ReadOrParseError}");
                continue;
            }

            var json = r.Json;
            var uniqueId = r.UniqueId;

            // Cache raw JSON + UniqueID for downstream services (WarpResolver, SpriteResolver, etc.)
            if (!string.IsNullOrEmpty(uniqueId))
            {
                if (JsonByUniqueId.ContainsKey(uniqueId))
                    Log.Warn($"JsonDataLoader: duplicate UniqueID '{uniqueId}' in {item.Mod.Name} — second file overwrites first (check for duplicate JSON files)");
                JsonByUniqueId[uniqueId] = json;
                if (r.ParsedTree != null)
                    ParsedJsonByUniqueId[uniqueId] = r.ParsedTree;
                AllModUniqueIds.Add(uniqueId);
                UniqueIdToModName[uniqueId] = item.Mod.Name;

                // Sidecar extras: Foo.json → Foo.extras.json (read in stage 2).
                if (r.SidecarError != null)
                    Log.Warn($"JsonDataLoader: failed to read sidecar for {item.FilePath}: {r.SidecarError}");
                else if (r.SidecarJson != null)
                    ExtraDataStore.Store(uniqueId, r.SidecarJson);
            }

            long t0 = Stopwatch.GetTimestamp();
            var obj = CreateInstanceSafe(item.Type);
            createTicks += Stopwatch.GetTimestamp() - t0;
            if (obj == null)
            {
                Log.Warn($"JsonDataLoader: failed to create instance of {item.TypeName} for {item.FilePath}");
                continue;
            }

            t0 = Stopwatch.GetTimestamp();
            JsonUtility.FromJsonOverwrite(json, obj);
            fromJsonTicks += Stopwatch.GetTimestamp() - t0;
            obj.name = Path.GetFileNameWithoutExtension(item.FilePath);

            if (obj is UniqueIDScriptable uidObj)
            {
                // Call Init() via reflection
                t0 = Stopwatch.GetTimestamp();
                var initMethod = ReflectionCache.GetMethod(item.Type, "Init");
                if (initMethod != null)
                {
                    try { initMethod.Invoke(uidObj, null); }
                    catch { /* Init may fail before full resolution — that's OK */ }
                }
                initTicks += Stopwatch.GetTimestamp() - t0;

                // Fallback: if FromJsonOverwrite didn't set UniqueID (field may be non-serialized),
                // use the value we extracted from raw JSON
                if (string.IsNullOrEmpty(uidObj.UniqueID) && !string.IsNullOrEmpty(uniqueId))
                {
                    try
                    {
                        var uidField = AccessTools.Field(typeof(UniqueIDScriptable), "UniqueID")
                                    ?? AccessTools.Field(typeof(UniqueIDScriptable), "uniqueID")
                                    ?? AccessTools.Field(typeof(UniqueIDScriptable), "m_UniqueID");
                        uidField?.SetValue(uidObj, uniqueId);
                        Log.Debug($"JsonDataLoader: set UniqueID via reflection for {obj.name} → {uniqueId}");
                    }
                    catch (Exception ex) { Log.Warn($"JsonDataLoader: could not set UniqueID on {obj.name}: {ex.GetType().Name}"); }
                }

                // Register in game's AllUniqueObjects + GameLoad.Instance.DataBase.AllData
                // (additive — first-wins coexistence with any other plugin).
                t0 = Stopwatch.GetTimestamp();
                GameRegistry.TryRegister(uidObj);
                RegisterInAllData(uidObj);
                registerTicks += Stopwatch.GetTimestamp() - t0;
            }

            totalLoaded++;
            modCounts.TryGetValue(item.Mod.Name, out var mc);
            modCounts[item.Mod.Name] = mc + 1;
        }
        var materializeMs = sw.ElapsedMilliseconds;

        foreach (var kvp in modCounts)
            if (kvp.Value > 0) Log.Debug($"JsonDataLoader: loaded {kvp.Value} objects from {kvp.Key}");

        Log.Debug($"JsonDataLoader: {totalLoaded} total objects loaded"
                 + (ExtraDataStore.Count > 0 ? $", {ExtraDataStore.Count} sidecar extras" : ""));

        // Log batched summary of skipped types at Debug level — these are typically
        // third-party mods from other games (e.g. CSTI) whose types CSFF doesn't resolve.
        foreach (var kvp in _skippedTypes)
            Log.Debug($"JsonDataLoader: [{kvp.Key}] skipped unknown types: {string.Join(", ", kvp.Value)}");
        _skippedTypes.Clear();

        // One [Perf] line breaking down the dominant load phase so the read+parse vs Unity-side
        // split is visible without verbose logging (answers "where does JsonDataLoader's time go").
        Log.Info($"[Perf] JsonDataLoader: {workItems.Count} files, {totalLoaded} loaded — "
               + $"discovery={discoveryMs}ms, "
               + $"parse({(workItems.Count >= ParallelParseThreshold ? $"parallel×{Environment.ProcessorCount}" : "serial")})={parseMs}ms, "
               + $"materialize={materializeMs}ms [create={TicksToMs(createTicks):F0} fromJson={TicksToMs(fromJsonTicks):F0} init={TicksToMs(initTicks):F0} register={TicksToMs(registerTicks):F0}]");
    }

    private static double TicksToMs(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    /// <summary>Resolve a mod content dir, falling back to the nested <c>Data/&lt;subDir&gt;</c> layout.</summary>
    private static string ResolveDir(ModManifest mod, string subDir)
    {
        var dir = Path.Combine(mod.DirectoryPath, subDir);
        if (Directory.Exists(dir)) return dir;
        // Fallback: some third-party mods (e.g. NPCTracker) nest data under Data/<subDir>/
        var dataDir = Path.Combine(mod.DirectoryPath, "Data", subDir);
        return Directory.Exists(dataDir) ? dataDir : null;
    }

    /// <summary>
    /// Resolve the directory's SO Type (serial — ReflectionCache is not thread-safe) and append a
    /// <see cref="WorkItem"/> per eligible JSON file, applying the .extras.json and Trigger/ skip filters.
    /// </summary>
    private static void CollectFiles(ModManifest mod, string dir, string typeName, List<WorkItem> into)
    {
        if (dir == null) return;

        var type = ReflectionCache.FindType(typeName);
        if (type == null)
        {
            if (!_skippedTypes.TryGetValue(mod.Name, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _skippedTypes[mod.Name] = set;
            }
            set.Add(typeName);
            return;
        }

        foreach (var file in Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories))
        {
            // Skip sidecar extras files — they're loaded as a side effect of their content sibling.
            if (file.EndsWith(".extras.json", StringComparison.OrdinalIgnoreCase)) continue;

            // Skip files inside a Trigger/ subdirectory — those are TriggerDefinition JSON files
            // consumed by TriggerLoader, not CardData SOs. Loading them as CardData creates phantom
            // CT0 entries that pollute AllData and confuse WarpResolver.
            var parentDirName = Path.GetFileName(Path.GetDirectoryName(file) ?? "");
            if (parentDirName.Equals("Trigger", StringComparison.OrdinalIgnoreCase)) continue;

            into.Add(new WorkItem(mod, file, type, typeName));
        }
    }

    /// <summary>
    /// Stage-2 work: read the file, extract its UniqueID, parse the MiniJson tree, and read any
    /// sidecar. Pure managed work (no Unity API, no shared mutable state) — safe to run in parallel.
    /// </summary>
    private static ParseResult ParseFile(WorkItem item)
    {
        var r = new ParseResult();
        try
        {
            r.Json = File.ReadAllText(item.FilePath);
            r.UniqueId = PathUtil.QuickExtractString(r.Json, "UniqueID");
            r.ParsedTree = MiniJson.Parse(r.Json) as Dictionary<string, object>;

            if (!string.IsNullOrEmpty(r.UniqueId))
            {
                var sidecar = Path.ChangeExtension(item.FilePath, null) + ".extras.json";
                if (File.Exists(sidecar))
                {
                    try { r.SidecarJson = File.ReadAllText(sidecar); }
                    catch (Exception ex) { r.SidecarError = Log.ExceptionText(ex); }
                }
            }
        }
        catch (Exception ex)
        {
            r.ReadOrParseError = Log.ExceptionText(ex);
        }
        return r;
    }

    private readonly struct WorkItem
    {
        public readonly ModManifest Mod;
        public readonly string FilePath;
        public readonly Type Type;
        public readonly string TypeName;
        public WorkItem(ModManifest mod, string filePath, Type type, string typeName)
        {
            Mod = mod; FilePath = filePath; Type = type; TypeName = typeName;
        }
    }

    private sealed class ParseResult
    {
        public string Json;
        public string UniqueId;
        public Dictionary<string, object> ParsedTree;
        public string SidecarJson;
        public string SidecarError;
        public string ReadOrParseError;
    }

    private enum FieldInitKind { EmptyArray, NewList, EmptyString, NewSerializable }

    private readonly struct FieldInitStep
    {
        public readonly FieldInfo Field;
        public readonly FieldInitKind Kind;
        public readonly Type ElementType; // element type for EmptyArray
        public FieldInitStep(FieldInfo field, FieldInitKind kind, Type elementType)
        {
            Field = field; Kind = kind; ElementType = elementType;
        }
    }

    // The set of fields that need defaulting (and how) is identical for every instance of a
    // given Type, but rediscovering it via GetFields per file is the kind of repeated reflection
    // CLAUDE.md flags. Cache the init plan per concrete Type; the per-card GetValue==null check
    // for serializable fields stays at apply time so behavior is byte-identical.
    private static readonly Dictionary<Type, FieldInitStep[]> _initPlanCache = new();

    private static FieldInitStep[] GetInitPlan(Type type)
    {
        if (_initPlanCache.TryGetValue(type, out var cached)) return cached;

        var steps = new List<FieldInitStep>();
        // Walk full hierarchy so base-class LocalizedString fields (e.g. any declared in
        // UniqueIDScriptable) are initialized too. GetDeclaredFields only returns the exact
        // type's own fields and would miss inherited ones.
        var currentType = type;
        while (currentType != null && currentType != typeof(object)
            && currentType != typeof(UnityEngine.Object)
            && currentType != typeof(ScriptableObject))
        {
            foreach (var field in currentType.GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                var ft = field.FieldType;
                if (ft.IsArray)
                    steps.Add(new FieldInitStep(field, FieldInitKind.EmptyArray, ft.GetElementType() ?? typeof(object)));
                else if (ft.IsGenericType && ft.GetGenericTypeDefinition() == typeof(List<>))
                    steps.Add(new FieldInitStep(field, FieldInitKind.NewList, null));
                else if (ft == typeof(string))
                    steps.Add(new FieldInitStep(field, FieldInitKind.EmptyString, null));
                // Serializable class fields (LocalizedString, etc.). Skip Unity Object types
                // (Sprite, AudioClip) — resolved later by WarpResolver. The instance null-check
                // is deferred to apply time.
                else if (ft.IsClass && ft.IsSerializable
                         && !typeof(UnityEngine.Object).IsAssignableFrom(ft))
                    steps.Add(new FieldInitStep(field, FieldInitKind.NewSerializable, null));
            }
            currentType = currentType.BaseType;
        }

        var plan = steps.ToArray();
        _initPlanCache[type] = plan;
        return plan;
    }

    /// <summary>
    /// Creates a ScriptableObject and initializes all fields to safe defaults
    /// (empty arrays, empty lists, empty strings). Prevents NullReferenceExceptions
    /// during JsonUtility.FromJsonOverwrite and WarpData resolution.
    /// </summary>
    internal static ScriptableObject CreateInstanceSafe(Type type)
    {
        var obj = ScriptableObject.CreateInstance(type);
        if (obj == null) return null;

        foreach (var step in GetInitPlan(type))
        {
            try
            {
                switch (step.Kind)
                {
                    case FieldInitKind.EmptyArray:
                        step.Field.SetValue(obj, Array.CreateInstance(step.ElementType, 0));
                        break;
                    case FieldInitKind.NewList:
                        step.Field.SetValue(obj, Activator.CreateInstance(step.Field.FieldType));
                        break;
                    case FieldInitKind.EmptyString:
                        step.Field.SetValue(obj, "");
                        break;
                    case FieldInitKind.NewSerializable:
                        if (step.Field.GetValue(obj) == null)
                            step.Field.SetValue(obj, Activator.CreateInstance(step.Field.FieldType, true));
                        break;
                }
            }
            catch { }
        }

        return obj;
    }

    private static void RegisterInAllData(UniqueIDScriptable obj) => GameRegistry.TryAddToAllData(obj);
}
