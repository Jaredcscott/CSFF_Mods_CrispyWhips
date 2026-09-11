using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace Skill_Speed_Boost;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency("crispywhips.CSFFModFramework", BepInDependency.DependencyFlags.SoftDependency)]
internal class Plugin : BaseUnityPlugin
{
    private const string PluginGuid = "crispywhips.skill_speed_boost";
    public const string PluginName = "Skill Speed Boost";
    public const string PluginVersion = "1.10.2";

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
    internal static float AreaFamiliarityMinBonus => _areaFamiliarityMinBonus?.Value ?? 0f;
    internal static int AreaFamiliarityVisitsForMaxBonus => _areaFamiliarityVisitsForMaxBonus?.Value ?? 80;
    internal static string ActiveProfile => _activeProfile?.Value ?? "None";
    internal static string AppliedProfile => _appliedProfile?.Value ?? "";
    internal static bool SkillSynergiesEnabled => _skillSynergiesEnabled?.Value ?? false;
    internal static bool SkillSynergiesDebugLog => _skillSynergiesDebugLog?.Value ?? false;
    internal static bool LevelScalingEnabled => _levelScalingEnabled?.Value ?? false;
    internal static float LevelScalingMaxBonus => _levelScalingMaxBonus?.Value ?? 0.50f;
    internal static string LevelScalingCurve => _levelScalingCurve?.Value ?? "Linear";
    internal static float MaxComposedMultiplier => _maxComposedMultiplier?.Value ?? 0f;
    internal static bool LogMultiplierCapHits => _logMultiplierCapHits?.Value ?? false;
    internal static bool DailyFirstUseBonusEnabled => _dailyFirstUseBonusEnabled?.Value ?? false;
    internal static float DailyFirstUseMultiplier => _dailyFirstUseMultiplier?.Value ?? 1.5f;
    internal static bool WellRestedBonusEnabled => _wellRestedBonusEnabled?.Value ?? false;
    internal static float WellRestedMultiplier => _wellRestedMultiplier?.Value ?? 1.25f;
    internal static float WellRestedThreshold => _wellRestedThreshold?.Value ?? 60f;
    internal static bool LowConditionPenaltyEnabled => _lowConditionPenaltyEnabled?.Value ?? false;
    internal static float LowConditionThreshold => _lowConditionThreshold?.Value ?? 60f;
    internal static float LowConditionMultiplier => _lowConditionMultiplier?.Value ?? 0.5f;
    internal static bool LogEffectiveSettings => _logEffectiveSettings?.Value ?? false;

    private static ConfigEntry<bool> _enableSkillStaleness;
    private static ConfigEntry<int> _skillExpMultiplier;
    private static ConfigEntry<bool> _enablePerSkillMultipliers;
    private static ConfigEntry<bool> _morningBonusEnabled;
    private static ConfigEntry<float> _morningBonusMultiplier;
    private static ConfigEntry<float> _morningStartHour;
    private static ConfigEntry<float> _morningEndHour;
    private static ConfigEntry<bool> _areaFamiliarityEnabled;
    private static ConfigEntry<float> _areaFamiliarityMaxBonus;
    private static ConfigEntry<float> _areaFamiliarityMinBonus;
    private static ConfigEntry<int> _areaFamiliarityVisitsForMaxBonus;
    private static ConfigEntry<string> _activeProfile;
    private static ConfigEntry<string> _appliedProfile;
    private static ConfigEntry<bool> _skillSynergiesEnabled;
    private static ConfigEntry<bool> _skillSynergiesDebugLog;
    private static ConfigEntry<bool> _levelScalingEnabled;
    private static ConfigEntry<float> _levelScalingMaxBonus;
    private static ConfigEntry<string> _levelScalingCurve;
    private static ConfigEntry<float> _maxComposedMultiplier;
    private static ConfigEntry<bool> _logMultiplierCapHits;
    private static ConfigEntry<bool> _dailyFirstUseBonusEnabled;
    private static ConfigEntry<float> _dailyFirstUseMultiplier;
    private static ConfigEntry<bool> _wellRestedBonusEnabled;
    private static ConfigEntry<float> _wellRestedMultiplier;
    private static ConfigEntry<float> _wellRestedThreshold;
    private static ConfigEntry<bool> _lowConditionPenaltyEnabled;
    private static ConfigEntry<float> _lowConditionThreshold;
    private static ConfigEntry<float> _lowConditionMultiplier;
    private static ConfigEntry<bool> _logEffectiveSettings;
    private static Harmony _harmony;

    internal static void SetGlobalExpMultiplier(int value)
    {
        if (_skillExpMultiplier != null)
            _skillExpMultiplier.Value = System.Math.Max(1, System.Math.Min(10, value));
    }

    internal static void SetEnableSkillStaleness(bool value)
    {
        if (_enableSkillStaleness != null)
            _enableSkillStaleness.Value = value;
    }

    // Profile write-throughs for the post-v1.7 feature toggles. Without these a profile
    // could only move ExpMultiplier and Staleness, so "one key sets everything" was only
    // ever half true (see Audit_Remediation_Plan N3).
    internal static void SetAreaFamiliarityEnabled(bool value)
    {
        if (_areaFamiliarityEnabled != null) _areaFamiliarityEnabled.Value = value;
    }

    internal static void SetLevelScalingEnabled(bool value)
    {
        if (_levelScalingEnabled != null) _levelScalingEnabled.Value = value;
    }

    internal static void SetMorningBonusEnabled(bool value)
    {
        if (_morningBonusEnabled != null) _morningBonusEnabled.Value = value;
    }

    internal static void SetSkillSynergiesEnabled(bool value)
    {
        if (_skillSynergiesEnabled != null) _skillSynergiesEnabled.Value = value;
    }

    internal static void SetDailyFirstUseBonusEnabled(bool value)
    {
        if (_dailyFirstUseBonusEnabled != null) _dailyFirstUseBonusEnabled.Value = value;
    }

    internal static void SetWellRestedBonusEnabled(bool value)
    {
        if (_wellRestedBonusEnabled != null) _wellRestedBonusEnabled.Value = value;
    }

    internal static void SetLowConditionPenaltyEnabled(bool value)
    {
        if (_lowConditionPenaltyEnabled != null) _lowConditionPenaltyEnabled.Value = value;
    }

    /// <summary>
    /// Applies <see cref="ActiveProfile"/>, but at startup only when it differs from the preset
    /// already written into the config file (<see cref="AppliedProfile"/>).
    ///
    /// A preset writes eight keys. Re-asserting it on every launch therefore silently reverted any
    /// individual toggle the player changed afterwards, which is the opposite of what README and
    /// FEATURES both tell them to do (audit M12). <paramref name="force"/> is true only for an
    /// interactive change of ActiveProfile, where applying it IS the request.
    /// </summary>
    private static void ApplyProfileOnce(bool force)
    {
        string profile = ActiveProfile;

        if (profile == "None")
        {
            // "None" means stop managing these keys. Drop the latch too, so choosing the same
            // preset again later reads as a change and applies, rather than as already-applied.
            if (_appliedProfile != null && _appliedProfile.Value.Length > 0)
                _appliedProfile.Value = "";
            return;
        }

        if (!force && string.Equals(AppliedProfile, profile, StringComparison.OrdinalIgnoreCase))
        {
            Logger.LogDebug(
                $"[DifficultyProfiles] '{profile}' is already written into this config file; leaving " +
                "individual settings alone. Clear AppliedProfile to apply it fresh.");
            return;
        }

        if (DifficultyProfiles.ApplyProfileSettings(profile) && _appliedProfile != null)
            _appliedProfile.Value = profile;
    }

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
                "Start of morning window, matching the in-game clock (0 = midnight, 12 = noon). Default 5 = early morning.",
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

        _areaFamiliarityMinBonus = Config.Bind(
            "AreaFamiliarity",
            "AreaFamiliarityMinBonus",
            0f,
            new ConfigDescription(
                "Starting familiarity bonus at a location you have never worked before. 0 (default) reproduces the original behaviour exactly: a brand-new location gives no bonus. " +
                "0.10 = every location starts at +10% and still ramps to AreaFamiliarityMaxBonus with use. Clamped to AreaFamiliarityMaxBonus so the ramp can never run backwards.",
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

        _activeProfile = Config.Bind(
            "DifficultyProfiles",
            "ActiveProfile",
            "None",
            new ConfigDescription(
                "Apply a named difficulty preset. A preset sets the WHOLE situational stack: ExpMultiplier, Staleness, AreaFamiliarity, LevelScaling, MorningBonus, SkillSynergies, DailyFirstUse, WellRested and LowConditionPenalty. " +
                "It does NOT touch EnablePerSkillMultipliers or any <Skill>_Multiplier: your per-skill overrides survive every preset, including the ones that advertise vanilla rates. 'None' = use individual settings. " +
                "Options: None, VanillaPlus (2x+staleness, familiarity only), Casual (3x, no staleness, every situational bonus on), Hardcore (1x+staleness, every situational bonus off and the low-condition XP penalty ON), " +
                "Grinder (10x, no staleness, every situational bonus on), Balanced (2x+staleness, familiarity+level scaling), Legacy (1x+staleness, every situational bonus off and the low-condition XP penalty ON), " +
                "Immersive (1x+staleness, no raw multiplier - only the diegetic bonuses: familiarity, morning, daily first use and well-rested - plus the low-condition XP penalty). " +
                "Selecting a preset OVERWRITES those nine settings in this file ONCE, the next time the game starts (or immediately if you change it while running). After that it leaves them alone, " +
                "so an individual key you edit afterwards survives - see AppliedProfile below. ExpMultiplier and the toggles apply immediately; the staleness change takes effect after reloading a save.",
                new AcceptableValueList<string>(new[] { "None" }.Concat(DifficultyProfiles.ProfileNames).ToArray())
            )
        );

        _appliedProfile = Config.Bind(
            "DifficultyProfiles",
            "AppliedProfile",
            "",
            "Bookkeeping - the preset whose values were last written into this file. The mod compares it to ActiveProfile at startup and only re-applies the preset when the two differ, " +
            "so editing an individual setting after choosing a preset is no longer undone on the next launch. Clear this (leave it empty) to force ActiveProfile to be applied fresh next start."
        );

        _skillSynergiesEnabled = Config.Bind(
            "SkillSynergies",
            "EnableSkillSynergies",
            false,
            "When true, performing related skills in sequence stacks a bonus: +10% XP per consecutive related action, capped at +50% (5-action combo). Combo resets after 5 real-time minutes of inactivity. Default: false."
        );

        _skillSynergiesDebugLog = Config.Bind(
            "SkillSynergies",
            "SkillSynergiesDebugLog",
            false,
            "When true, logs synergy bonus and combo state to LogOutput.log each time a synergy bonus applies. Useful for verifying combo groups. Default: false."
        );

        _levelScalingEnabled = Config.Bind(
            "LevelScaling",
            "LevelScalingEnabled",
            false,
            "When true, XP gains scale up as a skill approaches its maximum level. Compensates for increasing XP-per-level costs at higher levels. Default: false."
        );

        _levelScalingMaxBonus = Config.Bind(
            "LevelScaling",
            "LevelScalingMaxBonus",
            0.50f,
            new ConfigDescription(
                "Maximum extra XP multiplier at max skill level. 0.50 = +50% bonus when fully maxed. Scales linearly from 0% at level 0 to this value at max level. Stacks with all other multipliers.",
                new AcceptableValueRange<float>(0f, 3f)
            )
        );

        _levelScalingCurve = Config.Bind(
            "LevelScaling",
            "LevelScalingCurve",
            "Linear",
            new ConfigDescription(
                "Shape of the level-scaling ramp between 0% and LevelScalingMaxBonus. " +
                "Linear (default) = the bonus grows evenly with skill level. " +
                "EaseIn = slow at first and steep near the top, which rewards grinding a skill toward its maximum. " +
                "EaseOut = steep early and flat near the top, which front-loads help onto low-level skills.",
                new AcceptableValueList<string>("Linear", "EaseIn", "EaseOut")
            )
        );

        _maxComposedMultiplier = Config.Bind(
            "Experience",
            "MaxComposedMultiplier",
            0f,
            new ConfigDescription(
                "Ceiling on the COMBINED multiplier after every bonus has stacked (global/per-skill x morning x familiarity x synergy x level scaling x first-use x well-rested). " +
                "0 (default) = uncapped, exactly as before. 5 = never award more than 5x, no matter how many bonuses line up at once.",
                new AcceptableValueRange<float>(0f, 50f)
            )
        );

        _logMultiplierCapHits = Config.Bind(
            "Experience",
            "LogMultiplierCapHits",
            false,
            "When true, logs a line each time MaxComposedMultiplier actually clamps a gain, showing the uncapped value. Useful for choosing a cap. Ignored while MaxComposedMultiplier is 0. Default: false."
        );

        _dailyFirstUseBonusEnabled = Config.Bind(
            "DailyFirstUse",
            "DailyFirstUseBonusEnabled",
            false,
            "When true, the first XP gain for each skill on each in-game day is multiplied by DailyFirstUseMultiplier. Rewards practising broadly over grinding one skill. Default: false."
        );

        _dailyFirstUseMultiplier = Config.Bind(
            "DailyFirstUse",
            "DailyFirstUseMultiplier",
            1.5f,
            new ConfigDescription(
                "Multiplier for a skill's first XP gain of the in-game day. 1.5 = 50% bonus on that one gain. Stacks with every other multiplier.",
                new AcceptableValueRange<float>(1f, 3f)
            )
        );

        _wellRestedBonusEnabled = Config.Bind(
            "WellRested",
            "WellRestedBonusEnabled",
            false,
            "When true, skill XP is multiplied by WellRestedMultiplier while Energy, Satiation AND Hydration are all at or above WellRestedThreshold percent of their maximum. Default: false."
        );

        _wellRestedMultiplier = Config.Bind(
            "WellRested",
            "WellRestedMultiplier",
            1.25f,
            new ConfigDescription(
                "XP multiplier applied while rested, fed and watered. 1.25 = 25% bonus. Stacks with every other multiplier.",
                new AcceptableValueRange<float>(1f, 3f)
            )
        );

        _wellRestedThreshold = Config.Bind(
            "WellRested",
            "WellRestedThreshold",
            60f,
            new ConfigDescription(
                "Percent of maximum that Energy, Satiation and Hydration must EACH reach for the well-rested bonus to apply. 60 = all three at 60% or better.",
                new AcceptableValueRange<float>(0f, 100f)
            )
        );

        _lowConditionPenaltyEnabled = Config.Bind(
            "LowCondition",
            "LowConditionPenaltyEnabled",
            false,
            "When true, skill XP is multiplied by LowConditionMultiplier while ANY of Energy, Satiation or Hydration is BELOW LowConditionThreshold percent of its maximum. The inverse of the well-rested bonus: a penalty for working while exhausted, starving or parched, rather than a reward for being in good shape. Default: false."
        );

        _lowConditionThreshold = Config.Bind(
            "LowCondition",
            "LowConditionThreshold",
            60f,
            new ConfigDescription(
                "Percent of maximum below which a condition stat counts as low. 60 = the penalty applies as soon as Energy, Satiation or Hydration drops under 60% of its maximum. Left at the same default as WellRestedThreshold the two features are exact opposites with no gap between them.",
                new AcceptableValueRange<float>(0f, 100f)
            )
        );

        _lowConditionMultiplier = Config.Bind(
            "LowCondition",
            "LowConditionMultiplier",
            0.5f,
            new ConfigDescription(
                "XP multiplier applied while a condition stat is low. 0.5 = half XP; 1.0 = no penalty (the feature does nothing); 0 = no XP at all while a stat is low. Stacks with every other multiplier, so it can cancel out a raw multiplier rather than always reducing what you gain.",
                new AcceptableValueRange<float>(0f, 1f)
            )
        );

        _logEffectiveSettings = Config.Bind(
            "Diagnostics",
            "LogEffectiveSettings",
            false,
            "When true, writes one line per skill to LogOutput.log after game data loads, showing the settings that actually resolved for that skill (XP multiplier and where it came from, staleness on/off, decay rate) plus a summary of the global toggles. Turn this on first when a setting does not seem to be taking effect. Default: false."
        );

        // Initialize area familiarity persistence (loads counters from disk)
        Patcher.AreaFamiliarityService.Initialize();

        // Initialize skill config manager for per-skill multipliers and profiles
        SkillConfigManager.Initialize(Config);

        // Runtime hot-reload of deep stat graph proved unstable; apply on next load.
        _skillExpMultiplier.SettingChanged += (sender, args) =>
        {
            Logger.LogDebug($"Global SkillExpMultiplier changed to {SkillExpMultiplier}x. New value applies after loading a save or restarting the game.");
        };

        // Changing the preset at runtime always applies it: that is an explicit request.
        _activeProfile.SettingChanged += (sender, args) => ApplyProfileOnce(force: true);

        // At startup, apply only when the chosen preset is not the one already written into this
        // config file. A preset writes EIGHT keys, so re-asserting it unconditionally every launch
        // silently reverted any individual toggle the player edited afterwards - while README and
        // FEATURES both told them to make exactly that edit (audit M12). The AppliedProfile latch
        // makes the write one-shot per profile change, which is what both docs already promise.
        ApplyProfileOnce(force: false);

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
