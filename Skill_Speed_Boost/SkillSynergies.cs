using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Skill_Speed_Boost;

/// <summary>
/// Tracks skill combo state for the Synergy XP bonus.
/// Performing related skills in sequence stacks +10% per action, capped at +50% (5-action combo).
/// Combos time out after 5 real-time minutes of inactivity.
/// </summary>
internal static class SkillSynergies
{
    private const float BonusPerAction    = 0.10f;
    private const int   MaxCombo          = 5;
    private const float ComboTimeoutSecs  = 5f * 60f; // 5 real-time minutes

    // group name → (current combo count, last-action realtime)
    private static readonly Dictionary<string, (int count, float time)> _groupCombos
        = new(StringComparer.Ordinal);

    // skill name → synergy groups it belongs to (skills can belong to multiple).
    // Keys are the real GameStat GameName values (verified against
    // Documentation/GameData/CSFF-JsonData_EA_0-65g .../GameStat/Skill_*.json — the
    // 31 stats with UsesNovelty:true), NOT invented names.
    private static readonly Dictionary<string, string[]> _skillGroups
        = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Smithing"]        = new[] { "Crafting", "Tools" },
        ["Tailoring"]       = new[] { "Crafting" },
        ["Weaving"]         = new[] { "Crafting" },
        ["Woodworking"]     = new[] { "Crafting", "Tools" },
        ["Leatherworking"]  = new[] { "Crafting" },
        ["Pottery"]         = new[] { "Crafting" },
        ["Knapping"]        = new[] { "Crafting", "Tools" },
        ["Metalworking"]    = new[] { "Crafting", "Tools" },
        ["Crafting"]        = new[] { "Crafting" },
        ["Cooking"]         = new[] { "Crafting", "Survival" },
        ["Herbalism"]       = new[] { "Gathering", "Survival" },
        ["Climbing"]        = new[] { "Survival" },
        ["Swimming"]        = new[] { "Survival" },
        ["Butchering"]      = new[] { "Gathering", "Hunting", "Survival" },
        ["Fishing"]         = new[] { "Gathering", "Hunting" },
        ["Spear Fishing"]   = new[] { "Gathering", "Hunting" },
        ["Trapping"]        = new[] { "Gathering", "Hunting" },
        ["Tracking"]        = new[] { "Hunting" },
        ["Stealth"]         = new[] { "Hunting", "Combat" },
        ["Archery"]         = new[] { "Combat", "Hunting" },
        ["Axe Fighting"]    = new[] { "Combat" },
        ["ClubFighting"]    = new[] { "Combat" },
        ["Knife Fighting"]  = new[] { "Combat" },
        ["Spear Fighting"]  = new[] { "Combat" },
        ["Handguns"]        = new[] { "Combat" },
        ["Rock Throwing"]   = new[] { "Combat" },
        ["Sling"]           = new[] { "Combat" },
        ["Socials"]         = new[] { "Social" },
        ["Insight"]         = new[] { "Social" },
        ["Percussion"]      = new[] { "Performance" },
        ["Wind Instruments"] = new[] { "Performance" },
    };

    /// <summary>
    /// Records this action in all synergy groups the skill belongs to, then returns
    /// the best active synergy multiplier (1.0 = no bonus, 1.5 = max +50%).
    /// Call once per XP-granting action on the main thread (coroutine-safe).
    /// </summary>
    public static float GetAndRecordSynergyBonus(string skillName)
    {
        if (string.IsNullOrEmpty(skillName)) return 1f;
        if (!_skillGroups.TryGetValue(skillName, out var groups)) return 1f;

        // Skill disabled via per-skill multiplier → don't participate in combos
        if (Plugin.EnablePerSkillMultipliers
            && SkillConfigManager.GetSkillMultiplier(skillName) == 0)
            return 1f;

        float now = Time.realtimeSinceStartup;
        float bestBonus = 0f;

        foreach (var group in groups)
        {
            int newCount;
            if (!_groupCombos.TryGetValue(group, out var state)
                || now - state.time > ComboTimeoutSecs)
            {
                // First action in this group (or timeout reset)
                newCount = 1;
            }
            else
            {
                newCount = Math.Min(state.count + 1, MaxCombo);
            }
            _groupCombos[group] = (newCount, now);

            float groupBonus = newCount * BonusPerAction; // capped by MaxCombo clamp above
            if (groupBonus > bestBonus) bestBonus = groupBonus;
        }

        return 1f + bestBonus;
    }

    /// <summary>Returns a short human-readable summary of active combo state.</summary>
    public static string GetSynergyDebugInfo()
    {
        var sb = new StringBuilder();
        float now = Time.realtimeSinceStartup;
        foreach (var kv in _groupCombos)
        {
            float elapsed = now - kv.Value.time;
            if (elapsed < ComboTimeoutSecs)
                sb.Append($"{kv.Key}={kv.Value.count}({elapsed:F0}s) ");
        }
        return sb.Length > 0 ? sb.ToString().TrimEnd() : "(no active combos)";
    }

}
