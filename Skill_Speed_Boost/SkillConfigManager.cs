using BepInEx.Configuration;
using System.Collections.Generic;

namespace Skill_Speed_Boost;

/// <summary>
/// Manages per-skill XP multipliers and per-skill staleness configuration.
/// </summary>
public static class SkillConfigManager
{
    private static Dictionary<string, ConfigEntry<int>>   _skillMultipliers        = new();
    private static Dictionary<string, ConfigEntry<bool>>  _skillStalenessToggles   = new();
    private static Dictionary<string, ConfigEntry<float>> _skillStalenessRates     = new();

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
            RegisterSkillEntries(config, skill);
        }
    }

    private static void RegisterSkillEntries(ConfigFile config, string skill)
    {
        var skillKey = NormalizeSkillKey(skill);

        if (!_skillMultipliers.ContainsKey(skillKey))
        {
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

        if (!_skillStalenessToggles.ContainsKey(skillKey))
        {
            _skillStalenessToggles[skillKey] = config.Bind(
                "Per-Skill Staleness",
                $"{skill}_UseStaleness",
                true,
                $"Enable staleness decay for {skill}. AND-ed with global EnableSkillStaleness. Default true."
            );
        }

        if (!_skillStalenessRates.ContainsKey(skillKey))
        {
            _skillStalenessRates[skillKey] = config.Bind(
                "Per-Skill Staleness Multipliers",
                $"{skill}_StalenessMultiplier",
                1.0f,
                new ConfigDescription(
                    $"Staleness decay rate for {skill}. 1.0 = default (3 in-game hours). 2.0 = 2x faster decay. 0.5 = half speed.",
                    new AcceptableValueRange<float>(0.1f, 5.0f)
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
        RegisterSkillEntries(config, skillName);
    }

    /// <summary>Returns true if this skill should use staleness decay (AND-ed with global flag).</summary>
    public static bool GetSkillUseStaleness(string skillKey)
    {
        var normalized = NormalizeSkillKey(skillKey);
        if (_skillStalenessToggles.TryGetValue(normalized, out var entry))
            return entry.Value;
        return true;
    }

    /// <summary>
    /// Returns the per-skill staleness decay rate multiplier.
    /// 1.0 = default (3 in-game hours), 2.0 = 2x faster, 0.5 = half speed.
    /// </summary>
    public static float GetSkillStalenessRate(string skillKey)
    {
        var normalized = NormalizeSkillKey(skillKey);
        if (_skillStalenessRates.TryGetValue(normalized, out var entry))
            return entry.Value;
        return 1.0f;
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
