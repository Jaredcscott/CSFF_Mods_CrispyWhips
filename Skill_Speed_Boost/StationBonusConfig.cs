using System.Globalization;

namespace Skill_Speed_Boost;

/// <summary>
/// Parses the StationBonusMap config string: <c>UniqueID=multiplier;UniqueID=multiplier</c>.
///
/// The UniqueIDs are typed by the PLAYER into their own config; nothing in this mod ships one, so a
/// partner mod's structure is referenced only where the player chose to name it (soft dependency by
/// construction: an unknown UID simply never matches a structure).
///
/// Fails closed PER ENTRY: a segment that cannot be read is left out and described in
/// <paramref name="problems"/>, which the caller logs once each. Nothing here throws, so a typo in
/// this one setting can never take the rest of the mod down with it.
/// </summary>
internal static class StationBonusConfig
{
    internal const float MaxMultiplier = 10f;

    /// <summary>
    /// Pure, with no game or BepInEx types, so SkillSpeedBoost-BonusLogic.Tests.ps1 compiles and runs
    /// it: keep the body C# 5, which is what PowerShell 5.1's Add-Type compiles.
    /// </summary>
    internal static Dictionary<string, float> Parse(string text, List<string> problems)
    {
        var map = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(text) || text.Trim().Length == 0) return map;

        foreach (string raw in text.Split(';'))
        {
            string segment = raw.Trim();
            if (segment.Length == 0) continue;   // a trailing or doubled ';' is not an error

            int eq = segment.IndexOf('=');
            if (eq <= 0 || eq >= segment.Length - 1)
            {
                if (problems != null) problems.Add("'" + segment + "' is not UniqueID=multiplier, so it was skipped.");
                continue;
            }

            string uid = segment.Substring(0, eq).Trim();
            string number = segment.Substring(eq + 1).Trim();

            bool hasSpace = false;
            foreach (char c in uid)
            {
                if (char.IsWhiteSpace(c)) { hasSpace = true; break; }
            }
            if (uid.Length == 0 || hasSpace)
            {
                if (problems != null) problems.Add("'" + segment + "' has no usable UniqueID (UniqueIDs contain no spaces), so it was skipped.");
                continue;
            }

            float multiplier;
            if (!float.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out multiplier))
            {
                if (problems != null) problems.Add("'" + segment + "': '" + number + "' is not a number (write decimals with a dot, e.g. 1.25), so it was skipped.");
                continue;
            }

            // NaN fails both comparisons, so it lands here too.
            if (!(multiplier > 0f && multiplier <= MaxMultiplier))
            {
                if (problems != null) problems.Add("'" + segment + "': the multiplier must be above 0 and at most " + MaxMultiplier.ToString(CultureInfo.InvariantCulture) + ", so it was skipped.");
                continue;
            }

            if (map.ContainsKey(uid) && problems != null)
                problems.Add("'" + uid + "' is listed more than once; the last value, " + multiplier.ToString(CultureInfo.InvariantCulture) + ", is used.");
            map[uid] = multiplier;
        }

        return map;
    }
}
