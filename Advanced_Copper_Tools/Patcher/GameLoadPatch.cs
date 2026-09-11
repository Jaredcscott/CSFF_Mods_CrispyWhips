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
        // Additionally, iron-tier armor slots (which require iron nails) also accept copper nails
        // and rivets, so players can use whichever fastener tier they have available. Delegates to
        // the framework's shared helper (CSFFModFramework.Api.BlueprintAlternates), which
        // accumulates across repeated calls for the same primary (framework 2.17.0+) instead of
        // clobbering, so all alternates end up accepted on the same slot. WDI's UIDs are
        // referenced directly as plain strings — AddAlternateIngredient no-ops when WDI isn't
        // installed.
        private static void PatchNailInterchangeability(IEnumerable allData)
        {
            const string CopperNailUid = "advanced_copper_tools_copper_nails";
            const string IronNailUid = "act_iron_nail";
            const string WdiCopperRivetUid = "water_sawmill_copper_rivet";
            const string WdiIronRivetUid = "water_sawmill_iron_rivet";

            // BlueprintAlternates already logs its own Info-level summary line.
            // Copper nail slots accept all fastener types.
            CSFFModFramework.Api.BlueprintAlternates.AddAlternateIngredient(
                allData, CopperNailUid, IronNailUid, "ACT Copper Nail / Iron Nail");
            CSFFModFramework.Api.BlueprintAlternates.AddAlternateIngredient(
                allData, CopperNailUid, WdiCopperRivetUid, "ACT Copper Nail / WDI Copper Rivet");
            CSFFModFramework.Api.BlueprintAlternates.AddAlternateIngredient(
                allData, CopperNailUid, WdiIronRivetUid, "ACT Copper Nail / WDI Iron Rivet");
            
            // Iron nail slots (armor) also accept copper nails and rivets — all fasteners are
            // cross-compatible regardless of metal tier or mod source.
            CSFFModFramework.Api.BlueprintAlternates.AddAlternateIngredient(
                allData, IronNailUid, CopperNailUid, "ACT Iron Nail / Copper Nail");
            CSFFModFramework.Api.BlueprintAlternates.AddAlternateIngredient(
                allData, IronNailUid, WdiCopperRivetUid, "ACT Iron Nail / WDI Copper Rivet");
            CSFFModFramework.Api.BlueprintAlternates.AddAlternateIngredient(
                allData, IronNailUid, WdiIronRivetUid, "ACT Iron Nail / WDI Iron Rivet");
            
            PatchSolderInterchangeability(allData);
        }

        // Makes every blueprint/improvement element that requires tin solder also accept WDI's
        // alloy solder. Both solders are generic fasteners; ACT's tin solder is crafted from tin
        // ore (cave expedition cost), WDI's alloy solder is crafted from copper nuggets (cheaper).
        // With both mods installed, a player can use whichever they have available. WDI's UID is
        // referenced as a plain string — AddAlternateIngredient no-ops when WDI isn't installed.
        private static void PatchSolderInterchangeability(IEnumerable allData)
        {
            const string TinSolderUid = "act_tin_solder";
            const string WdiAlloySolderUid = "water_sawmill_alloy_solder";

            CSFFModFramework.Api.BlueprintAlternates.AddAlternateIngredient(
                allData, TinSolderUid, WdiAlloySolderUid, "ACT Tin Solder / WDI Alloy Solder");
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

        // Every ACT armor item, all three tiers. The save/reload + encounter repair below is not
        // copper-specific: any modded armor drops out of GameManager.ArmorCards the same way, so
        // Bronze and Iron (added after the original copper-only net) are covered too (audit 2026-09-01).
        private static readonly string[] ArmorUids = {
            "advanced_copper_tools_copper_helmet",
            "advanced_copper_tools_copper_breastplate",
            "advanced_copper_tools_copper_gauntlets",
            "advanced_copper_tools_copper_greaves",
            "act_bronze_helmet",
            "act_bronze_breastplate",
            "act_bronze_gauntlets",
            "act_bronze_greaves",
            "act_iron_helmet",
            "act_iron_breastplate_f",
            "act_iron_breastplate_m",
            "act_iron_gauntlets",
            "act_iron_greaves",
        };

        private static readonly System.Collections.Generic.HashSet<string> ArmorUidSet =
            new System.Collections.Generic.HashSet<string>(ArmorUids, StringComparer.OrdinalIgnoreCase);

        private static bool _loggedEncounterArmorRepairError;
        private static bool _subscribedToGmInitialized;
        private static Action _gmInitializedHandler;

        // Per-cause warn-once for the RepairArmorCards reflection chain. Reflect.GetMember
        // returns null (no log) on a member-not-found — a game-update rename would otherwise
        // fail the whole armor-repair feature with zero log output. Keyed per cause, not a
        // single shared bool, so the first rename to trip doesn't silence a later, different one.
        private static readonly System.Collections.Generic.HashSet<string> _warnedArmorRepairCauses =
            new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

        private static void WarnArmorRepairOnce(string cause, string message)
        {
            if (!_warnedArmorRepairCauses.Add(cause)) return;
            Logger?.LogError($"[ACT-Fix] {message}");
        }

        private static void PatchEncounterArmorRepair(Harmony harmony)
        {
            try
            {
                var encounterPopupType = AccessTools.TypeByName("EncounterPopup");
                var method = AccessTools.Method(encounterPopupType, "GenerateAndApplyPlayerWound");
                if (method == null)
                {
                    Logger.LogError("[ACT] EncounterPopup.GenerateAndApplyPlayerWound not found; armor combat repair not applied.");
                    return;
                }

                harmony.Patch(method, prefix: new HarmonyMethod(typeof(GameLoadPatch), nameof(EncounterArmorRepair_Prefix)));
            }
            catch (Exception ex)
            {
                Logger.LogError($"[ACT] Failed to patch armor combat repair: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void EncounterArmorRepair_Prefix(object __instance)
        {
            try
            {
                var gameManager = Reflect.GetMember(__instance, "GM") ?? CardUtil.GetGameManagerInstance();
                var graphicsManager = Reflect.GetMember(__instance, "GraphicsManager");
                var characterWindow = Reflect.GetMember(graphicsManager, "CharacterWindow");
                RepairArmorCards(gameManager, characterWindow, "encounter");
            }
            catch (Exception ex)
            {
                if (_loggedEncounterArmorRepairError) return;
                Logger.LogError($"[ACT] Armor combat repair failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
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
                    Logger.LogError("[ACT] GameManager.OnGMInitialized not found; save-load armor repair unavailable.");
                    return;
                }

                _gmInitializedHandler = OnGameManagerInitialized;
                var current = (Action)field.GetValue(null);
                field.SetValue(null, (Action)Delegate.Combine(current, _gmInitializedHandler));
                _subscribedToGmInitialized = true;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[ACT] Failed to subscribe armor save-load repair: {ex.InnerException?.ToString() ?? ex.ToString()}");
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
                Logger?.LogError($"[ACT] Failed to remove armor save-load repair subscription: {ex.InnerException?.ToString() ?? ex.ToString()}");
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
                host.StartCoroutine(DeferredRepairArmorCards());
                return;
            }

            RepairArmorCards(CardUtil.GetGameManagerInstance(), null, "save-load");
        }

        private static IEnumerator DeferredRepairArmorCards()
        {
            yield return null;
            RepairArmorCards(CardUtil.GetGameManagerInstance(), null, "save-load");
        }

        private static int RepairArmorCards(object gameManager, object characterWindow, string phase)
        {
            if (gameManager == null)
            {
                WarnArmorRepairOnce("GM-null", "RepairArmorCards: gameManager was null; armor combat/save-load repair unavailable.");
                return 0;
            }

            var armorCards = Reflect.GetMember(gameManager, "ArmorCards") as System.Collections.IList;
            if (armorCards == null)
            {
                WarnArmorRepairOnce("ArmorCards-null", "RepairArmorCards: GameManager.ArmorCards not found (reflection miss) — modded armor combat/save-load repair disabled. Possible field rename after a game update.");
                return 0;
            }

            if (characterWindow == null)
            {
                var gameGraphics = Reflect.GetMember(gameManager, "GameGraphics") ?? Reflect.GetMember(gameManager, "GraphicsManager");
                characterWindow = Reflect.GetMember(gameGraphics, "CharacterWindow");
            }

            var modArmorCards = new System.Collections.Generic.List<object>();
            int added = AddArmorFromList(CardFinder.AllCards(), armorCards, modArmorCards);

            foreach (var equippedCard in FindEquippedArmorCards(characterWindow))
            {
                if (equippedCard == null) continue;
                if (!modArmorCards.Contains(equippedCard)) modArmorCards.Add(equippedCard);
                if (armorCards.Contains(equippedCard)) continue;

                armorCards.Add(equippedCard);
                added++;
            }

            if (modArmorCards.Count > 0 && string.Equals(phase, "save-load", StringComparison.OrdinalIgnoreCase))
                RefreshArmorPassiveEffects(modArmorCards);

            if (added > 0)
                Logger?.LogDebug($"[ACT-Fix] Armor combat list repaired ({added} card(s), {phase}).");

            return added;
        }

        private static int AddArmorFromList(IEnumerable cards, System.Collections.IList armorCards, System.Collections.Generic.List<object> modArmorCards)
        {
            if (cards == null) return 0;

            int added = 0;
            foreach (var card in cards)
            {
                if (card == null) continue;
                var uid = GetCardUid(card);
                if (!ArmorUidSet.Contains(uid)) continue;

                if (!modArmorCards.Contains(card)) modArmorCards.Add(card);
                if (armorCards.Contains(card)) continue;

                armorCards.Add(card);
                added++;
            }

            return added;
        }

        private static void RefreshArmorPassiveEffects(System.Collections.Generic.List<object> modArmorCards)
        {
            var host = Plugin.Instance;
            if (host == null) return;

            foreach (var card in modArmorCards)
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

        private static System.Collections.Generic.List<object> FindEquippedArmorCards(object characterWindow)
        {
            var equippedCards = new System.Collections.Generic.List<object>();

            // Every early return here is a per-cause warn-once. Without these the whole
            // equipped-armor half of the repair returns an empty list on a reflection miss,
            // which RepairArmorCards then reports as "changed 0" - indistinguishable from a
            // genuine "no armor equipped". A game-update rename would silently disable it.
            if (characterWindow == null)
            {
                WarnArmorRepairOnce("CharacterWindow-null",
                    "FindEquippedArmorCards: CharacterWindow was null; equipped-armor repair skipped this pass.");
                return equippedCards;
            }

            var equipmentLine = Reflect.GetMember(characterWindow, "EquipmentSlotsLine");
            if (equipmentLine == null)
            {
                WarnArmorRepairOnce("EquipmentSlotsLine-null",
                    "FindEquippedArmorCards: CharacterWindow.EquipmentSlotsLine not found (reflection miss) - equipped-armor repair disabled. Possible field rename after a game update.");
                return equippedCards;
            }

            var slots = Reflect.GetMember(equipmentLine, "Slots") as System.Collections.IEnumerable;
            if (slots == null)
            {
                WarnArmorRepairOnce("EquipmentSlots-null",
                    "FindEquippedArmorCards: EquipmentSlotsLine.Slots not found or not enumerable (reflection miss) - equipped-armor repair disabled. Possible field rename after a game update.");
                return equippedCards;
            }

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
