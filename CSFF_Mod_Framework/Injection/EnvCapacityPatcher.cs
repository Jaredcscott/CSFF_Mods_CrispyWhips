using System.Reflection;
using CSFFModFramework.Loading;
using CSFFModFramework.Util;
using static CSFFModFramework.Loading.WorldMapLoader;

namespace CSFFModFramework.Injection;

/// <summary>
/// Applies <see cref="MapNodeDefinition.CapacityStats"/> overrides to a cloned CT8 location
/// card's SpecialDurability1–4 fields (Trees / Overgrowth / Foraging / Fertility).
///
/// <para>Called from <see cref="WorldMapInjector.PrepareNode"/> at data-load time (Phase 5i),
/// after <see cref="CardCloneService"/> produces the clone and before any InGameCardBase is
/// created from the SO.</para>
///
/// <para>The env popup capacity bars are <c>DurabilityStat</c> instances on the CT8 CardData,
/// NOT global GameStats. <c>StatModifier</c> / <c>PassiveStatEffects</c> cannot touch them.
/// Three fields matter: <c>MaxValue</c> (cap), <c>FloatValue</c> (starting value — on the
/// <c>OptionalFloatValue</c> base), and <c>RatePerDaytimePoint</c> (passive regen/drain per DTP).
/// All are set via reflection. See CLAUDE.md §WorldMap Clone Node Env Capacity Stats.</para>
/// </summary>
internal static class EnvCapacityPatcher
{
    private const BindingFlags BF =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static void Apply(CardData locationCard,
        Dictionary<string, CapacityStatOverride> overrides)
    {
        if (locationCard == null || overrides == null || overrides.Count == 0) return;
        var cardType = locationCard.GetType();

        int applied = 0;
        for (int i = 1; i <= 4; i++)
        {
            var sdField = FindField(cardType, $"SpecialDurability{i}");
            if (sdField == null) continue;

            var sd = sdField.GetValue(locationCard);
            if (sd == null) continue;

            var statName = GetStatName(sd);
            if (string.IsNullOrEmpty(statName)) continue;

            CapacityStatOverride match = null;
            foreach (var kv in overrides)
            {
                if (statName.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                { match = kv.Value; break; }
            }
            if (match == null) continue;

            ApplyOverride(locationCard, sdField, sd, statName, match);
            applied++;
        }

        if (applied > 0)
            Log.Debug($"EnvCapacityPatcher: applied {applied}/{overrides.Count} stat override(s) to CT8 '{locationCard.UniqueID}'");
        else if (overrides.Count > 0)
            Log.Warn($"EnvCapacityPatcher: none of the {overrides.Count} CapacityStats key(s) matched any SD1-4 stat name on CT8 '{locationCard.UniqueID}' — check the key spelling against CardStatName.DefaultText");
    }

    private static void ApplyOverride(CardData card, FieldInfo sdField, object sd,
        string statName, CapacityStatOverride ov)
    {
        var sdType = sd.GetType();

        if (ov.MaxValue.HasValue)
            TrySet(sd, sdType, "MaxValue", ov.MaxValue.Value);

        // FloatValue lives on the OptionalFloatValue base class — try both common names.
        float startVal = ov.InitialValue ?? ov.MaxValue ?? -1f;
        if (startVal >= 0f && !TrySet(sd, sdType, "FloatValue", startVal))
            TrySet(sd, sdType, "CurrentValue", startVal);

        if (ov.RatePerDTP.HasValue)
            TrySet(sd, sdType, "RatePerDaytimePoint", ov.RatePerDTP.Value);

        // DurabilityStat is a struct — write the mutated boxed copy back to the field.
        if (sdType.IsValueType)
            sdField.SetValue(card, sd);

        Log.Debug($"EnvCapacityPatcher: '{statName}' → MaxValue={ov.MaxValue}, " +
                  $"InitialValue={startVal}, RatePerDTP={ov.RatePerDTP}");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static bool TrySet(object target, Type type, string fieldName, float value)
    {
        var fi = FindField(type, fieldName);
        if (fi == null) return false;
        try { fi.SetValue(target, value); return true; }
        catch (Exception ex) { Log.Debug($"EnvCapacityPatcher: set '{fieldName}' threw: {Log.ExceptionText(ex)}"); return false; }
    }

    private static FieldInfo FindField(Type type, string name)
    {
        for (var t = type; t != null; t = t.BaseType)
        {
            var f = t.GetField(name, BF);
            if (f != null) return f;
        }
        return null;
    }

    /// <summary>Reads <c>CardStatName.DefaultText</c> from a boxed DurabilityStat.</summary>
    private static string GetStatName(object sd)
    {
        if (sd == null) return null;
        var sdType = sd.GetType();

        // CardStatName field — may live on a base type
        object statName = null;
        var nameField = FindField(sdType, "CardStatName");
        if (nameField != null) statName = nameField.GetValue(sd);
        if (statName == null)
        {
            var nameProp = sdType.GetProperty("CardStatName", BF);
            if (nameProp != null) statName = nameProp.GetValue(sd);
        }
        if (statName == null) return null;

        // DefaultText on the LocalizedString — field or property
        var lsType = statName.GetType();
        var dtField = FindField(lsType, "DefaultText");
        if (dtField != null) return dtField.GetValue(statName) as string;
        var dtProp = lsType.GetProperty("DefaultText", BF);
        return dtProp?.GetValue(statName) as string;
    }
}
