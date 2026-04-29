using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using BepInEx.Logging;
using UnityEngine;

namespace Skill_Speed_Boost.Patcher
{
    /// <summary>
    /// Patches GameLoad.LoadMainGameData to modify all skills and enable staleness decay.
    /// This runs after the game loads vanilla data, allowing us to override GameStat properties.
    /// </summary>
    public static class GameLoadPatch
    {
        private static ManualLogSource Logger => Plugin.Logger;
        private static readonly BindingFlags InstanceFieldFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Dictionary<Type, FieldInfo> UsesNoveltyFields = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> GameNameFields = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> NoveltyCooldownFields = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> DefaultTextFields = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> StalenessMultiplierFields = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> MaxStalenessStackFields = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> StatModificationsFields = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> InterpolatedStatModificationsFields = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> NpcStatModificationsFields = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> TemporaryStatModificationsFields = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> StatChangesFields = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> NpcStatChangesFields = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> StatModifiersFields = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> BlueprintStatModificationsFields = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> PassiveStatEffectsFields = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> ActionStatChangesFields = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> AddedStatModifiersFields = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> AddedTemporaryStatModifiersFields = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> WeaponActionStatChangesFields = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> StatModifierStatFields = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> StatModifierTargetStatFields = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> StatModifierAffectedStatFields = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> StatModifierGameStatFields = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> StatModifierStatWarpDataFields = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> StatModifierStatIdFields = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> StatModifierValueFields = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> StatModifierRateFields = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> StatModifierMinValueFields = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> StatModifierMaxValueFields = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> StatModifierIgnoreNoveltyFields = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> UniqueIdFields = new Dictionary<Type, FieldInfo>();
        internal static readonly HashSet<string> SkillUniqueIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<Type, Dictionary<string, PropertyInfo>> PropertyCaches = new Dictionary<Type, Dictionary<string, PropertyInfo>>();

        private static readonly string[] ModifierCollectionMemberNames =
        {
            "StatModifications",
            "InterpolatedStatModifications",
            "NPCStatModifications",
            "TemporaryStatModifications",
            "StatChanges",
            "NPCStatChanges",
            "StatModifiers",
            "BlueprintStatModifications",
            "PassiveStatEffects",
            "ActionStatChanges",
            "AddedStatModifiers",
            "AddedTemporaryStatModifiers",
            "WeaponActionStatChanges"
        };

        private static readonly HashSet<string> IgnoredNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Stress",
            "Morale",
            "Profile",
            "Altered Mindstate",
            "Mental Structure",
            "Focus",
            "Gratification",
            "Loneliness",
            "Thought Depth"
        };

        public static void ApplyPatch(Harmony harmony)
        {
            try
            {
                var gameLoadType = AccessTools.TypeByName("GameLoad");
                if (gameLoadType == null)
                {
                    Logger.LogError("[SkillSpeedBoost] GameLoad type not found");
                    return;
                }

                var loadMainGameDataMethod = AccessTools.Method(gameLoadType, "LoadMainGameData");
                if (loadMainGameDataMethod == null)
                {
                    Logger.LogError("[SkillSpeedBoost] LoadMainGameData method not found");
                    return;
                }

                harmony.Patch(
                    loadMainGameDataMethod,
                    postfix: new HarmonyMethod(typeof(GameLoadPatch), nameof(LoadMainGameData_Postfix))
                );
            }
            catch (Exception ex)
            {
                Logger.LogError($"[SkillSpeedBoost] Patch error: {ex.Message}");
            }
        }

        /// <summary>
        /// After game data is loaded, modify all skills to enable staleness decay.
        /// Sets NoveltyCooldownDuration to 12 (1 tick = 15 min in-game, so 12 ticks = 3 hours) for all skills that use novelty/staleness.
        /// </summary>
        static void LoadMainGameData_Postfix(object __instance)
        {
            try
            {
                // Get the DataBase from GameLoad instance
                var gmType = __instance.GetType();
                var dataBaseField = gmType.GetField("DataBase", InstanceFieldFlags);
                if (dataBaseField == null) return;

                var dataBase = dataBaseField.GetValue(__instance);
                if (dataBase == null) return;

                // Get AllData from DataBase
                var dbType = dataBase.GetType();
                var allDataField = dbType.GetField("AllData", InstanceFieldFlags);
                if (allDataField == null) return;

                var allData = allDataField.GetValue(dataBase);
                if (allData == null) return;

                // Collect values from all known root collections and dedupe by reference.
                var values = new List<object>();
                var seenRoots = new HashSet<object>(ReferenceEqualityComparer.Instance);

                void AddRoot(object root)
                {
                    if (root == null) return;
                    if (seenRoots.Add(root))
                    {
                        values.Add(root);
                    }
                }

                if (allData is System.Collections.IDictionary dataDict)
                {
                    foreach (var root in dataDict.Values)
                    {
                        AddRoot(root);
                    }
                }
                else if (allData is System.Collections.IList dataList)
                {
                    foreach (var root in dataList)
                    {
                        AddRoot(root);
                    }
                }
                else if (allData is System.Collections.IEnumerable enumerable)
                {
                    foreach (var root in enumerable)
                    {
                        AddRoot(root);
                    }
                }

                // GameStats and related ScriptableObjects are not always present in AllData.
                var scriptableObjects = Resources.FindObjectsOfTypeAll<ScriptableObject>();
                for (int i = 0; i < scriptableObjects.Length; i++)
                {
                    AddRoot(scriptableObjects[i]);
                }

                if (values.Count == 0)
                {
                    return;
                }

                int updatedCount = 0;
                int disabledStalenessCount = 0;
                int scaledModifiersCount = 0;
                int ignoreNoveltyForcedCount = 0;
                SkillUniqueIds.Clear();

                foreach (var entry in values)
                {
                    try
                    {
                        if (entry == null) continue;
                        
                        var entryType = entry.GetType();
                        
                        // Check if this is a GameStat with UsesNovelty.
                        if (!TryGetBoolMember(entry, entryType, "UsesNovelty", UsesNoveltyFields, out var usesNovelty) || !usesNovelty)
                        {
                            continue;
                        }
                        
                        // Get the skill name to filter out mental states
                        string skillName = "";
                        if (TryGetMemberValue(entry, entryType, "GameName", GameNameFields, out var gameNameObj) && gameNameObj != null)
                        {
                            if (TryGetStringMember(gameNameObj, gameNameObj.GetType(), "DefaultText", DefaultTextFields, out var nameText))
                            {
                                skillName = nameText;
                            }
                        }
                        
                        // Skip mental states - only modify actual skills
                        if (IgnoredNames.Contains(skillName))
                        {
                            continue;
                        }
                        if (TryGetStringMember(entry, entryType, "UniqueID", UniqueIdFields, out var skillUniqueId) && !string.IsNullOrWhiteSpace(skillUniqueId))
                        {
                            SkillUniqueIds.Add(skillUniqueId.Trim());
                        }

                        if (!Plugin.EnableSkillStaleness)
                        {
                            // Disable skill staleness: remove novelty penalties entirely
                            TrySetMemberValue(entry, entryType, "UsesNovelty", UsesNoveltyFields, false);

                            TrySetMemberValue(entry, entryType, "StalenessMultiplier", StalenessMultiplierFields, 0f);

                            TrySetMemberValue(entry, entryType, "MaxStalenessStack", MaxStalenessStackFields, 0);

                            disabledStalenessCount++;
                            continue;
                        }

                        // Enable skill staleness: Set NoveltyCooldownDuration to 12 (3 in-game hours)
                        if (!TryGetIntMember(entry, entryType, "NoveltyCooldownDuration", NoveltyCooldownFields, out var currentValue))
                        {
                            continue;
                        }

                        if (currentValue != 12)
                        {
                            TrySetMemberValue(entry, entryType, "NoveltyCooldownDuration", NoveltyCooldownFields, 12);
                            updatedCount++;
                        }
                    }
                    catch
                    {
                        // Silently skip entries that can't be processed
                    }
                }

                if (Plugin.SkillExpMultiplier > 1 || !Plugin.EnableSkillStaleness)
                {
                    ScaleSkillStatModifiers(values, Plugin.SkillExpMultiplier, !Plugin.EnableSkillStaleness, out scaledModifiersCount, out ignoreNoveltyForcedCount);
                    if (Plugin.SkillExpMultiplier > 1 && scaledModifiersCount == 0)
                    {
                        Logger.LogWarning("[SkillSpeedBoost] SkillExpMultiplier is enabled but no skill stat modifiers were scaled.");
                    }
                }

                Logger.LogDebug(
                    $"[SkillSpeedBoost] Settings applied: Roots={values.Count}, DecayUpdated={updatedCount}, NoStalenessApplied={disabledStalenessCount}, XpScaledModifiers={scaledModifiersCount}, IgnoreNoveltyForced={ignoreNoveltyForcedCount}, XpMultiplier={Plugin.SkillExpMultiplier}x"
                );
            }
            catch (Exception ex)
            {
                Logger.LogError($"[SkillSpeedBoost] Error modifying skills: {ex.Message}");
            }
        }

        private static void ScaleSkillStatModifiers(List<object> values, int multiplier, bool forceIgnoreNovelty, out int scaledCount, out int ignoreNoveltyCount)
        {
            scaledCount = 0;
            ignoreNoveltyCount = 0;
            var seenModifiers = new HashSet<object>(ReferenceEqualityComparer.Instance);
            var visitedObjects = new HashSet<object>(ReferenceEqualityComparer.Instance);

            // Pre-populate skill names for intelligent per-skill multiplier lookup
            CollectSkillNames(values);

            foreach (var entry in values)
            {
                if (entry == null) continue;

                try
                {
                    TraverseAndPatchStatModifiers(entry, multiplier, forceIgnoreNovelty, seenModifiers, visitedObjects, ref scaledCount, ref ignoreNoveltyCount);
                }
                catch
                {
                    // Skip entries that don't match expected runtime shapes.
                }
            }
        }

        private static Dictionary<string, string> _skillNamesByUniqueId = new();

        private static void CollectSkillNames(List<object> values)
        {
            _skillNamesByUniqueId.Clear();

            foreach (var entry in values)
            {
                if (entry == null) continue;
                var entryType = entry.GetType();

                // Try to extract UniqueID and GameName
                if (TryGetStringMember(entry, entryType, "UniqueID", UniqueIdFields, out var uniqueId) &&
                    TryGetMemberValue(entry, entryType, "GameName", GameNameFields, out var gameNameObj) &&
                    gameNameObj != null &&
                    TryGetStringMember(gameNameObj, gameNameObj.GetType(), "DefaultText", DefaultTextFields, out var skillName))
                {
                    if (!string.IsNullOrWhiteSpace(uniqueId) && !string.IsNullOrWhiteSpace(skillName))
                    {
                        _skillNamesByUniqueId[uniqueId.Trim()] = skillName.Trim();
                    }
                }
            }
        }

        private static void TraverseAndPatchStatModifiers(
            object current,
            int multiplier,
            bool forceIgnoreNovelty,
            HashSet<object> seenModifiers,
            HashSet<object> visitedObjects,
            ref int scaledCount,
            ref int ignoreNoveltyCount)
        {
            if (current == null) return;

            var currentType = current.GetType();
            if (IsTerminalType(currentType)) return;
            if (!currentType.IsValueType && !visitedObjects.Add(current)) return;

            for (int i = 0; i < ModifierCollectionMemberNames.Length; i++)
            {
                TryPatchModifiersField(current, currentType, ModifierCollectionMemberNames[i], multiplier, forceIgnoreNovelty, seenModifiers, ref scaledCount, ref ignoreNoveltyCount);
            }

            if (current is System.Collections.IEnumerable currentEnumerable && !(current is string))
            {
                foreach (var item in currentEnumerable)
                {
                    TraverseAndPatchStatModifiers(item, multiplier, forceIgnoreNovelty, seenModifiers, visitedObjects, ref scaledCount, ref ignoreNoveltyCount);
                }
                return;
            }

            var fields = currentType.GetFields(InstanceFieldFlags);
            for (int i = 0; i < fields.Length; i++)
            {
                var field = fields[i];
                if (field == null) continue;

                object next;
                try
                {
                    next = field.GetValue(current);
                }
                catch
                {
                    continue;
                }

                if (next == null) continue;
                TraverseAndPatchStatModifiers(next, multiplier, forceIgnoreNovelty, seenModifiers, visitedObjects, ref scaledCount, ref ignoreNoveltyCount);
            }
        }

        private static void TryPatchModifiersField(
            object owner,
            Type ownerType,
            string fieldName,
            int multiplier,
            bool forceIgnoreNovelty,
            HashSet<object> seenModifiers,
            ref int scaledCount,
            ref int ignoreNoveltyCount)
        {
            Dictionary<Type, FieldInfo> cache;
            switch (fieldName)
            {
                case "StatModifications":
                    cache = StatModificationsFields;
                    break;
                case "InterpolatedStatModifications":
                    cache = InterpolatedStatModificationsFields;
                    break;
                case "NPCStatModifications":
                    cache = NpcStatModificationsFields;
                    break;
                case "TemporaryStatModifications":
                    cache = TemporaryStatModificationsFields;
                    break;
                case "StatChanges":
                    cache = StatChangesFields;
                    break;
                case "NPCStatChanges":
                    cache = NpcStatChangesFields;
                    break;
                case "StatModifiers":
                    cache = StatModifiersFields;
                    break;
                case "BlueprintStatModifications":
                    cache = BlueprintStatModificationsFields;
                    break;
                case "PassiveStatEffects":
                    cache = PassiveStatEffectsFields;
                    break;
                case "ActionStatChanges":
                    cache = ActionStatChangesFields;
                    break;
                case "AddedStatModifiers":
                    cache = AddedStatModifiersFields;
                    break;
                case "AddedTemporaryStatModifiers":
                    cache = AddedTemporaryStatModifiersFields;
                    break;
                case "WeaponActionStatChanges":
                    cache = WeaponActionStatChangesFields;
                    break;
                default:
                    cache = StatModificationsFields;
                    break;
            }

            if (!TryGetMemberValue(owner, ownerType, fieldName, cache, out var statModsObj))
            {
                return;
            }

            if (statModsObj == null) return;
            PatchModifierCollection(statModsObj, multiplier, forceIgnoreNovelty, seenModifiers, ref scaledCount, ref ignoreNoveltyCount);
        }

        private static void PatchModifierCollection(
            object statModsObj,
            int multiplier,
            bool forceIgnoreNovelty,
            HashSet<object> seenModifiers,
            ref int scaledCount,
            ref int ignoreNoveltyCount)
        {
            if (statModsObj is System.Collections.IList list)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var modifier = list[i];
                    if (modifier == null || !seenModifiers.Add(modifier)) continue;

                    var patched = PatchSingleModifier(modifier, multiplier, forceIgnoreNovelty, out var didScale, out var didIgnoreNovelty);
                    if (didScale) scaledCount++;
                    if (didIgnoreNovelty) ignoreNoveltyCount++;

                    if (modifier.GetType().IsValueType)
                    {
                        list[i] = patched;
                    }
                }
                return;
            }

            if (statModsObj is Array array)
            {
                for (int i = 0; i < array.Length; i++)
                {
                    var modifier = array.GetValue(i);
                    if (modifier == null || !seenModifiers.Add(modifier)) continue;

                    var patched = PatchSingleModifier(modifier, multiplier, forceIgnoreNovelty, out var didScale, out var didIgnoreNovelty);
                    if (didScale) scaledCount++;
                    if (didIgnoreNovelty) ignoreNoveltyCount++;

                    if (modifier.GetType().IsValueType)
                    {
                        array.SetValue(patched, i);
                    }
                }
                return;
            }

            if (statModsObj is System.Collections.IEnumerable statMods)
            {
                foreach (var modifier in statMods)
                {
                    if (modifier == null || !seenModifiers.Add(modifier)) continue;

                    PatchSingleModifier(modifier, multiplier, forceIgnoreNovelty, out var didScale, out var didIgnoreNovelty);
                    if (didScale) scaledCount++;
                    if (didIgnoreNovelty) ignoreNoveltyCount++;
                }
            }
        }

        private static bool IsTerminalType(Type type)
        {
            if (type == null) return true;
            if (type.IsPrimitive || type.IsEnum) return true;
            if (type == typeof(string) || type == typeof(decimal) || type == typeof(DateTime) || type == typeof(TimeSpan) || type == typeof(Guid))
            {
                return true;
            }

            return false;
        }

        private static object PatchSingleModifier(object modifier, int globalMultiplier, bool forceIgnoreNovelty, out bool didScale, out bool didIgnoreNovelty)
        {
            didScale = false;
            didIgnoreNovelty = false;

            var modType = modifier.GetType();
            object statObj = null;
            var hasResolvedStat = false;
            string skillName = null;

            if (TryGetMemberValue(modifier, modType, "Stat", StatModifierStatFields, out statObj) && statObj != null)
            {
                hasResolvedStat = true;
            }

            if (!hasResolvedStat)
            {
                if (TryGetMemberValue(modifier, modType, "TargetStat", StatModifierTargetStatFields, out statObj) && statObj != null)
                {
                    hasResolvedStat = true;
                }
                else if (TryGetMemberValue(modifier, modType, "AffectedStat", StatModifierAffectedStatFields, out statObj) && statObj != null)
                {
                    hasResolvedStat = true;
                }
                else if (TryGetMemberValue(modifier, modType, "GameStat", StatModifierGameStatFields, out statObj) && statObj != null)
                {
                    hasResolvedStat = true;
                }
            }

            var isTargetSkillStat = false;
            if (hasResolvedStat)
            {
                isTargetSkillStat = IsTargetSkillStat(statObj);

                // Extract skill name for per-skill multiplier lookup
                if (isTargetSkillStat && TryGetStringMember(statObj, statObj.GetType(), "UniqueID", UniqueIdFields, out var skillId))
                {
                    skillId = skillId?.Trim();
                    if (!string.IsNullOrEmpty(skillId) && _skillNamesByUniqueId.TryGetValue(skillId, out var name))
                    {
                        skillName = name;
                    }
                }
            }

            if (!isTargetSkillStat)
            {
                isTargetSkillStat = IsTargetSkillModifierByWarpData(modifier, modType);

                // Try to get skill name from warp data
                if (isTargetSkillStat && TryGetStringMember(modifier, modType, "StatWarpData", StatModifierStatWarpDataFields, out var warpData))
                {
                    warpData = warpData?.Trim();
                    if (!string.IsNullOrEmpty(warpData) && _skillNamesByUniqueId.TryGetValue(warpData, out var name))
                    {
                        skillName = name;
                    }
                }
            }

            if (!isTargetSkillStat)
            {
                return modifier;
            }

            // Determine the effective multiplier for this skill
            int effectiveMultiplier = globalMultiplier;
            if (Plugin.EnablePerSkillMultipliers && !string.IsNullOrWhiteSpace(skillName))
            {
                effectiveMultiplier = SkillConfigManager.GetSkillMultiplier(skillName);
            }

            if (effectiveMultiplier > 1)
            {
                didScale |= TryScaleModifierVectorField(modifier, modType, StatModifierValueFields, "ValueModifier", effectiveMultiplier);
                didScale |= TryScaleModifierVectorField(modifier, modType, StatModifierRateFields, "RateModifier", effectiveMultiplier);
                didScale |= TryScaleModifierVectorField(modifier, modType, StatModifierMinValueFields, "MinValueModifier", effectiveMultiplier);
                didScale |= TryScaleModifierVectorField(modifier, modType, StatModifierMaxValueFields, "MaxValueModifier", effectiveMultiplier);
            }

            if (forceIgnoreNovelty)
            {
                if (TryGetBoolMember(modifier, modType, "IgnoreNovelty", StatModifierIgnoreNoveltyFields, out var ignoreNovelty) && !ignoreNovelty)
                {
                    if (TrySetMemberValue(modifier, modType, "IgnoreNovelty", StatModifierIgnoreNoveltyFields, true))
                    {
                        didIgnoreNovelty = true;
                    }
                }
            }

            return modifier;
        }

        private static bool IsTargetSkillModifierByWarpData(object modifier, Type modType)
        {
            try
            {
                if (SkillUniqueIds.Count == 0)
                {
                    return false;
                }

                if (TryGetStringMember(modifier, modType, "StatWarpData", StatModifierStatWarpDataFields, out var statWarpData))
                {
                    statWarpData = statWarpData?.Trim() ?? string.Empty;
                    if (!string.IsNullOrEmpty(statWarpData) && SkillUniqueIds.Contains(statWarpData))
                    {
                        return true;
                    }
                }

                if (TryGetStringMember(modifier, modType, "StatID", StatModifierStatIdFields, out var statId))
                {
                    statId = statId?.Trim() ?? string.Empty;
                    if (!string.IsNullOrEmpty(statId) && SkillUniqueIds.Contains(statId))
                    {
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryScaleModifierVectorField(object modifier, Type modType, Dictionary<Type, FieldInfo> cache, string fieldName, int multiplier)
        {
            if (!TryGetMemberValue(modifier, modType, fieldName, cache, out var memberValue) || memberValue == null)
            {
                return false;
            }

            if (memberValue is Vector2 vector)
            {
                var scaledVector = vector * multiplier;

                if (scaledVector == vector)
                {
                    return false;
                }

                return TrySetMemberValue(modifier, modType, fieldName, cache, scaledVector);
            }

            if (!TryScaleNumericXYObject(memberValue, multiplier, out var scaledObject))
            {
                return false;
            }

            return TrySetMemberValue(modifier, modType, fieldName, cache, scaledObject);
        }

        private static bool TryScaleNumericXYObject(object source, int multiplier, out object scaled)
        {
            scaled = source;
            var sourceType = source.GetType();

            if (!TryGetNumericXY(source, sourceType, out var x, out var y))
            {
                return false;
            }

            var scaledX = x * multiplier;
            var scaledY = y * multiplier;

            if (Math.Abs(scaledX - x) < 0.000001d && Math.Abs(scaledY - y) < 0.000001d)
            {
                return false;
            }

            var copy = source;
            if (!TrySetNumericXY(ref copy, sourceType, scaledX, scaledY))
            {
                return false;
            }

            scaled = copy;
            return true;
        }

        private static bool TryGetNumericXY(object source, Type sourceType, out double x, out double y)
        {
            x = 0d;
            y = 0d;

            var xField = sourceType.GetField("x", InstanceFieldFlags);
            var yField = sourceType.GetField("y", InstanceFieldFlags);

            if (xField != null && yField != null)
            {
                if (!TryConvertToDouble(xField.GetValue(source), out x) || !TryConvertToDouble(yField.GetValue(source), out y))
                {
                    return false;
                }

                return true;
            }

            var xProperty = sourceType.GetProperty("x", InstanceFieldFlags);
            var yProperty = sourceType.GetProperty("y", InstanceFieldFlags);

            if (xProperty != null && yProperty != null && xProperty.CanRead && yProperty.CanRead)
            {
                if (!TryConvertToDouble(xProperty.GetValue(source, null), out x) || !TryConvertToDouble(yProperty.GetValue(source, null), out y))
                {
                    return false;
                }

                return true;
            }

            return false;
        }

        private static bool TrySetNumericXY(ref object source, Type sourceType, double x, double y)
        {
            var xField = sourceType.GetField("x", InstanceFieldFlags);
            var yField = sourceType.GetField("y", InstanceFieldFlags);

            if (xField != null && yField != null)
            {
                if (!TryConvertFromDouble(x, xField.FieldType, out var xValue) || !TryConvertFromDouble(y, yField.FieldType, out var yValue))
                {
                    return false;
                }

                xField.SetValue(source, xValue);
                yField.SetValue(source, yValue);
                return true;
            }

            var xProperty = sourceType.GetProperty("x", InstanceFieldFlags);
            var yProperty = sourceType.GetProperty("y", InstanceFieldFlags);

            if (xProperty != null && yProperty != null && xProperty.CanWrite && yProperty.CanWrite)
            {
                if (!TryConvertFromDouble(x, xProperty.PropertyType, out var xValue) || !TryConvertFromDouble(y, yProperty.PropertyType, out var yValue))
                {
                    return false;
                }

                xProperty.SetValue(source, xValue, null);
                yProperty.SetValue(source, yValue, null);
                return true;
            }

            return false;
        }

        private static bool TryConvertToDouble(object value, out double converted)
        {
            converted = 0d;
            if (value == null) return false;

            try
            {
                converted = Convert.ToDouble(value);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryConvertFromDouble(double value, Type targetType, out object converted)
        {
            converted = null;

            try
            {
                if (targetType == typeof(float))
                {
                    converted = (float)value;
                    return true;
                }

                if (targetType == typeof(double))
                {
                    converted = value;
                    return true;
                }

                if (targetType == typeof(int))
                {
                    converted = (int)Math.Round(value);
                    return true;
                }

                if (targetType == typeof(long))
                {
                    converted = (long)Math.Round(value);
                    return true;
                }

                converted = Convert.ChangeType(value, targetType);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsTargetSkillStat(object statObj)
        {
            try
            {
                var statType = statObj.GetType();

                // Check UniqueID against previously-collected skill IDs first.
                // This is critical when staleness is disabled, because the first pass
                // sets UsesNovelty=false BEFORE the multiplier pass runs.
                string uniqueId = null;
                if (TryGetStringMember(statObj, statType, "UniqueID", UniqueIdFields, out var idText))
                {
                    uniqueId = idText?.Trim();
                }

                if (!string.IsNullOrEmpty(uniqueId) && SkillUniqueIds.Contains(uniqueId))
                {
                    // Already confirmed as a skill — just verify it's not an ignored mental state
                    if (TryGetMemberValue(statObj, statType, "GameName", GameNameFields, out var gn) && gn != null)
                    {
                        if (TryGetStringMember(gn, gn.GetType(), "DefaultText", DefaultTextFields, out var nm) && IgnoredNames.Contains(nm))
                        {
                            return false;
                        }
                    }
                    return true;
                }

                // Fall back to UsesNovelty for stats not yet in SkillUniqueIds
                if (!TryGetBoolMember(statObj, statType, "UsesNovelty", UsesNoveltyFields, out var usesNovelty) || !usesNovelty)
                {
                    return false;
                }

                if (TryGetMemberValue(statObj, statType, "GameName", GameNameFields, out var gameNameObj) && gameNameObj != null)
                {
                    if (TryGetStringMember(gameNameObj, gameNameObj.GetType(), "DefaultText", DefaultTextFields, out var name) && IgnoredNames.Contains(name))
                    {
                        return false;
                    }
                }

                if (!string.IsNullOrEmpty(uniqueId))
                {
                    SkillUniqueIds.Add(uniqueId);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static FieldInfo GetFieldCached(Dictionary<Type, FieldInfo> cache, Type type, string fieldName)
        {
            if (type == null) return null;

            if (cache.TryGetValue(type, out var field))
            {
                return field;
            }

            field = type.GetField(fieldName, InstanceFieldFlags);
            cache[type] = field;
            return field;
        }

        private static PropertyInfo GetPropertyCached(Type type, string propertyName)
        {
            if (type == null) return null;

            if (!PropertyCaches.TryGetValue(type, out var byName))
            {
                byName = new Dictionary<string, PropertyInfo>(StringComparer.Ordinal);
                PropertyCaches[type] = byName;
            }

            if (byName.TryGetValue(propertyName, out var property))
            {
                return property;
            }

            property = type.GetProperty(propertyName, InstanceFieldFlags);
            byName[propertyName] = property;
            return property;
        }

        private static bool TryGetMemberValue(object target, Type targetType, string memberName, Dictionary<Type, FieldInfo> fieldCache, out object value)
        {
            value = null;
            if (target == null || targetType == null)
            {
                return false;
            }

            var field = GetFieldCached(fieldCache, targetType, memberName);
            if (field != null)
            {
                try
                {
                    value = field.GetValue(target);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            var property = GetPropertyCached(targetType, memberName);
            if (property != null && property.CanRead && property.GetIndexParameters().Length == 0)
            {
                try
                {
                    value = property.GetValue(target, null);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        private static bool TrySetMemberValue(object target, Type targetType, string memberName, Dictionary<Type, FieldInfo> fieldCache, object value)
        {
            if (target == null || targetType == null)
            {
                return false;
            }

            var field = GetFieldCached(fieldCache, targetType, memberName);
            if (field != null)
            {
                try
                {
                    field.SetValue(target, value);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            var property = GetPropertyCached(targetType, memberName);
            if (property != null && property.CanWrite && property.GetIndexParameters().Length == 0)
            {
                try
                {
                    property.SetValue(target, value, null);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        private static bool TryGetBoolMember(object target, Type targetType, string memberName, Dictionary<Type, FieldInfo> fieldCache, out bool value)
        {
            value = false;
            if (!TryGetMemberValue(target, targetType, memberName, fieldCache, out var raw) || !(raw is bool b))
            {
                return false;
            }

            value = b;
            return true;
        }

        private static bool TryGetIntMember(object target, Type targetType, string memberName, Dictionary<Type, FieldInfo> fieldCache, out int value)
        {
            value = 0;
            if (!TryGetMemberValue(target, targetType, memberName, fieldCache, out var raw))
            {
                return false;
            }

            if (raw is int i)
            {
                value = i;
                return true;
            }

            return false;
        }

        private static bool TryGetStringMember(object target, Type targetType, string memberName, Dictionary<Type, FieldInfo> fieldCache, out string value)
        {
            value = string.Empty;
            if (!TryGetMemberValue(target, targetType, memberName, fieldCache, out var raw) || raw == null)
            {
                return false;
            }

            value = raw.ToString() ?? string.Empty;
            return true;
        }

        public static void ReapplyMultiplierPatches()
        {
            Logger.LogInfo("[SkillSpeedBoost] Runtime EXP multiplier reapply is disabled for stability. Changes apply after loading a save or restarting the game.");
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

            public new bool Equals(object x, object y) => ReferenceEquals(x, y);

            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
