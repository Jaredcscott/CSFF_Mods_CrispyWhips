using System;
using System.Collections;
using System.Reflection;
using BepInEx.Logging;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using HarmonyLib;

namespace Skill_Speed_Boost.Patcher;

/// <summary>
/// Single ChangeStatValue postfix that applies every runtime XP modifier: the global and
/// per-skill SkillExpMultiplier, the morning and night bonuses, area familiarity, the station
/// bonus, the season bonus, synergies, level scaling, the daily first-use bonus, the day streak, the rested-XP bank, the well-rested
/// bonus, the In the Flow (Focus) bonus, and the one term that can push the total BELOW 1 - the low-condition penalty,
/// then the one ADDITIVE term, the milestone burst, after the composed write, and last the XP overflow redirect
/// (plan E1), which hands the part of a gain the game's clamp ate to a related skill. While XpReadoutEnabled is on it
/// also names the terms that moved the multiplier and hands them to XpReadout (plan E3, a diagnostic, not a bonus). Multiple iterator postfixes on
/// the same coroutine cannot compose — only the first wrapper consumes the original
/// enumerator and observes the delta — so every dynamic skill bonus must be combined here.
/// </summary>
internal static class MorningBonusPatch
{
    private static ManualLogSource Logger => Plugin.Logger;
    private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    // CurrentBaseValue is the one field every XP measurement and write uses. Only a Permanent change
    // reaches them (IsPermanentModification), and vanilla applies a Permanent change to the base alone,
    // clamped against CurrentMinMaxValue (GameManager.ChangeStatValue). The displayed value
    // (SimpleCurrentValue) adds every modifier and clamps the SUM, so while a penalty holds a skill at
    // its minimum it does not move when XP lands: "Animals noticed your Actions" puts -75 on Stealth,
    // and until 1.10.4 a hunt's Stealth XP under it read as no gain at all. Resolved once, from the
    // first stat seen; every InGameStat shares the type.
    private static FieldInfo _currentBaseValueField;
    private static bool _baseFieldReflected;
    private static MethodInfo _invalidateConditionValues;
    private static bool _invalidateReflected;

    // D17: every failure on the XP path warns the FIRST time each cause occurs, then stays quiet. These
    // run on every skill-stat change, so unconditional logging would flood LogOutput.log; keying per
    // cause means a second, different failure still gets its own line instead of hiding behind the first.
    private static readonly HashSet<string> _warnedCauses = new(StringComparer.Ordinal);
    private static bool _clockReadFailureLogged;
    private static bool _capHitLogged;

    // Daily first-use ledger: which skills have already taken their once-a-day bonus, and the
    // in-game day that set belongs to. In memory only, by design - this is a small pacing
    // nudge, not save state, and re-arming it on reload costs the player nothing. If
    // GameQuery.CurrentDay is unreadable it returns a constant 0, in which case the set never
    // rolls over and the bonus degrades to once per skill per SESSION rather than firing on
    // every single gain.
    private static readonly HashSet<string> _firstUseSkills = new(StringComparer.OrdinalIgnoreCase);
    private static int _firstUseDay = int.MinValue;

    private static int _statArgIndex = 0;
    private static int _modificationArgIndex = 2;
    // ChangeStatValue's requested value (_Value): what the game ASKED to add, before its clamp into
    // CurrentMinMaxValue. Read only by XP overflow (plan E1). -1 when no float parameter named like a
    // value was found, which XP overflow reports once and then skips.
    private static int _valueArgIndex = 1;

    // How many GameManager.ApplyPerk calls are on the stack right now. ApplyPerkPrefix raises it and
    // ApplyPerkFinalizer lowers it. A finalizer runs even when ApplyPerk throws, so a failed grant
    // cannot leave it raised and every later gain unscaled. Read by IsRunSetup.
    internal static int PerkGrantDepth;

    public static void ApplyPatch(Harmony harmony)
    {
        try
        {
            var gmType = Reflect.TryGetType("GameManager");
            if (gmType == null)
            {
                Logger.LogWarning("[MorningBonus] GameManager type not found — morning bonus disabled.");
                return;
            }

            var method = FindChangeStatValue(gmType);
            if (method == null)
            {
                Logger.LogWarning("[MorningBonus] GameManager.ChangeStatValue not found — morning bonus disabled.");
                return;
            }

            harmony.Patch(method, postfix: new HarmonyMethod(typeof(MorningBonusPatch), nameof(ChangeStat_Post)));
            PatchApplyPerk(harmony, gmType);
        }
        catch (Exception ex)
        {
            Logger.LogError($"[MorningBonus] Patch error: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }

    /// <summary>
    /// A perk's StartingStatModifiers reach ChangeStatValue as Permanent changes through
    /// GameManager.ApplyPerk, which every in-run grant calls: the game's own AddedInRunPerks action
    /// branch, the mods that grant a perk mid-run by reflection (Sirus23's Pack Bond, CMC's graduate
    /// perks) and the harness's cheat/perk. A prefix and a finalizer around
    /// it mark that window. ApplyPerk is PRIVATE (root CLAUDE.md "Perks"), so it is resolved with
    /// AccessTools.Method: a public-only lookup returns null and the exemption would vanish without a
    /// word. A version that made it a coroutine would run the stat changes after the finalizer, so a
    /// non-void ApplyPerk is refused with a warning rather than patched into a guard that never holds.
    /// </summary>
    private static void PatchApplyPerk(Harmony harmony, Type gmType)
    {
        try
        {
            var applyPerk = AccessTools.Method(gmType, "ApplyPerk");
            if (applyPerk == null)
            {
                Logger.LogWarning("[MorningBonus] GameManager.ApplyPerk not found - a perk granted during a run has its starting skill bonus scaled as XP. The game has probably renamed it.");
                return;
            }
            if (applyPerk.ReturnType != typeof(void))
            {
                Logger.LogWarning($"[MorningBonus] GameManager.ApplyPerk now returns {applyPerk.ReturnType.Name}, not void - a perk granted during a run has its starting skill bonus scaled as XP. The game has probably changed how it applies perks.");
                return;
            }

            harmony.Patch(applyPerk,
                prefix: new HarmonyMethod(typeof(MorningBonusPatch), nameof(ApplyPerkPrefix)),
                finalizer: new HarmonyMethod(typeof(MorningBonusPatch), nameof(ApplyPerkFinalizer)));
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[MorningBonus] Patching GameManager.ApplyPerk failed - a perk granted during a run has its starting skill bonus scaled as XP: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }

    static void ApplyPerkPrefix()
    {
        PerkGrantDepth++;
    }

    // Returning the exception unchanged keeps ApplyPerk's own failure behaviour; this only lowers the
    // depth. The floor at 0 covers a prefix that never ran (another plugin's prefix threw first).
    static Exception ApplyPerkFinalizer(Exception __exception)
    {
        if (PerkGrantDepth > 0) PerkGrantDepth--;
        return __exception;
    }

    // IEnumerator coroutine postfix: yield the original first, then inspect the delta.
    static IEnumerator ChangeStat_Post(IEnumerator enumerator, object __instance, object[] __args)
    {
        object stat = GetArg(__args, _statArgIndex);
        object modification = GetArg(__args, _modificationArgIndex);

        bool morningOn = Plugin.MorningBonusEnabled;
        bool familiarityOn = Plugin.AreaFamiliarityEnabled;
        bool expMultOn = Plugin.SkillExpMultiplier > 1 || Plugin.EnablePerSkillMultipliers;
        bool synergiesOn = Plugin.SkillSynergiesEnabled;
        bool levelScalingOn = Plugin.LevelScalingEnabled;
        bool firstUseOn = Plugin.DailyFirstUseBonusEnabled;
        bool wellRestedOn = Plugin.WellRestedBonusEnabled;
        bool lowConditionOn = Plugin.LowConditionPenaltyEnabled;
        bool nightOn = Plugin.NightBonusEnabled;
        bool stationBonusOn = Plugin.StationBonusEnabled;
        bool streakOn = Plugin.StreakBonusEnabled;
        bool seasonOn = Plugin.SeasonBonusEnabled;
        bool envOn = Plugin.EnvironmentBonusEnabled;
        bool toolOn = Plugin.ToolBonusEnabled;
        bool actionOn = Plugin.ActionBonusEnabled;
        bool perkOn = Plugin.PerkBonusEnabled;
        bool flowOn = Plugin.FocusBonusEnabled;
        bool bankOn = Plugin.RestedBankEnabled;
        bool milestoneOn = Plugin.MilestoneBonusEnabled;
        bool overflowOn = Plugin.XpOverflowEnabled;

        // Every bonus this postfix can apply must be represented here. A bonus missing from
        // this list is silently dead whenever it is the ONLY one enabled, with no log to say so.
        // XP overflow (plan E1) above all: an overflow-only config runs exactly this path.
        if (stat == null || (!morningOn && !familiarityOn && !expMultOn && !synergiesOn
            && !levelScalingOn && !firstUseOn && !wellRestedOn && !lowConditionOn
            && !nightOn && !stationBonusOn && !streakOn && !seasonOn && !envOn && !toolOn && !actionOn && !perkOn
            && !flowOn && !bankOn && !milestoneOn && !overflowOn
            && !Plugin.LogSkillXpGains && !Plugin.LogDailyXpSummary))
        {
            yield return enumerator;
            yield break;
        }

        // Only a Permanent change is XP. Every other StatModification adds or removes a MODIFIER (a
        // card's passive effect, a stat status, a time-of-day mod, an action's temporary modifier),
        // and the game applies each one again, inverted, when it ends. A lit campfire takes 150
        // Stealth through AtBaseModifier; putting it out hands the 150 back, the value read below
        // rose, and the bonus on that "gain" was written into AtBaseModifiedValue, where it stayed
        // until InGameStat.Init zeroed the modifier fields on the next load.
        if (!IsPermanentModification(modification))
        {
            yield return enumerator;
            yield break;
        }

        // A run's starting stat modifiers (gamemode, character, and every perk's StartingStatModifiers)
        // are Permanent changes too, applied by InitializeStatsAndActions inside GameManager.Awake while
        // IsInitializing is true. They are not XP: scaling them turned a perk's +75 Herbalism into 150
        // on an 8x config (walkthrough T1.118). No skill is practised while a run is initializing.
        // A perk granted DURING a run applies its StartingStatModifiers the same way, through
        // GameManager.ApplyPerk, after IsInitializing has cleared, so IsRunSetup also skips while an
        // ApplyPerk call is on the stack (PerkGrantDepth). This read is in the wrapper's FIRST
        // MoveNext, which the game's inline coroutine runner executes inside ApplyPerk's own call
        // (SkillSpeedBoost plan W0.1), so the depth is still raised when it is read.
        if (IsRunSetup(__instance))
        {
            yield return enumerator;
            yield break;
        }

        // Measure on the base (see _currentBaseValueField). A base that cannot be read has nothing to
        // measure against and nothing to write to, so the change passes through unscaled, and
        // TryGetBaseValue has already said why, once per cause.
        if (!TryGetBaseValue(stat, out float beforeBase))
        {
            yield return enumerator;
            yield break;
        }

        yield return enumerator;                 // let original coroutine run

        if (!TryGetBaseValue(stat, out float afterBase)) yield break;

        float delta = afterBase - beforeBase;

        // Not an XP gain, or already at the maximum. With XP overflow on (plan E1), a skill gain the clamp
        // ate whole (the skill was at its maximum) is redirected to a group-mate first; with it off this
        // is the 1.11.0 exit exactly.
        if (delta <= 0f)
        {
            if (overflowOn) RedirectOverflow(stat, __args, 0f, Plugin.XpOverflowFraction);
            yield break;
        }
        if (!IsSkillStat(stat)) yield break;     // not a tracked skill

        // Resolve skill identity once — shared by expMult, synergies, and debug logging.
        var uid = StatAccess.GetUniqueId(stat);
        string resolvedSkillName = null;
        if (!string.IsNullOrEmpty(uid))
            GameLoadPatch.SkillNamesByUniqueId.TryGetValue(uid, out resolvedSkillName);

        // Compose multipliers from every active dynamic bonus.
        float multiplier = 1f;

        // The XP readout toast (plan E3) names the terms below that moved the multiplier. A diagnostic,
        // not a bonus, so it has no hoist and no guard term: with XpReadoutEnabled off this is one bool
        // test, nothing is allocated, and every 'readout?.Note' beside a term below skips its arguments.
        XpReadout.Terms readout = Plugin.XpReadoutEnabled ? new XpReadout.Terms() : null;

        // Global / per-skill SkillExpMultiplier — replaces the old load-time graph rewrite.
        if (expMultOn)
        {
            int expMult = Plugin.SkillExpMultiplier;
            if (Plugin.EnablePerSkillMultipliers && !string.IsNullOrWhiteSpace(resolvedSkillName))
                expMult = SkillConfigManager.GetSkillMultiplier(resolvedSkillName);

            if (expMult == 0)
            {
                // Per-skill set to 0 -> "disable skill leveling". Put the base back where it was before
                // the original coroutine applied the gain. Other bonuses (morning, familiarity)
                // intentionally don't apply when XP is disabled for this skill.
                bool reverted = TrySetBaseValue(stat, beforeBase);
                if (Plugin.LogSkillXpGains)
                    LogXpGain(resolvedSkillName ?? uid, delta, 0f, beforeBase, afterBase, beforeBase, !reverted, AreaFamiliarityPatch.CurrentLocationUid);
                if (Plugin.LogDailyXpSummary)
                    XpDigest.Record(resolvedSkillName ?? uid, delta, (reverted ? beforeBase : afterBase) - beforeBase);
                yield break;
            }
            if (expMult > 1) multiplier *= expMult;
            readout?.Note(multiplier, "x" + expMult + " setting");
        }

        // MorningAppliesTo and NightAppliesTo (plan D1) only narrow these two terms to the skills
        // MorningBonusSkills and NightBonusSkills list; an empty list, the default, is every skill.
        if (morningOn && IsMorningWindow() && Plugin.MorningAppliesTo(resolvedSkillName))
            multiplier *= Plugin.MorningBonusMultiplier;
        readout?.Note(multiplier, "morning");

        // Same clock as the morning window (TryGetCurrentHour). If a player overlaps the two
        // windows, both multiply, like every other pair of bonuses here: stacking, not arbitration.
        if (nightOn && IsNightWindow() && Plugin.NightAppliesTo(resolvedSkillName))
            multiplier *= Plugin.NightBonusMultiplier;
        readout?.Note(multiplier, "night");

        if (familiarityOn)
        {
            var locUid = AreaFamiliarityPatch.CurrentLocationUid;
            if (!string.IsNullOrEmpty(locUid))
            {
                multiplier *= AreaFamiliarityService.GetMultiplier(locUid);
                readout?.Note(multiplier, "area familiarity", true);
                // Mark this action as having gained skill XP so the ActionRoutine postfix
                // increments the visit counter exactly once per action.
                AreaFamiliarityPatch.NoteSkillXpGained();
            }
        }

        // A structure the player named in StationBonusMap. It reads the same placed-structure UID
        // familiarity reads, which AreaFamiliarityPatch tracks whenever EITHER feature is on, so
        // this still works with familiarity switched off.
        if (stationBonusOn)
        {
            var stationUid = AreaFamiliarityPatch.CurrentLocationUid;
            if (!string.IsNullOrEmpty(stationUid))
                multiplier *= Plugin.GetStationBonusMultiplier(stationUid);
        }
        readout?.Note(multiplier, "station");

        // The current season's entry in SeasonBonusMap for this skill, or its every-skill entry.
        if (seasonOn && !string.IsNullOrWhiteSpace(resolvedSkillName))
            multiplier *= Plugin.GetSeasonBonusMultiplier(resolvedSkillName);
        readout?.Note(multiplier, "season");

        // The most specific EnvironmentBonusMap place that names where the player is. Read from the
        // current environment's card directly, not through the ActionRoutine structure bridge.
        if (envOn && !string.IsNullOrWhiteSpace(resolvedSkillName))
            multiplier *= Plugin.GetEnvironmentBonusMultiplier(resolvedSkillName);
        readout?.Note(multiplier, "place");

        // The tool and the action of the running action, captured by AreaFamiliarityPatch around
        // ActionRoutine (plan C3). Each is the highest matching entry, never a product of its keys.
        if (toolOn && !string.IsNullOrWhiteSpace(resolvedSkillName))
            multiplier *= Plugin.GetToolBonusMultiplier(resolvedSkillName);
        readout?.Note(multiplier, "tool");

        if (actionOn && !string.IsNullOrWhiteSpace(resolvedSkillName))
            multiplier *= Plugin.GetActionBonusMultiplier(resolvedSkillName);
        readout?.Note(multiplier, "action");

        // The highest PerkBonusMap entry among the mapped perks the player holds, picked at character
        // creation or granted during the run (plan C4). The higher perk wins; two never multiply.
        if (perkOn && !string.IsNullOrWhiteSpace(resolvedSkillName))
            multiplier *= Plugin.GetPerkBonusMultiplier(resolvedSkillName);
        readout?.Note(multiplier, "graduate perk");

        if (synergiesOn && !string.IsNullOrWhiteSpace(resolvedSkillName))
        {
            float synergyMult = SkillSynergies.GetAndRecordSynergyBonus(resolvedSkillName);
            if (synergyMult > 1f)
            {
                multiplier *= synergyMult;
                readout?.Note(multiplier, "synergy combo");
                if (Plugin.SkillSynergiesDebugLog)
                    Logger.LogInfo($"[Synergy] {resolvedSkillName}: {synergyMult:F2}x | {SkillSynergies.GetSynergyDebugInfo()}");
            }
        }

        // A maximum that cannot be read has nothing to cap the composed write at, and an uncapped write
        // can push a skill past its maximum, so the game's own gain stands instead (no write, no
        // overflow, no milestone). StatAccess.GetMaxValue returns NaN without throwing, so warn here.
        float maxVal = StatAccess.GetMaxValue(stat);
        if (float.IsNaN(maxVal))
        {
            WarnOnce("max-unreadable", $"The maximum of skill '{resolvedSkillName ?? uid}' could not be read (CurrentMinMaxValue, MaxValue and the model's MinMaxValue all failed, likely a game update). Its XP gains keep the game's own amount and no bonus applies to them.");
            yield break;
        }

        if (levelScalingOn && maxVal > 0f)
        {
            // Fraction of max level already achieved (0.0 = just started, 1.0 = fully maxed), read from
            // the trained base after this XP tick, not from a displayed value a penalty may be holding down.
            float levelFraction = Math.Min(afterBase / maxVal, 1f);
            float levelBonus = Plugin.LevelScalingMaxBonus * ShapeLevelFraction(levelFraction);
            if (levelBonus > 0f)
                multiplier *= (1f + levelBonus);
            readout?.Note(multiplier, "skill level");
        }

        // First gain for this skill today. Claimed only after the gain has already been
        // confirmed as real skill XP, and after the per-skill "disabled" early-out above, so a
        // skill set to 0x never burns its daily claim. The same per-skill key keys the rested bank and the milestone marks.
        string firstUseKey = !string.IsNullOrWhiteSpace(resolvedSkillName) ? resolvedSkillName : uid;
        // A low-condition penalty of 0x means no XP at all: do not burn the once-a-day claims below for a gain
        // that is about to be zeroed (the penalty term itself is applied further down).
        bool penaltyZeroesGain = lowConditionOn && Plugin.LowConditionMultiplier <= 0f
            && PlayerConditionService.IsAnyConditionBelow(Plugin.LowConditionThreshold);
        if (firstUseOn && !penaltyZeroesGain)
        {
            if (!string.IsNullOrEmpty(firstUseKey) && ClaimFirstUseOfDay(firstUseKey))
                multiplier *= Plugin.DailyFirstUseMultiplier;
            readout?.Note(multiplier, "first use today");
        }

        // Consecutive in-game days with skill XP. The day is claimed here, after the gain is
        // confirmed real and after the per-skill 0x early-out, the same point first-use claims
        // at, so today already counts as day 1 of a fresh streak.
        if (streakOn && !penaltyZeroesGain)
        {
            int streakDays = SkillStreakService.NoteActivity(GameQuery.CurrentDay);
            float streakBonus = SkillStreakService.StreakBonus(streakDays, Plugin.StreakBonusMaxDays, Plugin.StreakBonusPerDay);
            if (streakBonus > 0f)
                multiplier *= 1f + streakBonus;
            readout?.Note(multiplier, streakDays + "-day streak");
        }

        // The rested-XP bank (plan D3): full days this skill went unused bank gains, and this gain
        // spends one. Claimed at the same point as first use and the streak, after the per-skill 0x
        // early-out, so a skill set to 0x never spends its bank.
        if (bankOn && !penaltyZeroesGain) multiplier *= RestedBankService.Consume(firstUseKey, GameQuery.CurrentDay);
        readout?.Note(multiplier, "rested bank");

        if (wellRestedOn && PlayerConditionService.IsWellRested(Plugin.WellRestedThreshold))
            multiplier *= Plugin.WellRestedMultiplier;
        readout?.Note(multiplier, "well rested");

        // Focus in its top band, In the Flow, read from Focus's own statuses (plan C5).
        if (flowOn && PlayerConditionService.IsInFlow())
            multiplier *= Plugin.FocusBonusMultiplier;
        readout?.Note(multiplier, "in the flow");

        // The one PENALTY in the stack: a sub-1.0 factor while any condition stat is low. It is
        // the reason the write below has to be direction-aware - every other term here can only
        // ever push the multiplier up.
        if (lowConditionOn && PlayerConditionService.IsAnyConditionBelow(Plugin.LowConditionThreshold))
            multiplier *= Plugin.LowConditionMultiplier;
        readout?.Note(multiplier, "low condition");

        // Ceiling on the composed total. Every bonus above is multiplicative, so a player who
        // turns several on at once can reach a figure none of the individual sliders suggests
        // (10x global by 1.5 morning by 1.3 familiarity by 1.5 synergy by 1.5 level scaling is
        // already over 65x). Default 0 keeps the old uncapped behaviour untouched.
        float cap = Plugin.MaxComposedMultiplier;
        if (cap > 0f && multiplier > cap)
        {
            if (Plugin.LogMultiplierCapHits)
            {
                Logger?.LogInfo(
                    $"[MultiplierCap] {resolvedSkillName ?? uid ?? "(unresolved skill)"}: " +
                    $"{multiplier:F2}x exceeded the cap and was clamped to {cap:F2}x.");
            }
            else if (!_capHitLogged)
            {
                _capHitLogged = true;
                Logger?.LogDebug(
                    $"[MultiplierCap] first clamp this session ({multiplier:F2}x -> {cap:F2}x). " +
                    "Set LogMultiplierCapHits=true to log every hit.");
            }
            multiplier = cap;
            readout?.Note(multiplier, "capped");
        }

        // A composed multiplier BELOW 1 is a penalty (low-condition suppression), so this cannot skip
        // on "not a bonus" the way it did while every term could only push the total up - that would
        // have discarded the whole penalty silently. Only a multiplier of exactly 1 (within float noise)
        // is a no-op, and it still reaches the diagnostic below, so a gain handled at 1x and a gain
        // never handled read differently in the log.
        float target = afterBase;
        bool writeFailed = false;
        if (Math.Abs(multiplier - 1f) > 0.0001f)
        {
            target = ComposeBaseTarget(beforeBase, afterBase, multiplier, maxVal);
            if (Math.Abs(target - afterBase) > 0.0001f)
                writeFailed = !TrySetBaseValue(stat, target);
        }

        // The milestone burst (plan D4): ADDITIVE, after the composed write, so no multiplier scales
        // it and MaxComposedMultiplier does not cap it; the [XpGain] line below reports the base with
        // it. A breakpoint is measured on the trained base the gain ended at, and pays once per
        // character (MilestoneService keeps the mark per save slot). Skipped when the write above
        // failed, since a second write to the same field would fail too. A skill set to 0x never gets
        // here: the 0x early-out has already returned.
        if (milestoneOn && !writeFailed && !string.IsNullOrEmpty(firstUseKey))
        {
            float reached = target;
            if (MilestoneService.TryAward(firstUseKey, beforeBase, reached, out int milestoneIndex, out int milestoneAt, out float burst))
            {
                float burstTarget = Math.Min(reached + burst, maxVal);
                bool paid = burstTarget > reached + 0.0001f && TrySetBaseValue(stat, burstTarget);
                if (paid) target = burstTarget;
                if (Plugin.LogSkillXpGains)
                    Logger?.LogInfo(
                        $"[Milestone] {firstUseKey}: reached {milestoneAt} (breakpoint {milestoneIndex}), " +
                        (paid ? $"+{burstTarget - reached:F2} XP, base {reached:F2} -> {burstTarget:F2}" : $"no burst (base {reached:F2} is at its maximum or the write failed)"));
            }
        }

        if (Plugin.LogSkillXpGains)
            LogXpGain(resolvedSkillName ?? uid, delta, multiplier, beforeBase, afterBase, target, writeFailed, AreaFamiliarityPatch.CurrentLocationUid);

        // LogDailyXpSummary: the same gain, into one line per skill per in-game day (XpDigest). Beside
        // every LogXpGain call, so the digest totals exactly the gains the XP log reports, and "final"
        // is the value LogXpGain reports as the base after (the game's own gain when the write failed).
        if (Plugin.LogDailyXpSummary)
            XpDigest.Record(resolvedSkillName ?? uid, delta, (writeFailed ? afterBase : target) - beforeBase);

        // The XP readout toast (plan E3): shown when the set of terms changed, or the same set after
        // XpReadoutMinSeconds. After the write and the logs, so it can never change what a gain gained.
        if (readout != null) XpReadout.Notify(resolvedSkillName ?? uid, multiplier, readout, Plugin.XpReadoutMinSeconds);

        // XP overflow (plan E1), after everything above: a gain that crossed the cap part way landed only
        // delta of what was requested, and the rest goes to a group-mate. Flat, so it is measured on the
        // game's own request and gain, never on the composed target.
        if (overflowOn) RedirectOverflow(stat, __args, delta, Plugin.XpOverflowFraction);
    }

    /// <summary>
    /// XP overflow (plan E1): hands the excess of one Permanent skill gain, the value ChangeStatValue was
    /// asked to add minus <paramref name="gained"/>, to XpOverflowService. Nothing happens unless the
    /// request was a gain, the excess is more than float noise, and the stat is a skill. Called only
    /// while XpOverflowEnabled is on, so with it off ChangeStat_Post does exactly what 1.11.0 did.
    /// </summary>
    private static void RedirectOverflow(object stat, object[] args, float gained, float fraction)
    {
        if (!TryGetRequestedValue(args, out float requested)) return;
        if (!(requested - Math.Max(gained, 0f) > 0.0001f)) return;
        if (!IsSkillStat(stat)) return;

        var uid = StatAccess.GetUniqueId(stat);
        string skillName = null;
        if (!string.IsNullOrEmpty(uid))
            GameLoadPatch.SkillNamesByUniqueId.TryGetValue(uid, out skillName);
        if (string.IsNullOrWhiteSpace(skillName))
        {
            WarnOnce("overflow-unnamed:" + (uid ?? "null"),
                $"XP overflow: a skill stat ({uid ?? "no UniqueID"}) has no name from the load scan, so its excess XP has no group to go to.");
            return;
        }

        XpOverflowService.Route(skillName, requested, gained, fraction);
    }

    /// <summary>
    /// ChangeStatValue's requested value, by the parameter index FindChangeStatValue resolved by type and
    /// name. False, with one warning per cause, when it cannot be read: XP overflow then does nothing.
    /// </summary>
    private static bool TryGetRequestedValue(object[] args, out float requested)
    {
        requested = 0f;
        if (_valueArgIndex < 0)
        {
            WarnOnce("value-arg-missing", "ChangeStatValue has no float parameter named like a value, so XP overflow cannot see what a gain asked for and redirects nothing. The game's signature has probably changed.");
            return false;
        }
        object raw = GetArg(args, _valueArgIndex);
        if (raw is float value)
        {
            requested = value;
            return true;
        }
        WarnOnce("value-arg-type", $"ChangeStatValue's value argument read as {raw?.GetType().Name ?? "null"}, not a float, so XP overflow redirects nothing.");
        return false;
    }

    // ── Bonus shaping helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Reshapes the 0-1 "how close to max level" fraction that level scaling multiplies by.
    /// Linear is the shipped behaviour. EaseIn (f squared) stays small until a skill is well
    /// along and then climbs, so it pays for pushing a skill toward its ceiling. EaseOut
    /// (the mirror) climbs immediately and flattens, so it helps low-level skills most.
    /// An unrecognised value falls through to Linear rather than disabling the feature.
    /// </summary>
    private static float ShapeLevelFraction(float fraction)
    {
        switch (Plugin.LevelScalingCurve)
        {
            case "EaseIn":
                return fraction * fraction;
            case "EaseOut":
                float inverse = 1f - fraction;
                return 1f - inverse * inverse;
            default:
                return fraction;
        }
    }

    /// <summary>
    /// Returns true exactly once per skill per in-game day. Rolls the ledger over whenever the
    /// game's day counter changes, in either direction, so loading an earlier save re-arms the
    /// bonus rather than stranding it.
    /// </summary>
    private static bool ClaimFirstUseOfDay(string skillKey)
    {
        int day = GameQuery.CurrentDay;
        if (day != _firstUseDay)
        {
            _firstUseDay = day;
            _firstUseSkills.Clear();
        }
        return _firstUseSkills.Add(skillKey);
    }

    // ── Reflection helpers ────────────────────────────────────────────────────

    private static MethodInfo FindChangeStatValue(Type gmType)
    {
        foreach (var method in gmType.GetMethods(Flags))
        {
            if (method.Name != "ChangeStatValue" || !typeof(IEnumerator).IsAssignableFrom(method.ReturnType))
                continue;

            var parameters = method.GetParameters();
            if (parameters.Length < 3) continue;

            _statArgIndex = 0;
            _modificationArgIndex = 2;
            _valueArgIndex = -1;
            for (int i = 0; i < parameters.Length; i++)
            {
                var name = parameters[i].Name ?? string.Empty;
                var typeName = parameters[i].ParameterType.Name ?? string.Empty;
                if (name.IndexOf("stat", StringComparison.OrdinalIgnoreCase) >= 0
                    || typeName.IndexOf("InGameStat", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _statArgIndex = i;
                }
                else if (name.IndexOf("modification", StringComparison.OrdinalIgnoreCase) >= 0
                    || typeName.IndexOf("StatModification", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _modificationArgIndex = i;
                }
                else if (parameters[i].ParameterType == typeof(float)
                    && name.IndexOf("value", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _valueArgIndex = i;
                }
            }

            return method;
        }

        return null;
    }

    private static object GetArg(object[] args, int index)
    {
        return args != null && index >= 0 && index < args.Length ? args[index] : null;
    }

    /// <summary>
    /// The base value a gain of (afterBase - beforeBase) ends at once the composed multiplier is
    /// applied: never above the stat's maximum, and never below where the base started, because a
    /// penalty scales down THIS gain and never claws back XP the player already had. The configured
    /// multiplier range cannot breach that floor, so it is a guarantee rather than arithmetic.
    /// Pure, with no game types, so SkillSpeedBoost-BonusComposition.Tests.ps1 compiles and runs it:
    /// keep the body C# 5, which is what PowerShell 5.1's Add-Type compiles.
    /// </summary>
    internal static float ComposeBaseTarget(float beforeBase, float afterBase, float multiplier, float maxValue)
    {
        // Signed: below afterBase whenever the penalty outweighs the bonuses. afterBase plus this term
        // is beforeBase + gain * multiplier either way, so one expression serves both directions.
        float target = Math.Min(afterBase + (afterBase - beforeBase) * (multiplier - 1f), maxValue);
        if (target < beforeBase) target = beforeBase;
        return target;
    }

    private static bool TryResolveBaseField(object stat)
    {
        if (!_baseFieldReflected)
        {
            _baseFieldReflected = true;
            _currentBaseValueField = stat.GetType().GetField("CurrentBaseValue", Flags);
        }
        if (_currentBaseValueField != null) return true;

        WarnOnce("CurrentBaseValue-missing",
            $"{stat.GetType().Name}.CurrentBaseValue did not resolve - skill XP now passes through unscaled, so every XP " +
            "setting in this mod is off. The game's stat field names have probably changed in an update.");
        return false;
    }

    /// <summary>
    /// Reads CurrentBaseValue. False, with one warning per cause, when it cannot be read; the caller
    /// then leaves the change exactly as the game applied it.
    /// </summary>
    internal static bool TryGetBaseValue(object stat, out float value)
    {
        value = 0f;
        if (!TryResolveBaseField(stat)) return false;
        try
        {
            object raw = _currentBaseValueField.GetValue(stat);
            if (raw == null)
            {
                WarnOnce("CurrentBaseValue-null", "CurrentBaseValue read back as null - skill XP now passes through unscaled.");
                return false;
            }
            value = Convert.ToSingle(raw);
            return true;
        }
        catch (Exception ex)
        {
            WarnOnce("CurrentBaseValue-read", $"Reading CurrentBaseValue failed - skill XP now passes through unscaled: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Writes CurrentBaseValue. False, with one warning per cause, when the write fails; the gain the
    /// game applied then stands unscaled.
    /// </summary>
    internal static bool TrySetBaseValue(object stat, float value)
    {
        if (!TryResolveBaseField(stat)) return false;
        try
        {
            _currentBaseValueField.SetValue(stat, value);
            InvalidateConditionCache(stat);
            return true;
        }
        catch (Exception ex)
        {
            WarnOnce("CurrentBaseValue-write", $"Writing CurrentBaseValue failed - XP bonuses are being dropped and the game's own gain kept: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// EA 0.69 caches each stat's value for conditions (skill gates on actions, dialog, blueprints)
    /// and only the game's own stat changes clear it, so a raw CurrentBaseValue write must clear it
    /// too or a condition read between the game's write and ours keeps the unscaled value. Called by
    /// reflection so this mod needs no newer framework; builds before 0.69 have no such method and no
    /// cache, which the warning says.
    /// </summary>
    private static void InvalidateConditionCache(object stat)
    {
        if (!_invalidateReflected)
        {
            _invalidateReflected = true;
            _invalidateConditionValues = stat.GetType().GetMethod("InvalidateConditionValues", Flags, null, Type.EmptyTypes, null);
        }
        if (_invalidateConditionValues == null)
        {
            WarnOnce("InvalidateConditionValues-missing",
                $"{stat.GetType().Name}.InvalidateConditionValues did not resolve - harmless before game EA 0.69; on 0.69 or later, " +
                "skill gates may read a skill's level from before this mod's XP change until the skill next changes.");
            return;
        }
        try { _invalidateConditionValues.Invoke(stat, null); }
        catch (Exception ex)
        {
            // InnerException first: Invoke wraps the real cause in a TargetInvocationException.
            WarnOnce("InvalidateConditionValues-call", $"Clearing the stat condition cache failed - skill gates may read a stale skill level: {ex.InnerException?.Message ?? ex.Message}");
        }
    }

    /// <summary>
    /// True while a Permanent change is a perk's starting bonus rather than practice: the run is
    /// initializing (IsRunInitializing, 1.10.6), or a GameManager.ApplyPerk call is on the stack
    /// (PerkGrantDepth, kept by ApplyPerkPrefix and ApplyPerkFinalizer). Each cause keeps its own
    /// warnings: IsRunInitializing warns once per cause on a read failure, and PatchApplyPerk warns
    /// at load when ApplyPerk cannot be patched.
    /// </summary>
    private static bool IsRunSetup(object gameManager)
    {
        return IsRunInitializing(gameManager) || PerkGrantDepth > 0;
    }

    private static PropertyInfo _isInitializingProperty;
    private static bool _isInitializingResolved;

    /// <summary>
    /// GameManager.IsInitializing on the instance ChangeStatValue runs on. Unreadable means false,
    /// the pre-1.10.6 behaviour (a starting skill bonus is scaled again), with one warning per cause.
    /// </summary>
    private static bool IsRunInitializing(object gameManager)
    {
        if (gameManager == null) return false;
        if (!_isInitializingResolved)
        {
            _isInitializingResolved = true;
            _isInitializingProperty = gameManager.GetType().GetProperty("IsInitializing",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }
        if (_isInitializingProperty == null)
        {
            WarnOnce("IsInitializing-missing", "GameManager.IsInitializing not found - a perk's starting skill bonus is scaled as XP again. The game has probably renamed it.");
            return false;
        }
        try { return _isInitializingProperty.GetValue(gameManager, null) is true; }
        catch (Exception ex)
        {
            WarnOnce("IsInitializing-read", $"Reading GameManager.IsInitializing failed - a perk's starting skill bonus is scaled as XP: {ex.Message}");
            return false;
        }
    }

    private static void WarnOnce(string cause, string message)
    {
        if (_warnedCauses.Add(cause))
            Logger?.LogWarning($"[MorningBonus] {message}");
    }

    /// <summary>
    /// LogSkillXpGains: one line per gain that reached composition, the 1x no-op and the per-skill 0
    /// revert included, so a gain handled at 1x and a gain never handled read differently. When the
    /// gain happened at a placed structure the line ends with that structure's UniqueID, which is
    /// the value a player copies into StationBonusMap. It is appended AFTER the fixed prefix so the
    /// "[XpGain] Skill: game +N xM" shape harness scenarios match on is unchanged. After it comes
    /// " in env UniqueID (asset name)", the environment the gain happened in, which is what a player
    /// copies into EnvironmentBonusMap (plan C2; appended last, plan S4). Then " by action 'name' (key
    /// k)" and " with tool UniqueID (tags ...)", the keys of ActionBonusMap and ToolBonusMap (plan C3).
    /// </summary>
    private static void LogXpGain(string skill, float vanillaDelta, float multiplier, float beforeBase, float afterBase, float target, bool writeFailed, string locationUid)
    {
        float finalBase = writeFailed ? afterBase : target;
        float finalDelta = finalBase - beforeBase;
        Logger?.LogInfo(
            $"[XpGain] {skill ?? "(unresolved skill)"}: game +{vanillaDelta:F2} x{multiplier:F2} = {finalDelta:+0.00;-0.00;0.00}, " +
            $"base {beforeBase:F2} -> {finalBase:F2}" +
            (writeFailed ? " (WRITE FAILED: the game's own gain was kept)" : "") +
            (string.IsNullOrEmpty(locationUid) ? "" : $" at structure {locationUid}") +
            EnvironmentBonusService.DescribeForXpLog() +
            AreaFamiliarityPatch.DescribeForXpLog());
    }

    // Resolved from the first ChangeStatValue argument seen: the int value of
    // StatModification.Permanent, or null when that argument is not an enum with that member.
    private static Type _modificationEnumType;
    private static int? _permanentModificationValue;
    private static bool _modificationUnreadableLogged;

    /// <summary>
    /// True when a ChangeStatValue call is StatModification.Permanent, the only kind of change that
    /// is XP. Compared through the enum's own "Permanent" member rather than a hardcoded 0, and
    /// cached, because this runs on every stat change in the game. If the argument cannot be read
    /// as that enum, every change is treated as XP (the pre-1.10.3 behaviour) instead of switching
    /// every bonus off, and one warning says so.
    /// </summary>
    private static bool IsPermanentModification(object modification)
    {
        var type = modification?.GetType();
        if (type != null && type != _modificationEnumType)
        {
            _modificationEnumType = type;
            _permanentModificationValue = type.IsEnum && Enum.IsDefined(type, "Permanent")
                ? Convert.ToInt32(Enum.Parse(type, "Permanent"))
                : (int?)null;
        }

        if (type == null || _permanentModificationValue == null)
        {
            if (!_modificationUnreadableLogged)
            {
                _modificationUnreadableLogged = true;
                Logger?.LogWarning(
                    $"[MorningBonus] ChangeStatValue's modification argument is not readable as StatModification ({type?.FullName ?? "null"}) " +
                    "- a campfire or status effect ending can be scaled as skill XP again. The game's ChangeStatValue signature has probably changed.");
            }
            return true;
        }

        return Convert.ToInt32(modification) == _permanentModificationValue.Value;
    }

    private static bool IsSkillStat(object stat)
    {
        try
        {
            // Primary: check against the skill UniqueID set populated by GameLoadPatch
            var uid = StatAccess.GetUniqueId(stat);
            if (!string.IsNullOrEmpty(uid) && GameLoadPatch.SkillUniqueIds.Contains(uid))
                return true;

            // Fallback: UsesNovelty, which lives on the GameStat/StatModel definition rather than
            // the runtime InGameStat instance. It is NOT a skill test on its own: 9 of the 41
            // vanilla stats that set it on EA 0.67i are not skills, EA 0.68 added a 10th
            // (Arousal), and GameLoadPatch deliberately keeps all of them (Morale, Stress, Focus,
            // Loneliness, Arousal, ...) OUT of SkillUniqueIds - so for exactly
            // those stats the primary lookup above misses and this fallback is what decides. Left
            // bare it answered yes, and every multiplier in this postfix then applied to a mental
            // stat: with area familiarity on by default, a Morale gain was scaled by the location
            // bonus and also counted as a visit toward it.
            var statModel = StatAccess.GetStatModel(stat);
            if (GameLoadPatch.IsIgnoredStatName(statModel)) return false;
            return Reflect.GetBool(statModel, "UsesNovelty", false);
        }
        catch (Exception ex)
        {
            // Gates ChangeStat_Post, which runs on EVERY stat change in the game (CLAUDE.md
            // Runtime Stat Change Hook) — a silent failure here would disable the entire
            // morning-bonus feature for a stat with zero diagnostic trail.
            Logger?.LogDebug($"[MorningBonus] IsSkillStat check failed, treating as non-skill: {ex.Message}");
        }
        return false;
    }

    private static bool IsMorningWindow() => IsCurrentHourInWindow(Plugin.MorningStartHour, Plugin.MorningEndHour);

    private static bool IsNightWindow() => IsCurrentHourInWindow(Plugin.NightStartHour, Plugin.NightEndHour);

    // Both windows go through ONE clock read, so the morning and night bonuses cannot drift apart on
    // the hour arithmetic. That arithmetic is the one tracker T1.75 confirmed live (2026-09-23).
    private static bool IsCurrentHourInWindow(float start, float end)
    {
        return TryGetCurrentHour(out float hour) && IsHourInWindow(hour, start, end);
    }

    /// <summary>
    /// True when a 0-24 clock hour falls inside [start, end). A window whose start is later than its
    /// end wraps past midnight (22 to 2 is 22:00-02:00); a window whose start equals its end is empty.
    /// Pure, with no game types, so SkillSpeedBoost-BonusLogic.Tests.ps1 compiles and runs it: keep
    /// the body C# 5, which is what PowerShell 5.1's Add-Type compiles.
    /// </summary>
    internal static bool IsHourInWindow(float hour, float start, float end)
    {
        return start <= end
            ? hour >= start && hour < end
            : hour >= start || hour < end;
    }

    private static bool TryGetCurrentHour(out float hour)
    {
        hour = 0f;
        try
        {
            if (CardUtil.GetGameManagerInstance() == null) return false;
            if (GameQuery.DayTimePoints < 0) return false;

            // The framework's clock: the game's own GameManager.HourOfTheDayValue, with a DaySettings
            // fallback if a game update removes it (corrected in the framework 2026-09-17). This mod
            // used to recompute the same formula by hand; one copy now carries every clock fix.
            hour = GameQuery.HourOfDay;
            return true;
        }
        catch (Exception ex)
        {
            if (!_clockReadFailureLogged)
            {
                _clockReadFailureLogged = true;
                Logger?.LogWarning($"[MorningBonus] Reading the in-game clock failed (the morning and night bonuses will silently stop applying): {ex.Message}");
            }
        }
        return false;
    }
}
