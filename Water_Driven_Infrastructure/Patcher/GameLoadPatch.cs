using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using BepInEx.Logging;

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
                var gameLoadType = AccessTools.TypeByName("GameLoad");
                var gameLoad = UnityEngine.Object.FindObjectOfType(gameLoadType);
                var dataBase = AccessTools.Field(gameLoadType, "DataBase")?.GetValue(gameLoad);
                var allDataObj = AccessTools.Field(dataBase.GetType(), "AllData")?.GetValue(dataBase);
                var allData = allDataObj as IEnumerable;

                if (allData != null)
                {
                    CopyKilnRecipesToForge(allData, Logger, "water_sawmill_forge_placed");
                    CopyKilnRecipesToForge(allData, Logger, "water_sawmill_workshop_placed");
                    InjectGreenstonePowderSmeltRecipe(allData, Logger, "water_sawmill_forge_placed");
                    InjectGreenstonePowderSmeltRecipe(allData, Logger, "water_sawmill_workshop_placed");
                    SuppressWaterDrivenReadyPopups(allData, Logger);
                    InjectMillRaceImprovements(allDataObj as IList, allData, Logger);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error in WDI LoadMainGameData postfix: {ex.Message}");
            }
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
                catch { }
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

            logger?.LogInfo($"[MillRaceInject] Phase 1: wdiDirs={wdiTemplates.Count}, explorableLocs={explorableLocations.Count}, allDataBefore={allDataList.Count}");

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
                int skippedMissingLocation = 0, skippedDirections = 0, skippedDuplicates = 0;
                var clonesByRoute = new Dictionary<string, CardData>(StringComparer.Ordinal);

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

                    var routeKey = RouteKey(edge.SourceLocationUid, edge.Direction, edge.DestinationLocationUid);
                    if (clonesByRoute.ContainsKey(routeKey))
                    { skippedDuplicates++; continue; }

                    var existing = envImprovementsField.GetValue(sourceLocation) as Array ?? Array.CreateInstance(cardSlotType, 0);
                    var presentUIDs = new HashSet<string>(StringComparer.Ordinal);
                    for (int k = 0; k < existing.Length; k++)
                    {
                        var slot = existing.GetValue(k);
                        if (slot == null) continue;
                        var slotCard = cardSlotCardField.GetValue(slot);
                        if (slotCard is UniqueIDScriptable slotUid)
                            presentUIDs.Add(slotUid.UniqueID);
                    }

                    var cloneUID = $"{template.UniqueID}_{FNV1aHex(edge.SourceLocationUid + "_" + edge.Direction + "_" + edge.DestinationLocationUid)}";
                    if (presentUIDs.Contains(cloneUID))
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

                    var newArray = Array.CreateInstance(cardSlotType, existing.Length + 1);
                    Array.Copy(existing, newArray, existing.Length);
                    newArray.SetValue(slotObj, existing.Length);
                    envImprovementsField.SetValue(sourceLocation, newArray);

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

                logger?.LogInfo($"[MillRaceInject] Static map done — edges={mapEdges.Count}, patched={patched}, clones={clonesCreated}, allDataAdded={allDataAdded}, allDataAfter={allDataList.Count}, opposites={oppositesLinked}, skippedMissingLocation={skippedMissingLocation}, skippedDirs={skippedDirections}, duplicates={skippedDuplicates}");
                MillRaceNetwork.LogRegistrySummary();
            }
            catch (Exception ex)
            {
                logger?.LogError($"[MillRaceInject] Static map injection failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
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

        // FNV-1a hash → 8-char lowercase hex. Deterministic across runs (no GetHashCode).
        private static string FNV1aHex(string s)
        {
            uint h = 2166136261u;
            foreach (char c in s) { h ^= (uint)c; h *= 16777619u; }
            return h.ToString("x8");
        }

    }
}
