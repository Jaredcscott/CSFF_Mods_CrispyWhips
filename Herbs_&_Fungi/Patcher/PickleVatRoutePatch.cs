using System;
using System.Collections;
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
    /// Two responsibilities, routed through Api.ActionRouter (Tier 2). Both handlers are
    /// registered once per process from Plugin.RegisterPatches (no OnGMInitialized subscription,
    /// so no Unregister is needed - CLAUDE.md "Per-Run Event Registration", pattern b):
    /// 1) On Spice Brine (feature 26): a Cancel handler. Dried wild garlic or dried ginger dragged
    ///    onto a vat holding plain BRINE swaps the liquid to spiced brine (and the CI then consumes
    ///    the spice); on anything else it cancels, so the spice is not spent.
    /// 2) Pickle BP gate: if a player picks a pickle blueprint while the vat's contained liquid
    ///    is not the brine that blueprint requires, block execution. Quantity gate alone (>=500)
    ///    can't tell water from brine, so we enforce liquid type here in code. The tables below map
    ///    each blueprint (UID and CardName key) to its required brine: a pickle blueprint missing
    ///    from the tables is UNGATED, so every spiced blueprint MUST be listed.
    /// PLAIN BRINE is the framework's Brine (framework 2.29.0, fleet plan 5.4, owner decision K2 "One
    /// Brine"): H&F's own Pickle Brine is retired and aliased to APE_LQ_SaltWater in UidAliases.json,
    /// and the framework's salt-on-water interaction makes it (it shadows the old Make Brine action,
    /// which is gone). The plain gate reads MEMBERSHIP of the framework group GpTag_FW_Brine (or the
    /// interop tag tag_x_Brine), never a UID: a saved vat's brine loads under the alias with the
    /// live UniqueID APE_LQ_SaltWater, and Additional Processing Equipment ships its own copy of that
    /// card with no tag, which the group lists by UID. A UID compare here would fail closed.
    /// </summary>
    public static class PickleVatRoutePatch
    {
        private const string PicklingVatID = "herbs_fungi_pickling_vat";
        private const string SpicedBrineID = "herbs_fungi_spiced_pickle_brine";
        private const string SpiceBrineKey = "Herbs_And_Fungi_PicklingVat_SpiceBrine_ActionName";

        /// <summary>The framework group that holds every plain brine (Interop Registry R29).</summary>
        internal const string BrineGroupName = "GpTag_FW_Brine";
        /// <summary>The interop tag a brine carries to join that group.</summary>
        internal const string BrineTag = "tag_x_Brine";
        // Table value meaning "any member of the brine group" (never a card UID).
        private const string PlainBrine = "group:" + BrineGroupName;

        // Blueprint UID -> the brine the vat must hold (plain for the four plain recipes, spiced for the four spiced).
        private static readonly Dictionary<string, string> PickleBpUids = new Dictionary<string, string>
        {
            { "herbs_fungi_bp_pickle_frogs", PlainBrine },
            { "herbs_fungi_bp_pickle_mushrooms", PlainBrine },
            { "herbs_fungi_bp_pickle_meat", PlainBrine },
            { "herbs_fungi_bp_pickle_vegetables", PlainBrine },
            { "herbs_fungi_bp_spiced_pickle_frogs", SpicedBrineID },
            { "herbs_fungi_bp_spiced_pickle_mushrooms", SpicedBrineID },
            { "herbs_fungi_bp_spiced_pickle_meat", SpicedBrineID },
            { "herbs_fungi_bp_spiced_pickle_vegetables", SpicedBrineID },
        };

        // Blueprint CardName key -> required brine (same table, keyed by the name key the action carries).
        private static readonly Dictionary<string, string> PickleBpNameKeys = new Dictionary<string, string>
        {
            { "Herbs_And_Fungi_Bp_PickleFrogs_CardName", PlainBrine },
            { "Herbs_And_Fungi_Bp_PickleMushrooms_CardName", PlainBrine },
            { "Herbs_And_Fungi_Bp_PickleMeat_CardName", PlainBrine },
            { "Herbs_And_Fungi_Bp_PickleVegetables_CardName", PlainBrine },
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
                    Name = "SpiceBrine",
                    CardUid = PicklingVatID,
                    ActionKeyPrefix = SpiceBrineKey,
                    Timing = ActionTiming.Cancel,
                    Before = ctx =>
                    {
                        var liquid = GetContainedLiquid(ctx.Card);
                        if (!IsBrine(liquid))
                        {
                            Logger?.LogDebug($"[SpiceBrine] Blocked: liquid='{CardUtil.GetCardUniqueId(liquid) ?? "none"}' - spice only works on plain brine ({BrineGroupName}).");
                            return true;
                        }
                        SwapLiquid(ctx.Card, SpicedBrineID);
                        return false;
                    }
                });

                ActionRouter.Register(new ActionHandler
                {
                    Name = "PickleBpGate",
                    CardPredicate = ctx => CardUtil.GetCardUniqueId(ctx.Card) == PicklingVatID && RequiredBrineFor(ctx.Action) != null,
                    Timing = ActionTiming.Cancel,
                    Before = ctx =>
                    {
                        var required = RequiredBrineFor(ctx.Action);
                        var liquid = GetContainedLiquid(ctx.Card);
                        if (required != null && LiquidMeets(required, liquid)) return false;
                        Logger?.LogDebug($"[PickleBpGate] Blocked: liquid='{CardUtil.GetCardUniqueId(liquid) ?? "none"}' - this recipe requires '{required}'.");
                        return true;
                    }
                });

                // The handler above never sees a pickle build: the game builds a blueprint in
                // InGameCardBase.PerformBlueprintBuild on the BLUEPRINT card sitting in the vat, and that
                // routine clears the ingredients and runs the vat's result action itself, so cancelling its
                // inner Build action only half-stops it. Measured 2026-10-30 (harness probe, Prompt 21): on
                // 1.14.17 and on a Build-action gate alike, Pickle Meat built on a vat of plain water. The
                // liquid check therefore runs before the build starts: a refused build does nothing at all.
                var build = AccessTools.Method(typeof(InGameCardBase), "PerformBlueprintBuild");
                if (build == null) Logger?.LogWarning("[PickleBpGate] InGameCardBase.PerformBlueprintBuild not found: the pickle recipes are not liquid-gated on this game version.");
                else _harmony.Patch(build, prefix: new HarmonyMethod(typeof(PickleVatRoutePatch), nameof(PerformBlueprintBuild_Prefix)));
            }
            catch (Exception ex) { Logger?.LogError($"[PickleVat] ApplyPatch: {ex.InnerException?.ToString() ?? ex.ToString()}"); }
        }

        // The brine this blueprint requires (PlainBrine or a UID), or null when the action is not one of the tables' pickle blueprints.
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

        // Refuses a pickle blueprint build in a vat whose liquid is not the brine the recipe needs: the
        // build routine is replaced by an empty one, so nothing is spent, cleared or produced.
        private static bool PerformBlueprintBuild_Prefix(InGameCardBase __instance, ref IEnumerator __result)
        {
            try
            {
                var required = RequiredBrineFor(__instance);
                if (required == null) return true;
                var vat = __instance.CurrentContainer;
                if (vat == null || CardUtil.GetCardUniqueId(vat) != PicklingVatID) return true;
                var liquid = GetContainedLiquid(vat);
                if (LiquidMeets(required, liquid)) return true;
                Logger?.LogInfo($"[PickleBpGate] Refused {CardUtil.GetCardUniqueId(__instance)}: the vat holds '{CardUtil.GetCardUniqueId(liquid) ?? "nothing"}', this recipe needs '{required}'.");
                __result = NoBuild();
                // The popup reads LastBlueprintBuildCost right after starting the build to animate its bar;
                // the original routine zeroes it first. Best effort: a miss only animates the last cost.
                try { BuildCostField?.SetValue(__instance, 0); } catch (Exception ex) { Logger?.LogDebug($"[PickleBpGate] cost reset: {ex.Message}"); }
                return false;
            }
            catch (Exception ex)
            {
                Logger?.LogWarning($"[PickleBpGate] build check failed, build allowed: {ex.Message}");
                return true;
            }
        }

        private static IEnumerator NoBuild() { yield break; }

        // Backing field of the auto-property (its private setter is not reachable through Traverse here).
        private static readonly FieldInfo BuildCostField = AccessTools.Field(typeof(InGameCardBase), "<LastBlueprintBuildCost>k__BackingField");

        private static bool LiquidMeets(string required, object liquid)
        {
            if (liquid == null) return false;
            if (required == PlainBrine) return IsBrine(liquid);
            return CardUtil.GetCardUniqueId(liquid) == required;
        }

        /// <summary>
        /// Whether a contained liquid card is plain brine: its CardData carries tag_x_Brine, or the
        /// framework group GpTag_FW_Brine lists it (the group is every tagged card plus the framework
        /// Brine by UID, so APE's untagged copy of APE_LQ_SaltWater counts too). Spiced brine is in neither.
        /// </summary>
        internal static bool IsBrine(object liquid)
        {
            if (liquid == null) return false;
            var model = CardUtil.GetCardData(liquid);
            if (model == null) return false;
            if (CardUtil.GetMemberValue(model, "CardTags") is IList tags)
            {
                foreach (var t in tags)
                    if (t is UnityEngine.Object tag && tag != null && tag.name == BrineTag) return true;
            }
            var group = GpTagContentPatch.FrameworkBrineGroup;
            if (group == null)
            {
                WarnOnce("no-brine-group", $"the framework group {BrineGroupName} was not found at load (CSFF Mod Framework older than 2.29.0?); only a liquid carrying {BrineTag} counts as brine.");
                return false;
            }
            var included = CardUtil.GetCachedField(group.GetType(), "IncludedCards");
            if (included == null)
            {
                WarnOnce("no-included-cards", $"{group.GetType().Name}.IncludedCards not found (a game update may have renamed it); only a liquid carrying {BrineTag} counts as brine.");
                return false;
            }
            return included.GetValue(group) is IList members && members.Contains(model);
        }

        // Warn once per cause: the pickle gate and Spice Brine fail CLOSED when the vat's liquid cannot be
        // read, so a game-update rename would silently disable every pickle blueprint without this line.
        private static readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);

        private static void WarnOnce(string cause, string message)
        {
            if (_warned.Add(cause)) Logger?.LogWarning("[PickleVat] " + message);
        }

        private static object GetContainedLiquid(object vat)
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
            if (CardUtil.GetCardUniqueId(liquidCard) == null)
                WarnOnce("liquid-uid-unreadable", $"the vat's liquid ({liquidCard.GetType().Name}) has no readable UniqueID; every pickle blueprint and Spice Brine will stay blocked.");
            return liquidCard;
        }

        private static void SwapLiquid(object vat, string targetUid)
        {
            var brineData = CardUtil.GetCardDataById(targetUid);
            if (brineData == null) { Logger?.LogError($"[SpiceBrine] CardData not found: {targetUid}"); return; }

            var liquidField = CardUtil.FindField(vat.GetType(), "ContainedLiquid");
            if (liquidField == null) { Logger?.LogError("[SpiceBrine] ContainedLiquid field not found on vat"); return; }

            var liquidCard = liquidField.GetValue(vat);
            if (liquidCard == null) { Logger?.LogError("[SpiceBrine] vat has no ContainedLiquid to swap"); return; }

            var setModel = liquidCard.GetType().GetMethod("SetModel", All, null, new[] { brineData.GetType() }, null);
            if (setModel != null)
            {
                try { setModel.Invoke(liquidCard, new[] { brineData }); }
                catch (Exception ex) { Logger?.LogError($"[SpiceBrine] SetModel threw: {ex.InnerException?.ToString() ?? ex.ToString()}"); return; }
            }
            else
            {
                if (!CardUtil.TrySetCardModel(liquidCard, brineData))
                {
                    Logger?.LogError($"[SpiceBrine] CardModel not settable on {liquidCard.GetType().Name}");
                    return;
                }
                CardUtil.ReinitCard(liquidCard, brineData);
            }

            var future = CardUtil.FindField(vat.GetType(), "FutureLiquidContained");
            if (future == null)
                WarnOnce("no-future-liquid", $"{vat.GetType().Name}.FutureLiquidContained not found (a game update may have renamed it); the brine swap may not persist across the next vat tick.");
            else
                future.SetValue(vat, brineData);

            RefreshCardVisuals(liquidCard);
            RefreshCardVisuals(vat);
            Logger?.LogDebug($"[SpiceBrine] Liquid swapped to {targetUid}");
        }

        private static void RefreshCardVisuals(object card)
        {
            try
            {
                var visualsField = CardUtil.FindField(card.GetType(), "CardVisuals");
                if (visualsField == null)
                {
                    WarnOnce("no-card-visuals", $"{card.GetType().Name}.CardVisuals not found (a game update may have renamed it); the vat will show its old liquid until it is redrawn.");
                    return;
                }
                // Null for a card that is not on screen: nothing to redraw, so this stays silent.
                var visuals = visualsField.GetValue(card);
                if (visuals == null) return;

                var setup = visuals.GetType().GetMethods(All)
                    .FirstOrDefault(m => m.Name == "Setup" && m.GetParameters().Length == 1
                        && m.GetParameters()[0].ParameterType.IsInstanceOfType(card));
                if (setup == null)
                {
                    WarnOnce("no-visuals-setup", $"{visuals.GetType().Name}.Setup({card.GetType().Name}) not found (a game update may have changed it); the vat will show its old liquid until it is redrawn.");
                    return;
                }
                setup.Invoke(visuals, new[] { card });
            }
            catch (Exception ex) { Logger?.LogError($"[SpiceBrine] RefreshCardVisuals: {ex.InnerException?.ToString() ?? ex.ToString()}"); }
        }
    }
}
