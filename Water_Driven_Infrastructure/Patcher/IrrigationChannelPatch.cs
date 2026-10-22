using System;
using HarmonyLib;

namespace WaterDrivenInfrastructure.Patcher
{
    /// <summary>
    /// Irrigation Channel floor hold (T2.1799, owner 2026-10-17 "Build defence-only channel": the channel settles a dry
    /// field just above 1920 Hydration, keeps its Health, never wets it past that). The channel's RemotePassiveEffect
    /// (WdiChannelHoldsFieldHydration, FuelRateModifier +20 on the eight vanilla field cards) cannot do that in data:
    /// the field's bands are inclusive at both ends (ExtraMath.FloatIsInRangeOrGreater), so at exactly 1920 Low (-12)
    /// AND Medium (-24) Evaporation run together with Low hydration (Health -0.05) AND Ideal Conditions (+0.05), and a
    /// flat +20 cycles a field 1920 -> 1904 -> 1912 -> 1920, below the floor two ticks in three, losing Health
    /// (measured 49.6 from 50 over 12 points, walkthrough-20261020-091816). A condition cannot band the remote effect
    /// on the FIELD's Hydration either: PassiveEffect conditions read the ConditionsCard, which is the channel.
    ///
    /// Fix: a prefix on GameManager.ChangeCardDurabilities, the one call through which ApplyRates applies a card's
    /// per-tick rates (and which then re-reads that card's passive bands). When the card carries an ACTIVE channel
    /// effect (its key is in InGameCardBase.PassiveEffects, so every vanilla gate in the JSON - flowing outlet on the
    /// board, not a river shore or wetland - already passed) and the change would leave its Hydration at or below 1920
    /// but less than one channel flow (+20) short of it, the change is raised so the field ends at 1921: out of the
    /// Low bands, inside Ideal Conditions, and no higher. In the steady state the channel's +20 meets the Medium band's
    /// -24 and the lift is the missing 4, so the field holds at 1921. A field further down still climbs at the plain
    /// net +8; a field above the floor still dries by -4 a tick; growth and yield read no Hydration band.
    ///
    /// Why here and not a ConditionsValid postfix: PassiveEffect.ConditionsValid runs for every passive effect on every
    /// card on every passive check. This prefix runs once per card per tick and returns on its first test (no fuel
    /// change, or no external passive effects) for almost every card. It runs at Priority.Last so the framework's
    /// catch-up batching prefix (CatchUpTickBatching) has already scaled _Fuel when this one reads it.
    /// </summary>
    public static class IrrigationChannelPatch
    {
        internal const string ChannelEffectName = "WdiChannelHoldsFieldHydration";

        /// <summary>The vanilla field's band edge: Low Evaporation and Low hydration run at or below it.</summary>
        internal const float FloorHydration = 1920f;

        /// <summary>Where a channel-fed field settles: the first whole value clear of every Low band.</summary>
        internal const float SettleHydration = 1921f;

        public static void ApplyPatch(Harmony harmony)
        {
            try
            {
                var change = AccessTools.Method(typeof(GameManager), "ChangeCardDurabilities");
                if (change == null)
                {
                    Plugin.Logger?.LogWarning("[IrrigationChannelPatch] GameManager.ChangeCardDurabilities not found (game update?): a channel-fed field will dip under 1920 Hydration two ticks in three and lose Health.");
                    return;
                }
                var prefix = new HarmonyMethod(AccessTools.Method(typeof(IrrigationChannelPatch), nameof(ChangeCardDurabilities_Prefix)))
                {
                    priority = Priority.Last
                };
                harmony.Patch(change, prefix: prefix);
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogError($"[IrrigationChannelPatch] Failed to apply patch: {ex}");
            }
        }

        private static void ChangeCardDurabilities_Prefix(InGameCardBase _Card, ref float _Fuel)
        {
            if (_Fuel == 0f || _Card == null) return;
            var external = _Card.ExternalPassiveEffects;
            if (external == null || external.Count == 0) return;
            try
            {
                float flow = ActiveChannelFlow(_Card, external);
                if (flow <= 0f) return;
                float lifted = Lift(_Card.CurrentFuel, _Fuel, flow);
                if (lifted != _Fuel)
                {
                    _Fuel = lifted;
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogDebug($"[IrrigationChannelPatch] floor hold skipped: {ex.Message}");
            }
        }

        /// <summary>
        /// The fuel change to apply instead of <paramref name="change"/>: unchanged unless the field would end at or
        /// below the floor and less than one channel flow short of it, in which case it is raised to end at
        /// SettleHydration. Pure, so the gate can drive it without a game.
        /// </summary>
        internal static float Lift(float current, float change, float flow)
        {
            float end = current + change;
            if (end > FloorHydration || end <= FloorHydration - flow) return change;
            return SettleHydration - current;
        }

        private static float ActiveChannelFlow(InGameCardBase card, System.Collections.Generic.List<PassiveEffect> external)
        {
            var active = card.PassiveEffects;
            if (active == null) return 0f;
            for (int i = 0; i < external.Count; i++)
            {
                string name = external[i].EffectName;
                if (string.IsNullOrEmpty(name) || !name.StartsWith(ChannelEffectName, StringComparison.Ordinal)) continue;
                if (!active.TryGetValue(name, out var applied)) continue;
                float rate = applied.FuelRateModifier; // OptionalFloatValue: 0 when null or inactive
                if (rate > 0f) return rate;
            }
            return 0f;
        }
    }
}
