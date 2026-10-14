using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;

namespace mod_update_manager
{
    /// <summary>
    /// Handles configuration for the Mod Update Manager
    /// </summary>
    public class ModUpdateConfig
    {
        private ConfigFile _config;

        // Config entries
        public ConfigEntry<string> NexusApiKey { get; private set; }
        public ConfigEntry<bool> UsePublicEndpointWithoutKey { get; private set; }
        public ConfigEntry<bool> EnableEndorseButton { get; private set; }
        public ConfigEntry<bool> CheckOnStartup { get; private set; }
        public ConfigEntry<bool> OpenWindowOnUpdates { get; private set; }
        public ConfigEntry<bool> ShowOnlyUpdates { get; private set; }
        public ConfigEntry<KeyCode> ToggleUIKey { get; private set; }
        public ConfigEntry<float> WindowWidth { get; private set; }
        public ConfigEntry<float> WindowHeight { get; private set; }
        public ConfigEntry<int> DetailsMaxChars { get; private set; }
        public ConfigEntry<bool> KeepPreviousSuiteVersion { get; private set; }
        public ConfigEntry<int> KeptVersions { get; private set; }

        // New features
        public ConfigEntry<int> CheckIntervalMinutes { get; private set; }
        public ConfigEntry<bool> EnableBackgroundChecking { get; private set; }
        public ConfigEntry<bool> ShowConflictWarnings { get; private set; }
        public ConfigEntry<bool> CachingEnabled { get; private set; }
        public ConfigEntry<bool> EnableNexusDiscovery { get; private set; }
        public ConfigEntry<int> DiscoveryMaxScanId { get; private set; }
        public ConfigEntry<int> DiscoveryMaxConsecutiveMisses { get; private set; }
        public ConfigEntry<int> DiscoveryFeedSize { get; private set; }

        // Mod ID mappings file path
        public string ModMappingsPath { get; private set; }
        public string ResponseCachePath { get; private set; }
        public string PreferencesPath { get; private set; }
        public string VersionLogPath { get; private set; }
        public string ProfilesDir { get; private set; }

        public ModUpdateConfig(ConfigFile config)
        {
            _config = config;
            ModMappingsPath = Path.Combine(Paths.ConfigPath, "ModUpdateManager_Mappings.json");
            ResponseCachePath = Path.Combine(Paths.ConfigPath, "ModUpdateManager_Cache.json");
            PreferencesPath = Path.Combine(Paths.ConfigPath, "ModUpdateManager_Preferences.json");
            // A text file, not JSON; named so it cannot meet the empty ModUpdateManager_History directory an older build left.
            VersionLogPath = Path.Combine(Paths.ConfigPath, "ModUpdateManager_VersionLog.txt");
            ProfilesDir = Path.Combine(Paths.ConfigPath, "ModUpdateManager_Profiles");
            InitializeConfig();
        }

        private void InitializeConfig()
        {
            // API Settings
            NexusApiKey = _config.Bind(
                "API",
                "NexusApiKey",
                "",
                "Your Nexus Mods API key. Get one from https://www.nexusmods.com/users/myaccount?tab=api+access"
            );

            // Off by default: the README promises nothing is sent without a key (plan feature 15).
            UsePublicEndpointWithoutKey = _config.Bind(
                "API",
                "UsePublicEndpointWithoutKey",
                false,
                "Check for updates without an API key. When no key is set, each update check sends one request to Nexus's public read endpoint (api.nexusmods.com/v2/graphql) naming the Nexus IDs of the mods being checked, with no key in it. Off by default, so nothing is sent without a key."
            );

            // Off by default (plan feature 17): an endorsement is a write to the player's Nexus account.
            EnableEndorseButton = _config.Bind(
                "API",
                "EnableEndorseButton",
                false,
                "Show an Endorse button on each My Mods row whose mod has a Nexus ID, while an API key is set. The button only asks \"Endorse <mod> on Nexus with your account?\"; only Yes sends anything: one request to Nexus (api.nexusmods.com/v1/.../endorse.json) carrying your key, the mod's Nexus ID and its installed version. Undo an endorsement on the mod's Nexus page. Off by default."
            );

            // Behavior Settings
            CheckOnStartup = _config.Bind(
                "Behavior",
                "CheckOnStartup",
                true,
                "Automatically check for updates when the game loads"
            );

            OpenWindowOnUpdates = _config.Bind(
                "Behavior",
                "OpenWindowOnUpdates",
                true,
                "Open the update manager window by itself, at most once per session, when the startup check finds an update. Background checks and the check buttons never open it."
            );

            ShowOnlyUpdates = _config.Bind(
                "Behavior",
                "ShowOnlyUpdates",
                false,
                "Only show mods that have updates available"
            );

            // UI Settings
            ToggleUIKey = _config.Bind(
                "UI",
                "ToggleKey",
                KeyCode.F3,
                "Key to toggle the update manager window"
            );

            WindowWidth = _config.Bind(
                "UI",
                "WindowWidth",
                900f,
                "Width of the update manager window"
            );

            WindowHeight = _config.Bind(
                "UI",
                "WindowHeight",
                680f,
                "Height of the update manager window"
            );

            DetailsMaxChars = _config.Bind(
                "UI",
                "DetailsMaxChars",
                SuiteDescription.DefaultMaxChars,
                new ConfigDescription(
                    "Longest description, in characters, that the ? panel on the Install & Update tab shows for a bundled mod before cutting it with a pointer to the Nexus page. The default is a starting value, not a measured limit.",
                    new AcceptableValueRange<int>(200, 10000))
            );

            // Install & Update. Opt-in: with it off an Apply deletes the copy it replaces, as always.
            KeepPreviousSuiteVersion = _config.Bind(
                "InstallAndUpdate",
                "KeepPreviousSuiteVersion",
                false,
                "Keep the previous version of a suite mod when Apply Updates replaces it, in BepInEx/mum_work/kept/ (outside the folders the game loads from), so the Install & Update tab can roll back to it. Off by default: each kept copy takes as much disk space as that mod's folder."
            );

            KeptVersions = _config.Bind(
                "InstallAndUpdate",
                "KeptVersions",
                SuiteKeepPolicy.DefaultKeptVersions,
                new ConfigDescription(
                    "How many previous versions of each suite mod are kept when KeepPreviousSuiteVersion is on, 1 to 3. When there are more, the oldest are deleted after an update.",
                    new AcceptableValueRange<int>(SuiteKeepPolicy.MinKeptVersions, SuiteKeepPolicy.MaxKeptVersions))
            );

            // Scheduling
            EnableBackgroundChecking = _config.Bind(
                "Scheduling",
                "EnableBackgroundChecking",
                false,
                "Periodically check for updates in the background"
            );

            CheckIntervalMinutes = _config.Bind(
                "Scheduling",
                "CheckIntervalMinutes",
                60,
                "Interval in minutes between background checks (10-1440)"
            );

            // Analysis & Warnings
            ShowConflictWarnings = _config.Bind(
                "Analysis",
                "ShowConflictWarnings",
                true,
                "Show warnings about potential mod conflicts"
            );

            // Performance
            CachingEnabled = _config.Bind(
                "Performance",
                "CachingEnabled",
                true,
                "Cache Nexus API responses to reduce network requests"
            );

            EnableNexusDiscovery = _config.Bind(
                "Performance",
                "EnableNexusDiscovery",
                false,
                "Opt in to a slow background scan of Nexus mod IDs to discover mappings for mods that do not declare NexusModId (needs an API key). Also adds a New and updated filter to My Mods, which sends one request to Nexus's public list when you first open it in a game session and again each time you press its Refresh button (see DiscoveryFeedSize). Leave disabled unless you specifically need discovery."
            );

            DiscoveryMaxScanId = _config.Bind(
                "Performance",
                "DiscoveryMaxScanId",
                2000,
                "Hard ceiling for the optional background Nexus mod-ID scan. The scan walks Nexus mod IDs sequentially to build a name->ID index for installed mods that don't declare NexusModId in their ModInfo.json."
            );

            DiscoveryMaxConsecutiveMisses = _config.Bind(
                "Performance",
                "DiscoveryMaxConsecutiveMisses",
                500,
                "Stop background discovery once this many consecutive Nexus IDs return 'mod not found'. Prevents the scan from running forever when there are no more new mods to find. Set to 0 to disable this stop condition."
            );

            DiscoveryFeedSize = _config.Bind(
                "Performance",
                "DiscoveryFeedSize",
                DiscoveryFeedPolicy.DefaultSize,
                new ConfigDescription(
                    "How many of the most recently updated CSFF mods on Nexus the New and updated filter on My Mods asks for in its one request, and the most rows it shows (mods you already have, have mapped or that are adult or not rated are left out of those). Used only when EnableNexusDiscovery is on. 1 to 50. The default is a starting value, not a measured one.",
                    new AcceptableValueRange<int>(DiscoveryFeedPolicy.MinSize, DiscoveryFeedPolicy.MaxSize))
            );
        }

        /// <summary>
        /// Checks if the API key is configured
        /// </summary>
        public bool HasApiKey => !string.IsNullOrEmpty(NexusApiKey.Value);

        /// <summary>
        /// True when an update check may run: a key is set, or the player turned on checks without one.
        /// </summary>
        public bool CanCheckForUpdates => PublicCheckPlan.CanCheck(HasApiKey, UsePublicEndpointWithoutKey.Value);

        /// <summary>
        /// The keep option as SuiteInstaller reads it, taken from the two settings each time it is asked
        /// (an Apply or a rollback reads it once, when it starts): off unless the player turned it on.
        /// </summary>
        public SuiteKeepPolicy KeepPolicy => new SuiteKeepPolicy(KeepPreviousSuiteVersion.Value, KeptVersions.Value);
    }
}
