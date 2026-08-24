namespace PartnerOverhaul.Patcher
{
    /// <summary>
    /// LogInfo-only diagnostics for the reported Pouch/acorn-flour stacking math bug (see
    /// Documentation/Plans/PartnerOverhaul/PartnerOverhaul_Plan.md "Explicitly out of scope /
    /// unresolved"). No behavior change — this exists to capture a real LogOutput.log of the
    /// generic engine liquid-transfer clamp (InGameCardBase.ClampLiquidTransferQuantity, shared
    /// by hundreds of vanilla items) whenever Pouch or AcornFlourRaw is one of the two cards
    /// involved, so a future fix can be built from evidence instead of a guess, per root
    /// CLAUDE.md's Debugging Discipline. Gated by its own Plugin.EnablePouchDiagnostics
    /// (independent of Plugin.EnableDiagnostics — different subsystem).
    /// </summary>
    public static class PouchTransferDiagnosticsPatch
    {
        private static BepInEx.Logging.ManualLogSource Logger => Plugin.Logger;

        public static void ApplyPatch(Harmony harmony)
        {
            var clampLiquidTransferQuantity = AccessTools.Method(typeof(InGameCardBase), nameof(InGameCardBase.ClampLiquidTransferQuantity));
            if (clampLiquidTransferQuantity != null)
                harmony.Patch(clampLiquidTransferQuantity, postfix: new HarmonyMethod(typeof(PouchTransferDiagnosticsPatch), nameof(LogClampResult)));
            else
                Logger.LogWarning("[PouchTransferDiagnosticsPatch] InGameCardBase.ClampLiquidTransferQuantity not found — pouch/acorn-flour diagnostic skipped.");

            Logger.LogDebug("PouchTransferDiagnosticsPatch applied.");
        }

        private static bool IsWatchedCard(CardData _Card)
        {
            if (_Card == null) return false;
            string uid = _Card.UniqueID ?? "";
            string name = _Card.name ?? "";
            return uid.Contains("Pouch") || name.Contains("Pouch")
                || uid.Contains("AcornFlourRaw") || name.Contains("AcornFlourRaw");
        }

        private static void LogClampResult(InGameCardBase __instance, CardData _LiquidCard, TransferedDurabilities _WithDurabilities, float __result)
        {
            try
            {
                if (!Plugin.EnablePouchDiagnostics.Value) return;

                var receivingModel = __instance != null ? __instance.CardModel : null;
                if (!IsWatchedCard(receivingModel) && !IsWatchedCard(_LiquidCard)) return;

                string receivingLabel = receivingModel != null ? $"{receivingModel.UniqueID}({receivingModel.name})" : "(null)";
                string sourceLabel = _LiquidCard != null ? $"{_LiquidCard.UniqueID}({_LiquidCard.name})" : "(null)";
                float inputLiquid = _WithDurabilities != null ? _WithDurabilities.Liquid : -1f;

                Logger.LogInfo($"[PouchTransferDiagnostics] Receiving='{receivingLabel}' Source='{sourceLabel}' "
                    + $"InputLiquid={inputLiquid} ClampedResult={__result}");
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"[PouchTransferDiagnostics] LogClampResult failed: {ex}");
            }
        }
    }
}
