using System.IO;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace mod_update_manager;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
internal class Plugin : BaseUnityPlugin
{
    private const string PluginGuid = "crispywhips.mod_update_manager";
    public const string PluginName = "Mod_Update_Manager";
    public const string PluginVersion = "2.0.3";

    internal new static ManualLogSource Logger;
    public static Plugin Instance { get; private set; }
    private static Harmony _harmony;

    // Core components
    private ModUpdateConfig _config;
    private NexusApiClient _nexusClient;
    private ModMappingManager _mappingManager;
    private UpdateChecker _updateChecker;
    private UpdateManagerUI _ui;
    private NexusModDiscovery _modDiscovery;

    private ConflictDetector _conflictDetector;
    private UpdateScheduler _updateScheduler;

    private bool _initialCheckDone = false;

    private void Awake()
    {
        Instance = this;
        Logger = base.Logger;
        Logger.LogInfo($"{PluginName} v{PluginVersion} loaded.");

        // Initialize configuration
        _config = new ModUpdateConfig(Config);

        // Initialize core components
        _mappingManager = new ModMappingManager(_config.ModMappingsPath);
        _nexusClient = new NexusApiClient(_config.NexusApiKey.Value, this, _config.ResponseCachePath, _config.CachingEnabled.Value);
        _modDiscovery = new NexusModDiscovery(
            _nexusClient,
            Path.GetDirectoryName(_config.ModMappingsPath),
            _config.DiscoveryMaxScanId.Value,
            _config.DiscoveryMaxConsecutiveMisses.Value);
        _updateChecker = new UpdateChecker(_nexusClient, _mappingManager, _modDiscovery);

        _conflictDetector = new ConflictDetector();
        _updateScheduler = new UpdateScheduler(_updateChecker, this);

        // Create UI component
        _ui = gameObject.AddComponent<UpdateManagerUI>();
        _ui.Initialize(_config, _updateChecker, _mappingManager, _conflictDetector, _updateScheduler, _nexusClient, _modDiscovery);

        // Subscribe to config changes
        _config.NexusApiKey.SettingChanged += (sender, args) =>
        {
            _nexusClient.SetApiKey(_config.NexusApiKey.Value);
            if (!_config.HasApiKey)
            {
                _modDiscovery.StopDiscovery();
            }
            else if (_config.EnableNexusDiscovery.Value)
            {
                _modDiscovery.StartDiscovery(this);
            }
            Logger.LogDebug("API key updated");
        };

        _config.CachingEnabled.SettingChanged += (sender, args) =>
        {
            _nexusClient.SetCachingEnabled(_config.CachingEnabled.Value, _config.ResponseCachePath);
            Logger.LogDebug($"Nexus response caching {(_config.CachingEnabled.Value ? "enabled" : "disabled")}");
        };

        _config.EnableNexusDiscovery.SettingChanged += (sender, args) =>
        {
            if (_config.EnableNexusDiscovery.Value && _config.HasApiKey)
            {
                _modDiscovery.StartDiscovery(this);
            }
            else
            {
                _modDiscovery.StopDiscovery();
            }
        };

        _config.EnableBackgroundChecking.SettingChanged += (sender, args) =>
        {
            if (_config.EnableBackgroundChecking.Value)
            {
                _updateScheduler.Start(_config.CheckIntervalMinutes.Value);
            }
            else
            {
                _updateScheduler.Stop();
            }
        };

        // Subscribe to update check results
        _updateChecker.OnAllChecksComplete += OnAllChecksComplete;

        // Initialize and apply Harmony patches
        _harmony = new Harmony(PluginGuid);
        try
        {
            mod_update_manager.Patcher.GameLoadPatch.ApplyPatch(_harmony);
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Failed to apply Harmony patches: {ex}");
        }
    }

    private void Start()
    {
        Logger.LogDebug($"Press {_config.ToggleUIKey.Value} to open the Mod Update Manager");
    }

    private void Update()
    {
        // Handle hotkey in the main plugin Update
        if (Input.GetKeyDown(_config.ToggleUIKey.Value))
        {
            Logger.LogDebug("Toggle key pressed!");
            _ui.Toggle();
        }

        // Update scheduled checking
        if (_config.EnableBackgroundChecking.Value)
        {
            _updateScheduler.Update();
        }
    }

    /// <summary>
    /// Called by GameLoadPatch after game data has loaded
    /// </summary>
    public void OnGameDataLoaded()
    {
        Logger.LogDebug("Game data loaded - initializing mod update check");

        // Scan installed mods
        _updateChecker.ScanMods();

        // Optional Nexus mod discovery walks IDs slowly and can use API quota.
        if (_config.HasApiKey && _config.EnableNexusDiscovery.Value)
        {
            _modDiscovery.StartDiscovery(this);
            Logger.LogDebug("Started Nexus mod discovery service");
        }

        // Start background checking if enabled
        if (_config.EnableBackgroundChecking.Value && _config.HasApiKey)
        {
            _updateScheduler.Start(_config.CheckIntervalMinutes.Value);
            Logger.LogDebug($"Background update checking enabled (interval: {_config.CheckIntervalMinutes.Value} minutes)");
        }

        // If configured, automatically check for updates
        if (_config.CheckOnStartup.Value && _config.HasApiKey && !_initialCheckDone)
        {
            _initialCheckDone = true;
            Logger.LogDebug("Starting automatic update check...");
            _updateChecker.CheckAllMods(this);
        }
        else if (!_config.HasApiKey)
        {
            Logger.LogDebug("No Nexus API key configured. Press F8 to open settings and configure your API key.");
        }
    }

    private void OnAllChecksComplete(System.Collections.Generic.List<InstalledModInfo> mods)
    {
        var updateCount = mods.FindAll(m => m.NeedsUpdate).Count;
        if (updateCount > 0)
        {
            Logger.LogDebug($"Found {updateCount} mod(s) with updates available!");
            _ui.Show();
        }
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }
}
