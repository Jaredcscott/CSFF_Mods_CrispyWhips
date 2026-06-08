using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using BepInEx.Logging;
using UnityEngine;
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

                // Extend vanilla Turnroot/Fireroot with tag_Fermentable so they work in the pickle vat
                AddFermentableTagToVanillaRoots(allDataEnumerable);

                // Accelerate Tendon drying on the vanilla DryingRack and ACT Tea Blending Station
                AddTendonDryingRecipe(allDataEnumerable);

                // Allow the vanilla Pouch to store herb/mushroom powders
                PatchVanillaPouchForPowderStorage(allDataEnumerable);

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

                    var uniqueIdField = AccessTools.Field(item.GetType(), "UniqueID");
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
                    var cardNameField = item.GetType().GetField("CardName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var cardNameObj = cardNameField?.GetValue(item);
                    if (cardNameObj == null) continue;

                    var localizationKey = GetMember(cardNameObj, "LocalizationKey") as string;

                    if (localizationKey == null || (!localizationKey.Contains("GardenPlot") && !localizationKey.Contains("TilledField")))
                        continue;

                    // Add hemp to the plantation card
                    var plantationCardsField = item.GetType().GetField("PlantationCards", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
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
                Logger?.LogError($"[HempSeeds] Error adding hemp plantation support: {ex.Message}");
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
                Logger?.LogWarning($"[HempSeeds] PlantationCards append failed: {ex.Message}");
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

                foreach (var item in allDataEnumerable)
                {
                    if (item == null) continue;

                    var uniqueIdField = AccessTools.Field(item.GetType(), "UniqueID");
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
                }

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
                    var cardNameField = item.GetType().GetField("CardName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var cardNameObj = cardNameField?.GetValue(item);
                    if (cardNameObj == null) continue;

                    var localizationKey = GetMember(cardNameObj, "LocalizationKey") as string;

                    if (string.IsNullOrEmpty(localizationKey)) continue;

                    // Check if this is a location type we want to modify
                    // Pattern examples: "GroveOak_MossyGrove_CardName", "River_GroveOak_FloodedGrove_CardName"
                    bool isOakGrove = localizationKey.Contains("GroveOak") || localizationKey.Contains("ThicketOak") || localizationKey.Contains("ClearingOak");
                    bool isPineForest = localizationKey.Contains("GrovePine") || localizationKey.Contains("ThicketPine") || localizationKey.Contains("ClearingPine");
                    bool isBirchForest = localizationKey.Contains("Birch") || localizationKey.Contains("GroveBirch") || localizationKey.Contains("ThicketBirch");
                    bool isWillowArea = localizationKey.Contains("Willow") || localizationKey.Contains("GroveWillow") || localizationKey.Contains("ThicketWillow");
                    bool isRiverBank = localizationKey.StartsWith("River_");
                    bool isPrimevalWoods = localizationKey.Contains("PrimevalWoods");
                    // Northern region: areas above Grenfell Falls (NorthernLakeBank, NorthernRapids)
                    bool isNorthernRegion = localizationKey.Contains("Northern");
                    bool isClearing = localizationKey.Contains("Clearing");
                    bool isWildWoods = localizationKey.Contains("WildWoods");
                    bool isLostWoods = localizationKey.Contains("LostWoods");
                    bool isGreenGrove = localizationKey.Contains("GreenGrove");
                    bool isGreenGlade = localizationKey.Contains("GreenGlade");
                    bool isOakenGrove = localizationKey.Contains("OakenGrove");
                    bool isPineMeadow = localizationKey.Contains("PineMeadow");

                    // === EA 0.61 CAVE LOCATIONS ===
                    bool isCaveOldHollow = localizationKey.Contains("CaveOldHollow");
                    bool isCaveShadyThicket = localizationKey.Contains("CaveShadyThicket");
                    bool isCaveStillHollow = localizationKey.Contains("CaveStillHollow");
                    bool isUndergroundCave = isCaveOldHollow || isCaveShadyThicket || isCaveStillHollow;

                    if (!isOakGrove && !isPineForest && !isBirchForest && !isWillowArea && !isRiverBank && !isPrimevalWoods && !isClearing && !isWildWoods && !isNorthernRegion && !isPineMeadow && !isUndergroundCave && !isLostWoods && !isGreenGrove && !isGreenGlade && !isOakenGrove) continue;

                    // Determine location type for mushroom drop logic
                    string locationType = isNorthernRegion ? "Northern" : (isPrimevalWoods ? "Primeval" : (isWillowArea ? "Willow" : (isWildWoods ? "WildWoods" : (isPineMeadow ? "PineMeadow" : (isOakGrove ? "Oak" : (isBirchForest ? "Birch" : (isPineForest ? "Pine" : (isUndergroundCave ? "Cave" : "River"))))))));

                    // Get DismantleActions array
                    var dismantleActionsField = AccessTools.Field(item.GetType(), "DismantleActions");
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
                        var producedCardsField = AccessTools.Field(action.GetType(), "ProducedCards");
                        var producedCards = producedCardsField?.GetValue(action) as IList;

                        if (producedCards == null) continue;

                        // Add mushroom drops based on location type and action
                        if (isForageAction)
                        {
                            // === ORIGINAL MUSHROOMS ===

                            // Morels in oak forests and river banks (8% chance)
                            if ((isOakGrove || isRiverBank) && morelMushroom != null)
                            {
                                AddMushroomDropToAction(producedCards, morelMushroom, 8.0f, true, false);
                            }

                            // Lion's Mane in oak forests (8% chance) - Spring/Summer/Fall only
                            if (isOakGrove && lionsManeMushroom != null)
                            {
                                AddMushroomDropToAction(producedCards, lionsManeMushroom, 8.0f, false, false, true);
                            }

                            // === CAVES: Lion's Mane and Black Trumpet ===
                            // Lion's Mane in caves (8% chance) - Spring/Summer/Fall only
                            if (isUndergroundCave && lionsManeMushroom != null)
                            {
                                AddMushroomDropToAction(producedCards, lionsManeMushroom, 8.0f, false, false, true);
                            }

                            // Black Trumpet in caves (6% chance) - Spring/Summer/Fall only
                            if (isUndergroundCave && blackTrumpet != null)
                            {
                                AddMushroomDropToAction(producedCards, blackTrumpet, 6.0f, false, false, true);
                            }

                            // Oyster mushrooms in oak AND pine forests (King 8%, Golden 14%)
                            if (isOakGrove || isPineForest)
                            {
                                if (kingOyster != null)
                                {
                                    AddMushroomDropToAction(producedCards, kingOyster, 8.0f, true, true);
                                }
                                if (goldenOyster != null)
                                {
                                    AddMushroomDropToAction(producedCards, goldenOyster, 14.0f, true, true);
                                }
                            }

                            // === NEW MUSHROOMS ===

                            // Chanterelle in oak AND birch forests (8% chance) - Spring/Summer/Fall only
                            if ((isOakGrove || isBirchForest) && chanterelle != null)
                            {
                                AddMushroomDropToAction(producedCards, chanterelle, 8.0f, false, false, true);
                            }

                            // Reishi in oak AND pine forests (4% chance - medicinal) - Spring/Summer/Fall only
                            if ((isOakGrove || isPineForest) && reishi != null)
                            {
                                AddMushroomDropToAction(producedCards, reishi, 4.0f, false, false, true);
                            }

                            // Puffball in clearings (10% chance - large food source) - Spring/Summer/Fall only
                            if (isClearing && puffball != null)
                            {
                                AddMushroomDropToAction(producedCards, puffball, 10.0f, false, false, true);
                            }

                            // Chicken of the Woods near Willow trees (12%) and Oak groves (6%) - Spring/Summer/Fall only
                            if (isWillowArea && chickenOfWoods != null)
                            {
                                AddMushroomDropToAction(producedCards, chickenOfWoods, 12.0f, false, false, true);
                            }
                            else if (isOakGrove && chickenOfWoods != null)
                            {
                                AddMushroomDropToAction(producedCards, chickenOfWoods, 6.0f, false, false, true);
                            }

                            // Death Cap in northern region above Grenfell Falls only (1% chance) - Spring/Summer/Fall only
                            if (isNorthernRegion && deathCap != null)
                            {
                                AddMushroomDropToAction(producedCards, deathCap, 1.0f, false, false, true);
                            }

                            // === NEW HERBS ===

                            // Ginseng in Primeval Woods, Lost Woods, Green Grove, Green Glade, Oaken Grove (5% chance) - Spring/Summer/Fall only
                            if ((isPrimevalWoods || isLostWoods || isGreenGrove || isGreenGlade || isOakenGrove) && ginseng != null)
                            {
                                AddMushroomDropToAction(producedCards, ginseng, 5.0f, false, false, true);
                            }

                            // Yarrow in pine meadows specifically (18% chance - 3x normal) - Spring/Summer/Fall only
                            if (isPineMeadow && yarrow != null)
                            {
                                AddMushroomDropToAction(producedCards, yarrow, 18.0f, false, false, true);
                            }
                            // Yarrow in other clearings (6% chance) - Spring/Summer/Fall only
                            else if (isClearing && !isPineMeadow && yarrow != null)
                            {
                                AddMushroomDropToAction(producedCards, yarrow, 6.0f, false, false, true);
                            }

                            // Wild Ginger in river banks and willow areas (6% chance) - Spring/Summer/Fall only
                            if ((isRiverBank || isWillowArea) && ginger != null)
                            {
                                AddMushroomDropToAction(producedCards, ginger, 6.0f, false, false, true);
                            }

                            // === NEWEST MUSHROOMS ===

                            // Black Trumpet in oak groves (6% chance - gourmet) - Spring/Summer/Fall only
                            if (isOakGrove && blackTrumpet != null)
                            {
                                AddMushroomDropToAction(producedCards, blackTrumpet, 6.0f, false, false, true);
                            }

                            // Shiitake in oak forests and dead wood areas (8% chance - immune boost) - Spring/Summer/Fall only
                            if ((isOakGrove || isPrimevalWoods) && shiitake != null)
                            {
                                AddMushroomDropToAction(producedCards, shiitake, 8.0f, false, false, true);
                            }

                            // === HEMP ===

                            // Hemp seeds in Primeval Woods (10% chance) - Spring/Summer/Fall only
                            if (isPrimevalWoods && hempSeeds != null)
                            {
                                AddMushroomDropToAction(producedCards, hempSeeds, 10.0f, false, false, true);
                            }

                            // Hemp plant in Primeval Woods (15% chance - rare find!) - Spring/Summer/Fall only
                            if (isPrimevalWoods && hempPlantMature != null)
                            {
                                AddMushroomDropToAction(producedCards, hempPlantMature, 15.0f, false, false, true);
                            }

                            // === BERRIES ===

                            // Blackcurrant: birch (12%), river banks (8%), oak (5%) - Summer only
                            if (isBirchForest && blackcurrant != null)
                                AddMushroomDropToAction(producedCards, blackcurrant, 12.0f, false, false, true);
                            if (isRiverBank && blackcurrant != null)
                                AddMushroomDropToAction(producedCards, blackcurrant, 8.0f, false, false, true);
                            if (isOakGrove && blackcurrant != null)
                                AddMushroomDropToAction(producedCards, blackcurrant, 5.0f, false, false, true);

                            // Redcurrant: oak (12%), birch (8%), clearings (6%) - Summer only
                            if (isOakGrove && redcurrant != null)
                                AddMushroomDropToAction(producedCards, redcurrant, 12.0f, false, false, true);
                            if (isBirchForest && redcurrant != null)
                                AddMushroomDropToAction(producedCards, redcurrant, 8.0f, false, false, true);
                            if (isClearing && !isPineMeadow && redcurrant != null)
                                AddMushroomDropToAction(producedCards, redcurrant, 6.0f, false, false, true);

                            // Lingonberry: pine meadow (18%), pine forest (16%), northern (12%) - Late Summer/Fall
                            if (isPineMeadow && lingonberry != null)
                                AddMushroomDropToAction(producedCards, lingonberry, 18.0f, false, false, true);
                            else if (isPineForest && lingonberry != null)
                                AddMushroomDropToAction(producedCards, lingonberry, 16.0f, false, false, true);
                            if (isNorthernRegion && lingonberry != null)
                                AddMushroomDropToAction(producedCards, lingonberry, 12.0f, false, false, true);

                            // Cloudberry: northern (10%), pine meadow (6%) - rare northern delicacy
                            if (isNorthernRegion && cloudberry != null)
                                AddMushroomDropToAction(producedCards, cloudberry, 10.0f, false, false, true);
                            if (isPineMeadow && cloudberry != null)
                                AddMushroomDropToAction(producedCards, cloudberry, 6.0f, false, false, true);

                            // === PHASE 1 DECORATIVE/MEDICINAL PLANTS (boosted rates for low-benefit items) ===

                            // Wild Flowers: clearings (20%), pine meadow (16%), river banks (12%)
                            if (isClearing && wildFlowers != null)
                                AddMushroomDropToAction(producedCards, wildFlowers, 20.0f, false, false, true);
                            if (isPineMeadow && wildFlowers != null)
                                AddMushroomDropToAction(producedCards, wildFlowers, 16.0f, false, false, true);
                            if (isRiverBank && wildFlowers != null)
                                AddMushroomDropToAction(producedCards, wildFlowers, 12.0f, false, false, true);

                            // Dandelion: clearings (20%), river banks (14%), oak groves (12%)
                            if (isClearing && dandelion != null)
                                AddMushroomDropToAction(producedCards, dandelion, 20.0f, false, false, true);
                            if (isRiverBank && dandelion != null)
                                AddMushroomDropToAction(producedCards, dandelion, 14.0f, false, false, true);
                            if (isOakGrove && dandelion != null)
                                AddMushroomDropToAction(producedCards, dandelion, 12.0f, false, false, true);

                            // Common Plantain: clearings (16%), river banks (14%), birch (10%)
                            if (isClearing && commonPlantain != null)
                                AddMushroomDropToAction(producedCards, commonPlantain, 16.0f, false, false, true);
                            if (isRiverBank && commonPlantain != null)
                                AddMushroomDropToAction(producedCards, commonPlantain, 14.0f, false, false, true);
                            if (isBirchForest && commonPlantain != null)
                                AddMushroomDropToAction(producedCards, commonPlantain, 10.0f, false, false, true);

                            // Chamomile: pine meadow (18%), clearings (14%), birch (10%)
                            if (isPineMeadow && chamomile != null)
                                AddMushroomDropToAction(producedCards, chamomile, 18.0f, false, false, true);
                            else if (isClearing && chamomile != null)
                                AddMushroomDropToAction(producedCards, chamomile, 14.0f, false, false, true);
                            if (isBirchForest && chamomile != null)
                                AddMushroomDropToAction(producedCards, chamomile, 10.0f, false, false, true);

                            forageActionsModified++;
                        }

                        if (isClearAction)
                        {
                            // Hemp seeds from clearing in Primeval Woods (8% chance) - Spring/Summer/Fall only
                            if (isPrimevalWoods && hempSeeds != null)
                            {
                                AddMushroomDropToAction(producedCards, hempSeeds, 8.0f, false, false, true);
                            }

                            clearActionsModified++;
                        }

                        if (isDigMudOrDirt)
                        {
                            // Truffle when digging up mud/dirt - RARE (1% chance) - Spring/Summer/Fall only
                            // Truffles are underground fungi found when disturbing soil
                            if (truffle != null)
                            {
                                AddMushroomDropToAction(producedCards, truffle, 1.0f, false, false, true);
                            }
                        }
                    }

                    // Oak locations get a dedicated "Dig for Truffles" action (higher find rate)
                    if (isOakGrove && truffle != null)
                    {
                        AddDigForTrufflesAction(item, dismantleActionsField, dismantleActions, truffle);
                    }
                }

                if (locationsModified > 0)
                    Logger?.Log(BepInEx.Logging.LogLevel.Debug, $"[Forage] Added mushroom drops to {locationsModified} locations ({forageActionsModified} forage, {clearActionsModified} clear)");
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[Forage] Error adding mushroom drops: {ex.Message}");
            }
        }

        /// <summary>
        /// Adds a mushroom/herb drop to a forage action's ProducedCards list.
        /// </summary>
        static void AddMushroomDropToAction(IList producedCards, object mushroom, float dropChance,
            bool allYears = false, bool springSummerOnly = false, bool noBotSpringOnly = false)
        {
            if (mushroom == null || producedCards == null) return;

            try
            {
                // Check if mushroom already in this action
                foreach (var collection in producedCards)
                {
                    var dropsField = AccessTools.Field(collection.GetType(), "DroppedCards");
                    if (dropsField == null) continue;

                    var drops = dropsField.GetValue(collection) as Array;
                    if (drops == null) continue;

                    foreach (var drop in drops)
                    {
                        var droppedCardField = AccessTools.Field(drop.GetType(), "DroppedCard");
                        if (droppedCardField?.GetValue(drop) == mushroom)
                            return; // Already present
                    }
                }

                // Add new drop to first collection
                if (producedCards.Count > 0)
                {
                    var collection = producedCards[0];
                    var dropsField = AccessTools.Field(collection.GetType(), "DroppedCards");
                    if (dropsField != null)
                    {
                        var dropsArray = dropsField.GetValue(collection) as Array;
                        if (dropsArray != null)
                        {
                            var dropType = dropsArray.GetType().GetElementType();
                            var newDropsArray = Array.CreateInstance(dropType, dropsArray.Length + 1);
                            Array.Copy(dropsArray, newDropsArray, dropsArray.Length);

                            var newDrop = Activator.CreateInstance(dropType);
                            var droppedCardField = AccessTools.Field(dropType, "DroppedCard");
                            if (droppedCardField != null)
                                droppedCardField.SetValue(newDrop, mushroom);

                            // Set Quantity to {1, 1} so the item actually drops
                            var quantityField = AccessTools.Field(dropType, "Quantity");
                            if (quantityField != null)
                                quantityField.SetValue(newDrop, new UnityEngine.Vector2Int(1, 1));

                            var dropChanceField = AccessTools.Field(dropType, "DropChance");
                            if (dropChanceField != null)
                            {
                                var dropChanceObj = Activator.CreateInstance(dropChanceField.FieldType);
                                var activeField = AccessTools.Field(dropChanceObj.GetType(), "Active");
                                var chanceField = AccessTools.Field(dropChanceObj.GetType(), "BaseDropChance");

                                if (activeField != null) activeField.SetValue(dropChanceObj, true);
                                if (chanceField != null) chanceField.SetValue(dropChanceObj, dropChance);

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
                Logger?.LogError($"[Forage] Error adding mushroom drop: {ex.Message}");
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
                Logger?.LogError($"[Truffle] Error adding Dig for Truffles action: {ex.Message}");
            }
        }

        /// <summary>
        /// Appends tag_Fermentable to vanilla TurnrootFresh and FirerootFresh so they can be placed
        /// into the H&F pickle vat alongside tagged mod items.
        /// </summary>
        static void AddFermentableTagToVanillaRoots(IEnumerable allDataEnumerable)
        {
            try
            {
                const string TurnrootGuid = "ac174c399999d14489fb3788c8931e93";
                const string FirerootGuid = "5ad63f32ed767c64190a64d79841d023";

                // Locate tag_Fermentable CardTag via Resources.FindObjectsOfTypeAll on the CardTag type
                var cardTagType = AccessTools.TypeByName("CardTag");
                if (cardTagType == null)
                {
                    Logger?.LogError("[FermentableTag] CardTag type not found.");
                    return;
                }

                UnityEngine.Object fermentableTag = null;
                var allTags = Resources.FindObjectsOfTypeAll(cardTagType);
                foreach (var tag in allTags)
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
                    var uniqueIdField = AccessTools.Field(item.GetType(), "UniqueID");
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
                Logger?.LogError($"[FermentableTag] Error: {ex.Message}\n{ex.StackTrace}");
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
                var uid = AccessTools.Field(item.GetType(), "UniqueID")?.GetValue(item) as string;
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
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                var recipesField = stationCard.GetType().GetField("CookingRecipes", flags);
                if (recipesField == null)
                {
                    Logger?.LogError($"[TendonDry] {label}: CookingRecipes field not found.");
                    return;
                }

                var recipeArr = recipesField.GetValue(stationCard) as Array;
                if (recipeArr == null || recipeArr.Length == 0)
                {
                    Logger?.LogError($"[TendonDry] {label}: CookingRecipes is null or empty.");
                    return;
                }

                var recipeType = recipeArr.GetType().GetElementType();

                // Idempotency: skip if Tendon recipe already injected
                var compatCardsField = recipeType.GetField("CompatibleCards", flags);
                if (compatCardsField != null)
                {
                    foreach (var existing in recipeArr)
                    {
                        var cc = compatCardsField.GetValue(existing) as Array;
                        if (cc != null && cc.Length == 1 && cc.GetValue(0) == tendon) return;
                    }
                }

                // Clone first recipe as a template for correct type defaults
                var template  = recipeArr.GetValue(0);
                var newRecipe = Activator.CreateInstance(recipeType);
                foreach (var fi in recipeType.GetFields(flags))
                    fi.SetValue(newRecipe, fi.GetValue(template));

                // CompatibleCards = [tendon]; CompatibleTags = []
                if (compatCardsField != null)
                {
                    var elemType = compatCardsField.FieldType.IsArray
                        ? compatCardsField.FieldType.GetElementType()
                        : typeof(object);
                    var arr = Array.CreateInstance(elemType, 1);
                    arr.SetValue(tendon, 0);
                    compatCardsField.SetValue(newRecipe, arr);
                }

                var compatTagsField = recipeType.GetField("CompatibleTags", flags);
                if (compatTagsField != null && compatTagsField.FieldType.IsArray)
                    compatTagsField.SetValue(newRecipe,
                        Array.CreateInstance(compatTagsField.FieldType.GetElementType(), 0));

                // ConditionsCard = 0 (no heat required), Duration = 1
                recipeType.GetField("ConditionsCard", flags)?.SetValue(newRecipe, 0);
                recipeType.GetField("Duration",       flags)?.SetValue(newRecipe, 1);

                // CookerChanges: ModType = 0 (no change to the station itself)
                var cookerField = recipeType.GetField("CookerChanges", flags);
                if (cookerField != null)
                {
                    var cooker = cookerField.GetValue(newRecipe)
                                 ?? Activator.CreateInstance(cookerField.FieldType);
                    cooker.GetType().GetField("ModType", flags)?.SetValue(cooker, 0);
                    cookerField.SetValue(newRecipe, cooker);
                }

                // IngredientChanges: ModType=1, FuelChange=-2 (drains faster), zero others
                var ingField = recipeType.GetField("IngredientChanges", flags);
                if (ingField == null)
                {
                    Logger?.LogError($"[TendonDry] {label}: IngredientChanges field not found.");
                    return;
                }

                var ing = ingField.GetValue(newRecipe)
                          ?? Activator.CreateInstance(ingField.FieldType);
                var ingType = ing.GetType();

                var fuelChangeField = ingType.GetField("FuelChange", flags);
                if (fuelChangeField == null)
                {
                    Logger?.LogError($"[TendonDry] {label}: FuelChange field not found on IngredientChanges — Tendon recipe not injected.");
                    return;
                }

                var zero = new UnityEngine.Vector2(0f, 0f);
                ingType.GetField("ModType",       flags)?.SetValue(ing, 1);
                ingType.GetField("UsageChange",   flags)?.SetValue(ing, zero);
                ingType.GetField("SpoilageChange",flags)?.SetValue(ing, zero);
                fuelChangeField.SetValue(ing, new UnityEngine.Vector2(-2f, -2f));
                ingField.SetValue(newRecipe, ing);

                // Append new recipe to the CookingRecipes array
                var newArr = Array.CreateInstance(recipeType, recipeArr.Length + 1);
                Array.Copy(recipeArr, newArr, recipeArr.Length);
                newArr.SetValue(newRecipe, recipeArr.Length);
                recipesField.SetValue(stationCard, newArr);

                Logger?.LogDebug($"[TendonDry] Injected Tendon drying recipe on {label}.");
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[TendonDry] {label}: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static object GetMember(object target, string name)
        {
            if (target == null) return null;
            var t = target.GetType();
            const BindingFlags f = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            try { var p = t.GetProperty(name, f); if (p != null && p.CanRead) return p.GetValue(target, null); } catch { }
            try { var fi = t.GetField(name, f); if (fi != null) return fi.GetValue(target); } catch { }
            return null;
        }

        private static void SetMember(object target, string name, object value)
        {
            if (target == null) return;
            var t = target.GetType();
            const BindingFlags f = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            try { var p = t.GetProperty(name, f); if (p != null && p.CanWrite) { p.SetValue(target, value, null); return; } } catch { }
            try { var fi = t.GetField(name, f); fi?.SetValue(target, value); } catch { }
        }

        /// <summary>
        /// Patches the vanilla Pouch (description: "ideal for preserving powders") to actually
        /// function as a powder container. Sets MaxWeightCapacity and adds tag_Powder /
        /// tag_PowderLiquid to its InventoryFilter so H&amp;F ground herbs fit inside.
        /// </summary>
        static void PatchVanillaPouchForPowderStorage(IEnumerable allDataEnumerable)
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
                    var uid = AccessTools.Field(item.GetType(), "UniqueID")?.GetValue(item) as string;
                    if (uid == PouchGuid) { pouch = item; break; }
                }

                if (pouch == null)
                {
                    Logger?.LogWarning("[PouchPatch] Vanilla Pouch not found in AllData.");
                    return;
                }

                // Give it enough weight capacity for ~10 powder items (each weighs 10)
                var weightField = AccessTools.Field(pouch.GetType(), "MaxWeightCapacity");
                if (weightField == null) { Logger?.LogError("[PouchPatch] MaxWeightCapacity field not found."); return; }
                weightField.SetValue(pouch, 100.0f);

                // Find tag_Powder and tag_PowderLiquid – created at runtime by the framework
                // when it processes H&F items that carry those tags.
                var cardTagType = AccessTools.TypeByName("CardTag");
                if (cardTagType == null) { Logger?.LogError("[PouchPatch] CardTag type not found."); return; }

                UnityEngine.Object tagPowder = null, tagPowderLiquid = null;
                foreach (var tag in Resources.FindObjectsOfTypeAll(cardTagType))
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

                Logger?.LogInfo("[PouchPatch] Vanilla Pouch patched: capacity=100, accepts tag_Powder + tag_PowderLiquid.");
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
