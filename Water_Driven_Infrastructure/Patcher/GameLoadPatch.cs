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
                harmony.Patch(loadMainGameDataMethod, postfix: new HarmonyMethod(postfixMethod));
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
                var allData = AccessTools.Field(dataBase.GetType(), "AllData")?.GetValue(dataBase) as IEnumerable;

                if (allData != null)
                {
                    // NOTE: CSFFModFramework handles WarpData resolution (including blueprint stages,
                    // results, action tags, CardsOnBoard, BuildingCardConditions), sprite loading,
                    // PassiveEffect normalization, perk relocation, ProducedCard defaults,
                    // DroppedCards array expansion, perk tab injection, and blueprint tab injection.
                    // Popup suppression must be done by each mod.

                    // Copy Kiln's CookingRecipes (clay firing + stat advancement) into the WDI forge.
                    // JSON-defined recipes can't reference real runtime CardTag/CardData objects
                    // (tag names are corrupted in exported data), so we copy from the working Kiln.
                    CopyKilnRecipesToForge(allData, Logger);

                    // Suppress noisy completion overlays such as "<card> is ready" for always-on stations.
                    SuppressWaterDrivenReadyPopups(allData, Logger);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error in WDI LoadMainGameData postfix: {ex.Message}");
            }
        }

        static void CopyKilnRecipesToForge(IEnumerable allData, ManualLogSource logger)
        {
            try
            {
                const string KILN_UID = "d7d2831f33ccf184e9b09f8411339948";
                const string WDI_FORGE_UID = "water_sawmill_forge_placed";

                object kilnCard = null;
                object forgeCard = null;

                foreach (var entry in allData)
                {
                    if (!(entry is UniqueIDScriptable scriptable))
                        continue;
                    if (scriptable.UniqueID == KILN_UID) kilnCard = scriptable;
                    else if (scriptable.UniqueID == WDI_FORGE_UID) forgeCard = scriptable;
                    if (kilnCard != null && forgeCard != null) break;
                }

                if (kilnCard == null)
                {
                    logger?.LogError("[CopyKilnRecipes] Kiln card not found");
                    return;
                }
                if (forgeCard == null)
                {
                    logger?.LogError("[CopyKilnRecipes] WDI Forge card not found");
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

                // Combine: existing forge recipes (greenstone smelting etc.) + all kiln recipes
                int existingLen = forgeRecipes?.Length ?? 0;
                var combined = Array.CreateInstance(elemType, existingLen + kilnRecipes.Length);
                if (forgeRecipes != null && existingLen > 0)
                    Array.Copy(forgeRecipes, combined, existingLen);
                for (int i = 0; i < kilnRecipes.Length; i++)
                    combined.SetValue(kilnRecipes.GetValue(i), existingLen + i);

                forgeRecipesField.SetValue(forgeCard, combined);

                logger?.LogDebug($"[CopyKilnRecipes] Copied {kilnRecipes.Length} Kiln recipes into WDI Forge (total: {combined.Length})");
            }
            catch (Exception ex)
            {
                logger?.LogError($"[CopyKilnRecipes] Error: {ex.Message}");
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
                    "water_sawmill_forge_placed"
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
                logger?.Log(BepInEx.Logging.LogLevel.Info, $"SuppressWaterDrivenReadyPopups failed: {ex.Message}");
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

    }
}
