using BepInEx.Logging;

namespace mod_update_manager;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
internal class Plugin : BaseUnityPlugin
{
    private const string PluginGuid = "crispywhips.mod_update_manager";
    public const string PluginName = "Mod_Update_Manager";
    public const string PluginVersion = "2.1.70";

    // PluginMetadataReader is host-free, so it carries BepInEx's DependencyFlags.SoftDependency value as
    // a constant of its own. A division by a constant zero is a compile error, so this line fails the
    // build, rather than the dependency reader, if BepInEx's own value ever differs from that constant.
    private const int SoftDependencyFlagMatchesBepInEx = 1 / (PluginMetadataReader.SoftDependencyFlag == (int)BepInDependency.DependencyFlags.SoftDependency ? 1 : 0);

    internal new static ManualLogSource Logger;
    public static Plugin Instance { get; private set; }
    private static Harmony _harmony;

    // Core components
    private ModUpdateConfig _config;
    private NexusApiClient _nexusClient;
    private ModMappingManager _mappingManager;
    private ModPreferences _preferences;
    private VersionLedger _versionLedger;
    private UpdateChecker _updateChecker;
    private UpdateManagerUI _ui;
    private NexusModDiscovery _modDiscovery;

    private ConflictDetector _conflictDetector;
    private UpdateScheduler _updateScheduler;

    private bool _initialCheckDone = false;

    // True from the moment OnGameDataLoaded starts the startup check until that check completes.
    // It is the only completion UpdateWindowPolicy lets open the window.
    private bool _startupCheckInFlight = false;
    private bool _autoOpenedThisSession = false;

    private void Awake()
    {
        Instance = this;
        Logger = base.Logger;
        // Before anything is constructed: ModMappingManager logs from its constructor.
        MumLog.Debug = m => Logger.LogDebug(m);
        MumLog.Info = m => Logger.LogInfo(m);
        MumLog.Warning = m => Logger.LogWarning(m);
        MumLog.Error = m => Logger.LogError(m);
        Logger.LogInfo($"{PluginName} v{PluginVersion} loaded.");

        // Initialize configuration
        _config = new ModUpdateConfig(Config);

        // Initialize core components
        _mappingManager = new ModMappingManager(_config.ModMappingsPath);
        _preferences = new ModPreferences(_config.PreferencesPath);
        _versionLedger = new VersionLedger(_config.VersionLogPath);
        _nexusClient = new NexusApiClient(_config.NexusApiKey.Value, this, _config.ResponseCachePath, _config.CachingEnabled.Value);
        _modDiscovery = new NexusModDiscovery(
            _nexusClient,
            Path.GetDirectoryName(_config.ModMappingsPath),
            _config.DiscoveryMaxScanId.Value,
            _config.DiscoveryMaxConsecutiveMisses.Value);
        // The public query is the first request of every check; without a key it runs only when the
        // player turned on UsePublicEndpointWithoutKey, read again at each check.
        _updateChecker = new UpdateChecker(_nexusClient, _mappingManager, _modDiscovery, _preferences,
            new NexusPublicClient(), () => _config.UsePublicEndpointWithoutKey.Value);

        _conflictDetector = new ConflictDetector();
        _updateScheduler = new UpdateScheduler(_updateChecker, this);

        // Create UI component
        _ui = gameObject.AddComponent<UpdateManagerUI>();
        _ui.Initialize(_config, _updateChecker, _mappingManager, _conflictDetector, _updateScheduler, _nexusClient, _modDiscovery, _preferences, _versionLedger);

        // Read embedded suite versions and installed versions for the Suite tab
        try
        {
            SuiteVersionReader.RefreshAll();
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"SuiteVersionReader.RefreshAll failed (Suite tab will show Unknown status): {ex}");
        }

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

        // Once per launch: what changed since the last one (the NEW chips and the History lists).
        System.Collections.Generic.List<VersionEvent> changes = null;
        try
        {
            changes = _versionLedger.Observe(_updateChecker.InstalledMods);
            Logger.LogDebug($"Version log: {changes.Count} change(s) since the last launch");
        }
        catch (System.Exception ex)
        {
            Logger.LogWarning($"Version log not updated this launch: {ex}");
        }

        // The scan is what shows a suite mod installed under another folder, so the Install & Update
        // rows are classified again with it (Awake read them before any scan existed).
        try
        {
            SuiteVersionReader.RefreshAll(_updateChecker.InstalledMods);
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"SuiteVersionReader.RefreshAll failed after the mod scan (the Install & Update rows keep their earlier status): {ex}");
        }

        // The upgrade handoff: right after the player replaced MUM with a newer one, open on Install &
        // Update with every installed suite mod this MUM bundles a newer copy of already ticked.
        try
        {
            RunUpgradeHandoff(changes);
        }
        catch (System.Exception ex)
        {
            Logger.LogWarning($"Upgrade handoff skipped: {ex}");
        }

        // Optional Nexus mod discovery walks IDs slowly and can use API quota.
        if (_config.HasApiKey && _config.EnableNexusDiscovery.Value)
        {
            _modDiscovery.StartDiscovery(this);
            Logger.LogDebug("Started Nexus mod discovery service");
        }

        // Start background checking if enabled
        if (_config.EnableBackgroundChecking.Value && _config.CanCheckForUpdates)
        {
            _updateScheduler.Start(_config.CheckIntervalMinutes.Value);
            Logger.LogDebug($"Background update checking enabled (interval: {_config.CheckIntervalMinutes.Value} minutes)");
        }

        // If configured, automatically check for updates
        if (_config.CheckOnStartup.Value && _config.CanCheckForUpdates && !_initialCheckDone)
        {
            _initialCheckDone = true;
            _startupCheckInFlight = true;
            Logger.LogDebug("Starting automatic update check...");
            _updateChecker.CheckAllMods(this, bypassCache: false);
        }
        else if (!_config.CanCheckForUpdates)
        {
            Logger.LogDebug($"No Nexus API key configured and checks without a key are off. Press {_config.ToggleUIKey.Value} to open the dashboard, then the Settings tab, to configure your API key.");
        }
    }

    private void OnAllChecksComplete(System.Collections.Generic.List<InstalledModInfo> mods)
    {
        // Only the startup check may open the window, and only once: scheduled checks and the
        // Re-check button refresh the counts and leave the window and its tab alone.
        var startupCheck = _startupCheckInFlight;
        _startupCheckInFlight = false;

        var updateCount = mods.FindAll(m => m.NeedsUpdate).Count;
        if (updateCount > 0)
        {
            Logger.LogDebug($"Found {updateCount} mod(s) with updates available!");
        }

        LeaveHandoffNote(mods);

        if (UpdateWindowPolicy.ShouldAutoOpen(_config.OpenWindowOnUpdates.Value, startupCheck, _autoOpenedThisSession, updateCount))
        {
            _autoOpenedThisSession = true;
            _ui.ShowForUpdates();
        }
    }

    private void RunUpgradeHandoff(System.Collections.Generic.List<VersionEvent> changes)
    {
        var note = MumHandoff.Read(_config.HandoffPath);
        var from = MumHandoff.UpgradedFrom(note, changes, PluginVersion);
        if (from == null) return;

        var plan = MumHandoff.Plan(from, MumHandoff.IsForThisMum(note, PluginVersion) ? note : null, SuiteModRegistry.All);
        Logger.LogInfo($"Upgraded from Mod Update Manager v{from}: {plan.Ticked.Count} suite mod(s) ticked for update" +
            (plan.StillBehind.Count > 0 ? $", {plan.StillBehind.Count} still behind Nexus ({string.Join(", ", plan.StillBehind.ToArray())})" : "") + ".");
        _ui.ShowForHandoff(plan);
        // Acted on: the next start is not an upgrade. A note this MUM leaves later replaces it.
        MumHandoff.Delete(_config.HandoffPath);
    }

    // After any check that read MUM's own Nexus version: leave the note for the newer MUM when there is
    // one, and remove a note this version left when Nexus no longer lists anything newer.
    private void LeaveHandoffNote(System.Collections.Generic.List<InstalledModInfo> mods)
    {
        var latest = _updateChecker.LatestMumVersion;
        if (string.IsNullOrWhiteSpace(latest)) return;

        if (MumHandoff.NewerMumAvailable(PluginVersion, latest))
        {
            var seen = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.KeyValuePair<string, string>>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var m in mods)
                if (m != null && !string.IsNullOrEmpty(m.FolderName) && !seen.ContainsKey(m.FolderName))
                    seen[m.FolderName] = new System.Collections.Generic.KeyValuePair<string, string>(m.Version, m.LatestVersion);
            var note = MumHandoff.BuildNote(PluginVersion, latest, System.DateTime.UtcNow, SuiteModRegistry.All, seen);
            if (MumHandoff.Write(_config.HandoffPath, note))
                Logger.LogInfo($"Mod Update Manager v{latest} is on Nexus (this is v{PluginVersion}); left a handoff note naming {note.Mods.Count} suite mod(s) with newer versions.");
        }
        else
        {
            var old = MumHandoff.Read(_config.HandoffPath);
            if (old != null && !MumHandoff.IsForThisMum(old, PluginVersion)) MumHandoff.Delete(_config.HandoffPath);
        }
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }
}
