using CSFFModFramework.Data;
using CSFFModFramework.Discovery;
using CSFFModFramework.Util;

namespace CSFFModFramework.Injection;

/// <summary>
/// Tag groups (framework 2.28.9): a <c>CardTabGroup</c> filled at load with every card that carries one of
/// the given tags, plus optional explicit extra cards. Declared in <c>TagGroups.json</c> beside a mod's
/// <c>ModInfo.json</c>, so any mod can use one:
/// <code>
/// [
///   { "Name": "GpTag_FW_Oil", "Label": "Oil", "LabelKey": "CSFFMFW_GpTag_FW_Oil_TabName",
///     "Tags": ["tag_Oil"], "ExtraCards": ["39ef9375746c7c34493215126e9888ee"], "ExcludeLiquids": false }
/// ]
/// </code>
/// WHY: a blueprint slot (<c>BlueprintElement</c>) and a liquid check (<c>LiquidContentCondition</c>) can
/// name a card or a <c>CardTabGroup</c>, never a tag. A hand-listed group goes stale the day another mod
/// adds a card with the tag; this one is rebuilt from the tags of every loaded card.
/// <para>
/// ORDER: <see cref="DeclareAll"/> runs BEFORE WarpResolver and registers each group under its name, so a
/// <c>RequiredTabGroupWarpData</c> / <c>RequiredGroupWarpData</c> naming it resolves to this one instance
/// (WarpResolver would otherwise create an empty group of that name). <see cref="FillAll"/> runs after
/// GameSourceModify, when every mod's JSON cards are loaded and their tags resolved, so a card from a mod
/// that loads later than the declaring mod is included. <see cref="ApplyPatch"/> refills once more at run
/// start (a <c>GameManager.Awake</c> prefix) for cards a content mod's own <c>LoadMainGameData</c> postfix
/// registers after the framework.
/// </para>
/// <para>
/// Several mods may declare the same Name: the declarations merge (union of tags and extras; the first
/// non-empty label wins; ExcludeLiquids holds only when every declaration asks for it). A tag matches by
/// NAME, so a duplicate tag instance (root CLAUDE.md, Runtime Card State Caching (2)) still counts.
/// An extra card that does not resolve (a partner mod not installed) is skipped with a Debug line.
/// ExcludeLiquids drops tag-matched CT9 liquids only; an extra card is always included.
/// </para>
/// </summary>
internal static class TagGroupService
{
    internal const string FileName = "TagGroups.json";
    private const int LiquidCardType = 9;

    internal sealed class TagGroupSpec
    {
        public string Name;
        public string Label;
        public string LabelKey;
        public List<string> Tags = new();
        public List<string> ExtraCards = new();
        public bool ExcludeLiquids;
        public List<string> DeclaredBy = new();
    }

    private static readonly Dictionary<string, TagGroupSpec> _specs = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, CardTabGroup> _groups = new(StringComparer.Ordinal);

    // ---------------------------------------------------------------- pure logic (tested without Unity)

    /// <summary>
    /// Parses one <c>TagGroups.json</c> text. Returns the valid entries; every rejected entry adds a line
    /// to <paramref name="problems"/> (never thrown), so one bad row never costs the rest of the file.
    /// </summary>
    internal static List<TagGroupSpec> ParseSpecs(string json, string modName, List<string> problems)
    {
        var result = new List<TagGroupSpec>();
        object parsed;
        try { parsed = MiniJson.Parse(json ?? ""); }
        catch (Exception ex) { problems?.Add($"{modName}: {FileName} is not valid JSON ({ex.Message})"); return result; }
        if (parsed is not List<object> arr)
        {
            problems?.Add($"{modName}: {FileName} root must be a JSON array");
            return result;
        }
        int index = 0;
        foreach (var item in arr)
        {
            if (item is not Dictionary<string, object> d)
            {
                problems?.Add($"{modName}: {FileName} entry {index} is not an object; skipped");
                index++;
                continue;
            }
            var name = d.TryGetValue("Name", out var n) ? n as string : null;
            var spec = new TagGroupSpec
            {
                Name = name,
                Label = d.TryGetValue("Label", out var l) ? l as string : null,
                LabelKey = d.TryGetValue("LabelKey", out var k) ? k as string : null,
                ExcludeLiquids = d.TryGetValue("ExcludeLiquids", out var x) && x is bool b && b,
            };
            spec.Tags.AddRange(Strings(d, "Tags"));
            spec.ExtraCards.AddRange(Strings(d, "ExtraCards"));
            spec.DeclaredBy.Add(modName);
            if (string.IsNullOrEmpty(spec.Name))
                problems?.Add($"{modName}: {FileName} entry {index} has no Name; skipped");
            else if (spec.Tags.Count == 0 && spec.ExtraCards.Count == 0)
                problems?.Add($"{modName}: {FileName} group '{spec.Name}' lists no Tags and no ExtraCards; skipped");
            else
                result.Add(spec);
            index++;
        }
        return result;
    }

    private static IEnumerable<string> Strings(Dictionary<string, object> d, string key)
    {
        if (!d.TryGetValue(key, out var v) || v is not List<object> list) yield break;
        foreach (var o in list)
            if (o is string s && !string.IsNullOrEmpty(s)) yield return s;
    }

    /// <summary>Merges declarations by Name, in the order given (see the class summary for the rules).</summary>
    internal static Dictionary<string, TagGroupSpec> MergeSpecs(IEnumerable<TagGroupSpec> specs)
    {
        var merged = new Dictionary<string, TagGroupSpec>(StringComparer.Ordinal);
        foreach (var s in specs)
        {
            if (s == null || string.IsNullOrEmpty(s.Name)) continue;
            if (!merged.TryGetValue(s.Name, out var m))
            {
                m = new TagGroupSpec { Name = s.Name, ExcludeLiquids = s.ExcludeLiquids };
                merged[s.Name] = m;
            }
            else m.ExcludeLiquids = m.ExcludeLiquids && s.ExcludeLiquids;
            if (string.IsNullOrEmpty(m.Label) && !string.IsNullOrEmpty(s.Label)) { m.Label = s.Label; m.LabelKey = s.LabelKey; }
            foreach (var t in s.Tags) if (!m.Tags.Contains(t)) m.Tags.Add(t);
            foreach (var c in s.ExtraCards) if (!m.ExtraCards.Contains(c)) m.ExtraCards.Add(c);
            foreach (var by in s.DeclaredBy) if (!m.DeclaredBy.Contains(by)) m.DeclaredBy.Add(by);
        }
        return merged;
    }

    /// <summary>
    /// Whether a card belongs in the group: an extra card always does; otherwise it must carry one of the
    /// group's tags (by name) and, when ExcludeLiquids is set, not be a CT9 liquid.
    /// </summary>
    internal static bool IsMember(TagGroupSpec spec, string uid, int cardType, IEnumerable<string> tagNames)
    {
        if (spec == null) return false;
        if (!string.IsNullOrEmpty(uid) && spec.ExtraCards.Contains(uid)) return true;
        if (spec.ExcludeLiquids && cardType == LiquidCardType) return false;
        if (tagNames == null) return false;
        foreach (var t in tagNames)
            if (t != null && spec.Tags.Contains(t)) return true;
        return false;
    }

    /// <summary>
    /// The UIDs of the members among parallel lists of cards (uid, card type, tag names), in input order,
    /// each once. Test seam for <see cref="IsMember"/> over a whole card list.
    /// </summary>
    internal static List<string> SelectMemberUids(TagGroupSpec spec, IList<string> uids, IList<int> cardTypes, IList<string[]> tags)
    {
        var result = new List<string>();
        if (uids == null) return result;
        for (int i = 0; i < uids.Count; i++)
        {
            int ct = cardTypes != null && i < cardTypes.Count ? cardTypes[i] : 0;
            var tg = tags != null && i < tags.Count ? tags[i] : null;
            if (IsMember(spec, uids[i], ct, tg) && !result.Contains(uids[i])) result.Add(uids[i]);
        }
        return result;
    }

    // ---------------------------------------------------------------- engine side

    /// <summary>Reads every mod's TagGroups.json and registers one empty group per declared Name.</summary>
    public static void DeclareAll(List<ModManifest> mods)
    {
        _specs.Clear();
        _groups.Clear();
        var all = new List<TagGroupSpec>();
        var problems = new List<string>();
        foreach (var mod in mods)
        {
            var path = Path.Combine(mod.DirectoryPath, FileName);
            if (!File.Exists(path)) continue;
            try { all.AddRange(ParseSpecs(File.ReadAllText(path), mod.Name, problems)); }
            catch (Exception ex) { problems.Add($"{mod.Name}: error reading {FileName}: {Log.ExceptionText(ex)}"); }
        }
        foreach (var p in problems) Log.Warn("TagGroups: " + p);
        if (all.Count == 0) return;

        foreach (var kv in MergeSpecs(all)) _specs[kv.Key] = kv.Value;
        foreach (var spec in _specs.Values)
        {
            var group = Database.GetTypedSO(typeof(CardTabGroup), spec.Name) as CardTabGroup;
            if (group == null)
            {
                group = ScriptableObject.CreateInstance<CardTabGroup>();
                group.name = spec.Name;
                Database.RegisterTypedSO(typeof(CardTabGroup), spec.Name, group);
            }
            group.IncludedCards ??= new List<CardData>();
            if (!string.IsNullOrEmpty(spec.Label))
                group.TabName = new LocalizedString { LocalizationKey = spec.LabelKey ?? "", DefaultText = spec.Label };
            _groups[spec.Name] = group;
        }
        Log.Debug($"TagGroups: declared {_groups.Count} group(s): {string.Join(", ", _groups.Keys)}");
    }

    /// <summary>Rebuilds every declared group's IncludedCards from the cards loaded now.</summary>
    public static void FillAll(IEnumerable allData, string phase)
    {
        if (_groups.Count == 0) return;
        var cards = new List<CardData>();
        foreach (var o in allData)
            if (o is CardData cd && cd != null) cards.Add(cd);

        var byUid = new Dictionary<string, CardData>(StringComparer.Ordinal);
        foreach (var cd in cards)
            if (!string.IsNullOrEmpty(cd.UniqueID) && !byUid.ContainsKey(cd.UniqueID)) byUid[cd.UniqueID] = cd;

        var summary = new List<string>();
        int unresolved = 0;
        foreach (var kv in _groups)
        {
            var spec = _specs[kv.Key];
            var group = kv.Value;
            if (group == null) continue;
            var members = new List<CardData>();
            foreach (var cd in cards)
            {
                if (members.Contains(cd)) continue;
                if (IsMember(spec, cd.UniqueID, (int)cd.CardType, TagNames(cd))) members.Add(cd);
            }
            foreach (var uid in spec.ExtraCards)
            {
                if (!byUid.TryGetValue(uid, out var extra))
                {
                    Log.Debug($"TagGroups: {spec.Name}: extra card '{uid}' does not resolve (a partner mod not installed?); skipped");
                    unresolved++;
                    continue;
                }
                if (!members.Contains(extra)) members.Add(extra);
            }
            group.IncludedCards ??= new List<CardData>();
            group.IncludedCards.Clear();
            group.IncludedCards.AddRange(members);
            summary.Add($"{spec.Name} {members.Count}");
        }
        Log.Info($"TagGroups ({phase}): {string.Join(", ", summary)} card(s)" + (unresolved > 0 ? $", {unresolved} extra card(s) skipped as unresolved" : ""));
    }

    private static IEnumerable<string> TagNames(CardData cd)
    {
        if (cd.CardTags == null) yield break;
        foreach (var t in cd.CardTags)
            if (t != null) yield return t.name;
    }

    /// <summary>Refill at run start, after content mods' own load postfixes.</summary>
    public static void ApplyPatch(Harmony harmony)
    {
        try
        {
            var awake = AccessTools.Method(typeof(GameManager), "Awake");
            if (awake == null) { Log.Warn("TagGroups: GameManager.Awake not found; groups keep their load-time members"); return; }
            harmony.Patch(awake, prefix: new HarmonyMethod(typeof(TagGroupService), nameof(Awake_Prefix)));
        }
        catch (Exception ex)
        {
            Log.Warn($"TagGroups: failed to patch GameManager.Awake: {Log.ExceptionText(ex)}");
        }
    }

    private static bool _refilledAtRunStart;

    private static void Awake_Prefix()
    {
        // The card graph persists for the session (LoadOrchestrator), so one refill after the content
        // mods' postfixes is enough; a later run start has nothing new to add.
        if (_refilledAtRunStart || _groups.Count == 0) return;
        _refilledAtRunStart = true;
        try { FillAll(Loading.LoadOrchestrator.GetAllData(), "run start"); }
        catch (Exception ex) { Log.Warn($"TagGroups: run-start refill failed: {Log.ExceptionText(ex)}"); }
    }
}
