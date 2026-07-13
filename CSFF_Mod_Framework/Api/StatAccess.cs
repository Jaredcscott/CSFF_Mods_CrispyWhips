using CSFFModFramework.Util;

namespace CSFFModFramework.Api;

/// <summary>
/// Runtime GameStat instance accessors (Centralization Tier 3). Consolidates the
/// CurrentValue/MaxValue/UniqueID property-then-field fallback chains independently
/// duplicated in Community_Mod_Chest (TraitsActionHandler/TraitsTickHandler) and
/// SkillSpeedBoost (MorningBonusPatch) — found by two separate audits of each mod,
/// neither aware of the other's code.
///
/// <para>Distinct from <see cref="CardUtil"/>'s card-durability-stat accessors (those
/// target an InGameCardBase's SpecialDurability/Progress/etc. fields) — this targets
/// the live GameStat instance itself (hunger, thirst, skill XP, custom mod stats, ...).</para>
/// </summary>
public static class StatAccess
{
    /// <summary>
    /// Reads a GameStat's current value. Tries SimpleCurrentValue, then CurrentValue,
    /// then CurrentBaseValue (widest fallback across observed runtime shapes).
    /// Returns <see cref="float.NaN"/> on failure — check with <see cref="float.IsNaN(float)"/>.
    /// </summary>
    public static float GetCurrentValue(object stat)
    {
        if (stat == null) return float.NaN;
        if (Reflect.TryGetMember(stat, "SimpleCurrentValue", out var v) && v != null) return CardUtil.ToFloat(v);
        if (Reflect.TryGetMember(stat, "CurrentValue", out v) && v != null) return CardUtil.ToFloat(v);
        if (Reflect.TryGetMember(stat, "CurrentBaseValue", out v) && v != null) return CardUtil.ToFloat(v);
        return float.NaN;
    }

    /// <summary>
    /// Writes a GameStat's current value directly (tries CurrentValue then
    /// CurrentBaseValue). Callers that need to route by ChangeStatValue's
    /// modification-type argument (GlobalModifier/AtBaseModifier/...) still own that
    /// dispatch themselves — it's patch-specific, not a general stat-access concern.
    /// Returns true on success.
    /// </summary>
    public static bool SetCurrentValue(object stat, float value)
        => stat != null && Reflect.SetMemberAny(stat, value, "CurrentValue", "CurrentBaseValue") != null;

    /// <summary>
    /// Adds <paramref name="delta"/> to a GameStat's current value, clamped to
    /// [0, <see cref="GetMaxValue"/>] (uncapped above if the max can't be resolved).
    /// Returns the new value, or <see cref="float.NaN"/> if the current value couldn't be read.
    /// </summary>
    public static float ModifyCurrentValue(object stat, float delta)
    {
        var current = GetCurrentValue(stat);
        if (float.IsNaN(current)) return float.NaN;
        var max = GetMaxValue(stat);
        var next = Mathf.Clamp(current + delta, 0f, float.IsNaN(max) ? float.MaxValue : max);
        SetCurrentValue(stat, next);
        return next;
    }

    /// <summary>
    /// Reads a GameStat's maximum value. Tries the live CurrentMinMaxValue.y (Vector2),
    /// then MaxValue, then the StatModel/Stat member's MinMaxValue.y. Returns
    /// <see cref="float.NaN"/> on failure — callers commonly treat NaN as uncapped
    /// (<c>float.IsNaN(max) ? float.MaxValue : max</c>).
    /// </summary>
    public static float GetMaxValue(object stat)
    {
        if (stat == null) return float.NaN;

        if (Reflect.TryGetMember(stat, "CurrentMinMaxValue", out var minMax)
            && TryGetVectorY(minMax, out var liveMax) && liveMax > 0f)
            return liveMax;

        if (Reflect.TryGetMember(stat, "MaxValue", out var mv) && mv != null)
            return CardUtil.ToFloat(mv);

        var model = GetStatModel(stat);
        if (model != null && Reflect.TryGetMember(model, "MinMaxValue", out var modelMinMax)
            && TryGetVectorY(modelMinMax, out var modelMax) && modelMax > 0f)
            return modelMax;

        return float.NaN;
    }

    /// <summary>
    /// Resolves the JSON-side stat definition (<c>StatModel</c> or <c>Stat</c> member)
    /// referenced by a runtime GameStat instance. Returns null on failure.
    /// </summary>
    public static object GetStatModel(object stat)
        => stat == null ? null : Reflect.GetMember(stat, "StatModel", "Stat");

    /// <summary>
    /// Reads a GameStat's UniqueID — checked on the stat instance first, then its
    /// StatModel/Stat member. Returns null on failure.
    /// </summary>
    public static string GetUniqueId(object stat)
    {
        if (stat == null) return null;
        if (Reflect.TryGetMember(stat, "UniqueID", out var uid) && uid is string s && !string.IsNullOrEmpty(s))
            return s;
        var model = GetStatModel(stat);
        return model != null && Reflect.TryGetMember(model, "UniqueID", out uid) ? uid as string : null;
    }

    private static bool TryGetVectorY(object vector, out float y)
    {
        y = 0f;
        if (vector == null) return false;
        if (!Reflect.TryGetMember(vector, "y", out var raw) || raw == null) return false;
        y = CardUtil.ToFloat(raw);
        return true;
    }
}
