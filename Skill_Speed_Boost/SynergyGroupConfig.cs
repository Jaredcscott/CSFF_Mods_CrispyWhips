namespace Skill_Speed_Boost;

/// <summary>
/// Parses the SynergyGroupOverrides config string: <c>Skill Name=GroupA,GroupB;Skill Name=GroupC</c>.
///
/// An entry REPLACES the built-in synergy groups of that one skill (SkillSynergies' table), so it can add
/// a skill the table does not list (a mod's own skill, or vanilla Perception), move a skill to other
/// groups, or, with nothing after the '=', take it out of synergies altogether. Skill names are the
/// names the game shows (GameStat GameName, as GameLoadPatch.SkillNamesByUniqueId records them) and may
/// contain spaces; skill and group names are both matched without regard to letter case.
///
/// The names are typed by the PLAYER, or are the shipped default naming Community Mod Chest's Melee
/// Fighting by its display name, never by a foreign UniqueID: a skill no loaded mod provides simply
/// never matches (soft dependency by construction).
///
/// Fails closed PER ENTRY: a segment that cannot be read is left out and described in
/// <paramref name="problems"/>, which the caller logs once each. Nothing here throws.
/// </summary>
internal static class SynergyGroupConfig
{
    /// <summary>
    /// Pure, with no game or BepInEx types, so SkillSpeedBoost-BonusLogic.Tests.ps1 compiles and runs
    /// it: keep the body C# 5, which is what PowerShell 5.1's Add-Type compiles. A repeated group inside
    /// one entry is kept once; a repeated skill warns and its last entry wins.
    /// </summary>
    internal static Dictionary<string, string[]> Parse(string text, List<string> problems)
    {
        var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(text) || text.Trim().Length == 0) return map;

        foreach (string raw in text.Split(';'))
        {
            string segment = raw.Trim();
            if (segment.Length == 0) continue;   // a trailing or doubled ';' is not an error

            int eq = segment.IndexOf('=');
            if (eq < 0)
            {
                if (problems != null) problems.Add("'" + segment + "' is not Skill Name=GroupA,GroupB, so it was skipped.");
                continue;
            }
            if (segment.IndexOf('=', eq + 1) >= 0)
            {
                if (problems != null) problems.Add("'" + segment + "' has more than one '=' (separate entries with ';'), so it was skipped.");
                continue;
            }

            string skill = segment.Substring(0, eq).Trim();
            if (skill.Length == 0)
            {
                if (problems != null) problems.Add("'" + segment + "' names no skill before the '=', so it was skipped.");
                continue;
            }
            if (skill.IndexOf(',') >= 0)
            {
                if (problems != null) problems.Add("'" + segment + "': '" + skill + "' is more than one skill (one skill an entry, entries separated by ';'), so it was skipped.");
                continue;
            }

            var groups = new List<string>();
            bool emptyGroup = false;
            string right = segment.Substring(eq + 1).Trim();
            if (right.Length > 0)
            {
                foreach (string part in right.Split(','))
                {
                    string group = part.Trim();
                    if (group.Length == 0) { emptyGroup = true; break; }
                    if (groups.FindIndex(g => string.Equals(g, group, StringComparison.OrdinalIgnoreCase)) < 0)
                        groups.Add(group);
                }
            }
            if (emptyGroup)
            {
                if (problems != null) problems.Add("'" + segment + "' has an empty group name (a stray ','), so it was skipped.");
                continue;
            }

            if (map.ContainsKey(skill) && problems != null)
                problems.Add("'" + skill + "' is listed more than once; the last entry, '" + segment + "', is used.");
            map[skill] = groups.ToArray();
        }

        return map;
    }

    /// <summary>
    /// The synergy groups of a skill: its override when it has one, else its row in the built-in table.
    /// Null when neither lists it (it takes no part in synergies); an empty array when an override took
    /// it out of every group.
    /// </summary>
    internal static string[] ResolveGroups(string skillName, Dictionary<string, string[]> overrides, Dictionary<string, string[]> table)
    {
        if (string.IsNullOrEmpty(skillName)) return null;
        string[] groups;
        if (overrides != null && overrides.TryGetValue(skillName, out groups)) return groups;
        if (table != null && table.TryGetValue(skillName, out groups)) return groups;
        return null;
    }

    /// <summary>
    /// The override skills worth a typo warning, sorted: no loaded skill has that name, and the shipped
    /// default does not name it either. The default's Melee Fighting is Community Mod Chest's skill, so a
    /// game without that mod lacks it on purpose, and a warning there on every boot would be noise.
    /// </summary>
    internal static List<string> UnknownSkills(Dictionary<string, string[]> overrides, ICollection<string> loadedSkills, Dictionary<string, string[]> shippedDefault)
    {
        var unknown = new List<string>();
        if (overrides == null) return unknown;
        foreach (string skill in overrides.Keys)
        {
            if (shippedDefault != null && shippedDefault.ContainsKey(skill)) continue;
            bool loaded = false;
            if (loadedSkills != null)
            {
                foreach (string name in loadedSkills)
                {
                    if (string.Equals(name, skill, StringComparison.OrdinalIgnoreCase)) { loaded = true; break; }
                }
            }
            if (!loaded) unknown.Add(skill);
        }
        unknown.Sort(StringComparer.OrdinalIgnoreCase);
        return unknown;
    }

    /// <summary>
    /// The resolved overrides for the [EffectiveSettings] log, sorted by skill: 'none', or entries such as
    /// 'Melee Fighting -> Combat; Swimming -> no group'. A skill no loaded skill matches is marked
    /// '(not a loaded skill)' once <paramref name="loadedSkills"/> is known; null leaves it unmarked.
    /// </summary>
    internal static string Describe(Dictionary<string, string[]> overrides, ICollection<string> loadedSkills)
    {
        if (overrides == null || overrides.Count == 0) return "none";
        var skills = new List<string>(overrides.Keys);
        skills.Sort(StringComparer.OrdinalIgnoreCase);
        var parts = new List<string>();
        foreach (string skill in skills)
        {
            string[] groups = overrides[skill];
            string entry = skill + " -> " + (groups == null || groups.Length == 0 ? "no group" : string.Join("/", groups));
            if (loadedSkills != null)
            {
                bool loaded = false;
                foreach (string name in loadedSkills)
                {
                    if (string.Equals(name, skill, StringComparison.OrdinalIgnoreCase)) { loaded = true; break; }
                }
                if (!loaded) entry += " (not a loaded skill)";
            }
            parts.Add(entry);
        }
        return string.Join("; ", parts.ToArray());
    }
}
