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

    // Modification-type routing fields — StatAccess.SetCurrentValue has no equivalent for
    // this: InGameStat (EA 0.65g) exposes CurrentValue only as a bool-parameter METHOD, never
    // as a writable field/property, so every write must land on one of these four raw fields
    // (chosen by the ChangeStatValue `modification` argument) or fall back to CurrentBaseValue.
    // This dispatch is specific to hooking ChangeStatValue and has no framework equivalent.
    private static FieldInfo _currentBaseValueField;
    private static FieldInfo _globalModifiedValueField;
    private static FieldInfo _atBaseModifiedValueField;
    private static FieldInfo _currentCompositeValueField;
    private static FieldInfo _temporaryModifiedValueField;
    private static bool _modRoutingReflected;

    // D17: each reflection catch below warns on its first failure only, then stays quiet —
    // these fire on every XP-write / every skill-stat change, so unconditional logging
    // would spam LogOutput.log; the latch lets the breadcrumb survive at LogWarning (visible
    // by default) without spamming, so a future field-rename that kills the feature is seen.
    private static bool _setCurrentValueFailureLogged;
    private static bool _setCurrentValueFieldMissingLogged;
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

        EnsureModificationRoutingFields(stat.GetType());

        float before = SafeGetCurrentValue(stat);
        yield return enumerator;                 // let original coroutine run
        float after = SafeGetCurrentValue(stat);

        float delta = after - before;

        if (delta <= 0f) yield break;            // not an XP gain — skip
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
                // Per-skill set to 0 → "disable skill leveling". Revert the gain that the
                // original coroutine just applied. Other bonuses (morning, familiarity)
                // intentionally don't apply when XP is disabled for this skill.
                SetCurrentValue(stat, before, modification);
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
            // Fraction of max level already achieved (0.0 = just started, 1.0 = fully maxed).
            // Use `after` so the scaling reflects the level *after* this XP tick.
            float levelFraction = Math.Min(after / maxVal, 1f);
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

        // A composed multiplier BELOW 1 is a penalty (low-condition suppression), so this cannot
        // early-out on "not a bonus" the way it did while every term could only push the total up -
        // that early-out would have discarded the whole penalty silently. Only a multiplier of
        // exactly 1 (within float noise) is a no-op.
        if (Math.Abs(multiplier - 1f) <= 0.0001f) yield break;

        // Signed: negative whenever the penalty outweighs the bonuses. after + adjustment is
        // before + delta * multiplier either way, so one expression serves both directions.
        float adjustment = delta * (multiplier - 1f);
        float target = Math.Min(after + adjustment, maxVal);

        // A penalty scales down THIS gain; it never claws back XP the player already had. The
        // configured multiplier range (0-1) cannot breach this, so it is a floor, not arithmetic.
        if (target < before) target = before;

        if (Math.Abs(target - after) > 0.0001f)
        {
            SetCurrentValue(stat, target, modification);
        }
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
    /// Resolves the four modification-type target fields plus the CurrentBaseValue fallback,
    /// once per stat type. These have no framework equivalent (StatAccess.SetCurrentValue
    /// only knows about CurrentValue/CurrentBaseValue — not the Global/AtBase/Composite/
    /// Temporary split needed to route a ChangeStatValue write correctly).
    /// </summary>
    private static void EnsureModificationRoutingFields(Type statType)
    {
        if (_modRoutingReflected) return;
        _modRoutingReflected = true;

        _currentBaseValueField = statType.GetField("CurrentBaseValue", Flags);
        _globalModifiedValueField = statType.GetField("GlobalModifiedValue", Flags);
        _atBaseModifiedValueField = statType.GetField("AtBaseModifiedValue", Flags);
        _currentCompositeValueField = statType.GetField("CurrentCompositeValue", Flags);
        _temporaryModifiedValueField = statType.GetField("TemporaryModifiedValue", Flags);
    }

    /// <summary>
    /// StatAccess.GetCurrentValue returns NaN on failure; the reflection scaffolding this
    /// file replaced returned 0f. Translate at the call site so downstream arithmetic
    /// (delta computation, adjustment math) keeps its original safe-fallback semantics.
    /// </summary>
    private static float SafeGetCurrentValue(object stat)
    {
        var v = StatAccess.GetCurrentValue(stat);
        return float.IsNaN(v) ? 0f : v;
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

    private static void SetCurrentValue(object stat, float value, object modification)
    {
        try
        {
            // Simple case: a genuinely writable CurrentValue field/property. Kept for
            // forward compatibility, but InGameStat in EA 0.65g does NOT have one —
            // CurrentValue is a bool-parameter METHOD there — so this always falls through
            // to the modification-type routing below in the current game version.
            // NOTE: deliberately NOT StatAccess.SetCurrentValue here — that helper also
            // falls back to writing CurrentBaseValue directly (an absolute set), which
            // would silently ignore GlobalModifiedValue/CurrentCompositeValue/
            // TemporaryModifiedValue and desync CurrentValue() from `value`. The adjustment
            // math below is what actually keeps those terms consistent.
            if (Reflect.SetMember(stat, "CurrentValue", value))
                return;

            float adjustment = value - SafeGetCurrentValue(stat);
            if (Math.Abs(adjustment) <= 0.0001f) return;

            var targetField = GetValueFieldForModification(modification) ?? _currentBaseValueField;
            if (targetField == null)
            {
                // The last silent early-return on the write path (audit M1). Reaching it means
                // EnsureModificationRoutingFields resolved NOTHING, not even CurrentBaseValue,
                // which is the field-rename scenario the catch below breadcrumbs for the throwing
                // case. Latched for the same reason: this sits on every XP write.
                if (!_setCurrentValueFieldMissingLogged)
                {
                    _setCurrentValueFieldMissingLogged = true;
                    Logger?.LogWarning(
                        "[MorningBonus] No writable stat value field resolved (not even CurrentBaseValue) " +
                        "- every XP bonus is being dropped silently. The InGameStat field names this mod " +
                        "reflects have probably changed in a game update.");
                }
                return;
            }
            float current = Convert.ToSingle(targetField.GetValue(stat));
            targetField.SetValue(stat, current + adjustment);
        }
        catch (Exception ex)
        {
            if (!_setCurrentValueFailureLogged)
            {
                _setCurrentValueFailureLogged = true;
                Logger?.LogWarning($"[MorningBonus] SetCurrentValue reflection failed (bonuses will silently stop applying): {ex.Message}");
            }
        }
    }

    private static FieldInfo GetValueFieldForModification(object modification)
    {
        var name = modification?.ToString() ?? string.Empty;
        if (name == "GlobalModifier") return _globalModifiedValueField;
        if (name == "AtBaseModifier") return _atBaseModifiedValueField;
        if (name == "CompositeModifier") return _currentCompositeValueField;
        if (name == "TemporaryModifier") return _temporaryModifiedValueField;
        return _currentBaseValueField;
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
            // the runtime InGameStat instance. It is NOT a skill test on its own: 10 of the 41
            // vanilla stats that set it are not skills, and GameLoadPatch deliberately keeps 9 of
            // them (Morale, Stress, Focus, Loneliness, ...) OUT of SkillUniqueIds - so for exactly
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
