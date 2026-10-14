using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace Herbs_And_Fungi.Patcher
{
    /// <summary>
    /// Three responsibilities, routed through Api.ActionRouter (Tier 2). All handlers are
    /// registered once per process from Plugin.RegisterPatches (no OnGMInitialized subscription,
    /// so no Unregister is needed - CLAUDE.md "Per-Run Event Registration", pattern b):
    /// 1) On Make Brine: swap the contained liquid (water → pickle brine) using the in-place
    ///    CardModel pattern. Vat itself stays as PicklingVat.
    /// 2) On Spice Brine (feature 26): a Cancel handler. Dried wild garlic or dried ginger dragged
    ///    onto a vat holding PLAIN brine swaps the liquid to spiced brine (and the CI then consumes
    ///    the spice); on anything else it cancels, so the spice is not spent.
    /// 3) Pickle BP gate: if a player picks a pickle blueprint while the vat's contained liquid
    ///    is not the brine that blueprint requires, block execution. Quantity gate alone (≥500)
    ///    can't tell water from brine, so we enforce liquid type here in code. The tables below map
    ///    each blueprint (UID and CardName key) to its required brine: a pickle blueprint missing
    ///    from the tables is UNGATED, so every spiced blueprint MUST be listed.
    /// </summary>
    public static class PickleVatRoutePatch
    {
        private const string PicklingVatID = "herbs_fungi_pickling_vat";
        private const string BrineID       = "herbs_fungi_pickle_brine";
        private const string SpicedBrineID = "herbs_fungi_spiced_pickle_brine";
        private const string MakeBrineKey  = "Herbs_And_Fungi_PicklingVat_MakeBrine_ActionName";
        private const string SpiceBrineKey = "Herbs_And_Fungi_PicklingVat_SpiceBrine_ActionName";

        // Blueprint UID -> the brine the vat must hold (plain for the four plain recipes, spiced for the four spiced).
        private static readonly Dictionary<string, string> PickleBpUids = new Dictionary<string, string>
        {
            { "herbs_fungi_bp_pickle_frogs", BrineID },
            { "herbs_fungi_bp_pickle_mushrooms", BrineID },
            { "herbs_fungi_bp_pickle_meat", BrineID },
            { "herbs_fungi_bp_pickle_vegetables", BrineID },
            { "herbs_fungi_bp_spiced_pickle_frogs", SpicedBrineID },
            { "herbs_fungi_bp_spiced_pickle_mushrooms", SpicedBrineID },
            { "herbs_fungi_bp_spiced_pickle_meat", SpicedBrineID },
            { "herbs_fungi_bp_spiced_pickle_vegetables", SpicedBrineID },
        };

        // Blueprint CardName key -> required brine (same table, keyed by the name key the action carries).
        private static readonly Dictionary<string, string> PickleBpNameKeys = new Dictionary<string, string>
        {
            { "Herbs_And_Fungi_Bp_PickleFrogs_CardName", BrineID },
            { "Herbs_And_Fungi_Bp_PickleMushrooms_CardName", BrineID },
            { "Herbs_And_Fungi_Bp_PickleMeat_CardName", BrineID },
            { "Herbs_And_Fungi_Bp_PickleVegetables_CardName", BrineID },
            { "Herbs_And_Fungi_Bp_SpicedPickleFrogs_CardName", SpicedBrineID },
            { "Herbs_And_Fungi_Bp_SpicedPickleMushrooms_CardName", SpicedBrineID },
            { "Herbs_And_Fungi_Bp_SpicedPickleMeat_CardName", SpicedBrineID },
            { "Herbs_And_Fungi_Bp_SpicedPickleVegetables_CardName", SpicedBrineID },
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
                        SwapLiquid(ctx.Card, BrineID);
                        return false;
                    }
                });

                ActionRouter.Register(new ActionHandler
                {
                    Name = "SpiceBrine",
                    CardUid = PicklingVatID,
                    ActionKeyPrefix = SpiceBrineKey,
                    Timing = ActionTiming.Cancel,
                    Before = ctx =>
                    {
                        var liquidUid = GetContainedLiquidUid(ctx.Card);
                        if (liquidUid != BrineID)
                        {
                            Logger?.LogDebug($"[SpiceBrine] Blocked: liquid='{liquidUid ?? "none"}' - spice only works on plain pickle brine.");
                            return true;
                        }
                        SwapLiquid(ctx.Card, SpicedBrineID);
                        return false;
                    }
                });

                ActionRouter.Register(new ActionHandler
                {
                    Name = "PickleBpGate",
                    CardPredicate = ctx => CardUtil.GetCardUniqueId(ctx.Card) == PicklingVatID
                                       && RequiredBrineFor(ctx.Action) != null,
                    Timing = ActionTiming.Cancel,
                    Before = ctx =>
                    {
                        var required = RequiredBrineFor(ctx.Action);
                        var liquidUid = GetContainedLiquidUid(ctx.Card);
                        if (required != null && liquidUid == required) return false;
                        Logger?.LogDebug($"[PickleBpGate] Blocked: liquid='{liquidUid ?? "none"}' on {ctx.CardUid} - this recipe requires '{required}'.");
                        return true;
                    }
                });
            }
            catch (Exception ex) { Logger?.LogError($"[PickleVat] ApplyPatch: {ex.InnerException?.ToString() ?? ex.ToString()}"); }
        }

        // The brine UID this blueprint requires, or null when the action is not one of the tables' pickle blueprints.
        private static string RequiredBrineFor(object actionOrCard)
        {
            if (actionOrCard == null) return null;
            string required;
            var uid = CardUtil.GetCardUniqueId(actionOrCard);
            if (uid != null && PickleBpUids.TryGetValue(uid, out required)) return required;
            var key = CardUtil.GetActionLocalizationKey(actionOrCard);
            if (key != null && PickleBpNameKeys.TryGetValue(key, out required)) return required;
            var cardName = CardUtil.GetMemberValue(actionOrCard, "CardName");
            key = CardUtil.GetMemberValue(cardName, "LocalizationKey") as string;
            if (key != null && PickleBpNameKeys.TryGetValue(key, out required)) return required;
            return null;
        }

        // Warn once per cause: the pickle gate and Spice Brine fail CLOSED when the vat's liquid cannot be
        // read, so a game-update rename would silently disable every pickle blueprint without this line.
        private static readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);

        private static void WarnOnce(string cause, string message)
        {
            if (_warned.Add(cause)) Logger?.LogWarning("[PickleVat] " + message);
        }

        private static string GetContainedLiquidUid(object vat)
        {
            if (vat == null) return null;
            // CardUtil.GetCachedField caches per (Type, name); a null here means the field is gone, not empty.
            var liquidField = CardUtil.GetCachedField(vat.GetType(), "ContainedLiquid");
            if (liquidField == null)
            {
                WarnOnce("no-contained-liquid", $"{vat.GetType().Name}.ContainedLiquid not found (a game update may have renamed it); every pickle blueprint and Spice Brine will stay blocked.");
                return null;
            }
            var liquidCard = liquidField.GetValue(vat);
            if (liquidCard == null) return null; // an empty vat: normal, the gate blocks with its own Debug line
            var uid = CardUtil.GetCardUniqueId(liquidCard);
            if (uid == null)
                WarnOnce("liquid-uid-unreadable", $"the vat's liquid ({liquidCard.GetType().Name}) has no readable UniqueID; every pickle blueprint and Spice Brine will stay blocked.");
            return uid;
        }

        private static void SwapLiquid(object vat, string targetUid)
        {
            var brineData = CardUtil.GetCardDataById(targetUid);
            if (brineData == null) { Logger?.LogError($"[MakeBrine] CardData not found: {targetUid}"); return; }

            var liquidField = CardUtil.FindField(vat.GetType(), "ContainedLiquid");
            if (liquidField == null) { Logger?.LogError("[MakeBrine] ContainedLiquid field not found on vat"); return; }

            var liquidCard = liquidField.GetValue(vat);
            if (liquidCard == null) { Logger?.LogError("[MakeBrine] vat has no ContainedLiquid to swap"); return; }

            var setModel = liquidCard.GetType().GetMethod("SetModel", All, null, new[] { brineData.GetType() }, null);
            if (setModel != null)
            {
                try { setModel.Invoke(liquidCard, new[] { brineData }); }
                catch (Exception ex) { Logger?.LogError($"[MakeBrine] SetModel threw: {ex.InnerException?.ToString() ?? ex.ToString()}"); return; }
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
            Logger?.LogDebug($"[MakeBrine] Liquid swapped to {targetUid}");
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
            catch (Exception ex) { Logger?.LogError($"[MakeBrine] RefreshCardVisuals: {ex.InnerException?.ToString() ?? ex.ToString()}"); }
        }
    }
}
