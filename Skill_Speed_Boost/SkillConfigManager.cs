using BepInEx.Configuration;
using System.Collections.Generic;

namespace Skill_Speed_Boost;

/// <summary>
/// Manages per-skill XP multipliers.
/// </summary>
public static class SkillConfigManager
{
    private static Dictionary<string, ConfigEntry<int>> _skillMultipliers = new();

    public static readonly string[] CoreSkills = new[]
    {
        "Archery", "Axe", "Blade", "Blunt", "Carpentry", "Cooking",
        "Dodge", "Firecraft", "Foraging", "Gathering", "Herbal",
        "Mining", "Smithing", "Spellcraft", "Tailoring", "Tracking", "Trapping"
    };

    public static void Initialize(ConfigFile config)
    {
        foreach (var skill in CoreSkills)
        {
            var skillKey = NormalizeSkillKey(skill);
            _skillMultipliers[skillKey] = config.Bind(
                "Per-Skill Multipliers",
                $"{skill}_Multiplier",
                1,
                new ConfigDescription(
                    $"XP multiplier for {skill}. 0 = disable skill leveling. 1 = use global (default). 2-10 = per-skill override.",
                    new AcceptableValueRange<int>(0, 10)
                )
            );
        }
    }

    /// <summary>
    /// Get XP multiplier for a specific skill.
    ///   0  → disable skill leveling (caller reverts the XP gain).
    ///   1  → use global SkillExpMultiplier (the default).
    ///   ≥2 → explicit per-skill override.
    /// </summary>
    public static int GetSkillMultiplier(string skillKey)
    {
        var normalized = NormalizeSkillKey(skillKey);

        if (_skillMultipliers.TryGetValue(normalized, out var entry))
        {
            if (entry.Value == 0) return 0;
            if (entry.Value > 1) return entry.Value;
        }

        return Plugin.SkillExpMultiplier;
    }

    public static void RegisterDynamicSkill(ConfigFile config, string skillName)
    {
        var skillKey = NormalizeSkillKey(skillName);
        if (_skillMultipliers.ContainsKey(skillKey)) return;

        _skillMultipliers[skillKey] = config.Bind(
            "Per-Skill Multipliers",
            $"{skillName}_Multiplier",
            1,
            new ConfigDescription(
                $"XP multiplier for {skillName}. 0 = disable skill leveling. 1 = use global (default). 2-10 = per-skill override.",
                new AcceptableValueRange<int>(0, 10)
            )
        );
    }

    public static IEnumerable<(string skillName, int multiplier)> GetAllSkillMultipliers()
    {
        foreach (var kv in _skillMultipliers)
            yield return (kv.Key, kv.Value.Value);
    }

    private static string NormalizeSkillKey(string skillKey)
    {
        if (string.IsNullOrWhiteSpace(skillKey)) return "";
        return skillKey.Trim();
    }
}
