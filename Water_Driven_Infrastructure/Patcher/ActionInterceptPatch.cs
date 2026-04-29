using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using BepInEx.Logging;

namespace WaterDrivenInfrastructure.Patcher
{
    /// <summary>
    /// Intercepts DismantleAction execution on water-driven structures to consume
    /// inventory items that pure JSON DismantleActions cannot handle.
    /// Patches GameManager.ActionRoutine with a prefix.
    ///
    /// Handles:
    ///   Sawmill  "Cut"           - consume one log from inventory
    ///   Mill     "Grind All"     - let JSON Progress + Mill effect handle conversion
    /// </summary>
    public static class ActionInterceptPatch
    {
        private static ManualLogSource Logger => Plugin.Logger;
        private static readonly BindingFlags Flags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // UniqueIDs
        private const string SawmillID          = "water_sawmill_placed";
        private const string MillID             = "water_sawmill_grinding_mill_placed";
        private const string SluiceID           = "water_sawmill_ore_sluice_placed";
        private const string ForgeID            = "water_sawmill_forge_placed";
        private const string FishpondFilledID   = "water_sawmill_fishpond_filled";
        private const string FishpondStockedID  = "water_sawmill_fishpond_stocked";

        // Vanilla GUIDs

        private const string LogGUID        = "0ab556ab6af1efc47a2cba5cdf4ace04";
        private const string MudPileGUID    = "22427beeefed8a9469a997bdce087332";
        private const string DirtPileGUID   = "6d47888db6018d04ea562476cde60440";
        private const string FineDirtGUID   = "3f131012e4586224c86da02a5fa50d26";
        private const string GreenstoneGUID = "bcc7d7a764978e447bc38b36dcca2055";
        private const string FlintGUID      = "bef002cfd45b3e8459864746f403cf73";
        private const string StoneGUID      = "a7384e5147b23a642809451cc4ef24fb";

        // Other Fish species GUIDs (sturgeon/trout/char share SpecialDurability4 in JSON)
        private const string SturgeonGUID = "de00abf9d0d186b42bed6807ba49a4cb";
        private const string TroutGUID    = "f4972170adc8b3d4f9d4bc09c6e7f760";
        private const string CharGUID     = "a966c94277465f741857b994202791f3";

        // Per-pond stocked-species counts (session-only; resets on save reload).
        // Keyed by InGameCardBase.GetInstanceID(). When tracking is unavailable,
        // Catch Other Fish falls back to the JSON's uniform 33/33/33 produced cards.
        private struct OtherFishCounts { public int Sturgeon, Trout, Charr; }
        private static readonly Dictionary<int, OtherFishCounts> _otherFish = new Dictionary<int, OtherFishCounts>();



        // Cached reflection handles (per-type for card fields)
        private static bool _reflected;
        private static readonly Dictionary<Type, PropertyInfo> _cardModelPropCache = new Dictionary<Type, PropertyInfo>();
        private static readonly Dictionary<Type, FieldInfo> _inventoryFieldCache = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, MethodInfo> _removeMethodCache = new Dictionary<Type, MethodInfo>();
        private static FieldInfo    _uidField;
        private static FieldInfo    _actionNameField;
        private static FieldInfo    _defaultTextField;
        private static PropertyInfo _currentFuelProp;
        private static FieldInfo    _currentFuelField;

        public static void ApplyPatch(Harmony harmony)
        {
            try
            {
                var gmType = AccessTools.TypeByName("GameManager");

                // CardInteractions (drag card to structure) go through ActionRoutine
                var actionRoutine = AccessTools.Method(gmType, "ActionRoutine");
                if (actionRoutine != null)
                {
                    harmony.Patch(actionRoutine, prefix: new HarmonyMethod(typeof(ActionInterceptPatch), nameof(ActionRoutine_Prefix)) { priority = Priority.High });
                }
                else
                {
                    Logger?.LogError("[ActionIntercept] GameManager.ActionRoutine not found");
                }

                // DismantleActions (buttons like "Sluice All", "Pack Up") go through PerformStackActionRoutine
                var stackRoutine = AccessTools.Method(gmType, "PerformStackActionRoutine");
                if (stackRoutine != null)
                {
                    harmony.Patch(stackRoutine, prefix: new HarmonyMethod(typeof(ActionInterceptPatch), nameof(PerformStackAction_Prefix)) { priority = Priority.High });
                }
                else
                {
                    Logger?.LogError("[ActionIntercept] GameManager.PerformStackActionRoutine not found");
                }

                // Fallback: PerformActionAsEnumerator may handle individual action execution
                var performAction = AccessTools.Method(gmType, "PerformActionAsEnumerator");
                if (performAction != null)
                {
                    harmony.Patch(performAction, prefix: new HarmonyMethod(typeof(ActionInterceptPatch), nameof(PerformActionAsEnum_Prefix)) { priority = Priority.High });
                }
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[ActionIntercept] Patch failed: {ex.Message}");
            }
        }

        // Prefix for DismantleAction buttons (e.g. "Sluice All", "Pack Up")
        // Uses positional params (__0, __1) because actual parameter names may differ from docs
        // Signature: IEnumerator PerformStackActionRoutine(CardAction, List<InGameCardBase>, InGameNPCOrPlayer)
        static bool PerformStackAction_Prefix(object __0, object __1)
        {
            try
            {
                if (__0 == null || __1 == null) return true;

                // __0 = CardAction, __1 = List<InGameCardBase>
                var cardList = __1 as IList;
                if (cardList == null || cardList.Count == 0) return true;
                object receivingCard = cardList[0];
                if (receivingCard == null) return true;

                EnsureReflection(receivingCard, __0);

                string actionName = GetActionName(__0);
                string cardId = GetCardUniqueId(receivingCard);

                if (cardId != null && cardId.StartsWith("water_sawmill_", StringComparison.OrdinalIgnoreCase))
                    Logger?.Log(LogLevel.Debug, $"[ActionIntercept] StackPrefix: cardId='{cardId}', actionName='{actionName}'");

                if (IsSluiceAllAction(__0))
                {
                    var sluiceCard = FindSluiceCard(__0, receivingCard);
                    if (sluiceCard != null)
                        return HandleSluiceAll(sluiceCard);
                }

                if (cardId == ForgeID && actionName == "Hammer All")
                    return HandleHammerAll(receivingCard);

                // Catch Other Fish — pick weighted by what was actually stocked.
                // Returns false to skip the JSON 33/33/33 produced cards if we have tracking
                // data; otherwise returns true so JSON fallback fires.
                if ((cardId == FishpondFilledID || cardId == FishpondStockedID)
                    && actionName == "Catch Other Fish")
                    return HandleCatchOtherFish(receivingCard);
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[ActionIntercept] StackPrefix error: {ex}");
            }
            return true;
        }

        // Fallback prefix for PerformActionAsEnumerator — catches individual action execution
        // Uses __args to capture all params regardless of signature
        static bool PerformActionAsEnum_Prefix(object[] __args)
        {
            try
            {
                if (__args == null || __args.Length == 0) return true;

                // Only process if we find both a "Sluice All" action and a sluice card
                object action = null;
                object card = null;
                foreach (var arg in __args)
                {
                    if (arg == null) continue;
                    string typeName = arg.GetType().Name;
                    if (typeName.Contains("CardAction") || typeName.Contains("Action"))
                    {
                        string name = GetActionName(arg);
                        if (name == "Sluice All")
                            action = arg;
                    }
                    if (typeName.Contains("Card") && !typeName.Contains("Action"))
                    {
                        string uid = GetCardUniqueId(arg);
                        if (uid == SluiceID)
                            card = arg;
                    }
                }

                if (action != null && card != null)
                {
                    Logger?.Log(LogLevel.Debug, "[ActionIntercept] PerformActionAsEnum: intercepted Sluice All");
                    return HandleSluiceAll(card);
                }
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[ActionIntercept] PerformActionAsEnum error: {ex}");
            }
            return true;
        }

        // __0 = CardAction, __1 = ReceivingCard, __2 = GivenCard (dragged card)
        // Signature: IEnumerator ActionRoutine(CardAction _Action, InGameCardBase _ReceivingCard, InGameCardBase _GivenCard, ...)
        static bool ActionRoutine_Prefix(
            object __0, object __1, object __2,
            bool __3, bool __4,
            bool __5, object __6)
        {
            try
            {
                if (__0 == null || __1 == null) return true;

                EnsureReflection(__1, __0);

                string cardId = GetCardUniqueId(__1);
                string actionName = GetActionName(__0);
                if (cardId == null || actionName == null) return true;

                // Diagnostic: log when our mod cards are actioned
                if (cardId != null && cardId.StartsWith("water_sawmill_", StringComparison.OrdinalIgnoreCase))
                    Logger?.Log(LogLevel.Debug, $"[ActionIntercept] Prefix: cardId='{cardId}', actionName='{actionName}', actionType={__0.GetType().Name}");

                // Sawmill "Cut" is a CardInteraction — JSON handles it fully:
                // GivenCardChanges.ModType=3 destroys the log, ProducedCards spawns 8 planks.
                // No C# intercept needed.

                if (cardId == MillID && actionName == "Grind All")
                    return HandleGrindAll(__1);

                if (cardId == ForgeID && actionName == "Hammer All")
                    return HandleHammerAll(__1);

                // Stock Sturgeon/Trout/Char — record which species was stocked so
                // Catch Other Fish can produce proportionally. JSON still handles
                // Special4Change (+1) and the destroy of the given fish card.
                if (cardId == FishpondFilledID || cardId == FishpondStockedID)
                {
                    if      (actionName == "Stock Sturgeon") IncrementOtherFish(__1, 1, 0, 0);
                    else if (actionName == "Stock Trout")    IncrementOtherFish(__1, 0, 1, 0);
                    else if (actionName == "Stock Char")     IncrementOtherFish(__1, 0, 0, 1);
                }

                if (IsSluiceAllAction(__0))
                {
                    var sluiceCard = FindSluiceCard(__0, __1, __2);
                    if (sluiceCard != null)
                        return HandleSluiceAll(sluiceCard);
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[ActionIntercept] prefix error: {ex}");
                return true;
            }
        }

        // ============================================================
        //  GRINDING MILL - C# Grind All handler
        // ============================================================
        private const string GrindingToolTag = "tag_GrindingTool";
        private const string HammerToolTag   = "tag_HammeringToolGeneral";
        private static bool _grindHandled;

        private static bool HandleGrindAll(object mill)
        {
            if (_grindHandled) return false;
            _grindHandled = true;
            try { return HandleGrindAllInner(mill); }
            finally { _grindHandled = false; }
        }

        private static bool HandleGrindAllInner(object mill)
        {
            var inventory = GetInventoryList(mill);
            if (inventory == null || inventory.Count == 0)
            {
                Logger?.Log(LogLevel.Debug, "[ActionIntercept] GrindAll: empty mill inventory");
                return false;
            }

            var toGrind = new List<(object card, string resultId)>();
            foreach (var slotItem in inventory)
            {
                if (slotItem == null) continue;
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
                    string resultId = ResolveGrindResult(slotItem);
                    if (resultId != null)
                        toGrind.Add((slotItem, resultId));
                }
            }

            if (toGrind.Count == 0)
            {
                Logger?.Log(LogLevel.Debug, "[ActionIntercept] GrindAll: no grindable items found");
                return false;
            }

            Logger?.Log(LogLevel.Debug, $"[ActionIntercept] GrindAll: grinding {toGrind.Count} item(s)");

            InitSpawnReflection();
            if (_getFromIDMethod != null && _giveCardMethod != null && _gmType != null)
            {
                foreach (var (_, resultId) in toGrind)
                    SpawnResultOnBoard(resultId);
            }

            EjectSourceCardsFromMill(mill, toGrind);
            return false;
        }

        private static void SpawnResultOnBoard(string uniqueId)
        {
            try
            {
                var cardData = _getFromIDMethod.Invoke(null, new object[] { uniqueId });
                if (cardData == null)
                {
                    Logger?.LogError($"[ActionIntercept] GrindAll: CardData not found for '{uniqueId}'");
                    return;
                }
                var gm = _gmType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                      ?? UnityEngine.Object.FindObjectOfType(_gmType);
                if (gm == null) { Logger?.LogError("[ActionIntercept] GrindAll: GameManager instance not found"); return; }

                var parms = _giveCardMethod.GetParameters();
                var args = new object[parms.Length];
                args[0] = cardData;
                for (int i = 1; i < parms.Length; i++)
                {
                    var pt = parms[i].ParameterType;
                    args[i] = pt.IsValueType ? Activator.CreateInstance(pt) : null;
                }
                _giveCardMethod.Invoke(gm, args);
                Logger?.Log(LogLevel.Debug, $"[ActionIntercept] GrindAll: spawned '{uniqueId}' on board");
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[ActionIntercept] SpawnResultOnBoard({uniqueId}) failed: {ex.InnerException?.Message ?? ex.Message}");
            }
        }

        private static void EjectSourceCardsFromMill(object mill, List<(object card, string resultId)> toGrind)
        {
            try
            {
                var cardSet = new HashSet<object>();
                foreach (var (card, _) in toGrind) cardSet.Add(card);

                var millSlots = GetInventoryList(mill);
                if (millSlots == null) return;

                var emptySlots = new List<object>();
                foreach (var slot in millSlots)
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
                        TrySetActive(card, false);
                    }
                    if (innerCards.Count == 0)
                        emptySlots.Add(slot);
                }
                foreach (var slot in emptySlots)
                    millSlots.Remove(slot);
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[ActionIntercept] EjectSourceCards error: {ex.Message}");
            }
        }

        private static void TrySetActive(object card, bool active)
        {
            try
            {
                if (card is UnityEngine.Object uo)
                {
                    var go = (uo as UnityEngine.Component)?.gameObject ?? uo as UnityEngine.GameObject;
                    go?.SetActive(active);
                }
            }
            catch { }
        }

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

                    var tagsField = compat.GetType().GetField("TriggerTags", Flags);
                    var tags = tagsField?.GetValue(compat) as IList;
                    if (tags == null || tags.Count == 0) continue;

                    bool matched = false;
                    foreach (var t in tags)
                    {
                        if (t == null) continue;
                        string tagName = (t is UnityEngine.Object uo) ? uo.name : null;
                        if (tagName == GrindingToolTag || tagName == HammerToolTag)
                        {
                            matched = true;
                            break;
                        }
                    }
                    if (!matched) continue;

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
                Logger?.Log(LogLevel.Debug, $"[ActionIntercept] ResolveGrindResult error: {ex.Message}");
            }
            return null;
        }

        private static object GetCardData(object card)
        {
            try
            {
                if (card == null) return null;
                Type cardType = card.GetType();
                _cardModelPropCache.TryGetValue(cardType, out var cmProp);
                if (cmProp == null)
                {
                    cmProp = cardType.GetProperty("CardModel", Flags);
                    _cardModelPropCache[cardType] = cmProp;
                }
                return cmProp?.GetValue(card);
            }
            catch { return null; }
        }

        // ============================================================
        //  FISHPOND - "Other Fish" proportional catch (sturgeon/trout/char)
        // ============================================================
        // Stock Sturgeon/Trout/Char records the species in a per-pond dict, then
        // Catch Other Fish picks a result weighted by what was actually stocked.
        // Tracking is session-only — survives Filled↔Stocked swaps (same Unity
        // instance), but not save/load. After a load, dict is empty and the JSON
        // 33/33/33 fallback fires until the player stocks again.

        private static int GetPondInstanceId(object pond)
        {
            return (pond is UnityEngine.Object uo) ? uo.GetInstanceID() : 0;
        }

        private static void IncrementOtherFish(object pond, int s, int t, int c)
        {
            int id = GetPondInstanceId(pond);
            if (id == 0) return;
            _otherFish.TryGetValue(id, out var counts);
            counts.Sturgeon += s;
            counts.Trout    += t;
            counts.Charr    += c;
            _otherFish[id] = counts;
        }

        private static bool HandleCatchOtherFish(object pond)
        {
            try
            {
                int id = GetPondInstanceId(pond);
                if (id == 0) return true;
                if (!_otherFish.TryGetValue(id, out var counts)) return true;

                int total = counts.Sturgeon + counts.Trout + counts.Charr;
                if (total <= 0) return true;

                int roll = UnityEngine.Random.Range(0, total);
                string pickedGuid;
                if (roll < counts.Sturgeon)
                {
                    counts.Sturgeon--;
                    pickedGuid = SturgeonGUID;
                }
                else if (roll < counts.Sturgeon + counts.Trout)
                {
                    counts.Trout--;
                    pickedGuid = TroutGUID;
                }
                else
                {
                    counts.Charr--;
                    pickedGuid = CharGUID;
                }
                _otherFish[id] = counts;

                // Decrement the visible Special4 total so the pond stat matches the dict
                float cur = GetDurabilityStatValue(pond, "SpecialDurability4");
                if (!float.IsNaN(cur))
                    SetDurabilityStatValue(pond, "SpecialDurability4", Math.Max(0f, cur - 1f));

                // Spawn the picked fish on the board
                InitSpawnReflection();
                SpawnResultOnBoard(pickedGuid);

                Logger?.Log(LogLevel.Debug,
                    $"[ActionIntercept] CatchOtherFish: picked {pickedGuid} (s={counts.Sturgeon} t={counts.Trout} c={counts.Charr})");

                return false; // skip JSON 33/33/33 ProducedCards
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[ActionIntercept] CatchOtherFish failed: {ex.Message}");
                return true; // fall back to JSON behavior on error
            }
        }

        // ============================================================
        //  WATER-DRIVEN FORGE - apply one hammer strike to all inventory items
        // ============================================================
        private static bool _hammerHandled;

        private static bool HandleHammerAll(object forge)
        {
            if (_hammerHandled) return false;
            _hammerHandled = true;
            try { return HandleHammerAllInner(forge); }
            finally { _hammerHandled = false; }
        }

        private static bool HandleHammerAllInner(object forge)
        {
            var inventory = GetInventoryList(forge);
            if (inventory == null || inventory.Count == 0)
            {
                Logger?.Log(LogLevel.Debug, "[ActionIntercept] HammerAll: empty forge inventory");
                return false;
            }

            var toComplete = new List<(object card, string resultId)>();
            var toAdvance  = new List<(object card, float special1Change, float fuelChange)>();

            foreach (var slotItem in inventory)
            {
                if (slotItem == null) continue;
                var innerCards = GetInventoryList(slotItem);
                if (innerCards != null && innerCards.Count > 0)
                {
                    foreach (var inner in innerCards)
                    {
                        if (inner == null) continue;
                        ClassifyHammerItem(inner, toComplete, toAdvance);
                    }
                }
                else
                {
                    ClassifyHammerItem(slotItem, toComplete, toAdvance);
                }
            }

            if (toComplete.Count == 0 && toAdvance.Count == 0)
            {
                Logger?.Log(LogLevel.Debug, "[ActionIntercept] HammerAll: no hammerable items found");
                return false;
            }

            Logger?.Log(LogLevel.Debug,
                $"[ActionIntercept] HammerAll: {toAdvance.Count} advance, {toComplete.Count} complete");

            // Apply one hit to items that don't finish yet
            foreach (var (card, s1Change, fuelChange) in toAdvance)
            {
                float cur = GetDurabilityStatValue(card, "SpecialDurability1");
                if (!float.IsNaN(cur))
                {
                    SetDurabilityStatValue(card, "SpecialDurability1", Math.Max(0f, cur + s1Change));
                    Logger?.Log(LogLevel.Debug,
                        $"[ActionIntercept] HammerAll: Strikes {cur} -> {Math.Max(0f, cur + s1Change)}");
                }
                if (fuelChange != 0f)
                {
                    float curFuel = GetDurabilityStatValue(card, "FuelCapacity");
                    if (!float.IsNaN(curFuel))
                        SetDurabilityStatValue(card, "FuelCapacity", Math.Max(0f, curFuel + fuelChange));
                }
            }

            // Spawn results and eject completed items
            InitSpawnReflection();
            foreach (var (_, resultId) in toComplete)
                SpawnResultOnBoard(resultId);

            if (toComplete.Count > 0)
                EjectCardsFromStructure(forge, toComplete.Select(t => t.card));

            return false;
        }

        private static void ClassifyHammerItem(
            object card,
            List<(object, string)> toComplete,
            List<(object, float, float)> toAdvance)
        {
            var hit = GetHammerHitInfo(card);
            if (!hit.canHammer) return;

            float curS1 = GetDurabilityStatValue(card, "SpecialDurability1");
            if (float.IsNaN(curS1)) return;

            float newS1 = curS1 + hit.special1Change; // special1Change is negative (e.g., -1)
            if (newS1 <= 0f && hit.onZeroResultId != null)
                toComplete.Add((card, hit.onZeroResultId));
            else
                toAdvance.Add((card, hit.special1Change, hit.fuelChange));
        }

        private struct HammerHitInfo
        {
            public bool   canHammer;
            public float  special1Change; // negative per-hit decrement, e.g. -1.0
            public float  fuelChange;     // negative heat loss per hit, e.g. -25.0
            public string onZeroResultId; // UniqueID to spawn when Strikes reach 0
        }

        private static HammerHitInfo GetHammerHitInfo(object card)
        {
            var info = new HammerHitInfo();
            try
            {
                object cardData = GetCardData(card);
                if (cardData == null) return info;
                Type cdType = cardData.GetType();

                // 1. Find the hammer CardInteraction (tag_Hammer trigger)
                var ciField = cdType.GetField("CardInteractions", Flags);
                var cis = ciField?.GetValue(cardData) as IList;
                if (cis == null) return info;

                foreach (var ci in cis)
                {
                    if (ci == null) continue;
                    var compat = ci.GetType().GetField("CompatibleCards", Flags)?.GetValue(ci);
                    if (compat == null) continue;
                    var trigTags = compat.GetType().GetField("TriggerTags", Flags)?.GetValue(compat) as IList;
                    if (trigTags == null || trigTags.Count == 0) continue;

                    bool isHammer = false;
                    foreach (var t in trigTags)
                    {
                        string tn = (t is UnityEngine.Object uo) ? uo.name : null;
                        if (tn == "tag_Hammer" || tn == HammerToolTag) { isHammer = true; break; }
                    }
                    if (!isHammer) continue;

                    info.canHammer = true;
                    var rcc = ci.GetType().GetField("ReceivingCardChanges", Flags)?.GetValue(ci);
                    if (rcc != null)
                    {
                        var s1Raw = rcc.GetType().GetField("Special1Change", Flags)?.GetValue(rcc);
                        if (s1Raw is UnityEngine.Vector2 s1v) info.special1Change = s1v.x;
                        var fRaw = rcc.GetType().GetField("FuelChange", Flags)?.GetValue(rcc);
                        if (fRaw is UnityEngine.Vector2 fv) info.fuelChange = fv.x;
                    }
                    break;
                }
                if (!info.canHammer) return info;

                // 2. Read SpecialDurability1.OnZero result from CardData definition
                var sd1Def = cdType.GetField("SpecialDurability1", Flags)?.GetValue(cardData);
                if (sd1Def == null) return info;
                var hasOnZero = sd1Def.GetType().GetField("HasActionOnZero", Flags)?.GetValue(sd1Def);
                if (hasOnZero is bool b && !b) return info;

                var onZero = sd1Def.GetType().GetField("OnZero", Flags)?.GetValue(sd1Def);
                var prods  = onZero?.GetType().GetField("ProducedCards", Flags)?.GetValue(onZero) as IList;
                if (prods == null || prods.Count == 0) return info;

                var coll   = prods[0];
                var drops  = coll?.GetType().GetField("DroppedCards", Flags)?.GetValue(coll) as IList;
                if (drops == null || drops.Count == 0) return info;

                var drop   = drops[0];
                var dc     = drop?.GetType().GetField("DroppedCard", Flags)?.GetValue(drop);
                if (dc is UniqueIDScriptable uid)
                    info.onZeroResultId = uid.UniqueID;
                else
                    info.onZeroResultId = dc?.GetType().GetField("UniqueID", Flags)?.GetValue(dc) as string;
            }
            catch (Exception ex)
            {
                Logger?.Log(LogLevel.Debug, $"[ActionIntercept] GetHammerHitInfo error: {ex.Message}");
            }
            return info;
        }

        private static float GetDurabilityStatValue(object card, string statName)
        {
            try
            {
                var cardType  = card.GetType();

                // EA 0.62b: InGameCardBase exposes raw current values as flat properties/fields.
                // Try this path first since DurabilityStats no longer exists on the runtime card.
                var directName = MapStatToRuntimeMember(statName);
                if (directName != null)
                {
                    var dp = cardType.GetProperty(directName, Flags);
                    if (dp != null && dp.CanRead) return Convert.ToSingle(dp.GetValue(card));
                    var df = cardType.GetField(directName, Flags);
                    if (df != null) return Convert.ToSingle(df.GetValue(card));
                }

                var sf        = cardType.GetField("DurabilityStats", Flags)
                             ?? cardType.GetField("CardDurabilities", Flags);
                if (sf == null) return float.NaN;
                var stats     = sf.GetValue(card);
                if (stats == null) return float.NaN;
                var durProp   = stats.GetType().GetProperty(statName, Flags);
                if (durProp == null) return float.NaN;
                var dur       = durProp.GetValue(stats);
                if (dur == null) return float.NaN;
                var durType   = dur.GetType();
                var cvProp    = durType.GetProperty("CurrentValue", Flags)
                             ?? durType.GetProperty("FloatValue",   Flags);
                if (cvProp != null) return Convert.ToSingle(cvProp.GetValue(dur));
                // Fall back to direct field (Unity serialized fields are public fields, not properties)
                var fv = durType.GetField("FloatValue", Flags)
                      ?? durType.GetField("CurrentValue", Flags);
                return fv != null ? Convert.ToSingle(fv.GetValue(dur)) : float.NaN;
            }
            catch { return float.NaN; }
        }

        private static bool SetDurabilityStatValue(object card, string statName, float newValue)
        {
            try
            {
                var cardType  = card.GetType();

                // EA 0.62b: InGameCardBase exposes raw current values as flat properties/fields.
                var directName = MapStatToRuntimeMember(statName);
                if (directName != null)
                {
                    var dp = cardType.GetProperty(directName, Flags);
                    if (dp != null && dp.CanWrite)
                    {
                        dp.SetValue(card, newValue);
                        return true;
                    }
                    if (dp != null)
                    {
                        var setter = dp.GetSetMethod(nonPublic: true);
                        if (setter != null) { setter.Invoke(card, new object[] { newValue }); return true; }
                    }
                    var df = cardType.GetField(directName, Flags);
                    if (df != null) { df.SetValue(card, newValue); return true; }
                }

                var sf        = cardType.GetField("DurabilityStats", Flags)
                             ?? cardType.GetField("CardDurabilities", Flags);
                if (sf == null) return false;
                var stats     = sf.GetValue(card);
                if (stats == null) return false;
                var durProp   = stats.GetType().GetProperty(statName, Flags);
                if (durProp == null) return false;
                var dur       = durProp.GetValue(stats);
                if (dur == null) return false;
                var durType   = dur.GetType();

                // 3-path write: CanWrite → non-public setter → backing field → direct field
                // (Unity/Mono CanWrite returns false for non-public setters)
                var cvProp = durType.GetProperty("CurrentValue", Flags)
                          ?? durType.GetProperty("FloatValue",   Flags);
                if (cvProp != null)
                {
                    if (cvProp.CanWrite)
                    {
                        cvProp.SetValue(dur, newValue);
                    }
                    else
                    {
                        var setter = cvProp.GetSetMethod(nonPublic: true);
                        if (setter != null)
                        {
                            setter.Invoke(dur, new object[] { newValue });
                        }
                        else
                        {
                            var t = durType;
                            FieldInfo bf = null;
                            while (t != null && bf == null)
                            {
                                bf = t.GetField($"<{cvProp.Name}>k__BackingField",
                                    BindingFlags.Instance | BindingFlags.NonPublic);
                                t = t.BaseType;
                            }
                            if (bf == null) return false;
                            bf.SetValue(dur, newValue);
                        }
                    }
                }
                else
                {
                    // Fall back to direct field (Unity serialized fields are public fields)
                    var fv = durType.GetField("FloatValue", Flags)
                          ?? durType.GetField("CurrentValue", Flags);
                    if (fv == null) return false;
                    fv.SetValue(dur, newValue);
                }

                if (dur.GetType().IsValueType)   durProp.SetValue(stats, dur);
                if (stats.GetType().IsValueType) sf.SetValue(card, stats);
                return true;
            }
            catch { return false; }
        }

        // EA 0.62b: InGameCardBase exposes raw current durability values as flat
        // properties/fields, not nested under a DurabilityStats container.
        private static string MapStatToRuntimeMember(string statName)
        {
            switch (statName)
            {
                case "Progress":           return "CurrentProgress";
                case "SpecialDurability1": return "CurrentSpecial1";
                case "SpecialDurability2": return "CurrentSpecial2";
                case "SpecialDurability3": return "CurrentSpecial3";
                case "SpecialDurability4": return "CurrentSpecial4";
                case "FuelCapacity":       return "CurrentFuel";
                case "UsageDurability":    return "CurrentUsage";
                case "SpoilageTime":       return "CurrentSpoilage";
                default:                   return null;
            }
        }

        private static void EjectCardsFromStructure(object structure, IEnumerable<object> cards)
        {
            try
            {
                var cardSet   = new HashSet<object>(cards);
                var slots     = GetInventoryList(structure);
                if (slots == null) return;
                var emptySlots = new List<object>();

                foreach (var slot in slots)
                {
                    if (slot == null) continue;
                    var inner = GetInventoryList(slot);
                    if (inner != null)
                    {
                        var toRemove = new List<object>();
                        foreach (var c in inner)
                            if (c != null && cardSet.Contains(c)) toRemove.Add(c);
                        foreach (var c in toRemove) { inner.Remove(c); TrySetActive(c, false); }
                        if (inner.Count == 0) emptySlots.Add(slot);
                    }
                    else if (cardSet.Contains(slot))
                    {
                        emptySlots.Add(slot);
                        TrySetActive(slot, false);
                    }
                }
                foreach (var slot in emptySlots) slots.Remove(slot);
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[ActionIntercept] EjectCardsFromStructure error: {ex.Message}");
            }
        }

        // ============================================================
        //  ORE SLUICE - process mud piles for greenstone, flint, and stone
        // ============================================================
        private static readonly System.Random _sluiceRng = new System.Random();

        private static bool _sluiceHandled; // guard against double-fire from multiple prefix patches

        private static bool HandleSluiceAll(object sluice)
        {
            // Prevent double execution — PerformStackAction and ActionRoutine both fire
            if (_sluiceHandled) return false;
            _sluiceHandled = true;
            try { return HandleSluiceAllInner(sluice); }
            finally { _sluiceHandled = false; }
        }

        private static bool HandleSluiceAllInner(object sluice)
        {
            Logger?.Log(LogLevel.Debug, $"[ActionIntercept] Sluice: HandleSluiceAll called, sluice type={sluice?.GetType().Name}");
            var inventory = GetInventoryList(sluice);
            if (inventory == null)
            {
                Logger?.Log(LogLevel.Debug, $"[ActionIntercept] Sluice: inventory is NULL for type={sluice?.GetType().Name}");
                return false;
            }

            Logger?.Log(LogLevel.Debug, $"[ActionIntercept] Sluice: inventory count={inventory.Count}");

            // Collect processable slots.  Inventory items are InventorySlots
            // that wrap a stack of N cards (displayed as "x6" in UI).
            // We track each slot + its inner card list for proper removal.
            var slotsToProcess = new List<(object slot, IList innerCards)>();
            int totalRolls = 0;

            foreach (var item in inventory)
            {
                if (item == null) continue;
                string uid = GetCardUniqueId(item);
                Logger?.Log(LogLevel.Debug, $"[ActionIntercept] Sluice: inventory item uid='{uid}', type={item.GetType().Name}");
                if (uid != MudPileGUID && uid != DirtPileGUID && uid != FineDirtGUID)
                    continue;

                // Get inner cards from InventorySlot
                var innerCards = GetInventoryList(item);
                int count = (innerCards != null && innerCards.Count > 0)
                    ? innerCards.Count
                    : Math.Max(1, GetCardCharges(item));
                Logger?.Log(LogLevel.Debug, $"[ActionIntercept] Sluice: slot contains {count} cards (innerList={innerCards != null})");
                totalRolls += count;
                slotsToProcess.Add((item, innerCards));
            }

            if (totalRolls == 0)
            {
                Logger?.Log(LogLevel.Debug, "[ActionIntercept] Sluice: no mud piles found in inventory");
                return false;
            }

            Logger?.Log(LogLevel.Debug, $"[ActionIntercept] Sluice: processing {totalRolls} mud pile rolls from {slotsToProcess.Count} slot(s)");

            // Transform each inner mud pile in-place into a result card.
            // Distribution guarantees a result per pile (no "nothing" outcome)
            // so we never need a destroy path — which is the behavior that used
            // to relocate cards to the nearest container (furnace/forge).
            //   5%  greenstone
            //   10% flint
            //   85% stone
            int greenstoneCount = 0;
            int flintCount = 0;
            int stoneCount = 0;

            foreach (var (slot, innerCards) in slotsToProcess)
            {
                if (innerCards == null || innerCards.Count == 0)
                {
                    // Single-card slot with no inner list — transform the slot card itself
                    string guid = RollResultGuid();
                    if (TransformCardInPlace(slot, guid))
                        IncrResult(guid, ref greenstoneCount, ref flintCount, ref stoneCount);
                    continue;
                }

                for (int i = 0; i < innerCards.Count; i++)
                {
                    var inner = innerCards[i];
                    if (inner == null) continue;
                    string guid = RollResultGuid();
                    if (TransformCardInPlace(inner, guid))
                        IncrResult(guid, ref greenstoneCount, ref flintCount, ref stoneCount);
                }
            }

            Logger?.Log(LogLevel.Debug,
                $"[ActionIntercept] Sluice: transformed {totalRolls} mud piles -> {greenstoneCount} greenstone, {flintCount} flint, {stoneCount} stone");

            return false; // block JSON action, we handled everything
        }

        private static string RollResultGuid()
        {
            double roll = _sluiceRng.NextDouble();
            if (roll < 0.05) return GreenstoneGUID;
            if (roll < 0.15) return FlintGUID;
            return StoneGUID;
        }

        private static void IncrResult(string guid, ref int g, ref int f, ref int s)
        {
            if (guid == GreenstoneGUID) g++;
            else if (guid == FlintGUID) f++;
            else s++;
        }

        // Cached reflection handles for in-place transform
        private static PropertyInfo _cardModelSetProp;
        private static MethodInfo _cardResetMethod;
        private static MethodInfo _cardSetupMethod;
        private static Type _transformCardType;

        /// <summary>
        /// Replaces a card's CardData (CardModel) with the target in-place.
        /// Avoids destroying the card — which triggers OnDestroy relocation — by
        /// mutating the existing in-world card object instead. Returns true on
        /// success.
        /// </summary>
        private static bool TransformCardInPlace(object card, string targetGuid)
        {
            if (card == null || string.IsNullOrEmpty(targetGuid)) return false;
            try
            {
                InitSpawnReflection();
                if (_getFromIDMethod == null)
                {
                    Logger?.LogError("[ActionIntercept] Transform: GetFromID reflection not available");
                    return false;
                }

                var targetData = _getFromIDMethod.Invoke(null, new object[] { targetGuid });
                if (targetData == null)
                {
                    Logger?.LogError($"[ActionIntercept] Transform: CardData not found for {targetGuid}");
                    return false;
                }

                Type cardType = card.GetType();
                if (_transformCardType != cardType)
                {
                    _transformCardType = cardType;
                    _cardModelSetProp = cardType.GetProperty("CardModel", Flags);
                    _cardResetMethod = cardType.GetMethod("ResetCard", Flags, null, Type.EmptyTypes, null);
                    // SetupCardSource(CardData) is used by vanilla to (re)initialize a card from a model
                    _cardSetupMethod = cardType.GetMethods(Flags)
                        .FirstOrDefault(m => m.Name == "SetupCardSource"
                            && m.GetParameters().Length >= 1
                            && m.GetParameters()[0].ParameterType.IsAssignableFrom(_cardDataType));
                    Logger?.Log(LogLevel.Debug, $"[ActionIntercept] Transform reflection for {cardType.Name}: " +
                        $"CardModel={(_cardModelSetProp != null)}, ResetCard={(_cardResetMethod != null)}, " +
                        $"SetupCardSource={(_cardSetupMethod != null)}");
                }

                if (_cardModelSetProp == null || !_cardModelSetProp.CanWrite)
                {
                    Logger?.LogError($"[ActionIntercept] Transform: CardModel not settable on {cardType.Name}");
                    return false;
                }

                // Swap the card's data model to the result
                _cardModelSetProp.SetValue(card, targetData);

                // Re-initialize derived state (durabilities, sprite, tags) from the new model
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

                return true;
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[ActionIntercept] Transform failed: {ex.Message}");
                return false;
            }
        }

        // Cached reflection handles for GiveCard
        private static MethodInfo _giveCardMethod;
        private static MethodInfo _getFromIDMethod;
        private static Type _cardDataType;
        private static Type _gmType;
        private static bool _spawnReflectionInit;

        private static void InitSpawnReflection()
        {
            if (_spawnReflectionInit && _getFromIDMethod != null && _giveCardMethod != null) return;
            _spawnReflectionInit = true;

            try
            {
                // Resolve types by scanning loaded assemblies (same approach as H&F)
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (_cardDataType == null)
                        _cardDataType = assembly.GetType("CardData", false);
                    if (_gmType == null)
                        _gmType = assembly.GetType("GameManager", false);
                }

                Logger?.Log(LogLevel.Debug, $"[ActionIntercept] SpawnInit: CardData={_cardDataType != null}, GameManager={_gmType != null}");

                if (_cardDataType == null || _gmType == null)
                {
                    Logger?.LogError("[ActionIntercept] SpawnInit: required types not found");
                    return;
                }

                // UniqueIDScriptable.GetFromID<CardData> — scan for the type too
                Type uidType = null;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    uidType = assembly.GetType("UniqueIDScriptable", false);
                    if (uidType != null) break;
                }

                if (uidType != null)
                {
                    // GetFromID has multiple overloads — find the generic one to avoid AmbiguousMatchException
                    var getFromID = uidType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(m => m.Name == "GetFromID" && m.IsGenericMethodDefinition);
                    Logger?.Log(LogLevel.Debug, $"[ActionIntercept] SpawnInit: UniqueIDScriptable={uidType.Assembly.GetName().Name}, GetFromID generic={getFromID != null}");
                    if (getFromID != null)
                        _getFromIDMethod = getFromID.MakeGenericMethod(_cardDataType);
                }
                else
                {
                    Logger?.LogError("[ActionIntercept] SpawnInit: UniqueIDScriptable type not found");
                }

                // GameManager.GiveCard(CardData, bool)
                _giveCardMethod = AccessTools.Method(_gmType, "GiveCard", new[] { _cardDataType, typeof(bool) });

                // If exact signature not found, search all GiveCard overloads
                if (_giveCardMethod == null)
                {
                    var candidates = _gmType.GetMethods(Flags)
                        .Where(m => m.Name == "GiveCard")
                        .ToArray();
                    Logger?.Log(LogLevel.Debug, $"[ActionIntercept] SpawnInit: GiveCard overloads found: {candidates.Length}");
                    foreach (var m in candidates)
                    {
                        var p = m.GetParameters();
                        Logger?.Log(LogLevel.Debug, $"[ActionIntercept]   GiveCard({string.Join(", ", p.Select(pp => pp.ParameterType.Name))})");
                        if (p.Length >= 1 && p[0].ParameterType.IsAssignableFrom(_cardDataType))
                        {
                            _giveCardMethod = m;
                            break;
                        }
                    }
                }

                Logger?.Log(LogLevel.Debug, $"[ActionIntercept] SpawnInit: GetFromID={_getFromIDMethod != null}, GiveCard={_giveCardMethod != null}");
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[ActionIntercept] SpawnInit error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static bool IsSluiceAllAction(object action)
        {
            string actionName = GetActionName(action);
            if (string.Equals(actionName, "Sluice All", StringComparison.OrdinalIgnoreCase))
                return true;

            string locKey = GetActionLocalizationKey(action);
            if (string.Equals(locKey, "Water_Sawmill_OreSluicePlaced_SluiceAll_ActionName", StringComparison.Ordinal))
                return true;

            return false;
        }

        private static object FindSluiceCard(object action, params object[] candidates)
        {
            if (candidates != null)
            {
                foreach (var candidate in candidates)
                {
                    if (candidate == null) continue;

                    if (IsSluiceCard(candidate))
                        return candidate;

                    if (candidate is IList list)
                    {
                        foreach (var item in list)
                        {
                            if (item != null && IsSluiceCard(item))
                                return item;
                        }
                    }
                }
            }

            // Last fallback: inspect action object fields/properties for card refs.
            if (action != null)
            {
                var t = action.GetType();

                foreach (var f in t.GetFields(Flags))
                {
                    try
                    {
                        var val = f.GetValue(action);
                        if (IsSluiceCard(val))
                            return val;
                    }
                    catch { }
                }

                foreach (var p in t.GetProperties(Flags))
                {
                    if (!p.CanRead || p.GetIndexParameters().Length > 0)
                        continue;

                    try
                    {
                        var val = p.GetValue(action, null);
                        if (IsSluiceCard(val))
                            return val;
                    }
                    catch { }
                }
            }

            return null;
        }

        private static bool IsSluiceCard(object card)
        {
            if (card == null) return false;
            string uid = GetCardUniqueId(card);
            return string.Equals(uid, SluiceID, StringComparison.Ordinal);
        }

        private static string GetActionLocalizationKey(object action)
        {
            try
            {
                if (action == null) return null;

                if (_actionNameField == null)
                    _actionNameField = action.GetType().GetField("ActionName", Flags);
                if (_actionNameField == null) return null;

                var nameObj = _actionNameField.GetValue(action);
                if (nameObj == null) return null;

                var locKeyField = nameObj.GetType().GetField("LocalizationKey", Flags);
                return locKeyField?.GetValue(nameObj) as string;
            }
            catch
            {
                return null;
            }
        }

        // ============================================================
        //  REFLECTION SETUP
        // ============================================================
        /// <summary>
        /// Resolves and caches reflection handles. Card-specific fields (CardData, inventory,
        /// remove method) are cached per-Type since different InGameCardBase subtypes may
        /// have different layouts. Action and UniqueID fields are shared across all types.
        /// </summary>
        /// <summary>
        /// Resolves and caches reflection handles. Card-specific fields are cached per-Type.
        /// The game uses a PROPERTY named "CardModel" (not a field "CardData") on InGameCardBase.
        /// </summary>
        private static void EnsureReflection(object card, object action)
        {
            try
            {
                Type cardType = card?.GetType();

                if (cardType != null && !_cardModelPropCache.ContainsKey(cardType))
                {
                    // CardModel property (the game's actual name for card data on InGameCardBase)
                    var cmProp = cardType.GetProperty("CardModel", Flags);
                    _cardModelPropCache[cardType] = cmProp;

                    // UniqueID field on CardData/CardModel type (shared — resolve once)
                    if (_uidField == null && cmProp != null)
                    {
                        object cardModel = cmProp.GetValue(card);
                        if (cardModel != null)
                            _uidField = cardModel.GetType().GetField("UniqueID", Flags);
                    }

                    // Inventory field for this card type
                    FieldInfo invField = null;
                    string[] inventoryNames = {
                        "ContainedCards", "AllCardsInSlots",
                        "CardsInInventory", "InventoryCards", "CardsInSlots"
                    };
                    foreach (var name in inventoryNames)
                    {
                        var f = cardType.GetField(name, Flags);
                        if (f != null && typeof(IList).IsAssignableFrom(f.FieldType))
                        {
                            invField = f;
                            break;
                        }
                    }
                    if (invField == null)
                    {
                        foreach (var f in cardType.GetFields(Flags))
                        {
                            if (!f.FieldType.IsGenericType) continue;
                            if (!typeof(IList).IsAssignableFrom(f.FieldType)) continue;
                            var args = f.FieldType.GetGenericArguments();
                            if (args.Length == 1 && args[0].Name.Contains("Card")
                                && !f.Name.Contains("Tag") && !f.Name.Contains("Slot"))
                            {
                                invField = f;
                                break;
                            }
                        }
                    }
                    _inventoryFieldCache[cardType] = invField;

                    // Remove method for this card type
                    MethodInfo removeMethod = null;
                    string[] removeNames = { "RemoveFromGame", "DestroyCard", "DestroyCardFromInventory" };
                    foreach (var name in removeNames)
                    {
                        var methods = cardType.GetMethods(Flags).Where(m => m.Name == name).ToArray();
                        foreach (var m in methods)
                        {
                            var p = m.GetParameters();
                            if (p.Length == 0 || (p.Length <= 2 && p.All(pp => pp.ParameterType == typeof(bool))))
                            {
                                removeMethod = m;
                                break;
                            }
                        }
                        if (removeMethod != null) break;
                    }
                    _removeMethodCache[cardType] = removeMethod;

                    Logger?.Log(LogLevel.Debug, $"[ActionIntercept] Reflected type {cardType.Name}: " +
                        $"cardModel={(cmProp?.Name ?? "NONE")}, " +
                        $"inventory={(invField?.Name ?? "NONE")}, " +
                        $"remove={(removeMethod?.Name ?? "NONE")}");
                }

                // ActionName (shared — resolve once from any action)
                if (_actionNameField == null && action != null)
                {
                    _actionNameField = action.GetType().GetField("ActionName", Flags);
                    if (_actionNameField != null)
                    {
                        var nameObj = _actionNameField.GetValue(action);
                        if (nameObj != null)
                            _defaultTextField = nameObj.GetType().GetField("DefaultText", Flags);
                    }
                }

                // Fuel (resolve once)
                if (!_reflected && cardType != null)
                {
                    string[] fuelProps = { "CurrentFuel", "CurrentFuelAmount", "FuelAmount" };
                    foreach (var name in fuelProps)
                    {
                        var p = cardType.GetProperty(name, Flags);
                        if (p != null && p.CanRead && p.CanWrite)
                        {
                            _currentFuelProp = p;
                            break;
                        }
                    }
                    if (_currentFuelProp == null)
                    {
                        string[] fuelFields = {
                            "currentFuel", "_currentFuel", "m_CurrentFuel",
                            "CurrentFuelCapacity", "currentFuelCapacity"
                        };
                        foreach (var name in fuelFields)
                        {
                            var f = cardType.GetField(name, Flags);
                            if (f != null) { _currentFuelField = f; break; }
                        }
                    }
                }

                _reflected = true;
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[ActionIntercept] Reflection error: {ex.Message}");
            }
        }

        // ============================================================
        //  Card Identity
        // ============================================================
        private static string GetCardUniqueId(object card)
        {
            try
            {
                if (card == null) return null;

                // CardData/ScriptableObject — has UniqueID directly
                if (card is UniqueIDScriptable s)
                    return s.UniqueID;

                // InGameCardBase — use CardModel property
                EnsureReflection(card, null);

                Type cardType = card.GetType();
                _cardModelPropCache.TryGetValue(cardType, out var cmProp);
                if (cmProp == null) return null;

                object cardModel = cmProp.GetValue(card);
                if (cardModel == null) return null;

                if (cardModel is UniqueIDScriptable s2)
                    return s2.UniqueID;

                return _uidField?.GetValue(cardModel) as string;
            }
            catch { return null; }
        }

        private static string GetActionName(object action)
        {
            try
            {
                if (!_reflected) EnsureReflection(null, action);

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

        // ============================================================
        //  Inventory Access
        // ============================================================
        private static IList GetInventoryList(object card)
        {
            try
            {
                EnsureReflection(card, null);
                Type cardType = card.GetType();
                _inventoryFieldCache.TryGetValue(cardType, out var invField);
                if (invField == null) return null;
                return invField.GetValue(card) as IList;
            }
            catch { return null; }
        }

        private static object FindFirstByUniqueId(IList inventory, string targetId)
        {
            foreach (var item in inventory)
            {
                if (item == null) continue;
                if (GetCardUniqueId(item) == targetId)
                    return item;
            }
            return null;
        }

        private static int GetCardCharges(object card)
        {
            try
            {
                var cardType = card.GetType();
                var prop = cardType.GetProperty("CurrentCharges", Flags)
                        ?? cardType.GetProperty("Charges", Flags);
                if (prop != null && prop.CanRead)
                    return Convert.ToInt32(prop.GetValue(card));

                var field = cardType.GetField("CurrentCharges", Flags)
                         ?? cardType.GetField("currentCharges", Flags);
                if (field != null)
                    return Convert.ToInt32(field.GetValue(card));
            }
            catch { }
            return 1;
        }

        // ============================================================
        //  Card Removal
        // ============================================================
        private static bool RemoveCard(object card, IList inventory)
        {
            try
            {
                Type cardType = card.GetType();

                // Ensure reflection is resolved for this card's type
                // (inner cards may be InGameDraggableCard, not InventorySlot)
                EnsureReflection(card, null);

                _removeMethodCache.TryGetValue(cardType, out var removeMethod);

                // Primary: game API (RemoveFromGame / DestroyCard)
                if (removeMethod != null)
                {
                    Logger?.Log(LogLevel.Debug, $"[ActionIntercept] RemoveCard: using {removeMethod.Name} on {cardType.Name}");
                    var p = removeMethod.GetParameters();
                    if (p.Length == 0)
                        removeMethod.Invoke(card, null);
                    else if (p.Length == 1)
                        removeMethod.Invoke(card, new object[] { true });
                    else if (p.Length == 2)
                        removeMethod.Invoke(card, new object[] { true, true });
                    return true;
                }

                // Fallback: just remove from list — do NOT call Object.Destroy
                // (Object.Destroy triggers OnDestroy callbacks that relocate cards
                //  to the nearest container instead of actually destroying them)
                Logger?.Log(LogLevel.Debug, $"[ActionIntercept] RemoveCard: no game API for {cardType.Name}, using list removal only");
                if (inventory != null)
                    inventory.Remove(card);

                return true;
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[ActionIntercept] RemoveCard failed: {ex.Message}");
                return false;
            }
        }

        // ============================================================
        //  Fuel Manipulation
        // ============================================================
        private static void AddFuel(object card, float amount)
        {
            try
            {
                // Direct property
                if (_currentFuelProp != null)
                {
                    float cur = Convert.ToSingle(_currentFuelProp.GetValue(card));
                    float max = GetFuelMax(card);
                    float newVal = Math.Min(cur + amount, max > 0 ? max : float.MaxValue);
                    _currentFuelProp.SetValue(card, newVal);
                    Logger?.Log(LogLevel.Debug, $"[ActionIntercept] Fuel {cur} -> {newVal} (via property)");
                    return;
                }

                // Direct field
                if (_currentFuelField != null)
                {
                    float cur = Convert.ToSingle(_currentFuelField.GetValue(card));
                    float max = GetFuelMax(card);
                    float newVal = Math.Min(cur + amount, max > 0 ? max : float.MaxValue);
                    _currentFuelField.SetValue(card, newVal);
                    Logger?.Log(LogLevel.Debug, $"[ActionIntercept] Fuel {cur} -> {newVal} (via field)");
                    return;
                }

                // Deep access: DurabilityStats -> FuelCapacity -> CurrentValue
                var cardType = card.GetType();
                var statsField = cardType.GetField("DurabilityStats", Flags)
                              ?? cardType.GetField("CardDurabilities", Flags);
                if (statsField != null)
                {
                    var stats = statsField.GetValue(card);
                    if (stats != null)
                    {
                        var fuelProp = stats.GetType().GetProperty("FuelCapacity", Flags)
                                    ?? stats.GetType().GetProperty("Fuel", Flags);
                        if (fuelProp != null)
                        {
                            var fuelObj = fuelProp.GetValue(stats);
                            if (fuelObj != null)
                            {
                                var curProp = fuelObj.GetType().GetProperty("CurrentValue", Flags)
                                           ?? fuelObj.GetType().GetProperty("FloatValue", Flags);
                                var maxProp = fuelObj.GetType().GetProperty("MaxValue", Flags);
                                if (curProp != null && curProp.CanWrite)
                                {
                                    float cur = Convert.ToSingle(curProp.GetValue(fuelObj));
                                    float max = maxProp != null
                                        ? Convert.ToSingle(maxProp.GetValue(fuelObj)) : 100f;
                                    float newVal = Math.Min(cur + amount, max);
                                    curProp.SetValue(fuelObj, newVal);
                                    Logger?.Log(LogLevel.Debug,
                                        $"[ActionIntercept] Fuel {cur} -> {newVal} (via deep access)");

                                    if (fuelObj.GetType().IsValueType)
                                        fuelProp.SetValue(stats, fuelObj);
                                    if (stats.GetType().IsValueType)
                                        statsField.SetValue(card, stats);
                                    return;
                                }
                            }
                        }
                    }
                }

                Logger?.LogError("[ActionIntercept] AddFuel: no fuel accessor found");
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[ActionIntercept] AddFuel failed: {ex.Message}");
            }
        }

        private static float GetFuelMax(object card)
        {
            try
            {
                var cardType = card.GetType();
                var prop = cardType.GetProperty("MaxFuel", Flags)
                        ?? cardType.GetProperty("MaxFuelCapacity", Flags);
                if (prop != null && prop.CanRead)
                    return Convert.ToSingle(prop.GetValue(card));
            }
            catch { }
            return 100f;
        }
    }
}
