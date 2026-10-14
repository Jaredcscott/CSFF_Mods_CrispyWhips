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
/// misleading - so a profile now writes every feature toggle explicitly (the setter list in
/// ApplyProfileSettings), including the ones it turns OFF.
///
/// Applied via the ActiveProfile config entry. Every value it writes takes effect as soon as it is
/// written: the multiplier and the toggles are read on each XP gain, and the staleness change is
/// re-applied to the skill stats by EnableSkillStaleness's SettingChanged handler (Plugin.Awake).
///
/// Applied ONCE per profile change, not on every launch: Plugin.ApplyProfileOnce compares
/// ActiveProfile against the AppliedProfile latch key, so an individual toggle the player edits
/// after choosing a preset is no longer reverted at the next start (audit M12).
///
/// A preset writes situational bonuses only. It never writes EnablePerSkillMultipliers or any
/// <Skill>_Multiplier, so per-skill overrides survive every preset (audit M14). StationBonusMap,
/// SeasonBonusMap (plan C1), EnvironmentBonusMap (plan C2), ToolBonusMap and ActionBonusMap (plan C3) are the same kind of per-install customization and are never written
/// either: an empty map is off, so a map needs no toggle for a preset to flip. Neither are the decay
/// magnitudes, which are tunables like AreaFamiliarityMaxBonus rather than toggles. PerkBonusMap (plan
/// C4) is never written either, but unlike those maps it ships with entries, so it has a switch of its own
/// and that switch IS written (below).
///
/// Night and Streak (1.11.0): Night is ON only in Casual and Grinder, the two presets documented as
/// "every situational bonus on", so that description stays true. Streak is ON in those two and in
/// Immersive, whose bonuses are the ones the fiction explains (practising every day).
///
/// SynergyRequireDifferentSkill (1.11.0, plan B1) is a restriction on the synergy bonus, not a bonus,
/// and every preset writes false: the two presets it suits (Immersive, Hardcore) have synergies off,
/// where it would do nothing, and Casual and Grinder promise every bonus at full strength.
///
/// PerkBonusEnabled (1.11.0, plan C4) is ON in Casual and Grinder (every situational bonus on) and in
/// Immersive, whose bonuses are the ones the fiction explains: a character who completed the Academy's
/// Smithing, Fishing, Herbalism or Woodworking course learns that skill faster. VanillaPlus, Hardcore,
/// Balanced and Legacy keep it off, as they keep every situational bonus off.
///
/// FocusBonusEnabled (1.11.0, plan C5) follows the same placement: ON in Casual, Grinder and Immersive
/// (a focused mind learns faster is a bonus the fiction explains), off in the rest.
///
/// RestedBankEnabled (1.11.0, plan D3) is ON in Casual and Grinder only. Immersive keeps it off: its
/// day streak rewards practising every day, and the bank rewards the opposite, a break.
///
/// MilestoneBonusEnabled (1.11.0, plan D4) is ON in Casual and Grinder only. Immersive keeps it off: a
/// flat burst at a round number is a game reward, not a bonus the fiction explains.
///
/// XpOverflowEnabled (1.11.0, plan E1) is ON in Casual and Grinder only, like the milestone burst: XP
/// passing from a mastered skill to a neighbour is a game convenience the fiction does not explain.
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
        public readonly bool Night;
        public readonly bool Streak;
        public readonly bool SynergyRequireDifferent;
        public readonly bool PerkBonus;
        public readonly bool FocusBonus;
        public readonly bool RestedBank;
        public readonly bool Milestone;
        public readonly bool XpOverflow;
        public readonly string Summary;

        public Profile(int expMult, bool staleness, bool familiarity, bool levelScaling,
            bool morning, bool synergies, bool dailyFirstUse, bool wellRested, bool lowCondition,
            bool night, bool streak, bool synergyRequireDifferent, bool perkBonus, bool focusBonus,
            bool restedBank, bool milestone, bool xpOverflow, string summary)
        {
            RestedBank = restedBank;
            Milestone = milestone;
            XpOverflow = xpOverflow;
            ExpMult = expMult;
            Staleness = staleness;
            Familiarity = familiarity;
            LevelScaling = levelScaling;
            Morning = morning;
            Synergies = synergies;
            DailyFirstUse = dailyFirstUse;
            WellRested = wellRested;
            LowCondition = lowCondition;
            Night = night;
            Streak = streak;
            SynergyRequireDifferent = synergyRequireDifferent;
            PerkBonus = perkBonus;
            FocusBonus = focusBonus;
            Summary = summary;
        }
    }

    //                                        exp  stale  famil  lvlSc  morn   syn    1stUse rested lowCond night  streak synDiff perk   focus  bank   mile   ovfl
    private static readonly Dictionary<string, Profile> _profiles
        = new(StringComparer.OrdinalIgnoreCase)
    {
        // Slight boost, vanilla feel: familiarity only, which is the shipped default set.
        ["VanillaPlus"] = new Profile(2,  true,  true,  false, false, false, false, false, false, false, false,  false, false, false, false, false, false,
            "slight boost, vanilla feel"),
        // Relaxed: no decay and every bonus available.
        ["Casual"]      = new Profile(3,  false, true,  true,  true,  true,  true,  true,  false, true,  true,  false, true, true, true, true, true,
            "relaxed, every bonus on"),
        // Vanilla difficulty: raw game rates for every situational bonus. NOTE: per-skill
        // multipliers are NOT written by any preset, so a <Skill>_Multiplier the player set
        // survives this one (audit M14) - the description string says so too.
        ["Hardcore"]    = new Profile(1,  true,  false, false, false, false, false, false, true,  false, false,  false, false, false, false, false, false,
            "vanilla rates, no bonuses, low-condition penalty on"),
        // Testing / sandbox.
        ["Grinder"]     = new Profile(10, false, true,  true,  true,  true,  true,  true,  false, true,  true,  false, true, true, true, true, true,
            "sandbox, everything on"),
        // Recommended: modest boost, natural decay, the two steady bonuses.
        ["Balanced"]    = new Profile(2,  true,  true,  true,  false, false, false, false, false, false, false,  false, false, false, false, false, false,
            "modest boost, familiarity and level scaling"),
        // Original vanilla (Hardcore alias, kept for config compatibility).
        ["Legacy"]      = new Profile(1,  true,  false, false, false, false, false, false, true,  false, false,  false, false, false, false, false, false,
            "original vanilla, no bonuses, low-condition penalty on"),
        // No raw multiplier at all: XP only moves faster when the fiction says it should
        // (a place you know well, a fresh morning, practising every day, a body that is rested
        // and fed). The rested-XP bank stays off: it rewards a break, the opposite of the streak.
        // The milestone burst stays off too: a reward at a round number is not in the fiction.
        ["Immersive"]   = new Profile(1,  true,  true,  false, true,  false, true,  true,  true,  false, true,  false, true, true, false, false, false,
            "no raw multiplier, diegetic bonuses and the low-condition penalty"),
    };

    public static IEnumerable<string> ProfileNames => _profiles.Keys;

    /// <summary>
    /// Writes the named preset's seventeen values into the config. Returns true only when a preset was
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
        Plugin.SetNightBonusEnabled(p.Night);
        Plugin.SetStreakBonusEnabled(p.Streak);
        Plugin.SetSynergyRequireDifferentSkill(p.SynergyRequireDifferent);
        Plugin.SetPerkBonusEnabled(p.PerkBonus);
        Plugin.SetFocusBonusEnabled(p.FocusBonus);
        Plugin.SetRestedBankEnabled(p.RestedBank);
        Plugin.SetMilestoneBonusEnabled(p.Milestone);
        Plugin.SetXpOverflowEnabled(p.XpOverflow);

        Plugin.Logger.LogDebug(
            $"[DifficultyProfiles] Applied '{profileName}' ({p.Summary}): ExpMultiplier={p.ExpMult}x, " +
            $"Staleness={p.Staleness}, AreaFamiliarity={p.Familiarity}, LevelScaling={p.LevelScaling}, " +
            $"MorningBonus={p.Morning}, NightBonus={p.Night}, SkillSynergies={p.Synergies}, " +
            $"SynergyRequireDifferentSkill={p.SynergyRequireDifferent}, DailyFirstUse={p.DailyFirstUse}, " +
            $"Streak={p.Streak}, WellRested={p.WellRested}, LowConditionPenalty={p.LowCondition}, PerkBonus={p.PerkBonus}, FocusBonus={p.FocusBonus}, RestedBank={p.RestedBank}, Milestone={p.Milestone}, XpOverflow={p.XpOverflow}. All of them, " +
            "staleness included, apply immediately. Per-skill multipliers are deliberately NOT touched."
        );

        return true;
    }
}
