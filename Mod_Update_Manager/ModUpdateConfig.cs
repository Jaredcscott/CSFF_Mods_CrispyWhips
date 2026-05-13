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
        public ConfigEntry<bool> CheckOnStartup { get; private set; }
        public ConfigEntry<bool> ShowOnlyUpdates { get; private set; }
        public ConfigEntry<KeyCode> ToggleUIKey { get; private set; }
        public ConfigEntry<float> WindowWidth { get; private set; }
        public ConfigEntry<float> WindowHeight { get; private set; }

        // New features
        public ConfigEntry<int> CheckIntervalMinutes { get; private set; }
        public ConfigEntry<bool> EnableBackgroundChecking { get; private set; }
        public ConfigEntry<bool> ShowConflictWarnings { get; private set; }
        public ConfigEntry<bool> CachingEnabled { get; private set; }
        public ConfigEntry<bool> EnableNexusDiscovery { get; private set; }
        public ConfigEntry<int> DiscoveryMaxScanId { get; private set; }
        public ConfigEntry<int> DiscoveryMaxConsecutiveMisses { get; private set; }

        // Mod ID mappings file path
        public string ModMappingsPath { get; private set; }
        public string ResponseCachePath { get; private set; }

        public ModUpdateConfig(ConfigFile config)
        {
            _config = config;
            ModMappingsPath = Path.Combine(Paths.ConfigPath, "ModUpdateManager_Mappings.json");
            ResponseCachePath = Path.Combine(Paths.ConfigPath, "ModUpdateManager_Cache.json");
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

            // Behavior Settings
            CheckOnStartup = _config.Bind(
                "Behavior",
                "CheckOnStartup",
                true,
                "Automatically check for updates when the game loads"
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
                KeyCode.F8,
                "Key to toggle the update manager window"
            );

            WindowWidth = _config.Bind(
                "UI",
                "WindowWidth",
                600f,
                "Width of the update manager window"
            );

            WindowHeight = _config.Bind(
                "UI",
                "WindowHeight",
                500f,
                "Height of the update manager window"
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
                "Opt in to a slow background scan of Nexus mod IDs to discover mappings for mods that do not declare NexusModId. Leave disabled unless you specifically need automatic discovery."
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
        }

        /// <summary>
        /// Checks if the API key is configured
        /// </summary>
        public bool HasApiKey => !string.IsNullOrEmpty(NexusApiKey.Value);
    }
}
