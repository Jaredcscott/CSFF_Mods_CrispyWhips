using System;
using System.Collections.Generic;
using System.Text;
using CSFFModFramework.Api;
using UnityEngine;

namespace Skill_Speed_Boost;

/// <summary>
/// Tracks skill combo state for the Synergy XP bonus.
/// Performing related skills in sequence stacks SynergyBonusPerAction per action (default +10%), up to
/// SynergyMaxCombo actions (default 5, so +50%). A combo times out after SynergyComboTimeoutMinutes
/// (default 5) without a related action, counted in real minutes (the default, as before 1.11.0) or in
/// in-game minutes (SynergyTimeoutUnit). With SynergyRequireDifferentSkill on, only a change of skill
/// grows a combo: repeating one skill keeps it alive without building it up. Which skills are related is
/// the built-in table below, with any SynergyGroupOverrides entry replacing a skill's row (GroupsOf).
/// </summary>
internal static class SkillSynergies
{
    // The in-game clock counts DaySettings.DailyPoints ticks a day. The framework reads that field by
    // reflection and assumes 96 (15-minute ticks) when it cannot (CSFFModFramework/Api/GameQuery.cs,
    // AssumedDailyPoints) and exposes no accessor for it, so the game-minute timeout assumes it too.
    private const int AssumedDailyPoints = 96;

    // group name -> (current combo count, the skill that last counted in it, time of that action in
    // minutes on the clock SynergyTimeoutUnit selects). Cleared when the unit changes, because a time
    // stored on one clock means nothing on the other. Group names ignore letter case, so a group a player
    // types into SynergyGroupOverrides as 'combat' is the built-in Combat group.
    private static readonly Dictionary<string, (int count, string lastSkill, double time)> _groupCombos
        = new(StringComparer.OrdinalIgnoreCase);

    private static bool _warnedGameClock;

    // Parsed SynergyGroupOverrides and the shipped default parsed the same way. Replaced whole on every
    // re-parse (Plugin.ReloadSynergyGroupOverrides), never edited in place, so a gain reads one complete map.
    private static Dictionary<string, string[]> _groupOverrides = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, string[]> _shippedDefaultOverrides = new(StringComparer.OrdinalIgnoreCase);

    // Override skills already reported as not loaded: one warning per name per session.
    private static readonly HashSet<string> _warnedUnknownOverrides = new(StringComparer.OrdinalIgnoreCase);

    // skill name → synergy groups it belongs to (skills can belong to multiple).
    // Keys are the real GameStat GameName values (verified against
    // Documentation/GameData/CSFF-JsonData_Current .../GameStat/*.json), NOT invented
    // names. 32 stats have UsesNovelty:true; Perception (whose file lacks the "Skill_"
    // prefix) is intentionally absent here — it has no natural synergy group and simply
    // won't participate in combos, though it still gets staleness/XP-multiplier config
    // via GameLoadPatch's dynamic scan. SynergyGroupOverrides can put it (or any mod's skill) in a group.
    private static readonly Dictionary<string, string[]> _skillGroups
        = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Smithing"]        = new[] { "Crafting", "Tools" },
        ["Tailoring"]       = new[] { "Crafting" },
        ["Weaving"]         = new[] { "Crafting" },
        ["Woodworking"]     = new[] { "Crafting", "Tools" },
        ["Leatherworking"]  = new[] { "Crafting" },
        ["Pottery"]         = new[] { "Crafting" },
        ["Knapping"]        = new[] { "Crafting", "Tools" },
        ["Metalworking"]    = new[] { "Crafting", "Tools" },
        ["Crafting"]        = new[] { "Crafting" },
        ["Cooking"]         = new[] { "Crafting", "Survival" },
        ["Herbalism"]       = new[] { "Gathering", "Survival" },
        ["Climbing"]        = new[] { "Survival" },
        ["Swimming"]        = new[] { "Survival" },
        ["Butchering"]      = new[] { "Gathering", "Hunting", "Survival" },
        ["Fishing"]         = new[] { "Gathering", "Hunting" },
        ["Spear Fishing"]   = new[] { "Gathering", "Hunting" },
        ["Trapping"]        = new[] { "Gathering", "Hunting" },
        ["Tracking"]        = new[] { "Hunting" },
        ["Stealth"]         = new[] { "Hunting", "Combat" },
        ["Archery"]         = new[] { "Combat", "Hunting" },
        ["Axe Fighting"]    = new[] { "Combat" },
        ["ClubFighting"]    = new[] { "Combat" },
        ["Knife Fighting"]  = new[] { "Combat" },
        ["Spear Fighting"]  = new[] { "Combat" },
        ["Handguns"]        = new[] { "Combat" },
        ["Rock Throwing"]   = new[] { "Combat" },
        ["Sling"]           = new[] { "Combat" },
        ["Socials"]         = new[] { "Social" },
        ["Insight"]         = new[] { "Social" },
        ["Percussion"]      = new[] { "Performance" },
        ["Wind Instruments"] = new[] { "Performance" },
    };

    /// <summary>
    /// Records this action in all synergy groups the skill belongs to, then returns
    /// the best active synergy multiplier (1.0 = no bonus; 1.5 at the defaults' cap).
    /// Call once per XP-granting action on the main thread (coroutine-safe).
    /// </summary>
    public static float GetAndRecordSynergyBonus(string skillName)
    {
        if (string.IsNullOrEmpty(skillName)) return 1f;
        string[] groups = GroupsOf(skillName);
        if (groups == null || groups.Length == 0) return 1f;

        // Skill disabled via per-skill multiplier → don't participate in combos
        if (Plugin.EnablePerSkillMultipliers
            && SkillConfigManager.GetSkillMultiplier(skillName) == 0)
            return 1f;

        double now = CurrentClockMinutes();
        bool requireDifferent = Plugin.SynergyRequireDifferentSkill;
        double timeout = Plugin.SynergyComboTimeoutMinutes;
        int maxCombo = Plugin.SynergyMaxCombo;
        int bestCount = 0;

        foreach (var group in groups)
        {
            int prevCount = 0;
            string prevSkill = null;
            double elapsed = 0;
            if (_groupCombos.TryGetValue(group, out var state))
            {
                prevCount = state.count;
                prevSkill = state.lastSkill;
                elapsed = now - state.time;
            }

            int newCount = NextComboCount(prevCount, prevSkill, skillName, requireDifferent, elapsed, timeout, maxCombo);
            // A skill in two groups is the last skill of both, and a repeat refreshes the time of
            // every group it belongs to, so holding a combo never lets it time out.
            _groupCombos[group] = (newCount, skillName, now);
            if (newCount > bestCount) bestCount = newCount;
        }

        return ComboMultiplier(bestCount, Plugin.SynergyBonusPerAction);
    }

    /// <summary>
    /// The synergy groups a skill takes part in: its SynergyGroupOverrides entry when it has one, else its
    /// built-in row. Null when neither lists it; empty when an override took it out of every group.
    /// </summary>
    internal static string[] GroupsOf(string skillName)
    {
        return SynergyGroupConfig.ResolveGroups(skillName, _groupOverrides, _skillGroups);
    }

    /// <summary>
    /// Installs a freshly parsed SynergyGroupOverrides (and the shipped default, parsed the same way, which
    /// is exempt from the not-loaded warning), then reports any override skill the game has not loaded.
    /// Combos already running are kept: a skill moved to another group simply counts there from its next
    /// action.
    /// </summary>
    internal static void SetGroupOverrides(Dictionary<string, string[]> overrides, Dictionary<string, string[]> shippedDefault)
    {
        _groupOverrides = overrides ?? new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        _shippedDefaultOverrides = shippedDefault ?? new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        WarnUnknownOverrideSkills();
    }

    /// <summary>
    /// One warning per override skill that no loaded skill has, so a typo in a skill name is visible
    /// instead of silently matching nothing. Waits for GameLoadPatch's skill scan (it is called again when
    /// the scan finishes); before that every skill would look unknown.
    /// </summary>
    internal static void WarnUnknownOverrideSkills()
    {
        if (!Patcher.GameLoadPatch.SkillScanDone) return;
        var unknown = SynergyGroupConfig.UnknownSkills(
            _groupOverrides, Patcher.GameLoadPatch.SkillNamesByUniqueId.Values, _shippedDefaultOverrides);
        foreach (string skill in unknown)
        {
            if (!_warnedUnknownOverrides.Add(skill)) continue;
            Plugin.Logger.LogWarning(
                $"[Synergy] SynergyGroupOverrides: '{skill}' is not a skill this game has loaded, so its entry does " +
                "nothing. Use the skill's name as the game shows it, for example Herbalism, Spear Fighting or ClubFighting.");
        }
    }

    /// <summary>The overrides as the [EffectiveSettings] log shows them, each marked when no loaded skill matches.</summary>
    internal static string DescribeGroupOverrides()
    {
        return SynergyGroupConfig.Describe(
            _groupOverrides, Patcher.GameLoadPatch.SkillScanDone ? Patcher.GameLoadPatch.SkillNamesByUniqueId.Values : null);
    }

    /// <summary>
    /// The combo count after one more action in a group. <paramref name="prevCount"/> 0 means the group
    /// has no combo yet. <paramref name="elapsed"/> and <paramref name="timeout"/> are in the same unit;
    /// an elapsed time that is negative (an earlier save's in-game clock) or not a number (an unreadable
    /// clock) counts as expired, so the combo starts again from one. With
    /// <paramref name="requireDifferent"/> on, the same skill again (any letter case) keeps the count
    /// where it is. Real time never runs backwards, so with it off this is the 1.10.6 formula for every
    /// elapsed time a real-minute clock can produce (compared point by point in
    /// SkillSpeedBoost-BonusLogic.Tests.ps1).
    /// </summary>
    internal static int NextComboCount(int prevCount, string prevSkill, string newSkill,
        bool requireDifferent, double elapsed, double timeout, int max)
    {
        if (max < 1) max = 1;
        if (prevCount <= 0 || !(elapsed >= 0) || elapsed > timeout) return 1;
        if (requireDifferent && string.Equals(prevSkill, newSkill, StringComparison.OrdinalIgnoreCase))
            return Math.Min(prevCount, max);
        return Math.Min(prevCount + 1, max);
    }

    /// <summary>The synergy multiplier for the best combo count across the skill's groups.</summary>
    internal static float ComboMultiplier(int bestCount, float bonusPerAction)
    {
        if (bestCount <= 0) return 1f;
        return 1f + bestCount * bonusPerAction;
    }

    /// <summary>
    /// In-game minutes since the start of day 1, on the game's own tick count: GameManager keeps
    /// CurrentTickInfo.z = (CurrentDay - 1) * DailyPoints + (DailyPoints - DayTimePoints). NaN when the day
    /// or the day-time points cannot be read (GameQuery returns 0 and -1 for those).
    ///
    /// Deliberately NOT CurrentDay * 1440 + HourOfDay * 60: the day counter rolls over at dawn
    /// (DaySettings.DayStartingHour, 04:00) while GameQuery.HourOfDay is the clock hour, so that sum runs
    /// backwards from midnight to 04:00 and jumps a whole day forward at 04:00. The tick count is
    /// continuous: the last tick of a day and the first of the next are the same minute.
    /// </summary>
    internal static double GameMinuteClock(int currentDay, int dayTimePoints, int dailyPoints)
    {
        if (currentDay < 1 || dayTimePoints < 0 || dailyPoints <= 0) return double.NaN;
        long ticks = (long)(currentDay - 1) * dailyPoints + (dailyPoints - dayTimePoints);
        return ticks * (1440.0 / dailyPoints);
    }

    /// <summary>Forgets every combo. Called when SynergyTimeoutUnit changes clock.</summary>
    internal static void ResetCombos()
    {
        _groupCombos.Clear();
    }

    // Minutes on the clock SynergyTimeoutUnit selects. NaN when the in-game clock cannot be read, which
    // NextComboCount treats as expired: an unreadable clock costs the combo, never more than that.
    private static double CurrentClockMinutes()
    {
        if (!Plugin.SynergyUsesGameMinutes)
            return Time.realtimeSinceStartup / 60.0;

        double minutes = GameMinuteClock(GameQuery.CurrentDay, GameQuery.DayTimePoints, AssumedDailyPoints);
        if (double.IsNaN(minutes) && !_warnedGameClock)
        {
            _warnedGameClock = true;
            Plugin.Logger.LogWarning(
                "[Synergy] SynergyTimeoutUnit is GameMinutes but the in-game clock could not be read " +
                $"(GameQuery.CurrentDay {GameQuery.CurrentDay}, DayTimePoints {GameQuery.DayTimePoints}), so every " +
                "combo starts again from one until it can be. RealMinutes does not depend on it.");
        }
        return minutes;
    }

    /// <summary>Returns a short human-readable summary of active combo state.</summary>
    public static string GetSynergyDebugInfo()
    {
        var sb = new StringBuilder();
        double now = CurrentClockMinutes();
        double timeout = Plugin.SynergyComboTimeoutMinutes;
        bool gameMinutes = Plugin.SynergyUsesGameMinutes;
        foreach (var kv in _groupCombos)
        {
            double elapsed = now - kv.Value.time;
            if (!(elapsed >= 0) || elapsed > timeout) continue;
            string age = gameMinutes ? $"{elapsed:F0} game min" : $"{elapsed * 60.0:F0}s";
            sb.Append($"{kv.Key}={kv.Value.count}({age}, {kv.Value.lastSkill}) ");
        }
        return sb.Length > 0 ? sb.ToString().TrimEnd() : "(no active combos)";
    }

}
