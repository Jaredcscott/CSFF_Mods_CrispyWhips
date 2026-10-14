using System.Globalization;
using BepInEx.Logging;
using CSFFModFramework.Api;

namespace Skill_Speed_Boost.Patcher;

/// <summary>
/// LogDailyXpSummary: one LogOutput.log line per skill per in-game day, totalling that day's gains.
/// A balance-tuning view between nothing and LogSkillXpGains, which writes a line for every gain.
///
/// MorningBonusPatch.ChangeStat_Post calls Record beside each LogXpGain call, so the digest counts
/// exactly the gains the XP log reports: gains that reached composition, the 1x no-op and the
/// per-skill 0x revert included. Record reads GameQuery.CurrentDay and rolls the totals over when it
/// changes, in either direction, the way the daily first-use ledger does (loading an earlier save
/// moves the day backwards). Plugin.OnApplicationQuit and OnDestroy call Flush for the day still
/// open. CurrentDay reads 0 when the game manager cannot be read, a constant, so the digest then
/// degrades to one set of lines at quit rather than one per gain.
///
/// Mod Logging Norms: per-item lines are Debug, but this is an opt-in diagnostic (default off) that
/// is bounded at one Info line per skill per day, and it has to reach a default LogOutput.log to be
/// any use. In memory only: nothing is written to disk but the log lines.
/// </summary>
internal static class XpDigest
{
    private static ManualLogSource Logger => Plugin.Logger;

    private static readonly DigestAccumulator _accumulator = new DigestAccumulator();
    // Warn once per cause (preflight D17): these run on every skill XP gain while the digest is on.
    private static bool _recordFailureLogged;
    private static bool _rollFailureLogged;
    private static bool _flushFailureLogged;

    /// <summary>Adds one gain to today's totals, first emitting the previous day's lines if the day changed.</summary>
    internal static void Record(string skill, float gameDelta, float finalDelta)
    {
        Roll(GameQuery.CurrentDay);
        try
        {
            _accumulator.Add(skill, gameDelta, finalDelta);
        }
        catch (Exception ex)
        {
            // Runs inside the ChangeStatValue postfix after the XP write, so a failure here must never
            // escape into the game's coroutine. Warn once: the digest is a diagnostic, not a bonus.
            if (!_recordFailureLogged)
            {
                _recordFailureLogged = true;
                Logger?.LogWarning($"[XpDigest] Recording a skill XP gain failed, so the daily XP summary may be incomplete: {ex.Message}");
            }
        }
    }

    /// <summary>Moves the digest to <paramref name="day"/>, emitting the lines of the day it leaves.</summary>
    internal static void Roll(int day)
    {
        try
        {
            Emit(_accumulator.Roll(day));
        }
        catch (Exception ex)
        {
            if (!_rollFailureLogged)
            {
                _rollFailureLogged = true;
                Logger?.LogWarning($"[XpDigest] Rolling the daily XP summary over to a new day failed: {ex.Message}");
            }
        }
    }

    /// <summary>Emits the lines of the day still open. Called on quit; a second call emits nothing.</summary>
    internal static void Flush()
    {
        try
        {
            Emit(_accumulator.Flush());
        }
        catch (Exception ex)
        {
            if (!_flushFailureLogged)
            {
                _flushFailureLogged = true;
                Logger?.LogWarning($"[XpDigest] Writing the last day's XP summary at quit failed: {ex.Message}");
            }
        }
    }

    private static void Emit(List<string> lines)
    {
        if (lines == null) return;
        foreach (string line in lines)
            Logger?.LogInfo(line);
    }
}

/// <summary>
/// The digest's arithmetic, with no game types and no logging. Pure and C# 5 (no expression bodies,
/// interpolation or null-conditionals), because SkillSpeedBoost-BonusLogic.Tests.ps1 compiles this
/// class out of the source with PowerShell 5.1's Add-Type and runs it.
/// </summary>
internal sealed class DigestAccumulator
{
    private sealed class Totals
    {
        public int Gains;
        public float Game;
        public float Final;
    }

    // The in-game day the totals belong to. int.MinValue until the first Roll, so the first Roll
    // always adopts its day without emitting anything.
    private int _day = int.MinValue;
    private readonly Dictionary<string, Totals> _bySkill = new Dictionary<string, Totals>(StringComparer.OrdinalIgnoreCase);

    internal int Day { get { return _day; } }

    /// <summary>Adds one gain to the totals of the current day.</summary>
    internal void Add(string skill, float gameDelta, float finalDelta)
    {
        string key = string.IsNullOrEmpty(skill) ? "(unresolved skill)" : skill;
        Totals totals;
        if (!_bySkill.TryGetValue(key, out totals))
        {
            totals = new Totals();
            _bySkill[key] = totals;
        }
        totals.Gains++;
        totals.Game += gameDelta;
        totals.Final += finalDelta;
    }

    /// <summary>
    /// Moves to <paramref name="day"/>. When it differs from the day the totals belong to, in EITHER
    /// direction (a backwards day is an earlier save loaded), returns that day's lines and clears the
    /// totals; the same day returns no lines and keeps them.
    /// </summary>
    internal List<string> Roll(int day)
    {
        if (day == _day) return new List<string>();
        List<string> lines = Flush();
        _day = day;
        return lines;
    }

    /// <summary>The current day's lines, one per skill in name order, then clears the totals. Keeps the day.</summary>
    internal List<string> Flush()
    {
        List<string> lines = new List<string>();
        if (_bySkill.Count == 0) return lines;

        List<string> skills = new List<string>(_bySkill.Keys);
        skills.Sort(StringComparer.OrdinalIgnoreCase);
        foreach (string skill in skills)
        {
            Totals totals = _bySkill[skill];
            lines.Add(FormatLine(_day, skill, totals.Gains, totals.Game, totals.Final));
        }
        _bySkill.Clear();
        return lines;
    }

    /// <summary>
    /// The day's average multiplier, weighted by gain size: the total after the mod divided by the
    /// game's own total. 1 when the game's total is not positive, so an empty day never divides by zero.
    /// </summary>
    internal static float AverageMultiplier(float gameTotal, float finalTotal)
    {
        return gameTotal > 0f ? finalTotal / gameTotal : 1f;
    }

    /// <summary>[XpDigest] day N: Skill: n gains, game +X, final +Y (avg xZ). Invariant culture, so a decimal is always a dot.</summary>
    internal static string FormatLine(int day, string skill, int gains, float gameTotal, float finalTotal)
    {
        return string.Format(CultureInfo.InvariantCulture,
            "[XpDigest] day {0}: {1}: {2} {3}, game {4:+0.00;-0.00;+0.00}, final {5:+0.00;-0.00;+0.00} (avg x{6:0.00})",
            day, skill, gains, gains == 1 ? "gain" : "gains", gameTotal, finalTotal, AverageMultiplier(gameTotal, finalTotal));
    }
}
