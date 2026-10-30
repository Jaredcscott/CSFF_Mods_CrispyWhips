using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using CSFFModFramework.Util;

namespace CSFFModFramework.Loading;

/// <summary>
/// Load-time authoring lint over the mod JSON <see cref="JsonDataLoader"/> already parsed
/// (<see cref="JsonDataLoader.ParsedJsonByUniqueId"/>, never a disk re-read). Each rule names a
/// shape that deserializes cleanly and then does nothing, with no error and no log line. The
/// predicates are copied from their in-repo Pester twins so the two cannot disagree on this repo's mods
/// (two narrowings for third-party exports are named below):
/// <list type="bullet">
/// <item><c>FlavourTags-IntensityEnum.Tests.ps1</c>: <c>FlavourTags</c> / <c>AddedFlavours</c> <c>Intensity</c> outside {0,1,2}.
/// Narrower than the twin in one way: an ABSENT <c>Intensity</c> reads as 0 (Medium) and works, so it is not reported here.</item>
/// <item><c>DurabilityStat-FieldNames.Tests.ps1</c>: bogus <c>DurabilityStat</c> keys, and a top-level durability block with keys but no <c>Active</c>.
/// Narrower than the twin in one way: the block must author a non-empty value (<see cref="IsAuthored"/>), so a
/// ModEditor export's all-default block for an unused stat is not reported. The twins sweep only this repo's
/// mods, where both exemptions are moot; this lint also reads every third-party mod a player installs.</item>
/// <item><c>BlueprintTransferRules-FieldNames.Tests.ps1</c>: a <c>TransferRules</c> claiming <c>TransferSpecialN</c> that no <c>AffectResultDurabilities</c> mask supplies.</item>
/// <item><c>DropCollection-Schema.Tests.ps1</c>: a flat <c>DroppedOnDestroy</c> / <c>ProducedCards</c> element.</item>
/// </list>
/// One Warn line per affected mod with counts; detail per finding at Debug. A
/// <c>BaseDropChance</c> in (0,1) is legal and usually a 0-100 scale mistake, so it is Debug only.
/// A non-UID ScriptableObject whose name another mod's file already holds is reported through
/// <see cref="RecordNameCollision"/> (fed by <see cref="JsonDataLoader"/>).
/// <para>Foreign-UID references (plan section 19, W0-12): a string in one of the structural
/// <c>*WarpData</c> keys below, with a reference <c>*WarpType</c> (3-6, WarpResolver's own predicate),
/// that <see cref="JsonDataLoader.UniqueIdToModName"/> files under a DIFFERENT mod. Such a reference
/// silently produces nothing when that mod is absent. It is allowed only when a plugin in the
/// referencing mod's folder declares a <c>HardDependency</c> on a plugin in the owner's folder
/// (<see cref="SetModPlugins"/>; the one such pair today is Community_Mod_Chest to
/// AdvancedCopperTools), or when it is a non-inverted card-on-board condition, which reads false while
/// the owner is absent and so fails closed (<see cref="IsFailClosedPresenceGate"/>, plan section 32; an
/// inverted one fails open and is still reported), or when it is a drop in an NPCAgent's own action
/// (<c>AgentActions[].DroppedCards[].DroppedCards[].DroppedCardWarpData</c>, <see cref="IsNpcDropSlot"/>, plan
/// section 32 item 5), the ratified drop-slot variant of INJ (<c>Documentation/Interop_Registry.md</c> amendment (a)):
/// the roll skips a null card, and WarpResolver's per-mod unresolved Info line still names it. A card's or
/// blueprint's own drop or ingredient is not exempt. A UID the framework itself ships (owner
/// <see cref="FrameworkModName"/>, such as the Metal Sheet, Metal Nail and Tin Solder parts under
/// <c>CardData/MetalParts/</c>) is never foreign: every mod depends on the framework, so it is present whenever
/// the referencing mod loads; it is counted in <see cref="FrameworkOwnedRefCount"/> and logs nothing. A value absent from the map is vanilla (or unresolved, which WarpResolver
/// reports), so the lint sees a foreign UID only while its owner is INSTALLED, whereas the harm happens
/// when it is absent. The <c>StatWarpData</c> family is out of scope, and declarative injector files
/// (<c>BlueprintTabs.json</c>, <c>DropInjections.json</c>, ...) never reach this pass because they
/// carry no UniqueID tree. A mod CardTag whose name already named a vanilla CardTag before any mod
/// registered (<see cref="IsVanillaTagName"/>) is counted on the same line
/// (<see cref="RecordVanillaTagName"/>, fed by <see cref="JsonDataLoader"/>).</para>
/// <para>Config <c>[Diagnostics] AuthoringLint</c>, default on: it is one line per affected mod and
/// silent otherwise. Add a rule by appending to <see cref="Rules"/>.</para>
/// </summary>
internal static class AuthoringLint
{
    internal const string RuleFlavourIntensity = "FlavourTags Intensity outside 0/1/2";
    internal const string RuleDurabilityKey = "bogus DurabilityStat key";
    internal const string RuleDurabilityActive = "durability block without Active";
    internal const string RuleTransferRules = "TransferRules claim no mask supplies";
    internal const string RuleFlatDrop = "flat drop collection";
    internal const string RuleNameCollision = "ScriptableObject name taken from another mod";
    internal const string RuleDropChanceFraction = "BaseDropChance below 1";
    internal const string RuleForeignUid = "foreign UID in a structural field";
    internal const string RuleVanillaTagName = "CardTag named like a vanilla tag";

    private static ConfigEntry<bool> _enabled;

    // Structural *WarpData keys whose leaf name alone identifies them (W0-12, plan section 19).
    internal static readonly string[] StructuralLeafKeys =
        { "RequiredCardWarpData", "DroppedCardWarpData", "DroppedCardsWarpData", "TransformIntoWarpData",
          "CommissionsBpWarpData", "TriggerCardsWarpData", "CompatibleCardsWarpData",
          "RequiredCardsOnBoardWarpData", "RequiredCardsNOTOnBoardWarpData", "RequiredCardsInInventoryWarpData",
          "LiquidCardWarpData", "AcceptedCardsWarpData", "DroppedEncounterWarpData", "EncounterNPCWarpData",
          "MoveToEnvironmentWarpData", "FromEnvironmentsWarpData", "ToEnvironmentsWarpData" };

    // Leaf names that are structural only under these parent keys (W0-12: "key on parent path plus leaf").
    internal static readonly Dictionary<string, string[]> StructuralParentKeys = new(StringComparer.Ordinal)
    {
        { "CardWarpData", new[] { "CardsOnBoard", "EnvironmentImprovements", "CardFilters" } },
        { "TriggerCardWarpData", new[] { "RequiredCardsOnBoard", "BlueprintCardConditions", "BuildingCardConditions" } },
        { "ItemWarpData", new[] { "StartingInventory", "AddedCardsInInventory" } },
    };

    /// <summary>One loaded BepInEx plugin, as the lint needs it: where its DLL is, its GUID, and its dependencies.</summary>
    internal sealed class PluginRecord
    {
        internal readonly string Location;
        internal readonly string Guid;
        internal readonly List<(string Guid, bool Hard)> Dependencies;

        internal PluginRecord(string location, string guid, IEnumerable<(string Guid, bool Hard)> dependencies)
        {
            Location = location;
            Guid = guid;
            Dependencies = dependencies?.ToList() ?? new List<(string Guid, bool Hard)>();
        }
    }

    // "referrer\u0001owner" mod-Name pairs a declared HardDependency allows (SetModPlugins).
    private static readonly HashSet<string> _allowedPairs = new(StringComparer.Ordinal);

    // CardTag names that existed as vanilla assets before any mod registered (BeginLoad).
    private static readonly HashSet<string> _vanillaTagNames = new(StringComparer.OrdinalIgnoreCase);

    // Mod CardTags whose name a vanilla CardTag already held, recorded by JsonDataLoader: (mod, name).
    private static readonly List<(string Mod, string Name)> _vanillaTagShadows = new();

    /// <summary>Foreign references the last <see cref="RunAll"/> let through on a declared HardDependency (read by the harness).</summary>
    internal static int AllowedForeignCount { get; private set; }

    /// <summary>Foreign references the last <see cref="RunAll"/> reported.</summary>
    internal static int ReportedForeignCount { get; private set; }

    /// <summary>Foreign references the last <see cref="RunAll"/> let through as a fail-closed presence gate (<see cref="IsFailClosedPresenceGate"/>).</summary>
    internal static int FailClosedForeignCount { get; private set; }

    /// <summary>Foreign references the last <see cref="RunAll"/> let through as an NPC drop slot (<see cref="IsNpcDropSlot"/>).</summary>
    internal static int NpcDropSlotForeignCount { get; private set; }

    /// <summary>References the last <see cref="RunAll"/> skipped because the framework itself owns the UID.</summary>
    internal static int FrameworkOwnedRefCount { get; private set; }

    /// <summary>
    /// The mod Name <see cref="JsonDataLoader.UniqueIdToModName"/> files framework-shipped UIDs under. Must equal
    /// <c>ModDiscovery.FrameworkManifestName</c> (a literal here so the lint compiles alone in its test harness;
    /// <c>Framework-AuthoringLint.Tests.ps1</c> asserts the two match).
    /// </summary>
    internal const string FrameworkModName = "CSFFModFramework";

    // Keys that do not exist on DurabilityStat (DurabilityStat-FieldNames.Tests.ps1 $BogusKeys).
    private static readonly string[] BogusDurabilityKeys =
        { "DurabilityName", "MaxDurability", "StartDurability", "Restricted", "IsHidden", "ShowAsBar" };

    // The eight DurabilityStat fields on CardData (same twin, $DurabilityKeys).
    private static readonly string[] DurabilityKeys =
        { "SpoilageTime", "UsageDurability", "FuelCapacity", "Progress",
          "SpecialDurability1", "SpecialDurability2", "SpecialDurability3", "SpecialDurability4" };

    // DurabilitiesTypes bit per Special index (.decomp/DurabilitiesTypes.cs).
    private static readonly int[] SpecialBits = { 0x20, 0x40, 0x80, 0x100 };

    // Collisions recorded by JsonDataLoader during the current load: (mod, rule detail).
    private static readonly List<(string Mod, string Detail)> _collisions = new();

    internal sealed class Findings
    {
        private readonly Dictionary<string, Dictionary<string, int>> _counts = new(StringComparer.Ordinal);
        private readonly List<string> _debug = new();

        internal void Add(string mod, string rule, string detail)
        {
            mod ??= "(unknown mod)";
            if (!_counts.TryGetValue(mod, out var byRule))
                _counts[mod] = byRule = new Dictionary<string, int>(StringComparer.Ordinal);
            byRule.TryGetValue(rule, out var n);
            byRule[rule] = n + 1;
            _debug.Add($"{mod}: {rule}: {detail}");
        }

        internal IReadOnlyDictionary<string, Dictionary<string, int>> Counts => _counts;
        internal IReadOnlyList<string> Detail => _debug;
    }

    /// <summary>One rule: inspects one parsed mod JSON tree and reports through the sink.</summary>
    internal delegate void Rule(string uid, string mod, Dictionary<string, object> tree, Findings sink);

    /// <summary>The per-tree rules, run in order over every parsed file. A new rule is one more entry.</summary>
    internal static readonly List<Rule> Rules = new()
    {
        FlavourIntensityRule,
        DurabilityRule,
        TransferRulesRule,
        DropShapeRule,
        ForeignUidRule,
    };

    internal static void Configure(ConfigFile config)
    {
        _enabled = config.Bind("Diagnostics", "AuthoringLint", true,
            "Scan the loaded mod JSON once at startup for shapes that load cleanly and then do nothing "
            + "(an out-of-range flavour Intensity, a bogus durability key, a flat drop list, another mod's "
            + "UniqueID in a structural field with no hard dependency on that mod, ...) and log "
            + "one Warn line per affected mod. Also warns once per field that two mods' GameSourceModify patches "
            + "both replace, and per mod CardTabGroup file that matches a vanilla group. Silent when nothing is "
            + "found; per-finding detail is Debug.",
            "启动时扫描一次已加载的模组 JSON，查找能正常加载却不起作用的写法（超出范围的风味强度、无效的耐久度键、扁平的掉落列表、在结构字段中引用了未设为硬依赖的其他模组的 UniqueID 等），每个受影响的模组记录一条警告。另外，两个模组的 GameSourceModify 补丁替换同一字段时，每个字段警告一次；模组的 CardTabGroup 文件与原版分组同名或 ParentObjectID 相同时，每个文件警告一次。没有发现问题时不输出；每项发现的详情为 Debug 级别。");
    }

    /// <summary>Called by <see cref="JsonDataLoader"/> when a non-UID ScriptableObject name another mod's file holds is replaced.</summary>
    internal static void RecordNameCollision(string mod, string previousMod, string typeName, string name)
        => _collisions.Add((mod, $"'{name}' ({typeName}) replaces {previousMod}'s object of that name"));

    /// <summary>Called by <see cref="JsonDataLoader"/> when a mod CardTag takes a name a vanilla CardTag held.</summary>
    internal static void RecordVanillaTagName(string mod, string name) => _vanillaTagShadows.Add((mod, name));

    /// <summary>
    /// Starts a load. <paramref name="vanillaCardTagNames"/> are the CardTag names present as game assets
    /// before any mod object registered (JsonDataLoader reads them from the typed cache at the start of
    /// its pass); null keeps the set empty.
    /// </summary>
    internal static void BeginLoad(IEnumerable<string> vanillaCardTagNames = null)
    {
        _collisions.Clear();
        _vanillaTagShadows.Clear();
        _vanillaTagNames.Clear();
        if (vanillaCardTagNames != null)
            foreach (var n in vanillaCardTagNames)
                if (!string.IsNullOrEmpty(n)) _vanillaTagNames.Add(n);
    }

    /// <summary>True when <paramref name="name"/> named a vanilla CardTag before any mod registered (case-insensitive, as the typed cache is).</summary>
    internal static bool IsVanillaTagName(string name) => !string.IsNullOrEmpty(name) && _vanillaTagNames.Contains(name);

    /// <summary>
    /// Records which mod-to-mod references a declared <c>HardDependency</c> allows. <paramref name="mods"/> are
    /// (mod Name as <see cref="JsonDataLoader.UniqueIdToModName"/> stores it, mod folder); a plugin belongs to a
    /// folder when its DLL path starts with that folder plus a separator (a PREFIX match: ModCore's DLL sits two
    /// folders down). Mod R may reference mod O when any plugin of R's folder lists, as a hard dependency, the
    /// GUID of any plugin of O's folder. A folder with no plugin (a data-only mod) declares nothing.
    /// </summary>
    internal static void SetModPlugins(IEnumerable<(string Name, string Dir)> mods, IEnumerable<PluginRecord> plugins)
    {
        _allowedPairs.Clear();
        var modList = (mods ?? Enumerable.Empty<(string Name, string Dir)>())
            .Where(m => !string.IsNullOrEmpty(m.Name) && !string.IsNullOrEmpty(m.Dir)).ToList();
        var pluginList = (plugins ?? Enumerable.Empty<PluginRecord>()).Where(p => p != null && !string.IsNullOrEmpty(p.Location)).ToList();

        var guidsByMod = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var hardDepsByMod = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var m in modList)
        {
            var prefix = NormalizeDir(m.Dir) + "\\";
            if (!guidsByMod.TryGetValue(m.Name, out var guids)) guidsByMod[m.Name] = guids = new HashSet<string>(StringComparer.Ordinal);
            if (!hardDepsByMod.TryGetValue(m.Name, out var hard)) hardDepsByMod[m.Name] = hard = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in pluginList)
            {
                if (!NormalizeDir(p.Location).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrEmpty(p.Guid)) guids.Add(p.Guid);
                foreach (var d in p.Dependencies)
                    if (d.Hard && !string.IsNullOrEmpty(d.Guid)) hard.Add(d.Guid);
            }
        }
        foreach (var r in hardDepsByMod)
            foreach (var o in guidsByMod)
                if (!string.Equals(r.Key, o.Key, StringComparison.Ordinal) && o.Value.Overlaps(r.Value))
                    _allowedPairs.Add(r.Key + "\u0001" + o.Key);
    }

    /// <summary>True when a declared HardDependency lets <paramref name="mod"/> reference <paramref name="owner"/>'s UIDs.</summary>
    internal static bool IsForeignAllowed(string mod, string owner) => _allowedPairs.Contains(mod + "\u0001" + owner);

    private static string NormalizeDir(string p) => p.Replace('/', '\\').TrimEnd('\\');

    internal static bool IsEnabled => _enabled == null || _enabled.Value;

    internal static void RunAll()
    {
        if (!IsEnabled) { Log.Debug("[Skip] AuthoringLint: disabled by [Diagnostics] AuthoringLint"); return; }

        AllowedForeignCount = 0;
        ReportedForeignCount = 0;
        FailClosedForeignCount = 0;
        NpcDropSlotForeignCount = 0;
        FrameworkOwnedRefCount = 0;
        var findings = new Findings();
        foreach (var kvp in JsonDataLoader.ParsedJsonByUniqueId)
        {
            if (kvp.Value == null) continue;
            JsonDataLoader.UniqueIdToModName.TryGetValue(kvp.Key, out var mod);
            foreach (var rule in Rules)
            {
                try { rule(kvp.Key, mod, kvp.Value, findings); }
                catch (Exception ex) { Log.Debug($"AuthoringLint: rule failed on '{kvp.Key}': {ex.GetType().Name} {ex.Message}"); }
            }
        }
        foreach (var c in _collisions)
            findings.Add(c.Mod, RuleNameCollision, c.Detail);
        foreach (var v in _vanillaTagShadows)
            findings.Add(v.Mod, RuleVanillaTagName, $"'{v.Name}' (CardTag) shadows the vanilla tag: a mod reference by that name gets this copy, which vanilla's checks for the original tag never match");
        if (AllowedForeignCount > 0)
            Log.Debug($"AuthoringLint: {AllowedForeignCount} foreign UID reference(s) allowed by a declared HardDependency");
        if (FailClosedForeignCount > 0)
            Log.Debug($"AuthoringLint: {FailClosedForeignCount} foreign UID reference(s) allowed as a non-inverted card-on-board condition (fails closed when the owner is absent)");
        if (NpcDropSlotForeignCount > 0)
            Log.Debug($"AuthoringLint: {NpcDropSlotForeignCount} foreign UID reference(s) allowed in an NPC agent's drop slot (the roll skips a missing card; WarpResolver's unresolved line names it)");

        Report(findings);
    }

    internal static void Report(Findings findings)
    {
        foreach (var d in findings.Detail) Log.Debug("AuthoringLint: " + d);
        foreach (var kvp in findings.Counts.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            // BaseDropChance below 1 is Debug only: it never makes a Warn line on its own.
            var warn = kvp.Value.Where(r => r.Key != RuleDropChanceFraction).ToList();
            if (warn.Count == 0) continue;
            int total = warn.Sum(r => r.Value);
            Log.Warn($"AuthoringLint: {kvp.Key} has {total} authoring shape(s) that load but do nothing: "
                   + string.Join("; ", warn.Select(r => $"{r.Value} x {r.Key}"))
                   + " (detail at Debug with VerboseLogging; silence with [Diagnostics] AuthoringLint = false)");
        }
    }

    // ── Rules ─────────────────────────────────────────────────────────────────

    private static void FlavourIntensityRule(string uid, string mod, Dictionary<string, object> tree, Findings sink)
    {
        foreach (var arrayName in new[] { "FlavourTags", "AddedFlavours" })
        {
            if (!tree.TryGetValue(arrayName, out var arr) || arr is not List<object> list) continue;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is not Dictionary<string, object> entry) continue;
                // An ABSENT Intensity is not this defect: FlavourAndIntensitySetup is a struct, so the
                // field deserializes to 0 = Medium, a working value (a ModEditor export omits it). The
                // in-repo twin still flags absence as authoring hygiene; this Warn claims "does nothing".
                if (!entry.TryGetValue("Intensity", out var v)) continue;
                if (v is double d && (d == 0 || d == 1 || d == 2)) continue;
                sink.Add(mod, RuleFlavourIntensity, $"{uid} {arrayName}[{i}] Intensity={(v == null ? "null" : v)}");
            }
        }
    }

    private static void DurabilityRule(string uid, string mod, Dictionary<string, object> tree, Findings sink)
    {
        // Bogus keys anywhere in the card (the twin matches the raw text for "Key":).
        var seen = new HashSet<string>(StringComparer.Ordinal);
        WalkKeys(tree, key => { if (Array.IndexOf(BogusDurabilityKeys, key) >= 0) seen.Add(key); });
        foreach (var key in seen)
            sink.Add(mod, RuleDurabilityKey, $"{uid} \"{key}\"");

        // A TOP-LEVEL durability block that authors a value but no Active of its own. Nested Active
        // (MinRate/MaxRate carry their own) does not count; keys compare case-sensitively. A key whose
        // value is empty (0, false, "", [], or an object holding only such values) authors nothing:
        // the Pikachu ModEditor writes every unused stat as such a block and never writes Active (it is
        // a protected field), so counting keys alone filled a Warn line with stats nobody meant to use
        // (382 blocks across five Duros mods, 332 of them all-empty, live boot 2026-10-04).
        foreach (var name in DurabilityKeys)
        {
            if (!tree.TryGetValue(name, out var block) || block is not Dictionary<string, object> d) continue;
            bool hasActive = d.ContainsKey("Active");
            bool hasOther = d.Any(kv => !string.Equals(kv.Key, "Active", StringComparison.Ordinal) && IsAuthored(kv.Value));
            if (hasOther && !hasActive)
                sink.Add(mod, RuleDurabilityActive, $"{uid} {name}");
        }
    }

    private static void TransferRulesRule(string uid, string mod, Dictionary<string, object> tree, Findings sink)
    {
        if (!(tree.TryGetValue("CardType", out var ct) && ct is double cd && cd == 7)) return;

        var claimed = new SortedSet<int>();
        if (tree.TryGetValue("BlueprintResult", out var res) && res is List<object> results)
            foreach (var r in results)
            {
                if (r is not Dictionary<string, object> rd) continue;
                if (!rd.TryGetValue("TransferRules", out var trObj) || trObj is not Dictionary<string, object> tr) continue;
                for (int i = 1; i <= 4; i++)
                    if (tr.TryGetValue("TransferSpecial" + i, out var flag) && flag is bool b && b) claimed.Add(i);
            }
        if (claimed.Count == 0) return;

        var masked = new HashSet<int>();
        if (tree.TryGetValue("BlueprintStages", out var st) && st is List<object> stages)
            foreach (var s in stages)
            {
                if (s is not Dictionary<string, object> sd) continue;
                if (!sd.TryGetValue("RequiredElements", out var re) || re is not List<object> elems) continue;
                foreach (var e in elems)
                {
                    if (e is not Dictionary<string, object> ed) continue;
                    if (!ed.TryGetValue("AffectResultDurabilities", out var ard) || ard is not Dictionary<string, object> ad) continue;
                    if (!ad.TryGetValue("MaskValue", out var mv) || mv is not double mask) continue;
                    for (int i = 1; i <= 4; i++)
                        if (((int)mask & SpecialBits[i - 1]) != 0) masked.Add(i);
                }
            }

        foreach (var i in claimed)
            if (!masked.Contains(i))
                sink.Add(mod, RuleTransferRules, $"{uid} TransferRules.TransferSpecial{i} (a TransferToDrop has no bool; no AffectResultDurabilities mask supplies Special{i})");
    }

    private static void DropShapeRule(string uid, string mod, Dictionary<string, object> tree, Findings sink)
    {
        WalkProperties(tree, "", (path, name, value) =>
        {
            if ((name == "DroppedOnDestroy" || name == "ProducedCards") && value is List<object> list
                && list.Any(IsFlatDropElement))
                sink.Add(mod, RuleFlatDrop, $"{uid} {path}{name}");
            else if (name == "BaseDropChance" && value is double chance && chance > 0 && chance < 1)
                sink.Add(mod, RuleDropChanceFraction, $"{uid} {path}{name}={chance} (a 0-100 percent scale, so this is under 1%)");
        });
    }

    // Another mod's UniqueID in a structural field, with no declared HardDependency on that mod. Reads the
    // reference and never rewrites it (plan guardrails).
    private static void ForeignUidRule(string uid, string mod, Dictionary<string, object> tree, Findings sink)
    {
        if (string.IsNullOrEmpty(mod)) return;
        WalkStructuralRefs(tree, "", "", (path, key, value, holder) =>
        {
            if (!JsonDataLoader.UniqueIdToModName.TryGetValue(value, out var owner) || string.IsNullOrEmpty(owner)) return; // vanilla or unresolved
            if (string.Equals(owner, mod, StringComparison.Ordinal)) return; // own
            // The framework's own UIDs are present whenever any mod loads (every mod depends on it).
            if (string.Equals(owner, FrameworkModName, StringComparison.Ordinal)) { FrameworkOwnedRefCount++; return; }
            if (IsForeignAllowed(mod, owner)) { AllowedForeignCount++; return; }
            if (IsFailClosedPresenceGate(key, holder))
            {
                FailClosedForeignCount++;
                Log.Debug($"AuthoringLint: {uid} {path}{key}='{value}' is {owner}'s, allowed: a non-inverted card-on-board condition reads false while {owner} is absent");
                return;
            }
            if (IsNpcDropSlot(key, path))
            {
                NpcDropSlotForeignCount++;
                Log.Debug($"AuthoringLint: {uid} {path}{key}='{value}' is {owner}'s, allowed: an NPC agent's drop slot skips a missing card (Interop_Registry amendment (a))");
                return;
            }
            ReportedForeignCount++;
            sink.Add(mod, RuleForeignUid, $"{uid} {path}{key}='{value}' is {owner}'s, and no plugin here declares a HardDependency on it (nothing happens when {owner} is absent)");
        });
    }

    // TriggerCardWarpData is structural only under RequiredCardsOnBoard, BlueprintCardConditions and
    // BuildingCardConditions (StructuralParentKeys), all CardOnBoardCondition[] (.decomp/CardOnBoardCondition.cs,
    // .decomp/CardData.cs). CardAction's RequiredCardsOnBoard check (.decomp/CardAction.cs, the !Inverted
    // branch) requires the card to be found, and every lookup it calls (CardIsOnBoard, CardIsInHand,
    // CardIsInEquipment, CardIsInBase/InLocation) matches by CardModel == _Card, which a null card never
    // does, so an absent owner leaves the action closed: the presence gate the cross-mod doctrine asks for.
    // Inverted true reverses it (the card must be ABSENT), so a null card fails OPEN and stays flagged.
    // Decided by the condition's own Inverted field; absent reads false (a bool's default).
    internal static bool IsFailClosedPresenceGate(string key, Dictionary<string, object> holder)
    {
        if (!string.Equals(key, "TriggerCardWarpData", StringComparison.Ordinal) || holder == null) return false;
        return !(holder.TryGetValue("Inverted", out var inv) && inv is bool b && b);
    }

    // An NPCAgent's AgentActions are NPCAction[] (.decomp/NPCAgent.cs), and an NPCAction's drops are
    // CardsDropCollection[] DroppedCards (.decomp/NPCAction.cs), each holding CardDrop[] DroppedCards. When the
    // action runs, NPCAction.ToAction hands that array to a CardAction as its ProducedCards, and the roll skips a
    // CardDrop whose DroppedCard is null (.decomp/CardsDropCollection.cs) with no exception, so an absent partner
    // just drops nothing from that row: the drop-slot variant of INJ (Interop_Registry.md amendment (a)). The path
    // is anchored at the root: AgentActions is a top-level field of NPCAgent only, so a card's or blueprint's
    // ProducedCards / DroppedOnDestroy / PassiveEffects[].DroppedCards and a dialog's DroppedCards never match.
    // NPCDuty's ComplexDutyAction also holds an NPCAction, but it is a non-UID ScriptableObject that never reaches
    // this pass (ParsedJsonByUniqueId holds UID trees only).
    internal static bool IsNpcDropSlot(string key, string path)
    {
        if (!string.Equals(key, "DroppedCardWarpData", StringComparison.Ordinal) || path == null) return false;
        var parts = path.TrimEnd('.').Split('.');
        return parts.Length == 3
            && IsIndexed(parts[0], "AgentActions") && IsIndexed(parts[1], "DroppedCards") && IsIndexed(parts[2], "DroppedCards");
    }

    // "Name[n]" with a non-empty run of digits for n.
    private static bool IsIndexed(string segment, string name)
    {
        if (segment.Length < name.Length + 3 || !segment.StartsWith(name + "[", StringComparison.Ordinal) || segment[segment.Length - 1] != ']') return false;
        for (int i = name.Length + 1; i < segment.Length - 1; i++)
            if (segment[i] < '0' || segment[i] > '9') return false;
        return true;
    }

    internal static bool IsStructuralKey(string key, string parentKey)
    {
        if (Array.IndexOf(StructuralLeafKeys, key) >= 0) return true;
        return StructuralParentKeys.TryGetValue(key, out var parents) && Array.IndexOf(parents, parentKey) >= 0;
    }

    // WarpResolver acts only on a *WarpData whose sibling *WarpType is 3-6 (WarpResolver.cs, the Walk predicate).
    private static bool HasReferenceWarpType(Dictionary<string, object> owner, string warpDataKey)
    {
        var typeKey = warpDataKey.Substring(0, warpDataKey.Length - "Data".Length) + "Type";
        if (!owner.TryGetValue(typeKey, out var t)) return false;
        double n;
        switch (t)
        {
            case double d: n = d; break;
            case long l: n = l; break;
            case int i: n = i; break;
            default: return false;
        }
        return n >= 3 && n <= 6;
    }

    // Visits (path, key, value) for every non-empty string in a structural *WarpData with a reference type.
    // parentKey is the key holding the current object (for an array element, the key holding the array).
    private static void WalkStructuralRefs(object node, string parentKey, string path, Action<string, string, string, Dictionary<string, object>> visit)
    {
        if (node is Dictionary<string, object> d)
        {
            foreach (var kvp in d)
            {
                if (kvp.Key.EndsWith("WarpData", StringComparison.Ordinal)
                    && IsStructuralKey(kvp.Key, parentKey) && HasReferenceWarpType(d, kvp.Key))
                {
                    if (kvp.Value is string s && s.Length > 0) visit(path, kvp.Key, s, d);
                    else if (kvp.Value is List<object> values)
                        foreach (var v in values)
                            if (v is string sv && sv.Length > 0) visit(path, kvp.Key, sv, d);
                }
                WalkStructuralRefs(kvp.Value, kvp.Key, path + kvp.Key + ".", visit);
            }
        }
        else if (node is List<object> l)
        {
            for (int i = 0; i < l.Count; i++)
                WalkStructuralRefs(l[i], parentKey, path.TrimEnd('.') + "[" + i + "].", visit);
        }
    }

    // Nested (correct) when it carries any collection key; flat (silently broken) when it carries
    // inner-drop keys and no collection key. Copied from Test-IsFlatDropElement.
    private static bool IsFlatDropElement(object element)
    {
        if (element is not Dictionary<string, object> d || d.Count == 0) return false;
        if (d.ContainsKey("CollectionName") || d.ContainsKey("CollectionWeight") || d.ContainsKey("DroppedCards")) return false;
        return d.ContainsKey("DroppedCard") || d.ContainsKey("DroppedCardWarpData")
            || d.ContainsKey("DroppedCardWarpType") || d.ContainsKey("Quantity");
    }

    // True when a parsed JSON value carries something an author wrote: a non-zero number, true, a
    // non-empty string or list, or an object holding any such value. A serializer's empty default
    // (0, false, "", [], {"m_FileID":0,"m_PathID":0}) is not authored.
    internal static bool IsAuthored(object value)
    {
        switch (value)
        {
            case null: return false;
            case bool b: return b;
            case double n: return n != 0;
            case long l: return l != 0;
            case int i: return i != 0;
            case string s: return s.Length > 0;
            case List<object> list: return list.Count > 0;
            case Dictionary<string, object> d: return d.Values.Any(IsAuthored);
            default: return true;
        }
    }

    // ── Walkers (shared by rules) ─────────────────────────────────────────────

    private static void WalkKeys(object node, Action<string> onKey)
    {
        if (node is Dictionary<string, object> d)
        {
            foreach (var kvp in d) { onKey(kvp.Key); WalkKeys(kvp.Value, onKey); }
        }
        else if (node is List<object> l)
        {
            foreach (var item in l) WalkKeys(item, onKey);
        }
    }

    private static void WalkProperties(object node, string path, Action<string, string, object> visit)
    {
        if (node is Dictionary<string, object> d)
        {
            foreach (var kvp in d)
            {
                visit(path, kvp.Key, kvp.Value);
                WalkProperties(kvp.Value, path + kvp.Key + ".", visit);
            }
        }
        else if (node is List<object> l)
        {
            for (int i = 0; i < l.Count; i++)
                WalkProperties(l[i], path.TrimEnd('.') + "[" + i + "].", visit);
        }
    }
}
