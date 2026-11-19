using System;
using HarmonyLib;
using CSFFModFramework.Util;

namespace WaterDrivenInfrastructure.Patcher
{
    /// <summary>
    /// Tells the player WHY a kit cannot be placed here (audit W1, 2026-11-12).
    ///
    /// MillRaceNetwork's gate (the "MillRaceGate" ActionRouter Cancel handler in ActionInterceptPatch)
    /// refuses a Place where no water reaches, and until now it did so in silence: the button could be
    /// pressed, nothing happened, and only a Debug line said why. The Fishpond Kit carried a "Requires
    /// Water" action meant to explain it, which could never render (no effect, so WillHaveAnEffect is
    /// false), and no other station kit carried anything.
    ///
    /// This is the game's own route for a refused action: CardAction.CollectActionModifiers resets
    /// ActionBlockedMessage, and DismantleActionButton.Setup then reads it through
    /// GameManager.CheckActionBlockers, greys the button out and puts the message in its tooltip
    /// (.decomp/CardAction.cs, .decomp/DismantleActionButton.cs, .decomp/GameManager.cs). A postfix
    /// writes the gate's reason there for a Place on a WDI build source. Community_Mod_Chest's
    /// InnPatch does the same for an Inn meal the account cannot pay for.
    ///
    /// The gate itself is unchanged and still cancels the action; this only makes its answer visible
    /// before the click. A card that is not a WDI build source leaves after two set lookups.
    /// </summary>
    public static class PlaceBlockedHintPatch
    {
        public static void ApplyPatch(Harmony harmony)
        {
            try
            {
                const string consequence = "a kit's Place button will not say why it is refused away from water (the refusal itself still works)";
                var collect = ReflectionMiss.Require(AccessTools.Method(typeof(CardAction), "CollectActionModifiers"), typeof(CardAction), "CollectActionModifiers", consequence);
                if (collect == null) return;
                harmony.Patch(collect, postfix: new HarmonyMethod(AccessTools.Method(typeof(PlaceBlockedHintPatch), nameof(CollectActionModifiers_Postfix))));
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogError($"[PlaceBlockedHintPatch] Failed to apply patch: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static bool _threwOnce;

        // __0 is _ReceivingCard, the card the action button belongs to (null for a card-less action).
        private static void CollectActionModifiers_Postfix(CardAction __instance, InGameCardBase __0)
        {
            try
            {
                if (__instance == null || __0 == null) return;
                var model = __0.CardModel;
                if (model == null) return;
                // This runs for every action button of every card: leave before reading anything else.
                string cardId = model.UniqueID;
                if (!MillRaceNetwork.IsPlaceBuildSource(cardId)) return;
                // The same two strings the gate reads (ActionContext.CardUid and ActionContext.ActionName).
                string reason = MillRaceNetwork.GetPlaceBlockedReason(cardId, CardUtil.GetActionName(__instance));
                if (string.IsNullOrEmpty(reason)) return;
                // Never replace a reason the game or another mod already gave this pass.
                if (!string.IsNullOrEmpty(__instance.ActionBlockedMessage)) return;
                __instance.ActionBlockedMessage = reason;
            }
            catch (Exception ex)
            {
                if (_threwOnce) return;
                _threwOnce = true;
                Plugin.Logger?.LogWarning($"[PlaceBlockedHintPatch] could not work out why a Place is refused, so its button gives no reason (the refusal itself still works): {ex.InnerException?.Message ?? ex.Message} [logged once per session]");
            }
        }
    }
}
