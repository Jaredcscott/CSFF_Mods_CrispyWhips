using System;
using System.Collections;
using System.Reflection;
using BepInEx.Logging;
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

    // Stat object reflection (lazily initialised on first call)
    private static FieldInfo _currentValueField;
    private static PropertyInfo _currentValueProp;
    private static MethodInfo _currentValueMethod;
    private static PropertyInfo _simpleCurrentValueProp;
    private static FieldInfo _currentBaseValueField;
    private static FieldInfo _globalModifiedValueField;
    private static FieldInfo _atBaseModifiedValueField;
    private static FieldInfo _currentCompositeValueField;
    private static FieldInfo _temporaryModifiedValueField;
    private static FieldInfo _maxValueField;
    private static PropertyInfo _maxValueProp;
    private static FieldInfo _currentMinMaxValueField;
    private static PropertyInfo _currentMinMaxValueProp;
    private static FieldInfo _statModelMinMaxValueField;
    private static PropertyInfo _statModelMinMaxValueProp;
    private static FieldInfo _statModelField;
    private static PropertyInfo _statModelProp;
    private static FieldInfo _usesNoveltyField;
    private static PropertyInfo _usesNoveltyProp;
    private static FieldInfo _uniqueIdField;
    private static PropertyInfo _uniqueIdProp;

    // GameManager singleton + DayTimePoints
    private static Type _gmType;
    private static PropertyInfo _gmInstanceProp;
    private static FieldInfo _gmInstanceField;
    private static FieldInfo _gmDtpField;
    private static PropertyInfo _gmDtpProp;
    private static FieldInfo _gmNotInBaseField;
    private static PropertyInfo _gmNotInBaseProp;

    private static int _statArgIndex = 0;
    private static int _modificationArgIndex = 2;

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
            Logger.LogError($"[MorningBonus] Patch error: {ex.Message}");
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

        if (stat == null || (!morningOn && !familiarityOn && !expMultOn))
        {
            yield return enumerator;
            yield break;
        }

        EnsureReflection(stat.GetType());

        float before = GetCurrentValue(stat);
        yield return enumerator;                 // let original coroutine run
        float after = GetCurrentValue(stat);

        float delta = after - before;
        if (delta <= 0f) yield break;            // not an XP gain — skip
        if (!IsSkillStat(stat)) yield break;     // not a tracked skill

        // Compose multipliers from every active dynamic bonus.
        float multiplier = 1f;

        // Global / per-skill SkillExpMultiplier — replaces the old load-time graph rewrite.
        if (expMultOn)
        {
            int expMult = Plugin.SkillExpMultiplier;
            if (Plugin.EnablePerSkillMultipliers)
            {
                var uid = GetUniqueId(stat);
                if (!string.IsNullOrEmpty(uid)
                    && GameLoadPatch.SkillNamesByUniqueId.TryGetValue(uid, out var skillName)
                    && !string.IsNullOrWhiteSpace(skillName))
                {
                    expMult = SkillConfigManager.GetSkillMultiplier(skillName);
                }
            }
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

        if (multiplier <= 1f) yield break;

        float bonus = delta * (multiplier - 1f);
        float maxVal = GetMaxValue(stat);
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

    private static void EnsureReflection(Type statType)
    {
        if (_reflected) return;
        _reflected = true;

        _currentValueField = statType.GetField("CurrentValue", Flags);
        _currentValueProp  = _currentValueField == null ? statType.GetProperty("CurrentValue", Flags) : null;
        _currentValueMethod = statType.GetMethod("CurrentValue", Flags, null, new[] { typeof(bool) }, null);
        _simpleCurrentValueProp = statType.GetProperty("SimpleCurrentValue", Flags);
        _currentBaseValueField = statType.GetField("CurrentBaseValue", Flags);
        _globalModifiedValueField = statType.GetField("GlobalModifiedValue", Flags);
        _atBaseModifiedValueField = statType.GetField("AtBaseModifiedValue", Flags);
        _currentCompositeValueField = statType.GetField("CurrentCompositeValue", Flags);
        _temporaryModifiedValueField = statType.GetField("TemporaryModifiedValue", Flags);
        _maxValueField     = statType.GetField("MaxValue", Flags);
        _maxValueProp      = _maxValueField == null ? statType.GetProperty("MaxValue", Flags) : null;
        _currentMinMaxValueField = statType.GetField("CurrentMinMaxValue", Flags);
        _currentMinMaxValueProp = _currentMinMaxValueField == null ? statType.GetProperty("CurrentMinMaxValue", Flags) : null;
        _statModelField = statType.GetField("StatModel", Flags) ?? statType.GetField("Stat", Flags);
        _statModelProp = _statModelField == null
            ? (statType.GetProperty("StatModel", Flags) ?? statType.GetProperty("Stat", Flags))
            : null;
        _usesNoveltyField  = statType.GetField("UsesNovelty", Flags);
        _usesNoveltyProp = _usesNoveltyField == null ? statType.GetProperty("UsesNovelty", Flags) : null;
        _uniqueIdField = statType.GetField("UniqueID", Flags);
        _uniqueIdProp = _uniqueIdField == null ? statType.GetProperty("UniqueID", Flags) : null;

        var statModelType = GetStatModelType();
        if (statModelType != null)
        {
            _usesNoveltyField ??= statModelType.GetField("UsesNovelty", Flags);
            _usesNoveltyProp ??= _usesNoveltyField == null ? statModelType.GetProperty("UsesNovelty", Flags) : null;
            _uniqueIdField ??= statModelType.GetField("UniqueID", Flags);
            _uniqueIdProp ??= _uniqueIdField == null ? statModelType.GetProperty("UniqueID", Flags) : null;
            _statModelMinMaxValueField = statModelType.GetField("MinMaxValue", Flags);
            _statModelMinMaxValueProp = _statModelMinMaxValueField == null ? statModelType.GetProperty("MinMaxValue", Flags) : null;
        }

        // GameManager singleton (type already confirmed in ApplyPatch; re-resolve here for safety)
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try { _gmType ??= asm.GetType("GameManager"); } catch { }
            if (_gmType != null) break;
        }

        if (_gmType == null) return;

        _gmInstanceProp = _gmType.GetProperty("Instance", Flags | BindingFlags.FlattenHierarchy);
        _gmInstanceField = _gmInstanceProp == null ? _gmType.GetField("Instance", Flags | BindingFlags.FlattenHierarchy) : null;

        // DayTimePoints — try several possible names for forward-compat
        foreach (var name in new[] { "DayTimePoints", "CurrentDayTimePoints", "DaytimePoints" })
        {
            _gmDtpField = _gmType.GetField(name, Flags);
            if (_gmDtpField != null) break;
            _gmDtpProp = _gmType.GetProperty(name, Flags);
            if (_gmDtpProp != null) break;
        }

        _gmNotInBaseField = _gmType.GetField("NotInBase", Flags);
        _gmNotInBaseProp = _gmNotInBaseField == null ? _gmType.GetProperty("NotInBase", Flags) : null;
    }

    private static Type GetStatModelType()
    {
        if (_statModelField != null) return _statModelField.FieldType;
        return _statModelProp?.PropertyType;
    }

    private static float GetCurrentValue(object stat)
    {
        try
        {
            if (_currentValueMethod != null) return Convert.ToSingle(_currentValueMethod.Invoke(stat, new object[] { GetNotInBase() }));
            if (_simpleCurrentValueProp != null) return Convert.ToSingle(_simpleCurrentValueProp.GetValue(stat, null));
            if (_currentValueField != null) return Convert.ToSingle(_currentValueField.GetValue(stat));
            if (_currentValueProp  != null) return Convert.ToSingle(_currentValueProp.GetValue(stat, null));
            if (_currentBaseValueField != null) return Convert.ToSingle(_currentBaseValueField.GetValue(stat));
        }
        catch { }
        return 0f;
    }

    private static float GetMaxValue(object stat)
    {
        try
        {
            var minMax = _currentMinMaxValueField != null ? _currentMinMaxValueField.GetValue(stat)
                : _currentMinMaxValueProp != null ? _currentMinMaxValueProp.GetValue(stat, null)
                : null;
            if (TryGetVectorY(minMax, out var currentMax) && currentMax > 0f) return currentMax;

            if (_maxValueField != null) return Convert.ToSingle(_maxValueField.GetValue(stat));
            if (_maxValueProp  != null) return Convert.ToSingle(_maxValueProp.GetValue(stat, null));

            var statModel = GetStatModel(stat);
            minMax = _statModelMinMaxValueField != null && statModel != null ? _statModelMinMaxValueField.GetValue(statModel)
                : _statModelMinMaxValueProp != null && statModel != null ? _statModelMinMaxValueProp.GetValue(statModel, null)
                : null;
            if (TryGetVectorY(minMax, out var modelMax) && modelMax > 0f) return modelMax;
        }
        catch { }
        return float.MaxValue;
    }

    private static void SetCurrentValue(object stat, float value, object modification)
    {
        try
        {
            if (_currentValueField != null) _currentValueField.SetValue(stat, value);
            else if (_currentValueProp != null && _currentValueProp.CanWrite) _currentValueProp.SetValue(stat, value, null);
            else
            {
                float adjustment = value - GetCurrentValue(stat);
                if (Math.Abs(adjustment) <= 0.0001f) return;

                var targetField = GetValueFieldForModification(modification) ?? _currentBaseValueField;
                if (targetField == null) return;
                float current = Convert.ToSingle(targetField.GetValue(stat));
                targetField.SetValue(stat, current + adjustment);
            }
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
            var uid = GetUniqueId(stat);
            if (!string.IsNullOrEmpty(uid) && GameLoadPatch.SkillUniqueIds.Contains(uid))
                return true;

            // Fallback: UsesNovelty flag (true only on skill stats)
            var v = GetMemberValue(stat, GetStatModel(stat), _usesNoveltyField, _usesNoveltyProp);
            return v is bool b && b;
        }
        catch { }
        return false;
    }

    private static string GetUniqueId(object stat)
    {
        var v = GetMemberValue(stat, GetStatModel(stat), _uniqueIdField, _uniqueIdProp);
        return v?.ToString()?.Trim();
    }

    private static object GetStatModel(object stat)
    {
        try
        {
            if (_statModelField != null) return _statModelField.GetValue(stat);
            if (_statModelProp != null) return _statModelProp.GetValue(stat, null);
        }
        catch { }
        return null;
    }

    private static object GetMemberValue(object stat, object statModel, FieldInfo field, PropertyInfo prop)
    {
        try
        {
            if (field != null)
            {
                var target = field.DeclaringType != null && field.DeclaringType.IsInstanceOfType(stat) ? stat : statModel;
                return target != null ? field.GetValue(target) : null;
            }
            if (prop != null)
            {
                var target = prop.DeclaringType != null && prop.DeclaringType.IsInstanceOfType(stat) ? stat : statModel;
                return target != null ? prop.GetValue(target, null) : null;
            }
        }
        catch { }
        return null;
    }

    private static bool TryGetVectorY(object vector, out float y)
    {
        y = 0f;
        if (vector == null) return false;
        try
        {
            var type = vector.GetType();
            var field = type.GetField("y", Flags);
            if (field != null)
            {
                y = Convert.ToSingle(field.GetValue(vector));
                return true;
            }

            var prop = type.GetProperty("y", Flags) ?? type.GetProperty("Y", Flags);
            if (prop != null)
            {
                y = Convert.ToSingle(prop.GetValue(vector, null));
                return true;
            }
        }
        catch { }
        return false;
    }

    private static bool GetNotInBase()
    {
        try
        {
            if (_gmType == null) return false;
            object gm = _gmInstanceProp != null
                ? _gmInstanceProp.GetValue(null, null)
                : _gmInstanceField?.GetValue(null);
            if (gm == null) return false;
            if (_gmNotInBaseField != null) return (bool)_gmNotInBaseField.GetValue(gm);
            if (_gmNotInBaseProp != null) return (bool)_gmNotInBaseProp.GetValue(gm, null);
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
