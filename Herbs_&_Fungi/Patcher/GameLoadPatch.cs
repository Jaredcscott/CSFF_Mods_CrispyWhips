using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using BepInEx.Logging;
using UnityEngine;
using CSFFModFramework.Api;
using Herbs_And_Fungi;

namespace Herbs_And_Fungi.Patcher
{
    /// <summary>
    /// Main orchestrator for game load patches.
    /// Coordinates all runtime data repairs and feature injections via specialized injector classes.
    /// </summary>
    public static class GameLoadPatch
    {
        private static ManualLogSource Logger => Plugin.Logger;
        private static float DensityScale => Plugin.ForageDropDensityScale?.Value ?? 1.0f;

        // Allowed-season set for a forage drop. Flags, so a drop can permit any subset of seasons.
        [Flags]
        private enum HfSeason
        {
            None = 0,
            Spring = 1,
            Summer = 2,
            Autumn = 4,
            Winter = 8,
            SpringSummerFall = Spring | Summer | Autumn,
            All = Spring | Summer | Autumn | Winter,
        }

        // The four vanilla SeasonCounter GameStats (each is a Season.DayCounterStat). Resolved once
        // per load from AllData in AddMushroomDropsToForaging; consumed by ApplySeasonSuppression.
        // Assumption (verified in-game via /playthrough-test-plan, not offline): SeasonCounter_X reads
        // >= 1 only while season X is active and drops out of the [1, N] range otherwise — the same
        // mutual-exclusivity the game's own SeasonsSettings.GetCurrentSeason()/Season.IsActive rely on.
        private const string SeasonSpringGuid = "6f3f0faaf50a7974e915ae0915f7e241";
        private const string SeasonSummerGuid = "4a9284146ffc28242b1f591ac7f7541f";
        private const string SeasonAutumnGuid = "f6f436140ffd58644a6438c0838e05a1";
        private const string SeasonWinterGuid = "20042dca062d34a4b8e2968f9dc11f9f";
        private static object _seasonStatSpring, _seasonStatSummer, _seasonStatAutumn, _seasonStatWinter;

        /// <summary>
        /// Registers all Harmony patches for game load and UI initialization.
        /// </summary>
        public static void ApplyPatch(Harmony harmony)
        {
            try
            {
                var gameLoadType = AccessTools.TypeByName("GameLoad");
                if (gameLoadType == null)
                {
                    Logger.LogError("GameLoad type not found; Herbs and Fungi load patches were not applied.");
                    return;
                }

                var loadMainGameDataMethod = AccessTools.Method(gameLoadType, "LoadMainGameData");
                if (loadMainGameDataMethod == null)
                {
                    Logger.LogError("GameLoad.LoadMainGameData not found; Herbs and Fungi load patches were not applied.");
                    return;
                }

                var postfixMethod = AccessTools.Method(typeof(GameLoadPatch), nameof(LoadMainGameData_Postfix));
                if (postfixMethod == null)
                {
                    Logger.LogError("GameLoadPatch.LoadMainGameData_Postfix not found; Herbs and Fungi load patches were not applied.");
                    return;
                }

                harmony.Patch(loadMainGameDataMethod, postfix: new HarmonyMethod(postfixMethod));

                // Blueprint tab injection is now handled by CSFFModFramework's BlueprintInjector
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to patch GameLoad.LoadMainGameData: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        /// <summary>
        /// Main postfix called after GameLoad.LoadMainGameData completes.
        /// Orchestrates all data repairs and feature injections in proper sequence.
        /// </summary>
        static void LoadMainGameData_Postfix(object __instance)
        {
            try
            {
                // Access DataBase.AllData
                var gameLoadType = __instance.GetType();
                var dataBaseField = AccessTools.Field(gameLoadType, "DataBase");
                var dataBase = dataBaseField?.GetValue(__instance);

                if (dataBase == null)
                {
                    Logger.LogError("Could not access GameLoad.DataBase!");
                    return;
                }

                var dataBaseType = dataBase.GetType();
                var allDataField = AccessTools.Field(dataBaseType, "AllData");
                var allData = allDataField?.GetValue(dataBase);

                if (allData == null)
                {
                    Logger.LogError("Could not access DataBase.AllData!");
                    return;
                }

                var allDataEnumerable = allData as IEnumerable;
                if (allDataEnumerable == null)
                {
                    Logger.LogError("AllData is not enumerable!");
                    return;
                }

                // === Feature Injection ===
                // (WarpData resolution, PassiveEffect normalization, sprite resolution,
                //  DroppedCards population, perk injection, and blueprint tab injection
                //  are now handled by CSFFModFramework)

                // Add hemp seed planting support to tilled fields and garden plots
                AddHempSeedPlantingSupport(allDataEnumerable);

                // Add mushroom drops to vanilla foraging actions (following ColdWinds' technique)
                AddMushroomDropsToForaging(allDataEnumerable);

                // Cache CardTag scan once for both fermentable-tag and pouch-patch methods
                var cardTagType = AccessTools.TypeByName("CardTag");
                var allCardTags = cardTagType != null && typeof(UnityEngine.Object).IsAssignableFrom(cardTagType)
                    ? Resources.FindObjectsOfTypeAll(cardTagType)
                    : null;

                // Extend vanilla Turnroot/Fireroot with tag_Fermentable so they work in the pickle vat
                AddFermentableTagToVanillaRoots(allDataEnumerable, allCardTags);

                // Accelerate Tendon drying on the vanilla DryingRack and ACT Tea Blending Station
                AddTendonDryingRecipe(allDataEnumerable);

                // Allow the vanilla Pouch to store herb/mushroom powders
                PatchVanillaPouchForPowderStorage(allDataEnumerable, allCardTags);

                // Populate the four pickleable GpTags (auto-created by framework) with raw-only ingredient lists
                GpTagContentPatch.Populate();

                // Blueprint tab injection is now handled by CSFFModFramework's BlueprintInjector
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error in LoadMainGameData postfix: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        /// <summary>
        /// Adds hemp seed planting support to garden plots and tilled fields.
        /// </summary>
        static void AddHempSeedPlantingSupport(IEnumerable allDataEnumerable)
        {
            try
            {
                // Find the hemp seeds and hemp plant objects
                object hempSeeds = null;
                object hempPlantGrowing = null;

                foreach (var item in allDataEnumerable)
                {
                    if (item == null) continue;

                    var uniqueIdField = CachedField(item.GetType(), "UniqueID");
                    var uniqueId = uniqueIdField?.GetValue(item) as string;

                    if (uniqueId == "herbs_fungi_hemp_seeds") hempSeeds = item;
                    else if (uniqueId == "herbs_fungi_hemp_plant_growing") hempPlantGrowing = item;
                }

                if (hempSeeds == null || hempPlantGrowing == null)
                    return;

                // Find garden plot and tilled field locations and add hemp seed placement
                foreach (var item in allDataEnumerable)
                {
                    if (item == null) continue;
                    var cardNameField = CachedField(item.GetType(), "CardName");
                    var cardNameObj = cardNameField?.GetValue(item);
                    if (cardNameObj == null) continue;

                    var localizationKey = GetMember(cardNameObj, "LocalizationKey") as string;

                    if (localizationKey == null || (!localizationKey.Contains("GardenPlot") && !localizationKey.Contains("TilledField")))
                        continue;

                    // Add hemp to the plantation card
                    var plantationCardsField = CachedField(item.GetType(), "PlantationCards");
                    if (plantationCardsField == null) continue;

                    var plantationCards = plantationCardsField.GetValue(item);
                    if (ContainsPlantationCard(plantationCards, hempSeeds, hempPlantGrowing)) continue;

                    if (!TryAddPlantationCard(item, plantationCardsField, plantationCards, hempSeeds))
                    {
                        Logger?.LogWarning($"[HempSeeds] Could not append hemp seed to {localizationKey} PlantationCards ({plantationCardsField.FieldType.Name})");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[HempSeeds] Error adding hemp plantation support: {ex}");
            }
        }

        private static bool ContainsPlantationCard(object plantationCards, object hempSeeds, object hempPlantGrowing)
        {
            if (plantationCards is not IEnumerable cards) return false;

            foreach (var card in cards)
            {
                if (card == hempSeeds || card == hempPlantGrowing) return true;
            }

            return false;
        }

        private static bool TryAddPlantationCard(object item, FieldInfo plantationCardsField, object plantationCards, object hempSeeds)
        {
            try
            {
                if (plantationCards is Array array)
                {
                    var elementType = plantationCardsField.FieldType.GetElementType() ?? hempSeeds.GetType();
                    var expandedArray = Array.CreateInstance(elementType, array.Length + 1);
                    Array.Copy(array, expandedArray, array.Length);
                    expandedArray.SetValue(hempSeeds, array.Length);
                    plantationCardsField.SetValue(item, expandedArray);
                    return true;
                }

                if (plantationCards is IList list && !list.IsFixedSize && !list.IsReadOnly)
                {
                    list.Add(hempSeeds);
                    return true;
                }

                if (plantationCards == null)
                {
                    return TryCreatePlantationCards(item, plantationCardsField, hempSeeds);
                }
            }
            catch (Exception ex)
            {
                Logger?.LogWarning($"[HempSeeds] PlantationCards append failed: {ex}");
            }

            return false;
        }

        private static bool TryCreatePlantationCards(object item, FieldInfo plantationCardsField, object hempSeeds)
        {
            var fieldType = plantationCardsField.FieldType;

            if (fieldType.IsArray)
            {
                var elementType = fieldType.GetElementType() ?? hempSeeds.GetType();
                var array = Array.CreateInstance(elementType, 1);
                array.SetValue(hempSeeds, 0);
                plantationCardsField.SetValue(item, array);
                return true;
            }

            if (typeof(IList).IsAssignableFrom(fieldType) && !fieldType.IsAbstract && !fieldType.IsInterface)
            {
                var list = Activator.CreateInstance(fieldType) as IList;
                if (list != null)
                {
                    list.Add(hempSeeds);
                    plantationCardsField.SetValue(item, list);
                    return true;
                }
            }

            if (fieldType.IsGenericType)
            {
                var genericArguments = fieldType.GetGenericArguments();
                if (genericArguments.Length == 1)
                {
                    var listType = typeof(List<>).MakeGenericType(genericArguments[0]);
                    if (fieldType.IsAssignableFrom(listType))
                    {
                        var list = Activator.CreateInstance(listType) as IList;
                        list?.Add(hempSeeds);
                        plantationCardsField.SetValue(item, list);
                        return list != null;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Adds mushroom drops to vanilla foraging locations based on biome type.
        /// Includes new EA 0.61 cave locations (CaveOldHollow, CaveShadyThicket, CaveStillHollow).
        /// </summary>
        public static void AddMushroomDropsToForaging(IEnumerable allDataEnumerable)
        {
            try
            {
                // Re-resolve the season stats fresh each load (SO instances can differ across reloads).
                _seasonStatSpring = _seasonStatSummer = _seasonStatAutumn = _seasonStatWinter = null;

                // Get our cards from the database - original mushrooms
                object morelMushroom = null;
                object kingOyster = null;
                object goldenOyster = null;
                object lionsManeMushroom = null;
                object hempSeeds = null;
                object hempPlantMature = null;

                // New mushrooms
                object chanterelle = null;
                object reishi = null;
                object puffball = null;
                object chickenOfWoods = null;
                object deathCap = null;
                object truffle = null;

                // New herbs
                object ginseng = null;
                object ginger = null;

                // Newest additions - Black Trumpet, Shiitake, Yarrow
                object blackTrumpet = null;
                object shiitake = null;
                object yarrow = null;

                // Berries
                object blackcurrant = null;
                object redcurrant = null;
                object lingonberry = null;
                object cloudberry = null;

                // Phase 1 decorative/medicinal plants
                object wildFlowers = null;
                object dandelion = null;
                object commonPlantain = null;
                object chamomile = null;

                // Peanuts
                object peanutPod = null;

                // Vanilla items
                object wolfsbaneFresh = null;

                foreach (var item in allDataEnumerable)
                {
                    if (item == null) continue;

                    var uniqueIdField = CachedField(item.GetType(), "UniqueID");
                    var uniqueId = uniqueIdField?.GetValue(item) as string;

                    // Original items
                    if (uniqueId == "herbs_fungi_morel_mushroom") morelMushroom = item;
                    else if (uniqueId == "herbs_fungi_king_oyster") kingOyster = item;
                    else if (uniqueId == "herbs_fungi_golden_oyster") goldenOyster = item;
                    else if (uniqueId == "herbs_fungi_lions_mane") lionsManeMushroom = item;
                    else if (uniqueId == "herbs_fungi_hemp_seeds") hempSeeds = item;
                    else if (uniqueId == "herbs_fungi_hemp_plant_mature") hempPlantMature = item;
                    // New fungi
                    else if (uniqueId == "herbs_fungi_chanterelle") chanterelle = item;
                    else if (uniqueId == "herbs_fungi_reishi") reishi = item;
                    else if (uniqueId == "herbs_fungi_puffball") puffball = item;
                    else if (uniqueId == "herbs_fungi_chicken_of_woods") chickenOfWoods = item;
                    else if (uniqueId == "herbs_fungi_death_cap") deathCap = item;
                    else if (uniqueId == "herbs_fungi_truffle") truffle = item;
                    // New herbs
                    else if (uniqueId == "herbs_fungi_ginseng") ginseng = item;
                    else if (uniqueId == "herbs_fungi_ginger") ginger = item;
                    // Newest additions
                    else if (uniqueId == "herbs_fungi_black_trumpet") blackTrumpet = item;
                    else if (uniqueId == "herbs_fungi_shiitake") shiitake = item;
                    else if (uniqueId == "herbs_fungi_yarrow") yarrow = item;
                    // Berries
                    else if (uniqueId == "herbs_fungi_blackcurrant") blackcurrant = item;
                    else if (uniqueId == "herbs_fungi_redcurrant") redcurrant = item;
                    else if (uniqueId == "herbs_fungi_lingonberry") lingonberry = item;
                    else if (uniqueId == "herbs_fungi_cloudberry") cloudberry = item;
                    // Phase 1 decorative/medicinal plants
                    else if (uniqueId == "herbs_fungi_wild_flowers") wildFlowers = item;
                    else if (uniqueId == "herbs_fungi_dandelion") dandelion = item;
                    else if (uniqueId == "herbs_fungi_common_plantain") commonPlantain = item;
                    else if (uniqueId == "herbs_fungi_chamomile") chamomile = item;
                    // Peanuts
                    else if (uniqueId == "herbs_fungi_peanut_pod") peanutPod = item;
                    // Vanilla items
                    else if (uniqueId == "ffbc6fdc5dc0eec43b100d0feb63b70d") wolfsbaneFresh = item;
                    // Season counter GameStats (for seasonal drop gating)
                    else if (uniqueId == SeasonSpringGuid) _seasonStatSpring = item;
                    else if (uniqueId == SeasonSummerGuid) _seasonStatSummer = item;
                    else if (uniqueId == SeasonAutumnGuid) _seasonStatAutumn = item;
                    else if (uniqueId == SeasonWinterGuid) _seasonStatWinter = item;
                }

                if (_seasonStatSpring == null || _seasonStatSummer == null || _seasonStatAutumn == null || _seasonStatWinter == null)
                    Logger?.LogWarning($"[Forage] Season counter stats not fully resolved (spring={_seasonStatSpring != null}, summer={_seasonStatSummer != null}, autumn={_seasonStatAutumn != null}, winter={_seasonStatWinter != null}); seasonal drops for any unresolved season will fall back to year-round.");

                int locationsModified = 0;
                int forageActionsModified = 0;
                int clearActionsModified = 0;

                // Find location cards and modify their forage/clear actions
                foreach (var item in allDataEnumerable)
                {
                    if (item == null) continue;

                    // Get CardName.LocalizationKey to identify location type
                    // UniqueIDs are GUIDs like "b71c4ef8847555b4abcab5730a529145", NOT readable names!
                    // LocalizationKey contains patterns like "GroveOak_MossyGrove_CardName" or "River_GroveOak_FloodedGrove_CardName"
                    var cardNameField = CachedField(item.GetType(), "CardName");
                    var cardNameObj = cardNameField?.GetValue(item);
                    if (cardNameObj == null) continue;

                    var localizationKey = GetMember(cardNameObj, "LocalizationKey") as string;

                    if (string.IsNullOrEmpty(localizationKey)) continue;

                    // Check if this is a location type we want to modify
                    // Pattern examples: "GroveOak_MossyGrove_CardName", "River_GroveOak_FloodedGrove_CardName"
                    bool isOakGrove = localizationKey.Contains("GroveOak") || localizationKey.Contains("ThicketOak") || localizationKey.Contains("ClearingOak");
                    bool isAlderWoods = localizationKey.Contains("GroveAlder") || localizationKey.Contains("ThicketAlder") || localizationKey.Contains("ClearingAlder");
                    bool isPineForest = localizationKey.Contains("GrovePine") || localizationKey.Contains("ThicketPine") || localizationKey.Contains("ClearingPine");
                    bool isBirchForest = localizationKey.Contains("Birch") || localizationKey.Contains("GroveBirch") || localizationKey.Contains("ThicketBirch");
                    bool isWillowArea = localizationKey.Contains("Willow") || localizationKey.Contains("GroveWillow") || localizationKey.Contains("ThicketWillow");
                    bool isRiverBank = localizationKey.StartsWith("River_");
                    bool isPrimevalWoods = localizationKey.Contains("PrimevalWoods");
                    // Northern region: areas above Grenfell Falls (NorthernLakeBank, NorthernRapids)
                    bool isNorthernRegion = localizationKey.Contains("Northern");
                    // "ClearingOak"/"ClearingAlder"/"ClearingPine" structurally contain "Clearing"
                    // as a substring, so a bare Contains("Clearing") double-stacks the full generic
                    // clearing drop set on top of the already-specific biome drop set for those three
                    // location types. Exclude them so each location only matches its most specific bucket.
                    bool isClearing = localizationKey.Contains("Clearing")
                        && !localizationKey.Contains("ClearingOak")
                        && !localizationKey.Contains("ClearingAlder")
                        && !localizationKey.Contains("ClearingPine");
                    bool isWildWoods = localizationKey.Contains("WildWoods");
                    bool isLostWoods = localizationKey.Contains("LostWoods");
                    bool isGreenGrove = localizationKey.Contains("GreenGrove");
                    bool isGreenGlade = localizationKey.Contains("GreenGlade");
                    bool isOakenGrove = localizationKey.Contains("OakenGrove");
                    bool isPineMeadow = localizationKey.Contains("PineMeadow");
                    // Lake Island is a pine grove surrounded by lake water (key contains "LakeIsland")
                    bool isLakeIsland = localizationKey.Contains("LakeIsland");

                    // === EA 0.61 CAVE LOCATIONS ===
                    bool isCaveOldHollow = localizationKey.Contains("CaveOldHollow");
                    bool isCaveShadyThicket = localizationKey.Contains("CaveShadyThicket");
                    bool isCaveStillHollow = localizationKey.Contains("CaveStillHollow");
                    bool isUndergroundCave = isCaveOldHollow || isCaveShadyThicket || isCaveStillHollow;

                    // H&F's own WorldMap clone environments (HerbsAndFungi/WorldMap/MapNodes.json)
                    // get a fresh CardName.LocalizationKey ("HF_Env_<Name>_CardName", assigned by
                    // the framework's CardCloneService) that matches none of the vanilla biome
                    // patterns above, so their Forage actions were silently skipped and never
                    // received herb/mushroom drops. Map each clone to the biome bucket of the
                    // vanilla environment it clones from (verified via CloneOfEnvironmentUID):
                    // hfEnvForagingPath <- Env_GroveOak_SecretGrove, hfEnvOakClearing <-
                    // Env_ClearingOak_MossyClearing, hfEnvPineClearing <- Env_ClearingPine_PineClearing,
                    // hfEnvAlderWoods <- Env_GroveAlder_AlderGrove, hfEnvHighlandMeadow <-
                    // Env_ClearingPine_PineMeadows (treated as PineMeadow+Northern+Clearing+Pine).
                    var envUniqueIdField = CachedField(item.GetType(), "UniqueID");
                    var envUniqueId = envUniqueIdField?.GetValue(item) as string;
                    if (envUniqueId == "hfEnvForagingPath") isOakGrove = true;
                    else if (envUniqueId == "hfEnvOakClearing") { isOakGrove = true; isClearing = true; }
                    else if (envUniqueId == "hfEnvAlderWoods") isAlderWoods = true;
                    else if (envUniqueId == "hfEnvPineClearing") { isPineForest = true; isClearing = true; }
                    else if (envUniqueId == "hfEnvHighlandMeadow") { isPineMeadow = true; isClearing = true; isNorthernRegion = true; isPineForest = true; }
                    else if (envUniqueId == "hfEnvMistyFalls") { isOakGrove = true; isRiverBank = true; }
                    // Lake Island is water-surrounded — gets river-bank drops in addition to pine-forest drops
                    if (isLakeIsland) isRiverBank = true;

                    if (!isOakGrove && !isAlderWoods && !isPineForest && !isBirchForest && !isWillowArea && !isRiverBank && !isPrimevalWoods && !isClearing && !isWildWoods && !isNorthernRegion && !isPineMeadow && !isUndergroundCave && !isLostWoods && !isGreenGrove && !isGreenGlade && !isOakenGrove) continue;

                    // Determine location type for mushroom drop logic
                    string locationType = isNorthernRegion ? "Northern" : (isPrimevalWoods ? "Primeval" : (isWillowArea ? "Willow" : (isWildWoods ? "WildWoods" : (isPineMeadow ? "PineMeadow" : (isOakGrove ? "Oak" : (isAlderWoods ? "Alder" : (isBirchForest ? "Birch" : (isPineForest ? "Pine" : (isUndergroundCave ? "Cave" : "River")))))))));

                    // Get DismantleActions array
                    var dismantleActionsField = CachedField(item.GetType(), "DismantleActions");
                    var dismantleActions = dismantleActionsField?.GetValue(item) as IList;

                    if (dismantleActions == null || dismantleActions.Count == 0) continue;

                    locationsModified++;

                    foreach (var action in dismantleActions)
                    {
                        if (action == null) continue;

                        // Get action name
                        var actionNameObj = GetMember(action, "ActionName");
                        if (actionNameObj == null) continue;

                        var actionName = GetMember(actionNameObj, "DefaultText") as string;

                        if (actionName == null) continue;

                        bool isForageAction = actionName.Contains("Forage");
                        bool isClearAction = actionName.Contains("Clear");
                        // Truffles only appear when digging up soil (mud/dirt), not every dig action
                        bool isDigMudOrDirt = actionName == "Dig up Mud" || actionName == "Dig up Dirt";

                        if (!isForageAction && !isClearAction && !isDigMudOrDirt) continue;

                        // Get ProducedCards array
                        var producedCardsField = CachedField(action.GetType(), "ProducedCards");
                        var producedCards = producedCardsField?.GetValue(action) as IList;

                        if (producedCards == null) continue;

                        // Add mushroom drops based on location type and action
                        if (isForageAction)
                        {
                            // === ORIGINAL MUSHROOMS ===

                            // Morels in oak/alder forests and river banks (8% chance)
                            if ((isOakGrove || isAlderWoods || isRiverBank) && morelMushroom != null)
                            {
                                AddMushroomDropToAction(producedCards, morelMushroom, 8.0f);
                            }

                            // Lion's Mane in oak/alder forests (8% chance) - Spring/Summer/Fall only
                            if ((isOakGrove || isAlderWoods) && lionsManeMushroom != null)
                            {
                                AddMushroomDropToAction(producedCards, lionsManeMushroom, 8.0f, HfSeason.SpringSummerFall);
                            }

                            // === CAVES: Lion's Mane and Black Trumpet ===
                            // Lion's Mane in caves (8% chance) - Spring/Summer/Fall only
                            if (isUndergroundCave && lionsManeMushroom != null)
                            {
                                AddMushroomDropToAction(producedCards, lionsManeMushroom, 8.0f, HfSeason.SpringSummerFall);
                            }

                            // Black Trumpet in caves (6% chance) - Spring/Summer/Fall only
                            if (isUndergroundCave && blackTrumpet != null)
                            {
                                AddMushroomDropToAction(producedCards, blackTrumpet, 6.0f, HfSeason.SpringSummerFall);
                            }

                            // Oyster mushrooms in oak/alder AND pine forests (King 8%, Golden 14%)
                            if (isOakGrove || isAlderWoods || isPineForest)
                            {
                                if (kingOyster != null)
                                {
                                    AddMushroomDropToAction(producedCards, kingOyster, 8.0f);
                                }
                                if (goldenOyster != null)
                                {
                                    AddMushroomDropToAction(producedCards, goldenOyster, 14.0f);
                                }
                            }

                            // === NEW MUSHROOMS ===

                            // Chanterelle in oak/alder AND birch forests (8% chance) - Spring/Summer/Fall only
                            if ((isOakGrove || isAlderWoods || isBirchForest) && chanterelle != null)
                            {
                                AddMushroomDropToAction(producedCards, chanterelle, 8.0f, HfSeason.SpringSummerFall);
                            }

                            // Reishi in oak/alder AND pine forests (4% chance - medicinal) - Spring/Summer/Fall only
                            if ((isOakGrove || isAlderWoods || isPineForest) && reishi != null)
                            {
                                AddMushroomDropToAction(producedCards, reishi, 4.0f, HfSeason.SpringSummerFall);
                            }

                            // Puffball in clearings (10% chance - large food source) - Spring/Summer/Fall only
                            if (isClearing && puffball != null)
                            {
                                AddMushroomDropToAction(producedCards, puffball, 10.0f, HfSeason.SpringSummerFall);
                            }

                            // Chicken of the Woods near Willow trees (12%) and Oak/Alder groves (6%) - Spring/Summer/Fall only
                            if (isWillowArea && chickenOfWoods != null)
                            {
                                AddMushroomDropToAction(producedCards, chickenOfWoods, 12.0f, HfSeason.SpringSummerFall);
                            }
                            else if ((isOakGrove || isAlderWoods) && chickenOfWoods != null)
                            {
                                AddMushroomDropToAction(producedCards, chickenOfWoods, 6.0f, HfSeason.SpringSummerFall);
                            }

                            // Death Cap in northern region above Grenfell Falls only (1% chance) - Spring/Summer/Fall only
                            if (isNorthernRegion && deathCap != null)
                            {
                                AddMushroomDropToAction(producedCards, deathCap, 1.0f, HfSeason.SpringSummerFall);
                            }

                            // === NEW HERBS ===

                            // Ginseng in Primeval Woods, Lost Woods, Green Grove, Green Glade, Oaken Grove (5% chance) - Spring/Summer/Fall only
                            if ((isPrimevalWoods || isLostWoods || isGreenGrove || isGreenGlade || isOakenGrove) && ginseng != null)
                            {
                                AddMushroomDropToAction(producedCards, ginseng, 5.0f, HfSeason.SpringSummerFall);
                            }

                            // Yarrow in pine meadows specifically (18% chance - 3x normal) - Spring/Summer/Fall only
                            if (isPineMeadow && yarrow != null)
                            {
                                AddMushroomDropToAction(producedCards, yarrow, 18.0f, HfSeason.SpringSummerFall);
                            }
                            // Yarrow in other clearings (6% chance) - Spring/Summer/Fall only
                            else if (isClearing && !isPineMeadow && yarrow != null)
                            {
                                AddMushroomDropToAction(producedCards, yarrow, 6.0f, HfSeason.SpringSummerFall);
                            }

                            // Wolfsbane at river banks/waterfalls (8% chance, poisonous — dangerous find) - Spring/Summer/Fall only
                            if (isRiverBank && wolfsbaneFresh != null)
                            {
                                AddMushroomDropToAction(producedCards, wolfsbaneFresh, 8.0f, HfSeason.SpringSummerFall);
                            }

                            // Wild Ginger in river banks and willow areas (6% chance) - Spring/Summer/Fall only
                            if ((isRiverBank || isWillowArea) && ginger != null)
                            {
                                AddMushroomDropToAction(producedCards, ginger, 6.0f, HfSeason.SpringSummerFall);
                            }

                            // === NEWEST MUSHROOMS ===

                            // Black Trumpet in oak/alder groves (6% chance - gourmet) - Spring/Summer/Fall only
                            if ((isOakGrove || isAlderWoods) && blackTrumpet != null)
                            {
                                AddMushroomDropToAction(producedCards, blackTrumpet, 6.0f, HfSeason.SpringSummerFall);
                            }

                            // Shiitake in oak/alder forests and dead wood areas (8% chance - immune boost) - Spring/Summer/Fall only
                            if ((isOakGrove || isAlderWoods || isPrimevalWoods) && shiitake != null)
                            {
                                AddMushroomDropToAction(producedCards, shiitake, 8.0f, HfSeason.SpringSummerFall);
                            }

                            // === HEMP ===

                            // Hemp seeds in Primeval Woods and Lake Island only (10% chance) - Spring/Summer/Fall only
                            if ((isPrimevalWoods || isLakeIsland) && hempSeeds != null)
                            {
                                AddMushroomDropToAction(producedCards, hempSeeds, 10.0f, HfSeason.SpringSummerFall);
                            }

                            // Hemp plant in Primeval Woods and Lake Island only (15% chance - rare find!) - Spring/Summer/Fall only
                            if ((isPrimevalWoods || isLakeIsland) && hempPlantMature != null)
                            {
                                AddMushroomDropToAction(producedCards, hempPlantMature, 15.0f, HfSeason.SpringSummerFall);
                            }

                            // === BERRIES ===

                            // Blackcurrant: birch (12%), river banks (8%), oak/alder (5%) - Summer only
                            if (isBirchForest && blackcurrant != null)
                                AddMushroomDropToAction(producedCards, blackcurrant, 12.0f, HfSeason.Summer);
                            if (isRiverBank && blackcurrant != null)
                                AddMushroomDropToAction(producedCards, blackcurrant, 8.0f, HfSeason.Summer);
                            if ((isOakGrove || isAlderWoods) && blackcurrant != null)
                                AddMushroomDropToAction(producedCards, blackcurrant, 5.0f, HfSeason.Summer);

                            // Redcurrant: oak/alder (12%), birch (8%), clearings (6%) - Summer only
                            if ((isOakGrove || isAlderWoods) && redcurrant != null)
                                AddMushroomDropToAction(producedCards, redcurrant, 12.0f, HfSeason.Summer);
                            if (isBirchForest && redcurrant != null)
                                AddMushroomDropToAction(producedCards, redcurrant, 8.0f, HfSeason.Summer);
                            if (isClearing && !isPineMeadow && redcurrant != null)
                                AddMushroomDropToAction(producedCards, redcurrant, 6.0f, HfSeason.Summer);

                            // Lingonberry: pine meadow (18%), pine forest (16%), northern (12%) - Late Summer/Fall
                            if (isPineMeadow && lingonberry != null)
                                AddMushroomDropToAction(producedCards, lingonberry, 18.0f, HfSeason.Summer | HfSeason.Autumn);
                            else if (isPineForest && lingonberry != null)
                                AddMushroomDropToAction(producedCards, lingonberry, 16.0f, HfSeason.Summer | HfSeason.Autumn);
                            if (isNorthernRegion && lingonberry != null)
                                AddMushroomDropToAction(producedCards, lingonberry, 12.0f, HfSeason.Summer | HfSeason.Autumn);

                            // Cloudberry: northern (10%), pine meadow (6%) - rare northern delicacy
                            if (isNorthernRegion && cloudberry != null)
                                AddMushroomDropToAction(producedCards, cloudberry, 10.0f, HfSeason.SpringSummerFall);
                            if (isPineMeadow && cloudberry != null)
                                AddMushroomDropToAction(producedCards, cloudberry, 6.0f, HfSeason.SpringSummerFall);

                            // === PHASE 1 DECORATIVE/MEDICINAL PLANTS (boosted rates for low-benefit items) ===

                            // Wild Flowers: clearings (20%), pine meadow (16%), river banks (12%)
                            if (isClearing && wildFlowers != null)
                                AddMushroomDropToAction(producedCards, wildFlowers, 20.0f, HfSeason.SpringSummerFall);
                            if (isPineMeadow && wildFlowers != null)
                                AddMushroomDropToAction(producedCards, wildFlowers, 16.0f, HfSeason.SpringSummerFall);
                            if (isRiverBank && wildFlowers != null)
                                AddMushroomDropToAction(producedCards, wildFlowers, 12.0f, HfSeason.SpringSummerFall);

                            // Dandelion: clearings (20%), river banks (14%), oak/alder groves (12%)
                            if (isClearing && dandelion != null)
                                AddMushroomDropToAction(producedCards, dandelion, 20.0f, HfSeason.SpringSummerFall);
                            if (isRiverBank && dandelion != null)
                                AddMushroomDropToAction(producedCards, dandelion, 14.0f, HfSeason.SpringSummerFall);
                            if ((isOakGrove || isAlderWoods) && dandelion != null)
                                AddMushroomDropToAction(producedCards, dandelion, 12.0f, HfSeason.SpringSummerFall);

                            // Common Plantain: clearings (16%), river banks (14%), birch (10%)
                            if (isClearing && commonPlantain != null)
                                AddMushroomDropToAction(producedCards, commonPlantain, 16.0f, HfSeason.SpringSummerFall);
                            if (isRiverBank && commonPlantain != null)
                                AddMushroomDropToAction(producedCards, commonPlantain, 14.0f, HfSeason.SpringSummerFall);
                            if (isBirchForest && commonPlantain != null)
                                AddMushroomDropToAction(producedCards, commonPlantain, 10.0f, HfSeason.SpringSummerFall);

                            // Chamomile: pine meadow (18%), clearings (14%), birch (10%)
                            if (isPineMeadow && chamomile != null)
                                AddMushroomDropToAction(producedCards, chamomile, 18.0f, HfSeason.SpringSummerFall);
                            else if (isClearing && chamomile != null)
                                AddMushroomDropToAction(producedCards, chamomile, 14.0f, HfSeason.SpringSummerFall);
                            if (isBirchForest && chamomile != null)
                                AddMushroomDropToAction(producedCards, chamomile, 10.0f, HfSeason.SpringSummerFall);

                            // === PEANUTS ===
                            // Peanut pods: oak/alder (10%), clearings (8%), pine (6%) - Spring/Summer/Fall only
                            if ((isOakGrove || isAlderWoods) && peanutPod != null)
                                AddMushroomDropToAction(producedCards, peanutPod, 10.0f, HfSeason.SpringSummerFall);
                            else if (isClearing && peanutPod != null)
                                AddMushroomDropToAction(producedCards, peanutPod, 8.0f, HfSeason.SpringSummerFall);
                            else if (isPineForest && peanutPod != null)
                                AddMushroomDropToAction(producedCards, peanutPod, 6.0f, HfSeason.SpringSummerFall);

                            forageActionsModified++;
                        }

                        if (isClearAction)
                        {
                            // Hemp seeds from clearing in Primeval Woods (8% chance) - Spring/Summer/Fall only
                            if (isPrimevalWoods && hempSeeds != null)
                            {
                                AddMushroomDropToAction(producedCards, hempSeeds, 8.0f, HfSeason.SpringSummerFall);
                            }

                            clearActionsModified++;
                        }

                        if (isDigMudOrDirt)
                        {
                            // Truffle when digging up mud/dirt - RARE (1% chance) - Spring/Summer/Fall only
                            // Truffles are underground fungi found when disturbing soil
                            if (truffle != null)
                            {
                                AddMushroomDropToAction(producedCards, truffle, 1.0f, HfSeason.SpringSummerFall);
                            }

                            // Peanut pods when digging in oak/alder/pine/clearing areas (5%) - peanuts grow underground
                            if ((isOakGrove || isAlderWoods || isPineForest || isClearing) && peanutPod != null)
                            {
                                AddMushroomDropToAction(producedCards, peanutPod, 5.0f, HfSeason.SpringSummerFall);
                            }
                        }
                    }

                    // Oak/Alder locations get a dedicated "Dig for Truffles" action (higher find rate)
                    if ((isOakGrove || isAlderWoods) && truffle != null)
                    {
                        AddDigForTrufflesAction(item, dismantleActionsField, dismantleActions, truffle);
                    }
                }

                if (locationsModified > 0)
                    Logger?.Log(BepInEx.Logging.LogLevel.Debug, $"[Forage] Added mushroom drops to {locationsModified} locations ({forageActionsModified} forage, {clearActionsModified} clear)");
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[Forage] Error adding mushroom drops: {ex}");
            }
        }

        /// <summary>
        /// Adds a mushroom/herb drop to a forage action's ProducedCards list.
        /// </summary>
        static void AddMushroomDropToAction(IList producedCards, object mushroom, float dropChance, HfSeason allowedSeasons = HfSeason.All)
        {
            if (mushroom == null || producedCards == null) return;

            try
            {
                // Check if mushroom already in this action
                foreach (var collection in producedCards)
                {
                    var dropsField = CachedField(collection.GetType(), "DroppedCards");
                    if (dropsField == null) continue;

                    var drops = dropsField.GetValue(collection) as Array;
                    if (drops == null) continue;

                    foreach (var drop in drops)
                    {
                        var droppedCardField = CachedField(drop.GetType(), "DroppedCard");
                        if (droppedCardField?.GetValue(drop) == mushroom)
                            return; // Already present
                    }
                }

                // Add new drop to first collection
                if (producedCards.Count > 0)
                {
                    var collection = producedCards[0];
                    var dropsField = CachedField(collection.GetType(), "DroppedCards");
                    if (dropsField != null)
                    {
                        var dropsArray = dropsField.GetValue(collection) as Array;
                        if (dropsArray != null)
                        {
                            var dropType = dropsArray.GetType().GetElementType();
                            var newDropsArray = Array.CreateInstance(dropType, dropsArray.Length + 1);
                            Array.Copy(dropsArray, newDropsArray, dropsArray.Length);

                            var newDrop = Activator.CreateInstance(dropType);
                            var droppedCardField = CachedField(dropType, "DroppedCard");
                            if (droppedCardField != null)
                                droppedCardField.SetValue(newDrop, mushroom);

                            // Set Quantity to {1, 1} so the item actually drops
                            var quantityField = CachedField(dropType, "Quantity");
                            if (quantityField != null)
                                quantityField.SetValue(newDrop, new UnityEngine.Vector2Int(1, 1));

                            var dropChanceField = CachedField(dropType, "DropChance");
                            if (dropChanceField != null)
                            {
                                var dropChanceObj = Activator.CreateInstance(dropChanceField.FieldType);
                                var activeField = CachedField(dropChanceObj.GetType(), "Active");
                                var chanceField = CachedField(dropChanceObj.GetType(), "BaseDropChance");

                                if (activeField != null) activeField.SetValue(dropChanceObj, true);
                                if (chanceField != null) chanceField.SetValue(dropChanceObj, dropChance * DensityScale);

                                // Gate the drop to its allowed seasons (no-op for HfSeason.All).
                                ApplySeasonSuppression(dropChanceObj, allowedSeasons);

                                dropChanceField.SetValue(newDrop, dropChanceObj);
                            }

                            newDropsArray.SetValue(newDrop, dropsArray.Length);
                            dropsField.SetValue(collection, newDropsArray);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[Forage] Error adding mushroom drop: {ex}");
            }
        }

        /// <summary>
        /// Season-gates a drop: for every season the item is NOT allowed in, adds a StatsModifier keyed
        /// on that season's SeasonCounter GameStat that subtracts a large amount from the drop chance
        /// while that season is active (driving the effective chance to 0%). During an allowed season,
        /// each disallowed-season counter is out of its active range, so its modifier returns 0 and the
        /// base chance is unaffected. No-op for <see cref="HfSeason.All"/>. Applied to the mod's OWN
        /// injected DropChance only — never a vanilla drop — so it is not a hot-path patch.
        /// </summary>
        private static void ApplySeasonSuppression(object dropChanceObj, HfSeason allowedSeasons)
        {
            if (allowedSeasons == HfSeason.All || dropChanceObj == null) return;

            var disallowed = new List<object>(4);
            if ((allowedSeasons & HfSeason.Spring) == 0 && _seasonStatSpring != null) disallowed.Add(_seasonStatSpring);
            if ((allowedSeasons & HfSeason.Summer) == 0 && _seasonStatSummer != null) disallowed.Add(_seasonStatSummer);
            if ((allowedSeasons & HfSeason.Autumn) == 0 && _seasonStatAutumn != null) disallowed.Add(_seasonStatAutumn);
            if ((allowedSeasons & HfSeason.Winter) == 0 && _seasonStatWinter != null) disallowed.Add(_seasonStatWinter);
            if (disallowed.Count == 0) return; // no resolvable disallowed-season stats -> leave year-round

            var statsModifiersField = CachedField(dropChanceObj.GetType(), "StatsModifiers"); // StatInterpolatedValue[]
            var elemType = statsModifiersField?.FieldType.GetElementType();
            if (elemType == null) return;

            var arr = Array.CreateInstance(elemType, disallowed.Count);
            for (int i = 0; i < disallowed.Count; i++)
            {
                var sm = BuildSeasonSuppressor(elemType, disallowed[i]);
                if (sm == null) return; // build failed -> leave drop un-gated rather than half-gated
                arr.SetValue(sm, i);
            }
            statsModifiersField.SetValue(dropChanceObj, arr);
        }

        /// <summary>
        /// Builds one boxed StatInterpolatedValue that returns -1000 while the given SeasonCounter stat
        /// is in its active range ([1, 999999]) and 0 otherwise (WhenOutOfRange = Return0Value).
        /// </summary>
        private static object BuildSeasonSuppressor(Type statInterpType, object seasonStat)
        {
            try
            {
                var sm = Activator.CreateInstance(statInterpType);
                CachedField(statInterpType, "InputStat")?.SetValue(sm, seasonStat);
                CachedField(statInterpType, "UseStatPercentage")?.SetValue(sm, false);

                var valueField = CachedField(statInterpType, "Value"); // InterpolatedValue
                if (valueField == null) return null;

                var ivType = valueField.FieldType;
                var iv = Activator.CreateInstance(ivType);
                CachedField(ivType, "Active")?.SetValue(iv, true);
                CachedField(ivType, "InputValueRange")?.SetValue(iv, new Vector2(1f, 999999f));
                CachedField(ivType, "OutputValueRange")?.SetValue(iv, new Vector2(-1000f, -1000f));

                var woorField = CachedField(ivType, "WhenOutOfRange"); // InterpolatedValueOutOfRange
                if (woorField != null)
                    woorField.SetValue(iv, Enum.Parse(woorField.FieldType, "Return0Value"));

                valueField.SetValue(sm, iv);
                return sm;
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[Forage] Failed to build season suppressor: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Clones the oak location's Forage action into a new "Dig for Truffles" action that
        /// only drops truffles, then appends it to the location's DismantleActions.
        /// </summary>
        static void AddDigForTrufflesAction(object locationItem, FieldInfo dismantleActionsField, IList dismantleActions, object truffle)
        {
            try
            {
                // Already added? Search by ActionName.DefaultText.
                object template = null;
                foreach (var action in dismantleActions)
                {
                    if (action == null) continue;
                    var nameObj = GetMember(action, "ActionName");
                    var defText = GetMember(nameObj, "DefaultText") as string;
                    if (defText == "Dig for Truffles") return;
                    if (defText == "Forage" && template == null) template = action;
                }

                if (template == null) return;

                var actionType = template.GetType();
                var newAction = Activator.CreateInstance(actionType);

                // Shallow-copy every field from template; shared references are fine for
                // stat modifications, action tags, sounds, etc.
                foreach (var fi in actionType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    fi.SetValue(newAction, fi.GetValue(template));
                }

                // Replace ActionName with a fresh LocalizedString so we don't mutate the template.
                var templateNameObj = GetMember(template, "ActionName");
                if (templateNameObj != null)
                {
                    var nameType = templateNameObj.GetType();
                    var newName = Activator.CreateInstance(nameType);
                    SetMember(newName, "ParentObjectID", "");
                    SetMember(newName, "LocalizationKey", "Herbs_And_Fungi_Action_DigForTruffles");
                    SetMember(newName, "DefaultText", "Dig for Truffles");
                    SetMember(newAction, "ActionName", newName);
                }

                // Digging is harder than foraging; give it a longer daytime cost.
                var daytimeCostField = AccessTools.Field(actionType, "DaytimeCost");
                daytimeCostField?.SetValue(newAction, 3);

                // Build a fresh ProducedCards collection by cloning the template's first entry
                // (inherits CollectionUses, modifiers, messages, etc.) and emptying DroppedCards.
                var producedCardsField = AccessTools.Field(actionType, "ProducedCards");
                if (producedCardsField == null) return;

                var templateProduced = producedCardsField.GetValue(template) as IList;
                if (templateProduced == null || templateProduced.Count == 0) return;

                var templateCollection = templateProduced[0];
                if (templateCollection == null) return;

                var collectionType = templateCollection.GetType();
                var freshCollection = Activator.CreateInstance(collectionType);
                foreach (var fi in collectionType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    fi.SetValue(freshCollection, fi.GetValue(templateCollection));
                }

                // Rename and reset drops so only truffle appears.
                AccessTools.Field(collectionType, "CollectionName")?.SetValue(freshCollection, "Truffle");
                var droppedCardsField = AccessTools.Field(collectionType, "DroppedCards");
                if (droppedCardsField != null)
                {
                    var dcType = droppedCardsField.FieldType;
                    if (dcType.IsArray)
                        droppedCardsField.SetValue(freshCollection, Array.CreateInstance(dcType.GetElementType(), 0));
                    else
                        droppedCardsField.SetValue(freshCollection, Activator.CreateInstance(dcType));
                }

                var producedCardsType = producedCardsField.FieldType;
                object freshProducedCards;
                if (producedCardsType.IsArray)
                {
                    var arr = Array.CreateInstance(producedCardsType.GetElementType(), 1);
                    arr.SetValue(freshCollection, 0);
                    freshProducedCards = arr;
                }
                else
                {
                    var list = (IList)Activator.CreateInstance(producedCardsType);
                    list.Add(freshCollection);
                    freshProducedCards = list;
                }

                producedCardsField.SetValue(newAction, freshProducedCards);

                // Now add the truffle drop (25% chance — player chose to dig here)
                AddMushroomDropToAction(freshProducedCards as IList, truffle, 25.0f);

                // Append newAction to DismantleActions (handle both Array and List<T>)
                if (dismantleActionsField.FieldType.IsArray)
                {
                    var oldArr = dismantleActionsField.GetValue(locationItem) as Array;
                    var elemType = oldArr.GetType().GetElementType();
                    var newArr = Array.CreateInstance(elemType, oldArr.Length + 1);
                    Array.Copy(oldArr, newArr, oldArr.Length);
                    newArr.SetValue(newAction, oldArr.Length);
                    dismantleActionsField.SetValue(locationItem, newArr);
                }
                else
                {
                    dismantleActions.Add(newAction);
                }
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[Truffle] Error adding Dig for Truffles action: {ex}");
            }
        }

        /// <summary>
        /// Appends tag_Fermentable to vanilla TurnrootFresh and FirerootFresh so they can be placed
        /// into the H&F pickle vat alongside tagged mod items.
        /// </summary>
        static void AddFermentableTagToVanillaRoots(IEnumerable allDataEnumerable, UnityEngine.Object[] allCardTags)
        {
            try
            {
                const string TurnrootGuid = "ac174c399999d14489fb3788c8931e93";
                const string FirerootGuid = "5ad63f32ed767c64190a64d79841d023";

                if (allCardTags == null || allCardTags.Length == 0)
                {
                    Logger?.LogError("[FermentableTag] CardTag scan was empty.");
                    return;
                }

                UnityEngine.Object fermentableTag = null;
                foreach (var tag in allCardTags)
                {
                    if (tag == null) continue;
                    if (tag.name == "tag_Fermentable") { fermentableTag = tag; break; }
                }

                if (fermentableTag == null)
                {
                    Logger?.LogError("[FermentableTag] tag_Fermentable not found in Resources.");
                    return;
                }

                // Locate TurnrootFresh and FirerootFresh by UniqueID in AllData
                object turnroot = null;
                object fireroot = null;
                foreach (var item in allDataEnumerable)
                {
                    if (item == null) continue;
                    var uniqueIdField = CachedField(item.GetType(), "UniqueID");
                    var uniqueId = uniqueIdField?.GetValue(item) as string;
                    if (uniqueId == TurnrootGuid) turnroot = item;
                    else if (uniqueId == FirerootGuid) fireroot = item;
                    if (turnroot != null && fireroot != null) break;
                }

                AppendFermentableTag(turnroot, fermentableTag, "TurnrootFresh");
                AppendFermentableTag(fireroot, fermentableTag, "FirerootFresh");
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[FermentableTag] Error: {ex}");
            }
        }

        /// <summary>
        /// Injects a Tendon-specific CookingRecipe onto the vanilla DryingRack, H&F Drying Tray,
        /// and (if loaded) both ACT Tea Blending Station variants. Tendon's "Wetness" countdown lives on
        /// FuelCapacity (-1/dtp passive). The drying rack's built-in recipes only drive
        /// UsageChange, so without this injection the rack has no effect on Tendon.
        /// The injected recipe drives FuelChange: -2/dtp, tripling the dry rate on any
        /// drying surface (passive -1 + recipe -2 = -3/dtp → ~1 in-game day on the rack).
        /// </summary>
        static void AddTendonDryingRecipe(IEnumerable allDataEnumerable)
        {
            const string TendonGuid      = "6d9b61e72f4d5334d91da54dcd939a8a";
            const string DryingRackGuid  = "4e2b3e00c88f8d14cb52a614584a66d5";
            const string DryingTrayId    = "herbs_fungi_drying_tray";
            const string TeaStationId    = "advanced_copper_tools_tea_blending_station";
            const string TeaStationLitId = "advanced_copper_tools_tea_blending_station_lit";

            object tendon = null, dryingRack = null, dryingTray = null, teaStation = null, teaStationLit = null;

            foreach (var item in allDataEnumerable)
            {
                if (item == null) continue;
                var uid = CachedField(item.GetType(), "UniqueID")?.GetValue(item) as string;
                if      (uid == TendonGuid)       tendon       = item;
                else if (uid == DryingRackGuid)   dryingRack   = item;
                else if (uid == DryingTrayId)     dryingTray   = item;
                else if (uid == TeaStationId)     teaStation   = item;
                else if (uid == TeaStationLitId)  teaStationLit = item;
            }

            if (tendon    == null) { Logger?.LogError("[TendonDry] Tendon not found in AllData.");    return; }
            if (dryingRack == null){ Logger?.LogError("[TendonDry] DryingRack not found in AllData."); return; }

            InjectTendonDryingRecipe(dryingRack,    tendon, "DryingRack");
            if (dryingTray   != null) InjectTendonDryingRecipe(dryingTray,   tendon, "DryingTray");
            if (teaStation    != null) InjectTendonDryingRecipe(teaStation,    tendon, "TeaBlendingStation");
            if (teaStationLit != null) InjectTendonDryingRecipe(teaStationLit, tendon, "TeaBlendingStationLit");
        }

        static void InjectTendonDryingRecipe(object stationCard, object tendon, string label)
        {
            try
            {
                var spec = new RecipeSpec
                {
                    CompatibleCards = new[] { tendon },
                    CompatibleTags = Array.Empty<object>(),
                    ConditionsCard = 0, // no heat required
                    Duration = 1,
                    CookerModType = 0, // no change to the station itself
                    IngredientModType = 1, // modify tendon in place
                    UsageChange = Vector2.zero,
                    SpoilageChange = Vector2.zero,
                    FuelChange = new Vector2(-2f, -2f), // drains Wetness faster
                };

                if (RecipeInjector.InjectCookingRecipe(stationCard, spec, label))
                    Logger?.LogDebug($"[TendonDry] Injected Tendon drying recipe on {label}.");
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[TendonDry] {label}: {ex}");
            }
        }

        private static object GetMember(object target, string name) => Reflect.GetMember(target, name);

        private static void SetMember(object target, string name, object value) => Reflect.SetMember(target, name, value);

        // Cached (Type, fieldName) -> FieldInfo lookups for the load-time per-item loops below
        // (AddHempSeedPlantingSupport, AddMushroomDropsToForaging, AddFermentableTagToVanillaRoots,
        // AddTendonDryingRecipe, PatchVanillaPouchForPowderStorage), each of which walks the
        // ENTIRE card database once. Reflect.GetMember already caches by (Type, name) for
        // value-only reads, but callers here that need the raw FieldInfo itself (SetValue,
        // FieldType) can't use it — this mirrors the same Dictionary<(Type,string), FieldInfo>
        // pattern CLAUDE.md calls for instead of re-resolving the same field name per item.
        private static readonly Dictionary<(Type, string), FieldInfo> _fieldCache = new();

        private static FieldInfo CachedField(Type type, string name)
        {
            // Native GetField, not AccessTools.Field: this is probed against every allData type
            // (SpiceTag, QuestLog, NPCDuty, ...), and AccessTools logs a HarmonyX warning per
            // miss — 26 warnings per load for fields most types legitimately lack.
            var key = (type, name);
            if (!_fieldCache.TryGetValue(key, out var fi))
                _fieldCache[key] = fi = type.GetField(name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return fi;
        }

        /// <summary>
        /// Patches the vanilla Pouch (description: "ideal for preserving powders") to actually
        /// function as a powder container. Sets MaxWeightCapacity and adds tag_Powder /
        /// tag_PowderLiquid to its InventoryFilter so H&amp;F ground herbs fit inside.
        /// </summary>
        static void PatchVanillaPouchForPowderStorage(IEnumerable allDataEnumerable, UnityEngine.Object[] allCardTags)
        {
            const string PouchGuid = "80fb7f8100618414d9abb10dec0e31a5";
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            try
            {
                // Locate the vanilla Pouch CardData
                object pouch = null;
                foreach (var item in allDataEnumerable)
                {
                    if (item == null) continue;
                    var uid = CachedField(item.GetType(), "UniqueID")?.GetValue(item) as string;
                    if (uid == PouchGuid) { pouch = item; break; }
                }

                if (pouch == null)
                {
                    Logger?.LogWarning("[PouchPatch] Vanilla Pouch not found in AllData.");
                    return;
                }

                // Give it enough weight capacity for ~10 powder items (each weighs 10)
                var weightField = CachedField(pouch.GetType(), "MaxWeightCapacity");
                if (weightField == null) { Logger?.LogError("[PouchPatch] MaxWeightCapacity field not found."); return; }
                weightField.SetValue(pouch, 100.0f);

                if (allCardTags == null || allCardTags.Length == 0)
                {
                    Logger?.LogWarning("[PouchPatch] CardTag scan was empty; powder filter not added.");
                    return;
                }

                UnityEngine.Object tagPowder = null, tagPowderLiquid = null;
                foreach (var tag in allCardTags)
                {
                    if (tag == null) continue;
                    if (tag.name == "tag_Powder")       tagPowder       = tag;
                    else if (tag.name == "tag_PowderLiquid") tagPowderLiquid = tag;
                    if (tagPowder != null && tagPowderLiquid != null) break;
                }

                if (tagPowder == null) { Logger?.LogWarning("[PouchPatch] tag_Powder not found; powder filter not added."); return; }

                // Access InventoryFilter on the Pouch CardData
                var filterField = AccessTools.Field(pouch.GetType(), "InventoryFilter");
                if (filterField == null) { Logger?.LogError("[PouchPatch] InventoryFilter field not found."); return; }
                var filterObj = filterField.GetValue(pouch);
                if (filterObj == null) { Logger?.LogError("[PouchPatch] InventoryFilter is null."); return; }

                // Access the TagFilters collection inside InventoryFilter
                var tagFiltersField = filterObj.GetType().GetField("TagFilters", Flags);
                if (tagFiltersField == null) { Logger?.LogError("[PouchPatch] TagFilters field not found."); return; }
                var tagFiltersObj = tagFiltersField.GetValue(filterObj);

                // Determine the TagFilter element type
                Type tagFilterType = null;
                if (tagFiltersObj is Array ta) tagFilterType = ta.GetType().GetElementType();
                else if (tagFiltersObj != null)
                {
                    var ga = tagFiltersObj.GetType().GetGenericArguments();
                    if (ga.Length > 0) tagFilterType = ga[0];
                }
                if (tagFilterType == null) { Logger?.LogError("[PouchPatch] Cannot resolve TagFilter element type."); return; }

                // Build the new TagFilter entries for tag_Powder and (optionally) tag_PowderLiquid
                var newFilters = new List<object>();
                foreach (var tag in new[] { tagPowder, tagPowderLiquid })
                {
                    if (tag == null) continue;
                    var tf = Activator.CreateInstance(tagFilterType);
                    tagFilterType.GetField("Tag", Flags)?.SetValue(tf, tag);
                    // NOT = false, OnlyWithLiquid = false  (value-type defaults are already correct)
                    newFilters.Add(tf);
                }

                // Append the new entries – handles both Array and List<T>
                if (tagFiltersObj is Array existingArr)
                {
                    int old = existingArr.Length;
                    var newArr = Array.CreateInstance(tagFilterType, old + newFilters.Count);
                    Array.Copy(existingArr, newArr, old);
                    for (int i = 0; i < newFilters.Count; i++) newArr.SetValue(newFilters[i], old + i);
                    tagFiltersField.SetValue(filterObj, newArr);
                }
                else if (tagFiltersObj is IList list)
                {
                    foreach (var tf in newFilters) list.Add(tf);
                }
                else { Logger?.LogError("[PouchPatch] TagFilters is neither Array nor IList."); return; }

                // Write the (potentially boxed) filter struct back onto the card
                filterField.SetValue(pouch, filterObj);

                Logger?.LogDebug("[PouchPatch] Vanilla Pouch patched: capacity=100, accepts tag_Powder + tag_PowderLiquid.");
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[PouchPatch] {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        static void AppendFermentableTag(object card, UnityEngine.Object fermentableTag, string label)
        {
            if (card == null)
            {
                Logger?.LogDebug($"[FermentableTag] {label} not loaded; skipping.");
                return;
            }

            var cardTagsField = AccessTools.Field(card.GetType(), "CardTags");
            if (cardTagsField == null)
            {
                Logger?.LogError($"[FermentableTag] {label}: CardTags field missing.");
                return;
            }

            var existing = cardTagsField.GetValue(card) as Array;
            if (existing != null)
            {
                foreach (var t in existing)
                {
                    if (t as UnityEngine.Object == fermentableTag) return; // already tagged
                }
            }

            var elemType = cardTagsField.FieldType.GetElementType();
            int oldLen = existing?.Length ?? 0;
            var newArr = Array.CreateInstance(elemType, oldLen + 1);
            if (existing != null) Array.Copy(existing, newArr, oldLen);
            newArr.SetValue(fermentableTag, oldLen);
            cardTagsField.SetValue(card, newArr);
            Logger?.LogDebug($"[FermentableTag] Added tag_Fermentable to {label}.");
        }

    }
}
