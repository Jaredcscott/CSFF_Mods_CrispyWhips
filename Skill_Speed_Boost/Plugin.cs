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
    public const string PluginVersion = "1.11.2";

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
    internal static bool SynergyRequireDifferentSkill => _synergyRequireDifferentSkill?.Value ?? false;
    internal static float SynergyBonusPerAction => _synergyBonusPerAction?.Value ?? 0.1f;
    internal static int SynergyMaxCombo => _synergyMaxCombo?.Value ?? 5;
    internal static float SynergyComboTimeoutMinutes => _synergyComboTimeoutMinutes?.Value ?? 5f;
    // Parsed from SynergyTimeoutUnit when it is bound and on every change, never per XP gain.
    internal static bool SynergyUsesGameMinutes { get; private set; }
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
    internal static bool LogSkillXpGains => _logSkillXpGains?.Value ?? false;
    internal static bool LogDailyXpSummary => _logDailyXpSummary?.Value ?? false;
    internal static bool NightBonusEnabled => _nightBonusEnabled?.Value ?? false;
    internal static float NightBonusMultiplier => _nightBonusMultiplier?.Value ?? 1.5f;
    internal static float NightStartHour => _nightStartHour?.Value ?? 22f;
    internal static float NightEndHour => _nightEndHour?.Value ?? 2f;
    // MorningBonusSkills and NightBonusSkills (plan D1): which skills each window applies to. Not
    // bonuses of their own, so no toggle, no setter and no guard term: each lookup only ever narrows
    // the morning or night term it sits beside, and an empty list (the default) means every skill.
    internal static bool MorningAppliesTo(string skillName) => SkillListConfig.AppliesTo(_morningSkills, skillName);
    internal static bool NightAppliesTo(string skillName) => SkillListConfig.AppliesTo(_nightSkills, skillName);
    internal static string MorningSkillsDescription => SkillListConfig.Describe(_morningSkills);
    internal static string NightSkillsDescription => SkillListConfig.Describe(_nightSkills);
    // An empty map IS "off": the station bonus has no separate toggle to forget.
    internal static bool StationBonusEnabled => _stationBonuses.Count > 0;
    internal static int StationBonusCount => _stationBonuses.Count;
    internal static bool AreaFamiliarityDecayEnabled => _areaFamiliarityDecayEnabled?.Value ?? false;
    internal static int AreaFamiliarityDecayAfterDays => _areaFamiliarityDecayAfterDays?.Value ?? 14;
    internal static float AreaFamiliarityDecayPerDay => _areaFamiliarityDecayPerDay?.Value ?? 1f;
    internal static bool StreakBonusEnabled => _streakBonusEnabled?.Value ?? false;
    internal static float StreakBonusPerDay => _streakBonusPerDay?.Value ?? 0.02f;
    internal static int StreakBonusMaxDays => _streakBonusMaxDays?.Value ?? 10;
    // Storage layout, not a bonus: read by ProgressionStore at each run start, never by the
    // composition point, and written by no preset (no Set* setter, plan D2).
    internal static bool PerSaveProgression => _perSaveProgression?.Value ?? false;
    internal static bool PerSaveResetOnNewGame => _perSaveResetOnNewGame?.Value ?? true;
    // An empty map IS "off", like StationBonusMap: no separate toggle, so no preset writes it.
    internal static bool SeasonBonusEnabled => _seasonBonuses.Count > 0;
    internal static int SeasonBonusCount => ScopedBonusConfig.CountEntries(_seasonBonuses);
    // The same empty-is-off shape for EnvironmentBonusMap.
    internal static bool EnvironmentBonusEnabled => _environmentBonuses.Count > 0;
    internal static int EnvironmentBonusCount => ScopedBonusConfig.CountEntries(_environmentBonuses);
    // And for ToolBonusMap and ActionBonusMap (plan C3).
    internal static bool ToolBonusEnabled => _toolBonuses.Count > 0;
    internal static int ToolBonusCount => ScopedBonusConfig.CountEntries(_toolBonuses);
    internal static bool ActionBonusEnabled => _actionBonuses.Count > 0;
    internal static int ActionBonusCount => ScopedBonusConfig.CountEntries(_actionBonuses);
    // PerkBonusMap (plan C4) ships a NON-empty default, so unlike the maps above it has a toggle of its own
    // (default false): an upgrade must not change anyone's rates. The count is the perks the map names.
    internal static bool PerkBonusEnabled => _perkBonusEnabled?.Value ?? false;
    internal static int PerkBonusCount => _perkBonuses.Count;
    // The In the Flow bonus (plan C5): a toggle and a magnitude, like the well-rested bonus.
    internal static bool FocusBonusEnabled => _focusBonusEnabled?.Value ?? false;
    internal static float FocusBonusMultiplier => _focusBonusMultiplier?.Value ?? 1.2f;
    // The rested-XP bank (plan D3): a toggle in the composition point; the three magnitudes are read
    // only by RestedBankService.
    internal static bool RestedBankEnabled => _restedBankEnabled?.Value ?? false;
    internal static int RestedBankGainsPerDay => _restedBankGainsPerDay?.Value ?? 3;
    internal static int RestedBankMaxGains => _restedBankMaxGains?.Value ?? 10;
    internal static float RestedBankMultiplier => _restedBankMultiplier?.Value ?? 2f;
    // The milestone burst (plan D4): a toggle in the composition point; the spacing and the burst are
    // read only by MilestoneService.
    internal static bool MilestoneBonusEnabled => _milestoneBonusEnabled?.Value ?? false;
    internal static int MilestoneEvery => _milestoneEvery?.Value ?? 25;
    internal static float MilestoneBonusXp => _milestoneBonusXp?.Value ?? 1f;
    // XP overflow (plan E1): a toggle in the composition point, which also reads the fraction behind it
    // and passes it to XpOverflowService.
    internal static bool XpOverflowEnabled => _xpOverflowEnabled?.Value ?? false;
    internal static float XpOverflowFraction => _xpOverflowFraction?.Value ?? 0.5f;
    // The XP readout toast (plan E3): a diagnostic like LogSkillXpGains, not a bonus, so no guard term and
    // no preset setter. ChangeStat_Post reads both below its guard (knownNonFlagReads in the composition gate).
    internal static bool XpReadoutEnabled => _xpReadoutEnabled?.Value ?? false;
    internal static float XpReadoutMinSeconds => _xpReadoutMinSeconds?.Value ?? 60f;
    // Partner skills follow your settings (plan E2): read by PartnerSkillPatch on a partner's stat gain and
    // by GameLoadPatch's staleness apply, never by ChangeStat_Post (a partner gain never reaches it), so it
    // has no guard term; no preset writes it.
    internal static bool PartnerSkillsFollowSettings => _partnerSkillsFollowSettings?.Value ?? false;

    /// <summary>The configured multiplier for a placed structure, or 1 when it is not listed.</summary>
    internal static float GetStationBonusMultiplier(string structureUid)
    {
        return !string.IsNullOrEmpty(structureUid) && _stationBonuses.TryGetValue(structureUid, out float multiplier)
            ? multiplier
            : 1f;
    }

    /// <summary>
    /// The SeasonBonusMap multiplier for a skill in the current season: its own entry, else the season's
    /// every-skill entry, else 1 (also 1 when the season cannot be read, which SeasonBonusService reports).
    /// </summary>
    internal static float GetSeasonBonusMultiplier(string skillName)
    {
        return ScopedBonusConfig.Lookup(_seasonBonuses, Patcher.SeasonBonusService.CurrentSeason(), skillName);
    }

    /// <summary>
    /// The EnvironmentBonusMap multiplier for a skill where the player is: the most specific matching
    /// place's entry for it, else that place's every-skill entry, else 1 (also 1 when the environment
    /// cannot be read, which EnvironmentBonusService reports).
    /// </summary>
    internal static float GetEnvironmentBonusMultiplier(string skillName)
    {
        return Patcher.EnvironmentBonusService.GetMultiplier(_environmentBonuses, skillName);
    }

    /// <summary>
    /// The ToolBonusMap multiplier for a skill gained in the running action: the highest entry for it
    /// among the tool's UniqueID and its tags, else 1 (also for an action with no tool).
    /// </summary>
    internal static float GetToolBonusMultiplier(string skillName)
    {
        return ScopedBonusConfig.LookupAny(_toolBonuses, Patcher.AreaFamiliarityPatch.CurrentGivenKeys, skillName);
    }

    /// <summary>
    /// The ActionBonusMap multiplier for a skill gained in the running action: the higher entry for it
    /// of the action's name and its localization key, else 1.
    /// </summary>
    internal static float GetActionBonusMultiplier(string skillName)
    {
        return ScopedBonusConfig.LookupAny(_actionBonuses,
            new[] { Patcher.AreaFamiliarityPatch.CurrentActionName, Patcher.AreaFamiliarityPatch.CurrentActionKey }, skillName);
    }

    /// <summary>
    /// The PerkBonusMap multiplier for a skill: the highest entry among the mapped perks the player holds,
    /// picked at character creation or granted during the run, else 1 (also 1 when the perks cannot be
    /// read, which PerkBonusService reports).
    /// </summary>
    internal static float GetPerkBonusMultiplier(string skillName)
    {
        return Patcher.PerkBonusService.GetMultiplier(_perkBonuses, skillName);
    }

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
    private static ConfigEntry<bool> _synergyRequireDifferentSkill;
    private static ConfigEntry<float> _synergyBonusPerAction;
    private static ConfigEntry<int> _synergyMaxCombo;
    private static ConfigEntry<float> _synergyComboTimeoutMinutes;
    private static ConfigEntry<string> _synergyTimeoutUnit;
    private static ConfigEntry<string> _synergyGroupOverrides;
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
    private static ConfigEntry<bool> _logSkillXpGains;
    private static ConfigEntry<bool> _logDailyXpSummary;
    private static ConfigEntry<bool> _nightBonusEnabled;
    private static ConfigEntry<float> _nightBonusMultiplier;
    private static ConfigEntry<float> _nightStartHour;
    private static ConfigEntry<float> _nightEndHour;
    private static ConfigEntry<string> _morningBonusSkills;
    private static ConfigEntry<string> _nightBonusSkills;
    private static ConfigEntry<string> _stationBonusMap;
    private static ConfigEntry<string> _seasonBonusMap;
    private static ConfigEntry<string> _environmentBonusMap;
    private static ConfigEntry<string> _toolBonusMap;
    private static ConfigEntry<string> _actionBonusMap;
    private static ConfigEntry<bool> _perkBonusEnabled;
    private static ConfigEntry<string> _perkBonusMap;
    private static ConfigEntry<bool> _focusBonusEnabled;
    private static ConfigEntry<float> _focusBonusMultiplier;
    private static ConfigEntry<bool> _restedBankEnabled;
    private static ConfigEntry<int> _restedBankGainsPerDay;
    private static ConfigEntry<int> _restedBankMaxGains;
    private static ConfigEntry<float> _restedBankMultiplier;
    private static ConfigEntry<bool> _milestoneBonusEnabled;
    private static ConfigEntry<int> _milestoneEvery;
    private static ConfigEntry<float> _milestoneBonusXp;
    private static ConfigEntry<bool> _xpOverflowEnabled;
    private static ConfigEntry<float> _xpOverflowFraction;
    private static ConfigEntry<bool> _xpReadoutEnabled;
    private static ConfigEntry<float> _xpReadoutMinSeconds;
    private static ConfigEntry<bool> _partnerSkillsFollowSettings;
    private static ConfigEntry<bool> _areaFamiliarityDecayEnabled;
    private static ConfigEntry<int> _areaFamiliarityDecayAfterDays;
    private static ConfigEntry<float> _areaFamiliarityDecayPerDay;
    private static ConfigEntry<bool> _streakBonusEnabled;
    private static ConfigEntry<float> _streakBonusPerDay;
    private static ConfigEntry<int> _streakBonusMaxDays;
    private static ConfigEntry<bool> _perSaveProgression;
    private static ConfigEntry<bool> _perSaveResetOnNewGame;
    private static Harmony _harmony;

    // Parsed StationBonusMap. Replaced whole on every re-parse, never edited in place, so the XP
    // postfix always reads one complete map.
    private static Dictionary<string, float> _stationBonuses = new(StringComparer.OrdinalIgnoreCase);

    // Parsed MorningBonusSkills and NightBonusSkills, each replaced whole on every re-read the same way.
    // Empty means every skill, so a missing or unread setting leaves both windows as 1.11.0 had them.
    private static HashSet<string> _morningSkills = new(StringComparer.OrdinalIgnoreCase);
    private static HashSet<string> _nightSkills = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> _warnedUnknownWindowSkills = new(StringComparer.OrdinalIgnoreCase);

    // Parsed SeasonBonusMap (season, then skill, then multiplier), replaced whole the same way.
    private static Dictionary<string, Dictionary<string, float>> _seasonBonuses = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> _warnedUnknownSeasonSkills = new(StringComparer.OrdinalIgnoreCase);

    // Parsed EnvironmentBonusMap (place, then skill, then multiplier), replaced whole the same way.
    private static Dictionary<string, Dictionary<string, float>> _environmentBonuses = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> _warnedUnknownEnvironmentSkills = new(StringComparer.OrdinalIgnoreCase);

    // Parsed ToolBonusMap (UniqueID or tag, then skill, then multiplier) and ActionBonusMap (action
    // name or key, then skill, then multiplier), each replaced whole the same way.
    private static Dictionary<string, Dictionary<string, float>> _toolBonuses = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, Dictionary<string, float>> _actionBonuses = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> _warnedUnknownToolSkills = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> _warnedUnknownActionSkills = new(StringComparer.OrdinalIgnoreCase);

    // Parsed PerkBonusMap (perk UniqueID, then skill, then multiplier), replaced whole the same way.
    private static Dictionary<string, Dictionary<string, float>> _perkBonuses = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> _warnedUnknownPerkSkills = new(StringComparer.OrdinalIgnoreCase);

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

    // A restriction on the synergy bonus rather than a bonus of its own. Every preset writes false: the
    // two presets the idea named (Immersive, Hardcore) have synergies off, so it would do nothing there.
    internal static void SetSynergyRequireDifferentSkill(bool value)
    {
        if (_synergyRequireDifferentSkill != null) _synergyRequireDifferentSkill.Value = value;
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

    internal static void SetNightBonusEnabled(bool value)
    {
        if (_nightBonusEnabled != null) _nightBonusEnabled.Value = value;
    }

    internal static void SetStreakBonusEnabled(bool value)
    {
        if (_streakBonusEnabled != null) _streakBonusEnabled.Value = value;
    }

    // The map ships non-empty, so this switch (not the map) is what a preset turns (plan C4).
    internal static void SetPerkBonusEnabled(bool value)
    {
        if (_perkBonusEnabled != null) _perkBonusEnabled.Value = value;
    }

    // ON in Casual, Grinder and Immersive (a focused mind learns faster is a diegetic bonus; plan C5).
    internal static void SetFocusBonusEnabled(bool value)
    {
        if (_focusBonusEnabled != null) _focusBonusEnabled.Value = value;
    }

    // ON in Casual and Grinder (every situational bonus on); OFF in Immersive, whose day streak rewards
    // practising every day, the opposite incentive, and in the rest (plan D3).
    internal static void SetRestedBankEnabled(bool value)
    {
        if (_restedBankEnabled != null) _restedBankEnabled.Value = value;
    }

    // ON in Casual and Grinder (every situational bonus on); off in the rest, Immersive included: a
    // flat burst at a round number is a game reward, not something the fiction explains (plan D4).
    internal static void SetMilestoneBonusEnabled(bool value)
    {
        if (_milestoneBonusEnabled != null) _milestoneBonusEnabled.Value = value;
    }

    // ON in Casual and Grinder (every situational bonus on); off in the rest, Immersive included: XP
    // jumping from a mastered skill to a neighbour is a game convenience, not something the fiction
    // explains (plan E1).
    internal static void SetXpOverflowEnabled(bool value)
    {
        if (_xpOverflowEnabled != null) _xpOverflowEnabled.Value = value;
    }

    /// <summary>
    /// Re-reads MorningBonusSkills and NightBonusSkills into <see cref="_morningSkills"/> and
    /// <see cref="_nightSkills"/>, each replaced whole (plan D1, S3). SkillListConfig.Parse cannot fail
    /// on any text, so the catch is only for the unexpected, and it falls back to every skill (the
    /// 1.11.0 window) rather than to none.
    /// </summary>
    private static void ReloadBonusWindowSkills()
    {
        try
        {
            _morningSkills = SkillListConfig.Parse(_morningBonusSkills?.Value);
            _nightSkills = SkillListConfig.Parse(_nightBonusSkills?.Value);
            if (_morningSkills.Count > 0 || _nightSkills.Count > 0)
                Logger.LogDebug($"[BonusWindows] morning applies to {SkillListConfig.Describe(_morningSkills)}, night to {SkillListConfig.Describe(_nightSkills)}.");
            WarnUnknownWindowSkills();
        }
        catch (Exception ex)
        {
            _morningSkills = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _nightSkills = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Logger.LogWarning($"[BonusWindows] MorningBonusSkills or NightBonusSkills could not be read, so both windows apply to every skill: {ex.Message}");
        }
    }

    /// <summary>
    /// Reports each MorningBonusSkills or NightBonusSkills name no loaded skill has, once per list: a
    /// typo in a list that names nothing real switches that window off for every skill. Called after
    /// the load scan (GameLoadPatch) and on every re-read, the way <see cref="WarnUnknownSeasonSkills"/> is.
    /// </summary>
    internal static void WarnUnknownWindowSkills()
    {
        if (!Patcher.GameLoadPatch.SkillScanDone) return;
        var loaded = Patcher.GameLoadPatch.SkillNamesByUniqueId.Values;
        foreach (string skill in SkillListConfig.UnknownSkills(_morningSkills, loaded))
        {
            if (!_warnedUnknownWindowSkills.Add("MorningBonusSkills:" + skill)) continue;
            Logger.LogWarning(
                $"[BonusWindows] MorningBonusSkills: '{skill}' is not a skill this game has loaded, so it gets no morning bonus. " +
                "Use the skill's name as the game shows it, for example Cooking or Herbalism.");
        }
        foreach (string skill in SkillListConfig.UnknownSkills(_nightSkills, loaded))
        {
            if (!_warnedUnknownWindowSkills.Add("NightBonusSkills:" + skill)) continue;
            Logger.LogWarning(
                $"[BonusWindows] NightBonusSkills: '{skill}' is not a skill this game has loaded, so it gets no night bonus. " +
                "Use the skill's name as the game shows it, for example Cooking or Herbalism.");
        }
    }

    /// <summary>
    /// Re-reads StationBonusMap into <see cref="_stationBonuses"/>. One warning per entry that could
    /// not be read (StationBonusConfig.Parse fails closed per entry), so a typo costs that entry and
    /// nothing else; the catch is only for a failure Parse itself does not anticipate.
    /// </summary>
    private static void ReloadStationBonusMap()
    {
        try
        {
            var problems = new List<string>();
            var parsed = StationBonusConfig.Parse(_stationBonusMap?.Value, problems);
            foreach (var problem in problems)
                Logger.LogWarning($"[StationBonus] StationBonusMap: {problem}");
            _stationBonuses = parsed;
            if (parsed.Count > 0)
                Logger.LogDebug($"[StationBonus] {parsed.Count} structure(s) listed in StationBonusMap.");
        }
        catch (Exception ex)
        {
            _stationBonuses = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            Logger.LogWarning($"[StationBonus] StationBonusMap could not be read, so no station bonus applies: {ex.Message}");
        }
    }

    /// <summary>
    /// Re-reads SeasonBonusMap into <see cref="_seasonBonuses"/>, the way <see cref="ReloadStationBonusMap"/>
    /// re-reads its map: one warning per entry that could not be read, and the catch only for a failure
    /// Parse itself does not anticipate. The ceiling is the station map's, so every map reads alike.
    /// </summary>
    private static void ReloadSeasonBonusMap()
    {
        try
        {
            var problems = new List<string>();
            var parsed = ScopedBonusConfig.ParseSeasonMap(_seasonBonusMap?.Value, StationBonusConfig.MaxMultiplier, problems);
            foreach (var problem in problems)
                Logger.LogWarning($"[SeasonBonus] SeasonBonusMap: {problem}");
            _seasonBonuses = parsed;
            if (parsed.Count > 0)
                Logger.LogDebug($"[SeasonBonus] {ScopedBonusConfig.CountEntries(parsed)} entr(ies) listed in SeasonBonusMap.");
            WarnUnknownSeasonSkills();
        }
        catch (Exception ex)
        {
            _seasonBonuses = new Dictionary<string, Dictionary<string, float>>(StringComparer.OrdinalIgnoreCase);
            Logger.LogWarning($"[SeasonBonus] SeasonBonusMap could not be read, so no season bonus applies: {ex.Message}");
        }
    }

    /// <summary>
    /// Reports each SeasonBonusMap skill name no loaded skill has, once: its entries never match a gain.
    /// Called after the load scan (GameLoadPatch) and on every re-read; before the scan every skill
    /// would look unknown, so it waits for it.
    /// </summary>
    internal static void WarnUnknownSeasonSkills()
    {
        if (!Patcher.GameLoadPatch.SkillScanDone) return;
        foreach (string skill in ScopedBonusConfig.UnknownSkills(_seasonBonuses, Patcher.GameLoadPatch.SkillNamesByUniqueId.Values))
        {
            if (!_warnedUnknownSeasonSkills.Add(skill)) continue;
            Logger.LogWarning(
                $"[SeasonBonus] SeasonBonusMap: '{skill}' is not a skill this game has loaded, so its entries do nothing. " +
                "Use the skill's name as the game shows it, for example Herbalism or Woodworking.");
        }
    }

    /// <summary>
    /// Re-reads EnvironmentBonusMap into <see cref="_environmentBonuses"/>, the way
    /// <see cref="ReloadSeasonBonusMap"/> re-reads its map, with the same ceiling.
    /// </summary>
    private static void ReloadEnvironmentBonusMap()
    {
        try
        {
            var problems = new List<string>();
            var parsed = ScopedBonusConfig.ParseEnvironmentMap(_environmentBonusMap?.Value, StationBonusConfig.MaxMultiplier, problems);
            foreach (var problem in problems)
                Logger.LogWarning($"[EnvironmentBonus] EnvironmentBonusMap: {problem}");
            _environmentBonuses = parsed;
            if (parsed.Count > 0)
                Logger.LogDebug($"[EnvironmentBonus] {ScopedBonusConfig.CountEntries(parsed)} entr(ies) listed in EnvironmentBonusMap.");
            WarnUnknownEnvironmentSkills();
        }
        catch (Exception ex)
        {
            _environmentBonuses = new Dictionary<string, Dictionary<string, float>>(StringComparer.OrdinalIgnoreCase);
            Logger.LogWarning($"[EnvironmentBonus] EnvironmentBonusMap could not be read, so no environment bonus applies: {ex.Message}");
        }
    }

    /// <summary>
    /// Reports each EnvironmentBonusMap skill name no loaded skill has, once, after the load scan, the
    /// way <see cref="WarnUnknownSeasonSkills"/> does.
    /// </summary>
    internal static void WarnUnknownEnvironmentSkills()
    {
        if (!Patcher.GameLoadPatch.SkillScanDone) return;
        foreach (string skill in ScopedBonusConfig.UnknownSkills(_environmentBonuses, Patcher.GameLoadPatch.SkillNamesByUniqueId.Values))
        {
            if (!_warnedUnknownEnvironmentSkills.Add(skill)) continue;
            Logger.LogWarning(
                $"[EnvironmentBonus] EnvironmentBonusMap: '{skill}' is not a skill this game has loaded, so its entries do nothing. " +
                "Use the skill's name as the game shows it, for example Herbalism or Knapping.");
        }
    }

    /// <summary>
    /// Re-reads ToolBonusMap into <see cref="_toolBonuses"/>, the way
    /// <see cref="ReloadEnvironmentBonusMap"/> re-reads its map, with the same ceiling.
    /// </summary>
    private static void ReloadToolBonusMap()
    {
        try
        {
            var problems = new List<string>();
            var parsed = ScopedBonusConfig.ParseToolMap(_toolBonusMap?.Value, StationBonusConfig.MaxMultiplier, problems);
            foreach (var problem in problems)
                Logger.LogWarning($"[ToolBonus] ToolBonusMap: {problem}");
            _toolBonuses = parsed;
            if (parsed.Count > 0)
                Logger.LogDebug($"[ToolBonus] {ScopedBonusConfig.CountEntries(parsed)} entr(ies) listed in ToolBonusMap.");
            WarnUnknownToolSkills();
        }
        catch (Exception ex)
        {
            _toolBonuses = new Dictionary<string, Dictionary<string, float>>(StringComparer.OrdinalIgnoreCase);
            Logger.LogWarning($"[ToolBonus] ToolBonusMap could not be read, so no tool bonus applies: {ex.Message}");
        }
    }

    /// <summary>Reports each ToolBonusMap skill name no loaded skill has, once, after the load scan.</summary>
    internal static void WarnUnknownToolSkills()
    {
        if (!Patcher.GameLoadPatch.SkillScanDone) return;
        foreach (string skill in ScopedBonusConfig.UnknownSkills(_toolBonuses, Patcher.GameLoadPatch.SkillNamesByUniqueId.Values))
        {
            if (!_warnedUnknownToolSkills.Add(skill)) continue;
            Logger.LogWarning(
                $"[ToolBonus] ToolBonusMap: '{skill}' is not a skill this game has loaded, so its entries do nothing. " +
                "Use the skill's name as the game shows it, for example Herbalism or Knapping.");
        }
    }

    /// <summary>
    /// Re-reads ActionBonusMap into <see cref="_actionBonuses"/>, the way
    /// <see cref="ReloadEnvironmentBonusMap"/> re-reads its map, with the same ceiling.
    /// </summary>
    private static void ReloadActionBonusMap()
    {
        try
        {
            var problems = new List<string>();
            var parsed = ScopedBonusConfig.ParseActionMap(_actionBonusMap?.Value, StationBonusConfig.MaxMultiplier, problems);
            foreach (var problem in problems)
                Logger.LogWarning($"[ActionBonus] ActionBonusMap: {problem}");
            _actionBonuses = parsed;
            if (parsed.Count > 0)
                Logger.LogDebug($"[ActionBonus] {ScopedBonusConfig.CountEntries(parsed)} entr(ies) listed in ActionBonusMap.");
            WarnUnknownActionSkills();
        }
        catch (Exception ex)
        {
            _actionBonuses = new Dictionary<string, Dictionary<string, float>>(StringComparer.OrdinalIgnoreCase);
            Logger.LogWarning($"[ActionBonus] ActionBonusMap could not be read, so no action bonus applies: {ex.Message}");
        }
    }

    /// <summary>
    /// Reports each ActionBonusMap skill name no loaded skill has, once, after the load scan. An action
    /// whose name holds a ':' and was written without an explicit skill lands here, since the part
    /// after its last ':' was read as a skill.
    /// </summary>
    internal static void WarnUnknownActionSkills()
    {
        if (!Patcher.GameLoadPatch.SkillScanDone) return;
        foreach (string skill in ScopedBonusConfig.UnknownSkills(_actionBonuses, Patcher.GameLoadPatch.SkillNamesByUniqueId.Values))
        {
            if (!_warnedUnknownActionSkills.Add(skill)) continue;
            Logger.LogWarning(
                $"[ActionBonus] ActionBonusMap: '{skill}' is not a skill this game has loaded, so its entries do nothing. " +
                "Use the skill's name as the game shows it, for example Herbalism or Knapping; for an action whose name holds a ':', end the key with :* (such as Cut: Fine:*=1.2).");
        }
    }

    /// <summary>
    /// Re-reads PerkBonusMap into <see cref="_perkBonuses"/>, the way <see cref="ReloadToolBonusMap"/>
    /// re-reads its map, with the same ceiling. Read whole even while PerkBonusEnabled is off, so the typo
    /// report and the [EffectiveSettings] count are right the moment the switch is turned on.
    /// </summary>
    private static void ReloadPerkBonusMap()
    {
        try
        {
            var problems = new List<string>();
            var parsed = ScopedBonusConfig.ParsePerkMap(_perkBonusMap?.Value, StationBonusConfig.MaxMultiplier, problems);
            foreach (var problem in problems)
                Logger.LogWarning($"[PerkBonus] PerkBonusMap: {problem}");
            _perkBonuses = parsed;
            if (parsed.Count > 0)
                Logger.LogDebug($"[PerkBonus] {parsed.Count} perk(s) listed in PerkBonusMap.");
            WarnUnknownPerkSkills();
        }
        catch (Exception ex)
        {
            _perkBonuses = new Dictionary<string, Dictionary<string, float>>(StringComparer.OrdinalIgnoreCase);
            Logger.LogWarning($"[PerkBonus] PerkBonusMap could not be read, so no perk bonus applies: {ex.Message}");
        }
    }

    /// <summary>Reports each PerkBonusMap skill name no loaded skill has, once, after the load scan.</summary>
    internal static void WarnUnknownPerkSkills()
    {
        if (!Patcher.GameLoadPatch.SkillScanDone) return;
        foreach (string skill in ScopedBonusConfig.UnknownSkills(_perkBonuses, Patcher.GameLoadPatch.SkillNamesByUniqueId.Values))
        {
            if (!_warnedUnknownPerkSkills.Add(skill)) continue;
            Logger.LogWarning(
                $"[PerkBonus] PerkBonusMap: '{skill}' is not a skill this game has loaded, so its entries do nothing. " +
                "Use the skill's name as the game shows it, for example Smithing or Herbalism.");
        }
    }

    /// <summary>
    /// Re-reads SynergyTimeoutUnit. A combo's last-action time is stored on the clock in use, so a
    /// switch to the other clock forgets every combo rather than comparing minutes from two clocks.
    /// </summary>
    private static void ReloadSynergyTimeoutUnit()
    {
        bool gameMinutes = string.Equals(_synergyTimeoutUnit?.Value, "GameMinutes", StringComparison.OrdinalIgnoreCase);
        if (gameMinutes != SynergyUsesGameMinutes)
            SkillSynergies.ResetCombos();
        SynergyUsesGameMinutes = gameMinutes;
    }

    /// <summary>
    /// Re-reads SynergyGroupOverrides whole, the way <see cref="ReloadStationBonusMap"/> re-reads its map:
    /// one warning per entry that could not be read, and the catch only for a failure Parse itself does not
    /// anticipate. The shipped default is parsed from the entry's own bound default, so the skill it names
    /// (from a mod that may be absent) is never reported as a typo.
    /// </summary>
    private static void ReloadSynergyGroupOverrides()
    {
        try
        {
            var problems = new List<string>();
            var parsed = SynergyGroupConfig.Parse(_synergyGroupOverrides?.Value, problems);
            foreach (var problem in problems)
                Logger.LogWarning($"[Synergy] SynergyGroupOverrides: {problem}");
            var shippedDefault = SynergyGroupConfig.Parse(_synergyGroupOverrides?.DefaultValue as string, null);
            SkillSynergies.SetGroupOverrides(parsed, shippedDefault);
            if (parsed.Count > 0)
                Logger.LogDebug($"[Synergy] {parsed.Count} skill(s) listed in SynergyGroupOverrides.");
        }
        catch (Exception ex)
        {
            SkillSynergies.SetGroupOverrides(null, null);
            Logger.LogWarning($"[Synergy] SynergyGroupOverrides could not be read, so every skill keeps its built-in synergy groups: {ex.Message}");
        }
    }

    /// <summary>
    /// Applies <see cref="ActiveProfile"/>, but at startup only when it differs from the preset
    /// already written into the config file (<see cref="AppliedProfile"/>).
    ///
    /// A preset writes every key DifficultyProfiles.ApplyProfileSettings sets. Re-asserting it on every launch therefore silently reverted any
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
            "Default true. When enabled, skills show staleness novelty penalties. When disabled, no staleness penalties apply. " +
            "A change made while the game is running (or by switching ActiveProfile) applies to every skill at once, and turning it back on restores the game's own staleness values. " +
            "An edit to this file is read when the game starts, so it needs a full restart: loading a save does not re-read the file."
        );

        _skillExpMultiplier = Config.Bind(
            "Experience",
            "SkillExpMultiplier",
            1,
            new ConfigDescription(
                "Default 1. Global XP multiplier for all skills (unless per-skill override is set). Allowed values: 1-10. " +
                "A change made while the game is running applies from the next XP gain; an edit to this file is read when the game starts, so it needs a full restart.",
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
            "When enabled, skill XP gains are multiplied during the morning window. Default: false. " +
            "If you also turn on the night bonus and set the two windows to overlap, an hour inside both gets both multipliers, one after the other, like every other pair of bonuses in this mod."
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

        _morningBonusSkills = Config.Bind(
            "MorningBonus",
            "MorningBonusSkills",
            "",
            "Which skills the morning bonus applies to: skill names as the game shows them, separated by commas, for example Cooking, Herbalism. " +
            "Empty (default) = every skill, exactly as before. A name no loaded skill has is reported once in LogOutput.log, because it would match nothing. " +
            "A change made while the game is running applies from the next XP gain."
        );

        _nightBonusEnabled = Config.Bind(
            "NightBonus",
            "NightBonusEnabled",
            false,
            "When enabled, skill XP gains are multiplied during the night window (default 22:00 to 02:00). The night-owl counterpart of the morning bonus, read from the same in-game clock. Default: false. " +
            "If you also turn on the morning bonus and set the two windows to overlap, an hour inside both gets both multipliers, one after the other, like every other pair of bonuses in this mod."
        );

        _nightBonusMultiplier = Config.Bind(
            "NightBonus",
            "NightBonusMultiplier",
            1.5f,
            new ConfigDescription(
                "XP multiplier applied during the night window. 1.5 = 50% bonus. Stacks with every other multiplier.",
                new AcceptableValueRange<float>(1f, 5f)
            )
        );

        _nightStartHour = Config.Bind(
            "NightBonus",
            "NightStartHour",
            22f,
            new ConfigDescription(
                "Start of the night window, matching the in-game clock (0 = midnight, 12 = noon). Default 22 = 10 pm. A start later than the end wraps past midnight.",
                new AcceptableValueRange<float>(0f, 23f)
            )
        );

        _nightEndHour = Config.Bind(
            "NightBonus",
            "NightEndHour",
            2f,
            new ConfigDescription(
                "End of the night window in game-hours (exclusive). Default 2 = 2 am, so the default window runs 22:00 to 02:00 across midnight. Setting it equal to NightStartHour makes the window empty.",
                new AcceptableValueRange<float>(0f, 23f)
            )
        );

        _nightBonusSkills = Config.Bind(
            "NightBonus",
            "NightBonusSkills",
            "",
            "Which skills the night bonus applies to: skill names as the game shows them, separated by commas, for example Cooking, Herbalism. " +
            "Empty (default) = every skill, exactly as before. A name no loaded skill has is reported once in LogOutput.log, because it would match nothing. " +
            "A change made while the game is running applies from the next XP gain."
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

        _areaFamiliarityDecayEnabled = Config.Bind(
            "AreaFamiliarity",
            "AreaFamiliarityDecayEnabled",
            false,
            "When true, a location's familiarity bonus fades while you stay away from it: after AreaFamiliarityDecayAfterDays in-game days without earning XP there, it counts AreaFamiliarityDecayPerDay fewer actions for every further day. " +
            "Nothing is deleted: the fade is worked out each time and never written over your recorded visits, so working there again, or turning this off, restores your full standing at once. " +
            "Familiarity is shared by every save, and the fade counts from the in-game day of your last visit on whichever save that was: switching between saves far apart in time can make a location read more or less neglected than it is on the save you are playing. Default: false."
        );

        _areaFamiliarityDecayAfterDays = Config.Bind(
            "AreaFamiliarity",
            "AreaFamiliarityDecayAfterDays",
            14,
            new ConfigDescription(
                "In-game days you can stay away from a location before its familiarity starts to fade. Only used while AreaFamiliarityDecayEnabled is true.",
                new AcceptableValueRange<int>(1, 365)
            )
        );

        _areaFamiliarityDecayPerDay = Config.Bind(
            "AreaFamiliarity",
            "AreaFamiliarityDecayPerDay",
            1f,
            new ConfigDescription(
                "Actions' worth of familiarity lost for each day past AreaFamiliarityDecayAfterDays. Actions beyond AreaFamiliarityVisitsForMaxBonus act as a buffer: the bonus only starts to drop once the fade takes the count below that number. Only used while AreaFamiliarityDecayEnabled is true.",
                new AcceptableValueRange<float>(0.1f, 50f)
            )
        );

        _stationBonusMap = Config.Bind(
            "StationBonus",
            "StationBonusMap",
            "",
            "Extra XP at structures you choose, such as your forge or your study desk. A list of UniqueID=multiplier pairs separated by semicolons, for example " +
            "some_structure_uniqueid=1.25;another_uniqueid=1.1 (that would give +25% at the first and +10% at the second). " +
            "It counts for any skill XP gained while you work AT the structure: dropping something onto it, or pressing one of its own buttons. Only placed structures count, not items. " +
            "To find a structure's UniqueID, turn on LogSkillXpGains, earn some XP there, and copy the id from the end of the [XpGain] line in LogOutput.log ('at structure ...'). " +
            "Stacks with every other multiplier, including area familiarity at the same structure. Multipliers must be above 0 and at most 10; an entry that cannot be read is skipped with a warning in the log. Empty (default) = off."
        );

        _seasonBonusMap = Config.Bind(
            "SeasonBonus",
            "SeasonBonusMap",
            "",
            "Faster (or slower) skill XP in the seasons you choose. A list of Season:Skill=multiplier pairs separated by semicolons, for example " +
            "Winter:Woodworking=1.25;Summer=1.1 (that would give Woodworking +25% in winter and every skill +10% in summer). " +
            "Season is Spring, Summer, Autumn or Winter. Skill is the skill's name as the game shows it; leave it out, or write *, for every skill, and an entry naming the skill beats the every-skill entry of the same season. " +
            "Multipliers must be above 0 and at most 10, so a value below 1 slows that skill in that season. Stacks with every other multiplier. " +
            "An entry that cannot be read is skipped with a warning in the log, and a skill name no loaded skill has is reported there too. Empty (default) = off."
        );

        _environmentBonusMap = Config.Bind(
            "EnvironmentBonus",
            "EnvironmentBonusMap",
            "",
            "Faster (or slower) skill XP in the kinds of place you choose. A list of Place:Skill=multiplier pairs separated by semicolons, for example " +
            "Env_Cave*:Knapping=1.2;Env_Grove*=1.1 (that would give Knapping +20% in every cave and every skill +10% in every grove). " +
            "A place is the start of an environment's asset name followed by *, such as Env_Cave* or Env_GrovePine*, or one environment's exact asset name or UniqueID. " +
            "To find them, turn on LogSkillXpGains: each [XpGain] line ends with 'in env <UniqueID> (<asset name>)'. " +
            "Skill is the skill's name as the game shows it; leave it out, or write *, for every skill. Where several places match, the most specific one with an entry for that skill wins " +
            "(the exact UniqueID or asset name, then the longest prefix), and within it an entry naming the skill beats its every-skill entry. " +
            "Multipliers must be above 0 and at most 10, so a value below 1 slows that skill there. Stacks with every other multiplier, including area familiarity and the station bonus. " +
            "An entry that cannot be read is skipped with a warning in the log, and a skill name no loaded skill has is reported there too. Empty (default) = off."
        );

        _toolBonusMap = Config.Bind(
            "ToolBonus",
            "ToolBonusMap",
            "",
            "Faster (or slower) skill XP when an action uses a tool you choose. A list of Tool:Skill=multiplier pairs separated by semicolons, for example " +
            "tag_Axe=1.1;some_axe_uniqueid:Woodworking=1.25 (every skill +10% with anything tagged as an axe, and Woodworking +25% with that one axe). " +
            "The tool is the card a drag-and-drop action uses on the other one: the game decides which, whichever card you drag (grinding Acorns uses the Stone Mortar and Pestle as the tool). " +
            "Tool is the card's UniqueID or one of its tag names. To find them, turn on LogSkillXpGains: each [XpGain] line from such an action ends with 'with tool <UniqueID> (tags ...)'. " +
            "Skill is the skill's name as the game shows it; leave it out, or write *, for every skill. Where the UniqueID and several tags all have an entry, the HIGHEST one applies, they do not stack. " +
            "Actions with no tool (a card's own buttons) are never changed. Multipliers must be above 0 and at most 10, so a value below 1 slows that skill with that tool. Stacks with every other multiplier. " +
            "An entry that cannot be read is skipped with a warning in the log, and a skill name no loaded skill has is reported there too. Empty (default) = off."
        );

        _actionBonusMap = Config.Bind(
            "ActionBonus",
            "ActionBonusMap",
            "",
            "Faster (or slower) skill XP from the actions you choose. A list of Action:Skill=multiplier pairs separated by semicolons, for example " +
            "Grind=1.2;Chop Wood:Woodworking=1.5. " +
            "Action is the action's name as its button shows it in English, or its localization key, which stays the same in every language and across game updates. " +
            "To find them, turn on LogSkillXpGains: each [XpGain] line ends with \"by action '<name>' (key <key>)\". " +
            "Skill is the skill's name as the game shows it; leave it out, or write *, for every skill. The key is split at its last ':', so for an action whose name holds a ':' end the key with :* (such as Cut: Fine:*=1.2). " +
            "Where both the name and the key have an entry, the higher one applies. Multipliers must be above 0 and at most 10, so a value below 1 slows that skill from that action. Stacks with every other multiplier, including ToolBonusMap. " +
            "An entry that cannot be read is skipped with a warning in the log, and a skill name no loaded skill has is reported there too. Empty (default) = off."
        );

        _perkBonusEnabled = Config.Bind(
            "PerkBonus",
            "PerkBonusEnabled",
            false,
            "When true, holding a perk listed in PerkBonusMap makes XP in the skill it names faster or slower. A perk counts whether you picked it at character creation or were granted it during the run " +
            "(the Academy graduate perks of Community Mod Chest are granted during the run). It is a switch of its own, unlike the other maps, because PerkBonusMap ships with entries: " +
            "nothing changes for anyone until this is turned on. The Casual, Grinder and Immersive presets turn it on. Default: false."
        );

        _perkBonusMap = Config.Bind(
            "PerkBonus",
            "PerkBonusMap",
            "cmcperkgradmetallurgy:Smithing=1.25;cmcperkgradarmorer:Smithing=1.25;cmcperkgradfishing:Fishing=1.25;cmcperkgradherbalism:Herbalism=1.25;cmcperkgradmedicine:Herbalism=1.25;cmcperkgradcarpentry:Woodworking=1.25;cmcperkgradarchitecture:Woodworking=1.25",
            "Which perks speed which skills, used only while PerkBonusEnabled is on. A list of Perk:Skill=multiplier pairs separated by semicolons. " +
            "Perk is a perk's UniqueID exactly as the perk's file spells it (case matters); skill is the skill's name as the game shows it, and leaving it out, or writing *, means every skill. " +
            "The default maps the seven Community Mod Chest Academy graduate perks (Metallurgy and Armorer to Smithing, Fishing to Fishing, Herbalism and Medicine to Herbalism, Carpentry and Architecture to Woodworking, each 1.25); " +
            "without that mod nobody holds them, so they do nothing. Add any perk from any mod the same way. " +
            "When several perks you hold name one skill, the HIGHEST entry applies, they do not stack. Multipliers must be above 0 and at most 10, so a value below 1 slows that skill. Stacks with every other multiplier. " +
            "An entry that cannot be read is skipped with a warning in the log, and a skill name no loaded skill has is reported there too."
        );

        _streakBonusEnabled = Config.Bind(
            "Streak",
            "StreakBonusEnabled",
            false,
            "When true, gaining skill XP on consecutive in-game days builds a streak that adds StreakBonusPerDay to every XP gain for each day of it, counting up to StreakBonusMaxDays days. " +
            "Skipping a whole day restarts it at day 1. One streak covers all skills, and like area familiarity it is shared by every save, so loading an older save or starting a new character also restarts it. Default: false."
        );

        _streakBonusPerDay = Config.Bind(
            "Streak",
            "StreakBonusPerDay",
            0.02f,
            new ConfigDescription(
                "Extra XP per consecutive day. 0.02 = +2% on day 1, +4% on day 2, and so on. Stacks with every other multiplier.",
                new AcceptableValueRange<float>(0f, 0.5f)
            )
        );

        _streakBonusMaxDays = Config.Bind(
            "Streak",
            "StreakBonusMaxDays",
            10,
            new ConfigDescription(
                "The most days a streak counts. With the default StreakBonusPerDay of 0.02, 10 caps the streak at +20%. Longer streaks keep that top value.",
                new AcceptableValueRange<int>(1, 100)
            )
        );

        _restedBankEnabled = Config.Bind(
            "RestedBank",
            "RestedBankEnabled",
            false,
            "When true, a skill you leave alone banks rested XP: every full in-game day since that skill's last XP gain banks RestedBankGainsPerDay gains, up to RestedBankMaxGains, and each XP gain spends one banked gain and is multiplied by RestedBankMultiplier. " +
            "It rewards a break, the opposite of the day streak. A skill's first gain ever banks nothing, so it is never doubled; loading an earlier save (the day going backwards) banks nothing either. " +
            "Each skill keeps its own bank in SkillRested.tsv, shared by every save like the day streak, or kept per save slot with PerSaveProgression. A skill set to 0x never spends its bank. The Casual and Grinder presets turn it on. Default: false."
        );

        _restedBankGainsPerDay = Config.Bind(
            "RestedBank",
            "RestedBankGainsPerDay",
            3,
            new ConfigDescription(
                "How many rested gains a skill banks for each full in-game day it goes unused. 3 = three doubled gains after one day away.",
                new AcceptableValueRange<int>(1, 50)
            )
        );

        _restedBankMaxGains = Config.Bind(
            "RestedBank",
            "RestedBankMaxGains",
            10,
            new ConfigDescription(
                "The most rested gains one skill can bank. A longer break banks no more than this.",
                new AcceptableValueRange<int>(1, 100)
            )
        );

        _restedBankMultiplier = Config.Bind(
            "RestedBank",
            "RestedBankMultiplier",
            2f,
            new ConfigDescription(
                "XP multiplier for a gain that spends a banked rested gain. 2.0 = double XP for that one gain. Stacks with every other multiplier.",
                new AcceptableValueRange<float>(1f, 5f)
            )
        );

        _milestoneBonusEnabled = Config.Bind(
            "Milestone",
            "MilestoneBonusEnabled",
            false,
            "When true, the first time a skill reaches each breakpoint (every MilestoneEvery points: 25, 50, 75, 100 and 125 at the default) that XP gain also adds MilestoneBonusXp skill points, once. " +
            "If one gain passes two breakpoints it pays one burst. The burst is added after every multiplier, so no multiplier scales it and MaxComposedMultiplier does not cap it; it never takes a skill above its maximum, and a skill set to 0x never reaches one. " +
            "Each character pays each breakpoint once: the breakpoints already paid are kept per save slot in BepInEx/config/SkillSpeedBoost/Milestones/Slot<N>/SkillMilestones.tsv whatever PerSaveProgression says, so reloading a save never pays one again, and starting a new game in a slot clears that slot's record. " +
            "The Casual and Grinder presets turn it on. Default: false."
        );

        _milestoneEvery = Config.Bind(
            "Milestone",
            "MilestoneEvery",
            25,
            new ConfigDescription(
                "The spacing of the breakpoints, in skill points. 25 = a burst at 25, 50, 75, 100 and 125.",
                new AcceptableValueRange<int>(5, 150)
            )
        );

        _milestoneBonusXp = Config.Bind(
            "Milestone",
            "MilestoneBonusXp",
            1f,
            new ConfigDescription(
                "Skill points added once when a skill reaches a breakpoint. Added on top of the gain after every multiplier, never scaled and never capped by MaxComposedMultiplier.",
                new AcceptableValueRange<float>(0.1f, 25f)
            )
        );

        _xpOverflowEnabled = Config.Bind(
            "XpOverflow",
            "XpOverflowEnabled",
            false,
            "When true, XP a skill cannot take because it is at its maximum is not lost: XpOverflowFraction of it goes to a related skill (one that shares a synergy group with it, SynergyGroupOverrides included) that is still below its maximum, the least trained of them first (ties by name). " +
            "A gain that crosses the maximum part way sends on only the part the maximum cut off. The redirected XP is multiplied only by the receiving skill's own SkillExpMultiplier or per-skill multiplier: no situational bonus applies to it, MaxComposedMultiplier does not cap it, and it counts as no practice (no daily first use, streak, rested bank, milestone or synergy). " +
            "It never takes the receiving skill past its maximum, goes one step only, and a skill set to 0x neither sends nor receives any. Works whether or not EnableSkillSynergies is on. The Casual and Grinder presets turn it on. Default: false."
        );

        _xpOverflowFraction = Config.Bind(
            "XpOverflow",
            "XpOverflowFraction",
            0.5f,
            new ConfigDescription(
                "The share of the XP a maxed skill cannot take that goes to the related skill. 0.5 = half.",
                new AcceptableValueRange<float>(0.05f, 1f)
            )
        );

        _xpReadoutEnabled = Config.Bind(
            "XpReadout",
            "XpReadoutEnabled",
            false,
            "When true, a short message near the time wheel names the XP bonuses that applied to a skill gain, for example 'Skill XP x1.50: morning, well rested' or 'Skill XP x1.10: area familiarity x1.10'. " +
            "It appears when the set of bonuses changes (including 'no bonus' when they stop applying), and again after XpReadoutMinSeconds while the same bonuses keep applying. " +
            "It only reports: it changes no XP. It reports only gains this mod handles, so it shows nothing while every XP setting of this mod is off. No preset changes it. Default: false."
        );

        _xpReadoutMinSeconds = Config.Bind(
            "XpReadout",
            "XpReadoutMinSeconds",
            60f,
            new ConfigDescription(
                "Real seconds before the same set of bonuses is shown again. A change of bonuses is always shown at once. 0 = on every gain.",
                new AcceptableValueRange<float>(0f, 3600f)
            )
        );

        _partnerSkillsFollowSettings = Config.Bind(
            "PartnerSkills",
            "PartnerSkillsFollowSettings",
            false,
            "When true, the Partner's skills (the companion a 'Partner:' perk brings, such as Partner: The Builder) follow two of your settings: your staleness settings (EnableSkillStaleness and the per-skill staleness toggles and rates of the matching skill), and your SkillExpMultiplier or per-skill multiplier on every skill point the Partner gains by doing something. " +
            "No situational bonus ever applies to the Partner (time of day, area familiarity, station, season, place, tool, action, graduate perk, synergy, level scaling, first use, streak, rested bank, well rested, In the Flow, low condition, milestone or XP overflow): those describe your own condition, not the Partner's. " +
            "Combat Experience is not a skill this mod tracks, so the Partner's keeps the game's own rules. When false (default) the Partner's skills are exactly as the game makes them. Switching it applies at once. No preset changes it. Default: false."
        );

        _perSaveProgression = Config.Bind(
            "PerSave",
            "PerSaveProgression",
            false,
            "When true, area familiarity (with its fade days), the day streak and the rested-XP bank are kept separately for each save slot, in a Slot<N> folder inside BepInEx/config/SkillSpeedBoost/, so a second character starts with none of the first one's. " +
            "When false (default) they are shared by every save in that folder, exactly as before. " +
            "The first save you LOAD after turning this on receives a copy of the shared files, and PerSaveMigrated.txt is written beside them so no other save gets one; a new game never receives a copy, and every other save starts empty. " +
            "The shared files are never changed or deleted: turning this off goes back to them as they were, without anything gained per save since. " +
            "Turning it on or off while playing moves to the right folder at once, and turning it on counts as loading the save you are in. No preset changes it. Default: false."
        );

        _perSaveResetOnNewGame = Config.Bind(
            "PerSave",
            "PerSaveResetOnNewGame",
            true,
            "Only used while PerSaveProgression is on. When true (default), starting a NEW game in a save slot first clears the familiarity, streak and rested-bank files an earlier game left in that slot's folder. " +
            "The game only starts a new game in an empty slot, so those files belong to a save that was deleted. Loading a save never clears anything. Default: true."
        );

        _activeProfile = Config.Bind(
            "DifficultyProfiles",
            "ActiveProfile",
            "None",
            new ConfigDescription(
                "Apply a named difficulty preset. A preset sets the WHOLE situational stack: ExpMultiplier, Staleness, AreaFamiliarity, LevelScaling, MorningBonus, NightBonus, SkillSynergies, SynergyRequireDifferentSkill (always false), DailyFirstUse, Streak, WellRested, LowConditionPenalty, PerkBonusEnabled, FocusBonusEnabled, RestedBankEnabled, MilestoneBonusEnabled and XpOverflowEnabled. " +
                "It does NOT touch EnablePerSkillMultipliers, any <Skill>_Multiplier, StationBonusMap, SeasonBonusMap, EnvironmentBonusMap, ToolBonusMap, ActionBonusMap, PerkBonusMap, MorningBonusSkills, NightBonusSkills, the synergy tuning or SynergyGroupOverrides: your per-skill overrides, your stations, your seasons, your places, your tools, your actions, your perks, your morning and night skill lists and your synergy groups survive every preset, including the ones that advertise vanilla rates. 'None' = use individual settings. " +
                "Options: None, VanillaPlus (2x+staleness, familiarity only), Casual (3x, no staleness, every situational bonus on), Hardcore (1x+staleness, every situational bonus off and the low-condition XP penalty ON), " +
                "Grinder (10x, no staleness, every situational bonus on), Balanced (2x+staleness, familiarity+level scaling), Legacy (1x+staleness, every situational bonus off and the low-condition XP penalty ON), " +
                "Immersive (1x+staleness, no raw multiplier - only the diegetic bonuses: familiarity, morning, daily first use, the day streak, well-rested, the Academy graduate perks and In the Flow - plus the low-condition XP penalty). " +
                "Selecting a preset OVERWRITES those seventeen settings in this file ONCE, the next time the game starts (or immediately if you change it while running). After that it leaves them alone, " +
                "so an individual key you edit afterwards survives - see AppliedProfile below. Everything it writes, staleness included, takes effect as soon as it is written.",
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
            "When true, performing related skills in sequence stacks a bonus. With the default tuning below that is +10% XP per consecutive related action, capped at +50% (5-action combo), and a combo resets after 5 real-time minutes of inactivity: " +
            "SynergyBonusPerAction, SynergyMaxCombo, SynergyComboTimeoutMinutes and SynergyTimeoutUnit change those numbers, SynergyRequireDifferentSkill makes only a change of skill grow a combo, and SynergyGroupOverrides changes which skills count as related. Default: false."
        );

        _skillSynergiesDebugLog = Config.Bind(
            "SkillSynergies",
            "SkillSynergiesDebugLog",
            false,
            "When true, logs synergy bonus and combo state to LogOutput.log each time a synergy bonus applies. Useful for verifying combo groups. Default: false."
        );

        _synergyRequireDifferentSkill = Config.Bind(
            "SkillSynergies",
            "SynergyRequireDifferentSkill",
            false,
            "When true, a synergy combo grows only when you switch to a DIFFERENT skill of the same group, so repeating one skill (by hand, or through a mod such as RepeatAction) holds the combo where it is instead of building it up. " +
            "Repeating a skill still keeps its combo from timing out. Default: false, which counts every related action, the same skill included, exactly as before. Every difficulty preset writes false."
        );

        _synergyBonusPerAction = Config.Bind(
            "SkillSynergies",
            "SynergyBonusPerAction",
            0.1f,
            new ConfigDescription(
                "Extra XP for each action in a synergy combo. 0.1 (default) = +10% for the first related action, +20% for the second, and so on up to SynergyMaxCombo actions. 0 leaves combos counting but adds nothing.",
                new AcceptableValueRange<float>(0f, 0.5f)
            )
        );

        _synergyMaxCombo = Config.Bind(
            "SkillSynergies",
            "SynergyMaxCombo",
            5,
            new ConfigDescription(
                "The most actions a synergy combo counts. With the default SynergyBonusPerAction of 0.1, 5 (default) caps a combo at +50%; further related actions keep that top value.",
                new AcceptableValueRange<int>(1, 20)
            )
        );

        _synergyComboTimeoutMinutes = Config.Bind(
            "SkillSynergies",
            "SynergyComboTimeoutMinutes",
            5f,
            new ConfigDescription(
                "How long a synergy combo survives without a related action before it starts again from one, in the unit SynergyTimeoutUnit names. Default 5.",
                new AcceptableValueRange<float>(1f, 120f)
            )
        );

        _synergyTimeoutUnit = Config.Bind(
            "SkillSynergies",
            "SynergyTimeoutUnit",
            "RealMinutes",
            new ConfigDescription(
                "What SynergyComboTimeoutMinutes counts. RealMinutes (default) = minutes on your own clock, as before: a menu left open can end a combo, while a long in-game sleep taken inside a few real minutes keeps it. " +
                "GameMinutes = minutes on the in-game clock, which moves in 15-minute steps, so most actions take at least 15 in-game minutes and a timeout of 30 or more suits this unit. " +
                "Loading an earlier save restarts every combo under GameMinutes, and switching this setting while playing restarts them too.",
                new AcceptableValueList<string>("RealMinutes", "GameMinutes")
            )
        );

        _synergyGroupOverrides = Config.Bind(
            "SkillSynergies",
            "SynergyGroupOverrides",
            "Melee Fighting=Combat",
            "Changes which synergy groups a skill belongs to: Skill Name=GroupA,GroupB, entries separated by ';'. An entry REPLACES that skill's built-in groups, so it can add a skill no group lists (another mod's skill, or Perception), " +
            "move a skill to other groups, or, with nothing after the '=', take a skill out of synergies (for example Swimming=). Skill names are the names the game shows; one that no loaded skill has is reported once in LogOutput.log. " +
            "Group names ignore letter case: the built-in ones are Crafting, Tools, Survival, Gathering, Hunting, Combat, Social and Performance, and any other name starts a group of its own. " +
            "Default: Melee Fighting=Combat, which puts Community Mod Chest's Melee Fighting skill in the Combat group and does nothing without that mod. Only matters with EnableSkillSynergies on."
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
                "Ceiling on the COMBINED multiplier after every bonus has stacked (global/per-skill x morning x night x familiarity x station x season x environment x tool x action x perk x synergy x level scaling x first-use x streak x rested bank x well-rested x In the Flow x low-condition penalty). " +
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

        _focusBonusEnabled = Config.Bind(
            "FocusBonus",
            "FocusBonusEnabled",
            false,
            "When true, skill XP is multiplied by FocusBonusMultiplier while your Focus is In the Flow, its top band (72 and up on the current game). " +
            "The band is read from the game's own Focus statuses, so the bonus applies exactly while the game shows you In the Flow. " +
            "Anything that raises Focus, such as a focus tea from another mod, then also speeds learning. It stacks with the game's own In the Flow effects. " +
            "The Casual, Grinder and Immersive presets turn it on. Default: false."
        );

        _focusBonusMultiplier = Config.Bind(
            "FocusBonus",
            "FocusBonusMultiplier",
            1.2f,
            new ConfigDescription(
                "XP multiplier applied while In the Flow. 1.2 = 20% bonus. Stacks with every other multiplier.",
                new AcceptableValueRange<float>(1f, 3f)
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

        _logSkillXpGains = Config.Bind(
            "Diagnostics",
            "LogSkillXpGains",
            false,
            "When true, writes one line to LogOutput.log for every skill XP gain this mod handles: the skill, the gain the game applied, the combined XP multiplier, the gain after it, and the skill's trained (base) value before and after, then where it happened: the structure, the environment, the action and the tool it used, which are the keys StationBonusMap, EnvironmentBonusMap, ActionBonusMap and ToolBonusMap take. Use it to check that a setting is doing what you expect, since a small gain cannot be read off the skill bar. It logs on every XP gain, so leave it off in normal play. Default: false."
        );

        _logDailyXpSummary = Config.Bind(
            "Diagnostics",
            "LogDailyXpSummary",
            false,
            "When true, writes one line per skill per in-game day to LogOutput.log: how many XP gains that skill had that day, the game's own total, the total after this mod's multipliers and bonuses, and the average multiplier (the second total divided by the first). " +
            "It counts the same gains LogSkillXpGains would report, and a day's lines are written when the first gain of a later day arrives (or of an earlier day, after loading an older save), so the last day's lines appear when you quit the game. " +
            "A quieter way to check your balance than LogSkillXpGains, which writes a line for every gain. Default: false."
        );

        // Initialize area familiarity and day-streak persistence (loads both from disk). Before any
        // run they read the shared folder; with PerSaveProgression on, each run start moves them to
        // its save slot's folder (ProgressionStore, from GameLoadPatch's run-start postfix), and a
        // change of the setting made while playing moves them at once.
        Patcher.AreaFamiliarityService.Initialize();
        Patcher.SkillStreakService.Initialize();
        Patcher.RestedBankService.Initialize();
        _perSaveProgression.SettingChanged += (sender, args) => Patcher.ProgressionStore.Refresh("PerSaveProgression changed");

        // Initialize skill config manager for per-skill multipliers and profiles
        SkillConfigManager.Initialize(Config);

        // The morning and night skill lists: the same once-and-on-change read (plan D1).
        ReloadBonusWindowSkills();
        _morningBonusSkills.SettingChanged += (sender, args) => ReloadBonusWindowSkills();
        _nightBonusSkills.SettingChanged += (sender, args) => ReloadBonusWindowSkills();

        // Station bonuses: parsed once here and again whenever the entry changes, never per XP gain.
        ReloadStationBonusMap();
        _stationBonusMap.SettingChanged += (sender, args) => ReloadStationBonusMap();

        // Season bonuses: the same once-and-on-change read; the season itself is read at XP time.
        ReloadSeasonBonusMap();
        _seasonBonusMap.SettingChanged += (sender, args) => ReloadSeasonBonusMap();

        // Environment bonuses: the same once-and-on-change read; the environment is read at XP time.
        ReloadEnvironmentBonusMap();
        _environmentBonusMap.SettingChanged += (sender, args) => ReloadEnvironmentBonusMap();

        // Tool and action bonuses: the same once-and-on-change read; the tool and the action are
        // captured around each action by AreaFamiliarityPatch.ActionRoutine_Post.
        ReloadToolBonusMap();
        _toolBonusMap.SettingChanged += (sender, args) => ReloadToolBonusMap();
        ReloadActionBonusMap();
        _actionBonusMap.SettingChanged += (sender, args) => ReloadActionBonusMap();

        // Perk bonuses: the same once-and-on-change read; the perks held are read at XP time.
        ReloadPerkBonusMap();
        _perkBonusMap.SettingChanged += (sender, args) => ReloadPerkBonusMap();

        // The synergy clock: the same once-and-on-change read, and a switch of clock forgets every combo.
        ReloadSynergyTimeoutUnit();
        _synergyTimeoutUnit.SettingChanged += (sender, args) => ReloadSynergyTimeoutUnit();

        // The synergy group overrides: parsed whole here and on every change, like the station map.
        ReloadSynergyGroupOverrides();
        _synergyGroupOverrides.SettingChanged += (sender, args) => ReloadSynergyGroupOverrides();

        // The multiplier is read on every XP gain (MorningBonusPatch.ChangeStat_Post), so a change made
        // in-game needs no re-apply. The note that used to sit here said such a change waited for a
        // reload; that described the load-time stat-graph walk 1.7.2 replaced, never this read.
        _skillExpMultiplier.SettingChanged += (sender, args) =>
        {
            Logger.LogDebug($"Global SkillExpMultiplier changed to {SkillExpMultiplier}x. It applies from the next XP gain.");
        };

        // Staleness lives on the skill GameStats themselves, so a change has to be written to them:
        // re-apply to every skill at once rather than waiting for the next run start. The per-skill
        // staleness toggles and rates are hooked the same way in SkillConfigManager.
        _enableSkillStaleness.SettingChanged += (sender, args) =>
            Patcher.GameLoadPatch.ReapplyStaleness("EnableSkillStaleness changed");

        // The Partner's skill staleness (plan E2) is written to its stat models the same way, so switching
        // PartnerSkillsFollowSettings re-applies at once: on writes your settings, off gives the originals back.
        _partnerSkillsFollowSettings.SettingChanged += (sender, args) =>
            Patcher.GameLoadPatch.ReapplyStaleness("PartnerSkillsFollowSettings changed");

        // Changing the preset at runtime always applies it: that is an explicit request.
        _activeProfile.SettingChanged += (sender, args) => ApplyProfileOnce(force: true);

        // At startup, apply only when the chosen preset is not the one already written into this
        // config file. A preset writes every key ApplyProfileSettings sets, so re-asserting it unconditionally every launch
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
        bool patchesApplied = true;
        void Apply(string name, System.Action<Harmony> apply)
        {
            try { apply(_harmony); }
            catch (System.Exception ex)
            {
                patchesApplied = false;
                Logger.LogError($"Failed to apply {name}: {ex}");
            }
        }
        Apply("GameLoadPatch", Patcher.GameLoadPatch.ApplyPatch);
        Apply("MorningBonusPatch", Patcher.MorningBonusPatch.ApplyPatch);
        Apply("AreaFamiliarityPatch", Patcher.AreaFamiliarityPatch.ApplyPatch);
        Apply("PartnerSkillPatch", Patcher.PartnerSkillPatch.ApplyPatch);

        if (patchesApplied)
            Logger.LogInfo($"{PluginName} v{PluginVersion} loaded.");
        else
            Logger.LogWarning($"{PluginName} v{PluginVersion} loaded with failed patches (see errors above).");
    }

    private void OnApplicationQuit()
    {
        Patcher.AreaFamiliarityService.Save(forceWrite: true);
        Patcher.SkillStreakService.Save(forceWrite: true);
        Patcher.RestedBankService.Save(forceWrite: true);
        Patcher.MilestoneService.Save(forceWrite: true);
        // The day still open in the daily XP summary; a second flush (OnDestroy) writes nothing.
        Patcher.XpDigest.Flush();
    }

    private void OnDestroy()
    {
        Patcher.AreaFamiliarityService.Save(forceWrite: true);
        Patcher.SkillStreakService.Save(forceWrite: true);
        Patcher.RestedBankService.Save(forceWrite: true);
        Patcher.MilestoneService.Save(forceWrite: true);
        Patcher.XpDigest.Flush();
        _harmony?.UnpatchSelf();
    }
}
