using System;
using System.Collections.Generic;
using HarmonyLib;
using BepInEx.Logging;
using CSFFModFramework.Api;
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
                var gameLoadType = Reflect.TryGetType("GameLoad");
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
                Logger.LogError($"[SkillSpeedBoost] Patch error: {ex.InnerException?.ToString() ?? ex.ToString()}");
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

                var gameStatType = Reflect.TryGetType("GameStat");
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

                        if (!Reflect.GetBool(entry, "UsesNovelty", false))
                        {
                            continue;
                        }

                        string skillName = "";
                        if (Reflect.TryGetMember(entry, "GameName", out var gameNameObj) && gameNameObj != null)
                        {
                            if (Reflect.TryGetMember(gameNameObj, "DefaultText", out var nameTextObj) && nameTextObj != null)
                            {
                                skillName = nameTextObj.ToString() ?? "";
                            }
                        }

                        if (IgnoredNames.Contains(skillName))
                        {
                            continue;
                        }

                        if (Reflect.TryGetMember(entry, "UniqueID", out var uidObj) && uidObj is string skillUniqueId
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
                            Reflect.SetMember(entry, "UsesNovelty", false);
                            Reflect.SetMember(entry, "StalenessMultiplier", 0f);
                            Reflect.SetMember(entry, "MaxStalenessStack", 0);
                            disabledStalenessCount++;
                            continue;
                        }

                        // Per-skill staleness: check toggle + rate before setting cooldown.
                        bool perSkillUse  = string.IsNullOrWhiteSpace(skillName)
                            || SkillConfigManager.GetSkillUseStaleness(skillName);
                        float perSkillRate = string.IsNullOrWhiteSpace(skillName)
                            ? 1f
                            : SkillConfigManager.GetSkillStalenessRate(skillName);

                        if (!perSkillUse)
                        {
                            Reflect.SetMember(entry, "UsesNovelty", false);
                            Reflect.SetMember(entry, "StalenessMultiplier", 0f);
                            Reflect.SetMember(entry, "MaxStalenessStack", 0);
                            disabledStalenessCount++;
                            continue;
                        }

                        // Staleness enabled: NoveltyCooldownDuration controls decay speed.
                        // Base = 12 (3 in-game hours, since 1 unit = 15 min).
                        // Rate multiplier scales the cooldown inversely: 2x faster → cooldown/2.
                        if (!Reflect.TryGetMember(entry, "NoveltyCooldownDuration", out var cooldownObj) || cooldownObj is not int currentValue)
                        {
                            continue;
                        }

                        int targetCooldown = (int)Math.Max(1, Math.Round(12.0 / perSkillRate));
                        if (currentValue != targetCooldown)
                        {
                            Reflect.SetMember(entry, "NoveltyCooldownDuration", targetCooldown);
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
                Logger.LogError($"[SkillSpeedBoost] Error modifying skills: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }
    }
}
