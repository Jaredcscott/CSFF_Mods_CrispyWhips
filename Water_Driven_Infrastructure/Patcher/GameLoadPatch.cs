using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using BepInEx.Logging;
using CSFFModFramework.Api;

namespace WaterDrivenInfrastructure.Patcher
{
    public static class GameLoadPatch
    {
        private static ManualLogSource Logger => Plugin.Logger;
        static readonly BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static void ApplyPatch(Harmony harmony)
        {
            try
            {
                var gameLoadType = AccessTools.TypeByName("GameLoad");
                var loadMainGameDataMethod = AccessTools.Method(gameLoadType, "LoadMainGameData");
                var postfixMethod = AccessTools.Method(typeof(GameLoadPatch), nameof(LoadMainGameData_Postfix));
                var postfix = new HarmonyMethod(postfixMethod)
                {
                    after = new[] { "crispywhips.CSFFModFramework" }
                };
                harmony.Patch(loadMainGameDataMethod, postfix: postfix);
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[GameLoadPatch] Failed to apply patches: {ex}");
            }
        }

        static void LoadMainGameData_Postfix()
        {
            try
            {
                if (!BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("crispywhips.CSFFModFramework"))
                {
                    Logger?.LogError("[WDI] CSFFModFramework is not installed. WDI requires it to load card data. " +
                        "Install CSFF_Mod_Framework into BepInEx/plugins/. Stations and improvements will not function.");
                    return;
                }

                var gameLoadType = AccessTools.TypeByName("GameLoad");
                if (gameLoadType == null)
                {
                    Logger?.LogError("[WDI] GameLoadPatch: 'GameLoad' type not found via AccessTools.TypeByName — game update may have renamed it. Stations and improvements will not function.");
                    return;
                }

                var gameLoad = UnityEngine.Object.FindObjectOfType(gameLoadType);
                if (gameLoad == null)
                {
                    Logger?.LogError("[WDI] GameLoadPatch: no 'GameLoad' instance found in scene at postfix time. Stations and improvements will not function.");
                    return;
                }

                var dataBase = AccessTools.Field(gameLoadType, "DataBase")?.GetValue(gameLoad);
                if (dataBase == null)
                {
                    Logger?.LogError("[WDI] GameLoadPatch: 'GameLoad.DataBase' is null at postfix time. Stations and improvements will not function.");
                    return;
                }

                var allDataObj = AccessTools.Field(dataBase.GetType(), "AllData")?.GetValue(dataBase);
                var allData = allDataObj as IEnumerable;
                if (allData == null)
                {
                    Logger?.LogError("[WDI] GameLoadPatch: 'DataBase.AllData' is null or not enumerable at postfix time. Stations and improvements will not function.");
                    return;
                }

                CopyKilnRecipesToForge(allData, Logger, "water_sawmill_forge_placed");
                CopyKilnRecipesToForge(allData, Logger, "water_sawmill_workshop_placed");
                InjectGreenstonePowderSmeltRecipe(allData, Logger, "water_sawmill_forge_placed");
                InjectGreenstonePowderSmeltRecipe(allData, Logger, "water_sawmill_workshop_placed");
                InjectSmeltingContainerIronTag(allData, Logger);
                SuppressWaterDrivenReadyPopups(allData, Logger);
                InjectMillRaceImprovements(allDataObj as IList, allData, Logger);
                InjectActFastenerAlternates(allData, Logger);
                InjectVanillaGrindablesIntoMillFilter(allData, Logger);

                Logger?.LogDebug("[WDI] GameLoadPatch: LoadMainGameData postfix completed (kiln recipes, greenstone smelt, iron tag, mill race improvements, ACT fastener alternates, mill grindables filter).");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[WDI] Error in GameLoadPatch LoadMainGameData postfix: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // WDI has no hard dependency on AdvancedCopperTools (root CLAUDE.md §WDI/ACT
        // Decoupling). This detects ACT once (cached on ActCompat.IsInstalled for
        // ActionInterceptPatch's Workshop craft output) and, when present, makes the 12
        // fastener slots repointed to WDI-native items also accept ACT's originals — a
        // player with both mods installed can use whichever they already have.
        //
        // Iron Rivet is WDI-native (always craftable, no ACT needed) and is wired as an
        // additional accepted alternate wherever WDI's own Copper Rivet is the primary — nails
        // and rivets are a generic fastener commodity (root CLAUDE.md §CardInteractions —
        // AdvancedCopperTools already treats copper/iron nails as interchangeable "in all
        // constructions"), so all four fastener items (ACT Copper/Iron Nail, WDI Copper/Iron
        // Rivet) end up accepted in every fastener slot regardless of which mod authored it.
        // BlueprintAlternates.AddAlternateIngredient accumulates across repeated calls for the
        // same primary instead of clobbering (framework 2.17.0+), so calling it multiple times
        // here is safe.
        static void InjectActFastenerAlternates(IEnumerable allData, ManualLogSource logger)
        {
            const string WdiCopperRivetUid = "water_sawmill_copper_rivet";
            const string WdiIronRivetUid = "water_sawmill_iron_rivet";
            const string ActIronNailUid = "act_iron_nail";

            // WDI-internal: Iron Rivet accepted anywhere Copper Rivet is required, and vice versa
            // (cross-tier acceptance for rivets within WDI), independent of whether ACT is
            // installed.
            BlueprintAlternates.AddAlternateIngredient(allData, WdiCopperRivetUid, WdiIronRivetUid, "WDI Copper Rivet / WDI Iron Rivet");
            BlueprintAlternates.AddAlternateIngredient(allData, WdiIronRivetUid, WdiCopperRivetUid, "WDI Iron Rivet / Copper Rivet");

            ActCompat.Detect(allData);
            if (!ActCompat.IsInstalled)
            {
                logger?.LogDebug("[WDI] ACT fastener alternates: AdvancedCopperTools not installed; WDI-native fasteners only.");
                return;
            }

            // Copper Rivet slots accept ACT's nails.
            BlueprintAlternates.AddAlternateIngredient(allData, WdiCopperRivetUid, ActCompat.CopperNailsUid, "WDI Copper Rivet / ACT Copper Nail");
            BlueprintAlternates.AddAlternateIngredient(allData, WdiCopperRivetUid, ActIronNailUid, "WDI Copper Rivet / ACT Iron Nail");
            
            // Iron Rivet slots also accept ACT's nails.
            BlueprintAlternates.AddAlternateIngredient(allData, WdiIronRivetUid, ActCompat.CopperNailsUid, "WDI Iron Rivet / ACT Copper Nail");
            BlueprintAlternates.AddAlternateIngredient(allData, WdiIronRivetUid, ActIronNailUid, "WDI Iron Rivet / ACT Iron Nail");
            
            // Alloy Solder slots accept ACT's Tin Solder.
            BlueprintAlternates.AddAlternateIngredient(allData, "water_sawmill_alloy_solder", "act_tin_solder", "WDI Alloy Solder / ACT Tin Solder");
        }

        static void CopyKilnRecipesToForge(IEnumerable allData, ManualLogSource logger, string targetForgeUid)
        {
            try
            {
                const string KILN_UID = "d7d2831f33ccf184e9b09f8411339948";

                object kilnCard = null;
                object forgeCard = null;

                foreach (var entry in allData)
                {
                    if (!(entry is UniqueIDScriptable scriptable))
                        continue;
                    if (scriptable.UniqueID == KILN_UID) kilnCard = scriptable;
                    else if (scriptable.UniqueID == targetForgeUid) forgeCard = scriptable;
                    if (kilnCard != null && forgeCard != null) break;
                }

                if (kilnCard == null)
                {
                    logger?.LogError("[CopyKilnRecipes] Kiln card not found");
                    return;
                }
                if (forgeCard == null)
                {
                    logger?.LogError($"[CopyKilnRecipes] WDI forge card not found: {targetForgeUid}");
                    return;
                }

                var recipesField = kilnCard.GetType().GetField("CookingRecipes", InstanceFlags);
                if (recipesField == null)
                {
                    logger?.LogError("[CopyKilnRecipes] CookingRecipes field not found");
                    return;
                }

                var kilnRecipes = recipesField.GetValue(kilnCard) as Array;
                if (kilnRecipes == null || kilnRecipes.Length == 0)
                {
                    logger?.LogError("[CopyKilnRecipes] Kiln has no CookingRecipes");
                    return;
                }

                var forgeRecipesField = forgeCard.GetType().GetField("CookingRecipes", InstanceFlags);
                if (forgeRecipesField == null) return;

                var forgeRecipes = forgeRecipesField.GetValue(forgeCard) as Array;
                var elemType = recipesField.FieldType.GetElementType();
                int existingLen = forgeRecipes?.Length ?? 0;

                var recipesToCopy = new List<object>();
                for (int i = 0; i < kilnRecipes.Length; i++)
                {
                    var kilnRecipe = kilnRecipes.GetValue(i);
                    bool alreadyCopied = false;
                    for (int j = 0; j < existingLen; j++)
                    {
                        if (ReferenceEquals(forgeRecipes.GetValue(j), kilnRecipe))
                        {
                            alreadyCopied = true;
                            break;
                        }
                    }

                    if (!alreadyCopied)
                        recipesToCopy.Add(kilnRecipe);
                }

                if (recipesToCopy.Count == 0)
                {
                    logger?.LogDebug("[CopyKilnRecipes] WDI Forge already has all Kiln recipes");
                    return;
                }

                // Combine: existing forge recipes (greenstone smelting etc.) + missing kiln recipes
                var combined = Array.CreateInstance(elemType, existingLen + recipesToCopy.Count);
                if (forgeRecipes != null && existingLen > 0)
                    Array.Copy(forgeRecipes, combined, existingLen);
                for (int i = 0; i < recipesToCopy.Count; i++)
                    combined.SetValue(recipesToCopy[i], existingLen + i);

                forgeRecipesField.SetValue(forgeCard, combined);

                logger?.LogDebug($"[CopyKilnRecipes] Copied {recipesToCopy.Count} Kiln recipes into {targetForgeUid} (total: {combined.Length})");
            }
            catch (Exception ex)
            {
                logger?.LogError($"[CopyKilnRecipes] Error: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        static void SuppressWaterDrivenReadyPopups(IEnumerable allData, ManualLogSource logger)
        {
            try
            {
                if (allData == null)
                    return;

                var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "water_sawmill_placed",
                    "water_sawmill_grinding_mill_placed",
                    "water_sawmill_forge_placed",
                    "water_sawmill_workshop_placed"
                };

                int cardsPatched = 0;
                int recipesPatched = 0;

                foreach (var entry in allData)
                {
                    if (!(entry is UniqueIDScriptable scriptable))
                        continue;

                    if (!targets.Contains(scriptable.UniqueID))
                        continue;

                    cardsPatched++;

                    // Extra guard: only show card popups while equipped (these are location cards, so this silences board spam).
                    SetBoolMember(scriptable, "OnlyShowPopupsWhenEquipped", true);

                    var recipesObj = GetMemberValue(scriptable, "CookingRecipes") as IEnumerable;
                    if (recipesObj == null)
                        continue;

                    foreach (var recipe in recipesObj)
                    {
                        if (recipe == null)
                            continue;

                        // Primary suppression flag used by cooking completion notifications.
                        if (SetBoolMember(recipe, "HideResultNotification", true))
                            recipesPatched++;

                        // Keep notification type neutral when available.
                        SetIntMember(recipe, "Notification", 0);
                    }
                }

                if (cardsPatched > 0)
                {
                    logger?.Log(BepInEx.Logging.LogLevel.Debug,
                        $"Suppressed ready popups on {cardsPatched} water-driven cards ({recipesPatched} recipes).");
                }
            }
            catch (Exception ex)
            {
                logger?.Log(BepInEx.Logging.LogLevel.Error, $"SuppressWaterDrivenReadyPopups failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        static object GetMemberValue(object instance, string memberName)
        {
            if (instance == null || string.IsNullOrEmpty(memberName))
                return null;

            var type = instance.GetType();
            var field = type.GetField(memberName, InstanceFlags);
            if (field != null)
                return field.GetValue(instance);

            var property = type.GetProperty(memberName, InstanceFlags);
            if (property != null && property.CanRead)
                return property.GetValue(instance, null);

            return null;
        }

        static bool SetBoolMember(object instance, string memberName, bool value)
        {
            if (instance == null || string.IsNullOrEmpty(memberName))
                return false;

            var type = instance.GetType();
            var field = type.GetField(memberName, InstanceFlags);
            if (field != null && field.FieldType == typeof(bool))
            {
                field.SetValue(instance, value);
                return true;
            }

            var property = type.GetProperty(memberName, InstanceFlags);
            if (property != null && property.CanWrite && property.PropertyType == typeof(bool))
            {
                property.SetValue(instance, value, null);
                return true;
            }

            return false;
        }

        static bool SetIntMember(object instance, string memberName, int value)
        {
            if (instance == null || string.IsNullOrEmpty(memberName))
                return false;

            var type = instance.GetType();
            var field = type.GetField(memberName, InstanceFlags);
            if (field != null && field.FieldType == typeof(int))
            {
                field.SetValue(instance, value);
                return true;
            }

            var property = type.GetProperty(memberName, InstanceFlags);
            if (property != null && property.CanWrite && property.PropertyType == typeof(int))
            {
                property.SetValue(instance, value, null);
                return true;
            }

            return false;
        }

        // WarpResolver does not resolve CompatibleCards/DroppedCard inside CookingRecipes elements.
        // This method mirrors what SmeltingRecipeInjector does: set the fields directly on the recipe.
        static void InjectGreenstonePowderSmeltRecipe(IEnumerable allData, ManualLogSource logger, string stationUid)
        {
            try
            {
                const string GREENSTONE_POWDER_UID = "492ba87119393c1448fb6d320e371b65";
                const string METAL_NUGGET_UID = "4b0f4937a5ecb90499428c8c10288afc";
                const string GREENSTONE_SLAG_UID = "efde4db74f8cb194e9ad7cd014bfd7d6";

                object stationCard = null, greenstonePowder = null, metalNugget = null, greenstoneSlag = null;
                foreach (var entry in allData)
                {
                    if (!(entry is UniqueIDScriptable s)) continue;
                    if (s.UniqueID == stationUid) stationCard = s;
                    else if (s.UniqueID == GREENSTONE_POWDER_UID) greenstonePowder = s;
                    else if (s.UniqueID == METAL_NUGGET_UID) metalNugget = s;
                    else if (s.UniqueID == GREENSTONE_SLAG_UID) greenstoneSlag = s;
                }

                if (stationCard == null) { logger?.LogError($"[GreenstoneSmelt] {stationUid} not found"); return; }
                if (greenstonePowder == null || metalNugget == null || greenstoneSlag == null)
                { logger?.LogError("[GreenstoneSmelt] Required vanilla card(s) not found"); return; }

                var recipesField = stationCard.GetType().GetField("CookingRecipes", InstanceFlags);
                var recipes = recipesField?.GetValue(stationCard) as Array;
                if (recipes == null || recipes.Length == 0) return;

                var recipeType = recipes.GetType().GetElementType();
                if (recipeType == null) return;

                var ccField = recipeType.GetField("CompatibleCards", InstanceFlags);
                var ctField = recipeType.GetField("CompatibleTags", InstanceFlags);
                var dropsField = recipeType.GetField("Drops", InstanceFlags);
                if (ccField == null) { logger?.LogError("[GreenstoneSmelt] CompatibleCards field not found"); return; }

                for (int i = 0; i < recipes.Length; i++)
                {
                    var recipe = recipes.GetValue(i);
                    if (recipe == null) continue;

                    var cards = ccField.GetValue(recipe) as Array;
                    var tags = ctField?.GetValue(recipe) as Array;

                    // Skip recipes that have CompatibleTags (Kiln clay recipes) or have cards for something else
                    if (tags != null && tags.Length > 0) continue;

                    bool hasGreenstonePowder = false;
                    bool hasOtherCard = false;
                    if (cards != null)
                    {
                        for (int j = 0; j < cards.Length; j++)
                        {
                            var c = cards.GetValue(j) as UniqueIDScriptable;
                            if (c == null || string.IsNullOrEmpty(c.UniqueID)) continue;
                            if (c.UniqueID == GREENSTONE_POWDER_UID) hasGreenstonePowder = true;
                            else hasOtherCard = true;
                        }
                    }
                    if (hasOtherCard) continue; // belongs to another recipe (WheelbarrowBucket etc.)

                    // Set CompatibleCards = [GreenstonePowder] if not already resolved
                    if (!hasGreenstonePowder)
                    {
                        var elemType = ccField.FieldType.IsArray
                            ? ccField.FieldType.GetElementType()
                            : typeof(UniqueIDScriptable);
                        var cardArr = Array.CreateInstance(elemType ?? typeof(UniqueIDScriptable), 1);
                        cardArr.SetValue(greenstonePowder, 0);
                        ccField.SetValue(recipe, cardArr);
                    }

                    // Fix DroppedCard references in Drops (also unresolved by WarpResolver)
                    if (dropsField != null)
                    {
                        var drops = dropsField.GetValue(recipe) as Array;
                        if (drops != null && drops.Length >= 1)
                        {
                            var dropType = drops.GetType().GetElementType();
                            var dcField = dropType?.GetField("DroppedCard", InstanceFlags);
                            if (dcField != null)
                            {
                                // Drop 0: Metal Nugget
                                var d0 = drops.GetValue(0);
                                if (d0 != null) { dcField.SetValue(d0, metalNugget); drops.SetValue(d0, 0); }
                                // Drop 1: Greenstone Slag
                                if (drops.Length >= 2)
                                {
                                    var d1 = drops.GetValue(1);
                                    if (d1 != null) { dcField.SetValue(d1, greenstoneSlag); drops.SetValue(d1, 1); }
                                }
                            }
                        }
                    }

                    if (recipeType.IsValueType) recipes.SetValue(recipe, i);
                    logger?.LogDebug($"[GreenstoneSmelt] Fixed Smelt Greenstone recipe[{i}] on {stationUid}");
                }
            }
            catch (Exception ex)
            {
                logger?.LogError($"[GreenstoneSmelt] Error: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // Creates one CardData clone per real world-map edge and registers it before
        // GameManager scans AllData for unlockable EnvImprovement cards.
        static void InjectMillRaceImprovements(IList allDataList, IEnumerable allData, ManualLogSource logger)
        {
            const int CARD_TYPE_ENVIRONMENT = 4;
            const int CARD_TYPE_LOCATION    = 8;
            const int CARD_TYPE_IMPROVEMENT = 10;

            var wdiUIDs = new HashSet<string>(StringComparer.Ordinal)
            {
                "water_sawmill_mill_race_north",
                "water_sawmill_mill_race_south",
                "water_sawmill_mill_race_east",
                "water_sawmill_mill_race_west",
            };

            Type cardDataType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { cardDataType = asm.GetType("CardData"); if (cardDataType != null) break; }
                catch (Exception ex) { logger?.LogDebug($"[MillRaceInject] GetType(\"CardData\") failed on {asm.GetName().Name}: {ex.Message}"); }
            }
            if (cardDataType == null)
            { logger?.LogError("[MillRaceInject] CardData type not found"); return; }

            var cardTypeField        = cardDataType.GetField("CardType",               InstanceFlags);
            var envImprovementsField = cardDataType.GetField("EnvironmentImprovements", InstanceFlags);
            var oppositeRoadField    = cardDataType.GetField("OppositeRoad",            InstanceFlags);
            if (cardTypeField == null || envImprovementsField == null)
            {
                logger?.LogError("[MillRaceInject] Required CardData fields missing");
                return;
            }

            var cardSlotType      = envImprovementsField.FieldType.IsArray
                ? envImprovementsField.FieldType.GetElementType() : null;
            var cardSlotCardField = cardSlotType?.GetField("Card", InstanceFlags);
            if (cardSlotType == null || cardSlotCardField == null)
            {
                logger?.LogError("[MillRaceInject] Setup failed (EnvironmentImprovements slot type missing)");
                return;
            }

            // UniqueID is on UniqueIDScriptable — try public field then non-public setter property.
            var uidFieldInfo = typeof(UniqueIDScriptable)
                .GetField("UniqueID", BindingFlags.Public | BindingFlags.Instance);
            System.Reflection.PropertyInfo uidPropInfo = null;
            if (uidFieldInfo == null)
                uidPropInfo = typeof(UniqueIDScriptable)
                    .GetProperty("UniqueID", BindingFlags.Public | BindingFlags.Instance);
            if (uidFieldInfo == null && uidPropInfo == null)
            { logger?.LogError("[MillRaceInject] UniqueID not found on UniqueIDScriptable"); return; }

            var wdiTemplates        = new Dictionary<int, CardData>();
            var explorableLocations = new Dictionary<string, CardData>(StringComparer.Ordinal);
            var environmentCards    = new Dictionary<string, CardData>(StringComparer.Ordinal);

            foreach (var entry in allData)
            {
                if (!(entry is UniqueIDScriptable uid)) continue;
                if (!cardDataType.IsInstanceOfType(entry)) continue;

                var rawCT = cardTypeField.GetValue(entry);
                int ct    = rawCT is Enum ? Convert.ToInt32(rawCT) : (rawCT is int iv ? iv : -1);

                if (ct == CARD_TYPE_IMPROVEMENT && wdiUIDs.Contains(uid.UniqueID))
                {
                    int direction = GetIntMember(entry, "RoadDirection", -1);
                    if (direction >= 0 && !wdiTemplates.ContainsKey(direction))
                        wdiTemplates[direction] = (CardData)entry;
                }
                else if (ct == CARD_TYPE_ENVIRONMENT)
                    environmentCards[uid.UniqueID] = (CardData)entry;
                else if (ct == CARD_TYPE_LOCATION)
                    explorableLocations[uid.UniqueID] = (CardData)entry;
            }

            if (wdiTemplates.Count == 0)
            { logger?.LogError("[MillRaceInject] WDI improvement templates not found — ensure mod JSON is deployed"); return; }

            if (allDataList == null)
            { logger?.LogError("[MillRaceInject] DataBase.AllData is not an IList; clone cards cannot enter the unlockable scan"); return; }

            logger?.LogDebug($"[MillRaceInject] Phase 1: wdiDirs={wdiTemplates.Count}, explorableLocs={explorableLocations.Count}, allDataBefore={allDataList.Count}");

            var mapEdges = MillRaceMapEdgeProvider.Load(logger, explorableLocations);
            if (mapEdges.Count == 0)
            {
                logger?.LogError("[MillRaceInject] Static map had no valid edges; mill race improvements were not injected");
                return;
            }

            InjectMillRaceEdgesFromStaticMap(
                allDataList,
                wdiTemplates,
                explorableLocations,
                environmentCards,
                mapEdges,
                envImprovementsField,
                oppositeRoadField,
                cardSlotType,
                cardSlotCardField,
                uidFieldInfo,
                uidPropInfo,
                logger);
        }

        private static void InjectMillRaceEdgesFromStaticMap(
            IList allDataList,
            Dictionary<int, CardData> wdiTemplates,
            Dictionary<string, CardData> explorableLocations,
            Dictionary<string, CardData> environmentCards,
            List<MillRaceMapEdge> mapEdges,
            FieldInfo envImprovementsField,
            FieldInfo oppositeRoadField,
            Type cardSlotType,
            FieldInfo cardSlotCardField,
            FieldInfo uidFieldInfo,
            PropertyInfo uidPropInfo,
            ManualLogSource logger)
        {
            try
            {
                MillRaceNetwork.Reset();

                int patched = 0, clonesCreated = 0, allDataAdded = 0, oppositesLinked = 0;
                int fallbackPatched = 0, fallbackClones = 0;
                int skippedMissingLocation = 0, skippedDirections = 0, skippedDuplicates = 0, fallbackDuplicates = 0;
                var clonesByRoute = new Dictionary<string, CardData>(StringComparer.Ordinal);
                var patchStates = new Dictionary<CardData, LocationPatchState>();
                var linkedSourceDirections = new HashSet<string>(StringComparer.Ordinal);
                // Locations that actually appear as an endpoint of a validated map edge — i.e.
                // real outdoor mill-race-network locations. The fallback pass below (fills in
                // unlinked directions) must be scoped to THIS set, not every CT8 explorable card,
                // or indoor locations (building interiors, which are CardType 8 too) get all four
                // directions of Mill Race improvements injected despite having no water/travel
                // relevance whatsoever.
                var locationsInNetwork = new HashSet<string>(StringComparer.Ordinal);

                foreach (var edge in mapEdges)
                {
                    if (!wdiTemplates.TryGetValue(edge.Direction, out var template))
                    { skippedDirections++; continue; }
                    if (!explorableLocations.TryGetValue(edge.SourceLocationUid, out var sourceLocation) ||
                        !explorableLocations.TryGetValue(edge.DestinationLocationUid, out var destinationLocation))
                    { skippedMissingLocation++; continue; }
                    if (!environmentCards.TryGetValue(edge.SourceEnvUid, out var sourceEnvironment) ||
                        !environmentCards.TryGetValue(edge.DestinationEnvUid, out var destinationEnvironment))
                    { skippedMissingLocation++; continue; }

                    MillRaceNetwork.RegisterLocation(sourceLocation, edge.SourceEnvUid, sourceEnvironment);
                    MillRaceNetwork.RegisterLocation(destinationLocation, edge.DestinationEnvUid, destinationEnvironment);

                    locationsInNetwork.Add(edge.SourceLocationUid);
                    locationsInNetwork.Add(edge.DestinationLocationUid);

                    var routeKey = RouteKey(edge.SourceLocationUid, edge.Direction, edge.DestinationLocationUid);
                    linkedSourceDirections.Add(SourceDirectionKey(edge.SourceLocationUid, edge.Direction));
                    if (clonesByRoute.ContainsKey(routeKey))
                    { skippedDuplicates++; continue; }

                    if (!patchStates.TryGetValue(sourceLocation, out var patchState))
                    {
                        var existing = envImprovementsField.GetValue(sourceLocation) as Array ?? Array.CreateInstance(cardSlotType, 0);
                        patchState = new LocationPatchState(existing);

                        for (int k = 0; k < existing.Length; k++)
                        {
                            var slot = existing.GetValue(k);
                            if (slot == null) continue;
                            var slotCard = cardSlotCardField.GetValue(slot);
                            if (slotCard is UniqueIDScriptable slotUid)
                                patchState.PresentUIDs.Add(slotUid.UniqueID);
                        }

                        patchStates[sourceLocation] = patchState;
                    }

                    var cloneUID = $"{template.UniqueID}_{FNV1aHex(edge.SourceLocationUid + "_" + edge.Direction + "_" + edge.DestinationLocationUid)}";
                    if (patchState.PresentUIDs.Contains(cloneUID))
                    { skippedDuplicates++; continue; }

                    var clone = UnityEngine.Object.Instantiate(template);
                    clone.name = cloneUID;
                    if (uidFieldInfo != null) uidFieldInfo.SetValue(clone, cloneUID);
                    else uidPropInfo?.GetSetMethod(nonPublic: true)?.Invoke(clone, new object[] { cloneUID });
                    oppositeRoadField?.SetValue(clone, null);
                    clone.RegisterID();

                    allDataList.Add(clone);
                    allDataAdded++;
                    clonesByRoute[routeKey] = clone;

                    var slotObj = Activator.CreateInstance(cardSlotType);
                    cardSlotCardField.SetValue(slotObj, clone);
                    patchState.NewSlots.Add(slotObj);
                    patchState.PresentUIDs.Add(cloneUID);

                    MillRaceNetwork.RegisterEdge(new MillRaceNetwork.EdgeRecord
                    {
                        CloneUid = cloneUID,
                        CloneCard = clone,
                        SourceLocationUid = edge.SourceLocationUid,
                        SourceEnvironmentKey = edge.SourceEnvUid,
                        DestinationLocationUid = edge.DestinationLocationUid,
                        DestinationEnvironmentKey = edge.DestinationEnvUid,
                        Direction = edge.Direction
                    });

                    patched++;
                    clonesCreated++;
                }

                foreach (var locationEntry in explorableLocations)
                {
                    var sourceLocationUid = locationEntry.Key;
                    if (!locationsInNetwork.Contains(sourceLocationUid))
                        continue;
                    var sourceLocation = locationEntry.Value;

                    for (int direction = 0; direction <= 3; direction++)
                    {
                        if (linkedSourceDirections.Contains(SourceDirectionKey(sourceLocationUid, direction)))
                            continue;
                        if (!wdiTemplates.TryGetValue(direction, out var template))
                            continue;

                        if (!patchStates.TryGetValue(sourceLocation, out var patchState))
                        {
                            var existing = envImprovementsField.GetValue(sourceLocation) as Array ?? Array.CreateInstance(cardSlotType, 0);
                            patchState = new LocationPatchState(existing);

                            for (int slotIndex = 0; slotIndex < existing.Length; slotIndex++)
                            {
                                var slot = existing.GetValue(slotIndex);
                                if (slot == null) continue;
                                var slotCard = cardSlotCardField.GetValue(slot);
                                if (slotCard is UniqueIDScriptable slotUid)
                                    patchState.PresentUIDs.Add(slotUid.UniqueID);
                            }

                            patchStates[sourceLocation] = patchState;
                        }

                        var cloneUID = $"{template.UniqueID}_{FNV1aHex(sourceLocationUid + "_" + direction + "_unlinked")}";
                        if (patchState.PresentUIDs.Contains(cloneUID))
                        {
                            fallbackDuplicates++;
                            continue;
                        }

                        var clone = UnityEngine.Object.Instantiate(template);
                        clone.name = cloneUID;
                        if (uidFieldInfo != null) uidFieldInfo.SetValue(clone, cloneUID);
                        else uidPropInfo?.GetSetMethod(nonPublic: true)?.Invoke(clone, new object[] { cloneUID });
                        oppositeRoadField?.SetValue(clone, null);
                        clone.RegisterID();

                        allDataList.Add(clone);
                        allDataAdded++;

                        var slotObj = Activator.CreateInstance(cardSlotType);
                        cardSlotCardField.SetValue(slotObj, clone);
                        patchState.NewSlots.Add(slotObj);
                        patchState.PresentUIDs.Add(cloneUID);

                        fallbackPatched++;
                        fallbackClones++;
                    }
                }

                foreach (var kvp in patchStates)
                {
                    var patchState = kvp.Value;
                    if (patchState.NewSlots.Count == 0)
                        continue;

                    var existing = patchState.ExistingSlots;
                    var newArray = Array.CreateInstance(cardSlotType, existing.Length + patchState.NewSlots.Count);
                    Array.Copy(existing, newArray, existing.Length);

                    for (int i = 0; i < patchState.NewSlots.Count; i++)
                        newArray.SetValue(patchState.NewSlots[i], existing.Length + i);

                    envImprovementsField.SetValue(kvp.Key, newArray);
                }

                foreach (var edge in mapEdges)
                {
                    var routeKey = RouteKey(edge.SourceLocationUid, edge.Direction, edge.DestinationLocationUid);
                    if (!clonesByRoute.TryGetValue(routeKey, out var clone))
                        continue;

                    var oppositeRouteKey = RouteKey(edge.DestinationLocationUid, OppositeDirection(edge.Direction), edge.SourceLocationUid);
                    if (!clonesByRoute.TryGetValue(oppositeRouteKey, out var oppositeClone))
                        continue;

                    oppositeRoadField?.SetValue(clone, oppositeClone);
                    MillRaceNetwork.SetOpposite(clone.UniqueID, oppositeClone.UniqueID);
                    oppositesLinked++;
                }

                logger?.LogDebug($"[MillRaceInject] Static map done — edges={mapEdges.Count}, linkedPatched={patched}, linkedClones={clonesCreated}, fallbackPatched={fallbackPatched}, fallbackClones={fallbackClones}, allDataAdded={allDataAdded}, allDataAfter={allDataList.Count}, opposites={oppositesLinked}, skippedMissingLocation={skippedMissingLocation}, skippedDirs={skippedDirections}, duplicates={skippedDuplicates}, fallbackDuplicates={fallbackDuplicates}");
                MillRaceNetwork.LogRegistrySummary();
            }
            catch (Exception ex)
            {
                logger?.LogError($"[MillRaceInject] Static map injection failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private sealed class LocationPatchState
        {
            public readonly Array ExistingSlots;
            public readonly HashSet<string> PresentUIDs = new HashSet<string>(StringComparer.Ordinal);
            public readonly List<object> NewSlots = new List<object>();

            public LocationPatchState(Array existingSlots)
            {
                ExistingSlots = existingSlots;
            }
        }

        private static int GetIntMember(object instance, string memberName, int fallback)
        {
            var value = GetMemberValue(instance, memberName);
            if (value is int intValue) return intValue;
            if (value is Enum enumValue) return Convert.ToInt32(enumValue);
            return fallback;
        }

        private static string RouteKey(string sourceUid, int direction, string destinationUid)
        {
            return $"{sourceUid}:{direction}:{destinationUid}";
        }

        private static string SourceDirectionKey(string sourceUid, int direction)
        {
            return $"{sourceUid}:{direction}";
        }

        private static int OppositeDirection(int direction)
        {
            switch (direction)
            {
                case 0: return 1;
                case 1: return 0;
                case 2: return 3;
                case 3: return 2;
                default: return -1;
            }
        }

        // Ensures the WDI forge/workshop have the VANILLA tag_SmeltingContainerIron SO in their CardTags.
        // WarpResolver resolves the human-readable "tag_SmeltingContainerIron" string, but if the vanilla
        // SO's runtime .name is the obfuscated asset name "LayoutElement_6968" (not the human-readable form),
        // WarpResolver creates a NEW SO that won't match the reference MetalNugget PE1 checks at runtime.
        // PE1 drains FuelCapacity at -300/DTP when the item is NOT in a container tagged with this SO.
        // Net heat balance without this fix: Kiln +200 - PE1 300 - PE3 100 = -200/tick → iron never heats.
        // Fix: extract the actual SO from MetalNugget's PE1 RequiredNOTContainerTags and inject it.
        // The Grinding Mill's inventory filter accepts tag_Millable only — a tag no vanilla
        // item carries (memory: feedback_grindall_no_millable_gate). Vanilla marks its
        // grindables (wheat/rye grains, acorns, bone splinters, charcoal, ...) purely via a
        // CardInteraction triggered by tag_GrindingTool / tag_HammeringToolGeneral — the same
        // detection HandleGrindAllInner uses. Mirror that detection here and append every
        // vanilla grindable to the mill filter's AcceptedCards so those items can enter the
        // mill inventory and be picked up by Grind All (grain→flour, bone splinters→bonemeal).
        static void InjectVanillaGrindablesIntoMillFilter(IEnumerable allData, ManualLogSource logger)
        {
            try
            {
                const string MILL_UID = "water_sawmill_grinding_mill_placed";
                object mill = null;
                var grindables = new List<object>();

                foreach (var entry in allData)
                {
                    if (!(entry is UniqueIDScriptable s)) continue;
                    if (s.UniqueID == MILL_UID) { mill = s; continue; }
                    if (HasGrindingToolInteraction(s)) grindables.Add(s);
                }

                if (mill == null) { logger?.LogError("[MillFilter] grinding mill placed card not found"); return; }
                if (grindables.Count == 0) { logger?.LogWarning("[MillFilter] no grindable cards detected — filter unchanged"); return; }

                var filterField = mill.GetType().GetField("InventoryFilter", InstanceFlags);
                var filter = filterField?.GetValue(mill);
                if (filter == null) { logger?.LogError("[MillFilter] InventoryFilter not found on mill"); return; }

                var acceptedField = filter.GetType().GetField("AcceptedCards", InstanceFlags);
                if (acceptedField == null) { logger?.LogError("[MillFilter] AcceptedCards field not found on InventoryFilter"); return; }

                var existing = acceptedField.GetValue(filter);
                int added = 0;
                if (existing is IList list && !acceptedField.FieldType.IsArray)
                {
                    var present = new HashSet<object>();
                    foreach (var e in list) if (e != null) present.Add(e);
                    foreach (var g in grindables)
                        if (!present.Contains(g)) { list.Add(g); added++; }
                }
                else
                {
                    var merged = new List<object>();
                    var present = new HashSet<object>();
                    if (existing is Array old)
                        foreach (var e in old) { if (e != null) { merged.Add(e); present.Add(e); } }
                    foreach (var g in grindables)
                        if (!present.Contains(g)) { merged.Add(g); added++; }
                    var elemType = acceptedField.FieldType.GetElementType();
                    if (elemType == null) { logger?.LogError("[MillFilter] AcceptedCards is neither IList nor array"); return; }
                    var arr = Array.CreateInstance(elemType, merged.Count);
                    for (int i = 0; i < merged.Count; i++) arr.SetValue(merged[i], i);
                    acceptedField.SetValue(filter, arr);
                }

                logger?.LogDebug($"[MillFilter] accepted {added} grindable card(s) into the Grinding Mill inventory filter");
            }
            catch (Exception ex)
            {
                logger?.LogError($"[MillFilter] error: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        static bool HasGrindingToolInteraction(object cardData)
        {
            try
            {
                // CT0 items only — locations/liquids/blueprints never belong in the mill.
                var ctField = cardData.GetType().GetField("CardType", InstanceFlags);
                if (ctField == null || Convert.ToInt32(ctField.GetValue(cardData)) != 0) return false;

                var ciField = cardData.GetType().GetField("CardInteractions", InstanceFlags);
                var interactions = ciField?.GetValue(cardData) as IList;
                if (interactions == null) return false;

                foreach (var ci in interactions)
                {
                    if (ci == null) continue;
                    var compat = ci.GetType().GetField("CompatibleCards", InstanceFlags)?.GetValue(ci);
                    var tags = compat?.GetType().GetField("TriggerTags", InstanceFlags)?.GetValue(compat) as IList;
                    if (tags == null) continue;
                    foreach (var t in tags)
                    {
                        var name = (t as UnityEngine.Object)?.name;
                        if (name == "tag_GrindingTool" || name == "tag_HammeringToolGeneral") return true;
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                // Silent false here would drop EVERY vanilla grindable from the mill filter.
                Logger?.LogDebug($"[MillFilter] HasGrindingToolInteraction reflection failed: {ex.Message}");
                return false;
            }
        }

        static void InjectSmeltingContainerIronTag(IEnumerable allData, ManualLogSource logger)
        {
            try
            {
                const string METAL_NUGGET_UID = "4b0f4937a5ecb90499428c8c10288afc";
                var wdiForgeUids = new HashSet<string>
                {
                    "water_sawmill_forge_placed",
                    "water_sawmill_workshop_placed"
                };

                object metalNugget = null;
                var wdiForges = new List<object>();

                foreach (var entry in allData)
                {
                    if (!(entry is UniqueIDScriptable s)) continue;
                    if (s.UniqueID == METAL_NUGGET_UID) metalNugget = s;
                    else if (wdiForgeUids.Contains(s.UniqueID)) wdiForges.Add(s);
                }

                if (metalNugget == null) { logger?.LogError("[IronSmelt] MetalNugget not found"); return; }
                if (wdiForges.Count == 0) { logger?.LogError("[IronSmelt] WDI forge/workshop not found"); return; }

                var passiveEffectsField = metalNugget.GetType().GetField("PassiveEffects", InstanceFlags);
                if (passiveEffectsField == null) { logger?.LogError("[IronSmelt] PassiveEffects field not found"); return; }

                var passiveEffects = passiveEffectsField.GetValue(metalNugget) as Array;
                if (passiveEffects == null || passiveEffects.Length == 0) { logger?.LogError("[IronSmelt] MetalNugget has no PassiveEffects"); return; }

                var peType = passiveEffects.GetType().GetElementType();
                var conditionsField = peType?.GetField("Conditions", InstanceFlags);
                if (conditionsField == null) { logger?.LogError("[IronSmelt] Conditions field not found on PassiveEffect"); return; }

                // Extract the vanilla LayoutElement_6968 SO from either:
                //   PE0.Conditions.RequiredNOTContainerTags (fires when NOT in iron container)
                //   PE1.Conditions.RequiredContainerTags (fires when IN iron container but cold)
                object smeltingContainerIronTag = null;
                for (int i = 0; i < passiveEffects.Length; i++)
                {
                    var pe = passiveEffects.GetValue(i);
                    if (pe == null) continue;
                    var conditions = conditionsField.GetValue(pe);
                    if (conditions == null) continue;
                    var condType = conditions.GetType();

                    // Try RequiredNOTContainerTags first (PE0)
                    var reqNotField = condType.GetField("RequiredNOTContainerTags", InstanceFlags);
                    if (reqNotField != null)
                    {
                        var notTags = reqNotField.GetValue(conditions) as Array;
                        if (notTags != null && notTags.Length > 0)
                        {
                            smeltingContainerIronTag = notTags.GetValue(0);
                            if (smeltingContainerIronTag != null) break;
                        }
                    }

                    // Fall back to RequiredContainerTags (PE1)
                    var reqField = condType.GetField("RequiredContainerTags", InstanceFlags);
                    if (reqField != null)
                    {
                        var tags = reqField.GetValue(conditions) as Array;
                        if (tags != null && tags.Length > 0)
                        {
                            smeltingContainerIronTag = tags.GetValue(0);
                            if (smeltingContainerIronTag != null) break;
                        }
                    }
                }

                if (smeltingContainerIronTag == null)
                {
                    logger?.LogError("[IronSmelt] tag_SmeltingContainerIron SO not found in MetalNugget PE RequiredNOTContainerTags");
                    return;
                }

                var tagRuntimeName = (smeltingContainerIronTag as UnityEngine.Object)?.name ?? "?";
                logger?.LogDebug($"[IronSmelt] Vanilla tag_SmeltingContainerIron SO: '{tagRuntimeName}'");
                if (!tagRuntimeName.Contains("Iron") && !tagRuntimeName.Contains("6968")
                    && !tagRuntimeName.Contains("1000") && !tagRuntimeName.Contains("Temperature"))
                {
                    logger?.LogError($"[IronSmelt] Extracted SO '{tagRuntimeName}' does not look like tag_SmeltingContainerIron — aborting injection");
                    return;
                }

                foreach (var forge in wdiForges)
                {
                    var uid = (forge as UniqueIDScriptable)?.UniqueID ?? "?";
                    var cardTagsField = forge.GetType().GetField("CardTags", InstanceFlags);
                    if (cardTagsField == null) { logger?.LogError($"[IronSmelt] CardTags field not found on {uid}"); continue; }

                    var cardTags = cardTagsField.GetValue(forge) as Array;
                    int existingLen = cardTags?.Length ?? 0;

                    // Check for exact reference match first
                    for (int i = 0; i < existingLen; i++)
                    {
                        if (ReferenceEquals(cardTags.GetValue(i), smeltingContainerIronTag))
                        {
                            logger?.LogDebug($"[IronSmelt] {uid}: already has vanilla SO by reference — no change needed");
                            goto nextForge;
                        }
                    }

                    // Look for a same-name SO to replace (WarpResolver created wrong instance)
                    for (int i = 0; i < existingLen; i++)
                    {
                        var t = cardTags?.GetValue(i) as UnityEngine.Object;
                        if (t != null && t.name == tagRuntimeName)
                        {
                            cardTags.SetValue(smeltingContainerIronTag, i);
                            logger?.LogDebug($"[IronSmelt] {uid}: replaced wrong '{tagRuntimeName}' instance with vanilla SO at index {i}");
                            goto nextForge;
                        }
                    }

                    // Not present at all — append
                    {
                        var elemType = cardTagsField.FieldType.GetElementType() ?? typeof(UnityEngine.Object);
                        var newTags = Array.CreateInstance(elemType, existingLen + 1);
                        if (cardTags != null && existingLen > 0)
                            Array.Copy(cardTags, newTags, existingLen);
                        newTags.SetValue(smeltingContainerIronTag, existingLen);
                        cardTagsField.SetValue(forge, newTags);
                        logger?.LogDebug($"[IronSmelt] {uid}: appended vanilla tag_SmeltingContainerIron SO '{tagRuntimeName}'");
                    }

                    nextForge:;
                }
            }
            catch (Exception ex)
            {
                logger?.LogError($"[IronSmelt] Error: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // FNV-1a hash → 8-char lowercase hex. Deterministic across runs (no GetHashCode).
        private static string FNV1aHex(string s)
        {
            uint h = 2166136261u;
            foreach (char c in s) { h ^= (uint)c; h *= 16777619u; }
            return h.ToString("x8");
        }

    }
}
