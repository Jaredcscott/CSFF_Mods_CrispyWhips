using System;
using System.Collections.Generic;
using HarmonyLib;
using BepInEx.Logging;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace Advanced_Copper_Tools.Patcher
{
    /// <summary>
    /// ACT 1.18.9 (ACT-B19): the Wearable Metal Pan's "Melt Fat" uses up an empty Clay Bowl.
    /// The Oil it makes is "fat in a clay bowl" and gives the bowl back when used, so before this
    /// every Melt Fat made a free Clay Bowl (vanilla's own oil recipe takes 2 Fat and a Clay Bowl).
    ///
    /// The JSON half: the CardInteraction only shows while a Clay Bowl is on the board
    /// (RequiredCardsOnBoard). This half: once the action has finished (AfterWrapped, so its time
    /// and its Oil are already done), one empty Clay Bowl in the player's environment is removed.
    /// A bowl holding liquid or anything else is never taken. Reflection only, like the rest of
    /// ACT's patches.
    /// </summary>
    internal static class PanMeltFatPatch
    {
        private static ManualLogSource Logger => Plugin.Logger;

        private const string PanUid = "advanced_copper_tools_wearable_metal_pan";
        private const string MeltFatKey = "Advanced_Copper_Tools_WearableMetalPan_MeltFat_ActionName";
        internal const string ClayBowlGuid = "a968f3eaffc6b9743b82982b5af2ab8c";

        private static readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);

        private static void WarnOnce(string cause, string msg)
        {
            if (_warned.Add(cause)) Logger?.LogWarning($"[PanMeltFat] {msg} (logged once)");
        }

        public static void ApplyPatch(Harmony _)
        {
            try
            {
                ActionRouter.Register(new ActionHandler
                {
                    Name = "PanMeltFatBowl",
                    CardUid = PanUid,
                    ActionKeyPrefix = MeltFatKey,
                    Timing = ActionTiming.AfterWrapped,
                    After = ctx => ConsumeOneEmptyBowl()
                });
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[PanMeltFat] Failed to register: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void ConsumeOneEmptyBowl()
        {
            var cards = GameQuery.CardsInPlayerEnv();
            if (cards == null || cards.Count == 0)
            {
                WarnOnce("env-empty", "no cards read in the player's environment; the Clay Bowl was not used up");
                return;
            }

            object bowl = null;
            foreach (var card in cards)
            {
                if (card == null) continue;
                if (!string.Equals(CardUtil.GetCardUniqueId(card), ClayBowlGuid, StringComparison.OrdinalIgnoreCase)) continue;
                if (!Reflect.TryGetMember(card, "ContainedLiquid", out var rawLiquid))
                {
                    // A null-returning miss is indistinguishable from "empty bowl"; fail safe
                    // (never consume a bowl we cannot prove is empty) and leave a cause-keyed breadcrumb.
                    WarnOnce("liquid-member-missing", "ContainedLiquid not found on the Clay Bowl; cannot tell an empty bowl from a full one, so none was used up");
                    continue;
                }
                if (rawLiquid is UnityEngine.Object liquid && liquid != null) continue;
                var inv = CardUtil.GetInventoryList(card);
                if (inv != null && HasAnyCard(inv)) continue;
                bowl = card;
                break;
            }

            if (bowl == null)
            {
                WarnOnce("no-bowl", "Melt Fat finished but no empty Clay Bowl was found in the player's environment; none was used up");
                return;
            }

            if (CardUtil.TryRemoveCard(bowl))
                Logger?.LogDebug("[PanMeltFat] Used up one Clay Bowl for Melt Fat");
            else
                WarnOnce("remove-failed", "could not remove the Clay Bowl after Melt Fat");
        }

        private static bool HasAnyCard(System.Collections.IList inv)
        {
            foreach (var slot in inv)
            {
                if (slot == null) continue;
                var inner = CardUtil.GetInventoryList(slot);
                if (inner == null || inner.Count > 0) return true;
            }
            return false;
        }
    }
}
