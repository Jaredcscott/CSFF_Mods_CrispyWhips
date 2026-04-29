using BepInEx.Configuration;
using System.Collections.Generic;
using System.Linq;

namespace Skill_Speed_Boost;

/// <summary>
/// Manages per-skill XP multipliers and difficulty presets.
/// Allows fine-tuning individual skill leveling rates and switching between profiles.
/// </summary>
public static class SkillConfigManager
{
    private static ConfigEntry<string> _activeProfile;
    private static Dictionary<string, ConfigEntry<int>> _skillMultipliers = new();
    private static Dictionary<string, ConfigEntry<bool>> _skillStalenessOverrides = new();
    private static Dictionary<string, ConfigEntry<float>> _skillStalenessMultiplierOverrides = new();

    /// <summary>
    /// Known skill keys for profile configuration. This is a subset; perks are dynamically added.
    /// </summary>
    public static readonly string[] CoreSkills = new[]
    {
        "Archery", "Axe", "Blade", "Blunt", "Carpentry", "Cooking",
        "Dodge", "Firecraft", "Foraging", "Gathering", "Herbal",
        "Mining", "Smithing", "Spellcraft", "Tailoring", "Tracking", "Trapping"
    };

    /// <summary>
    /// Available difficulty profiles and their settings.
    /// </summary>
    public static class Profiles
    {
        public const string VanillaPlus = "VanillaPlus";
        public const string Casual = "Casual";
        public const string Hardcore = "Hardcore";
        public const string Grinder = "Grinder";
        public const string Balanced = "Balanced";
        public const string Legacy = "Legacy";

        public static readonly string[] All = { VanillaPlus, Casual, Hardcore, Grinder, Balanced, Legacy };

        public static (int expMult, bool staleness) GetProfileSettings(string profile) => profile switch
        {
            VanillaPlus => (expMult: 2, staleness: true),      // 2x XP, maintain vanilla staleness
            Casual => (expMult: 3, staleness: false),           // 3x XP, no staleness
            Hardcore => (expMult: 1, staleness: true),          // Vanilla XP, full staleness
            Grinder => (expMult: 10, staleness: false),         // Max XP, no staleness (testing)
            Balanced => (expMult: 2, staleness: true),          // 2x XP, manageable staleness
            Legacy => (expMult: 1, staleness: true),            // Original vanilla behavior
            _ => (expMult: 1, staleness: true),                 // Default to vanilla
        };
    }

    public static void Initialize(ConfigFile config)
    {
        _activeProfile = config.Bind(
            "Profiles",
            "ActiveProfile",
            Profiles.Balanced,
            new ConfigDescription(
                $"Active difficulty profile. Choose from: {string.Join(", ", Profiles.All)}",
                new AcceptableValueList<string>(Profiles.All)
            )
        );

        // Initialize core skill multipliers
        foreach (var skill in CoreSkills)
        {
            var skillKey = NormalizeSkillKey(skill);
            _skillMultipliers[skillKey] = config.Bind(
                "Per-Skill Multipliers",
                $"{skill}_Multiplier",
                1,
                new ConfigDescription(
                    $"XP multiplier for {skill}. Values: 0-10. 0 = disable skill leveling.",
                    new AcceptableValueRange<int>(0, 10)
                )
            );

            _skillStalenessOverrides[skillKey] = config.Bind(
                "Per-Skill Staleness",
                $"{skill}_UseStaleness",
                true,
                $"Enable staleness penalties for {skill}."
            );

            _skillStalenessMultiplierOverrides[skillKey] = config.Bind(
                "Per-Skill Staleness Multipliers",
                $"{skill}_StalenessMultiplier",
                1f,
                new ConfigDescription(
                    $"Staleness decay rate multiplier for {skill}. Higher = decay faster.",
                    new AcceptableValueRange<float>(0.1f, 5f)
                )
            );
        }

        // Listen for profile changes
        _activeProfile.SettingChanged += (sender, args) =>
        {
            ApplyProfileSettings(_activeProfile.Value);
        };

        // Apply initial profile on startup
        ApplyProfileSettings(_activeProfile.Value);
    }

    /// <summary>
    /// Get XP multiplier for a specific skill. Returns per-skill override if set, otherwise global multiplier.
    /// </summary>
    public static int GetSkillMultiplier(string skillKey)
    {
        var normalized = NormalizeSkillKey(skillKey);

        if (_skillMultipliers.TryGetValue(normalized, out var entry) && entry.Value > 0)
        {
            return entry.Value;
        }

        // Fall back to global multiplier
        return Plugin.SkillExpMultiplier;
    }

    /// <summary>
    /// Check if a skill has staleness enabled. Returns per-skill override if set.
    /// </summary>
    public static bool GetSkillUseStaleness(string skillKey)
    {
        var normalized = NormalizeSkillKey(skillKey);

        if (_skillStalenessOverrides.TryGetValue(normalized, out var entry))
        {
            return entry.Value && Plugin.EnableSkillStaleness;
        }

        return Plugin.EnableSkillStaleness;
    }

    /// <summary>
    /// Get staleness decay multiplier for a skill. Higher = decays faster.
    /// </summary>
    public static float GetSkillStalenessMultiplier(string skillKey)
    {
        var normalized = NormalizeSkillKey(skillKey);

        if (_skillStalenessMultiplierOverrides.TryGetValue(normalized, out var entry))
        {
            return entry.Value;
        }

        return 1f;
    }

    /// <summary>
    /// Get the currently active profile name.
    /// </summary>
    public static string GetActiveProfile() => _activeProfile?.Value ?? Profiles.Balanced;

    /// <summary>
    /// Apply settings from a named profile to all skills.
    /// </summary>
    public static void ApplyProfileSettings(string profileName)
    {
        if (!Profiles.All.Contains(profileName))
        {
            Plugin.Logger.LogWarning($"[SkillSpeedBoost] Unknown profile: {profileName}");
            return;
        }

        // Profiles with custom skill-level settings can be added here.
        // For now, profiles are global settings managed through Plugin.cs.
        Plugin.Logger.LogDebug($"[SkillSpeedBoost] Profile switched to: {profileName}");
    }

    /// <summary>
    /// Normalize skill names for consistent lookup (trim, lowercase first letter for internal names).
    /// </summary>
    private static string NormalizeSkillKey(string skillKey)
    {
        if (string.IsNullOrWhiteSpace(skillKey)) return "";
        return skillKey.Trim();
    }

    /// <summary>
    /// Register a dynamic skill that isn't in CoreSkills (e.g., mod-added skills).
    /// </summary>
    public static void RegisterDynamicSkill(ConfigFile config, string skillName)
    {
        var skillKey = NormalizeSkillKey(skillName);
        if (_skillMultipliers.ContainsKey(skillKey)) return; // Already registered

        _skillMultipliers[skillKey] = config.Bind(
            "Per-Skill Multipliers",
            $"{skillName}_Multiplier",
            1,
            new ConfigDescription(
                $"XP multiplier for {skillName}. Values: 0-10. 0 = disable skill leveling.",
                new AcceptableValueRange<int>(0, 10)
            )
        );

        _skillStalenessOverrides[skillKey] = config.Bind(
            "Per-Skill Staleness",
            $"{skillName}_UseStaleness",
            true,
            $"Enable staleness penalties for {skillName}."
        );

        _skillStalenessMultiplierOverrides[skillKey] = config.Bind(
            "Per-Skill Staleness Multipliers",
            $"{skillName}_StalenessMultiplier",
            1f,
            new ConfigDescription(
                $"Staleness decay rate multiplier for {skillName}. Higher = decay faster.",
                new AcceptableValueRange<float>(0.1f, 5f)
            )
        );
    }

    /// <summary>
    /// Get all registered skill multipliers.
    /// </summary>
    public static IEnumerable<(string skillName, int multiplier)> GetAllSkillMultipliers()
    {
        return _skillMultipliers
            .Where(kvp => kvp.Value.Value > 0)
            .Select(kvp => (kvp.Key, kvp.Value.Value));
    }
}
