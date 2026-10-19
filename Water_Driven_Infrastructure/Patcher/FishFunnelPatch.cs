using System;
using HarmonyLib;

namespace WaterDrivenInfrastructure.Patcher
{
    /// <summary>
    /// One Fish Funnel per Funnel Trap (WDI-B20, CHANGELOG [1.12.10]: "A second Fish Funnel at the same location
    /// adds nothing; one funnel still doubles a Funnel Trap's speed"). The funnel's RemotePassiveEffect
    /// (WdiFishFunnelBoost, Special1 rate +10 on the vanilla Funnel Trap) cannot be capped in data:
    /// InGameCardBase.AddExternalPassiveEffect renames every remote copy with a free numeric suffix
    /// (WdiFishFunnelBoost0, WdiFishFunnelBoost1, ...), and OnBoardEffectCap counts stacks under
    /// PassiveEffect.GetGlobalEffectName(card, renamed name), so a second funnel's copy never shares the first
    /// one's stack key (T2.1712: two funnels read rate 30 with OnBoardEffectCap 1 loaded). That global key is
    /// also shared by EVERY trap's first funnel copy, so a cap of 1 would let one trap in the whole world take
    /// the boost; FishFunnel_Placed.json therefore carries OnBoardEffectCap 0 and this guard holds the rule.
    ///
    /// A prefix on AddExternalPassiveEffect refuses a funnel boost on a card that already carries one from a
    /// DIFFERENT live funnel. When that funnel goes, GameManager.RemoveRemotePassiveEffects bumps
    /// RemoteEffectsVersion, the trap's next CheckForRemoteEffects re-collects every source, and the remaining
    /// funnel's boost is admitted then.
    /// </summary>
    public static class FishFunnelPatch
    {
        internal const string FunnelEffectName = "WdiFishFunnelBoost";

        public static void ApplyPatch(Harmony harmony)
        {
            try
            {
                var add = AccessTools.Method(typeof(InGameCardBase), "AddExternalPassiveEffect");
                if (add == null)
                {
                    Plugin.Logger?.LogWarning("[FishFunnelPatch] InGameCardBase.AddExternalPassiveEffect not found (game update?): a second Fish Funnel at a location will speed a Funnel Trap up again.");
                    return;
                }
                harmony.Patch(add, prefix: new HarmonyMethod(AccessTools.Method(typeof(FishFunnelPatch), nameof(AddExternalPassiveEffect_Prefix))));
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogError($"[FishFunnelPatch] Failed to apply patch: {ex}");
            }
        }

        private static bool AddExternalPassiveEffect_Prefix(InGameCardBase __instance, PassiveEffect _Effect, InGameCardBase _ConditionsCard)
        {
            try
            {
                if (_ConditionsCard == null || !IsFunnelBoost(_Effect.EffectName)) return true;
                var existing = __instance?.ExternalPassiveEffects;
                if (existing == null) return true;
                foreach (var effect in existing)
                {
                    if (!IsFunnelBoost(effect.EffectName)) continue;
                    var source = effect.ConditionsCard;
                    if (source == null || source == _ConditionsCard || source.Destroyed) continue;
                    return false; // this trap already takes one funnel's boost
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogDebug($"[FishFunnelPatch] guard skipped: {ex.Message}");
            }
            return true;
        }

        private static bool IsFunnelBoost(string effectName) =>
            !string.IsNullOrEmpty(effectName) && effectName.StartsWith(FunnelEffectName, StringComparison.Ordinal);
    }
}
