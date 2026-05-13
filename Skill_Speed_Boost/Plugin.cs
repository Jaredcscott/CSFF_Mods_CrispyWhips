using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace Skill_Speed_Boost;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
internal class Plugin : BaseUnityPlugin
{
    private const string PluginGuid = "crispywhips.skill_speed_boost";
    public const string PluginName = "Skill Speed Boost";
    public const string PluginVersion = "1.7.4";

    internal static Plugin Instance { get; private set; }
    internal new static ManualLogSource Logger;
    internal static bool EnableSkillStaleness => _enableSkillStaleness?.Value ?? true;
    internal static int SkillExpMultiplier => _skillExpMultiplier?.Value ?? 1;
    internal static bool EnablePerSkillMultipliers => _enablePerSkillMultipliers?.Value ?? true;
    internal static bool MorningBonusEnabled => _morningBonusEnabled?.Value ?? false;
    internal static float MorningBonusMultiplier => _morningBonusMultiplier?.Value ?? 1.5f;
    internal static float MorningStartHour => _morningStartHour?.Value ?? 5f;
    internal static float MorningEndHour => _morningEndHour?.Value ?? 9f;
    internal static bool AreaFamiliarityEnabled => _areaFamiliarityEnabled?.Value ?? true;
    internal static float AreaFamiliarityMaxBonus => _areaFamiliarityMaxBonus?.Value ?? 0.30f;
    internal static int AreaFamiliarityVisitsForMaxBonus => _areaFamiliarityVisitsForMaxBonus?.Value ?? 80;

    private static ConfigEntry<bool> _enableSkillStaleness;
    private static ConfigEntry<int> _skillExpMultiplier;
    private static ConfigEntry<bool> _enablePerSkillMultipliers;
    private static ConfigEntry<bool> _morningBonusEnabled;
    private static ConfigEntry<float> _morningBonusMultiplier;
    private static ConfigEntry<float> _morningStartHour;
    private static ConfigEntry<float> _morningEndHour;
    private static ConfigEntry<bool> _areaFamiliarityEnabled;
    private static ConfigEntry<float> _areaFamiliarityMaxBonus;
    private static ConfigEntry<int> _areaFamiliarityVisitsForMaxBonus;
    private static Harmony _harmony;

    private void Awake()
    {
        Instance = this;
        Logger = base.Logger;

        _enableSkillStaleness = Config.Bind(
            "Staleness",
            "EnableSkillStaleness",
            true,
            "Default true. When enabled, skills show staleness novelty penalties. When disabled, no staleness penalties apply."
        );

        _skillExpMultiplier = Config.Bind(
            "Experience",
            "SkillExpMultiplier",
            1,
            new ConfigDescription(
                "Default 1. Global XP multiplier for all skills (unless per-skill override is set). Allowed values: 1-10. IMPORTANT: Changes apply after loading a save or restarting the game.",
                new AcceptableValueList<int>(1, 2, 3, 4, 5, 6, 7, 8, 9, 10)
            )
        );

        _enablePerSkillMultipliers = Config.Bind(
            "Experience",
            "EnablePerSkillMultipliers",
            true,
            "Enable per-skill XP multiplier customization. When enabled, individual skills can have different multipliers."
        );

        _morningBonusEnabled = Config.Bind(
            "MorningBonus",
            "MorningBonusEnabled",
            false,
            "When enabled, skill XP gains are multiplied during the morning window. Default: false."
        );

        _morningBonusMultiplier = Config.Bind(
            "MorningBonus",
            "MorningBonusMultiplier",
            1.5f,
            new ConfigDescription(
                "XP multiplier applied during morning hours. 1.5 = 50% bonus. Stacks with global/per-skill multipliers.",
                new AcceptableValueRange<float>(1f, 5f)
            )
        );

        _morningStartHour = Config.Bind(
            "MorningBonus",
            "MorningStartHour",
            5f,
            new ConfigDescription(
                "Start of morning window in game-hours (0 = day start, 12 = midday). Default 5 = early morning.",
                new AcceptableValueRange<float>(0f, 23f)
            )
        );

        _morningEndHour = Config.Bind(
            "MorningBonus",
            "MorningEndHour",
            9f,
            new ConfigDescription(
                "End of morning window in game-hours (exclusive). Default 9 = end of morning.",
                new AcceptableValueRange<float>(0f, 23f)
            )
        );

        _areaFamiliarityEnabled = Config.Bind(
            "AreaFamiliarity",
            "AreaFamiliarityEnabled",
            true,
            "Default true. Skill XP gained while interacting with a location grows by up to AreaFamiliarityMaxBonus the more you visit that location. Stacks with global/per-skill/morning multipliers."
        );

        _areaFamiliarityMaxBonus = Config.Bind(
            "AreaFamiliarity",
            "AreaFamiliarityMaxBonus",
            0.30f,
            new ConfigDescription(
                "Maximum extra XP at full familiarity. 0.30 = +30% on top of base XP. Range 0–2.0 (capped at 200%).",
                new AcceptableValueRange<float>(0f, 2f)
            )
        );

        _areaFamiliarityVisitsForMaxBonus = Config.Bind(
            "AreaFamiliarity",
            "AreaFamiliarityVisitsForMaxBonus",
            80,
            new ConfigDescription(
                "Number of XP-granting actions at a single location to reach the max bonus. Bonus scales linearly from 0 to this count.",
                new AcceptableValueRange<int>(1, 1000)
            )
        );

        // Initialize area familiarity persistence (loads counters from disk)
        Patcher.AreaFamiliarityService.Initialize();

        // Initialize skill config manager for per-skill multipliers and profiles
        SkillConfigManager.Initialize(Config);

        // Runtime hot-reload of deep stat graph proved unstable; apply on next load.
        _skillExpMultiplier.SettingChanged += (sender, args) =>
        {
            Logger.LogInfo($"Global SkillExpMultiplier changed to {SkillExpMultiplier}x. New value applies after loading a save or restarting the game.");
        };

        Logger.LogDebug(
            $"{PluginName} v{PluginVersion}: " +
            $"Staleness={EnableSkillStaleness}, " +
            $"GlobalExpMultiplier={SkillExpMultiplier}x, " +
            $"PerSkillMultipliers={EnablePerSkillMultipliers}"
        );

        if (EnablePerSkillMultipliers)
        {
            foreach (var (skillName, multiplier) in SkillConfigManager.GetAllSkillMultipliers())
            {
                if (multiplier != 1)
                    Logger.LogDebug($"[PerSkill] {skillName} = {multiplier}x");
            }
        }

        _harmony = new Harmony(PluginGuid);
        try
        {
            Patcher.GameLoadPatch.ApplyPatch(_harmony);
            Patcher.MorningBonusPatch.ApplyPatch(_harmony);
            Patcher.AreaFamiliarityPatch.ApplyPatch(_harmony);
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Failed to apply patches: {ex}");
        }

        Logger.LogInfo($"{PluginName} v{PluginVersion} loaded.");
    }

    private void OnApplicationQuit()
    {
        Patcher.AreaFamiliarityService.Save(forceWrite: true);
    }

    private void OnDestroy()
    {
        Patcher.AreaFamiliarityService.Save(forceWrite: true);
        _harmony?.UnpatchSelf();
    }
}
