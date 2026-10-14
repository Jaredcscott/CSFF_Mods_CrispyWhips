using System.Globalization;

namespace Skill_Speed_Boost;

/// <summary>
/// Parses a scoped bonus map: <c>Scope:Skill=multiplier;Scope=multiplier</c>, where the scope is what
/// the map is keyed by (a season for SeasonBonusMap; the environment, tool, action and perk maps the
/// plan adds next reuse this parser with their own scope test) and the skill is the name the game
/// shows. A scope with no skill, or with the skill written as <c>*</c>, applies to every skill, and an
/// entry that names the skill beats the every-skill entry of the same scope.
///
/// The key is split at its LAST ':' so a scope may contain a ':' of its own (an action name), and the
/// number is everything after the LAST '='. Fails closed PER ENTRY: a segment that cannot be read is
/// left out and described in the problems list, which the caller logs once each. Nothing here throws,
/// so a typo in one setting can never take the rest of the mod down with it.
///
/// Pure, with no game or BepInEx types, so SkillSpeedBoost-BonusLogic.Tests.ps1 compiles and runs it:
/// keep every body C# 5, which is what PowerShell 5.1's Add-Type compiles.
/// </summary>
internal static class ScopedBonusConfig
{
    /// <summary>The skill key of an entry that applies to every skill of its scope.</summary>
    internal const string AnySkill = "*";

    /// <summary>The season names GameQuery.CurrentSeason returns (SeasonsSettings, EA 0.68b).</summary>
    internal static readonly string[] SeasonNames = { "Spring", "Summer", "Autumn", "Winter" };

    /// <summary>
    /// Parses <paramref name="text"/> into scope, then skill, then multiplier, both keys ignoring letter
    /// case. <paramref name="isValidScope"/> decides which scopes exist; <paramref name="scopeLabel"/>
    /// ("Season") and <paramref name="validScopes"/> ("a season (Spring, Summer, Autumn or Winter)")
    /// only word the problems. A multiplier must be above 0 and at most <paramref name="maxMultiplier"/>;
    /// below 1 is allowed and slows XP, like the low-condition penalty.
    /// </summary>
    internal static Dictionary<string, Dictionary<string, float>> Parse(string text, float maxMultiplier,
        Predicate<string> isValidScope, string scopeLabel, string validScopes, List<string> problems)
    {
        var map = new Dictionary<string, Dictionary<string, float>>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(text) || text.Trim().Length == 0) return map;

        foreach (string raw in text.Split(';'))
        {
            string segment = raw.Trim();
            if (segment.Length == 0) continue;   // a trailing or doubled ';' is not an error

            int eq = segment.LastIndexOf('=');
            if (eq <= 0 || eq >= segment.Length - 1)
            {
                if (problems != null) problems.Add("'" + segment + "' is not " + scopeLabel + ":Skill=multiplier or " + scopeLabel + "=multiplier, so it was skipped.");
                continue;
            }

            string key = segment.Substring(0, eq).Trim();
            string number = segment.Substring(eq + 1).Trim();

            string scope = key;
            string skill = AnySkill;
            int colon = key.LastIndexOf(':');
            if (colon >= 0)
            {
                scope = key.Substring(0, colon).Trim();
                skill = key.Substring(colon + 1).Trim();
                if (skill.Length == 0)
                {
                    if (problems != null) problems.Add("'" + segment + "' names no skill after the ':' (write * for every skill), so it was skipped.");
                    continue;
                }
            }

            if (scope.Length == 0)
            {
                if (problems != null) problems.Add("'" + segment + "' names no " + scopeLabel + " before the ':', so it was skipped.");
                continue;
            }

            if (isValidScope != null && !isValidScope(scope))
            {
                if (problems != null) problems.Add("'" + segment + "': '" + scope + "' is not " + validScopes + ", so it was skipped.");
                continue;
            }

            float multiplier;
            if (!float.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out multiplier))
            {
                if (problems != null) problems.Add("'" + segment + "': '" + number + "' is not a number (write decimals with a dot, e.g. 1.25), so it was skipped.");
                continue;
            }

            // NaN fails both comparisons, so it lands here too.
            if (!(multiplier > 0f && multiplier <= maxMultiplier))
            {
                if (problems != null) problems.Add("'" + segment + "': the multiplier must be above 0 and at most " + maxMultiplier.ToString(CultureInfo.InvariantCulture) + ", so it was skipped.");
                continue;
            }

            Dictionary<string, float> skills;
            if (!map.TryGetValue(scope, out skills))
            {
                skills = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
                map[scope] = skills;
            }
            if (skills.ContainsKey(skill) && problems != null)
                problems.Add("'" + scope + ":" + skill + "' is listed more than once; the last value, " + multiplier.ToString(CultureInfo.InvariantCulture) + ", is used.");
            skills[skill] = multiplier;
        }

        return map;
    }

    /// <summary>
    /// The multiplier for <paramref name="skill"/> in <paramref name="scope"/>: the entry naming the
    /// skill, else the scope's every-skill entry, else 1 (no scope, an unlisted scope, no skill name).
    /// </summary>
    internal static float Lookup(Dictionary<string, Dictionary<string, float>> map, string scope, string skill)
    {
        if (map == null || string.IsNullOrEmpty(scope)) return 1f;
        Dictionary<string, float> skills;
        if (!map.TryGetValue(scope, out skills) || skills == null) return 1f;
        float multiplier;
        if (!string.IsNullOrEmpty(skill) && skills.TryGetValue(skill, out multiplier)) return multiplier;
        if (skills.TryGetValue(AnySkill, out multiplier)) return multiplier;
        return 1f;
    }

    /// <summary>How many Scope:Skill entries the map holds, for the [EffectiveSettings] log.</summary>
    internal static int CountEntries(Dictionary<string, Dictionary<string, float>> map)
    {
        if (map == null) return 0;
        int count = 0;
        foreach (var skills in map.Values)
        {
            if (skills != null) count += skills.Count;
        }
        return count;
    }

    /// <summary>
    /// The skill names a map lists that no loaded skill has, sorted and each once: a typo there never
    /// matches a gain, so the caller reports it once the skill scan has run. The every-skill key is
    /// never reported.
    /// </summary>
    internal static List<string> UnknownSkills(Dictionary<string, Dictionary<string, float>> map, ICollection<string> loadedSkills)
    {
        var unknown = new List<string>();
        if (map == null) return unknown;
        foreach (var skills in map.Values)
        {
            if (skills == null) continue;
            foreach (string skill in skills.Keys)
            {
                if (skill == AnySkill) continue;
                bool loaded = false;
                if (loadedSkills != null)
                {
                    foreach (string name in loadedSkills)
                    {
                        if (string.Equals(name, skill, StringComparison.OrdinalIgnoreCase)) { loaded = true; break; }
                    }
                }
                if (loaded) continue;
                bool listed = false;
                foreach (string seen in unknown)
                {
                    if (string.Equals(seen, skill, StringComparison.OrdinalIgnoreCase)) { listed = true; break; }
                }
                if (!listed) unknown.Add(skill);
            }
        }
        unknown.Sort(StringComparer.OrdinalIgnoreCase);
        return unknown;
    }

    /// <summary>True for Spring, Summer, Autumn or Winter, in any letter case.</summary>
    internal static bool IsSeasonName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        foreach (string season in SeasonNames)
        {
            if (string.Equals(season, name, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>SeasonBonusMap: a scoped map whose scope is a season name.</summary>
    internal static Dictionary<string, Dictionary<string, float>> ParseSeasonMap(string text, float maxMultiplier, List<string> problems)
    {
        return Parse(text, maxMultiplier, IsSeasonName, "Season", "a season (Spring, Summer, Autumn or Winter)", problems);
    }

    /// <summary>
    /// An EnvironmentBonusMap place: an exact environment UniqueID or asset name (no '*'), or an
    /// asset-name prefix ending in '*' (Env_Cave*). A '*' anywhere else is not a place, and nor is a
    /// '*' alone: "everywhere" is what the per-skill multipliers already are.
    /// </summary>
    internal static bool IsEnvironmentScope(string scope)
    {
        if (string.IsNullOrEmpty(scope)) return false;
        int star = scope.IndexOf('*');
        if (star < 0) return true;
        return star == scope.Length - 1 && scope.Length > 1;
    }

    /// <summary>EnvironmentBonusMap: a scoped map whose scope is a place (see IsEnvironmentScope).</summary>
    internal static Dictionary<string, Dictionary<string, float>> ParseEnvironmentMap(string text, float maxMultiplier, List<string> problems)
    {
        return Parse(text, maxMultiplier, IsEnvironmentScope, "Place",
            "a place's UniqueID or asset name, or an asset-name prefix ending in * (such as Env_Cave*)", problems);
    }

    /// <summary>
    /// The EnvironmentBonusMap place whose entries apply to <paramref name="skill"/> in the environment
    /// <paramref name="uid"/>, whose asset name is <paramref name="name"/> (Env_CaveOldHollow), or null.
    /// The most specific place wins: the exact UniqueID, then the exact asset name, then the longest
    /// matching prefix. A place with neither an entry for this skill nor an every-skill entry is passed
    /// over, so Env_GrovePine*:Woodworking does not hide Env_Grove*'s every-skill entry from Herbalism.
    /// </summary>
    internal static string MatchEnvironmentScope(Dictionary<string, Dictionary<string, float>> map, string uid, string name, string skill)
    {
        if (map == null || map.Count == 0) return null;
        if (ExactPlaceApplies(map, uid, skill)) return uid;
        if (ExactPlaceApplies(map, name, skill)) return name;
        if (string.IsNullOrEmpty(name)) return null;

        string best = null;
        int bestLength = -1;
        foreach (var place in map)
        {
            string key = place.Key;
            if (key.Length < 2 || key[key.Length - 1] != '*') continue;
            string prefix = key.Substring(0, key.Length - 1);
            if (prefix.Length <= bestLength) continue;
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (!HasEntryFor(place.Value, skill)) continue;
            best = key;
            bestLength = prefix.Length;
        }
        return best;
    }

    /// <summary>
    /// The EnvironmentBonusMap multiplier for <paramref name="skill"/> in this environment: the
    /// matching place's entry for the skill, else that place's every-skill entry, else 1.
    /// </summary>
    internal static float LookupEnvironment(Dictionary<string, Dictionary<string, float>> map, string uid, string name, string skill)
    {
        string place = MatchEnvironmentScope(map, uid, name, skill);
        return place == null ? 1f : Lookup(map, place, skill);
    }

    /// <summary>
    /// True when some place in the map names this environment, whatever the skill. An environment no
    /// place names gets one Debug breadcrumb, which is how a prefix a game update renamed away shows.
    /// </summary>
    internal static bool EnvironmentListed(Dictionary<string, Dictionary<string, float>> map, string uid, string name)
    {
        if (map == null || map.Count == 0) return false;
        if (!string.IsNullOrEmpty(uid) && map.ContainsKey(uid)) return true;
        if (string.IsNullOrEmpty(name)) return false;
        if (map.ContainsKey(name)) return true;
        foreach (string key in map.Keys)
        {
            if (key.Length > 1 && key[key.Length - 1] == '*'
                && name.StartsWith(key.Substring(0, key.Length - 1), StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    // An exact place (no '*') that this environment's UniqueID or asset name equals, with an entry
    // that applies to the skill.
    private static bool ExactPlaceApplies(Dictionary<string, Dictionary<string, float>> map, string key, string skill)
    {
        if (string.IsNullOrEmpty(key) || key.IndexOf('*') >= 0) return false;
        Dictionary<string, float> skills;
        return map.TryGetValue(key, out skills) && HasEntryFor(skills, skill);
    }

    // The place lists this skill by name, or has an every-skill entry.
    private static bool HasEntryFor(Dictionary<string, float> skills, string skill)
    {
        if (skills == null) return false;
        if (!string.IsNullOrEmpty(skill) && skills.ContainsKey(skill)) return true;
        return skills.ContainsKey(AnySkill);
    }

    /// <summary>
    /// A ToolBonusMap key: a card's UniqueID or one of its tag names (tag_Axe). Neither ever holds a
    /// space or a '*', so a key with one is a typo, not a card, and is reported rather than kept.
    /// </summary>
    internal static bool IsToolScope(string scope)
    {
        if (string.IsNullOrEmpty(scope)) return false;
        foreach (char c in scope)
        {
            if (c == '*' || char.IsWhiteSpace(c)) return false;
        }
        return true;
    }

    /// <summary>ToolBonusMap: a scoped map whose scope is a card UniqueID or tag name.</summary>
    internal static Dictionary<string, Dictionary<string, float>> ParseToolMap(string text, float maxMultiplier, List<string> problems)
    {
        return Parse(text, maxMultiplier, IsToolScope, "Tool",
            "a card's UniqueID or tag name (such as tag_Axe), which never holds a space or a *", problems);
    }

    /// <summary>
    /// An ActionBonusMap key: an action's name as its button shows it in English (Grind), or its
    /// localization key. Names hold spaces, so only a '*' is refused: no action has one, and a prefix
    /// such as Grind* would otherwise match nothing in silence.
    /// </summary>
    internal static bool IsActionScope(string scope)
    {
        return !string.IsNullOrEmpty(scope) && scope.IndexOf('*') < 0;
    }

    /// <summary>
    /// ActionBonusMap: a scoped map whose scope is an action. The key splits at its LAST ':', so an
    /// action whose name holds a ':' is written with an explicit skill, such as "Cut: Fine:*=1.2".
    /// </summary>
    internal static Dictionary<string, Dictionary<string, float>> ParseActionMap(string text, float maxMultiplier, List<string> problems)
    {
        return Parse(text, maxMultiplier, IsActionScope, "Action",
            "an action's name or localization key (without a *)", problems);
    }

    /// <summary>
    /// The multiplier for <paramref name="skill"/> from a map looked up by several keys at once (a
    /// tool's UniqueID and its tags; an action's name and its key): the HIGHEST of the keys that
    /// have an entry for the skill (their own, else their every-skill entry), never their product, so
    /// listing a card and one of its tags does not stack. 1 when no key has one.
    /// </summary>
    internal static float LookupAny(Dictionary<string, Dictionary<string, float>> map, IList<string> keys, string skill)
    {
        if (map == null || map.Count == 0 || keys == null) return 1f;
        bool found = false;
        float best = 1f;
        foreach (string key in keys)
        {
            if (string.IsNullOrEmpty(key)) continue;
            Dictionary<string, float> skills;
            if (!map.TryGetValue(key, out skills) || !HasEntryFor(skills, skill)) continue;
            float value = Lookup(map, key, skill);
            if (!found || value > best)
            {
                best = value;
                found = true;
            }
        }
        return found ? best : 1f;
    }

    /// <summary>
    /// A PerkBonusMap key: a perk's UniqueID, exactly as the perk's own file spells it (the game looks a
    /// perk up case-sensitively, so a wrongly cased key never matches). No UniqueID holds a space or a
    /// '*', so a key with one is a typo or an attempt at a pattern the map does not have, and is reported
    /// rather than kept. An id no installed mod defines is NOT an error: it simply never matches, which is
    /// how the shipped default stays inert without the mod that owns those perks.
    /// </summary>
    internal static bool IsPerkScope(string scope)
    {
        if (string.IsNullOrEmpty(scope)) return false;
        foreach (char c in scope)
        {
            if (c == '*' || char.IsWhiteSpace(c)) return false;
        }
        return true;
    }

    /// <summary>PerkBonusMap: a scoped map whose scope is a perk UniqueID.</summary>
    internal static Dictionary<string, Dictionary<string, float>> ParsePerkMap(string text, float maxMultiplier, List<string> problems)
    {
        return Parse(text, maxMultiplier, IsPerkScope, "Perk",
            "a perk's UniqueID, which never holds a space or a *", problems);
    }

    /// <summary>
    /// The PerkBonusMap multiplier for <paramref name="skill"/> given the mapped perks the player holds
    /// (<paramref name="held"/>, a list of map keys): the HIGHEST entry among them for the skill, never
    /// their product, so two graduate perks that both speed one skill do not stack. 1 when nothing is
    /// held, a held perk names no entry for the skill, or the map is empty.
    /// </summary>
    internal static float HighestFor(string skill, IList<string> held, Dictionary<string, Dictionary<string, float>> map)
    {
        return LookupAny(map, held, skill);
    }
}
