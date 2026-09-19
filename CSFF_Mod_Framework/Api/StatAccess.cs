using CSFFModFramework.Util;

namespace CSFFModFramework.Api;

/// <summary>
/// Accessors for a LIVE stat instance (Centralization Tier 3). Consolidates the
/// CurrentValue/MaxValue/UniqueID property-then-field fallback chains independently
/// duplicated in Community_Mod_Chest and SkillSpeedBoost (MorningBonusPatch), found by two
/// separate audits of each mod, neither aware of the other's code.
///
/// <para><b>Pass the live <c>InGameStat</c>, never the <c>GameStat</c> definition.</b> The game has
/// two types with near-identical names. <c>GameStat</c> is the ScriptableObject DEFINITION that
/// <c>UniqueIDScriptable.GetFromID</c> returns: it carries <c>BaseValue</c> and <c>MinMaxValue</c>
/// and has NO current value. <c>InGameStat</c> is the per-run MonoBehaviour that holds the value,
/// and it is reached through the definition: <c>GameManager.StatsDict[definition]</c>
/// (<c>Dictionary&lt;GameStat, InGameStat&gt;</c>). Working idiom:
/// <c>Community_Mod_Chest/Patcher/HiddenStat.cs</c> (<c>ResolveInstance</c>).</para>
///
/// <para>Handing a definition to a value accessor cannot work, and it used to fail in complete
/// silence (NaN / false, no log), which is how every C# trait effect in Community Mod Chest
/// shipped dead for months (Trait_Effect_Repair_Plan D1). Each accessor that fails on a
/// definition now says so: one warning per (definition type, member), then the SAME failure
/// value as before. <see cref="GetUniqueId"/> is the one accessor that is valid on a definition
/// and does not warn.</para>
///
/// <para>Distinct from <see cref="CardUtil"/>'s card-durability-stat accessors (those
/// target an InGameCardBase's SpecialDurability/Progress/etc. fields): this targets
/// the live stat itself (hunger, thirst, skill XP, custom mod stats, ...).</para>
/// </summary>
public static class StatAccess
{
    // (runtime type of the definition, StatAccess member) pairs already warned about.
    private static readonly HashSet<(Type, string)> _definitionWarned = new();

    /// <summary>
    /// Reads a live stat's current value. Tries SimpleCurrentValue, then CurrentValue,
    /// then CurrentBaseValue (widest fallback across observed runtime shapes).
    /// Returns <see cref="float.NaN"/> on failure (check with <see cref="float.IsNaN(float)"/>),
    /// including when handed a <c>GameStat</c> definition, which warns once.
    /// </summary>
    public static float GetCurrentValue(object stat)
    {
        if (stat == null) return float.NaN;
        if (IsDefinition(stat, nameof(GetCurrentValue), "NaN")) return float.NaN;
        if (Reflect.TryGetMember(stat, "SimpleCurrentValue", out var v) && v != null) return CardUtil.ToFloat(v);
        if (Reflect.TryGetMember(stat, "CurrentValue", out v) && v != null) return CardUtil.ToFloat(v);
        if (Reflect.TryGetMember(stat, "CurrentBaseValue", out v) && v != null) return CardUtil.ToFloat(v);
        return float.NaN;
    }

    /// <summary>
    /// Writes a live stat's current value directly (tries CurrentValue then
    /// CurrentBaseValue). Callers that need to route by ChangeStatValue's
    /// modification-type argument (GlobalModifier/AtBaseModifier/...) still own that
    /// dispatch themselves: it's patch-specific, not a general stat-access concern.
    /// Returns true on success; false when handed a <c>GameStat</c> definition, which warns once.
    /// </summary>
    public static bool SetCurrentValue(object stat, float value)
    {
        if (stat == null) return false;
        if (IsDefinition(stat, nameof(SetCurrentValue), "false and wrote nothing")) return false;
        return Reflect.SetMemberAny(stat, value, "CurrentValue", "CurrentBaseValue") != null;
    }

    /// <summary>
    /// Adds <paramref name="delta"/> to a live stat's current value, clamped to
    /// [0, <see cref="GetMaxValue"/>] (uncapped above if the max can't be resolved).
    /// Returns the new value, or <see cref="float.NaN"/> if the current value couldn't be read,
    /// including when handed a <c>GameStat</c> definition, which warns once and writes nothing.
    /// </summary>
    public static float ModifyCurrentValue(object stat, float delta)
    {
        // Checked here, under this member's own name, so the one warning names the call the mod
        // actually made instead of the GetCurrentValue it fans out to.
        if (stat != null && IsDefinition(stat, nameof(ModifyCurrentValue), "NaN and wrote nothing")) return float.NaN;
        var current = GetCurrentValue(stat);
        if (float.IsNaN(current)) return float.NaN;
        var max = GetMaxValue(stat);
        var next = Mathf.Clamp(current + delta, 0f, float.IsNaN(max) ? float.MaxValue : max);
        SetCurrentValue(stat, next);
        return next;
    }

    /// <summary>
    /// Reads a live stat's maximum value. Tries the live CurrentMinMaxValue.y (Vector2),
    /// then MaxValue, then the StatModel/Stat member's MinMaxValue.y. Returns
    /// <see cref="float.NaN"/> on failure (callers commonly treat NaN as uncapped:
    /// <c>float.IsNaN(max) ? float.MaxValue : max</c>), including when handed a <c>GameStat</c>
    /// definition, which warns once. The live maximum moves with perk Min/MaxValueModifiers, so
    /// the definition's own <c>MinMaxValue</c> is deliberately not substituted for it.
    /// </summary>
    public static float GetMaxValue(object stat)
    {
        if (stat == null) return float.NaN;
        if (IsDefinition(stat, nameof(GetMaxValue), "NaN")) return float.NaN;

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
    /// referenced by a live stat instance. Returns null on failure, including when handed a
    /// <c>GameStat</c> definition (it has no such member: it IS the definition), which warns once.
    /// </summary>
    public static object GetStatModel(object stat)
    {
        if (stat == null) return null;
        if (IsDefinition(stat, nameof(GetStatModel), "null")) return null;
        return Reflect.GetMember(stat, "StatModel", "Stat");
    }

    /// <summary>
    /// Reads a stat's UniqueID: checked on the object itself first, then its
    /// StatModel/Stat member. Returns null on failure. Valid on a live stat AND on a
    /// <c>GameStat</c> definition (the definition owns the UniqueID), so this is the one
    /// accessor with no definition guard.
    /// </summary>
    public static string GetUniqueId(object stat)
    {
        if (stat == null) return null;
        if (Reflect.TryGetMember(stat, "UniqueID", out var uid) && uid is string s && !string.IsNullOrEmpty(s))
            return s;
        // A definition that reaches this line has an empty UniqueID and no StatModel to try:
        // return null without routing through GetStatModel's guard, which would warn about a
        // call the mod never made.
        if (stat is GameStat) return null;
        var model = GetStatModel(stat);
        return model != null && Reflect.TryGetMember(model, "UniqueID", out uid) ? uid as string : null;
    }

    /// <summary>
    /// True when <paramref name="stat"/> is a <c>GameStat</c> DEFINITION rather than the live
    /// <c>InGameStat</c>. The caller must then return its own failure value. Emits ONE warning
    /// per (definition runtime type, <paramref name="member"/>) for the life of the process,
    /// never one per call: these accessors sit on per-tick paths. Never throws.
    /// </summary>
    private static bool IsDefinition(object stat, string member, string failureText)
    {
        if (stat is not GameStat definition) return false;
        try
        {
            if (_definitionWarned.Add((stat.GetType(), member)))
            {
                string uid = null, name = null;
                try
                {
                    uid = definition.UniqueID;
                    if (definition) name = definition.name;
                }
                catch (Exception ex) { Log.Debug($"[StatAccess] definition identity unreadable: {ex.GetType().Name} {ex.Message}"); }

                Log.Warn($"StatAccess.{member} was handed a {stat.GetType().Name} DEFINITION "
                       + $"(UniqueID '{uid ?? "<unreadable>"}', name '{name ?? "<unreadable>"}') instead of the live InGameStat, "
                       + $"so it returned {failureText}. A definition from UniqueIDScriptable.GetFromID has no current value. "
                       + "Fix the caller: resolve the live instance first with GameManager.StatsDict[definition] "
                       + "(working idiom: Community_Mod_Chest/Patcher/HiddenStat.cs, ResolveInstance) and pass that. "
                       + $"Logged once per StatAccess member; further {member} calls with a definition fail silently.");
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"[StatAccess] definition guard for {member} threw while logging: {ex.GetType().Name} {ex.Message}");
        }
        return true;
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
