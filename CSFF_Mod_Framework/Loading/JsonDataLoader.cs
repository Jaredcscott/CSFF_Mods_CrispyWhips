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
    /// All UniqueIDs loaded from mod JSON files.
    /// Reused by ProducedCardService, PassiveEffectNormalizer, AlwaysUpdateService, PerkRelocationService.
    /// </summary>
    internal static HashSet<string> AllModUniqueIds { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Maps UniqueID → mod name. Used by WarpResolver to group unresolved refs by source mod.
    /// </summary>
    internal static Dictionary<string, string> UniqueIdToModName { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static void LoadAll(List<ModManifest> mods)
    {
        JsonByUniqueId.Clear();
        AllModUniqueIds.Clear();
        UniqueIdToModName.Clear();
        ExtraDataStore.Clear();
        int totalLoaded = 0;

        foreach (var mod in mods)
        {
            int modCount = 0;

            // Load from well-known directories
            foreach (var kvp in DirToTypeName)
                modCount += LoadDirectory(mod, kvp.Key, kvp.Value);

            // Load from ScriptableObject/{TypeName}/ directories
            var soDir = Path.Combine(mod.DirectoryPath, "ScriptableObject");
            if (Directory.Exists(soDir))
            {
                foreach (var typeDir in Directory.GetDirectories(soDir))
                {
                    var typeName = Path.GetFileName(typeDir);
                    modCount += LoadJsonFiles(mod, typeDir, typeName, SearchOption.AllDirectories);
                }
            }

            if (modCount > 0)
                Log.Debug($"JsonDataLoader: loaded {modCount} objects from {mod.Name}");
            totalLoaded += modCount;
        }

        Log.Info($"JsonDataLoader: {totalLoaded} total objects loaded"
                 + (ExtraDataStore.Count > 0 ? $", {ExtraDataStore.Count} sidecar extras" : ""));

        // Log batched summary of skipped types at Debug level — these are typically
        // third-party mods from other games (e.g. CSTI) whose types CSFF doesn't resolve.
        // Not an actionable warning for the user.
        foreach (var kvp in _skippedTypes)
            Log.Debug($"JsonDataLoader: [{kvp.Key}] skipped unknown types: {string.Join(", ", kvp.Value)}");
        _skippedTypes.Clear();
    }

    private static int LoadDirectory(ModManifest mod, string subDir, string typeName)
    {
        var dir = Path.Combine(mod.DirectoryPath, subDir);
        if (Directory.Exists(dir))
            return LoadJsonFiles(mod, dir, typeName, SearchOption.AllDirectories);

        // Fallback: some third-party mods (e.g. NPCTracker) nest data under Data/<subDir>/
        var dataDir = Path.Combine(mod.DirectoryPath, "Data", subDir);
        if (Directory.Exists(dataDir))
            return LoadJsonFiles(mod, dataDir, typeName, SearchOption.AllDirectories);

        return 0;
    }

    // Track skipped types per mod for batched summary warning
    private static readonly Dictionary<string, List<string>> _skippedTypes = new();

    private static int LoadJsonFiles(ModManifest mod, string dir, string typeName, SearchOption searchOption)
    {
        int count = 0;
        var type = ReflectionCache.FindType(typeName);
        if (type == null)
        {
            if (!_skippedTypes.TryGetValue(mod.Name, out var list))
            {
                list = new List<string>();
                _skippedTypes[mod.Name] = list;
            }
            if (!list.Contains(typeName)) list.Add(typeName);
            return 0;
        }

        foreach (var file in Directory.GetFiles(dir, "*.json", searchOption))
        {
            // Skip sidecar extras files — they're loaded as a side effect of their content sibling.
            if (file.EndsWith(".extras.json", StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                var json = File.ReadAllText(file);
                var uniqueId = PathUtil.QuickExtractString(json, "UniqueID");

                // Cache raw JSON + UniqueID for downstream services (WarpResolver, SpriteResolver, etc.)
                if (!string.IsNullOrEmpty(uniqueId))
                {
                    if (JsonByUniqueId.ContainsKey(uniqueId))
                        Log.Warn($"JsonDataLoader: duplicate UniqueID '{uniqueId}' in {mod.Name} — second file overwrites first (check for duplicate JSON files)");
                    JsonByUniqueId[uniqueId] = json;
                    AllModUniqueIds.Add(uniqueId);
                    UniqueIdToModName[uniqueId] = mod.Name;

                    // Sidecar extras: Foo.json → Foo.extras.json (optional, free-form JSON
                    // queryable through Api.ExtraData by the content file's UniqueID).
                    var sidecar = Path.ChangeExtension(file, null) + ".extras.json";
                    if (File.Exists(sidecar))
                    {
                        try
                        {
                            var extras = File.ReadAllText(sidecar);
                            ExtraDataStore.Store(uniqueId, extras);
                        }
                        catch (Exception ex)
                        {
                            Log.Warn($"JsonDataLoader: failed to read sidecar {sidecar}: {ex.Message}");
                        }
                    }
                }

                var obj = CreateInstanceSafe(type);
                if (obj == null)
                {
                    Log.Warn($"JsonDataLoader: failed to create instance of {typeName} for {file}");
                    continue;
                }

                JsonUtility.FromJsonOverwrite(json, obj);
                obj.name = Path.GetFileNameWithoutExtension(file);

                if (obj is UniqueIDScriptable uidObj)
                {
                    // Call Init() via reflection
                    var initMethod = ReflectionCache.GetMethod(type, "Init");
                    if (initMethod != null)
                    {
                        try { initMethod.Invoke(uidObj, null); }
                        catch { /* Init may fail before full resolution — that's OK */ }
                    }

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
                        catch { }
                    }

                    // Register in game's AllUniqueObjects (additive — first-wins coexistence
                    // with any other plugin writing into the same registry).
                    GameRegistry.TryRegister(uidObj);

                    // Register in GameLoad.Instance.DataBase.AllData
                    RegisterInAllData(uidObj);
                }

                count++;
            }
            catch (Exception ex)
            {
                Log.Warn($"JsonDataLoader: failed to load {file}: {ex.Message}");
            }
        }

        return count;
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

        foreach (var field in AccessTools.GetDeclaredFields(type))
        {
            try
            {
                if (field.FieldType.IsArray)
                {
                    field.SetValue(obj, Array.CreateInstance(
                        field.FieldType.GetElementType() ?? typeof(object), 0));
                }
                else if (field.FieldType.IsGenericType &&
                         field.FieldType.GetGenericTypeDefinition() == typeof(List<>))
                {
                    field.SetValue(obj, Activator.CreateInstance(field.FieldType));
                }
                else if (field.FieldType == typeof(string))
                {
                    field.SetValue(obj, "");
                }
                // Initialize serializable class fields (LocalizedString, etc.)
                // Skip Unity Object types (Sprite, AudioClip) — resolved later by WarpResolver
                else if (field.FieldType.IsClass
                         && field.FieldType.IsSerializable
                         && !typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType)
                         && field.GetValue(obj) == null)
                {
                    field.SetValue(obj, Activator.CreateInstance(field.FieldType, true));
                }
            }
            catch { }
        }

        return obj;
    }

    private static void RegisterInAllData(UniqueIDScriptable obj) => GameRegistry.TryAddToAllData(obj);
}
