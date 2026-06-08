using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using BepInEx.Logging;
using CSFFModFramework.Util;

namespace Advanced_Copper_Tools.Patcher
{
    /// <summary>
    /// Handles the Tea Blending Station's "Grind All" DismantleAction.
    /// Every tag_Millable card in the station's inventory is transformed
    /// in-place to its ground variant by reading each card's own Grind
    /// CardInteraction (tag_GrindingTool / tag_HammeringToolGeneral trigger).
    /// Uses in-place CardModel swap (WDI sluice pattern) to avoid OnDestroy
    /// relocation that occurs with DeactivateAndDestroy.
    /// </summary>
    public static class TeaStationPatch
    {
        private static ManualLogSource Logger => Plugin.Logger;
        private static readonly BindingFlags Flags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private const string StationUnlitID = "advanced_copper_tools_tea_blending_station";
        private const string StationLitID   = "advanced_copper_tools_tea_blending_station_lit";
        private const string GrindAllName   = "Grind All";
        private const string DrawBoiledWaterName = "Draw Boiled Water";
        private const float  BoiledWaterTemperature = 200f;

        private const string GrindingToolTag = "tag_GrindingTool";
        private const string HammerToolTag   = "tag_HammeringToolGeneral";

        // No local reflection caches — CardUtil handles all card identity, inventory, action name,
        // in-place model swap, and GetFromID lookups.

        private static readonly string[] _durabilityCurrentFields =
        {
            "CurrentSpoilage", "CurrentUsageDurability", "CurrentFuel", "CurrentProgress",
            "CurrentSpecial1", "CurrentSpecial2", "CurrentSpecial3", "CurrentSpecial4"
        };

        private static readonly string[] _durabilityModelFields =
        {
            "SpoilageTime", "UsageDurability", "FuelCapacity", "Progress",
            "SpecialDurability1", "SpecialDurability2", "SpecialDurability3", "SpecialDurability4"
        };

        private static readonly string[] _durabilityRateFields =
        {
            "BaseSpoilageRate", "BaseUsageRate", "BaseFuelRate", "BaseConsumableRate",
            "BaseSpecial1Rate", "BaseSpecial2Rate", "BaseSpecial3Rate", "BaseSpecial4Rate"
        };

        private static readonly string[] _durabilityLatchFields =
        {
            "SpoilFull", "SpoilEmpty", "UsageFull", "UsageEmpty", "FuelFull", "FuelEmpty",
            "ProgressFull", "ProgressEmpty", "Special1Full", "Special1Empty", "Special2Full",
            "Special2Empty", "Special3Full", "Special3Empty", "Special4Full", "Special4Empty",
            "LiquidEmpty"
        };

        private static int _lastGrindFrame = -1;

        public static void ApplyPatch(Harmony harmony)
        {
            try
            {
                var gmType = AccessTools.TypeByName("GameManager");
                if (gmType == null)
                {
                    Logger?.LogError("[TeaStation] GameManager type not found");
                    return;
                }

                var actionRoutine = FindActionRoutine(gmType);
                var cardOnCardRoutine = FindCardOnCardActionRoutine(gmType);
                if (actionRoutine != null)
                {
                    TryPatch(
                        harmony,
                        actionRoutine,
                        "ActionRoutine prefix",
                        prefix: new HarmonyMethod(typeof(TeaStationPatch), nameof(ActionRoutine_Prefix)) { priority = Priority.High });

                    if (cardOnCardRoutine == null)
                    {
                        TryPatch(
                            harmony,
                            actionRoutine,
                            "ActionRoutine postfix",
                            postfix: new HarmonyMethod(typeof(TeaStationPatch), nameof(ActionRoutine_Postfix)));
                    }
                }
                else
                {
                    Logger?.LogError("[TeaStation] ActionRoutine method not found on GameManager");
                }

                if (cardOnCardRoutine != null)
                {
                    TryPatch(
                        harmony,
                        cardOnCardRoutine,
                        "CardOnCardActionRoutine postfix",
                        postfix: new HarmonyMethod(typeof(TeaStationPatch), nameof(CardOnCardActionRoutine_Postfix)));
                }

                var stackRoutine = AccessTools.Method(gmType, "PerformStackActionRoutine");
                if (stackRoutine != null)
                {
                    TryPatch(
                        harmony,
                        stackRoutine,
                        "PerformStackActionRoutine prefix",
                        prefix: new HarmonyMethod(typeof(TeaStationPatch), nameof(PerformStackAction_Prefix)) { priority = Priority.High });
                }
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[TeaStation] Patch failed: {FullException(ex)}");
            }
        }

        private static bool TryPatch(Harmony harmony, MethodInfo method, string label, HarmonyMethod prefix = null, HarmonyMethod postfix = null)
        {
            try
            {
                harmony.Patch(method, prefix: prefix, postfix: postfix);
                Logger?.LogDebug($"[TeaStation] {label} patched");
                return true;
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[TeaStation] Failed to patch {label}: {FullException(ex)}");
                return false;
            }
        }

        private static MethodInfo FindActionRoutine(Type gmType)
            => CardUtil.FindMethodBySignature(gmType, "ActionRoutine", "CardAction", "InGameCardBase");

        private static MethodInfo FindCardOnCardActionRoutine(Type gmType)
            => CardUtil.FindMethodBySignature(gmType, "CardOnCardActionRoutine", "CardOnCardAction", "InGameCardBase", "InGameCardBase");

        // DismantleAction dispatch: PerformStackActionRoutine(CardAction, List<InGameCardBase>, ...)
        static bool PerformStackAction_Prefix(object __0, object __1)
        {
            try
            {
                if (__0 == null || __1 == null) return true;
                var cardList = __1 as IList;
                if (cardList == null || cardList.Count == 0) return true;

                object receivingCard = cardList[0];
                if (receivingCard == null) return true;
                if (!IsGrindAllOnStation(__0, receivingCard)) return true;
                return HandleGrindAll(receivingCard);
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[TeaStation] StackPrefix error: {ex}");
                return true;
            }
        }

        // Single-card dispatch fallback: ActionRoutine(CardAction, ReceivingCard, ...)
        static bool ActionRoutine_Prefix(object __0, object __1)
        {
            try
            {
                if (__0 == null || __1 == null) return true;
                if (!IsGrindAllOnStation(__0, __1)) return true;
                return HandleGrindAll(__1);
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[TeaStation] ActionRoutine prefix error: {ex}");
                return true;
            }
        }

        private static bool IsGrindAllOnStation(object action, object card)
        {
            string actionName = CardUtil.GetActionName(action);
            if (!string.Equals(actionName, GrindAllName, StringComparison.Ordinal)) return false;

            string uid = CardUtil.GetCardUniqueId(card);
            return uid == StationUnlitID || uid == StationLitID;
        }

        // ============================================================
        //  GRIND ALL HANDLER
        // ============================================================
        private static bool HandleGrindAll(object station)
        {
            if (_lastGrindFrame == UnityEngine.Time.frameCount) return false;
            _lastGrindFrame = UnityEngine.Time.frameCount;
            return HandleGrindAllInner(station);
        }

        private static bool HandleGrindAllInner(object station)
        {
            var inventory = CardUtil.GetInventoryList(station);
            if (inventory == null || inventory.Count == 0)
            {
                Logger?.Log(LogLevel.Debug, "[TeaStation] GrindAll: empty station inventory");
                return false;
            }

            // Collect individual cards that are millable, paired with their ground result UID.
            // The station holds InventorySlot wrappers; each slot holds 1+ stacked herb cards.
            var toGrind = new List<(object card, string resultId)>();

            foreach (var slotItem in inventory)
            {
                if (slotItem == null) continue;

                // Try to get inner cards from the slot (InventorySlot wraps stacked cards)
                var innerCards = CardUtil.GetInventoryList(slotItem);
                if (innerCards != null && innerCards.Count > 0)
                {
                    foreach (var inner in innerCards)
                    {
                        if (inner == null) continue;
                        string resultId = ResolveGrindResult(inner);
                        if (resultId != null)
                            toGrind.Add((inner, resultId));
                    }
                }
                else
                {
                    // slotItem might itself be a card (no wrapper layer)
                    string resultId = ResolveGrindResult(slotItem);
                    if (resultId != null)
                        toGrind.Add((slotItem, resultId));
                }
            }

            if (toGrind.Count == 0)
            {
                Logger?.Log(LogLevel.Debug, "[TeaStation] GrindAll: no millable items in inventory");
                return false;
            }

            int transformed = 0;
            foreach (var (card, resultId) in toGrind)
            {
                if (TransformCardInPlace(card, resultId))
                    transformed++;
            }

            Logger?.LogDebug($"[TeaStation] GrindAll: transformed {transformed}/{toGrind.Count} held card(s)");

            return false; // block the no-op JSON action; we handled everything
        }

        // ============================================================
        //  IN-PLACE CARD TRANSFORM
        // ============================================================
        private static bool TransformCardInPlace(object card, string targetUniqueId)
        {
            if (card == null || string.IsNullOrEmpty(targetUniqueId)) return false;
            try
            {
                var targetData = CardUtil.GetCardDataById(targetUniqueId);
                if (targetData == null)
                {
                    Logger?.LogError($"[TeaStation] Transform: CardData not found for '{targetUniqueId}'");
                    return false;
                }

                if (!CardUtil.TrySetCardModel(card, targetData))
                {
                    Logger?.LogError($"[TeaStation] Transform: CardModel not settable on {card.GetType().Name}");
                    return false;
                }

                var placement = CapturePlacement(card);
                CardUtil.ReinitCard(card, targetData);
                ResetRuntimeStateForNewModel(card, targetData);
                RestorePlacementIfNeeded(card, placement);

                Logger?.Log(LogLevel.Debug, $"[TeaStation] Transformed → {targetUniqueId}");
                return true;
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[TeaStation] Transform failed: {FullException(ex)}");
                return false;
            }
        }

        private sealed class PlacementSnapshot
        {
            public object Container;
            public object Slot;
            public object SlotInfo;
        }

        private static PlacementSnapshot CapturePlacement(object card)
        {
            return new PlacementSnapshot
            {
                Container = CardUtil.GetMemberValue(card, "CurrentContainer"),
                Slot = CardUtil.GetMemberValue(card, "CurrentSlot"),
                SlotInfo = CardUtil.GetMemberValue(card, "CurrentSlotInfo")
            };
        }

        private static void RestorePlacementIfNeeded(object card, PlacementSnapshot placement)
        {
            if (card == null || placement == null) return;

            try
            {
                if (placement.Container != null && CardUtil.GetMemberValue(card, "CurrentContainer") == null)
                {
                    var setContainer = card.GetType().GetMethod("SetCurrentContainer", Flags);
                    if (setContainer != null)
                        setContainer.Invoke(card, new[] { placement.Container });
                    else
                        CardUtil.SetMemberValue(card, "<CurrentContainer>k__BackingField", placement.Container);
                }

                if (placement.Slot != null && CardUtil.GetMemberValue(card, "CurrentSlot") == null)
                    CardUtil.SetMemberValue(card, "CurrentSlot", placement.Slot);

                if (placement.SlotInfo != null && CardUtil.GetMemberValue(card, "CurrentSlotInfo") == null)
                    CardUtil.SetMemberValue(card, "CurrentSlotInfo", placement.SlotInfo);
            }
            catch (Exception ex)
            {
                Logger?.Log(LogLevel.Debug, $"[TeaStation] RestorePlacement failed: {FullException(ex)}");
            }
        }

        private static void ResetRuntimeStateForNewModel(object card, object targetData)
        {
            if (card == null || targetData == null) return;

            try
            {
                for (int i = 0; i < _durabilityCurrentFields.Length; i++)
                {
                    object stat = CardUtil.GetMemberValue(targetData, _durabilityModelFields[i]);
                    bool active = IsDurabilityActive(stat);
                    CardUtil.SetMemberValue(card, _durabilityCurrentFields[i], active ? GenerateStartingValue(stat) : 0f);
                    CardUtil.SetMemberValue(card, _durabilityRateFields[i], active ? GetFloatMember(stat, "RatePerDaytimePoint") : 0f);
                }

                CardUtil.SetMemberValue(card, "CurrentLiquidQuantity", 0f);
                CardUtil.SetMemberValue(card, "BaseEvaporationRate", 0f);
                CardUtil.SetMemberValue(card, "IgnoreTickDurabilityChanges", false);

                foreach (var fieldName in _durabilityLatchFields)
                    CardUtil.SetMemberValue(card, fieldName, false);
            }
            catch (Exception ex)
            {
                Logger?.Log(LogLevel.Debug, $"[TeaStation] ResetRuntimeState failed: {FullException(ex)}");
            }
        }

        private static bool IsDurabilityActive(object stat)
        {
            if (stat == null) return false;
            object active = CardUtil.GetMemberValue(stat, "Active");
            return active is bool b && b;
        }

        private static float GenerateStartingValue(object stat)
        {
            if (stat == null) return 0f;
            try
            {
                var method = stat.GetType().GetMethod("GenerateStartingValue", Flags, null, Type.EmptyTypes, null);
                var value = method?.Invoke(stat, null);
                if (value is float f) return f;
                if (value is double d) return (float)d;
                if (value is int i) return i;
            }
            catch (Exception ex)
            {
                Logger?.Log(LogLevel.Debug, $"[TeaStation] GenerateStartingValue failed: {FullException(ex)}");
            }
            return GetFloatMember(stat, "FloatValue");
        }

        private static float GetFloatMember(object instance, string name)
        {
            object value = CardUtil.GetMemberValue(instance, name);
            if (value is float f) return f;
            if (value is double d) return (float)d;
            if (value is int i) return i;
            return 0f;
        }

        // ============================================================
        //  GRIND RESULT RESOLUTION
        // ============================================================

        /// <summary>
        /// Finds the card's Grind CardInteraction (trigger tag = tag_GrindingTool or
        /// tag_HammeringToolGeneral) and returns the target UniqueID, or null if the
        /// card has no such interaction.
        ///
        /// We do NOT require tag_Millable: vanilla grindable herbs (Dry Frostleaf,
        /// Dry Appleweed, Wheat Grains, Edible Acorns, etc.) lack that tag — they're
        /// identified solely by carrying a CardInteraction with the grinding-tool
        /// trigger. The slot's InventoryFilter already pre-screens what gets in.
        /// </summary>
        private static string ResolveGrindResult(object card)
        {
            try
            {
                if (card == null) return null;

                object cardData = CardUtil.GetCardData(card);
                if (cardData == null) return null;

                var ciField = cardData.GetType().GetField("CardInteractions", Flags);
                if (ciField == null) return null;
                var interactions = ciField.GetValue(cardData) as IList;
                if (interactions == null) return null;

                foreach (var ci in interactions)
                {
                    if (ci == null) continue;

                    var compatField = ci.GetType().GetField("CompatibleCards", Flags);
                    var compat = compatField?.GetValue(ci);
                    if (compat == null) continue;

                    // Read resolved TriggerTags (populated by WarpResolver from TriggerTagsWarpData)
                    var tagsField = compat.GetType().GetField("TriggerTags", Flags);
                    var tags = tagsField?.GetValue(compat) as IList;

                    bool matched = false;
                    if (tags != null)
                    {
                        foreach (var t in tags)
                        {
                            if (t == null) continue;
                            // CardTag is not UniqueIDScriptable — use SO .name (tag identifier)
                            string tagName = (t is UnityEngine.Object uo) ? uo.name : null;
                            if (tagName == GrindingToolTag || tagName == HammerToolTag)
                            {
                                matched = true;
                                break;
                            }
                        }
                    }

                    string actionName = CardUtil.GetActionName(ci);
                    if (!matched && !string.Equals(actionName, "Grind", StringComparison.Ordinal))
                        continue;

                    var uid = GetTransformIntoUniqueId(ci);
                    if (!string.IsNullOrEmpty(uid)) return uid;
                }
            }
            catch (Exception ex)
            {
                Logger?.Log(LogLevel.Debug, $"[TeaStation] ResolveGrindResult error: {FullException(ex)}");
            }
            return null;
        }

        private static string GetTransformIntoUniqueId(object cardInteraction)
        {
            var rccField = cardInteraction.GetType().GetField("ReceivingCardChanges", Flags);
            var rcc = rccField?.GetValue(cardInteraction);
            if (rcc == null) return null;

            var transformField = rcc.GetType().GetField("TransformInto", Flags);
            var transformCard  = transformField?.GetValue(rcc);
            if (transformCard != null)
            {
                var uidField = transformCard.GetType().GetField("UniqueID", Flags);
                var uid = uidField?.GetValue(transformCard) as string;
                if (!string.IsNullOrEmpty(uid)) return uid;
            }

            var warpField = rcc.GetType().GetField("TransformIntoWarpData", Flags);
            return warpField?.GetValue(rcc) as string;
        }

        // ============================================================
        //  DRAW BOILED WATER — TEMPERATURE FIX
        // ============================================================
        // Fix: postfix CardOnCardActionRoutine (EA 0.63+) / ActionRoutine fallback on "Draw Boiled Water" + tea-station-lit
        // and force the given container's ContainedLiquid.CurrentFuel to max
        // after the JSON CreatedLiquidInGivenCard path fills the bowl.
        static IEnumerator CardOnCardActionRoutine_Postfix(
            IEnumerator enumerator,
            object _Action,
            object _GivenCard,
            object _ReceivingCard,
            object _User,
            bool _FastMode,
            bool _UseReceivingForSlot,
            bool _DontPlaySounds)
        {
            yield return enumerator;
            HandleDrawBoiledWater(_Action, _ReceivingCard, _GivenCard);
        }

        static IEnumerator ActionRoutine_Postfix(
            IEnumerator enumerator,
            object _Action,
            object _ReceivingCard,
            object _User,
            bool _FastMode,
            bool _DontPlaySounds,
            bool _ModifiersAlreadyCollected,
            object _GivenCard)
        {
            // Run the original coroutine to completion first so the liquid is
            // actually placed in the bowl. Iterating manually with try/catch
            // would prevent yield through finally; use the proven SkillSpeedBoost
            // pattern (yield return enumerator).
            yield return enumerator;

            HandleDrawBoiledWater(_Action, _ReceivingCard, _GivenCard);
        }

        private static void HandleDrawBoiledWater(object action, object receivingCard, object givenCard)
        {
            // Filter: must be our action on our station, with a target container.
            if (action == null || receivingCard == null || givenCard == null)
                return;

            string actionName = CardUtil.GetActionName(action);
            if (!string.Equals(actionName, DrawBoiledWaterName, StringComparison.Ordinal))
                return;

            string recvUid = CardUtil.GetCardUniqueId(receivingCard);
            if (recvUid != StationLitID && recvUid != StationUnlitID)
                return;

            Logger?.LogDebug($"[TeaStation] DrawBoiled fired: action='{actionName}' recv={recvUid}");
            ApplyBoiledWaterTemperature(givenCard);
            DrainWaterCharge(receivingCard);
        }

        // Outside the iterator so it can use try/catch around reflection.
        private static void ApplyBoiledWaterTemperature(object givenCard)
        {
            try
            {
                if (givenCard == null) return;
                var givenType = givenCard.GetType();
                var containedLiquidField = givenType.GetField("ContainedLiquid", Flags);
                if (containedLiquidField == null)
                {
                    Logger?.LogError("[TeaStation] DrawBoiled: ContainedLiquid field missing");
                    return;
                }
                var liquid = containedLiquidField.GetValue(givenCard);
                if (liquid == null)
                {
                    Logger?.LogDebug("[TeaStation] DrawBoiled: given card has no ContainedLiquid");
                    return;
                }

                var liquidType = liquid.GetType();
                var fuelField = liquidType.GetField("CurrentFuel", Flags);
                if (fuelField == null)
                {
                    Logger?.LogError("[TeaStation] DrawBoiled: CurrentFuel field missing");
                    return;
                }

                // Cap to the liquid card's actual MaxValue if we can read it; otherwise use 200.
                float target = BoiledWaterTemperature;
                try
                {
                    var cardModelProp = liquidType.GetProperty("CardModel", Flags);
                    var cardModel = cardModelProp?.GetValue(liquid);
                    if (cardModel != null)
                    {
                        var fuelCapField = cardModel.GetType().GetField("FuelCapacity", Flags);
                        var fuelCap = fuelCapField?.GetValue(cardModel);
                        if (fuelCap != null)
                        {
                            var maxField = fuelCap.GetType().GetField("MaxValue", Flags);
                            if (maxField != null)
                            {
                                var maxObj = maxField.GetValue(fuelCap);
                                if (maxObj is float fmax && fmax > 0f) target = fmax;
                            }
                        }
                    }
                }
                catch { /* fall through to default 200 */ }

                fuelField.SetValue(liquid, target);
                Logger?.LogDebug(
                    $"[TeaStation] DrawBoiled: set ContainedLiquid.CurrentFuel = {target}");
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[TeaStation] DrawBoiled temperature fix failed: {FullException(ex)}");
            }
        }

        // Drain one Water Charge (CurrentSpecial4) from the tea station after dispensing
        // boiled water. RCC.ModType was changed from 1→0 to fix a WikiMod NullRef crash
        // (FormatCardStateChange hits null when ModType=1 + Special4Change + CreatedLiquidInGivenCard
        // are combined — all 14 vanilla CIs with CreatedLiquidInGivenCard use ModType=0 on both sides).
        private static void DrainWaterCharge(object stationCard)
        {
            try
            {
                if (stationCard == null) return;
                float before = CardUtil.ToFloat(CardUtil.GetMemberValue(stationCard, "CurrentSpecial4"));
                if (!CardUtil.ModifyDurabilityStat(stationCard, "CurrentSpecial4", -1f))
                {
                    Logger?.LogError("[TeaStation] DrainWaterCharge: CurrentSpecial4 not found");
                    return;
                }
                float after = Math.Max(0f, before - 1f);
                Logger?.LogDebug($"[TeaStation] DrainWaterCharge: WaterCharges {before} -> {after}");
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[TeaStation] DrainWaterCharge failed: {FullException(ex)}");
            }
        }

        private static string FullException(Exception ex)
        {
            return ex.InnerException?.ToString() ?? ex.ToString();
        }
    }
}
