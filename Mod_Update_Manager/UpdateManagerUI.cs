using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace mod_update_manager
{
    /// <summary>
    /// Unity IMGUI-based UI for displaying mod update status
    /// </summary>
    public class UpdateManagerUI : MonoBehaviour
    {
        private ModUpdateConfig _config;
        private UpdateChecker _updateChecker;
        private ModMappingManager _mappingManager;
        private NexusApiClient _nexusClient;
        private NexusModDiscovery _modDiscovery;
        private ConflictDetector _conflictDetector;
        private UpdateScheduler _updateScheduler;

        private bool _showWindow = false;
        private Rect _windowRect;
        private Vector2 _scrollPosition;
        private string _statusMessage = "Press Check Updates to scan for mod updates";
        private string _searchFilter = "";
        private int _selectedTab = 0;
        private string[] _tabNames = { "All Mods", "Updates Available", "Up to Date", "Unable to Check", "Conflicts", "Analytics", "Settings" };

        // Styles
        private GUIStyle _headerStyle;
        private GUIStyle _updateAvailableStyle;
        private GUIStyle _upToDateStyle;
        private GUIStyle _errorStyle;
        private GUIStyle _modNameStyle;
        private GUIStyle _windowStyle;
        private GUIStyle _boxStyle;
        private bool _stylesInitialized = false;
        private Texture2D _opaqueBackground;

        // Settings input
        private string _apiKeyInput = "";
        private string _newMappingModName = "";
        private string _newMappingNexusId = "";

        // Inline quick-map input (mod folder name -> nexus id text)
        private Dictionary<string, string> _inlineNexusIds = new Dictionary<string, string>();

        public void Initialize(ModUpdateConfig config, UpdateChecker updateChecker, ModMappingManager mappingManager,
            ConflictDetector conflictDetector, UpdateScheduler updateScheduler, NexusApiClient nexusClient,
            NexusModDiscovery modDiscovery = null)
        {
            _config = config;
            _updateChecker = updateChecker;
            _mappingManager = mappingManager;
            _nexusClient = nexusClient;
            _modDiscovery = modDiscovery;
            _conflictDetector = conflictDetector;
            _updateScheduler = updateScheduler;
            
            _windowRect = new Rect(
                (Screen.width - _config.WindowWidth.Value) / 2,
                (Screen.height - _config.WindowHeight.Value) / 2,
                _config.WindowWidth.Value,
                _config.WindowHeight.Value
            );

            _apiKeyInput = _config.NexusApiKey.Value;

            // Subscribe to events
            _updateChecker.OnStatusUpdate += (msg) => _statusMessage = msg;
            _updateChecker.OnAllChecksComplete += (mods) => 
            {
                var updates = mods.Count(m => m.NeedsUpdate);
                if (updates > 0)
                {
                    _selectedTab = 1; // Switch to updates tab
                }
            };
        }

        public void Toggle()
        {
            _showWindow = !_showWindow;
            if (_showWindow && _updateChecker.InstalledMods.Count == 0)
            {
                _updateChecker.ScanMods();
            }
        }

        public void Show()
        {
            _showWindow = true;
            if (_updateChecker.InstalledMods.Count == 0)
            {
                _updateChecker.ScanMods();
            }
        }

        public void Hide()
        {
            _showWindow = false;
        }

        private void OnGUI()
        {
            if (!_showWindow) return;

            InitializeStyles();

            // Draw opaque background
            GUI.color = Color.white;
            GUI.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 1f);

            _windowRect = GUILayout.Window(
                GetInstanceID(),
                _windowRect,
                DrawWindow,
                "Mod Update Manager",
                GUILayout.MinWidth(500),
                GUILayout.MinHeight(400)
            );
        }

        private void InitializeStyles()
        {
            if (_stylesInitialized) return;

            // Create opaque window style
            _windowStyle = new GUIStyle(GUI.skin.window);
            _windowStyle.normal.background = MakeOpaqueTexture(new Color(0.15f, 0.15f, 0.15f, 1f));
            _windowStyle.onNormal.background = _windowStyle.normal.background;

            // Create opaque box style for content areas
            _boxStyle = new GUIStyle(GUI.skin.box);
            _boxStyle.normal.background = MakeOpaqueTexture(new Color(0.2f, 0.2f, 0.2f, 1f));

            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            _updateAvailableStyle = new GUIStyle(GUI.skin.label)
            {
                normal = { textColor = new Color(1f, 0.6f, 0.2f) }, // Orange
                fontStyle = FontStyle.Bold
            };

            _upToDateStyle = new GUIStyle(GUI.skin.label)
            {
                normal = { textColor = new Color(0.4f, 0.9f, 0.4f) } // Green
            };

            _errorStyle = new GUIStyle(GUI.skin.label)
            {
                normal = { textColor = new Color(0.7f, 0.7f, 0.7f) } // Gray
            };

            _modNameStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold
            };

            _stylesInitialized = true;
        }

        /// <summary>
        /// Creates a solid color texture for opaque backgrounds
        /// </summary>
        private Texture2D MakeOpaqueTexture(Color color)
        {
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private void DrawWindow(int windowId)
        {
            // Draw solid background
            GUI.DrawTexture(new Rect(0, 0, _windowRect.width, _windowRect.height), 
                _opaqueBackground ?? (_opaqueBackground = MakeOpaqueTexture(new Color(0.18f, 0.18f, 0.18f, 1f))));

            GUILayout.BeginVertical();

            // Header with game version
            var gameVersion = ModScanner.GetGameVersion();
            GUILayout.Label($"Card Survival: Fantasy Forest · {gameVersion}", _headerStyle);
            GUILayout.Space(5);

            // Status bar
            GUILayout.BeginHorizontal("box");
            GUILayout.Label(_statusMessage);
            if (_updateChecker.IsChecking)
            {
                GUILayout.Label($"Progress: {_updateChecker.Progress:P0}", GUILayout.Width(100));
            }
            GUILayout.EndHorizontal();

            // Action buttons
            GUILayout.BeginHorizontal();
            
            GUI.enabled = !_updateChecker.IsChecking && _config.HasApiKey;
            if (GUILayout.Button("Check for Updates", GUILayout.Height(30)))
            {
                _updateChecker.ScanMods();
                _updateChecker.CheckAllMods(this);
            }
            GUI.enabled = true;

            if (GUILayout.Button("Refresh Mod List", GUILayout.Height(30)))
            {
                _updateChecker.ScanMods();
            }

            if (GUILayout.Button("Close", GUILayout.Height(30), GUILayout.Width(80)))
            {
                Hide();
            }
            
            GUILayout.EndHorizontal();

            if (!_config.HasApiKey)
            {
                GUILayout.Label("⚠️ Configure your Nexus API key in Settings to check for updates", _updateAvailableStyle);
            }

            GUILayout.Space(10);

            // Tabs
            _selectedTab = GUILayout.Toolbar(_selectedTab, _tabNames);
            
            GUILayout.Space(5);

            // Search filter (not for settings, conflicts, analytics, or advanced settings tabs)
            if (_selectedTab < 4)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Filter:", GUILayout.Width(50));
                _searchFilter = GUILayout.TextField(_searchFilter);
                if (GUILayout.Button("Clear", GUILayout.Width(50)))
                {
                    _searchFilter = "";
                }
                GUILayout.EndHorizontal();
            }

            // Content area
            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, "box");

            switch (_selectedTab)
            {
                case 0: DrawAllMods(); break;
                case 1: DrawUpdatesAvailable(); break;
                case 2: DrawUpToDate(); break;
                case 3: DrawUnableToCheck(); break;
                case 4: DrawConflicts(); break;
                case 5: DrawAnalytics(); break;
                case 6: DrawSettings(); break;
            }

            GUILayout.EndScrollView();

            GUILayout.EndVertical();

            // Make window draggable
            GUI.DragWindow(new Rect(0, 0, _windowRect.width, 25));
        }

        private void DrawAllMods()
        {
            var mods = _config.ShowOnlyUpdates.Value
                ? FilterMods(_updateChecker.GetModsNeedingUpdate())
                : FilterMods(_updateChecker.InstalledMods);
            DrawModList(mods);
        }

        private void DrawUpdatesAvailable()
        {
            var mods = FilterMods(_updateChecker.GetModsNeedingUpdate());
            if (mods.Count == 0)
            {
                GUILayout.Label("No updates available! All configured mods are up to date.", _upToDateStyle);
            }
            else
            {
                GUILayout.Label($"{mods.Count} mod(s) have updates available:", _updateAvailableStyle);
                DrawModList(mods);
            }
        }

        private void DrawUpToDate()
        {
            var mods = FilterMods(_updateChecker.GetUpToDateMods());
            if (mods.Count == 0)
            {
                GUILayout.Label("No mods have been checked yet.");
            }
            else
            {
                DrawModList(mods);
            }
        }

        private void DrawUnableToCheck()
        {
            var mods = FilterMods(_updateChecker.GetUncheckedMods());
            GUILayout.Label("These mods don't have a Nexus Mod ID configured:", _errorStyle);
            GUILayout.Label("Add mappings in the Settings tab, add 'NexusModId' to their ModInfo.json,");
            GUILayout.Label("or enter the Nexus ID directly below each mod.");
            GUILayout.Space(10);

            if (mods.Count == 0)
            {
                GUILayout.Label("All mods have been mapped!", _upToDateStyle);
                return;
            }

            foreach (var mod in mods.OrderBy(m => m.Name))
            {
                GUILayout.BeginVertical("box");

                GUILayout.BeginHorizontal();
                GUILayout.Label(mod.Name, _modNameStyle, GUILayout.Width(250));
                GUILayout.Label($"v{mod.Version}", _errorStyle, GUILayout.Width(100));
                GUILayout.FlexibleSpace();

                // Check if known registry has a suggestion
                var knownId = KnownModRegistry.GetNexusModId(mod.Name)
                    ?? KnownModRegistry.GetNexusModId(mod.FolderName);
                if (knownId != null)
                {
                    var knownName = KnownModRegistry.GetDisplayName(mod.Name)
                        ?? KnownModRegistry.GetDisplayName(mod.FolderName) ?? mod.Name;
                    GUILayout.Label($"Known: #{knownId}", _upToDateStyle, GUILayout.Width(100));
                }

                GUILayout.EndHorizontal();

                // Inline quick-map: text field + button
                if (!_inlineNexusIds.ContainsKey(mod.FolderName))
                    _inlineNexusIds[mod.FolderName] = knownId ?? "";

                GUILayout.BeginHorizontal();
                GUILayout.Label("  Nexus ID:", GUILayout.Width(80));
                _inlineNexusIds[mod.FolderName] = GUILayout.TextField(_inlineNexusIds[mod.FolderName], GUILayout.Width(80));
                if (GUILayout.Button("Save Mapping", GUILayout.Width(110)))
                {
                    var id = _inlineNexusIds[mod.FolderName].Trim();
                    if (!string.IsNullOrEmpty(id))
                    {
                        _mappingManager.SetMapping(mod.FolderName, id, mod.Name);
                        _statusMessage = $"Mapped {mod.Name} -> Nexus #{id}. Re-check to verify.";
                    }
                }
                GUILayout.Label("(Find the ID in the Nexus URL: /mods/##)", _errorStyle);
                GUILayout.EndHorizontal();

                GUILayout.EndVertical();
            }
        }

        private void DrawSettings()
        {
            GUILayout.Label("API Configuration", _headerStyle);
            GUILayout.Space(5);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Nexus API Key:", GUILayout.Width(100));
            _apiKeyInput = GUILayout.PasswordField(_apiKeyInput, '*');
            if (GUILayout.Button("Save", GUILayout.Width(60)))
            {
                _config.NexusApiKey.Value = _apiKeyInput;
                _statusMessage = "API key saved!";
            }
            GUILayout.EndHorizontal();

            GUILayout.Label("Get your API key from: https://www.nexusmods.com/users/myaccount?tab=api+access", _errorStyle);

            GUILayout.Space(20);
            GUILayout.Label("Behavior Settings", _headerStyle);
            GUILayout.Space(5);

            _config.CheckOnStartup.Value = GUILayout.Toggle(_config.CheckOnStartup.Value, "Check for updates on game startup");
            _config.ShowOnlyUpdates.Value = GUILayout.Toggle(_config.ShowOnlyUpdates.Value, "Show only mods with updates in main list");

            GUILayout.Space(20);
            GUILayout.Label("Add Mod Mapping", _headerStyle);
            GUILayout.Label("Map a local mod to its Nexus Mods page", _errorStyle);
            GUILayout.Space(5);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Mod Name:", GUILayout.Width(80));
            _newMappingModName = GUILayout.TextField(_newMappingModName, GUILayout.Width(200));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Nexus ID:", GUILayout.Width(80));
            _newMappingNexusId = GUILayout.TextField(_newMappingNexusId, GUILayout.Width(200));
            if (GUILayout.Button("Add Mapping", GUILayout.Width(100)))
            {
                if (!string.IsNullOrEmpty(_newMappingModName) && !string.IsNullOrEmpty(_newMappingNexusId))
                {
                    _mappingManager.SetMapping(_newMappingModName, _newMappingNexusId);
                    _statusMessage = $"Added mapping: {_newMappingModName} -> {_newMappingNexusId}";
                    _newMappingModName = "";
                    _newMappingNexusId = "";
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            GUILayout.Label("Current Mappings:", _modNameStyle);

            foreach (var mapping in _mappingManager.GetAllMappings().ToList())
            {
                GUILayout.BeginHorizontal("box");
                GUILayout.Label($"{mapping.LocalModName} → Nexus ID: {mapping.NexusModId}");
                if (GUILayout.Button("Remove", GUILayout.Width(60)))
                {
                    _mappingManager.RemoveMapping(mapping.LocalModName);
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(20);
            GUILayout.Label("Background Checking", _headerStyle);
            GUILayout.Space(5);

            _config.EnableBackgroundChecking.Value = GUILayout.Toggle(_config.EnableBackgroundChecking.Value, "Enable periodic update checking");

            if (_config.EnableBackgroundChecking.Value)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Check Interval (min):", GUILayout.Width(150));
                var intervalStr = GUILayout.TextField(_config.CheckIntervalMinutes.Value.ToString(), GUILayout.Width(50));
                if (int.TryParse(intervalStr, out int interval) && interval >= 10 && interval <= 1440)
                {
                    _config.CheckIntervalMinutes.Value = interval;
                }
                GUILayout.EndHorizontal();

                if (_updateScheduler.IsRunning)
                {
                    GUILayout.Label($"Next check: {_updateScheduler.GetTimeUntilNextCheckFormatted()}", _upToDateStyle);
                }
            }

            GUILayout.Space(20);
            GUILayout.Label("Analysis & Warnings", _headerStyle);
            GUILayout.Space(5);

            _config.ShowConflictWarnings.Value = GUILayout.Toggle(_config.ShowConflictWarnings.Value, "Show potential mod conflict warnings");

            GUILayout.Space(20);
            GUILayout.Label("Performance", _headerStyle);
            GUILayout.Space(5);

            _config.CachingEnabled.Value = GUILayout.Toggle(_config.CachingEnabled.Value, "Cache API responses");

            if (_config.CachingEnabled.Value)
            {
                var cacheSize = _nexusClient.GetCacheSize();
                GUILayout.Label($"Cached responses: {cacheSize}", _upToDateStyle);
            }

            _config.EnableNexusDiscovery.Value = GUILayout.Toggle(_config.EnableNexusDiscovery.Value, "Enable slow Nexus ID discovery");
            if (_config.EnableNexusDiscovery.Value)
            {
                var progress = _modDiscovery?.GetProgressInfo() ?? (0, 0);
                GUILayout.Label($"Discovery progress: scanned through #{progress.LastScannedId}, found {progress.TotalDiscovered}", _errorStyle);
            }
        }

        private void DrawConflicts()
        {
            if (!_config.ShowConflictWarnings.Value)
            {
                GUILayout.Label("Conflict warnings are disabled in Settings.", _errorStyle);
                return;
            }

            if (_updateChecker.InstalledMods.Count == 0)
            {
                GUILayout.Label("No mods installed.");
                return;
            }

            var conflicts = _conflictDetector.DetectConflicts(_updateChecker.InstalledMods);

            if (conflicts.Count == 0)
            {
                GUILayout.Label("No conflicts detected! ✓", _upToDateStyle);
                return;
            }

            GUILayout.Label($"Found {conflicts.Count} potential conflict(s):", _headerStyle);
            GUILayout.Space(5);

            foreach (var conflict in conflicts)
            {
                GUILayout.BeginHorizontal("box");

                var severityColor = conflict.Severity switch
                {
                    ConflictDetector.ConflictSeverity.Critical => _updateAvailableStyle,
                    ConflictDetector.ConflictSeverity.Warning => _modNameStyle,
                    _ => _upToDateStyle
                };

                GUILayout.Label($"[{conflict.Severity}] {conflict.ModA} <-> {conflict.ModB}", severityColor);
                GUILayout.EndHorizontal();

                GUILayout.Label($"  {conflict.Description}", _errorStyle);
                GUILayout.Label($"  Resolution: {_conflictDetector.GetConflictResolution(conflict)}", _errorStyle);
                GUILayout.Space(5);
            }
        }

        private void DrawAnalytics()
        {
            if (_updateChecker.InstalledMods.Count == 0)
            {
                GUILayout.Label("No mods installed.");
                return;
            }

            var stats = ModComparisonView.GenerateStats(_updateChecker.InstalledMods);

            GUILayout.Label("Update Statistics", _headerStyle);
            GUILayout.Space(5);

            GUILayout.BeginHorizontal("box");
            GUILayout.Label("Total Mods:");
            GUILayout.Label(stats.TotalMods.ToString(), _modNameStyle);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal("box");
            GUILayout.Label("Up to Date:");
            GUILayout.Label(stats.ModsUpToDate.ToString(), _upToDateStyle);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal("box");
            GUILayout.Label("Need Updates:");
            GUILayout.Label(stats.ModsNeedingUpdate.ToString(), _updateAvailableStyle);
            GUILayout.EndHorizontal();

            if (stats.CriticalUpdates > 0)
            {
                GUILayout.BeginHorizontal("box");
                GUILayout.Label("Critical Updates:");
                GUILayout.Label(stats.CriticalUpdates.ToString(), _updateAvailableStyle);
                GUILayout.EndHorizontal();
            }

            if (stats.ModsUnchecked > 0)
            {
                GUILayout.BeginHorizontal("box");
                GUILayout.Label("Unable to Check:");
                GUILayout.Label(stats.ModsUnchecked.ToString(), _errorStyle);
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(10);
            GUILayout.Label("Update Summary", _headerStyle);
            GUILayout.Space(5);

            if (stats.ModsNeedingUpdate == 0)
            {
                GUILayout.Label("All mods are up to date!", _upToDateStyle);
            }

            GUILayout.Space(10);

            if (_updateScheduler.IsRunning)
            {
                GUILayout.BeginHorizontal("box");
                GUILayout.Label("Background Checking:", _modNameStyle);
                GUILayout.Label($"Next check in {_updateScheduler.GetTimeUntilNextCheckFormatted()}", _upToDateStyle);
                GUILayout.EndHorizontal();
            }
        }

        private List<InstalledModInfo> FilterMods(List<InstalledModInfo> mods)
        {
            if (string.IsNullOrEmpty(_searchFilter))
                return mods;

            return mods.Where(m => 
                m.Name.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                m.FolderName.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (m.Author != null && m.Author.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0)
            ).ToList();
        }

        private void DrawModList(List<InstalledModInfo> mods)
        {
            if (mods.Count == 0)
            {
                GUILayout.Label("No mods found matching criteria.");
                return;
            }

            foreach (var mod in mods.OrderBy(m => m.Name))
            {
                DrawModEntry(mod);
            }
        }

        private void DrawModEntry(InstalledModInfo mod)
        {
            GUILayout.BeginVertical("box");

            GUILayout.BeginHorizontal();
            GUILayout.Label(mod.Name, _modNameStyle, GUILayout.Width(250));
            GUILayout.Label($"by {mod.Author}", GUILayout.Width(150));
            GUILayout.FlexibleSpace();

            // Version status
            if (mod.NeedsUpdate)
            {
                GUILayout.Label($"v{mod.Version} -> v{mod.LatestVersion}", _updateAvailableStyle);
            }
            else if (!mod.CheckFailed && !string.IsNullOrEmpty(mod.LatestVersion) && mod.LatestVersion != "No Nexus ID")
            {
                GUILayout.Label($"v{mod.Version} [OK]", _upToDateStyle);
            }
            else
            {
                GUILayout.Label($"v{mod.Version}", _errorStyle);
            }

            GUILayout.EndHorizontal();

            // Additional info row
            if (mod.NeedsUpdate)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("  UPDATE AVAILABLE", _updateAvailableStyle);
                GUILayout.FlexibleSpace();
                if (!string.IsNullOrEmpty(mod.NexusUrl))
                {
                    if (GUILayout.Button("Open Nexus Page", GUILayout.Width(130)))
                    {
                        Application.OpenURL(mod.NexusUrl);
                    }
                }
                GUILayout.EndHorizontal();
            }
            else if (mod.CheckFailed && !string.IsNullOrEmpty(mod.CheckError))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"  {mod.CheckError}", _errorStyle);
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
        }
    }
}
