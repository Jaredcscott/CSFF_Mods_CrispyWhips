using System;
using System.Collections.Generic;
using System.Linq;
using CSFFModFramework.Util;

namespace CSFFModFramework.Loading;

/// <summary>
/// Load-time detector for two ways one mod silently undoes vanilla's or another mod's content
/// (fleet plan section 6.4). It sits beside <see cref="AuthoringLint"/>, shares its
/// <c>[Diagnostics] AuthoringLint</c> switch (every caller checks <see cref="AuthoringLint.IsEnabled"/>),
/// reads only JSON trees its callers already parsed, and never touches the disk or a Unity type, so the
/// gate compiles it alone.
/// <list type="number">
/// <item><b>Two mods replacing one field.</b> <c>GameSourceModifier.ApplyPatch</c> hands every patch it
/// applies to <see cref="RecordPatch"/> with the object it landed on (a bulk <c>MatchTagWarpData</c> /
/// <c>MatchTypeWarpData</c> patch once per matched card). <see cref="ReplacedFields"/> names the top-level
/// fields that patch REPLACES rather than appends to; when two or more mods replace the same field of
/// the same object, <see cref="ReportConflicts"/> warns once for that object and field, naming every mod.</item>
/// <item><b>A CardTabGroup file that matches a vanilla group.</b> <c>JsonDataLoader</c> hands every mod
/// <c>ScriptableObject/CardTabGroup/</c> file to <see cref="RecordCardTabGroup"/>; one whose name or
/// <c>TabName.ParentObjectID</c> matches a vanilla group (<see cref="BeginTabGroups"/>, read from the
/// typed cache before any mod object registers) gets one Warn from <see cref="ReportTabGroups"/>.</item>
/// </list>
/// Each Warn carries one hint: <see cref="AppendHint"/> or <see cref="TabGroupHint"/>.
/// </summary>
internal static class OverrideConflictLint
{
    /// <summary>The hint every replace-conflict Warn ends with.</summary>
    internal const string AppendHint =
        "Hint: append with \"_appendArrays\" (or a *WarpData with WarpType 4) instead of replacing the field.";

    /// <summary>The hint every CardTabGroup Warn ends with.</summary>
    internal const string TabGroupHint =
        "Hint: declare the group in TagGroups.json, or place blueprints with BlueprintTabs.json, instead of shipping CardTabGroup files.";

    /// <summary>Replace conflicts past this many go to Debug, with one Warn counting them.</summary>
    internal const int MaxConflictWarns = 25;

    /// <summary>One object and field two or more mods replace.</summary>
    internal sealed class Conflict
    {
        internal string ObjectKey;
        internal string ObjectLabel;
        internal string Field;
        /// <summary>Each replacing mod once, in the order their patches applied, with its first file.</summary>
        internal List<(string Mod, string File)> By;
    }

    /// <summary>A mod CardTabGroup file that matches a vanilla group.</summary>
    internal sealed class TabGroupFinding
    {
        internal string Mod;
        internal string Name;
        internal string ParentObjectId;
        /// <summary>True when the file's NAME is a vanilla group's; false when only its ParentObjectID matched.</summary>
        internal bool ByName;
        internal string VanillaGroup;
    }

    private sealed class Claim
    {
        internal string ObjectKey;
        internal string ObjectLabel;
        internal string Field;
        internal readonly List<(string Mod, string File)> By = new();
    }

    // "object\u0001field" -> who replaced it. Object keys compare case-insensitively (UIDs do in the
    // registry), field names case-sensitively (they are C# field names).
    private static readonly Dictionary<string, Claim> _claims = new(StringComparer.Ordinal);
    private static readonly List<Claim> _claimOrder = new();

    private static readonly Dictionary<string, string> _vanillaGroupNames = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> _vanillaGroupByParentId = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<TabGroupFinding> _tabGroupFindings = new();

    // ── Field semantics ───────────────────────────────────────────────────────

    /// <summary>
    /// The top-level fields a <c>GameSourceModify</c> patch REPLACES, in file order, each once:
    /// <list type="bullet">
    /// <item>a plain key: <c>JsonUtility.FromJsonOverwrite</c> overwrites that field (a list is replaced whole);</item>
    /// <item><c>XWarpData</c> with WarpType 3 (Reference: sets the field) or 5 (Modify: rewrites existing
    /// elements in place, or replaces the list for string entries), counted as field <c>X</c> when it carries a value.</item>
    /// </list>
    /// Not replaces: <c>_appendArrays</c> and WarpType 4 or 6 (both append), a WarpType WarpResolver skips
    /// (absent or outside 3-6), the target selector <c>UniqueID</c>, the bulk-match selectors
    /// <c>MatchTagWarpData</c> / <c>MatchTypeWarpData</c>, every <c>*WarpType</c> key, and a key that cannot
    /// name a field (a comment key such as <c>"//note"</c>).
    /// </summary>
    internal static List<string> ReplacedFields(Dictionary<string, object> patch)
    {
        var fields = new List<string>();
        if (patch == null) return fields;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kvp in patch)
        {
            var key = kvp.Key;
            if (string.IsNullOrEmpty(key)) continue;
            if (key == "UniqueID" || key == "_appendArrays") continue;
            if (key.EndsWith("WarpType", StringComparison.Ordinal)) continue;

            string field;
            if (key.EndsWith("WarpData", StringComparison.Ordinal))
            {
                if (key == "MatchTagWarpData" || key == "MatchTypeWarpData") continue;
                int warpType = WarpTypeOf(patch, key);
                if (warpType != 3 && warpType != 5) continue;
                if (!CarriesValue(kvp.Value)) continue;
                field = key.Substring(0, key.Length - "WarpData".Length);
            }
            else field = key;

            if (IsIdentifier(field) && seen.Add(field)) fields.Add(field);
        }
        return fields;
    }

    // A key that can name a C# field: letters, digits and underscores, not starting with a digit. A
    // comment key such as "//AxeRusty" (Forest Lore, Sirus23) names no field and is ignored by
    // FromJsonOverwrite, so it replaces nothing.
    private static bool IsIdentifier(string s)
    {
        if (string.IsNullOrEmpty(s) || char.IsDigit(s[0])) return false;
        foreach (var c in s)
            if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
        return true;
    }

    // The sibling *WarpType as an int, or 0 when absent or not a number (WarpResolver.Walk's own reading).
    private static int WarpTypeOf(Dictionary<string, object> owner, string warpDataKey)
    {
        var typeKey = warpDataKey.Substring(0, warpDataKey.Length - "Data".Length) + "Type";
        if (!owner.TryGetValue(typeKey, out var t)) return 0;
        switch (t)
        {
            case double d: return (int)d;
            case long l: return (int)l;
            case int i: return i;
            default: return 0;
        }
    }

    // A *WarpData value WarpResolver acts on: a non-empty string, or a list holding a non-empty string
    // or a non-empty object (an empty {} skips that element).
    private static bool CarriesValue(object value)
    {
        if (value is string s) return s.Length > 0;
        if (value is List<object> list)
            foreach (var e in list)
            {
                if (e is string es && es.Length > 0) return true;
                if (e is Dictionary<string, object> ed && ed.Count > 0) return true;
            }
        return false;
    }

    // ── Replace conflicts (GameSourceModify) ──────────────────────────────────

    /// <summary>Starts a GameSourceModify pass: forgets every recorded replace.</summary>
    internal static void BeginPatches()
    {
        _claims.Clear();
        _claimOrder.Clear();
    }

    /// <summary>
    /// Records one applied patch: <paramref name="mod"/>'s <paramref name="file"/> landed on the object
    /// <paramref name="objectKey"/> (its UniqueID, or its name for a non-UID object), shown as
    /// <paramref name="objectLabel"/>. Call in the order patches apply.
    /// </summary>
    internal static void RecordPatch(string mod, string objectKey, string objectLabel, string file, Dictionary<string, object> patch)
    {
        if (string.IsNullOrEmpty(objectKey) || patch == null) return;
        mod ??= "(unknown mod)";
        var lowered = objectKey.ToLowerInvariant();
        foreach (var field in ReplacedFields(patch))
        {
            var key = lowered + "\u0001" + field;
            if (!_claims.TryGetValue(key, out var claim))
            {
                claim = new Claim { ObjectKey = objectKey, ObjectLabel = string.IsNullOrEmpty(objectLabel) ? objectKey : objectLabel, Field = field };
                _claims[key] = claim;
                _claimOrder.Add(claim);
            }
            if (!claim.By.Any(b => string.Equals(b.Mod, mod, StringComparison.Ordinal)))
                claim.By.Add((mod, file ?? ""));
        }
    }

    /// <summary>Every (object, field) the recorded patches replace, in first-record order, with who replaced it.</summary>
    internal static List<Conflict> Replaces()
        => _claimOrder.Select(c => new Conflict { ObjectKey = c.ObjectKey, ObjectLabel = c.ObjectLabel, Field = c.Field, By = c.By.ToList() }).ToList();

    /// <summary>The recorded replaces two or more mods share.</summary>
    internal static List<Conflict> Conflicts() => Replaces().Where(c => c.By.Count >= 2).ToList();

    /// <summary>The Warn text for one conflict.</summary>
    internal static string FormatConflict(Conflict c)
    {
        var mods = c.By.Select(b => b.Mod).ToList();
        string who = mods.Count == 2
            ? $"{mods[0]} and {mods[1]} both"
            : string.Join(", ", mods.Take(mods.Count - 1)) + " and " + mods[mods.Count - 1] + " all";
        string files = string.Join(", ", c.By.Select(b => $"{b.Mod}'s {b.File}"));
        return $"OverrideConflictLint: {who} replace '{c.Field}' on {c.ObjectLabel} ({files}; applied in that order), "
             + "so one mod's change is lost or overwritten. " + AppendHint;
    }

    /// <summary>One Warn per conflicting object and field (the first <see cref="MaxConflictWarns"/>; the rest at Debug).</summary>
    internal static void ReportConflicts()
    {
        var conflicts = Conflicts();
        int shown = 0;
        foreach (var c in conflicts)
        {
            if (shown < MaxConflictWarns) { Log.Warn(FormatConflict(c)); shown++; }
            else Log.Debug(FormatConflict(c));
        }
        if (conflicts.Count > shown)
            Log.Warn($"OverrideConflictLint: {conflicts.Count - shown} more replace conflict(s) (detail at Debug with VerboseLogging; silence with [Diagnostics] AuthoringLint = false)");
        Log.Debug($"OverrideConflictLint: {_claimOrder.Count} replaced field(s) recorded, {conflicts.Count} replaced by more than one mod");
    }

    // ── CardTabGroup files (JsonDataLoader) ───────────────────────────────────

    /// <summary>
    /// Starts a load: <paramref name="vanillaGroups"/> are the CardTabGroups present as game assets before
    /// any mod object registered, each with its <c>TabName.ParentObjectID</c> (null or empty when it has
    /// none). Null keeps both sets empty.
    /// </summary>
    internal static void BeginTabGroups(IEnumerable<(string Name, string ParentObjectId)> vanillaGroups)
    {
        _vanillaGroupNames.Clear();
        _vanillaGroupByParentId.Clear();
        _tabGroupFindings.Clear();
        if (vanillaGroups == null) return;
        foreach (var (name, parent) in vanillaGroups)
        {
            if (string.IsNullOrEmpty(name)) continue;
            if (!_vanillaGroupNames.ContainsKey(name)) _vanillaGroupNames[name] = name;
            if (!string.IsNullOrEmpty(parent) && !_vanillaGroupByParentId.ContainsKey(parent)) _vanillaGroupByParentId[parent] = name;
        }
    }

    /// <summary>The <c>TabName.ParentObjectID</c> of a parsed CardTabGroup file, or null.</summary>
    internal static string ParentObjectIdOf(Dictionary<string, object> tree)
    {
        if (tree == null || !tree.TryGetValue("TabName", out var tn) || tn is not Dictionary<string, object> tabName) return null;
        return tabName.TryGetValue("ParentObjectID", out var p) && p is string s && s.Length > 0 ? s : null;
    }

    /// <summary>
    /// Records one mod <c>ScriptableObject/CardTabGroup/</c> file, <paramref name="name"/> being its file
    /// name without extension (the object's name). Kept as a finding when that name, or its
    /// <c>TabName.ParentObjectID</c>, is a vanilla group's.
    /// </summary>
    internal static void RecordCardTabGroup(string mod, string name, Dictionary<string, object> tree)
    {
        if (string.IsNullOrEmpty(name)) return;
        var parent = ParentObjectIdOf(tree);
        if (_vanillaGroupNames.TryGetValue(name, out var vanillaName))
            _tabGroupFindings.Add(new TabGroupFinding { Mod = mod ?? "(unknown mod)", Name = name, ParentObjectId = parent, ByName = true, VanillaGroup = vanillaName });
        else if (parent != null && _vanillaGroupByParentId.TryGetValue(parent, out var byParent))
            _tabGroupFindings.Add(new TabGroupFinding { Mod = mod ?? "(unknown mod)", Name = name, ParentObjectId = parent, ByName = false, VanillaGroup = byParent });
    }

    /// <summary>The CardTabGroup findings recorded since the last <see cref="BeginTabGroups"/>.</summary>
    internal static IReadOnlyList<TabGroupFinding> TabGroupFindings => _tabGroupFindings;

    /// <summary>The Warn text for one CardTabGroup finding.</summary>
    internal static string FormatTabGroup(TabGroupFinding f)
    {
        string what = f.ByName
            ? $"is named like the vanilla group '{f.VanillaGroup}', so it takes that name in the framework's registry and every reference by that name gets this copy"
            : $"has the same TabName.ParentObjectID ('{f.ParentObjectId}') as the vanilla group '{f.VanillaGroup}', so it is a copy of that vanilla group";
        return $"OverrideConflictLint: {f.Mod} ships ScriptableObject/CardTabGroup/{f.Name}.json, which {what}. " + TabGroupHint;
    }

    /// <summary>One Warn per recorded CardTabGroup finding.</summary>
    internal static void ReportTabGroups()
    {
        foreach (var f in _tabGroupFindings) Log.Warn(FormatTabGroup(f));
    }
}
