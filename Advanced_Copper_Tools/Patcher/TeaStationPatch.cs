using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using BepInEx.Logging;

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
        private const string StewWaterGuid = "a0e1cf6d47685a741b5cd9889fb39227";
        private static bool _stewWaterPatched;

        private const string MillableTag     = "tag_Millable";
        private const string GrindingToolTag = "tag_GrindingTool";
        private const string HammerToolTag   = "tag_HammeringToolGeneral";

        // Reflection handles — CardModel / inventory / action name
        private static readonly Dictionary<Type, PropertyInfo> _cardModelPropCache = new Dictionary<Type, PropertyInfo>();
        private static readonly Dictionary<Type, FieldInfo>    _inventoryFieldCache = new Dictionary<Type, FieldInfo>();
        private static FieldInfo _uidField;
        private static FieldInfo _actionNameField;
        private static FieldInfo _defaultTextField;

        // In-place transform reflection (ported from WDI TransformCardInPlace)
        private static PropertyInfo _cardModelSetProp;
        private static MethodInfo   _cardModelSetter;
        private static FieldInfo    _cardModelBackingField;
        private static MethodInfo   _cardResetMethod;
        private static MethodInfo   _cardSetupMethod;
        private static Type         _transformCardType;

        // Spawn reflection — UniqueID → CardData → GiveCard
        private static MethodInfo _getFromIDMethod;
        private static MethodInfo _giveCardMethod;
        private static Type       _cardDataType;
        private static Type       _gmType;
        private static bool       _spawnReflectionInit;

        private static bool _grindHandled;

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

                var actionRoutine = AccessTools.Method(gmType, "ActionRoutine");
                if (actionRoutine != null)
                {
                    harmony.Patch(actionRoutine,
                        prefix: new HarmonyMethod(typeof(TeaStationPatch), nameof(ActionRoutine_Prefix)) { priority = Priority.High },
                        postfix: new HarmonyMethod(typeof(TeaStationPatch), nameof(ActionRoutine_Postfix)));
                    Logger?.LogDebug("[TeaStation] ActionRoutine patched (prefix+postfix)");
                }
                else
                {
                    Logger?.LogError("[TeaStation] ActionRoutine method not found on GameManager");
                }

                var stackRoutine = AccessTools.Method(gmType, "PerformStackActionRoutine");
                if (stackRoutine != null)
                    harmony.Patch(stackRoutine,
                        prefix: new HarmonyMethod(typeof(TeaStationPatch), nameof(PerformStackAction_Prefix)) { priority = Priority.High });
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[TeaStation] Patch failed: {ex.Message}");
            }
        }

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
            string actionName = GetActionName(action);
            if (!string.Equals(actionName, GrindAllName, StringComparison.Ordinal)) return false;

            string uid = GetCardUniqueId(card);
            return uid == StationUnlitID || uid == StationLitID;
        }

        // ============================================================
        //  GRIND ALL HANDLER
        // ============================================================
        private static bool HandleGrindAll(object station)
        {
            if (_grindHandled) return false;
            _grindHandled = true;
            try { return HandleGrindAllInner(station); }
            finally { _grindHandled = false; }
        }

        private static bool HandleGrindAllInner(object station)
        {
            var inventory = GetInventoryList(station);
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
                var innerCards = GetInventoryList(slotItem);
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

            Logger?.LogDebug($"[TeaStation] GrindAll: ejecting {toGrind.Count} ground herb(s) to board");

            // Spawn ground results on the board first, then clear source cards from slots.
            // We collect all pairs before mutating the inventory to avoid mid-iteration issues.
            InitGetFromIDReflection();
            if (_getFromIDMethod != null)
            {
                foreach (var (card, resultId) in toGrind)
                    SpawnOnBoard(resultId);
            }

            // Remove source cards and clear their slots from the station inventory.
            // Pattern: deactivate source cards, clear inner list, remove empty slots.
            EjectSourceCards(station, toGrind);

            return false; // block the no-op JSON action; we handled everything
        }

        private static void SpawnOnBoard(string uniqueId)
        {
            try
            {
                if (_getFromIDMethod == null || _giveCardMethod == null || _gmType == null)
                {
                    Logger?.LogError($"[TeaStation] SpawnOnBoard: reflection not ready (GetFromID={_getFromIDMethod != null}, GiveCard={_giveCardMethod != null}, GM={_gmType != null})");
                    return;
                }

                var cardData = _getFromIDMethod.Invoke(null, new object[] { uniqueId });
                if (cardData == null)
                {
                    Logger?.LogError($"[TeaStation] SpawnOnBoard: CardData not found for '{uniqueId}'");
                    return;
                }

                var gm = _gmType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                      ?? UnityEngine.Object.FindObjectOfType(_gmType);
                if (gm == null) { Logger?.LogError("[TeaStation] SpawnOnBoard: GameManager instance not found"); return; }

                var parms = _giveCardMethod.GetParameters();
                var args = new object[parms.Length];
                args[0] = cardData;
                for (int i = 1; i < parms.Length; i++)
                {
                    var pt = parms[i].ParameterType;
                    args[i] = pt.IsValueType ? Activator.CreateInstance(pt) : null;
                }
                _giveCardMethod.Invoke(gm, args);
                Logger?.Log(LogLevel.Debug, $"[TeaStation] Spawned '{uniqueId}' on board");
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[TeaStation] SpawnOnBoard({uniqueId}) failed: {ex.InnerException?.Message ?? ex.Message}");
            }
        }

        private static void EjectSourceCards(object station, List<(object card, string resultId)> toGrind)
        {
            try
            {
                // Build a set of card objects to remove for fast lookup
                var cardSet = new HashSet<object>();
                foreach (var (card, _) in toGrind) cardSet.Add(card);

                var stationSlots = GetInventoryList(station);
                if (stationSlots == null) return;

                // Walk slots; for each slot clear out matched inner cards and deactivate them.
                var emptySlots = new List<object>();
                foreach (var slot in stationSlots)
                {
                    if (slot == null) continue;
                    var innerCards = GetInventoryList(slot);
                    if (innerCards == null) continue;

                    var toRemove = new List<object>();
                    foreach (var inner in innerCards)
                    {
                        if (inner != null && cardSet.Contains(inner))
                            toRemove.Add(inner);
                    }

                    foreach (var card in toRemove)
                    {
                        innerCards.Remove(card);
                        // Deactivate the orphaned card GameObject so it's invisible
                        TrySetActive(card, false);
                    }

                    if (innerCards.Count == 0)
                        emptySlots.Add(slot);
                }

                // Remove fully-emptied slots from station inventory
                foreach (var slot in emptySlots)
                    stationSlots.Remove(slot);
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[TeaStation] EjectSourceCards error: {ex.Message}");
            }
        }

        private static void TrySetActive(object card, bool active)
        {
            try
            {
                if (card is UnityEngine.Object uo)
                {
                    var go = (uo as UnityEngine.Component)?.gameObject
                          ?? uo as UnityEngine.GameObject;
                    go?.SetActive(active);
                }
            }
            catch { }
        }

        // ============================================================
        //  IN-PLACE CARD TRANSFORM (ported from WDI ActionInterceptPatch)
        // ============================================================
        private static void TransformCardInPlace(object card, string targetUniqueId)
        {
            if (card == null || string.IsNullOrEmpty(targetUniqueId)) return;
            try
            {
                InitGetFromIDReflection();
                if (_getFromIDMethod == null) return;

                var targetData = _getFromIDMethod.Invoke(null, new object[] { targetUniqueId });
                if (targetData == null)
                {
                    Logger?.LogError($"[TeaStation] Transform: CardData not found for '{targetUniqueId}'");
                    return;
                }

                Type cardType = card.GetType();
                if (_transformCardType != cardType)
                {
                    _transformCardType = cardType;
                    _cardModelSetProp  = cardType.GetProperty("CardModel", Flags);
                    // Auto-property setter may be private/internal; fetch it directly
                    _cardModelSetter   = _cardModelSetProp?.GetSetMethod(nonPublic: true);
                    // Final fallback — auto-property backing field
                    _cardModelBackingField = ResolveBackingField(cardType, "CardModel");
                    _cardResetMethod   = cardType.GetMethod("ResetCard", Flags, null, Type.EmptyTypes, null);
                    _cardSetupMethod   = cardType.GetMethods(Flags)
                        .FirstOrDefault(m => m.Name == "SetupCardSource"
                            && m.GetParameters().Length >= 1
                            && _cardDataType != null
                            && m.GetParameters()[0].ParameterType.IsAssignableFrom(_cardDataType));
                    Logger?.Log(LogLevel.Debug,
                        $"[TeaStation] Transform reflection for {cardType.Name}: " +
                        $"CardModelProp={(_cardModelSetProp != null)}, " +
                        $"Setter={(_cardModelSetter != null)}, " +
                        $"BackingField={(_cardModelBackingField != null)}, " +
                        $"SetupCardSource={(_cardSetupMethod != null)}, " +
                        $"ResetCard={(_cardResetMethod != null)}");
                }

                if (!TrySetCardModel(card, targetData))
                {
                    Logger?.LogError($"[TeaStation] Transform: CardModel not settable on {cardType.Name}");
                    return;
                }

                if (_cardSetupMethod != null)
                {
                    var p = _cardSetupMethod.GetParameters();
                    var args = new object[p.Length];
                    args[0] = targetData;
                    for (int i = 1; i < p.Length; i++)
                    {
                        var pt = p[i].ParameterType;
                        args[i] = pt.IsValueType ? Activator.CreateInstance(pt) : null;
                    }
                    _cardSetupMethod.Invoke(card, args);
                }
                else if (_cardResetMethod != null)
                {
                    _cardResetMethod.Invoke(card, null);
                }

                Logger?.Log(LogLevel.Debug, $"[TeaStation] Transformed → {targetUniqueId}");
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[TeaStation] Transform failed: {ex.Message}");
            }
        }

        private static bool TrySetCardModel(object card, object targetData)
        {
            // Path 1 — public/writable property
            try
            {
                if (_cardModelSetProp != null && _cardModelSetProp.CanWrite)
                {
                    _cardModelSetProp.SetValue(card, targetData);
                    return true;
                }
            }
            catch (Exception ex) { Logger?.Log(LogLevel.Debug, $"[TeaStation] prop.SetValue failed: {ex.Message}"); }

            // Path 2 — non-public setter invoked directly
            try
            {
                if (_cardModelSetter != null)
                {
                    _cardModelSetter.Invoke(card, new object[] { targetData });
                    return true;
                }
            }
            catch (Exception ex) { Logger?.Log(LogLevel.Debug, $"[TeaStation] setter.Invoke failed: {ex.Message}"); }

            // Path 3 — auto-property backing field
            try
            {
                if (_cardModelBackingField != null)
                {
                    _cardModelBackingField.SetValue(card, targetData);
                    return true;
                }
            }
            catch (Exception ex) { Logger?.Log(LogLevel.Debug, $"[TeaStation] backingField.SetValue failed: {ex.Message}"); }

            return false;
        }

        private static FieldInfo ResolveBackingField(Type type, string propName)
        {
            string target = $"<{propName}>k__BackingField";
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                var f = t.GetField(target, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (f != null) return f;
            }
            return null;
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

                object cardData = GetCardData(card);
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
                    if (tags == null || tags.Count == 0) continue;

                    bool matched = false;
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
                    if (!matched) continue;

                    // Grind CI found — read resolved TransformInto
                    var rccField = ci.GetType().GetField("ReceivingCardChanges", Flags);
                    var rcc = rccField?.GetValue(ci);
                    if (rcc == null) continue;

                    var transformField = rcc.GetType().GetField("TransformInto", Flags);
                    var transformCard  = transformField?.GetValue(rcc);
                    if (transformCard == null) continue;

                    var uidField = transformCard.GetType().GetField("UniqueID", Flags);
                    var uid = uidField?.GetValue(transformCard) as string;
                    if (!string.IsNullOrEmpty(uid)) return uid;
                }
            }
            catch (Exception ex)
            {
                Logger?.Log(LogLevel.Debug, $"[TeaStation] ResolveGrindResult error: {ex.Message}");
            }
            return null;
        }

        private static bool HasTag(object cardData, string tagUid)
        {
            try
            {
                var tagsField = cardData.GetType().GetField("CardTags", Flags);
                var tags = tagsField?.GetValue(cardData) as IList;
                if (tags == null) return false;
                foreach (var t in tags)
                {
                    if (t == null) continue;
                    // CardTag is not UniqueIDScriptable — use SO .name (set by WarpResolver to the tag identifier)
                    if (t is UnityEngine.Object uo && uo.name == tagUid) return true;
                }
            }
            catch { }
            return false;
        }

        // ============================================================
        //  SHARED REFLECTION HELPERS
        // ============================================================

        private static object GetCardData(object card)
        {
            try
            {
                if (card == null) return null;
                Type cardType = card.GetType();
                if (!_cardModelPropCache.TryGetValue(cardType, out var cmProp))
                {
                    cmProp = cardType.GetProperty("CardModel", Flags);
                    _cardModelPropCache[cardType] = cmProp;
                }
                return cmProp?.GetValue(card);
            }
            catch { return null; }
        }

        private static string GetCardUniqueId(object card)
        {
            try
            {
                if (card == null) return null;
                if (card is UniqueIDScriptable s) return s.UniqueID;

                object cardData = GetCardData(card);
                if (cardData == null) return null;
                if (cardData is UniqueIDScriptable s2) return s2.UniqueID;

                if (_uidField == null)
                    _uidField = cardData.GetType().GetField("UniqueID", Flags);
                return _uidField?.GetValue(cardData) as string;
            }
            catch { return null; }
        }

        private static string GetActionName(object action)
        {
            try
            {
                if (action == null) return null;
                if (_actionNameField == null)
                    _actionNameField = action.GetType().GetField("ActionName", Flags);
                if (_actionNameField == null) return null;
                var nameObj = _actionNameField.GetValue(action);
                if (nameObj == null) return null;
                if (_defaultTextField == null)
                    _defaultTextField = nameObj.GetType().GetField("DefaultText", Flags);
                return _defaultTextField?.GetValue(nameObj) as string;
            }
            catch { return null; }
        }

        /// <summary>
        /// Finds the list field that holds inventory items on a card.
        /// Handles both direct card lists (List&lt;InGameCardBase&gt;) and
        /// InventorySlot wrapper lists (List&lt;InventorySlot&gt;).
        /// Results are cached per type.
        /// </summary>
        private static IList GetInventoryList(object card)
        {
            try
            {
                if (card == null) return null;
                Type cardType = card.GetType();
                if (!_inventoryFieldCache.TryGetValue(cardType, out var invField))
                {
                    // Pass 1: known field names (covers most card and slot types)
                    string[] names = {
                        "ContainedCards", "AllCardsInSlots", "CardsInInventory",
                        "InventoryCards", "CardsInSlots", "AllCards",
                        "InventorySlots", "Slots", "CardSlots"
                    };
                    foreach (var name in names)
                    {
                        var f = cardType.GetField(name, Flags);
                        if (f != null && typeof(IList).IsAssignableFrom(f.FieldType))
                        {
                            invField = f;
                            break;
                        }
                    }

                    // Pass 2: generic List<T> where T's name contains "Card" OR "Slot"
                    // (catches backing fields of auto-properties and non-standard names)
                    if (invField == null)
                    {
                        foreach (var f in cardType.GetFields(Flags))
                        {
                            if (!f.FieldType.IsGenericType) continue;
                            if (!typeof(IList).IsAssignableFrom(f.FieldType)) continue;
                            var args = f.FieldType.GetGenericArguments();
                            if (args.Length != 1) continue;
                            string elemName = args[0].Name;
                            if ((elemName.Contains("Card") || elemName.Contains("Slot"))
                                && !f.Name.Contains("Tag"))
                            {
                                invField = f;
                                break;
                            }
                        }
                    }

                    _inventoryFieldCache[cardType] = invField;
                    Logger?.Log(LogLevel.Debug,
                        $"[TeaStation] InventoryField for {cardType.Name}: {(invField?.Name ?? "NONE")}");
                }
                return invField?.GetValue(card) as IList;
            }
            catch { return null; }
        }

        private static void InitGetFromIDReflection()
        {
            if (_spawnReflectionInit && _getFromIDMethod != null && _giveCardMethod != null) return;
            _spawnReflectionInit = true;
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (_cardDataType == null) _cardDataType = asm.GetType("CardData", false);
                    if (_gmType == null)       _gmType       = asm.GetType("GameManager", false);
                }

                Type uidType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    uidType = asm.GetType("UniqueIDScriptable", false);
                    if (uidType != null) break;
                }

                if (uidType != null && _cardDataType != null)
                {
                    var getFromID = uidType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(m => m.Name == "GetFromID" && m.IsGenericMethodDefinition);
                    if (getFromID != null)
                        _getFromIDMethod = getFromID.MakeGenericMethod(_cardDataType);
                }

                if (_gmType != null && _cardDataType != null)
                {
                    _giveCardMethod = AccessTools.Method(_gmType, "GiveCard", new[] { _cardDataType, typeof(bool) })
                        ?? _gmType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                            .FirstOrDefault(m => m.Name == "GiveCard" && m.GetParameters().Length >= 1
                                && m.GetParameters()[0].ParameterType == _cardDataType);
                }

                Logger?.Log(LogLevel.Debug,
                    $"[TeaStation] SpawnReflection: GetFromID={_getFromIDMethod != null}, GiveCard={_giveCardMethod != null}, GM={_gmType != null}");

                if (_getFromIDMethod == null) Logger?.LogError("[TeaStation] GetFromID reflection init failed");
                if (_giveCardMethod == null)  Logger?.LogError("[TeaStation] GiveCard reflection init failed");
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[TeaStation] SpawnReflection init error: {ex.Message}");
            }
        }

        // ============================================================
        //  DRAW BOILED WATER — TEMPERATURE FIX
        // ============================================================
        // The vanilla CardOnCardAction.CreatedLiquidInGivenCard spawn path
        // (GameManager.ActionRoutine, ~line 3450 of decompiled Assembly-CSharp)
        // builds the new liquid card's runtime durabilities by reading the
        // SOURCE CardData's stat defaults — specifically:
        //     transferedDurabilities.Fuel.FloatValue =
        //         _Action.CreatedLiquidInGivenCard.LiquidCard.FuelCapacity;
        // For LQ_StewWater, FuelCapacity.FloatValue = 0, so the liquid spawns
        // ice-cold even though semantically it's "boiled water". The struct's
        // serialized `Durabilities` (TransferToDrop) field is dead code for
        // this path; `LiquidDurabilities` (TransferedDurabilities) is
        // [NonSerialized] so JSON cannot supply it either.
        //
        // Fix: postfix ActionRoutine on "Draw Boiled Water" + tea-station-lit
        // and force the given container's ContainedLiquid.CurrentFuel to max
        // after the spawn coroutine completes.
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
            EnsureStewWaterDefaultFuel();
            yield return enumerator;

            // Filter: must be our action on our station, with a target container.
            if (_Action == null || _ReceivingCard == null || _GivenCard == null)
                yield break;

            string actionName = GetActionName(_Action);
            if (!string.Equals(actionName, DrawBoiledWaterName, StringComparison.Ordinal))
                yield break;

            string recvUid = GetCardUniqueId(_ReceivingCard);
            if (recvUid != StationLitID && recvUid != StationUnlitID)
                yield break;

            Logger?.LogDebug($"[TeaStation] DrawBoiled fired: action='{actionName}' recv={recvUid}");
            ApplyBoiledWaterTemperature(_GivenCard);
            DrainWaterCharge(_ReceivingCard);
        }

        // Outside the iterator so it can use try/catch around reflection.
        private static void ApplyBoiledWaterTemperature(object givenCard)        {
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
                Logger?.LogError($"[TeaStation] DrawBoiled temperature fix failed: {ex.Message}");
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
                FieldInfo s4Field = null;
                for (var t = stationCard.GetType(); t != null && t != typeof(object); t = t.BaseType)
                {
                    s4Field = t.GetField("CurrentSpecial4", Flags | BindingFlags.DeclaredOnly);
                    if (s4Field != null) break;
                }
                if (s4Field == null) { Logger?.LogError("[TeaStation] DrainWaterCharge: CurrentSpecial4 field not found"); return; }

                float current = (float)s4Field.GetValue(stationCard);
                float newVal = Math.Max(0f, current - 1f);
                s4Field.SetValue(stationCard, newVal);
                Logger?.LogDebug($"[TeaStation] DrainWaterCharge: WaterCharges {current} -> {newVal}");
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[TeaStation] DrainWaterCharge failed: {ex.Message}");
            }
        }

        // One-shot mutation: LQ_StewWater.FuelCapacity.FloatValue from 0 → 200.
        // The decompiled GameManager.ActionRoutine seeds the new liquid card's
        // CurrentFuel from `LiquidCard.FuelCapacity` (implicit struct→float =
        // FloatValue). Vanilla LQ_StewWater ships FloatValue=0 with
        // HasActionOnZero=True (transform→LQ_Water), so any spawn via
        // CreatedLiquidInGivenCard fires OnZero immediately during the spawn
        // coroutine and reverts to cold LQ_Water before our postfix can run.
        // Setting the default to 200 makes new spawns start hot; the existing
        // CoolDown PassiveEffect (-100/dtp) still cools it normally.
        private static void EnsureStewWaterDefaultFuel()
        {
            if (_stewWaterPatched) return;
            _stewWaterPatched = true;
            try
            {
                Type uidScriptable = null;
                Type cardDataType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (uidScriptable == null) uidScriptable = asm.GetType("UniqueIDScriptable");
                    if (cardDataType == null)  cardDataType  = asm.GetType("CardData");
                    if (uidScriptable != null && cardDataType != null) break;
                }
                if (uidScriptable == null || cardDataType == null)
                {
                    Logger?.LogInfo("[TeaStation] EnsureStewWaterDefaultFuel: types not found");
                    return;
                }
                var getFromID = uidScriptable.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "GetFromID" && m.IsGenericMethodDefinition);
                if (getFromID == null) { Logger?.LogInfo("[TeaStation] GetFromID not found"); return; }
                var generic = getFromID.MakeGenericMethod(cardDataType);
                var stewWater = generic.Invoke(null, new object[] { StewWaterGuid });
                if (stewWater == null)
                {
                    Logger?.LogInfo($"[TeaStation] LQ_StewWater not found by GUID {StewWaterGuid}");
                    return;
                }
                var fuelCapField = cardDataType.GetField("FuelCapacity", Flags);
                if (fuelCapField == null) { Logger?.LogError("[TeaStation] CardData.FuelCapacity field not found"); return; }
                var fuelCap = fuelCapField.GetValue(stewWater);
                if (fuelCap == null) { Logger?.LogError("[TeaStation] FuelCapacity is null"); return; }
                var fuelCapType = fuelCap.GetType();
                var floatValueField = fuelCapType.GetField("FloatValue", Flags);
                if (floatValueField == null) { Logger?.LogError("[TeaStation] FuelCapacity.FloatValue not found"); return; }

                float oldVal = (float)floatValueField.GetValue(fuelCap);
                floatValueField.SetValue(fuelCap, BoiledWaterTemperature);
                // Capacity is likely a struct — write back through the field.
                fuelCapField.SetValue(stewWater, fuelCap);
                Logger?.LogDebug(
                    $"[TeaStation] LQ_StewWater.FuelCapacity.FloatValue: {oldVal} -> {BoiledWaterTemperature}");
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[TeaStation] EnsureStewWaterDefaultFuel failed: {ex.Message}");
            }
        }
    }
}
