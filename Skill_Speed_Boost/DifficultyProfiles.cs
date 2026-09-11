using System;
using System.Collections.Generic;

namespace Skill_Speed_Boost;

/// <summary>
/// Named difficulty presets that set the whole tuning stack in one key.
///
/// Before 1.10.0 a profile only wrote SkillExpMultiplier + EnableSkillStaleness, so every
/// feature added after v1.7 (area familiarity, level scaling, morning bonus, skill synergies,
/// and now the daily first-use and well-rested bonuses, and the low-condition penalty) was left
/// untouched and "one key sets everything" was only half true. Every toggle a profile does NOT
/// write is a toggle that silently keeps its previous value, which is what made the old presets
/// misleading - so a profile now writes all seven feature toggles explicitly, including the ones
/// it turns OFF.
///
/// Applied via the ActiveProfile config entry. The multiplier and toggles apply immediately;
/// the staleness change is read at load time, so it takes effect after a save reload.
///
/// Applied ONCE per profile change, not on every launch: Plugin.ApplyProfileOnce compares
/// ActiveProfile against the AppliedProfile latch key, so an individual toggle the player edits
/// after choosing a preset is no longer reverted at the next start (audit M12).
///
/// A preset writes situational bonuses only. It never writes EnablePerSkillMultipliers or any
/// <Skill>_Multiplier, so per-skill overrides survive every preset (audit M14).
/// </summary>
internal static class DifficultyProfiles
{
    private readonly struct Profile
    {
        public readonly int ExpMult;
        public readonly bool Staleness;
        public readonly bool Familiarity;
        public readonly bool LevelScaling;
        public readonly bool Morning;
        public readonly bool Synergies;
        public readonly bool DailyFirstUse;
        public readonly bool WellRested;
        public readonly bool LowCondition;
        public readonly string Summary;

        public Profile(int expMult, bool staleness, bool familiarity, bool levelScaling,
            bool morning, bool synergies, bool dailyFirstUse, bool wellRested, bool lowCondition,
            string summary)
        {
            ExpMult = expMult;
            Staleness = staleness;
            Familiarity = familiarity;
            LevelScaling = levelScaling;
            Morning = morning;
            Synergies = synergies;
            DailyFirstUse = dailyFirstUse;
            WellRested = wellRested;
            LowCondition = lowCondition;
            Summary = summary;
        }
    }

    //                                        exp  stale  famil  lvlSc  morn   syn    1stUse rested lowCond
    private static readonly Dictionary<string, Profile> _profiles
        = new(StringComparer.OrdinalIgnoreCase)
    {
        // Slight boost, vanilla feel: familiarity only, which is the shipped default set.
        ["VanillaPlus"] = new Profile(2,  true,  true,  false, false, false, false, false, false,
            "slight boost, vanilla feel"),
        // Relaxed: no decay and every bonus available.
        ["Casual"]      = new Profile(3,  false, true,  true,  true,  true,  true,  true,  false,
            "relaxed, every bonus on"),
        // Vanilla difficulty: raw game rates for every situational bonus. NOTE: per-skill
        // multipliers are NOT written by any preset, so a <Skill>_Multiplier the player set
        // survives this one (audit M14) - the description string says so too.
        ["Hardcore"]    = new Profile(1,  true,  false, false, false, false, false, false, true,
            "vanilla rates, no bonuses, low-condition penalty on"),
        // Testing / sandbox.
        ["Grinder"]     = new Profile(10, false, true,  true,  true,  true,  true,  true,  false,
            "sandbox, everything on"),
        // Recommended: modest boost, natural decay, the two steady bonuses.
        ["Balanced"]    = new Profile(2,  true,  true,  true,  false, false, false, false, false,
            "modest boost, familiarity and level scaling"),
        // Original vanilla (Hardcore alias, kept for config compatibility).
        ["Legacy"]      = new Profile(1,  true,  false, false, false, false, false, false, true,
            "original vanilla, no bonuses, low-condition penalty on"),
        // No raw multiplier at all: XP only moves faster when the fiction says it should
        // (a place you know well, a fresh morning, a body that is rested and fed).
        ["Immersive"]   = new Profile(1,  true,  true,  false, true,  false, true,  true,  true,
            "no raw multiplier, diegetic bonuses and the low-condition penalty"),
    };

    public static IEnumerable<string> ProfileNames => _profiles.Keys;

    /// <summary>
    /// Writes the named preset's nine values into the config. Returns true only when a preset was
    /// actually applied, so the caller does not latch "applied" for a name that resolved to nothing.
    /// </summary>
    public static bool ApplyProfileSettings(string profileName)
    {
        if (string.IsNullOrEmpty(profileName) || profileName == "None") return false;
        if (!_profiles.TryGetValue(profileName, out var p))
        {
            Plugin.Logger.LogWarning(
                $"[DifficultyProfiles] Unknown profile '{profileName}'; no settings changed. " +
                $"Valid names: None, {string.Join(", ", ProfileNames)}.");
            return false;
        }

        Plugin.SetGlobalExpMultiplier(p.ExpMult);
        Plugin.SetEnableSkillStaleness(p.Staleness);
        Plugin.SetAreaFamiliarityEnabled(p.Familiarity);
        Plugin.SetLevelScalingEnabled(p.LevelScaling);
        Plugin.SetMorningBonusEnabled(p.Morning);
        Plugin.SetSkillSynergiesEnabled(p.Synergies);
        Plugin.SetDailyFirstUseBonusEnabled(p.DailyFirstUse);
        Plugin.SetWellRestedBonusEnabled(p.WellRested);
        Plugin.SetLowConditionPenaltyEnabled(p.LowCondition);

        Plugin.Logger.LogDebug(
            $"[DifficultyProfiles] Applied '{profileName}' ({p.Summary}): ExpMultiplier={p.ExpMult}x, " +
            $"Staleness={p.Staleness}, AreaFamiliarity={p.Familiarity}, LevelScaling={p.LevelScaling}, " +
            $"MorningBonus={p.Morning}, SkillSynergies={p.Synergies}, DailyFirstUse={p.DailyFirstUse}, " +
            $"WellRested={p.WellRested}, LowConditionPenalty={p.LowCondition}. Multiplier and toggles " +
            "apply immediately; the staleness change " +
            "takes effect after reloading a save. Per-skill multipliers are deliberately NOT touched."
        );

        return true;
    }
}
