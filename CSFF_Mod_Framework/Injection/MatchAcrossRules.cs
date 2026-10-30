namespace CSFFModFramework.Injection;

/// <summary>
/// One <c>MatchAcross</c> rule of one blueprint: every card in the listed element slots must carry the same value of
/// one durability stat. <see cref="Stages"/>[i] / <see cref="Elements"/>[i] is the i-th listed slot, zero-based,
/// distinct, as resolved against the blueprint's loaded stages.
/// </summary>
internal sealed class MatchAcrossRule
{
    /// <summary>The stat name as written (<c>Special4</c>, <c>Usage</c>, ...).</summary>
    public string Stat;

    /// <summary>The vanilla <c>DurabilitiesTypes</c> value of <see cref="Stat"/> (Spoilage 1 ... Special4 256).</summary>
    public int DurabilityType;

    public int[] Stages = new int[0];
    public int[] Elements = new int[0];

    /// <summary>Where the rule was read: <c>blueprint JSON</c> or <c>.extras.json sidecar</c>.</summary>
    public string Source;

    /// <summary>True when stage <paramref name="stage"/>, element <paramref name="element"/> is a listed slot.</summary>
    internal bool IsListed(int stage, int element)
    {
        for (int i = 0; i < Stages.Length; i++)
            if (Stages[i] == stage && Elements[i] == element) return true;
        return false;
    }

    /// <summary>True when the rule lists any slot of stage <paramref name="stage"/>.</summary>
    internal bool StageHasListed(int stage)
    {
        for (int i = 0; i < Stages.Length; i++)
            if (Stages[i] == stage) return true;
        return false;
    }

    public override string ToString()
    {
        var parts = new string[Stages.Length];
        for (int i = 0; i < Stages.Length; i++) parts[i] = Stages[i] + "." + Elements[i];
        return Stat + " across [" + string.Join(", ", parts) + "]";
    }
}

/// <summary>
/// A card already counted for a rule: the stage and element slot it sits in, and its value of the rule's stat.
/// <see cref="Element"/> is -1 for a card remembered from an EARLIER stage (vanilla's
/// <c>BlueprintSaveData.PrevStageCards</c> keeps the stage index but not the element index); such a card counts
/// when the rule lists any slot of its stage.
/// </summary>
internal struct MatchAcrossPlaced
{
    public int Stage;
    public int Element;
    public float Value;

    public MatchAcrossPlaced(int stage, int element, float value)
    {
        Stage = stage;
        Element = element;
        Value = value;
    }
}

/// <summary>
/// The cross-slot matching rule (framework 2.29.0, fleet plan 6.1; finding
/// <c>Documentation/Research/Fleet/APE_Port/W0.4_cross_slot.md</c>). A blueprint declares, in its own JSON or in its
/// <c>.extras.json</c> sidecar:
/// <code>"MatchAcross": [ { "Stat": "Special4", "Elements": [ "0.0", "0.2" ] } ]</code>
/// Each element is <c>"&lt;stage&gt;.&lt;element&gt;"</c>, both zero-based positions in <c>BlueprintStages</c> and
/// its <c>RequiredElements</c> (the only identity an element has), or <c>"&lt;stage&gt;.&lt;uid&gt;"</c>, the
/// UniqueID of the element's required card or the name of its required tab group, resolved to a position at load.
/// A card offered to a listed slot whose stat differs from a card already in a listed slot (this stage, or an
/// earlier stage whose element remembers its cards) is refused.
///
/// Pure (BCL types only; the element names come through a delegate), so
/// <c>Development_Tools/Tests/Framework-MatchAcross.Tests.ps1</c> runs <see cref="Parse"/> and <see cref="Decide"/>
/// outside the game. <see cref="MatchAcrossService"/> is the game side.
/// </summary>
internal static class MatchAcrossRules
{
    /// <summary>The key a blueprint JSON or its sidecar carries.</summary>
    internal const string Key = "MatchAcross";

    private static readonly string[] StatNames = { "Spoilage", "Usage", "Fuel", "Progress", "Special1", "Special2", "Special3", "Special4" };
    private static readonly int[] StatTypes = { 1, 2, 4, 8, 32, 64, 128, 256 };

    /// <summary>The vanilla <c>DurabilitiesTypes</c> value for a stat name (case-insensitive), or 0 when it is none.</summary>
    internal static int DurabilityTypeOf(string stat)
    {
        if (string.IsNullOrEmpty(stat)) return 0;
        for (int i = 0; i < StatNames.Length; i++)
            if (string.Equals(StatNames[i], stat, StringComparison.OrdinalIgnoreCase)) return StatTypes[i];
        return 0;
    }

    /// <summary>
    /// Reads the value of a <c>MatchAcross</c> key (as MiniJson parsed it) into rules for blueprint
    /// <paramref name="owner"/>. <paramref name="elementsPerStage"/>[s] is the element count of stage s.
    /// <paramref name="namesAt"/>(s, e) returns the names that identify element e of stage s (its required card's
    /// UniqueID, its required tab group's name); it may be null when no element is named by UID. Every problem adds
    /// one line to <paramref name="issues"/>: a reference that is malformed, out of range, names nothing or names
    /// several elements is dropped; a rule whose Stat is unknown, or that keeps fewer than two distinct slots, is
    /// dropped whole.
    /// </summary>
    internal static List<MatchAcrossRule> Parse(string owner, object value, int[] elementsPerStage, Func<int, int, string[]> namesAt, List<string> issues, string source = null)
    {
        var rules = new List<MatchAcrossRule>();
        issues ??= new List<string>();
        elementsPerStage ??= new int[0];
        var where = $"{owner}: {Key}" + (string.IsNullOrEmpty(source) ? "" : $" ({source})");

        if (value is not List<object> list)
        {
            issues.Add($"{where} must be an array of {{ \"Stat\", \"Elements\" }} objects; ignored.");
            return rules;
        }

        for (int r = 0; r < list.Count; r++)
        {
            var at = $"{where} rule {r}";
            if (list[r] is not Dictionary<string, object> obj)
            {
                issues.Add($"{at} is not an object; skipped.");
                continue;
            }

            var stat = obj.TryGetValue("Stat", out var st) ? st as string : null;
            int type = DurabilityTypeOf(stat);
            if (type == 0)
            {
                issues.Add($"{at}: \"Stat\" must be one of {string.Join(", ", StatNames)} (got '{stat ?? "nothing"}'); skipped.");
                continue;
            }

            if (!obj.TryGetValue("Elements", out var el) || el is not List<object> refs)
            {
                issues.Add($"{at}: \"Elements\" must be an array of \"<stage>.<element>\" strings; skipped.");
                continue;
            }

            var stages = new List<int>();
            var elements = new List<int>();
            foreach (var raw in refs)
            {
                if (!TryResolve(raw as string, elementsPerStage, namesAt, out int s, out int e, out var problem))
                {
                    issues.Add($"{at}: element '{raw ?? "null"}' {problem}; that element is left out.");
                    continue;
                }
                bool dup = false;
                for (int i = 0; i < stages.Count; i++)
                    if (stages[i] == s && elements[i] == e) { dup = true; break; }
                if (dup)
                {
                    issues.Add($"{at}: element '{raw}' names slot {s}.{e} again; counted once.");
                    continue;
                }
                stages.Add(s);
                elements.Add(e);
            }

            if (stages.Count < 2)
            {
                issues.Add($"{at} ({stat}): needs at least two distinct element slots, has {stages.Count}; skipped.");
                continue;
            }

            rules.Add(new MatchAcrossRule
            {
                Stat = stat,
                DurabilityType = type,
                Stages = stages.ToArray(),
                Elements = elements.ToArray(),
                Source = source,
            });
        }
        return rules;
    }

    // "<stage>.<element>" (both non-negative integers) or "<stage>.<uid or group name>".
    private static bool TryResolve(string text, int[] elementsPerStage, Func<int, int, string[]> namesAt, out int stage, out int element, out string problem)
    {
        stage = -1;
        element = -1;
        problem = null;
        if (string.IsNullOrEmpty(text))
        {
            problem = "is not a \"<stage>.<element>\" string";
            return false;
        }
        int dot = text.IndexOf('.');
        if (dot <= 0 || dot == text.Length - 1)
        {
            problem = "is not \"<stage>.<element>\"";
            return false;
        }
        var stagePart = text.Substring(0, dot);
        var elementPart = text.Substring(dot + 1);
        if (!IsDigits(stagePart) || !int.TryParse(stagePart, out stage))
        {
            problem = $"has a stage '{stagePart}' that is not a zero-based number";
            return false;
        }
        if (stage >= elementsPerStage.Length)
        {
            problem = $"names stage {stage}, but the blueprint has {elementsPerStage.Length} stage(s)";
            return false;
        }
        int count = elementsPerStage[stage];

        if (IsDigits(elementPart))
        {
            if (!int.TryParse(elementPart, out element) || element >= count)
            {
                problem = $"names element {elementPart}, but stage {stage} has {count} element(s)";
                element = -1;
                return false;
            }
            return true;
        }

        // By name: the one element of that stage whose required card UID or tab group name it is.
        int found = -1, matches = 0;
        for (int e = 0; e < count; e++)
        {
            var names = namesAt?.Invoke(stage, e);
            if (names == null) continue;
            foreach (var n in names)
            {
                if (!string.IsNullOrEmpty(n) && string.Equals(n, elementPart, StringComparison.OrdinalIgnoreCase))
                {
                    if (found != e) { matches++; found = e; }
                    break;
                }
            }
        }
        if (matches == 0)
        {
            problem = $"names no element of stage {stage} (no required card UID or tab group name '{elementPart}')";
            return false;
        }
        if (matches > 1)
        {
            problem = $"names {matches} elements of stage {stage}; use \"{stage}.<element number>\"";
            return false;
        }
        element = found;
        return true;
    }

    private static bool IsDigits(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (var c in s)
            if (c < '0' || c > '9') return false;
        return true;
    }

    /// <summary>
    /// The rule itself. A candidate offered to slot <paramref name="element"/> of stage <paramref name="stage"/>
    /// (the stage being built) with stat value <paramref name="candidate"/> is accepted when the slot is not listed,
    /// or when every card in <paramref name="placed"/> that counts carries the same value. A placed card counts when
    /// it sits in a listed slot of this stage or an earlier one (an earlier-stage card with Element -1 counts when
    /// its stage has a listed slot); cards in later stages never count. The caller leaves the candidate itself out
    /// of <paramref name="placed"/>. Values compare as vanilla's stack filter compares them (Unity
    /// <c>Mathf.Approximately</c>). On a refusal <paramref name="conflict"/> is the index of the first placed card
    /// that differs, otherwise -1.
    /// </summary>
    internal static bool Decide(MatchAcrossRule rule, int stage, int element, float candidate, IList<MatchAcrossPlaced> placed, out int conflict)
    {
        conflict = -1;
        if (rule == null || !rule.IsListed(stage, element) || placed == null) return true;
        for (int i = 0; i < placed.Count; i++)
        {
            var p = placed[i];
            if (p.Stage > stage) continue;
            bool counts = p.Element < 0
                ? p.Stage < stage && rule.StageHasListed(p.Stage)
                : rule.IsListed(p.Stage, p.Element);
            if (!counts) continue;
            if (!Approximately(p.Value, candidate))
            {
                conflict = i;
                return false;
            }
        }
        return true;
    }

    /// <summary>Unity's <c>Mathf.Approximately</c>, restated without the engine.</summary>
    internal static bool Approximately(float a, float b)
        => Math.Abs(b - a) < Math.Max(1E-06f * Math.Max(Math.Abs(a), Math.Abs(b)), float.Epsilon * 8f);
}
