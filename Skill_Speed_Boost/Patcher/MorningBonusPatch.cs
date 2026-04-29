using System;
using System.Collections;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace Skill_Speed_Boost.Patcher;

/// <summary>
/// Single ChangeStatValue postfix that applies all runtime XP bonuses (morning bonus +
/// area familiarity). Multiple iterator postfixes on the same coroutine cannot compose —
/// only the first wrapper consumes the original enumerator and observes the delta — so
/// every dynamic skill bonus must be combined here.
/// </summary>
internal static class MorningBonusPatch
{
    private static ManualLogSource Logger => Plugin.Logger;
    private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    // Stat object reflection (lazily initialised on first call)
    private static FieldInfo _currentValueField;
    private static PropertyInfo _currentValueProp;
    private static FieldInfo _maxValueField;
    private static PropertyInfo _maxValueProp;
    private static FieldInfo _usesNoveltyField;
    private static FieldInfo _uniqueIdField;

    // GameManager singleton + DayTimePoints
    private static Type _gmType;
    private static PropertyInfo _gmInstanceProp;
    private static FieldInfo _gmInstanceField;
    private static FieldInfo _gmDtpField;
    private static PropertyInfo _gmDtpProp;

    private static bool _reflected;

    public static void ApplyPatch(Harmony harmony)
    {
        try
        {
            var gmType = AccessTools.TypeByName("GameManager");
            if (gmType == null)
            {
                Logger.LogWarning("[MorningBonus] GameManager type not found — morning bonus disabled.");
                return;
            }

            var method = AccessTools.Method(gmType, "ChangeStatValue");
            if (method == null)
            {
                Logger.LogWarning("[MorningBonus] GameManager.ChangeStatValue not found — morning bonus disabled.");
                return;
            }

            harmony.Patch(method, postfix: new HarmonyMethod(typeof(MorningBonusPatch), nameof(ChangeStat_Post)));
        }
        catch (Exception ex)
        {
            Logger.LogError($"[MorningBonus] Patch error: {ex.Message}");
        }
    }

    // IEnumerator coroutine postfix: yield the original first, then inspect the delta.
    // _Stat = the stat object whose value just changed (parameter name on ChangeStatValue).
    static IEnumerator ChangeStat_Post(IEnumerator enumerator, object _Stat)
    {
        bool morningOn = Plugin.MorningBonusEnabled;
        bool familiarityOn = Plugin.AreaFamiliarityEnabled;

        if (_Stat == null || (!morningOn && !familiarityOn))
        {
            yield return enumerator;
            yield break;
        }

        EnsureReflection(_Stat.GetType());

        float before = GetCurrentValue(_Stat);
        yield return enumerator;                 // let original coroutine run
        float after = GetCurrentValue(_Stat);

        float delta = after - before;
        if (delta <= 0f) yield break;            // not an XP gain — skip
        if (!IsSkillStat(_Stat)) yield break;    // not a tracked skill

        // Compose multipliers from every active dynamic bonus.
        float multiplier = 1f;

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

        if (multiplier <= 1f) yield break;

        float bonus = delta * (multiplier - 1f);
        float maxVal = GetMaxValue(_Stat);
        float capped = Math.Min(after + bonus, maxVal);
        if (capped > after)
            SetCurrentValue(_Stat, capped);
    }

    // ── Reflection helpers ────────────────────────────────────────────────────

    private static void EnsureReflection(Type statType)
    {
        if (_reflected) return;
        _reflected = true;

        _currentValueField = statType.GetField("CurrentValue", Flags);
        _currentValueProp  = _currentValueField == null ? statType.GetProperty("CurrentValue", Flags) : null;
        _maxValueField     = statType.GetField("MaxValue", Flags);
        _maxValueProp      = _maxValueField == null ? statType.GetProperty("MaxValue", Flags) : null;
        _usesNoveltyField  = statType.GetField("UsesNovelty", Flags);
        _uniqueIdField     = statType.GetField("UniqueID", Flags);

        // GameManager singleton (type already confirmed in ApplyPatch; re-resolve here for safety)
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try { _gmType ??= asm.GetType("GameManager"); } catch { }
            if (_gmType != null) break;
        }

        if (_gmType == null) return;

        _gmInstanceProp = _gmType.GetProperty("Instance", Flags);
        _gmInstanceField = _gmInstanceProp == null ? _gmType.GetField("Instance", Flags) : null;

        // DayTimePoints — try several possible names for forward-compat
        foreach (var name in new[] { "DayTimePoints", "CurrentDayTimePoints", "DaytimePoints" })
        {
            _gmDtpField = _gmType.GetField(name, Flags);
            if (_gmDtpField != null) break;
            _gmDtpProp = _gmType.GetProperty(name, Flags);
            if (_gmDtpProp != null) break;
        }
    }

    private static float GetCurrentValue(object stat)
    {
        try
        {
            if (_currentValueField != null) return Convert.ToSingle(_currentValueField.GetValue(stat));
            if (_currentValueProp  != null) return Convert.ToSingle(_currentValueProp.GetValue(stat, null));
        }
        catch { }
        return 0f;
    }

    private static float GetMaxValue(object stat)
    {
        try
        {
            if (_maxValueField != null) return Convert.ToSingle(_maxValueField.GetValue(stat));
            if (_maxValueProp  != null) return Convert.ToSingle(_maxValueProp.GetValue(stat, null));
        }
        catch { }
        return float.MaxValue;
    }

    private static void SetCurrentValue(object stat, float value)
    {
        try
        {
            if (_currentValueField != null) _currentValueField.SetValue(stat, value);
            else _currentValueProp?.SetValue(stat, value, null);
        }
        catch { }
    }

    private static bool IsSkillStat(object stat)
    {
        try
        {
            // Primary: check against the skill UniqueID set populated by GameLoadPatch
            var uid = _uniqueIdField?.GetValue(stat)?.ToString()?.Trim();
            if (!string.IsNullOrEmpty(uid) && GameLoadPatch.SkillUniqueIds.Contains(uid))
                return true;

            // Fallback: UsesNovelty flag (true only on skill stats)
            if (_usesNoveltyField != null)
            {
                var v = _usesNoveltyField.GetValue(stat);
                return v is bool b && b;
            }
        }
        catch { }
        return false;
    }

    private static bool IsMorningWindow()
    {
        try
        {
            if (_gmType == null) return false;

            object gm = _gmInstanceProp != null
                ? _gmInstanceProp.GetValue(null, null)
                : _gmInstanceField?.GetValue(null);
            if (gm == null) return false;

            float dtp = _gmDtpField  != null ? Convert.ToSingle(_gmDtpField.GetValue(gm))
                      : _gmDtpProp   != null ? Convert.ToSingle(_gmDtpProp.GetValue(gm, null))
                      : -1f;
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
