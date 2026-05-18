using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using CSFFModFramework.Util;

namespace Herbs_And_Fungi.Patcher
{
    /// <summary>
    /// Two responsibilities:
    /// 1) On Make Brine: swap the contained liquid (water → pickle brine) using the in-place
    ///    CardModel pattern. Vat itself stays as PicklingVat.
    /// 2) Pickle BP gate: if a player picks a pickle blueprint while the vat's contained liquid
    ///    is anything other than pickle_brine, block execution. Quantity gate alone (≥500)
    ///    can't tell water from brine, so we enforce liquid type here in code.
    /// </summary>
    public static class PickleVatRoutePatch
    {
        private const string PicklingVatID = "herbs_fungi_pickling_vat";
        private const string BrineID       = "herbs_fungi_pickle_brine";
        private const string MakeBrineKey  = "Herbs_And_Fungi_PicklingVat_MakeBrine_ActionName";

        private static readonly HashSet<string> PickleBpUids = new HashSet<string>
        {
            "herbs_fungi_bp_pickle_frogs",
            "herbs_fungi_bp_pickle_mushrooms",
            "herbs_fungi_bp_pickle_meat",
            "herbs_fungi_bp_pickle_vegetables",
        };

        private static readonly HashSet<string> PickleBpNameKeys = new HashSet<string>
        {
            "Herbs_And_Fungi_Bp_PickleFrogs_CardName",
            "Herbs_And_Fungi_Bp_PickleMushrooms_CardName",
            "Herbs_And_Fungi_Bp_PickleMeat_CardName",
            "Herbs_And_Fungi_Bp_PickleVegetables_CardName",
        };

        private const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static ManualLogSource Logger => Plugin.Logger;

        public static void ApplyPatch(Harmony harmony)
        {
            try
            {
                var gmt = AccessTools.TypeByName("GameManager");
                if (gmt == null) { Logger?.LogError("[PickleVat] GameManager not found"); return; }

                PatchIfFound(harmony, gmt, "ActionRoutine", nameof(ActionRoutine_Prefix), "CardAction", "InGameCardBase");
                PatchIfFound(harmony, gmt, "CardOnCardActionRoutine", nameof(CardOnCardActionRoutine_Prefix), "CardOnCardAction", "InGameCardBase", "InGameCardBase");
                PatchIfFound(harmony, gmt, "PerformStackActionRoutine", nameof(PerformStackActionRoutine_Prefix), "CardAction");
            }
            catch (Exception ex) { Logger?.LogError($"[PickleVat] ApplyPatch: {ex.InnerException?.ToString() ?? ex.ToString()}"); }
        }

        private static void PatchIfFound(Harmony harmony, Type t, string methodName, string prefixName, params string[] paramTypeNames)
        {
            var method = CardUtil.FindMethodBySignature(t, methodName, paramTypeNames);
            if (method == null) { Logger?.LogError($"[PickleVat] {methodName} with expected parameters not found"); return; }
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(PickleVatRoutePatch), prefixName));
        }

        // ActionRoutine(CardAction action, InGameCardBase receiving, ...) — most card actions, including BP execution from a contained-blueprint click.
        private static bool ActionRoutine_Prefix(object __0, object __1)
        {
            return Gate(action: __0, receiver: __1);
        }

        // CardOnCardActionRoutine(action, given, receiving, ...) — drag-drop CIs.
        private static bool CardOnCardActionRoutine_Prefix(object __0, object __1, object __2)
        {
            return Gate(action: __0, receiver: __2);
        }

        // PerformStackActionRoutine(stackAction, receiving, ...) — stack-based actions.
        private static bool PerformStackActionRoutine_Prefix(object __0, object __1)
        {
            return Gate(action: __0, receiver: GetStackReceiver(__1) ?? __1);
        }

        private static object GetStackReceiver(object stackCards)
        {
            if (stackCards is IList cards && cards.Count > 0) return cards[0];
            return null;
        }

        /// <summary>
        /// Shared dispatch: handles MakeBrine swap, then pickle-BP brine-required gate.
        /// Returns false to block the original (only when the BP gate trips).
        /// </summary>
        private static bool Gate(object action, object receiver)
        {
            try
            {
                if (action == null) return true;

                // Make Brine: liquid swap (water → brine), allow original to proceed
                if (receiver != null && IsMakeBrine(action) && CardUtil.GetCardUniqueId(receiver) == PicklingVatID)
                {
                    SwapLiquidToBrine(receiver);
                    return true;
                }

                // Pickle BP gate: only allow when contained liquid is brine
                if (IsPickleBp(action))
                {
                    var liquidUid = GetContainedLiquidUid(receiver);
                    if (liquidUid == BrineID) return true;
                    Logger?.LogDebug($"[PickleBpGate] Blocked: liquid='{liquidUid ?? "none"}' on receiver='{CardUtil.GetCardUniqueId(receiver) ?? "?"}' — pickle recipes require brine.");
                    return false;
                }
            }
            catch (Exception ex) { Logger?.LogError($"[PickleVat] Gate: {ex.InnerException?.Message ?? ex.Message}"); }
            return true;
        }

        private static bool IsPickleBp(object actionOrCard)
        {
            if (actionOrCard == null) return false;
            var uid = CardUtil.GetCardUniqueId(actionOrCard);
            if (uid != null && PickleBpUids.Contains(uid)) return true;
            var key = CardUtil.GetActionLocalizationKey(actionOrCard);
            if (key != null && PickleBpNameKeys.Contains(key)) return true;
            var cardName = CardUtil.GetMemberValue(actionOrCard, "CardName");
            key = CardUtil.GetMemberValue(cardName, "LocalizationKey") as string;
            if (key != null && PickleBpNameKeys.Contains(key)) return true;
            return false;
        }

        private static string GetContainedLiquidUid(object vat)
        {
            if (vat == null) return null;
            var liquidField = CardUtil.FindField(vat.GetType(), "ContainedLiquid");
            var liquidCard = liquidField?.GetValue(vat);
            return CardUtil.GetCardUniqueId(liquidCard);
        }

        private static void SwapLiquidToBrine(object vat)
        {
            var brineData = CardUtil.GetCardDataById(BrineID);
            if (brineData == null) { Logger?.LogError($"[MakeBrine] CardData not found: {BrineID}"); return; }

            var liquidField = CardUtil.FindField(vat.GetType(), "ContainedLiquid");
            if (liquidField == null) { Logger?.LogError("[MakeBrine] ContainedLiquid field not found on vat"); return; }

            var liquidCard = liquidField.GetValue(vat);
            if (liquidCard == null) { Logger?.LogError("[MakeBrine] vat has no ContainedLiquid to swap"); return; }

            var setModel = liquidCard.GetType().GetMethod("SetModel", All, null, new[] { brineData.GetType() }, null);
            if (setModel != null)
            {
                try { setModel.Invoke(liquidCard, new[] { brineData }); }
                catch (Exception ex) { Logger?.LogError($"[MakeBrine] SetModel threw: {ex.InnerException?.Message ?? ex.Message}"); return; }
            }
            else
            {
                if (!CardUtil.TrySetCardModel(liquidCard, brineData))
                {
                    Logger?.LogError($"[MakeBrine] CardModel not settable on {liquidCard.GetType().Name}");
                    return;
                }
                CardUtil.ReinitCard(liquidCard, brineData);
            }

            var future = CardUtil.FindField(vat.GetType(), "FutureLiquidContained");
            future?.SetValue(vat, brineData);

            RefreshCardVisuals(liquidCard);
            RefreshCardVisuals(vat);
            Logger?.LogDebug("[MakeBrine] Liquid swapped to brine");
        }

        private static void RefreshCardVisuals(object card)
        {
            try
            {
                var visualsField = CardUtil.FindField(card.GetType(), "CardVisuals");
                var visuals = visualsField?.GetValue(card);
                if (visuals == null) return;

                var setup = visuals.GetType().GetMethods(All)
                    .FirstOrDefault(m => m.Name == "Setup" && m.GetParameters().Length == 1
                        && m.GetParameters()[0].ParameterType.IsInstanceOfType(card));
                setup?.Invoke(visuals, new[] { card });
            }
            catch (Exception ex) { Logger?.LogError($"[MakeBrine] RefreshCardVisuals: {ex.InnerException?.Message ?? ex.Message}"); }
        }

        private static bool IsMakeBrine(object action)
        {
            try
            {
                var key = CardUtil.GetActionLocalizationKey(action);
                if (key == MakeBrineKey) return true;
                return CardUtil.GetActionName(action) == "Make Brine";
            }
            catch { return false; }
        }
    }
}
