namespace PartnerOverhaul.Patcher
{
    /// <summary>
    /// An NPC (Partner) auto-triggering dialog mid-ritual (e.g. a spirit-summon) reads as "my
    /// summon got cancelled" to the player. The legacy AgentActions path has an opt-in guard for
    /// this (<c>CannotPerformIfPlayerCannotBeInterrupted</c>) but EA 0.66's Partner Duty engine
    /// uses <c>StartDialogDutyAction</c> instead, which has no such guard at all — confirmed by
    /// reading <c>.decomp/StartDialogDutyAction.cs</c> in full. This adds one.
    /// </summary>
    public static class DialogInterruptPatch
    {
        private static BepInEx.Logging.ManualLogSource Logger => Plugin.Logger;

        public static void ApplyPatch(Harmony harmony)
        {
            harmony.Patch(
                AccessTools.Method(typeof(StartDialogDutyAction), nameof(StartDialogDutyAction.CanBePerformed)),
                prefix: new HarmonyMethod(typeof(DialogInterruptPatch), nameof(BlockIfPlayerBusy)));
            Logger.LogDebug("DialogInterruptPatch applied.");
        }

        private static bool BlockIfPlayerBusy(ref bool __result)
        {
            try
            {
                var gm = GameManager.Instance;
                // RootActionRemainingTicks dereferences RootAction internally with no null
                // guard — the left-to-right && short-circuit here is load-bearing, not stylistic.
                if (gm != null && gm.RootAction != null && gm.RootActionRemainingTicks > 0)
                {
                    __result = false;
                    return false; // skip original — player is mid-action (e.g. a spirit-summon ritual)
                }
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"[DialogInterruptPatch] BlockIfPlayerBusy failed: {ex}");
            }
            return true;
        }
    }
}
