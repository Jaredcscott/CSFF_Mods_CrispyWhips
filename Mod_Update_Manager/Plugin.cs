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
    public const string PluginVersion = "2.0.0";

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

    // New feature managers
    private BackupManager _backupManager;
    private VersionHistoryManager _versionHistoryManager;
    private IgnoreFavoriteManager _ignoreFavoriteManager;
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
        _nexusClient = new NexusApiClient(_config.NexusApiKey.Value, this, _config.ResponseCachePath);
        _modDiscovery = new NexusModDiscovery(_nexusClient, Path.GetDirectoryName(_config.ModMappingsPath));
        _updateChecker = new UpdateChecker(_nexusClient, _mappingManager, _modDiscovery);

        // Initialize new feature managers
        _backupManager = new BackupManager(_config.BackupPath);
        _versionHistoryManager = new VersionHistoryManager(_config.VersionHistoryPath);
        _ignoreFavoriteManager = new IgnoreFavoriteManager(_config.IgnoreFavoritePath);
        _conflictDetector = new ConflictDetector();
        _updateScheduler = new UpdateScheduler(_updateChecker, this);

        // Create UI component
        _ui = gameObject.AddComponent<UpdateManagerUI>();
        _ui.Initialize(_config, _updateChecker, _mappingManager, _backupManager,
            _versionHistoryManager, _ignoreFavoriteManager, _conflictDetector, _updateScheduler, _nexusClient, _modDiscovery);

        // Subscribe to config changes
        _config.NexusApiKey.SettingChanged += (sender, args) =>
        {
            _nexusClient.SetApiKey(_config.NexusApiKey.Value);
            Logger.LogInfo("API key updated");
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
        Logger.LogInfo( $"Press {_config.ToggleUIKey.Value} to open the Mod Update Manager");
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
        Logger.LogInfo("Game data loaded - initializing mod update check");

        // Scan installed mods
        _updateChecker.ScanMods();

        // Start Nexus mod discovery in background (runs continuously, discovers mods by ID)
        if (_config.HasApiKey)
        {
            _modDiscovery.StartDiscovery(this);
            Logger.LogInfo("Started Nexus mod discovery service");
        }

        // Start background checking if enabled
        if (_config.EnableBackgroundChecking.Value && _config.HasApiKey)
        {
            _updateScheduler.Start(_config.CheckIntervalMinutes.Value);
            Logger.LogInfo($"Background update checking enabled (interval: {_config.CheckIntervalMinutes.Value} minutes)");
        }

        // If configured, automatically check for updates
        if (_config.CheckOnStartup.Value && _config.HasApiKey && !_initialCheckDone)
        {
            _initialCheckDone = true;
            Logger.LogInfo("Starting automatic update check...");
            _updateChecker.CheckAllMods(this);
        }
        else if (!_config.HasApiKey)
        {
            Logger.LogInfo("No Nexus API key configured. Press F8 to open settings and configure your API key.");
        }
    }

    private void OnAllChecksComplete(System.Collections.Generic.List<InstalledModInfo> mods)
    {
        var updateCount = mods.FindAll(m => m.NeedsUpdate).Count;
        if (updateCount > 0)
        {
            Logger.LogInfo($"Found {updateCount} mod(s) with updates available!");
            _ui.Show();
        }
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }
}
