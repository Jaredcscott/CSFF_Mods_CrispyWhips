using System;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace Herbs_And_Fungi.Patcher
{
    /// <summary>
    /// Two responsibilities, now routed through Api.ActionRouter (Tier 2):
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

        public static void ApplyPatch(Harmony _harmony)
        {
            try
            {
                ActionRouter.Register(new ActionHandler
                {
                    Name = "MakeBrine",
                    CardUid = PicklingVatID,
                    ActionKeyPrefix = MakeBrineKey,
                    ActionNamePrefix = "Make Brine",
                    Timing = ActionTiming.Before,
                    Before = ctx =>
                    {
                        SwapLiquidToBrine(ctx.Card);
                        return false;
                    }
                });

                ActionRouter.Register(new ActionHandler
                {
                    Name = "PickleBpGate",
                    CardPredicate = ctx => CardUtil.GetCardUniqueId(ctx.Card) == PicklingVatID
                                       && IsPickleBp(ctx.Action),
                    Timing = ActionTiming.Cancel,
                    Before = ctx =>
                    {
                        var liquidUid = GetContainedLiquidUid(ctx.Card);
                        if (liquidUid == BrineID) return false;
                        Logger?.LogDebug($"[PickleBpGate] Blocked: liquid='{liquidUid ?? "none"}' on {ctx.CardUid} — pickle recipes require brine.");
                        return true;
                    }
                });
            }
            catch (Exception ex) { Logger?.LogError($"[PickleVat] ApplyPatch: {ex.InnerException?.ToString() ?? ex.ToString()}"); }
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
    }
}
