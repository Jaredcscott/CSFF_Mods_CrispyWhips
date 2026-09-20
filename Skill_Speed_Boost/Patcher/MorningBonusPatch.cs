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
/// per-skill SkillExpMultiplier, the morning bonus, area familiarity, synergies, level
/// scaling, the daily first-use bonus, the well-rested bonus, and the one term that can push
/// the total BELOW 1 - the low-condition penalty. Multiple iterator postfixes on
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

    // D17: every failure on the XP path warns the FIRST time each cause occurs, then stays quiet. These
    // run on every skill-stat change, so unconditional logging would flood LogOutput.log; keying per
    // cause means a second, different failure still gets its own line instead of hiding behind the first.
    private static readonly HashSet<string> _warnedCauses = new(StringComparer.Ordinal);
    private static bool _morningWindowFailureLogged;
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
        }
        catch (Exception ex)
        {
            Logger.LogError($"[MorningBonus] Patch error: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }

    // IEnumerator coroutine postfix: yield the original first, then inspect the delta.
    static IEnumerator ChangeStat_Post(IEnumerator enumerator, object[] __args)
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

        // Every bonus this postfix can apply must be represented here. A bonus missing from
        // this list is silently dead whenever it is the ONLY one enabled, with no log to say so.
        if (stat == null || (!morningOn && !familiarityOn && !expMultOn && !synergiesOn
            && !levelScalingOn && !firstUseOn && !wellRestedOn && !lowConditionOn))
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

        if (delta <= 0f) yield break;            // not an XP gain, or already at the maximum
        if (!IsSkillStat(stat)) yield break;     // not a tracked skill

        // Resolve skill identity once — shared by expMult, synergies, and debug logging.
        var uid = StatAccess.GetUniqueId(stat);
        string resolvedSkillName = null;
        if (!string.IsNullOrEmpty(uid))
            GameLoadPatch.SkillNamesByUniqueId.TryGetValue(uid, out resolvedSkillName);

        // Compose multipliers from every active dynamic bonus.
        float multiplier = 1f;

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
                    LogXpGain(resolvedSkillName ?? uid, delta, 0f, beforeBase, afterBase, beforeBase, !reverted);
                yield break;
            }
            if (expMult > 1) multiplier *= expMult;
        }

        if (morningOn && IsMorningWindow())
            multiplier *= Plugin.MorningBonusMultiplier;

        if (familiarityOn)
        {
            var locUid = AreaFamiliarityPatch.CurrentLocationUid;
            if (!string.IsNullOrEmpty(locUid))
            {
                multiplier *= AreaFamiliarityService.GetMultiplier(locUid);
                // Mark this action as having gained skill XP so the ActionRoutine postfix
                // increments the visit counter exactly once per action.
                AreaFamiliarityPatch.NoteSkillXpGained();
            }
        }

        if (synergiesOn && !string.IsNullOrWhiteSpace(resolvedSkillName))
        {
            float synergyMult = SkillSynergies.GetAndRecordSynergyBonus(resolvedSkillName);
            if (synergyMult > 1f)
            {
                multiplier *= synergyMult;
                if (Plugin.SkillSynergiesDebugLog)
                    Logger.LogInfo($"[Synergy] {resolvedSkillName}: {synergyMult:F2}x | {SkillSynergies.GetSynergyDebugInfo()}");
            }
        }

        float maxVal = SafeGetMaxValue(stat);

        if (levelScalingOn && maxVal > 0f)
        {
            // Fraction of max level already achieved (0.0 = just started, 1.0 = fully maxed), read from
            // the trained base after this XP tick, not from a displayed value a penalty may be holding down.
            float levelFraction = Math.Min(afterBase / maxVal, 1f);
            float levelBonus = Plugin.LevelScalingMaxBonus * ShapeLevelFraction(levelFraction);
            if (levelBonus > 0f)
                multiplier *= (1f + levelBonus);
        }

        // First gain for this skill today. Claimed only after the gain has already been
        // confirmed as real skill XP, and after the per-skill "disabled" early-out above, so a
        // skill set to 0x never burns its daily claim.
        if (firstUseOn)
        {
            string firstUseKey = !string.IsNullOrWhiteSpace(resolvedSkillName) ? resolvedSkillName : uid;
            if (!string.IsNullOrEmpty(firstUseKey) && ClaimFirstUseOfDay(firstUseKey))
                multiplier *= Plugin.DailyFirstUseMultiplier;
        }

        if (wellRestedOn && PlayerConditionService.IsWellRested(Plugin.WellRestedThreshold))
            multiplier *= Plugin.WellRestedMultiplier;

        // The one PENALTY in the stack: a sub-1.0 factor while any condition stat is low. It is
        // the reason the write below has to be direction-aware - every other term here can only
        // ever push the multiplier up.
        if (lowConditionOn && PlayerConditionService.IsAnyConditionBelow(Plugin.LowConditionThreshold))
            multiplier *= Plugin.LowConditionMultiplier;

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

        if (Plugin.LogSkillXpGains)
            LogXpGain(resolvedSkillName ?? uid, delta, multiplier, beforeBase, afterBase, target, writeFailed);
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
    /// StatAccess.GetMaxValue returns NaN on failure; the local code it replaced returned
    /// float.MaxValue (i.e. "uncapped"). Translate at the call site to preserve that.
    /// </summary>
    private static float SafeGetMaxValue(object stat)
    {
        var v = StatAccess.GetMaxValue(stat);
        return float.IsNaN(v) ? float.MaxValue : v;
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
    private static bool TryGetBaseValue(object stat, out float value)
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
    private static bool TrySetBaseValue(object stat, float value)
    {
        if (!TryResolveBaseField(stat)) return false;
        try
        {
            _currentBaseValueField.SetValue(stat, value);
            return true;
        }
        catch (Exception ex)
        {
            WarnOnce("CurrentBaseValue-write", $"Writing CurrentBaseValue failed - XP bonuses are being dropped and the game's own gain kept: {ex.Message}");
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
    /// revert included, so a gain handled at 1x and a gain never handled read differently.
    /// </summary>
    private static void LogXpGain(string skill, float vanillaDelta, float multiplier, float beforeBase, float afterBase, float target, bool writeFailed)
    {
        float finalBase = writeFailed ? afterBase : target;
        float finalDelta = finalBase - beforeBase;
        Logger?.LogInfo(
            $"[XpGain] {skill ?? "(unresolved skill)"}: game +{vanillaDelta:F2} x{multiplier:F2} = {finalDelta:+0.00;-0.00;0.00}, " +
            $"base {beforeBase:F2} -> {finalBase:F2}" +
            (writeFailed ? " (WRITE FAILED: the game's own gain was kept)" : ""));
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

    private static bool IsMorningWindow()
    {
        try
        {
            var gm = CardUtil.GetGameManagerInstance();
            if (gm == null) return false;

            // DayTimePoints — try several possible names for forward-compat.
            var dtpObj = Reflect.GetMember(gm, "DayTimePoints", "CurrentDayTimePoints", "DaytimePoints");
            float dtp = dtpObj != null ? CardUtil.ToFloat(dtpObj) : -1f;
            if (dtp < 0f) return false;

            // DaySettings.DailyPoints/DayStartingHour — read live rather than hardcoding
            // 96/0. Matches GameManager.HourOfTheDayValue: DayStartingHour + 24 -
            // dtp*(24/DailyPoints), wrapped to a 0-24 clock hour. DTP counts DOWN from
            // DailyPoints (start of day) to 0 (end of day). Omitting DayStartingHour here
            // previously produced an "hours since day start" axis instead of the actual
            // clock hour the MorningStartHour/MorningEndHour config text promises
            // (e.g. vanilla DayStartingHour=4 shifted the window 4 hours late).
            var daySettings = Reflect.GetMember(gm, "DaySettings");
            float dailyPoints = daySettings != null ? Reflect.GetFloat(daySettings, "DailyPoints", 96f) : 96f;
            if (dailyPoints <= 0f) dailyPoints = 96f;
            float dayStartingHour = daySettings != null ? Reflect.GetFloat(daySettings, "DayStartingHour", 0f) : 0f;

            float pointToHours = 24f / dailyPoints;
            float hour = Mod24(dayStartingHour + 24f - (dtp % dailyPoints) * pointToHours);

            float start = Plugin.MorningStartHour;
            float end   = Plugin.MorningEndHour;

            // Support wrap-around windows (e.g. start=22, end=4)
            return start <= end
                ? hour >= start && hour < end
                : hour >= start || hour < end;
        }
        catch (Exception ex)
        {
            if (!_morningWindowFailureLogged)
            {
                _morningWindowFailureLogged = true;
                Logger?.LogWarning($"[MorningBonus] IsMorningWindow reflection failed (morning bonus will silently stop applying): {ex.Message}");
            }
        }
        return false;
    }

    private static float Mod24(float hour)
    {
        float h = hour % 24f;
        return h < 0f ? h + 24f : h;
    }
}
