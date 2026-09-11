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

        // Vanilla stats that set UsesNovelty but are NOT skills. Measured on the EA 0.67i export:
        // 41 GameStats set UsesNovelty; 31 are Skill_*, these 9 are mental stats, and the 10th
        // (Perception) IS a skill and is documented as one. Every name below matches its
        // GameName.DefaultText exactly, which is what the Ordinal comparer requires.
        internal static readonly HashSet<string> IgnoredNames = new HashSet<string>(StringComparer.Ordinal)
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

        /// <summary>
        /// True for a stat this mod deliberately does not treat as a skill, read from the stat
        /// MODEL rather than from a UniqueID set.
        ///
        /// Exists because UsesNovelty is not a skill test: the load-time scan skips the nine
        /// mental stats above before they ever reach <see cref="SkillUniqueIds"/>, so any second
        /// opinion that falls back to UsesNovelty alone reaches the OPPOSITE verdict on exactly
        /// those nine. Both paths have to consult one list or they disagree silently.
        /// </summary>
        internal static bool IsIgnoredStatName(object statModel)
        {
            if (statModel == null) return false;
            if (!Reflect.TryGetMember(statModel, "GameName", out var gameNameObj) || gameNameObj == null)
                return false;
            if (!Reflect.TryGetMember(gameNameObj, "DefaultText", out var nameTextObj) || nameTextObj == null)
                return false;

            var name = nameTextObj.ToString();
            if (string.IsNullOrWhiteSpace(name)) return false;

            // Raw first, to match the scan's own comparison exactly, then trimmed as a courtesy.
            return IgnoredNames.Contains(name) || IgnoredNames.Contains(name.Trim());
        }

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

                // TryGetType resolves by SIMPLE NAME across every loaded assembly, so a foreign
                // assembly defining its own unrelated "GameStat" could win the lookup. Resources.
                // FindObjectsOfTypeAll requires a UnityEngine.Object subclass: handed anything else
                // it logs a Unity error and can return null, NRE-ing the loop below.
                if (!typeof(UnityEngine.Object).IsAssignableFrom(gameStatType))
                {
                    Logger.LogWarning(
                        $"[SkillSpeedBoost] 'GameStat' resolved to {gameStatType.FullName}, which is not a " +
                        "UnityEngine.Object - skill config skipped");
                    return;
                }

                // Direct GameStat scan — only ~30 stats vs 4000+ ScriptableObjects.
                // FindObjectsOfTypeAll covers both vanilla GameStats and any registered
                // by mods via the framework (which creates them via ScriptableObject.CreateInstance).
                var stats = Resources.FindObjectsOfTypeAll(gameStatType);

                int skillCount = 0;
                int updatedCount = 0;
                int disabledStalenessCount = 0;

                // Effective-settings summary: built only when the player asked for it, so the
                // normal path allocates nothing. Emitted after the loop so the lines land
                // together rather than interleaved with anything else patching at load time.
                var effectiveLines = Plugin.LogEffectiveSettings ? new List<string>() : null;

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

                        if (effectiveLines != null && !string.IsNullOrWhiteSpace(skillName))
                            effectiveLines.Add(DescribeEffectiveSettings(skillName.Trim()));

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

                        int targetCooldown = ComputeCooldown(perSkillRate);
                        if (currentValue != targetCooldown)
                        {
                            Reflect.SetMember(entry, "NoveltyCooldownDuration", targetCooldown);
                            updatedCount++;
                        }
                    }
                    catch (Exception exSkill)
                    {
                        // Skip stats that don't match expected shapes. Breadcrumb so a
                        // reflection/field-shape drift on the staleness path isn't invisible
                        // (fleet-wide silent-catch rule / preflight D17). skillName is declared
                        // inside the try, so it isn't referenced here.
                        Logger.LogDebug(
                            $"[SkillSpeedBoost] skipped a skill stat during staleness apply (unexpected field shape): {exSkill.GetType().Name}: {exSkill.Message}");
                    }
                }

                Logger.LogDebug(
                    $"[SkillSpeedBoost] skill config applied: {skillCount} skills, decay updated={updatedCount}, " +
                    $"staleness disabled={disabledStalenessCount}, multiplier={Plugin.SkillExpMultiplier}x (runtime hook)"
                );

                if (effectiveLines != null)
                    LogEffectiveSettings(effectiveLines);
            }
            catch (Exception ex)
            {
                Logger.LogError($"[SkillSpeedBoost] Error modifying skills: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        /// <summary>
        /// NoveltyCooldownDuration for a given per-skill staleness rate. Base 12 = 3 in-game
        /// hours (1 unit = 15 minutes); the rate scales the cooldown inversely, so 2x faster
        /// decay halves it. Shared by the apply path and the effective-settings log so the two
        /// can never disagree about what a rate actually produces.
        /// </summary>
        private static int ComputeCooldown(float stalenessRate)
        {
            return (int)Math.Max(1, Math.Round(12.0 / stalenessRate));
        }

        /// <summary>
        /// One resolved-settings line for a single skill: the multiplier that will actually be
        /// used and where it came from, then the staleness state and, when staleness is live,
        /// the decay rate and the cooldown it produces.
        /// </summary>
        private static string DescribeEffectiveSettings(string skillName)
        {
            int resolved = SkillConfigManager.GetSkillMultiplier(skillName);

            string source;
            if (!Plugin.EnablePerSkillMultipliers)
                source = "global; per-skill overrides disabled";
            else if (SkillConfigManager.TryGetRawSkillMultiplier(skillName, out int raw) && raw != 1)
                source = raw == 0 ? "per-skill override: levelling disabled" : "per-skill override";
            else
                source = "global";

            string staleness;
            if (!Plugin.EnableSkillStaleness)
                staleness = "staleness off (global)";
            else if (!SkillConfigManager.GetSkillUseStaleness(skillName))
                staleness = "staleness off (per-skill)";
            else
            {
                float rate = SkillConfigManager.GetSkillStalenessRate(skillName);
                staleness = $"staleness on (rate {rate:0.##}x, cooldown {ComputeCooldown(rate)})";
            }

            return $"[EffectiveSettings] {skillName}: XP {resolved}x ({source}), {staleness}";
        }

        /// <summary>
        /// Emits the effective-settings summary. Gated on the LogEffectiveSettings config entry
        /// by the caller, which is why this is allowed to use LogInfo in a per-skill loop: the
        /// whole point is to be readable in a default LogOutput.log, and it is off by default.
        /// Same shape as the existing SkillSynergiesDebugLog gate.
        /// </summary>
        private static void LogEffectiveSettings(List<string> lines)
        {
            Logger.LogInfo(
                $"[EffectiveSettings] {lines.Count} skills. Profile={Plugin.ActiveProfile}, " +
                $"GlobalExpMultiplier={Plugin.SkillExpMultiplier}x, PerSkillMultipliers={Plugin.EnablePerSkillMultipliers}, " +
                $"Staleness={Plugin.EnableSkillStaleness}");

            string familiarity = Plugin.AreaFamiliarityEnabled
                ? $"on (+{Plugin.AreaFamiliarityMinBonus * 100f:0.#}% at a new location rising to +{Plugin.AreaFamiliarityMaxBonus * 100f:0.#}% over {Plugin.AreaFamiliarityVisitsForMaxBonus} actions)"
                : "off";
            string morning = Plugin.MorningBonusEnabled
                ? $"on ({Plugin.MorningBonusMultiplier:0.##}x between {Plugin.MorningStartHour:0}:00 and {Plugin.MorningEndHour:0}:00)"
                : "off";
            string levelScaling = Plugin.LevelScalingEnabled
                ? $"on (up to +{Plugin.LevelScalingMaxBonus * 100f:0.#}%, {Plugin.LevelScalingCurve} curve)"
                : "off";
            string cap = Plugin.MaxComposedMultiplier > 0f
                ? $"{Plugin.MaxComposedMultiplier:0.##}x"
                : "uncapped";

            Logger.LogInfo(
                $"[EffectiveSettings] AreaFamiliarity={familiarity}, MorningBonus={morning}, LevelScaling={levelScaling}, " +
                $"SkillSynergies={(Plugin.SkillSynergiesEnabled ? "on" : "off")}, " +
                $"DailyFirstUse={(Plugin.DailyFirstUseBonusEnabled ? $"on ({Plugin.DailyFirstUseMultiplier:0.##}x)" : "off")}, " +
                $"WellRested={(Plugin.WellRestedBonusEnabled ? $"on ({Plugin.WellRestedMultiplier:0.##}x above {Plugin.WellRestedThreshold:0}%)" : "off")}, " +
                $"LowConditionPenalty={(Plugin.LowConditionPenaltyEnabled ? $"on ({Plugin.LowConditionMultiplier:0.##}x below {Plugin.LowConditionThreshold:0}%)" : "off")}, " +
                $"MaxComposedMultiplier={cap}");

            foreach (var line in lines)
                Logger.LogInfo(line);
        }
    }
}
