using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using BepInEx.Logging;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace Advanced_Copper_Tools.Patcher
{
    public static class GameLoadPatch
    {
        private static ManualLogSource Logger => Plugin.Logger;

        public static void ApplyPatch(Harmony harmony)
        {
            try
            {
                var gameLoadType = AccessTools.TypeByName("GameLoad");
                if (gameLoadType == null)
                {
                    Logger.LogError("[ACT] GameLoad type not found; load patches not applied.");
                    return;
                }

                var loadMethod = AccessTools.Method(gameLoadType, "LoadMainGameData");
                if (loadMethod == null)
                {
                    Logger.LogError("[ACT] GameLoad.LoadMainGameData not found; load patches not applied.");
                    return;
                }

                var postfix = new HarmonyMethod(typeof(GameLoadPatch), nameof(LoadMainGameData_Postfix));
                postfix.after = new[] { "crispywhips.CSFFModFramework" };
                harmony.Patch(loadMethod, postfix: postfix);

                PatchEncounterArmorRepair(harmony);
                TrySubscribeToGameManagerInitialized();
            }
            catch (Exception ex)
            {
                Logger.LogError($"[ACT] Failed to patch GameLoad.LoadMainGameData: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        static void LoadMainGameData_Postfix(object __instance)
        {
            try
            {
                var dbField = AccessTools.Field(__instance.GetType(), "DataBase");
                var db = dbField?.GetValue(__instance);
                if (db == null) { Logger.LogError("[ACT] Could not access GameLoad.DataBase"); return; }

                var allDataField = AccessTools.Field(db.GetType(), "AllData");
                var allData = allDataField?.GetValue(db) as IEnumerable;
                if (allData == null) { Logger.LogError("[ACT] Could not access DataBase.AllData"); return; }

                VanillaFireKettlePatch.InjectKettleSlots(allData);
                RepairCopperArmorMultipliers(allData);
                PatchNailInterchangeability(allData);
                PatchSheetInterchangeability(allData);
            }
            catch (Exception ex)
            {
                Logger.LogError($"[ACT] LoadMainGameData postfix error: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // Makes every blueprint/improvement element that requires copper nails also accept iron
        // nails, and — since WDI's rivets are the same fastener commodity under a different mod
        // (root CLAUDE.md §Soft-dep doctrine, R-mechanism ALT) — WDI's Copper/Iron Rivet too.
        // Delegates to the framework's shared helper (CSFFModFramework.Api.BlueprintAlternates),
        // which accumulates across repeated calls for the same primary (framework 2.17.0+)
        // instead of clobbering, so all three alternates end up accepted on the same slot. WDI's
        // UIDs are referenced directly as plain strings — AddAlternateIngredient no-ops (and
        // logs at Debug) when the alternate doesn't resolve, so this stays a soft dependency even
        // though WaterDrivenInfrastructure isn't installed.
        private static void PatchNailInterchangeability(IEnumerable allData)
        {
            const string CopperNailUid = "advanced_copper_tools_copper_nails";
            const string IronNailUid = "act_iron_nail";
            const string WdiCopperRivetUid = "water_sawmill_copper_rivet";
            const string WdiIronRivetUid = "water_sawmill_iron_rivet";

            // BlueprintAlternates already logs its own Info-level summary line.
            CSFFModFramework.Api.BlueprintAlternates.AddAlternateIngredient(
                allData, CopperNailUid, IronNailUid, "ACT Copper Nail / Iron Nail");
            CSFFModFramework.Api.BlueprintAlternates.AddAlternateIngredient(
                allData, CopperNailUid, WdiCopperRivetUid, "ACT Copper Nail / WDI Copper Rivet");
            CSFFModFramework.Api.BlueprintAlternates.AddAlternateIngredient(
                allData, CopperNailUid, WdiIronRivetUid, "ACT Copper Nail / WDI Iron Rivet");
        }

        // Same-tier, cross-mod acceptance for sheet material: ACT's Copper/Iron Sheet slots also
        // accept WDI's Cast Copper/Iron Sheet. Unlike nails (a generic fastener, interchangeable
        // across tiers by ACT's own design), sheets stay tier-locked — Iron Sheet only pairs with
        // WDI's Cast Iron Sheet, not the copper one — so iron-tier armor still requires iron-tier
        // material. Soft: no-ops when WaterDrivenInfrastructure isn't installed.
        private static void PatchSheetInterchangeability(IEnumerable allData)
        {
            const string CopperSheetUid = "advanced_copper_tools_metal_sheet";
            const string IronSheetUid = "act_iron_sheet";
            const string WdiCastCopperSheetUid = "water_sawmill_cast_metal_sheet";
            const string WdiCastIronSheetUid = "water_sawmill_cast_iron_sheet";

            CSFFModFramework.Api.BlueprintAlternates.AddAlternateIngredient(
                allData, CopperSheetUid, WdiCastCopperSheetUid, "ACT Copper Sheet / WDI Cast Copper Sheet");
            CSFFModFramework.Api.BlueprintAlternates.AddAlternateIngredient(
                allData, IronSheetUid, WdiCastIronSheetUid, "ACT Iron Sheet / WDI Cast Iron Sheet");
        }

        private static readonly string[] ArmorUids = {
            "advanced_copper_tools_copper_helmet",
            "advanced_copper_tools_copper_breastplate",
            "advanced_copper_tools_copper_gauntlets",
            "advanced_copper_tools_copper_greaves",
        };

        private static readonly System.Collections.Generic.HashSet<string> ArmorUidSet =
            new System.Collections.Generic.HashSet<string>(ArmorUids, StringComparer.OrdinalIgnoreCase);

        private static bool _loggedEncounterArmorRepairError;
        private static bool _subscribedToGmInitialized;
        private static Action _gmInitializedHandler;

        // All four copper armor items share the same multiplier parameters.
        private const string ArmorMultiplierEntryJson =
            "{\"InputDurability\":64,\"Value\":{\"Active\":true," +
            "\"InputValueRange\":{\"x\":0.0,\"y\":100.0}," +
            "\"OutputValueRange\":{\"x\":1.0,\"y\":1.5}," +
            "\"WhenOutOfRange\":0,\"OutOfRangeCustomValue\":0.0}}";

        private static void RepairCopperArmorMultipliers(IEnumerable allData)
        {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var armorSet = new System.Collections.Generic.HashSet<string>(ArmorUids);
            foreach (var item in allData)
            {
                if (item == null) continue;
                var uid = AccessTools.Field(item.GetType(), "UniqueID")?.GetValue(item) as string;
                if (uid == null || !armorSet.Contains(uid)) continue;

                var multField = item.GetType().GetField("ArmorValueDurabilitiesMultiplier", Flags);
                var multArray = multField?.GetValue(item) as Array;
                int multCount = multArray?.Length ?? -1;

                if (multCount == 0 && multField != null)
                    FixArmorMultiplier(item, uid, multField);
            }
        }

        private static void FixArmorMultiplier(object card, string uid, FieldInfo multField)
        {
            try
            {
                var elementType = multField.FieldType.GetElementType();
                if (elementType == null)
                {
                    Logger.LogError($"[ACT-Fix] {uid}: could not get ArmorValueDurabilitiesMultiplier element type");
                    return;
                }

                // JsonUtility.FromJson works for single objects (not arrays).
                // We create one entry and wrap it in a 1-element array.
                var entry = UnityEngine.JsonUtility.FromJson(ArmorMultiplierEntryJson, elementType);
                if (entry == null)
                {
                    Logger.LogError($"[ACT-Fix] {uid}: JsonUtility.FromJson returned null for multiplier entry (type={elementType.Name})");
                    return;
                }

                var newArray = Array.CreateInstance(elementType, 1);
                newArray.SetValue(entry, 0);
                multField.SetValue(card, newArray);
                Logger.LogDebug($"[ACT-Fix] {uid}: ArmorValueDurabilitiesMultiplier restored (1 entry, type={elementType.Name})");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[ACT-Fix] {uid}: FixArmorMultiplier failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void SetStatFloat(object card, string statFieldName, string subFieldName, float value, BindingFlags flags)
        {
            var statField = card.GetType().GetField(statFieldName, flags);
            if (statField == null) return;
            var stat = statField.GetValue(card);
            if (stat == null) return;
            var floatField = stat.GetType().GetField(subFieldName, flags);
            if (floatField == null) return;
            floatField.SetValue(stat, value);
            // Write back in case the stat is a value type (struct).
            statField.SetValue(card, stat);
        }

        private static void PatchEncounterArmorRepair(Harmony harmony)
        {
            try
            {
                var encounterPopupType = AccessTools.TypeByName("EncounterPopup");
                var method = AccessTools.Method(encounterPopupType, "GenerateAndApplyPlayerWound");
                if (method == null)
                {
                    Logger.LogError("[ACT] EncounterPopup.GenerateAndApplyPlayerWound not found; copper armor combat repair not applied.");
                    return;
                }

                harmony.Patch(method, prefix: new HarmonyMethod(typeof(GameLoadPatch), nameof(EncounterArmorRepair_Prefix)));
            }
            catch (Exception ex)
            {
                Logger.LogError($"[ACT] Failed to patch copper armor combat repair: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void EncounterArmorRepair_Prefix(object __instance)
        {
            try
            {
                var gameManager = Reflect.GetMember(__instance, "GM") ?? CardUtil.GetGameManagerInstance();
                var graphicsManager = Reflect.GetMember(__instance, "GraphicsManager");
                var characterWindow = Reflect.GetMember(graphicsManager, "CharacterWindow");
                RepairCopperArmorCards(gameManager, characterWindow, "encounter");
            }
            catch (Exception ex)
            {
                if (_loggedEncounterArmorRepairError) return;
                Logger.LogError($"[ACT] Copper armor combat repair failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
                _loggedEncounterArmorRepairError = true;
            }
        }

        private static void TrySubscribeToGameManagerInitialized()
        {
            if (_subscribedToGmInitialized) return;

            try
            {
                var gmType = AccessTools.TypeByName("GameManager");
                var field = gmType?.GetField("OnGMInitialized", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null || field.FieldType != typeof(Action))
                {
                    Logger.LogError("[ACT] GameManager.OnGMInitialized not found; save-load copper armor repair unavailable.");
                    return;
                }

                _gmInitializedHandler = OnGameManagerInitialized;
                var current = (Action)field.GetValue(null);
                field.SetValue(null, (Action)Delegate.Combine(current, _gmInitializedHandler));
                _subscribedToGmInitialized = true;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[ACT] Failed to subscribe copper armor save-load repair: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // Removes the OnGMInitialized subscription so a post-teardown fire can't run against
        // a nulled Logger / destroyed Plugin instance. Called from Plugin.OnModDestroy.
        public static void Unsubscribe()
        {
            if (!_subscribedToGmInitialized || _gmInitializedHandler == null) return;
            try
            {
                var gmType = AccessTools.TypeByName("GameManager");
                var field = gmType?.GetField("OnGMInitialized", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null && field.FieldType == typeof(Action))
                {
                    var current = (Action)field.GetValue(null);
                    field.SetValue(null, (Action)Delegate.Remove(current, _gmInitializedHandler));
                }
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[ACT] Failed to remove copper armor save-load repair subscription: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
            finally
            {
                _gmInitializedHandler = null;
                _subscribedToGmInitialized = false;
            }
        }

        private static void OnGameManagerInitialized()
        {
            var host = Plugin.Instance;
            if (host != null)
            {
                host.StartCoroutine(DeferredRepairCopperArmorCards());
                return;
            }

            RepairCopperArmorCards(CardUtil.GetGameManagerInstance(), null, "save-load");
        }

        private static IEnumerator DeferredRepairCopperArmorCards()
        {
            yield return null;
            RepairCopperArmorCards(CardUtil.GetGameManagerInstance(), null, "save-load");
        }

        private static int RepairCopperArmorCards(object gameManager, object characterWindow, string phase)
        {
            if (gameManager == null) return 0;

            var armorCards = Reflect.GetMember(gameManager, "ArmorCards") as System.Collections.IList;
            if (armorCards == null) return 0;

            if (characterWindow == null)
            {
                var gameGraphics = Reflect.GetMember(gameManager, "GameGraphics") ?? Reflect.GetMember(gameManager, "GraphicsManager");
                characterWindow = Reflect.GetMember(gameGraphics, "CharacterWindow");
            }

            var copperCards = new System.Collections.Generic.List<object>();
            int added = AddCopperArmorFromList(Reflect.GetMember(gameManager, "AllCards") as IEnumerable, armorCards, copperCards);

            foreach (var equippedCard in FindEquippedCopperArmorCards(characterWindow))
            {
                if (equippedCard == null) continue;
                if (!copperCards.Contains(equippedCard)) copperCards.Add(equippedCard);
                if (armorCards.Contains(equippedCard)) continue;

                armorCards.Add(equippedCard);
                added++;
            }

            if (copperCards.Count > 0 && string.Equals(phase, "save-load", StringComparison.OrdinalIgnoreCase))
                RefreshCopperArmorPassiveEffects(copperCards);

            if (added > 0)
                Logger?.LogDebug($"[ACT-Fix] Copper armor combat list repaired ({added} card(s), {phase}).");

            return added;
        }

        private static int AddCopperArmorFromList(IEnumerable cards, System.Collections.IList armorCards, System.Collections.Generic.List<object> copperCards)
        {
            if (cards == null) return 0;

            int added = 0;
            foreach (var card in cards)
            {
                if (card == null) continue;
                var uid = GetCardUid(card);
                if (!ArmorUidSet.Contains(uid)) continue;

                if (!copperCards.Contains(card)) copperCards.Add(card);
                if (armorCards.Contains(card)) continue;

                armorCards.Add(card);
                added++;
            }

            return added;
        }

        private static void RefreshCopperArmorPassiveEffects(System.Collections.Generic.List<object> copperCards)
        {
            var host = Plugin.Instance;
            if (host == null) return;

            foreach (var card in copperCards)
            {
                try
                {
                    var update = AccessTools.Method(card.GetType(), "UpdatePassiveEffects");
                    var routine = update?.Invoke(card, null) as IEnumerator;
                    if (routine != null) host.StartCoroutine(routine);
                }
                catch (Exception ex) { Logger?.LogError($"[ACT] RefreshPassiveEffects failed: {ex.InnerException?.ToString() ?? ex.ToString()}"); }
            }
        }

        private static System.Collections.Generic.List<object> FindEquippedCopperArmorCards(object characterWindow)
        {
            var equippedCards = new System.Collections.Generic.List<object>();
            var equipmentLine = Reflect.GetMember(characterWindow, "EquipmentSlotsLine");
            var slots = Reflect.GetMember(equipmentLine, "Slots") as System.Collections.IEnumerable;
            if (slots == null) return equippedCards;

            foreach (var slotObject in slots)
            {
                var assignedCard = Reflect.GetMember(slotObject, "AssignedCard");
                var uid = GetCardUid(assignedCard);
                if (!ArmorUidSet.Contains(uid)) continue;
                equippedCards.Add(assignedCard);
            }

            return equippedCards;
        }

        private static string GetCardUid(object cardObject)
        {
            var model = Reflect.GetMember(cardObject, "CardModel");
            return Reflect.GetMember(model, "UniqueID") as string;
        }
    }
}
