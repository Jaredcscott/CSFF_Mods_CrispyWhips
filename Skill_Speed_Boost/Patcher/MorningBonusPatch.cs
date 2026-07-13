using System;
using System.Collections;
using System.Reflection;
using BepInEx.Logging;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using HarmonyLib;

namespace Skill_Speed_Boost.Patcher;

/// <summary>
/// Single ChangeStatValue postfix that applies all runtime XP bonuses (global/per-skill
/// SkillExpMultiplier + morning bonus + area familiarity). Multiple iterator postfixes on
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

        if (stat == null || (!morningOn && !familiarityOn && !expMultOn && !synergiesOn && !levelScalingOn))
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
            float levelBonus = Plugin.LevelScalingMaxBonus * levelFraction;
            if (levelBonus > 0f)
                multiplier *= (1f + levelBonus);
        }

        if (multiplier <= 1f) yield break;

        float bonus = delta * (multiplier - 1f);
        float capped = Math.Min(after + bonus, maxVal);
        if (capped > after)
            SetCurrentValue(stat, capped, modification);
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
            if (targetField == null) return;
            float current = Convert.ToSingle(targetField.GetValue(stat));
            targetField.SetValue(stat, current + adjustment);
        }
        catch { }
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

            // Fallback: UsesNovelty flag (true only on skill stats) — lives on the
            // GameStat/StatModel definition, not the runtime InGameStat instance.
            var statModel = StatAccess.GetStatModel(stat);
            return Reflect.GetBool(statModel, "UsesNovelty", false);
        }
        catch { }
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

            // DTP counts down from 96 each day (96 = start of day, 0 = end of day).
            // Convert to hour-within-day (0.0 = day start, 23.99 = day end).
            float hour = (96f - (dtp % 96f)) / 4f;

            float start = Plugin.MorningStartHour;
            float end   = Plugin.MorningEndHour;

            // Support wrap-around windows (e.g. start=22, end=4)
            return start <= end
                ? hour >= start && hour < end
                : hour >= start || hour < end;
        }
        catch { }
        return false;
    }
}
