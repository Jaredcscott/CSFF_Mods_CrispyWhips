namespace Skill_Speed_Boost;

/// <summary>
/// Parses the MorningBonusSkills and NightBonusSkills config strings (plan D1): a list of skill names
/// separated by ',' or ';', such as <c>Cooking, Herbalism</c>. An EMPTY list means every skill, which is
/// the default and exactly what the morning and night windows did before the lists existed (1.11.0).
/// A listed name limits that window to the skills named.
///
/// Skill names are the names the game shows (GameStat GameName, as GameLoadPatch.SkillNamesByUniqueId
/// records them), may contain spaces, and are matched without regard to letter case. A name no loaded
/// skill has simply never matches; Plugin reports it once after the skill scan, because a typo in a
/// non-empty list would otherwise switch the window off for every skill without a word.
///
/// Nothing here throws, and nothing here needs a game or BepInEx type, so
/// SkillSpeedBoost-BonusLogic.Tests.ps1 compiles and runs every member: keep the bodies C# 5, which is
/// what PowerShell 5.1's Add-Type compiles (no string interpolation, no '?.', no expression bodies).
/// </summary>
internal static class SkillListConfig
{
    /// <summary>
    /// The skill names in <paramref name="text"/>, trimmed, each kept once whatever its letter case.
    /// Empty pieces (a doubled or trailing separator, a blank setting) are not names and are dropped, so
    /// an empty or blank setting gives an empty set: every skill.
    /// </summary>
    internal static HashSet<string> Parse(string text)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(text)) return names;

        foreach (string raw in text.Split(new char[] { ',', ';' }))
        {
            string name = raw.Trim();
            if (name.Length > 0) names.Add(name);
        }
        return names;
    }

    /// <summary>
    /// True when a window limited to <paramref name="listed"/> applies to <paramref name="skillName"/>.
    /// An empty (or missing) list applies to every skill, the skill's name unread or not, which is the
    /// 1.11.0 behaviour exactly. A non-empty list applies only to a skill it names; a gain whose skill
    /// name could not be resolved is not named by any list, so it gets no window bonus under one.
    /// </summary>
    internal static bool AppliesTo(HashSet<string> listed, string skillName)
    {
        if (listed == null || listed.Count == 0) return true;
        if (string.IsNullOrEmpty(skillName)) return false;
        return listed.Contains(skillName.Trim());
    }

    /// <summary>
    /// The names in <paramref name="listed"/> that no loaded skill has, sorted, for one warning each.
    /// </summary>
    internal static List<string> UnknownSkills(HashSet<string> listed, IEnumerable<string> loadedSkillNames)
    {
        var unknown = new List<string>();
        if (listed == null || listed.Count == 0) return unknown;

        var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (loadedSkillNames != null)
        {
            foreach (string loadedName in loadedSkillNames)
            {
                if (!string.IsNullOrEmpty(loadedName)) loaded.Add(loadedName);
            }
        }

        foreach (string name in listed)
        {
            if (!loaded.Contains(name)) unknown.Add(name);
        }
        unknown.Sort(StringComparer.OrdinalIgnoreCase);
        return unknown;
    }

    /// <summary>
    /// The list as the [EffectiveSettings] line shows it: "every skill" when empty, otherwise the names
    /// sorted and joined with '/', so the line's own ', ' separators stay unambiguous.
    /// </summary>
    internal static string Describe(HashSet<string> listed)
    {
        if (listed == null || listed.Count == 0) return "every skill";
        var sorted = new List<string>(listed);
        sorted.Sort(StringComparer.OrdinalIgnoreCase);
        return string.Join("/", sorted.ToArray());
    }
}
