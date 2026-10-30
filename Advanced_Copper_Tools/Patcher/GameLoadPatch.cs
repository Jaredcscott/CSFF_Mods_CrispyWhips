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
                HammerGroupPatch.Populate(allData);
            }
            catch (Exception ex)
            {
                Logger.LogError($"[ACT] LoadMainGameData postfix error: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // Every nail slot in the game requires the one Metal Nail card (advanced_copper_tools_copper_nails,
        // the UID kept from when it was the Copper Nail; ACT 1.18.0 folded the Iron Nail into it as Metal
        // Type 200). A nail of any metal works in every recipe, so no nail slot reads the Metal Type. Each
        // nail slot also accepts the retired act_iron_nail, which an old save can still hold for the quarter
        // hour before it reforges itself into a Metal Nail. Delegates to the framework's shared helper
        // (CSFFModFramework.Api.BlueprintAlternates), which logs its own summary line.
        // WDI's rivets and Alloy Solder are no longer alternates: WaterDrivenInfrastructure 1.12.3 retired them
        // for the shared Metal Nail and Tin Solder (they reforge themselves into those within a tick), so every
        // WDI fastener slot names the shared cards and ACT has nothing of WDI's left to accept.
        private static void PatchNailInterchangeability(IEnumerable allData)
        {
            const string MetalNailUid = "advanced_copper_tools_copper_nails";
            const string RetiredIronNailUid = "act_iron_nail";

            CSFFModFramework.Api.BlueprintAlternates.AddAlternateIngredient(
                allData, MetalNailUid, RetiredIronNailUid, "ACT Metal Nail / retired Iron Nail");
        }

        // The blueprints whose sheet slot admits Metal Type 200: the Large Saw (any rung 100-200 since the saw merge,
        // fleet plan 5.6) and, since the armor merge, the five metal armor blueprints (any rung 100-200, the piece
        // takes the sheets' metal). The retired iron armor blueprints and the retired Iron Large Saw blueprint left this
        // list with their merges: they are out of every tab and only hand their research on to the merged blueprint
        // (AlsoUnlocks).
        private static readonly string[] IronSheetBlueprintUids = {
            "advanced_copper_tools_bp_large_saw",
            "advanced_copper_tools_bp_copper_helmet",
            "advanced_copper_tools_bp_copper_breastplate",
            "advanced_copper_tools_bp_copper_gauntlets",
            "advanced_copper_tools_bp_copper_greaves",
            "act_bp_copper_shield",
        };

        // Sheets stay tier-locked, unlike nails: every sheet slot requires the one Metal Sheet card and
        // its own Special4 (Metal Type) gate picks the metal. An alternate card passes that gate only by
        // its OWN Special4, and a card whose Special4 is inactive passes every gate
        // (.decomp/DurabilitiesConditions.cs skips an inactive stat), so the retired act_iron_sheet (Metal
        // Type 200, held by an old save for the quarter hour before it reforges itself) is allow-listed to
        // the iron recipes only (AddAlternateIngredientIn, framework 2.28.0).
        // WDI's Cast Copper Sheet and Cast Iron Sheet are no longer alternates: WaterDrivenInfrastructure 1.12.3
        // retired them for the shared Metal Sheet (they reforge themselves into it within a tick).
        private static void PatchSheetInterchangeability(IEnumerable allData)
        {
            const string MetalSheetUid = "advanced_copper_tools_metal_sheet";
            const string RetiredIronSheetUid = "act_iron_sheet";

            CSFFModFramework.Api.BlueprintAlternates.AddAlternateIngredientIn(
                allData, MetalSheetUid, RetiredIronSheetUid, IronSheetBlueprintUids, "ACT Metal Sheet (iron recipes) / retired Iron Sheet");
        }

        // Every ACT armor item, all three tiers. The save/reload + encounter repair below is not
        // copper-specific: any modded armor drops out of GameManager.ArmorCards the same way, so
        // Bronze and Iron (added after the original copper-only net) are covered too (audit 2026-09-01).
        private static readonly string[] ArmorUids = {
            "advanced_copper_tools_copper_helmet",
            "advanced_copper_tools_copper_breastplate",
            "advanced_copper_tools_copper_gauntlets",
            "advanced_copper_tools_copper_gloves",
            "advanced_copper_tools_copper_greaves",
            "act_bronze_helmet",
            "act_bronze_breastplate",
            "act_bronze_gauntlets",
            "act_bronze_greaves",
            "act_ghostbronze_helmet",
            "act_ghostbronze_breastplate",
            "act_ghostbronze_gauntlets",
            "act_ghostbronze_greaves",
            "act_tin_helmet",
            "act_tin_breastplate",
            "act_tin_gauntlets",
            "act_tin_greaves",
            "act_whitebronze_helmet",
            "act_whitebronze_breastplate",
            "act_whitebronze_gauntlets",
            "act_whitebronze_greaves",
            "act_iron_helmet",
            "act_iron_breastplate_f",
            "act_iron_breastplate_m",
            "act_iron_gauntlets",
            "act_iron_greaves",
            "act_copper_shield",
            "act_iron_shield",
            "act_ghostbronze_shield",
        };

        private static readonly System.Collections.Generic.HashSet<string> ArmorUidSet =
            new System.Collections.Generic.HashSet<string>(ArmorUids, StringComparer.OrdinalIgnoreCase);

        private static bool _subscribedToGmInitialized;
        private static Action _gmInitializedHandler;

        // Per-cause warn-once for the RepairArmorCards reflection chain. Reflect.GetMember
        // returns null (no log) on a member-not-found; a game-update rename would otherwise
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
                ArmorWardPatch.Apply(harmony, method);
            }
            catch (Exception ex)
            {
                Logger.LogError($"[ACT] Failed to patch armor combat repair: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void EncounterArmorRepair_Prefix(object __instance)
        {
            // Arms the merged armor's spiritual ward for this blow (ArmorWardPatch; it catches and warns itself).
            ArmorWardPatch.BeginWound(__instance);
            try
            {
                var gameManager = Reflect.GetMember(__instance, "GM") ?? CardUtil.GetGameManagerInstance();
                var graphicsManager = Reflect.GetMember(__instance, "GraphicsManager");
                var characterWindow = Reflect.GetMember(graphicsManager, "CharacterWindow");
                RepairArmorCards(gameManager, characterWindow, "encounter");
            }
            catch (Exception ex)
            {
                WarnArmorRepairOnce("encounter-prefix-threw", $"Armor combat repair failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void TrySubscribeToGameManagerInitialized()
        {
            if (_subscribedToGmInitialized) return;

            try
            {
                var gmType = CardUtil.FindGameType("GameManager");
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
                var gmType = CardUtil.FindGameType("GameManager");
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
                WarnArmorRepairOnce("ArmorCards-null", "RepairArmorCards: GameManager.ArmorCards not found (reflection miss) - modded armor combat/save-load repair disabled. Possible field rename after a game update.");
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

            if (modArmorCards.Count > 0)
                RaisePreLeatherParityUsage(modArmorCards, phase);

            if (modArmorCards.Count > 0 && string.Equals(phase, "save-load", StringComparison.OrdinalIgnoreCase))
                RefreshArmorPassiveEffects(modArmorCards);

            if (added > 0)
                Logger?.LogDebug($"[ACT-Fix] Armor combat list repaired ({added} card(s), {phase}).");

            return added;
        }

        // ACT 1.18.9 (ACT-B7): every helmet, armor, bracer and greave had Usage 90..140 while each
        // enemy hit on its slot costs 250 (EncounterPopup.ArmorDefaultDurabilityDamage) x
        // GameManager.CancelledAtTick (an int: the cost of the action the fight broke into; root
        // CLAUDE.md "Armor wear and enemy damage types"), so the first hit destroyed it. The JSON now
        // carries leather parity, 46080 (about 185 hits during a 1-point action, fewer during a longer
        // one; every hit is still a whole multiple of 250, which the argument below relies on). A piece saved before
        // the change keeps its old CURRENT Usage, so raise it once here. Idempotent across every
        // OnGMInitialized re-fire and every encounter: only a piece sitting EXACTLY on its own old
        // maximum is raised. A new piece worn by hits totalling k quarter-hour ticks (k = the sum of each
        // hit's CancelledAtTick) reads 46080 - 250k, which is 80 mod 250, and
        // none of 90, 100, 120, 140 is, so a raised piece can never be raised twice. Any old piece
        // that was hit even once was destroyed, so every surviving old piece is at its old maximum.
        // Shields are not in this table (their Usage was already in the thousands).
        // Since the armor merge an old piece of any metal reforges into the merged card (the copper UID) and
        // carries its Usage across (TransferUsage), so a pre-1.18.9 tin helmet can arrive on the merged UID at
        // 90, a tin bronze one at 120, an iron one at 140. The merged UIDs therefore accept every old maximum;
        // 46080 - 250k is still 80 mod 250, so none of 90, 100, 120, 140 can be a worn new piece.
        private const float LeatherParityUsage = 46080f;

        private static readonly System.Collections.Generic.Dictionary<string, float[]> PreParityArmorUsageMax =
            BuildPreParityArmorUsageMax();

        private static System.Collections.Generic.Dictionary<string, float[]> BuildPreParityArmorUsageMax()
        {
            var map = new System.Collections.Generic.Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
            string[] pieces = { "helmet", "breastplate", "gauntlets", "greaves" };
            foreach (var p in pieces)
            {
                map["act_tin_" + p] = new[] { 90f };
                map["advanced_copper_tools_copper_" + p] = new[] { 90f, 100f, 120f, 140f };
                map["act_whitebronze_" + p] = new[] { 100f };
                map["act_bronze_" + p] = new[] { 120f };
                map["act_ghostbronze_" + p] = new[] { 120f };
            }
            map["act_iron_helmet"] = new[] { 140f };
            map["act_iron_breastplate_f"] = new[] { 140f };
            map["act_iron_breastplate_m"] = new[] { 140f };
            map["act_iron_gauntlets"] = new[] { 140f };
            map["act_iron_greaves"] = new[] { 140f };
            return map;
        }

        private static void RaisePreLeatherParityUsage(System.Collections.Generic.List<object> modArmorCards, string phase)
        {
            int raised = 0;
            foreach (var card in modArmorCards)
            {
                var uid = GetCardUid(card);
                if (uid == null || !PreParityArmorUsageMax.TryGetValue(uid, out float[] oldMaxes)) continue;

                float current = CardUtil.GetDurability(card, "UsageDurability");
                if (float.IsNaN(current))
                {
                    WarnArmorRepairOnce("ArmorUsage-NaN", "RaisePreLeatherParityUsage: UsageDurability not readable (reflection miss) - pre-1.18.9 armor keeps its old one-hit durability. Possible field rename after a game update.");
                    continue;
                }
                bool atOldMax = false;
                foreach (var oldMax in oldMaxes)
                    if (Math.Abs(current - oldMax) <= 0.01f) { atOldMax = true; break; }
                if (!atOldMax) continue;

                if (CardUtil.SetDurability(card, "UsageDurability", LeatherParityUsage)) raised++;
                else WarnArmorRepairOnce("ArmorUsage-write", "RaisePreLeatherParityUsage: UsageDurability write failed - pre-1.18.9 armor keeps its old one-hit durability.");
            }

            if (raised > 0)
                Logger?.LogInfo($"[ACT-Fix] Raised {raised} pre-1.18.9 armor piece(s) to {LeatherParityUsage} durability ({phase}).");
        }

        private static int AddArmorFromList(IEnumerable cards, System.Collections.IList armorCards, System.Collections.Generic.List<object> modArmorCards)
        {
            if (cards == null)
            {
                WarnArmorRepairOnce("AllCards-null", "AddArmorFromList: CardFinder.AllCards() returned null; board armor repair skipped (only equipped armor is repaired).");
                return 0;
            }

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
            if (host == null)
            {
                WarnArmorRepairOnce("RefreshHost-null", "RefreshArmorPassiveEffects: Plugin.Instance was null; armor passive effects not refreshed on save-load.");
                return;
            }

            foreach (var card in modArmorCards)
            {
                try
                {
                    var update = AccessTools.Method(card.GetType(), "UpdatePassiveEffects");
                    if (update == null)
                    {
                        WarnArmorRepairOnce("UpdatePassiveEffects-null", "RefreshArmorPassiveEffects: UpdatePassiveEffects not found (reflection miss) - armor passive effects not refreshed on save-load. Possible method rename after a game update.");
                        continue;
                    }
                    var routine = update.Invoke(card, null) as IEnumerator;
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
            // A null card or a null CardModel is routine (an empty equipment slot, a pooled card),
            // and so is a null UniqueID VALUE: the game builds a wandering NPC's board card (Agent_Hare
            // and the like) at runtime with no UniqueID, and that card is not armor. Only a model type
            // with no UniqueID MEMBER is reported: that is a rename, and it would make every armor card
            // silently miss ArmorUidSet. (Until 1.18.10 any null value logged this as an Error: a save
            // with a hare on the board did so on every load.)
            var model = Reflect.GetMember(cardObject, "CardModel");
            if (model == null) return null;
            var uid = Reflect.GetMember(model, "UniqueID") as string;
            if (uid == null && !HasUniqueIdMember(model.GetType()))
                WarnArmorRepairOnce("UniqueID-null", "GetCardUid: CardModel has no UniqueID member (reflection miss) - armor cards cannot be identified, repair disabled. Possible field rename after a game update.");
            return uid;
        }

        private static readonly System.Collections.Generic.Dictionary<Type, bool> _hasUniqueIdMember =
            new System.Collections.Generic.Dictionary<Type, bool>();

        private static bool HasUniqueIdMember(Type modelType)
        {
            if (_hasUniqueIdMember.TryGetValue(modelType, out bool has)) return has;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
            has = false;
            for (var t = modelType; t != null && !has; t = t.BaseType)
                has = t.GetField("UniqueID", flags) != null || t.GetProperty("UniqueID", flags) != null;
            _hasUniqueIdMember[modelType] = has;
            return has;
        }
    }
}
