namespace PartnerOverhaul.Patcher
{
    /// <summary>
    /// The reported "pouch stacking-math bug": pouring a powder (Acorn Flour, any solid with a
    /// liquid form) into a container that has room for only PART of it destroys the whole card
    /// and keeps only the part that fit. Vanilla's generated pour action
    /// (CardData.GenerateSolidToLiquidTransferAction, .decomp/CardData.cs:3127-3161) always sets
    /// GivenCardChanges to Destroy, but adds only InGameCardBase.ClampLiquidTransferQuantity(...),
    /// which is capped at the container's free room. A Pouch holds 150 and one Acorn Flour pours
    /// 112.5 (its own "Take Out" spends 112.5 per card), so a second flour loses 75 of its 112.5.
    ///
    /// Keeping the unpoured remainder on the card is NOT a fix: a part-empty flour card still
    /// counts as one whole flour in every recipe, which would duplicate flour. So a pour that
    /// cannot take the whole card is refused instead, on both paths that reach the generator:
    ///  * a single drag asks the protected InGameCardBase.CanTransferLiquids first
    ///    (.decomp/InGameCardBase.cs:9188, its only caller) and offers no pour when it is false;
    ///  * a stack pour regenerates the action for every card after the first
    ///    (.decomp/GameManager.cs:4234) and never asks CanTransferLiquids, so the generated
    ///    action itself is emptied (no destroy, no liquid) for a card that would not fit.
    /// Neither refusal flag vanilla already has is usable here: the "cannot take more" message
    /// both callers build from _EnoughLiquidToTransfer dereferences the solid's ContainedLiquid or
    /// CurrentContainer, which are null for a powder lying on the board.
    ///
    /// Filtered to solid-to-liquid pours whose full amount exceeds the free room; every other
    /// liquid transfer, and every pour that fits, is untouched. Config: Fixes / Refuse Partial
    /// Powder Pours (default ON).
    /// </summary>
    public static class PartialPowderPourPatch
    {
        private static BepInEx.Logging.ManualLogSource Logger => Plugin.Logger;

        // ClampLiquidTransferQuantity returns Mathf.Min of two floats, so a pour that fits exactly
        // can come back a hair under the card's amount; never refuse on rounding noise.
        private const float FitTolerance = 0.01f;

        public static void ApplyPatch(Harmony harmony)
        {
            var canTransfer = AccessTools.Method(typeof(InGameCardBase), "CanTransferLiquids");
            if (canTransfer != null)
                harmony.Patch(canTransfer, postfix: new HarmonyMethod(typeof(PartialPowderPourPatch), nameof(RefusePartialDrag)));
            else
                Logger.LogWarning("[PartialPowderPourPatch] InGameCardBase.CanTransferLiquids not found - a single-drag partial powder pour is not refused.");

            var generate = AccessTools.Method(typeof(CardData), nameof(CardData.GenerateLiquidTransferAction));
            if (generate != null)
                harmony.Patch(generate, postfix: new HarmonyMethod(typeof(PartialPowderPourPatch), nameof(EmptyPartialStackPour)));
            else
                Logger.LogWarning("[PartialPowderPourPatch] CardData.GenerateLiquidTransferAction not found - a stacked partial powder pour is not refused.");

            Logger.LogDebug("PartialPowderPourPatch applied.");
        }

        // CanTransferLiquids(this, _WithCard): mirror GenerateLiquidTransferAction's own choice of
        // which card is the solid (.decomp/CardData.cs:2943-2954).
        private static void RefusePartialDrag(InGameCardBase __instance, InGameCardBase _WithCard, ref bool __result)
        {
            try
            {
                if (!__result || !Plugin.RefusePartialPowderPours.Value) return;
                if (IsPartialSolidPour(__instance, _WithCard))
                {
                    __result = false;
                    Logger.LogDebug($"[PartialPowderPourPatch] refused a partial pour between '{Describe(__instance)}' and '{Describe(_WithCard)}'.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"[PartialPowderPourPatch] RefusePartialDrag failed: {ex}");
            }
        }

        private static void EmptyPartialStackPour(InGameCardBase _ContainerA, InGameCardBase _ContainerB, CardOnCardAction __result)
        {
            try
            {
                if (__result == null || !Plugin.RefusePartialPowderPours.Value) return;
                if (!IsPartialSolidPour(_ContainerA, _ContainerB)) return;

                // A fresh object built for this one call, so emptying it touches no shared data.
                // Both changes are replaced whole, not just set to ModType None: when the container
                // already holds liquid the generator writes the pour into ReceivingCardChanges'
                // LiquidQuantityChange with ModifyLiquid set (.decomp/CardData.cs:3206), and
                // GameManager.ApplyCardStateChange applies that whenever ModifyLiquid is true,
                // whatever the ModType (.decomp/GameManager.cs:11296). ModType None alone kept the
                // card AND added its 37.5, creating flour (harness T2.436, 2026-09-28).
                __result.GivenCardChanges = new CardStateChange();
                __result.ReceivingCardChanges = new CardStateChange();
                __result.ProducedCards = new CardsDropCollection[0];
                __result.AverageFlavoursAndSpicesInReceiving = false;
                Logger.LogDebug($"[PartialPowderPourPatch] emptied a partial stacked pour between '{Describe(_ContainerA)}' and '{Describe(_ContainerB)}'.");
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"[PartialPowderPourPatch] EmptyPartialStackPour failed: {ex}");
            }
        }

        /// <summary>
        /// True when one of the two cards is a solid with a liquid form, the other is the container
        /// it would pour into, and the container cannot take the solid's whole amount.
        /// </summary>
        private static bool IsPartialSolidPour(InGameCardBase a, InGameCardBase b)
        {
            if (a == null || b == null) return false;

            InGameCardBase solid, container;
            if (a.LiquidVersion != null) { solid = a; container = b; }
            else if (b.LiquidVersion != null) { solid = b; container = a; }
            else return false;

            if (container.LiquidVersion != null) return false; // two powders: not a pour into a container
            if (container.CardModel == null || !container.CardModel.CanContainLiquid) return false;

            var durabilities = solid.GetDurabilities();
            if (durabilities == null) return false;
            float whole = durabilities.Liquid;
            if (whole <= 0f) return false;

            // The same liquid card and the same clamp vanilla's generator uses (.decomp/CardData.cs:3160-3161).
            var liquidCard = container.CardModel.ContainedLiquidTransform != null
                ? container.CardModel.ContainedLiquidTransform
                : solid.LiquidVersion;
            float fits = container.ClampLiquidTransferQuantity(liquidCard, durabilities);
            return fits + FitTolerance < whole;
        }

        private static string Describe(InGameCardBase card)
        {
            return card != null && card.CardModel != null ? $"{card.CardModel.name}#{card.GetInstanceID()}" : "(null)";
        }
    }
}
