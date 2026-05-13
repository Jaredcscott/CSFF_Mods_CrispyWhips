using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using BepInEx.Logging;
using UnityEngine;

namespace Skill_Speed_Boost.Patcher
{
    /// <summary>
    /// Patches GameLoad.LoadMainGameData to configure skill staleness behavior on each
    /// GameStat. The actual XP multiplier is applied at runtime by MorningBonusPatch's
    /// ChangeStatValue postfix — there's no per-modifier scaling pass anymore. This
    /// keeps the load-time cost proportional to the number of skill stats (~10–20)
    /// rather than the entire ScriptableObject graph (~4000).
    /// </summary>
    public static class GameLoadPatch
    {
        private static ManualLogSource Logger => Plugin.Logger;
        private static readonly BindingFlags InstanceFieldFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // Per-type field caches for the GameStat config pass.
        private static readonly Dictionary<Type, FieldInfo> UsesNoveltyFields = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> GameNameFields = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> NoveltyCooldownFields = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> DefaultTextFields = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> StalenessMultiplierFields = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> MaxStalenessStackFields = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> UniqueIdFields = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, Dictionary<string, PropertyInfo>> PropertyCaches = new Dictionary<Type, Dictionary<string, PropertyInfo>>();

        // Populated during GameStat scan; consumed by MorningBonusPatch.IsSkillStat and the
        // per-skill multiplier lookup in the same postfix.
        internal static readonly HashSet<string> SkillUniqueIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal static readonly Dictionary<string, string> SkillNamesByUniqueId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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
        /// After game data loads, identify skill stats (GameStat where UsesNovelty == true)
        /// and configure their staleness behavior. Records each skill's UniqueID + GameName
        /// for the runtime multiplier hook in MorningBonusPatch.
        /// </summary>
        static void LoadMainGameData_Postfix(object __instance)
        {
            try
            {
                SkillUniqueIds.Clear();
                SkillNamesByUniqueId.Clear();

                var gameStatType = AccessTools.TypeByName("GameStat");
                if (gameStatType == null)
                {
                    Logger.LogWarning("[SkillSpeedBoost] GameStat type not found — skill config skipped");
                    return;
                }

                // Direct GameStat scan — only ~30 stats vs 4000+ ScriptableObjects.
                // FindObjectsOfTypeAll covers both vanilla GameStats and any registered
                // by mods via the framework (which creates them via ScriptableObject.CreateInstance).
                var stats = Resources.FindObjectsOfTypeAll(gameStatType);

                int skillCount = 0;
                int updatedCount = 0;
                int disabledStalenessCount = 0;

                foreach (var entry in stats)
                {
                    try
                    {
                        if (entry == null) continue;
                        var entryType = entry.GetType();

                        if (!TryGetBoolMember(entry, entryType, "UsesNovelty", UsesNoveltyFields, out var usesNovelty) || !usesNovelty)
                        {
                            continue;
                        }

                        string skillName = "";
                        if (TryGetMemberValue(entry, entryType, "GameName", GameNameFields, out var gameNameObj) && gameNameObj != null)
                        {
                            if (TryGetStringMember(gameNameObj, gameNameObj.GetType(), "DefaultText", DefaultTextFields, out var nameText))
                            {
                                skillName = nameText;
                            }
                        }

                        if (IgnoredNames.Contains(skillName))
                        {
                            continue;
                        }

                        if (TryGetStringMember(entry, entryType, "UniqueID", UniqueIdFields, out var skillUniqueId)
                            && !string.IsNullOrWhiteSpace(skillUniqueId))
                        {
                            var uid = skillUniqueId.Trim();
                            SkillUniqueIds.Add(uid);
                            if (!string.IsNullOrWhiteSpace(skillName))
                            {
                                var trimmedName = skillName.Trim();
                                SkillNamesByUniqueId[uid] = trimmedName;
                                // Register any skills that aren't in the hard-coded core list
                                // so players can configure per-skill multipliers for mod-added skills.
                                SkillConfigManager.RegisterDynamicSkill(Plugin.Instance.Config, trimmedName);
                            }
                        }

                        skillCount++;

                        if (!Plugin.EnableSkillStaleness)
                        {
                            // Disable staleness at the stat level — the runtime hook then sees
                            // raw deltas with no novelty reduction to multiply. Per-modifier
                            // IgnoreNovelty is no longer needed since the stat-level flag
                            // short-circuits novelty before any modifier evaluates.
                            TrySetMemberValue(entry, entryType, "UsesNovelty", UsesNoveltyFields, false);
                            TrySetMemberValue(entry, entryType, "StalenessMultiplier", StalenessMultiplierFields, 0f);
                            TrySetMemberValue(entry, entryType, "MaxStalenessStack", MaxStalenessStackFields, 0);
                            disabledStalenessCount++;
                            continue;
                        }

                        // Staleness enabled: set NoveltyCooldownDuration to 12 (3 in-game hours,
                        // since 1 unit = 15 min) so identical actions stop refunding novelty
                        // immediately.
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
                        // Skip stats that don't match expected shapes.
                    }
                }

                Logger.LogDebug(
                    $"[SkillSpeedBoost] skill config applied: {skillCount} skills, decay updated={updatedCount}, " +
                    $"staleness disabled={disabledStalenessCount}, multiplier={Plugin.SkillExpMultiplier}x (runtime hook)"
                );
            }
            catch (Exception ex)
            {
                Logger.LogError($"[SkillSpeedBoost] Error modifying skills: {ex.Message}");
            }
        }

        // ── Reflection helpers ────────────────────────────────────────────────────

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
            if (target == null || targetType == null) return false;

            var field = GetFieldCached(fieldCache, targetType, memberName);
            if (field != null)
            {
                try { value = field.GetValue(target); return true; } catch { return false; }
            }

            var property = GetPropertyCached(targetType, memberName);
            if (property != null && property.CanRead && property.GetIndexParameters().Length == 0)
            {
                try { value = property.GetValue(target, null); return true; } catch { return false; }
            }

            return false;
        }

        private static bool TrySetMemberValue(object target, Type targetType, string memberName, Dictionary<Type, FieldInfo> fieldCache, object value)
        {
            if (target == null || targetType == null) return false;

            var field = GetFieldCached(fieldCache, targetType, memberName);
            if (field != null)
            {
                try { field.SetValue(target, value); return true; } catch { return false; }
            }

            var property = GetPropertyCached(targetType, memberName);
            if (property != null && property.CanWrite && property.GetIndexParameters().Length == 0)
            {
                try { property.SetValue(target, value, null); return true; } catch { return false; }
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
            if (!TryGetMemberValue(target, targetType, memberName, fieldCache, out var raw)) return false;
            if (raw is int i) { value = i; return true; }
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
    }
}
