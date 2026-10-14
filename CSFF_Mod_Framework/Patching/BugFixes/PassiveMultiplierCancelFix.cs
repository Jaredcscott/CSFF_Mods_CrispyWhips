namespace CSFFModFramework.Patching.BugFixes;

/// <summary>
/// Makes leaving a multiplying passive effect undo the multiply, for every durability, not only
/// Spoilage.
///
/// <para><strong>The vanilla flaw</strong> (EA 0.68b <c>.decomp/InGameCardBase.cs</c>).
/// <c>ApplyPassiveEffectDurabilities</c> honours <c>PassiveEffect.Multiply&lt;X&gt;Rate</c> for all
/// eight durabilities (<c>&lt;X&gt;RateMultiplier *= m</c>), but <c>CancelPassiveEffectDurabilities</c>
/// checks the flag only for Spoilage: for Usage, Fuel, Progress (ConsumableCharges) and Special1-4 it
/// always runs <c>Base&lt;X&gt;Rate -= m</c>. So when such an effect ends, the multiplier stays applied
/// and the base rate drops by m as well, and it compounds on every enter and leave. Vanilla data never
/// sets those flags, so vanilla never hits it; Sirus23 does (Wolf Den and Owl Perch Morale x0.75, the
/// sheep's Spring Growth wool x1.25 and Winter Chill milk x0.75). Harness T2.543 (2026-10-07): a wolf
/// that left two Dens read Morale -1.40625 a tick instead of -1, which is base -1 - 0.75 - 0.75 = -2.5
/// times the left-over multiplier 0.5625.</para>
///
/// <para><strong>The fix.</strong> A postfix that, for each non-Spoilage durability vanilla handled
/// (same guards: the card has the durability and the effect's modifier is active) and whose Multiply
/// flag is set, adds back the m vanilla subtracted and divides the multiplier by m, which is exactly
/// vanilla's own Spoilage branch. A zero modifier is skipped (the multiply cannot be undone, as for
/// vanilla's Spoilage). Effects without the flag are untouched.</para>
/// </summary>
internal static class PassiveMultiplierCancelFix
{
    private static AccessTools.FieldRef<InGameCardBase, float> F(string name)
        => AccessTools.FieldRefAccess<InGameCardBase, float>(name);

    private static AccessTools.FieldRef<InGameCardBase, float> _baseUsage, _multUsage, _baseFuel, _multFuel,
        _baseCons, _multCons, _baseS1, _multS1, _baseS2, _multS2, _baseS3, _multS3, _baseS4, _multS4;

    public static void ApplyPatch(Harmony harmony)
    {
        try
        {
            var original = AccessTools.Method(typeof(InGameCardBase), "CancelPassiveEffectDurabilities");
            if (original == null)
            {
                Util.Log.Warn("PassiveMultiplierCancelFix: InGameCardBase.CancelPassiveEffectDurabilities not found; a multiplying passive effect on a non-Spoilage durability still corrupts the rate when it ends.");
                return;
            }
            _baseUsage = F("BaseUsageRate"); _multUsage = F("UsageRateMultiplier");
            _baseFuel = F("BaseFuelRate"); _multFuel = F("FuelRateMultiplier");
            _baseCons = F("BaseConsumableRate"); _multCons = F("ConsumableRateMultiplier");
            _baseS1 = F("BaseSpecial1Rate"); _multS1 = F("Special1RateMultiplier");
            _baseS2 = F("BaseSpecial2Rate"); _multS2 = F("Special2RateMultiplier");
            _baseS3 = F("BaseSpecial3Rate"); _multS3 = F("Special3RateMultiplier");
            _baseS4 = F("BaseSpecial4Rate"); _multS4 = F("Special4RateMultiplier");
            harmony.Patch(original, postfix: new HarmonyMethod(AccessTools.Method(typeof(PassiveMultiplierCancelFix), nameof(Postfix))));
            Util.Log.Debug("PassiveMultiplierCancelFix: patched InGameCardBase.CancelPassiveEffectDurabilities.");
        }
        catch (Exception ex)
        {
            Util.Log.Warn($"PassiveMultiplierCancelFix: patch failed; multiplying passive effects keep corrupting non-Spoilage rates when they end: {Util.Log.ExceptionText(ex)}");
        }
    }

    private static void Postfix(InGameCardBase __instance, PassiveEffect _Effect)
    {
        if (__instance == null) return;   // PassiveEffect is a struct: never null
        var model = __instance.CardModel;
        if (model == null) return;
        try
        {
            if (_Effect.MultiplyUsageRate && (bool)model.UsageDurability && (bool)_Effect.UsageRateModifier)
                Undo(__instance, _Effect, _Effect.UsageRateModifier, _baseUsage, _multUsage);
            if (_Effect.MultiplyFuelRate && (bool)model.FuelCapacity && (bool)_Effect.FuelRateModifier)
                Undo(__instance, _Effect, _Effect.FuelRateModifier, _baseFuel, _multFuel);
            if (_Effect.MultiplyConsumableChargesRate && (bool)model.Progress && (bool)_Effect.ConsumableChargesModifier)
                Undo(__instance, _Effect, _Effect.ConsumableChargesModifier, _baseCons, _multCons);
            if (_Effect.MultiplySpecial1Rate && (bool)model.SpecialDurability1 && (bool)_Effect.Special1RateModifier)
                Undo(__instance, _Effect, _Effect.Special1RateModifier, _baseS1, _multS1);
            if (_Effect.MultiplySpecial2Rate && (bool)model.SpecialDurability2 && (bool)_Effect.Special2RateModifier)
                Undo(__instance, _Effect, _Effect.Special2RateModifier, _baseS2, _multS2);
            if (_Effect.MultiplySpecial3Rate && (bool)model.SpecialDurability3 && (bool)_Effect.Special3RateModifier)
                Undo(__instance, _Effect, _Effect.Special3RateModifier, _baseS3, _multS3);
            if (_Effect.MultiplySpecial4Rate && (bool)model.SpecialDurability4 && (bool)_Effect.Special4RateModifier)
                Undo(__instance, _Effect, _Effect.Special4RateModifier, _baseS4, _multS4);
        }
        catch (Exception ex)
        {
            Util.Log.Debug($"PassiveMultiplierCancelFix: postfix failed on {model.UniqueID}: {Util.Log.ExceptionText(ex)}");
        }
    }

    // The same m vanilla used: the modifier, scaled when the effect scales with durabilities.
    private static void Undo(InGameCardBase card, PassiveEffect effect, OptionalFloatValue modifier,
        AccessTools.FieldRef<InGameCardBase, float> baseRate, AccessTools.FieldRef<InGameCardBase, float> multiplier)
    {
        float m = effect.EffectScalesWithDurabilities.Active ? (float)modifier * effect.CurrentScaling : (float)modifier;
        baseRate(card) += m;            // vanilla subtracted it as if additive
        if (m != 0f) multiplier(card) /= m;
    }
}
