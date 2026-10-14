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
        private ModPreferences _preferences;
        private VersionLedger _versionLedger;   // the NEW chips and per-mod history (plan feature 19); null leaves both out
        private HashSet<string> _historyExpanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private bool _showWindow = false;
        private Rect _windowRect;
        private Vector2 _scrollPosition;
        private string _statusMessage = null; // null until a status arrives: the first-run line is read when drawn
        private string _searchFilter = "";
        private ModSortMode _sortMode = ModSorter.DefaultMode; // the My Mods list order, saved by ModPreferences
        private int _selectedTab = 0;
        private string[] TabNames => new[] { MumText.Get("InstallUpdateHeader"), MumText.Get("TabMyMods"), MumText.Get("TabConflicts"), MumText.Get("TabSettings") };
        private int _myModsFilterMode = 0; // 0=All, 1=Updates Available, 2=Up to Date, 3=Unmapped, 4=New and updated (only with EnableNexusDiscovery on)

        // Styles
        private GUIStyle _headerStyle;
        private GUIStyle _updateAvailableStyle;
        private GUIStyle _majorUpdateStyle;
        private GUIStyle _upToDateStyle;
        private GUIStyle _errorStyle;
        private GUIStyle _modNameStyle;
        private GUIStyle _favoriteStyle;
        private GUIStyle _ignoredStyle;
        private GUIStyle _summaryStyle;
        private GUIStyle _restartStyle;
        private GUIStyle _suiteNoteStyle;
        private GUIStyle _windowStyle;
        private GUIStyle _boxStyle;
        private bool _stylesInitialized = false;
        private Texture2D _opaqueBackground;

        // Settings input
        private string _apiKeyInput = "";
        private bool _apiKeyChecking = false;          // a key check is in flight; Save waits for it
        private ApiKeyResult? _apiKeyResult = null;    // the last check's verdict, null before one or after an edit
        private string _apiKeyResultText = "";
        private string _newMappingModName = "";
        private string _newMappingNexusId = "";

        // Inline quick-map input (mod folder name -> nexus id text)
        private Dictionary<string, string> _inlineNexusIds = new Dictionary<string, string>();

        // All Mods tab filter state
        private bool _showIgnored = false;
        private bool _favoritesOnly = false;

        // Per-mod expanded state: changelog, notes
        private Dictionary<string, bool> _changelogExpanded = new Dictionary<string, bool>();
        private Dictionary<string, bool> _notesExpanded = new Dictionary<string, bool>();
        private Dictionary<string, string> _notesInput = new Dictionary<string, string>();
        private Dictionary<string, Vector2> _changelogScroll = new Dictionary<string, Vector2>();

        // Changelog cache: folderName -> (entries, error)
        private Dictionary<string, (List<(string Version, List<string> Entries)> data, string error)> _changelogCache
            = new Dictionary<string, (List<(string, List<string>)>, string)>();
        private HashSet<string> _changelogFetching = new HashSet<string>();

        // Tested-build chip, parsed once when a changelog arrives: folderName -> (token, verdict).
        // A missing key means the changelog carries no "Compatible with ... EA x.y" line.
        private Dictionary<string, (string Token, CompatibilityChip.Verdict Verdict)> _testedBuildCache
            = new Dictionary<string, (string, CompatibilityChip.Verdict)>();

        // Export text
        private string _exportText = "";
        private Vector2 _exportScroll;

        // Suite tab state
        private HashSet<string> _suiteSelected = new HashSet<string>();
        private bool _suiteApplying = false;
        private bool _suitePendingRestart = false;
        private HashSet<string> _suiteFailed = new HashSet<string>();   // FolderNames the last Apply failed or held back; Retry failed re-applies them
        private HashSet<string> _suiteDeclined = new HashSet<string>(); // FolderNames of required rows the player cleared (Clear beside "also installing"); a tick, a Select button or a retry forgets them
        private string _suiteStatusMessage = "";
        private string _suiteDetailTarget = null;   // FolderName of the row whose details are shown
        private Vector2 _suiteScroll;
        private Vector2 _suiteDetailScroll;   // the details panel's own scroll position, reset when another row's panel opens
        private string _suiteDetailKey;       // "<FolderName>|<cap>" that _suiteDetailText was resolved for
        private string _suiteDetailText;

        public void Initialize(ModUpdateConfig config, UpdateChecker updateChecker, ModMappingManager mappingManager,
            ConflictDetector conflictDetector, UpdateScheduler updateScheduler, NexusApiClient nexusClient,
            NexusModDiscovery modDiscovery = null, ModPreferences preferences = null, VersionLedger versionLedger = null)
        {
            BindText();
            _versionLedger = versionLedger;
            _config = config;
            _updateChecker = updateChecker;
            _mappingManager = mappingManager;
            _nexusClient = nexusClient;
            _modDiscovery = modDiscovery;
            _conflictDetector = conflictDetector;
            _updateScheduler = updateScheduler;
            _preferences = preferences;
            _searchFilter = _preferences?.GetSearchFilter() ?? "";
            _sortMode = _preferences?.GetSortMode() ?? ModSorter.DefaultMode;

            _windowRect = new Rect(
                (Screen.width - _config.WindowWidth.Value) / 2,
                (Screen.height - _config.WindowHeight.Value) / 2,
                _config.WindowWidth.Value,
                _config.WindowHeight.Value
            );

            _apiKeyInput = _config.NexusApiKey.Value;

            _updateChecker.OnStatusUpdate += (msg) => _statusMessage = msg;
            // No OnAllChecksComplete handler here: a finished check never moves the player to another
            // tab. Plugin.OnAllChecksComplete calls ShowForUpdates for the one automatic open.
        }

        // Points MumText at the game's own dictionary (LocalizationManager.GetText, reached by reflection so MUM keeps
        // no typed game reference) and at a glyph check on the window font.
        private static void BindText()
        {
            System.Reflection.MethodInfo getText = null;
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name != "Assembly-CSharp") continue;
                    var type = asm.GetType("LocalizationManager");
                    if (type != null)
                        getText = type.GetMethod("GetText", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                            null, new[] { typeof(string), typeof(string).MakeByRefType() }, null);
                    break;
                }
            }
            catch (Exception ex) { MumLog.Warning("MumText: could not find LocalizationManager.GetText, so text stays English: " + ex.Message); }

            if (getText == null)
            {
                MumLog.Warning("MumText: LocalizationManager.GetText was not found, so text stays English.");
                return;
            }

            MumText.SetLookup(key =>
            {
                var args = new object[] { key, null };
                var ok = getText.Invoke(null, args);
                return ok is bool b && b ? args[1] as string : null;
            });
            // GUI.skin may only be read inside OnGUI: read here, from the update check's coroutine, it threw, the
            // check answered false, and every status line the check wrote stayed English in a Chinese game. So the
            // window font is read once by OnGUI (CaptureGuiFont) and this check only uses that captured value,
            // falling back to the OS Arial (as it did when the skin had no font) until the first OnGUI has run.
            MumText.SetGlyphCheck(character =>
            {
                var font = _guiFont;
                if (font == null)
                {
                    if (_fallbackFont == null) _fallbackFont = Font.CreateDynamicFontFromOSFont("Arial", 14);
                    font = _fallbackFont;
                }
                if (font == null || string.IsNullOrEmpty(character)) return false;
                font.RequestCharactersInTexture(character, 14);
                return font.HasCharacter(character[0]);
            });
        }

        public void Toggle()
        {
            _showWindow = !_showWindow;
            if (_showWindow) MumText.ResetCache();
            if (_showWindow && _updateChecker.InstalledMods.Count == 0)
                _updateChecker.ScanMods();
        }

        public void Show()
        {
            MumText.ResetCache();
            _showWindow = true;
            if (_updateChecker.InstalledMods.Count == 0)
                _updateChecker.ScanMods();
        }

        /// <summary>
        /// The one automatic open, after the startup check found updates (Plugin.OnAllChecksComplete,
        /// once UpdateWindowPolicy agrees). Opens the window on My Mods with the Updates Available
        /// filter, unless the player already had it open, in which case it stays on their tab.
        /// </summary>
        public void ShowForUpdates()
        {
            var wasOpen = _showWindow;
            Show();
            if (wasOpen) return;
            _selectedTab = 1;
            _myModsFilterMode = 1;
        }

        public void Hide() => _showWindow = false;

        private static Font _guiFont;        // the window skin's font, read on the first OnGUI (GUI.skin is OnGUI-only)
        private static bool _guiFontCaptured;
        private static Font _fallbackFont;    // OS Arial, used by the glyph check when the skin has no font or before the first OnGUI

        // Reads the skin font once, inside OnGUI where GUI.skin is allowed, and forgets the looked-up answers so any
        // string decided against the fallback font before this is asked again against the real one.
        private static void CaptureGuiFont()
        {
            if (_guiFontCaptured) return;
            _guiFontCaptured = true;
            _guiFont = GUI.skin != null ? GUI.skin.font : null;
            MumText.ResetCache();
        }

        private void OnGUI()
        {
            CaptureGuiFont();
            if (!_showWindow) return;
            InitializeStyles();
            GUI.color = Color.white;
            GUI.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 1f);
            _windowRect = GUILayout.Window(
                GetInstanceID(),
                _windowRect,
                DrawWindow,
                MumText.Get("Name"),
                GUILayout.MinWidth(500),
                GUILayout.MinHeight(400)
            );
        }

        private void InitializeStyles()
        {
            if (_stylesInitialized) return;

            _windowStyle = new GUIStyle(GUI.skin.window);
            _windowStyle.normal.background = MakeOpaqueTexture(new Color(0.15f, 0.15f, 0.15f, 1f));
            _windowStyle.onNormal.background = _windowStyle.normal.background;

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
                normal = { textColor = new Color(1f, 0.6f, 0.2f) },
                fontStyle = FontStyle.Bold
            };

            _majorUpdateStyle = new GUIStyle(GUI.skin.label)
            {
                normal = { textColor = new Color(1f, 0.3f, 0.3f) },
                fontStyle = FontStyle.Bold
            };

            _upToDateStyle = new GUIStyle(GUI.skin.label)
            {
                normal = { textColor = new Color(0.4f, 0.9f, 0.4f) }
            };

            _errorStyle = new GUIStyle(GUI.skin.label)
            {
                normal = { textColor = new Color(0.7f, 0.7f, 0.7f) }
            };

            _modNameStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold
            };

            _favoriteStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.85f, 0.2f) }
            };

            _ignoredStyle = new GUIStyle(GUI.skin.label)
            {
                normal = { textColor = new Color(0.45f, 0.45f, 0.45f) }
            };

            _summaryStyle = new GUIStyle(GUI.skin.label)
            {
                normal = { textColor = new Color(0.75f, 0.75f, 0.75f) },
                wordWrap = true,
                fontSize = GUI.skin.label.fontSize - 1
            };

            // The suite tab's notes (the skew line and the cleared-row warnings): orange like
            // _updateAvailableStyle, but wrapped, because they are sentences.
            _suiteNoteStyle = new GUIStyle(_updateAvailableStyle)
            {
                wordWrap = true,
                fontSize = GUI.skin.label.fontSize - 1
            };

            _stylesInitialized = true;
        }

        private Texture2D MakeOpaqueTexture(Color color)
        {
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private void DrawWindow(int windowId)
        {
            GUI.DrawTexture(new Rect(0, 0, _windowRect.width, _windowRect.height),
                _opaqueBackground ?? (_opaqueBackground = MakeOpaqueTexture(new Color(0.18f, 0.18f, 0.18f, 1f))));

            GUILayout.BeginVertical();

            var gameVersion = ModScanner.GetGameVersion();
            GUILayout.Label(string.Format(MumText.Get("GameVersionLine"), gameVersion), _headerStyle);
            GUILayout.Space(5);

            GUILayout.BeginHorizontal("box");
            GUILayout.Label(_statusMessage ?? MumText.Get("StatusInitial"));
            if (_updateChecker.IsChecking)
                GUILayout.Label(string.Format(MumText.Get("ProgressFormat"), _updateChecker.Progress.ToString("P0")), GUILayout.Width(100));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUI.enabled = !_updateChecker.IsChecking && _config.CanCheckForUpdates;
            if (GUILayout.Button(MumText.Get("CheckUpdates"), GUILayout.Height(30)))
            {
                _updateChecker.ScanMods();
                // A button press asks Nexus, so a release from this morning shows; only startup and
                // scheduled checks use the 24-hour cache.
                _updateChecker.CheckAllMods(this, bypassCache: true);
            }
            GUI.enabled = true;

            if (GUILayout.Button(MumText.Get("RefreshModList"), GUILayout.Height(30)))
            {
                _updateChecker.ScanMods();
                RefreshSuiteRows();
            }

            if (GUILayout.Button(MumText.Get("Close"), GUILayout.Height(30), GUILayout.Width(80)))
                Hide();
            GUILayout.EndHorizontal();

            if (!_config.CanCheckForUpdates)
                GUILayout.Label(MumText.Get("ConfigureKeyPrompt"), _updateAvailableStyle);

            GUILayout.Space(10);

            _selectedTab = GUILayout.Toolbar(_selectedTab, TabNames);
            GUILayout.Space(5);

            // The search box filters the installed mods, which the New and updated list is not made of.
            if (_selectedTab == 1 && !FeedFilterShown)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(MumText.Get("FilterLabel"), GUILayout.Width(50));
                var newSearchFilter = GUILayout.TextField(_searchFilter);
                if (newSearchFilter != _searchFilter)
                {
                    _searchFilter = newSearchFilter;
                    _preferences?.SetSearchFilter(_searchFilter);
                }
                if (GUILayout.Button(MumText.Get("ClearRequired"), GUILayout.Width(50)))
                {
                    _searchFilter = "";
                    _preferences?.SetSearchFilter(_searchFilter);
                }
                GUILayout.EndHorizontal();
            }

            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, "box");

            switch (_selectedTab)
            {
                case 0: DrawSuiteTab(); break;
                case 1: DrawMyMods(); break;
                case 2: DrawConflicts(); break;
                case 3: DrawSettingsAndAnalytics(); break;
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUI.DragWindow(new Rect(0, 0, _windowRect.width, 25));
        }

        // ── Tabs ────────────────────────────────────────────────────────────

        private void DrawMyMods()
        {
            // The fifth filter exists only with discovery on; turning discovery off while it is selected
            // returns to All.
            var feedOn = _config.EnableNexusDiscovery.Value;
            if (!feedOn && _myModsFilterMode == FeedFilterMode) _myModsFilterMode = 0;

            GUILayout.BeginHorizontal();
            _myModsFilterMode = GUILayout.Toolbar(_myModsFilterMode, feedOn ? MyModsFilterLabelsWithFeed : MyModsFilterLabels);
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            DrawRecheckRateLimitedBanner();
            DrawFrameworkStaleBanner();

            switch (_myModsFilterMode)
            {
                case 0: DrawAllMods(); break;
                case 1: DrawUpdatesAvailable(); break;
                case 2: DrawUpToDate(); break;
                case 3: DrawUnableToCheck(); break;
                case FeedFilterMode: DrawNewAndUpdated(); break;
            }
        }

        // ── New and updated (plan feature 16) ───────────────────────────────
        // The fifth My Mods filter, behind EnableNexusDiscovery: the most recently updated CSFF mods on Nexus
        // that the player does not have. One list request (NexusPublicClient.FetchFeed), made when the filter
        // is first opened in a game session and again only by its Refresh button, never at startup and never
        // from a check; the answer lives in these fields and nowhere else (no file, no response cache), and
        // nothing is downloaded. Rows Mod_Update_Manager_DiscoveryFeedTab, _DiscoveryFeedHeader ({0}=shown,
        // {1}=listed, {2}=already installed or mapped, {3}=adult or not rated), _DiscoveryFeedLoading,
        // _DiscoveryFeedFailed, _DiscoveryFeedBlocked, _DiscoveryFeedRefresh, _DiscoveryFeedOpenPage and
        // _DiscoveryFeedNote in Localization/SimpEn.csv and SimpCn.csv; nothing reads them until runtime
        // localization lands.
        private const int FeedFilterMode = 4;
        private static string DiscoveryFeedTabText => MumText.Get("DiscoveryFeedTab");
        private static string DiscoveryFeedHeaderFormat => MumText.Get("DiscoveryFeedHeader");
        private static string DiscoveryFeedLoadingText => MumText.Get("DiscoveryFeedLoading");
        private static string DiscoveryFeedFailedText => MumText.Get("DiscoveryFeedFailed");
        private static string DiscoveryFeedBlockedText => MumText.Get("DiscoveryFeedBlocked");
        private static string DiscoveryFeedRefreshText => MumText.Get("DiscoveryFeedRefresh");
        private static string DiscoveryFeedOpenPageText => MumText.Get("DiscoveryFeedOpenPage");
        private static string DiscoveryFeedNoteText => MumText.Get("DiscoveryFeedNote");

        private static string[] MyModsFilterLabels => new[] { MumText.Get("FilterAll"), MumText.Get("FilterUpdates"), MumText.Get("FilterUpToDate"), MumText.Get("FilterUnmapped") };
        private static string[] MyModsFilterLabelsWithFeed => new[] { MumText.Get("FilterAll"), MumText.Get("FilterUpdates"), MumText.Get("FilterUpToDate"), MumText.Get("FilterUnmapped"), DiscoveryFeedTabText };

        private readonly NexusPublicClient _publicClient = new NexusPublicClient();
        private bool _feedRequested = false;   // a list request has gone out in this game session
        private bool _feedLoading = false;     // one is in flight
        private int _feedAskedSize = DiscoveryFeedPolicy.DefaultSize;   // the clamped size that request asked for
        private NexusPublicAnswer _feedAnswer = null;

        private bool FeedFilterShown => _selectedTab == 1 && _myModsFilterMode == FeedFilterMode && _config.EnableNexusDiscovery.Value;

        private void DrawNewAndUpdated()
        {
            var discoveryOn = _config.EnableNexusDiscovery.Value;
            var canCheck = _config.CanCheckForUpdates;

            if (!canCheck)
            {
                GUILayout.Label(DiscoveryFeedBlockedText, _updateAvailableStyle);
                return;
            }

            if (DiscoveryFeedPolicy.ShouldSendOnOpen(discoveryOn, canCheck, _feedRequested, _feedLoading))
                StartFeedRequest();

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUI.enabled = DiscoveryFeedPolicy.MaySend(discoveryOn, canCheck, _feedLoading);
            if (GUILayout.Button(DiscoveryFeedRefreshText, GUILayout.Width(90)))
                StartFeedRequest();
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            if (_feedLoading || _feedAnswer == null)
            {
                GUILayout.Label(DiscoveryFeedLoadingText, _errorStyle);
                return;
            }
            if (!_feedAnswer.Ok)
            {
                GUILayout.Label(DiscoveryFeedFailedText, _updateAvailableStyle);
                return;
            }

            var view = DiscoveryFeedPolicy.Select(_feedAnswer, _updateChecker.InstalledOrMappedNexusIds(), _feedAskedSize);
            GUILayout.Label(string.Format(DiscoveryFeedHeaderFormat, view.Shown, view.Listed, view.AlreadyHave, view.AdultOrUnrated), _errorStyle);
            GUILayout.Space(4);
            foreach (var mod in view.Rows)
                DrawFeedRow(mod);
        }

        // The one place a list request starts. It re-asks the policy, so a button press that slipped past
        // a disabled button, or a draw while one is in flight, sends nothing.
        private void StartFeedRequest()
        {
            if (!DiscoveryFeedPolicy.MaySend(_config.EnableNexusDiscovery.Value, _config.CanCheckForUpdates, _feedLoading))
                return;

            _feedRequested = true;
            _feedLoading = true;
            _feedAskedSize = DiscoveryFeedPolicy.ClampSize(_config.DiscoveryFeedSize.Value);
            StartCoroutine(_publicClient.FetchFeed(_feedAskedSize, answer =>
            {
                _feedAnswer = answer;
                _feedLoading = false;
                if (answer.Ok)
                    Plugin.Logger.LogDebug($"New and updated: Nexus listed {answer.Mods.Count} mod(s).");
                else
                    Plugin.Logger.LogWarning($"Nexus public list failed ({answer.Failure}); press Refresh on the New and updated filter to try again.");
            }));
        }

        private void DrawFeedRow(NexusPublicMod mod)
        {
            GUILayout.BeginVertical("box");

            GUILayout.BeginHorizontal();
            GUILayout.Label(DiscoveryFeedPolicy.DisplayName(mod), _modNameStyle, GUILayout.Width(250));
            GUILayout.Label("♥ " + mod.Endorsements, _upToDateStyle, GUILayout.Width(60));
            GUILayout.FlexibleSpace();
            if (!string.IsNullOrWhiteSpace(mod.Version))
                GUILayout.Label(string.Format(MumText.Get("VersionFormat"), mod.Version.Trim()), _errorStyle);
            GUILayout.EndHorizontal();

            var line = DiscoveryFeedPolicy.OneLine(mod.Summary, DiscoveryFeedPolicy.SummaryMaxChars);
            if (line.Length > 0)
                GUILayout.Label("  " + line, _summaryStyle);

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(DiscoveryFeedOpenPageText, GUILayout.Width(130)))
                Application.OpenURL(DiscoveryFeedPolicy.PageUrl(mod));
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }
        // ── end New and updated ─────────────────────────────────────────────

        // Manual re-check for HTTP-429'd mods only (N5,
        // Documentation/Design/Mod_Update_Manager_Audit_Remediation_As_Built.md). Shown only while
        // at least one mod's last check hit Nexus's rate limit; re-runs the staggered check
        // for that subset alone, never the whole list. Only a mod that 429'd twice lands here:
        // NexusApiClient retries a 429 once on its own first.
        private void DrawRecheckRateLimitedBanner()
        {
            if (!_updateChecker.HasRateLimitedMods) return;

            var rateLimitedCount = _updateChecker.InstalledMods.Count(m => m.RateLimited);

            GUILayout.BeginHorizontal("box");
            GUILayout.Label("  " + string.Format(MumText.Get("RateLimitedFormat"), rateLimitedCount), _errorStyle);
            GUILayout.FlexibleSpace();
            GUI.enabled = !_updateChecker.IsChecking && _config.HasApiKey;
            if (GUILayout.Button(MumText.Get("RecheckRateLimited"), GUILayout.Width(160)))
                _updateChecker.RecheckRateLimitedMods(this);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
        }

        // English-column template for Mod_Update_Manager_FrameworkStale (Localization/SimpEn.csv /
        // SimpCn.csv) - {0}=framework display name, {1}=installed version, {2}=latest known version.
        private static string FrameworkStaleBannerFormat => MumText.Get("FrameworkStale");

        // Stale-framework detector banner (N7,
        // Documentation/Design/Mod_Update_Manager_Audit_Remediation_As_Built.md): warns when the
        // installed CSFF Modding Framework (Nexus ID 30, see KnownModRegistry) is behind the
        // newest version already known from the normal per-mod update check - no new network
        // call, this just reads the same InstalledModInfo row every other tab reads. Renders
        // nothing when the framework isn't installed, hasn't been checked yet (or the check
        // failed), or is already current; VersionComparer (never a string compare) decides
        // "behind".
        private void DrawFrameworkStaleBanner()
        {
            var framework = _updateChecker.InstalledMods.FirstOrDefault(m => GetNexusIdForMod(m) == "30");
            if (framework == null) return;
            if (framework.CheckFailed || string.IsNullOrEmpty(framework.LatestVersion)) return;
            if (!VersionComparer.NeedsUpdate(framework.Version, framework.LatestVersion)) return;

            var text = string.Format(FrameworkStaleBannerFormat, framework.Name, framework.Version, framework.LatestVersion);

            GUILayout.BeginHorizontal("box");
            GUILayout.Label("  " + text, framework.IsMajorVersionUpdate ? _majorUpdateStyle : _updateAvailableStyle);
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
        }

        private void DrawAllMods()
        {
            GUILayout.BeginHorizontal();
            _favoritesOnly = GUILayout.Toggle(_favoritesOnly, MumText.Get("FavoritesOnly"), GUILayout.Width(110));
            _showIgnored = GUILayout.Toggle(_showIgnored, MumText.Get("ShowIgnored"), GUILayout.Width(100));
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            var mods = _config.ShowOnlyUpdates.Value
                ? FilterMods(_updateChecker.GetModsNeedingUpdate())
                : FilterMods(_updateChecker.InstalledMods);

            if (_favoritesOnly)
                mods = mods.Where(m => m.IsFavorited).ToList();
            if (!_showIgnored)
                mods = mods.Where(m => !m.IsIgnored).ToList();

            DrawModList(mods);
        }

        private void DrawUpdatesAvailable()
        {
            var mods = FilterMods(_updateChecker.GetModsNeedingUpdate());
            if (!_showIgnored)
                mods = mods.Where(m => !m.IsIgnored).ToList();

            if (mods.Count == 0)
            {
                GUILayout.Label(MumText.Get("NoUpdates"), _upToDateStyle);
            }
            else
            {
                var majorCount = mods.Count(m => m.IsMajorVersionUpdate);
                GUILayout.Label(majorCount > 0
                    ? string.Format(MumText.Get("UpdatesAvailableMajorFormat"), mods.Count, majorCount)
                    : string.Format(MumText.Get("UpdatesAvailableFormat"), mods.Count), _updateAvailableStyle);
                DrawModList(mods);
            }
        }

        private void DrawUpToDate()
        {
            var mods = FilterMods(_updateChecker.GetUpToDateMods());
            if (!_showIgnored)
                mods = mods.Where(m => !m.IsIgnored).ToList();

            if (mods.Count == 0)
                GUILayout.Label(MumText.Get("NoneChecked"));
            else
                DrawModList(mods);
        }

        private void DrawUnableToCheck()
        {
            var mods = FilterMods(_updateChecker.GetUncheckedMods());
            GUILayout.Label(MumText.Get("UnmappedHeader"), _errorStyle);
            GUILayout.Label(MumText.Get("UnmappedHelp1"));
            GUILayout.Label(MumText.Get("UnmappedHelp2"));
            GUILayout.Space(10);

            if (mods.Count == 0)
            {
                GUILayout.Label(MumText.Get("AllMapped"), _upToDateStyle);
                return;
            }

            foreach (var mod in mods.OrderBy(m => m.Name))
            {
                GUILayout.BeginVertical("box");

                GUILayout.BeginHorizontal();
                GUILayout.Label(mod.Name, _modNameStyle, GUILayout.Width(250));
                GUILayout.Label(string.Format(MumText.Get("VersionFormat"), mod.Version), _errorStyle, GUILayout.Width(100));
                GUILayout.FlexibleSpace();

                var knownId = KnownModRegistry.GetNexusModId(mod.Name)
                    ?? KnownModRegistry.GetNexusModId(mod.FolderName);
                if (knownId != null)
                    GUILayout.Label(string.Format(MumText.Get("KnownFormat"), knownId), _upToDateStyle, GUILayout.Width(100));
                GUILayout.EndHorizontal();

                if (!_inlineNexusIds.ContainsKey(mod.FolderName))
                    _inlineNexusIds[mod.FolderName] = knownId ?? "";

                GUILayout.BeginHorizontal();
                GUILayout.Label("  " + MumText.Get("NexusIdLabel"), GUILayout.Width(80));
                _inlineNexusIds[mod.FolderName] = GUILayout.TextField(_inlineNexusIds[mod.FolderName], GUILayout.Width(80));
                if (GUILayout.Button(MumText.Get("SaveMapping"), GUILayout.Width(110)))
                {
                    var id = _inlineNexusIds[mod.FolderName].Trim();
                    if (!string.IsNullOrEmpty(id))
                    {
                        _mappingManager.SetMapping(mod.FolderName, id, mod.Name);
                        _statusMessage = string.Format(MumText.Get("MappedStatusFormat"), mod.Name, id);
                    }
                }
                GUILayout.Label(MumText.Get("FindIdHint"), _errorStyle);
                GUILayout.EndHorizontal();

                GUILayout.EndVertical();
            }
        }

        private void DrawConflicts()
        {
            if (!_config.ShowConflictWarnings.Value)
            {
                GUILayout.Label(MumText.Get("ConflictsDisabled"), _errorStyle);
                return;
            }

            if (_updateChecker.InstalledMods.Count == 0)
            {
                GUILayout.Label(MumText.Get("NoModsInstalled"));
                return;
            }

            DrawModConflicts();
            GUILayout.Space(12);
            DrawDependencyIssues();
            DrawLoadOrder();
        }

        // English-column templates (Localization/SimpEn.csv + SimpCn.csv):
        //   Mod_Update_Manager_ConflictGuidConfirmed
        //   Mod_Update_Manager_ConflictHeuristic
        private static string ConflictGuidConfirmedBadge => MumText.Get("ConflictGuidConfirmed");
        private static string ConflictHeuristicSuffix => MumText.Get("ConflictHeuristic");

        private void DrawModConflicts()
        {
            var conflicts = _conflictDetector.DetectConflicts(_updateChecker.InstalledMods);

            if (conflicts.Count == 0)
            {
                GUILayout.Label(MumText.Get("NoConflicts"), _upToDateStyle);
                return;
            }

            GUILayout.Label(string.Format(MumText.Get("ConflictsFoundFormat"), conflicts.Count), _headerStyle);
            GUILayout.Space(5);

            foreach (var conflict in conflicts)
            {
                GUILayout.BeginHorizontal("box");
                if (conflict.Source == ConflictDetector.ConflictSource.GuidConfirmed)
                {
                    // Identity read from the DLLs themselves: the two files register one GUID.
                    GUILayout.Label(string.Format(MumText.Get("ConflictPairFormat"), ConflictGuidConfirmedBadge, conflict.ModA, conflict.ModB), _updateAvailableStyle);
                }
                else
                {
                    // Matched on names only, so it is labelled a guess and drawn in the muted style.
                    GUILayout.Label(string.Format(MumText.Get("ConflictHeuristicLineFormat"), conflict.Severity, conflict.ModA, conflict.ModB, ConflictHeuristicSuffix), _summaryStyle);
                }
                GUILayout.EndHorizontal();
                GUILayout.Label("  " + conflict.Description, _errorStyle);
                GUILayout.Label("  " + string.Format(MumText.Get("ResolutionFormat"), _conflictDetector.GetConflictResolution(conflict)), _errorStyle);
                GUILayout.Space(5);
            }
        }

        // English-column templates for the Dependencies sub-section (N6,
        // Documentation/Design/Mod_Update_Manager_Audit_Remediation_As_Built.md). Keys in
        // Localization/SimpEn.csv + SimpCn.csv:
        //   Mod_Update_Manager_DependencyHeader           {0}=unsatisfied count
        //   Mod_Update_Manager_MissingDependencyBadge
        //   Mod_Update_Manager_MissingOptionalDependencyBadge
        //   Mod_Update_Manager_MissingHardDependency      {0}=missing GUID, {1}=version clause
        //   Mod_Update_Manager_MissingSoftDependency      {0}=missing GUID, {1}=version clause
        //   Mod_Update_Manager_DependencyMinVersion       {0}=minimum version
        private static string DependencyHeaderFormat => MumText.Get("DependencyHeader");
        private static string MissingDependencyBadge => MumText.Get("MissingDependencyBadge");
        private static string MissingOptionalDependencyBadge => MumText.Get("MissingOptionalDependencyBadge");
        private static string MissingHardDependencyFormat => MumText.Get("MissingHardDependency");
        private static string MissingSoftDependencyFormat => MumText.Get("MissingSoftDependency");
        private static string DependencyMinVersionFormat => MumText.Get("DependencyMinVersion");

        /// <summary>
        /// Dependency / SoftDependency graph validator (N6). Flags any [BepInDependency] declared by
        /// an installed plugin whose target GUID is not among the installed [BepInPlugin] GUIDs.
        /// Declarations are read straight out of each DLL's metadata tables - nothing is loaded or
        /// executed (see PluginMetadataReader) - and DependencyValidator caches the result, because
        /// this method runs on every OnGUI pass.
        ///
        /// Renders NOTHING when every declared dependency resolves. The scan's outcome is logged
        /// once per scan instead, so a clean run is still confirmable from LogOutput.log.
        /// </summary>
        private void DrawDependencyIssues()
        {
            var issues = DependencyValidator.GetIssues();
            if (issues == null || issues.Count == 0) return;

            GUILayout.Label(string.Format(DependencyHeaderFormat, issues.Count), _headerStyle);
            GUILayout.Space(5);

            foreach (var issue in issues)
            {
                GUILayout.BeginHorizontal("box");
                GUILayout.Label(issue.IsHard ? MissingDependencyBadge : MissingOptionalDependencyBadge,
                    issue.IsHard ? _majorUpdateStyle : _updateAvailableStyle);
                GUILayout.Label(issue.DependentName, _modNameStyle);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                var versionClause = string.IsNullOrEmpty(issue.MinimumVersion)
                    ? ""
                    : string.Format(DependencyMinVersionFormat, issue.MinimumVersion);
                var text = string.Format(
                    issue.IsHard ? MissingHardDependencyFormat : MissingSoftDependencyFormat,
                    issue.MissingGuid, versionClause);

                GUILayout.Label("  " + text, _errorStyle);
                GUILayout.Space(5);
            }
        }

        private void DrawSettingsAndAnalytics()
        {
            DrawAnalytics();
            GUILayout.Space(20);
            DrawSettings();
        }

        private void DrawAnalytics()
        {
            if (_updateChecker.InstalledMods.Count == 0)
            {
                GUILayout.Label(MumText.Get("NoModsInstalled"));
                return;
            }

            var stats = ModComparisonView.GenerateStats(_updateChecker.InstalledMods);

            GUILayout.Label(MumText.Get("StatsHeader"), _headerStyle);
            GUILayout.Space(5);

            GUILayout.BeginHorizontal("box");
            GUILayout.Label(MumText.Get("StatsTotal"));
            GUILayout.Label(stats.TotalMods.ToString(), _modNameStyle);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal("box");
            GUILayout.Label(MumText.Get("StatsUpToDate"));
            GUILayout.Label(stats.ModsUpToDate.ToString(), _upToDateStyle);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal("box");
            GUILayout.Label(MumText.Get("StatsNeedUpdates"));
            GUILayout.Label(stats.ModsNeedingUpdate.ToString(), _updateAvailableStyle);
            GUILayout.EndHorizontal();

            if (stats.CriticalUpdates > 0)
            {
                GUILayout.BeginHorizontal("box");
                GUILayout.Label(MumText.Get("StatsCritical"));
                GUILayout.Label(stats.CriticalUpdates.ToString(), _majorUpdateStyle);
                GUILayout.EndHorizontal();
            }

            if (stats.ModsUnchecked > 0)
            {
                GUILayout.BeginHorizontal("box");
                GUILayout.Label(MumText.Get("StatsUnableToCheck"));
                GUILayout.Label(stats.ModsUnchecked.ToString(), _errorStyle);
                GUILayout.EndHorizontal();
            }

            var favCount = _updateChecker.InstalledMods.Count(m => m.IsFavorited);
            var ignCount = _updateChecker.InstalledMods.Count(m => m.IsIgnored);
            if (favCount > 0 || ignCount > 0)
            {
                GUILayout.Space(5);
                GUILayout.BeginHorizontal("box");
                GUILayout.Label(string.Format(MumText.Get("FavoritesIgnoredFormat"), favCount, ignCount), _errorStyle);
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(10);
            if (_updateScheduler.IsRunning)
            {
                GUILayout.BeginHorizontal("box");
                GUILayout.Label(MumText.Get("NextBackgroundCheck"), _modNameStyle);
                GUILayout.Label(_updateScheduler.GetTimeUntilNextCheckFormatted(), _upToDateStyle);
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(10);
            DrawProfiles();

            GUILayout.Space(10);
            GUILayout.Label(MumText.Get("ExportHeader"), _headerStyle);
            GUILayout.Space(4);
            GUILayout.Label(MumText.Get("ExportNote"), _errorStyle);

            if (GUILayout.Button(MumText.Get("GenerateExport"), GUILayout.Width(150)))
            {
                _exportText = ModPreferences.ExportText(_updateChecker.InstalledMods);
                _exportScroll = Vector2.zero;
            }

            if (!string.IsNullOrEmpty(_exportText))
            {
                if (GUILayout.Button(MumText.Get("CopyToClipboard"), GUILayout.Width(150)))
                    GUIUtility.systemCopyBuffer = _exportText;

                _exportScroll = GUILayout.BeginScrollView(_exportScroll, GUILayout.Height(180));
                GUILayout.TextArea(_exportText);
                GUILayout.EndScrollView();
            }
        }

        // English-column text for the key-check result rows (Localization/SimpEn.csv, SimpCn.csv):
        // Mod_Update_Manager_KeyChecking, _KeyAccepted, _KeyRejected, _KeyRateLimited,
        // _KeyUnreachable and _KeyInconclusive ({0}=HTTP status).
        private static string ApiKeyCheckingText => MumText.Get("KeyChecking");
        private static string ApiKeyAcceptedText => MumText.Get("KeyAccepted");
        private static string ApiKeyRejectedText => MumText.Get("KeyRejected");
        private static string ApiKeyRateLimitedText => MumText.Get("KeyRateLimited");
        private static string ApiKeyUnreachableText => MumText.Get("KeyUnreachable");
        private static string ApiKeyInconclusiveFormat => MumText.Get("KeyInconclusive");

        // English-column text for Mod_Update_Manager_UsePublicWithoutKey (Localization/SimpEn.csv, SimpCn.csv).
        private static string UsePublicWithoutKeyText => MumText.Get("UsePublicWithoutKey");

        private static string DescribeApiKeyResult(ApiKeyResult result, long httpStatus)
        {
            switch (result)
            {
                case ApiKeyResult.Accepted: return ApiKeyAcceptedText;
                case ApiKeyResult.Rejected: return ApiKeyRejectedText;
                case ApiKeyResult.RateLimited: return ApiKeyRateLimitedText;
                case ApiKeyResult.Unreachable: return ApiKeyUnreachableText;
                default: return string.Format(ApiKeyInconclusiveFormat, httpStatus);
            }
        }

        // Settings Save: writes the key, then asks Nexus once whether it is accepted. Never run at
        // startup. The verdict comes from NexusApiClient.ValidateApiKey, which reads only the HTTP
        // status; nothing the reply holds is drawn here.
        private void SaveApiKeyAndCheck()
        {
            _config.NexusApiKey.Value = _apiKeyInput;
            _statusMessage = MumText.Get("ApiKeySaved");
            _apiKeyResult = null;
            _apiKeyResultText = "";

            if (!_config.HasApiKey) return;

            _apiKeyChecking = true;
            _apiKeyResultText = ApiKeyCheckingText;
            _nexusClient.ValidateApiKey((result, httpStatus) =>
            {
                _apiKeyChecking = false;
                _apiKeyResult = result;
                _apiKeyResultText = DescribeApiKeyResult(result, httpStatus);
            });
        }

        private void DrawSettings()
        {
            GUILayout.Label(MumText.Get("ApiConfigHeader"), _headerStyle);
            GUILayout.Space(5);

            GUILayout.BeginHorizontal();
            GUILayout.Label(MumText.Get("NexusApiKeyLabel"), GUILayout.Width(100));
            var typedKey = GUILayout.PasswordField(_apiKeyInput, '*');
            if (typedKey != _apiKeyInput)
            {
                _apiKeyInput = typedKey;
                // The last verdict was about the key as saved, not the text now in the field.
                if (!_apiKeyChecking)
                {
                    _apiKeyResult = null;
                    _apiKeyResultText = "";
                }
            }
            GUI.enabled = !_apiKeyChecking;
            if (GUILayout.Button(MumText.Get("Save"), GUILayout.Width(60)))
                SaveApiKeyAndCheck();
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(_apiKeyResultText))
            {
                var resultStyle = _apiKeyChecking ? _errorStyle
                    : _apiKeyResult == ApiKeyResult.Accepted ? _upToDateStyle
                    : _apiKeyResult == ApiKeyResult.Rejected ? _majorUpdateStyle
                    : _updateAvailableStyle;
                GUILayout.Label(_apiKeyResultText, resultStyle);
            }
            GUILayout.Label(MumText.Get("ApiKeyHelp"), _errorStyle);
            // Opt-in (plan feature 15): off, nothing is sent without a key.
            _config.UsePublicEndpointWithoutKey.Value = GUILayout.Toggle(_config.UsePublicEndpointWithoutKey.Value, UsePublicWithoutKeyText);
            // Opt-in (plan feature 17): off, no row shows an Endorse button.
            _config.EnableEndorseButton.Value = GUILayout.Toggle(_config.EnableEndorseButton.Value, EnableEndorseText);

            GUILayout.Space(20);
            GUILayout.Label(MumText.Get("BehaviorHeader"), _headerStyle);
            GUILayout.Space(5);

            _config.CheckOnStartup.Value = GUILayout.Toggle(_config.CheckOnStartup.Value, MumText.Get("CheckOnStartup"));
            _config.OpenWindowOnUpdates.Value = GUILayout.Toggle(_config.OpenWindowOnUpdates.Value, MumText.Get("OpenWindowOnUpdates"));
            _config.ShowOnlyUpdates.Value = GUILayout.Toggle(_config.ShowOnlyUpdates.Value, MumText.Get("ShowOnlyUpdates"));

            // Opt-in (plan feature 12): an Apply deletes the copy it replaces unless this is on.
            GUILayout.Space(20);
            GUILayout.Label(InstallUpdateHeaderText, _headerStyle);
            GUILayout.Space(5);

            _config.KeepPreviousSuiteVersion.Value = GUILayout.Toggle(_config.KeepPreviousSuiteVersion.Value, KeepPreviousText);
            if (_config.KeepPreviousSuiteVersion.Value)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(KeptVersionsText, GUILayout.Width(150));
                var chosen = GUILayout.Toolbar(_config.KeptVersions.Value - SuiteKeepPolicy.MinKeptVersions, KeptVersionLabels, GUILayout.Width(120));
                _config.KeptVersions.Value = chosen + SuiteKeepPolicy.MinKeptVersions;
                GUILayout.EndHorizontal();
                GUILayout.Label(KeepDiskNote, _suiteNoteStyle);
            }

            GUILayout.Space(20);
            GUILayout.Label(MumText.Get("AddMappingHeader"), _headerStyle);
            GUILayout.Label(MumText.Get("AddMappingNote"), _errorStyle);
            GUILayout.Space(5);

            GUILayout.BeginHorizontal();
            GUILayout.Label(MumText.Get("ModNameLabel"), GUILayout.Width(80));
            _newMappingModName = GUILayout.TextField(_newMappingModName, GUILayout.Width(200));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(MumText.Get("NexusIdLabel"), GUILayout.Width(80));
            _newMappingNexusId = GUILayout.TextField(_newMappingNexusId, GUILayout.Width(200));
            if (GUILayout.Button(MumText.Get("AddMapping"), GUILayout.Width(100)))
            {
                if (!string.IsNullOrEmpty(_newMappingModName) && !string.IsNullOrEmpty(_newMappingNexusId))
                {
                    _mappingManager.SetMapping(_newMappingModName, _newMappingNexusId);
                    _statusMessage = string.Format(MumText.Get("AddedMappingFormat"), _newMappingModName, _newMappingNexusId);
                    _newMappingModName = "";
                    _newMappingNexusId = "";
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            GUILayout.Label(MumText.Get("CurrentMappings"), _modNameStyle);

            foreach (var mapping in _mappingManager.GetAllMappings().ToList())
            {
                GUILayout.BeginHorizontal("box");
                GUILayout.Label(string.Format(MumText.Get("MappingLineFormat"), mapping.LocalModName, mapping.NexusModId));
                if (GUILayout.Button(MumText.Get("Remove"), GUILayout.Width(60)))
                    _mappingManager.RemoveMapping(mapping.LocalModName);
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(20);
            GUILayout.Label(MumText.Get("BackgroundHeader"), _headerStyle);
            GUILayout.Space(5);

            _config.EnableBackgroundChecking.Value = GUILayout.Toggle(_config.EnableBackgroundChecking.Value, MumText.Get("EnablePeriodic"));

            if (_config.EnableBackgroundChecking.Value)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(MumText.Get("CheckIntervalLabel"), GUILayout.Width(150));
                var intervalStr = GUILayout.TextField(_config.CheckIntervalMinutes.Value.ToString(), GUILayout.Width(50));
                if (int.TryParse(intervalStr, out int interval) && interval >= 10 && interval <= 1440)
                    _config.CheckIntervalMinutes.Value = interval;
                GUILayout.EndHorizontal();

                if (_updateScheduler.IsRunning)
                    GUILayout.Label(string.Format(MumText.Get("NextCheckFormat"), _updateScheduler.GetTimeUntilNextCheckFormatted()), _upToDateStyle);
            }

            GUILayout.Space(20);
            GUILayout.Label(MumText.Get("AnalysisHeader"), _headerStyle);
            GUILayout.Space(5);

            _config.ShowConflictWarnings.Value = GUILayout.Toggle(_config.ShowConflictWarnings.Value, MumText.Get("ShowConflictWarnings"));

            GUILayout.Space(20);
            GUILayout.Label(MumText.Get("PerformanceHeader"), _headerStyle);
            GUILayout.Space(5);

            _config.CachingEnabled.Value = GUILayout.Toggle(_config.CachingEnabled.Value, MumText.Get("CacheResponses"));

            if (_config.CachingEnabled.Value)
                GUILayout.Label(string.Format(MumText.Get("CachedResponsesFormat"), _nexusClient.GetCacheSize()), _upToDateStyle);

            _config.EnableNexusDiscovery.Value = GUILayout.Toggle(_config.EnableNexusDiscovery.Value, MumText.Get("EnableDiscovery"));
            if (_config.EnableNexusDiscovery.Value)
            {
                var progress = _modDiscovery?.GetProgressInfo() ?? (0, 0);
                GUILayout.Label(string.Format(MumText.Get("DiscoveryProgressFormat"), progress.LastScannedId, progress.TotalDiscovered), _errorStyle);
                GUILayout.Label(DiscoveryFeedNoteText, _summaryStyle);
            }
        }

        // ── Suite tab ────────────────────────────────────────────────────────

        // MUM's own line on the Install & Update tab (plan feature 23). Static text: it sends nothing,
        // and it is not a SuiteModEntry, so Select All and Apply never see it. Both strings have
        // Mod_Update_Manager_SelfLine and Mod_Update_Manager_SelfNexusPage rows in Localization/SimpEn.csv
        // and SimpCn.csv, which nothing reads until runtime localization lands.
        private static string SelfLineFormat => MumText.Get("SelfLine");
        private static string SelfNexusPageText => MumText.Get("SelfNexusPage");

        // The status line while Apply runs (plan feature 8), one per SuiteInstallPhase, redrawn after
        // every step. Rows Mod_Update_Manager_SuiteChecking, _SuiteExtracting, _SuiteVerifying and
        // _SuiteRemoving in Localization/SimpEn.csv and SimpCn.csv; nothing reads them until runtime
        // localization lands.
        private static string SuiteCheckingFormat => MumText.Get("SuiteChecking");
        private static string SuiteExtractingFormat => MumText.Get("SuiteExtracting");
        private static string SuiteVerifyingFormat => MumText.Get("SuiteVerifying");
        private static string SuiteRemovingFormat => MumText.Get("SuiteRemoving");

        // A row whose mod is already installed under another folder (plan feature 10), and the "?"
        // panel (plan feature 9). Rows Mod_Update_Manager_InstalledElsewhere, _InstalledElsewhereDetail and
        // _DetailsCut in Localization/SimpEn.csv and SimpCn.csv; nothing reads them until runtime
        // localization lands.
        private static string SuiteInstalledElsewhereBadge => MumText.Get("InstalledElsewhere");
        private static string SuiteInstalledElsewhereFormat => MumText.Get("InstalledElsewhereDetail");
        private const float SuiteDetailsHeight = 120f;

        // Dependencies and the framework skew line (plan feature 11). Rows Mod_Update_Manager_AlsoInstalling
        // ({0}=the mod that needs it), _AlsoInstallingMany ({0}=the mods that need it), _ClearRequired,
        // _RequiredClearedHard and _RequiredClearedSoft ({0}=the cleared mod, {1}=the mod that needs it),
        // _FrameworkSkew ({0}=installed framework version, {1}=bundled, {2}=content mods at or above
        // their bundled version) and _SuiteStopped in Localization/SimpEn.csv and SimpCn.csv; nothing
        // reads them until runtime localization lands.
        private static string SuiteAlsoInstallingFormat => MumText.Get("AlsoInstalling");
        private static string SuiteAlsoInstallingManyFormat => MumText.Get("AlsoInstallingMany");
        private static string SuiteClearRequiredText => MumText.Get("ClearRequired");
        private static string SuiteRequiredClearedHardFormat => MumText.Get("RequiredClearedHard");
        private static string SuiteRequiredClearedSoftFormat => MumText.Get("RequiredClearedSoft");
        private static string SuiteFrameworkSkewFormat => MumText.Get("FrameworkSkew");
        private static string SuiteStoppedText => MumText.Get("SuiteStopped");

        // The opt-in "keep the previous version" option and Roll back (plan feature 12). Rows
        // Mod_Update_Manager_KeepPreviousVersion, _KeptVersions, _KeepDiskNote, _Previous ({0}=the kept
        // version), _RollBack and _SuitePruning ({0}=the mod, {1}=files deleted, {2}=files to delete) in
        // Localization/SimpEn.csv and SimpCn.csv (and _InstallUpdateHeader for the Settings heading
        // above them), and _SuiteRolledBack, _RollbackNoWorkFolder and
        // _RollbackKeptMissing for the status lines SuiteInstaller and ModSuiteExtractor write; nothing
        // reads them until runtime localization lands. The disk figure was first measured 2026-10-01
        // (Documentation/Design/Mod_Update_Manager_Round2_As_Built.md section 12) over nine folders and
        // re-measured 2026-10-13 over all fifteen suite folders (README "Keep the previous version").
        private static string InstallUpdateHeaderText => MumText.Get("InstallUpdateHeader");
        private static string KeepPreviousText => MumText.Get("KeepPreviousVersion");
        private static string KeptVersionsText => MumText.Get("KeptVersions");
        private static string KeepDiskNote => MumText.Get("KeepDiskNote");
        private static string SuitePreviousFormat => MumText.Get("Previous");
        private static string SuiteRollbackText => MumText.Get("RollBack");
        private static string SuitePruningFormat => MumText.Get("SuitePruning");
        private static readonly string[] KeptVersionLabels = Enumerable
            .Range(SuiteKeepPolicy.MinKeptVersions, SuiteKeepPolicy.MaxKeptVersions - SuiteKeepPolicy.MinKeptVersions + 1)
            .Select(n => n.ToString())
            .ToArray();

        // A row's installed version as the suite tab already shows it, for planning every frame without
        // reading a file: null unless the row's own folder holds a copy (UpToDate or OutOfDate).
        private static string SuiteRowInstalledVersion(SuiteModEntry entry)
        {
            return entry.Status == SuiteInstallStatus.UpToDate || entry.Status == SuiteInstallStatus.OutOfDate ? entry.InstalledVersion : null;
        }

        // The Install & Update rows classified again with the latest scan of plugins/, which is what
        // shows a suite mod installed under another folder. Plugin.OnGameDataLoaded does the same after
        // its scan; Awake's first pass had no scan.
        private void RefreshSuiteRows()
        {
            try
            {
                SuiteVersionReader.RefreshAll(_updateChecker.InstalledMods);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"SuiteVersionReader.RefreshAll failed after a mod scan: {ex}");
            }
        }

        // The "?" panel's text: the bundled mod's own ModInfo.json Description, or the registry's typed
        // line when it has none, cut at DetailsMaxChars (SuiteDescription.Resolve). Resolved once per row
        // and cap, because IMGUI draws several times a frame and the embedded text does not change while
        // the game runs (the key carries its length, so a read that failed once is resolved again).
        private string SuiteDetailsText(SuiteModEntry entry)
        {
            var cap = _config.DetailsMaxChars.Value;
            var key = entry.FolderName + "|" + cap + "|" + (entry.EmbeddedModInfoJson == null ? -1 : entry.EmbeddedModInfoJson.Length);
            if (_suiteDetailKey != key)
            {
                _suiteDetailText = SuiteDescription.Resolve(entry.EmbeddedModInfoJson, entry.Description, cap);
                _suiteDetailKey = key;
            }
            return _suiteDetailText;
        }

        private void DrawSuiteTab()
        {
            GUILayout.Label(MumText.Get("SuiteHeader"), _headerStyle);
            GUILayout.Label(MumText.Get("SuiteIntro"), _summaryStyle);
            GUILayout.Space(6);

            // MUM's own line (feature 23): drawn here, above the suite list and ahead of the restart
            // banner further down, so it shows whether or not a restart is pending or an apply is running.
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label(string.Format(SelfLineFormat, Plugin.PluginVersion), _summaryStyle);
            if (GUILayout.Button(SelfNexusPageText, GUILayout.Width(110)))
                Application.OpenURL(KnownModRegistry.GetNexusUrl(KnownModRegistry.SelfNexusModId));
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            // What Apply would install from the current ticks (feature 11): the rows a ticked row needs,
            // and a warning for each one the player cleared. SuiteRowInstalledVersion reads the rows, not
            // the disk, because this runs every frame. The skew line is drawn first.
            var plan = SuiteBatch.Plan(SuiteModRegistry.All, _suiteSelected, SuiteRowInstalledVersion, _suiteDeclined);
            var skew = SuiteDependencies.FrameworkSkew(SuiteModRegistry.All);
            if (skew != null)
            {
                GUILayout.Label(string.Format(SuiteFrameworkSkewFormat, skew.FrameworkInstalled, skew.FrameworkBundled, skew.ModCount), _suiteNoteStyle);
                GUILayout.Space(6);
            }

            _suiteScroll = GUILayout.BeginScrollView(_suiteScroll, GUILayout.ExpandHeight(true));

            foreach (var entry in SuiteModRegistry.All)
            {
                GUILayout.BeginHorizontal(GUI.skin.box);

                // Checkbox - disabled while applying or after restart pending, and for a row only
                // installed because another needs it (the Clear button below is how to decline it)
                var requiredRow = plan.Added.Find(r => r.Entry == entry);
                GUI.enabled = !_suiteApplying && !_suitePendingRestart && requiredRow == null;
                bool wasSelected = requiredRow != null || _suiteSelected.Contains(entry.FolderName);
                bool isSelected = GUILayout.Toggle(wasSelected, "", GUILayout.Width(20));
                if (requiredRow == null)
                {
                    // A row ticked by hand is wanted whatever was cleared before.
                    if (isSelected && !wasSelected) { _suiteSelected.Add(entry.FolderName); _suiteDeclined.Remove(entry.FolderName); }
                    else if (!isSelected && wasSelected) _suiteSelected.Remove(entry.FolderName);
                }
                GUI.enabled = true;

                // Name + category
                GUILayout.Label(string.Format(MumText.Get("CategoryFormat"), entry.Category), _summaryStyle, GUILayout.Width(80));
                GUILayout.Label(entry.DisplayName, _modNameStyle, GUILayout.Width(220));

                // Version columns
                GUILayout.Label(string.Format(MumText.Get("InstalledVersionFormat"), entry.InstalledVersion), _summaryStyle, GUILayout.Width(130));
                GUILayout.Label(string.Format(MumText.Get("SuiteVersionFormat"), entry.EmbeddedVersion), _summaryStyle, GUILayout.Width(100));

                // Status badge - a swap that left the plugin folder missing (SuiteInstaller,
                // FailedFolderMissing) overrides the version-based Status badge below, which would
                // otherwise read it as a plain "[Not Installed]" and hide that the previous copy is
                // sitting in SuiteInstaller's backup folder (BepInEx/mum_work/old/<Folder>).
                GUIStyle badgeStyle;
                string badgeText;
                if (entry.LastInstallIncomplete)
                {
                    badgeStyle = _errorStyle;
                    badgeText = MumText.Get("InstallIncomplete");
                }
                else
                {
                    badgeStyle = entry.Status switch
                    {
                        SuiteInstallStatus.UpToDate => _upToDateStyle,
                        SuiteInstallStatus.OutOfDate => _updateAvailableStyle,
                        SuiteInstallStatus.NotInstalled => _errorStyle,
                        SuiteInstallStatus.InstalledElsewhere => _updateAvailableStyle,
                        _ => GUI.skin.label
                    };
                    badgeText = entry.Status switch
                    {
                        SuiteInstallStatus.UpToDate => MumText.Get("BadgeUpToDate"),
                        SuiteInstallStatus.OutOfDate => MumText.Get("BadgeUpdateAvailable"),
                        SuiteInstallStatus.NotInstalled => MumText.Get("BadgeNotInstalled"),
                        SuiteInstallStatus.InstalledElsewhere => SuiteInstalledElsewhereBadge,
                        _ => MumText.Get("BadgeUnknown")
                    };
                }
                GUILayout.Label(badgeText, badgeStyle, GUILayout.Width(170));

                // Details toggle
                if (GUILayout.Button("?", GUILayout.Width(24)))
                {
                    _suiteDetailTarget = _suiteDetailTarget == entry.FolderName ? null : entry.FolderName;
                    _suiteDetailScroll = Vector2.zero;
                }

                GUILayout.EndHorizontal();

                // Where the copy that is already installed sits (feature 10), always shown, not behind "?"
                if (entry.Status == SuiteInstallStatus.InstalledElsewhere)
                {
                    GUILayout.BeginVertical(GUI.skin.box);
                    GUILayout.Label(string.Format(SuiteInstalledElsewhereFormat, entry.InstalledElsewherePath), _summaryStyle);
                    GUILayout.EndVertical();
                }

                // The versions kept for this row by the keep option (feature 12), one line each, always
                // shown, with the way back to it. Drawn from a copy: a rollback replaces the list.
                foreach (var keptVersion in entry.KeptVersions.ToArray())
                {
                    GUILayout.BeginHorizontal(GUI.skin.box);
                    GUILayout.Label(string.Format(SuitePreviousFormat, keptVersion), _summaryStyle);
                    GUI.enabled = !_suiteApplying;
                    if (GUILayout.Button(SuiteRollbackText, GUILayout.Width(90)))
                        RollBackSuiteMod(entry, keptVersion);
                    GUI.enabled = true;
                    GUILayout.EndHorizontal();
                }

                // A row installed only because a ticked row needs it (feature 11), always shown, with the
                // way to decline it; and, under the row that needs something the player cleared, the
                // consequence. The names come from the DLLs' own declarations (SuiteDependencies).
                if (requiredRow != null)
                {
                    var needers = string.Join(", ", requiredRow.RequiredBy.Select(r => r.DisplayName));
                    GUILayout.BeginHorizontal(GUI.skin.box);
                    GUILayout.Label(string.Format(requiredRow.RequiredBy.Count == 1 ? SuiteAlsoInstallingFormat : SuiteAlsoInstallingManyFormat, needers), _summaryStyle);
                    GUI.enabled = !_suiteApplying && !_suitePendingRestart;
                    if (GUILayout.Button(SuiteClearRequiredText, GUILayout.Width(70)))
                        _suiteDeclined.Add(entry.FolderName);
                    GUI.enabled = true;
                    GUILayout.EndHorizontal();
                }
                foreach (var warning in plan.Warnings)
                {
                    if (warning.Dependent != entry) continue;
                    GUILayout.BeginVertical(GUI.skin.box);
                    GUILayout.Label(string.Format(warning.IsHard ? SuiteRequiredClearedHardFormat : SuiteRequiredClearedSoftFormat, warning.Required.DisplayName, warning.Dependent.DisplayName), _suiteNoteStyle);
                    GUILayout.EndVertical();
                }

                // Details panel (feature 9): the bundled mod's own description in a scroll view
                if (_suiteDetailTarget == entry.FolderName)
                {
                    GUILayout.BeginVertical(GUI.skin.box);
                    _suiteDetailScroll = GUILayout.BeginScrollView(_suiteDetailScroll, GUILayout.Height(SuiteDetailsHeight));
                    GUILayout.Label(SuiteDetailsText(entry), _summaryStyle);
                    GUILayout.EndScrollView();
                    GUILayout.EndVertical();
                }
            }

            GUILayout.EndScrollView();
            GUILayout.Space(4);

            // Pending restart banner
            if (_suitePendingRestart)
            {
                var restartStyle = _restartStyle ??= new GUIStyle(GUI.skin.label) { normal = { textColor = new Color(1f, 0.3f, 0.3f) }, fontStyle = FontStyle.Bold, wordWrap = true };
                GUILayout.Label(MumText.Get("RestartBanner"), restartStyle);
                // The banner returns before the status line below, so without this a failed or
                // skipped row in a batch that also installed something was never shown.
                if (!string.IsNullOrEmpty(_suiteStatusMessage))
                    GUILayout.Label(_suiteStatusMessage, _summaryStyle);
                GUILayout.Space(4);
                // The checkboxes and the action bar are hidden while a restart is pending, so
                // without this a row that failed beside one that installed could not be tried again
                // until the game restarted. It selects the failed rows and runs the same Apply.
                if (_suiteFailed.Count > 0)
                {
                    GUI.enabled = !_suiteApplying;
                    if (GUILayout.Button(string.Format(MumText.Get("RetryFailed"), _suiteFailed.Count)))
                    {
                        _suiteDeclined.Clear();
                        _suiteSelected = new HashSet<string>(_suiteFailed);
                        ApplySuiteUpdates();
                    }
                    GUI.enabled = true;
                }
                // Disabled while a Retry failed batch is still installing (Apply runs across frames
                // now), so the game is not quit with that batch half done.
                GUI.enabled = !_suiteApplying;
                if (GUILayout.Button(MumText.Get("QuitGame")))
                {
                    // 2868860 is CSFF (steamapps/appmanifest_2868860.acf; the harness pins the same id
                    // in Harness-LaunchNoArgs.Tests.ps1). No arguments: any argument makes Steam
                    // open a dialog that waits for a click.
                    try { System.Diagnostics.Process.Start("steam://run/2868860"); }
                    catch (System.Exception ex) { MumLog.Warning($"Quit: could not relaunch through Steam ({ex.Message}); quitting anyway."); }
                    UnityEngine.Application.Quit();
                }
                GUI.enabled = true;
                return;
            }

            // Status line
            if (!string.IsNullOrEmpty(_suiteStatusMessage))
                GUILayout.Label(_suiteStatusMessage, _summaryStyle);

            GUILayout.Space(4);

            // Bottom action bar
            GUILayout.BeginHorizontal();
            GUI.enabled = !_suiteApplying;
            if (GUILayout.Button(MumText.Get("SelectOutOfDate")))
            {
                _suiteDeclined.Clear();
                _suiteSelected = new HashSet<string>(SuiteBatch.SelectOutOfDate(SuiteModRegistry.All));
            }
            if (GUILayout.Button(MumText.Get("SelectAll"))) { _suiteDeclined.Clear(); _suiteSelected = new HashSet<string>(SuiteBatch.SelectAll(SuiteModRegistry.All)); }
            if (GUILayout.Button(MumText.Get("ClearRequired"))) { _suiteDeclined.Clear(); _suiteSelected.Clear(); }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // The ticked rows plus the ones installed because a ticked row needs them (feature 11).
            int suiteToApply = _suiteSelected.Count + plan.Added.Count;
            GUI.enabled = suiteToApply > 0 && !_suiteApplying;
            if (GUILayout.Button(string.Format(MumText.Get("ApplyFormat"), suiteToApply)))
                ApplySuiteUpdates();
            GUI.enabled = true;
        }

        // Apply Updates and the banner's Retry failed button both come here. The batch runs as a
        // coroutine on this component, so it carries on if the window is closed, and while it runs
        // _suiteApplying keeps every suite button disabled and a second press out.
        private void ApplySuiteUpdates()
        {
            if (_suiteApplying) return;
            StartCoroutine(ApplySuiteCoroutine());
        }

        // Installs the selected rows a step at a time (SuiteInstaller.InstallSteps through
        // ModSuiteExtractor.ExtractSteps), waiting one frame after every step, so the window keeps
        // drawing the status line and the game keeps rendering. Apply used to run whole inside one
        // OnGUI call, and the game stopped drawing until it finished. Each wait is yield return null,
        // never WaitForSeconds: CSFF sits at timeScale 0 while it waits for the player, and a
        // scaled-time wait would never end (CLAUDE.md "Coroutine Timing").
        private IEnumerator ApplySuiteCoroutine()
        {
            _suiteApplying = true;
            try
            {
                // Which rows apply, in what order, and which are left as installed: SuiteBatch.Plan
                // (the skip rules live there, gated by MUM-CompiledLogic).
                var batch = SuiteBatch.Plan(SuiteModRegistry.All, _suiteSelected, SuiteVersionReader.ReadInstalledVersion, _suiteDeclined);
                foreach (var skip in batch.Skipped)
                    Plugin.Logger.LogWarning($"Suite: skipped {skip.Entry.FolderName}: {skip.Reason}; left as installed.");
                // The rows nobody ticked that a ticked row needs (feature 11), as the tab showed them.
                foreach (var required in batch.Added)
                    Plugin.Logger.LogInfo($"Suite: also installing {required.Entry.FolderName}: {string.Join(", ", required.RequiredBy.Select(r => r.FolderName))} need it.");

                // The keep option as the player has it now, read once for the whole batch (feature 12).
                var keep = _config.KeepPolicy;
                foreach (var entry in batch.Rows)
                {
                    // The failure policy (feature 11): once the framework has failed nothing further is
                    // touched, and a row whose hard dependency did not install is left as installed.
                    var blocked = batch.BlockedReason(entry);
                    if (blocked != null)
                    {
                        batch.Block(entry, blocked);
                        Plugin.Logger.LogWarning($"Suite: skipped {entry.FolderName}: {blocked}; left as installed.");
                        continue;
                    }

                    var progress = new SuiteInstallProgress();
                    var versionBefore = SuiteVersionReader.ReadInstalledVersion(entry);
                    var steps = ModSuiteExtractor.ExtractSteps(entry, keep, msg => _suiteStatusMessage = msg, progress);
                    while (true)
                    {
                        bool more;
                        try
                        {
                            more = steps.MoveNext();
                        }
                        catch (Exception ex)
                        {
                            Plugin.Logger.LogError($"ModSuiteExtractor.ExtractSteps threw for {entry.FolderName}: {ex}");
                            _suiteStatusMessage = string.Format(MumText.Get("SuiteFailedFormat"), entry.DisplayName, ex.Message);
                            more = false;
                        }
                        if (!more) break;
                        _suiteStatusMessage = SuiteProgressLine(entry.DisplayName, progress);
                        yield return null;
                    }
                    bool result = progress.Outcome == SuiteInstallOutcome.Installed;
                    batch.Record(entry.FolderName, result);
                    if (result) _versionLedger?.RecordApply(entry.FolderName, versionBefore, entry.EmbeddedVersion);
                }

                // Refresh version display after installation
                try
                {
                    SuiteVersionReader.RefreshAll(_updateChecker.InstalledMods);
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogError($"SuiteVersionReader.RefreshAll failed after apply: {ex}");
                }

                // A retry that installs nothing must not clear the banner the first batch raised, so a
                // restart already pending stays pending (on a first apply this is just ok > 0).
                _suitePendingRestart = _suitePendingRestart || batch.Ok > 0;
                // The rows of THIS batch that failed, and the ones held back because a row they need did
                // not install, which the banner's Retry failed button selects again.
                _suiteFailed = new HashSet<string>(batch.Retry);
                _suiteStatusMessage = string.Format(MumText.Get("SuiteDoneFormat"), batch.Ok, batch.Fail)
                    + (batch.Skipped.Count > 0 ? " " + string.Format(MumText.Get("SuiteSkippedFormat"), batch.Skipped.Count, string.Join(", ", batch.Skipped.Select(s => s.Entry.DisplayName))) : "")
                    + (batch.Stopped ? " " + SuiteStoppedText : "");
            }
            finally
            {
                _suiteApplying = false;
            }
        }

        // The Roll back button beside a kept version (feature 12). Like Apply it runs as a coroutine on
        // this component, with _suiteApplying keeping every suite button out while it does.
        private void RollBackSuiteMod(SuiteModEntry entry, string version)
        {
            if (_suiteApplying) return;
            StartCoroutine(RollbackSuiteCoroutine(entry, version));
        }

        // SuiteInstaller.RollbackSteps through ModSuiteExtractor.RollbackSteps, one frame after every
        // step (yield return null, never WaitForSeconds: CLAUDE.md "Coroutine Timing"). A rollback
        // replaces the mod's files on disk the way an Apply does, so it asks for the same restart.
        private IEnumerator RollbackSuiteCoroutine(SuiteModEntry entry, string version)
        {
            _suiteApplying = true;
            try
            {
                var progress = new SuiteInstallProgress();
                var steps = ModSuiteExtractor.RollbackSteps(entry, version, _config.KeepPolicy, msg => _suiteStatusMessage = msg, progress);
                while (true)
                {
                    bool more;
                    try
                    {
                        more = steps.MoveNext();
                    }
                    catch (Exception ex)
                    {
                        Plugin.Logger.LogError($"ModSuiteExtractor.RollbackSteps threw for {entry.FolderName}: {ex}");
                        _suiteStatusMessage = string.Format(MumText.Get("SuiteFailedFormat"), entry.DisplayName, ex.Message);
                        more = false;
                    }
                    if (!more) break;
                    _suiteStatusMessage = SuiteProgressLine(entry.DisplayName, progress);
                    yield return null;
                }
                // Only ever raises the banner, never clears it (a rollback that failed leaves it as it was).
                if (progress.Outcome == SuiteInstallOutcome.Installed) _suitePendingRestart = true;

                // The rows read again with the latest scan, so the version, the status and the kept
                // versions show what the rollback did.
                RefreshSuiteRows();
            }
            finally
            {
                _suiteApplying = false;
            }
        }

        // The status line between two steps of one mod's install.
        private static string SuiteProgressLine(string displayName, SuiteInstallProgress progress)
        {
            switch (progress.Phase)
            {
                case SuiteInstallPhase.Extracting:
                    return string.Format(SuiteExtractingFormat, displayName, progress.Done, progress.Total);
                case SuiteInstallPhase.Verifying:
                    return string.Format(SuiteVerifyingFormat, displayName, progress.Done, progress.Total);
                case SuiteInstallPhase.Removing:
                    return string.Format(SuiteRemovingFormat, displayName, progress.Done, progress.Total);
                case SuiteInstallPhase.Pruning:
                    return string.Format(SuitePruningFormat, displayName, progress.Done, progress.Total);
                default:
                    return string.Format(SuiteCheckingFormat, displayName);
            }
        }

        // ── Mod list helpers ─────────────────────────────────────────────────

        private List<InstalledModInfo> FilterMods(List<InstalledModInfo> mods)
        {
            if (string.IsNullOrEmpty(_searchFilter)) return mods;
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
                GUILayout.Label(MumText.Get("NoModsMatch"));
                return;
            }

            DrawSortButton();
            DrawMarkAllSeen();

            foreach (var mod in ModSorter.Sort(mods, _sortMode))
                DrawModEntry(mod);
        }

        // ── Sort (plan feature 18) ───────────────────────────────────────────
        // The button's text and its four labels. Rows of the same names are in Localization/SimpEn.csv and
        // SimpCn.csv (Mod_Update_Manager_Sort*); nothing reads them until the runtime localization feature.
        private static string SortButtonFormat => MumText.Get("SortButton");
        private static string SortNameText => MumText.Get("SortName");
        private static string SortUpdatesText => MumText.Get("SortUpdates");
        private static string SortEndorsementsText => MumText.Get("SortEndorsements");
        private static string SortAuthorText => MumText.Get("SortAuthor");

        private static string SortLabel(ModSortMode mode)
        {
            switch (mode)
            {
                case ModSortMode.UpdateStatus: return SortUpdatesText;
                case ModSortMode.Endorsements: return SortEndorsementsText;
                case ModSortMode.Author: return SortAuthorText;
                default: return SortNameText;
            }
        }

        // One button over the list: each click moves to the next order and saves it. It only reorders the
        // rows already listed, so it never asks Nexus for anything.
        private void DrawSortButton()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(string.Format(SortButtonFormat, SortLabel(_sortMode)), GUILayout.Width(220)))
            {
                _sortMode = ModSorter.Next(_sortMode);
                _preferences?.SetSortMode(_sortMode);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(4);
        }
        // ── end Sort ─────────────────────────────────────────────────────────

        // ── Version ledger (plan feature 19) ─────────────────────────────────
        // Rows of the same meaning are in Localization/SimpEn.csv and SimpCn.csv (Mod_Update_Manager_Version*);
        // nothing reads them until the runtime localization feature. The chip and history-line texts live in
        // VersionLedger (host-free, gated).
        private static string MarkAllSeenText => MumText.Get("VersionMarkSeen");
        private static string HistoryButtonText => MumText.Get("VersionHistory");
        private static string HideHistoryButtonText => MumText.Get("VersionHistoryHide");
        private static string HistoryEmptyText => MumText.Get("VersionHistoryEmpty");

        // One button over the list while any mod has a change the player has not marked as seen. It only
        // writes the ledger's own small file; it asks Nexus for nothing.
        private void DrawMarkAllSeen()
        {
            if (_versionLedger == null || !_versionLedger.AnyUnseen) return;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(MarkAllSeenText, GUILayout.Width(220)))
                _versionLedger.MarkAllSeen();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(4);
        }

        private void DrawVersionChip(InstalledModInfo mod)
        {
            var change = _versionLedger?.UnseenFor(mod.FolderName);
            if (change != null)
                GUILayout.Label("  " + VersionLedger.ChipText(change), _updateAvailableStyle);
        }

        private void DrawHistory(InstalledModInfo mod)
        {
            if (_versionLedger == null || !_historyExpanded.Contains(mod.FolderName)) return;
            GUILayout.BeginVertical("box");
            var history = _versionLedger.HistoryFor(mod.FolderName);
            if (history.Count == 0)
                GUILayout.Label(HistoryEmptyText, _summaryStyle);
            foreach (var e in history)
                GUILayout.Label(VersionLedger.LineText(e), _summaryStyle);
            GUILayout.EndVertical();
        }
        // ── end Version ledger ───────────────────────────────────────────────

        // ── Profiles (plan feature 20) ───────────────────────────────────────
        // Read-only: a profile is a saved list of the installed mods that can be compared with the current
        // install. Nothing here installs, updates or removes a mod; Delete removes only a profile file.
        // Rows of the same meaning are in Localization/SimpEn.csv and SimpCn.csv (Mod_Update_Manager_Profile*);
        // nothing reads them until the runtime localization feature. The difference lines are ModProfile's.
        private static string ProfilesHeaderText => MumText.Get("ProfilesHeader");
        private static string ProfilesNoteText => MumText.Get("ProfilesNote");
        private static string ProfileNameLabelText => MumText.Get("ProfileNameLabel");
        private static string ProfileSaveText => MumText.Get("ProfileSave");
        private static string ProfileCompareText => MumText.Get("ProfileCompare");
        private static string ProfileDeleteText => MumText.Get("ProfileDelete");
        private static string ProfileNoneText => MumText.Get("ProfileNone");
        private static string ProfileBadNameText => MumText.Get("ProfileBadName");
        private static string ProfileSavedFormat => MumText.Get("ProfileSaved");
        private static string ProfileSaveFailedText => MumText.Get("ProfileSaveFailed");
        private static string ProfileSummaryFormat => MumText.Get("ProfileSummary");
        private static string ProfileUnreadableFormat => MumText.Get("ProfileUnreadable");

        private string _profileName = "";
        private string _profileStatus = "";
        private string _profileDiffTitle = "";
        private List<ProfileDiffRow> _profileDiff;
        private List<string> _profileNames;
        private ProfileStore _profileStore;

        private ProfileStore Profiles => _profileStore ?? (_profileStore = new ProfileStore(_config.ProfilesDir));

        private void DrawProfiles()
        {
            GUILayout.Space(10);
            GUILayout.Label(ProfilesHeaderText, _headerStyle);
            GUILayout.Space(4);
            GUILayout.Label(ProfilesNoteText, _errorStyle);

            GUILayout.BeginHorizontal();
            GUILayout.Label(ProfileNameLabelText, GUILayout.Width(100));
            _profileName = GUILayout.TextField(_profileName, 60, GUILayout.Width(220));
            if (GUILayout.Button(ProfileSaveText, GUILayout.Width(120)))
            {
                var rows = ModProfile.Snapshot(_updateChecker.InstalledMods);
                if (ModProfile.SafeName(_profileName) == null) _profileStatus = ProfileBadNameText;
                else if (Profiles.Save(_profileName, rows)) { _profileStatus = string.Format(ProfileSavedFormat, _profileName.Trim(), rows.Count); _profileNames = null; }
                else _profileStatus = ProfileSaveFailedText;
            }
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_profileStatus)) GUILayout.Label(_profileStatus, _upToDateStyle);

            if (_profileNames == null) _profileNames = Profiles.List();
            if (_profileNames.Count == 0) GUILayout.Label(ProfileNoneText, _errorStyle);

            string toDelete = null;
            foreach (var name in _profileNames)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(name, _modNameStyle, GUILayout.Width(260));
                if (GUILayout.Button(ProfileCompareText, GUILayout.Width(100)))
                {
                    int skipped;
                    var saved = Profiles.Load(name, out skipped);
                    if (saved != null)
                    {
                        _profileDiff = ModProfile.Diff(saved, ModProfile.Snapshot(_updateChecker.InstalledMods));
                        var different = _profileDiff.Count(d => d.Kind != ProfileDiffKind.Same);
                        _profileDiffTitle = string.Format(ProfileSummaryFormat, name, different, _profileDiff.Count - different)
                            + (skipped > 0 ? " " + string.Format(ProfileUnreadableFormat, skipped) : "");
                    }
                }
                if (GUILayout.Button(ProfileDeleteText, GUILayout.Width(80))) toDelete = name;
                GUILayout.EndHorizontal();
            }
            if (toDelete != null)
            {
                Profiles.Delete(toDelete);
                _profileNames = null;
            }

            if (_profileDiff != null)
            {
                GUILayout.Label(_profileDiffTitle, _modNameStyle);
                foreach (var d in _profileDiff)
                    if (d.Kind != ProfileDiffKind.Same)
                        GUILayout.Label("  " + ModProfile.Describe(d), _summaryStyle);
            }
        }
        // ── end Profiles ─────────────────────────────────────────────────────

        // ── Loaded status and load order (plan feature 21) ───────────────────
        // Texts live in LoadedStatus (host-free, gated); Mod_Update_Manager_Loaded* and _LoadOrder* rows are in
        // Localization/SimpEn.csv and SimpCn.csv, unread until the runtime localization feature. The facts are
        // read when a row or the list is first drawn after a scan, never at load time, because the framework
        // answers only once it has finished loading.
        private List<InstalledModInfo> _loadedFor;
        private Dictionary<string, LoadedRow> _loadedRows;
        private List<LoadedPlugin> _loadedPlugins;

        private void EnsureLoadedFacts()
        {
            var mods = _updateChecker.InstalledMods;
            if (_loadedRows != null && ReferenceEquals(_loadedFor, mods)) return;
            _loadedFor = mods;
            _loadedPlugins = LoadedProbe.ReadPlugins();
            var rows = LoadedStatus.Compute(mods, _loadedPlugins, LoadedProbe.ReadFrameworkDirs());
            _loadedRows = new Dictionary<string, LoadedRow>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rows) _loadedRows[r.Folder] = r;
        }

        private void DrawLoadedStatus(InstalledModInfo mod)
        {
            EnsureLoadedFacts();
            LoadedRow row;
            if (!_loadedRows.TryGetValue(mod.FolderName, out row)) return;
            var text = LoadedStatus.PluginText(row);
            var fw = LoadedStatus.FrameworkText(row);
            if (fw.Length > 0) text += "  |  " + fw;
            GUILayout.Label("  " + text, row.Plugin == LoadedFact.No ? _errorStyle : _summaryStyle);
        }

        private static string LoadOrderShowText => MumText.Get("LoadOrderShow");
        private static string LoadOrderHideText => MumText.Get("LoadOrderHide");
        private bool _loadOrderOpen;

        private void DrawLoadOrder()
        {
            GUILayout.Space(12);
            if (GUILayout.Button(_loadOrderOpen ? LoadOrderHideText : LoadOrderShowText, GUILayout.Width(160)))
                _loadOrderOpen = !_loadOrderOpen;
            if (!_loadOrderOpen) return;

            EnsureLoadedFacts();
            GUILayout.Label(LoadedStatus.LoadOrderHeaderText, _headerStyle);
            foreach (var line in LoadedStatus.OrderLines(_loadedPlugins))
                GUILayout.Label("  " + line, _summaryStyle);
        }
        // ── end Loaded status ────────────────────────────────────────────────

        private void DrawModEntry(InstalledModInfo mod)
        {
            var nameStyle = mod.IsIgnored ? _ignoredStyle
                          : mod.IsFavorited ? _favoriteStyle
                          : _modNameStyle;

            GUILayout.BeginVertical("box");

            // ── Row 1: star | name | author | endorsements | version ──
            GUILayout.BeginHorizontal();

            // Favorite toggle
            var starLabel = mod.IsFavorited ? "★" : "☆";
            if (GUILayout.Button(starLabel, GUILayout.Width(24), GUILayout.Height(20)))
            {
                mod.IsFavorited = !mod.IsFavorited;
                _preferences?.SetFavorited(mod.FolderName, mod.IsFavorited);
            }

            GUILayout.Label(mod.Name, nameStyle, GUILayout.Width(220));
            GUILayout.Label(string.Format(MumText.Get("ByAuthorFormat"), mod.Author), _errorStyle, GUILayout.Width(130));

            if (mod.EndorsementCount > 0)
                GUILayout.Label("♥ " + mod.EndorsementCount, _upToDateStyle, GUILayout.Width(60));

            GUILayout.FlexibleSpace();

            if (mod.NeedsUpdate)
            {
                var verStyle = mod.IsMajorVersionUpdate ? _majorUpdateStyle : _updateAvailableStyle;
                GUILayout.Label(string.Format(MumText.Get("VersionArrowFormat"), mod.Version, mod.LatestVersion), verStyle);
            }
            else if (!mod.CheckFailed && !string.IsNullOrEmpty(mod.LatestVersion) && mod.LatestVersion != "No Nexus ID")
            {
                GUILayout.Label(string.Format(MumText.Get("VersionOkFormat"), mod.Version), _upToDateStyle);
            }
            else
            {
                GUILayout.Label(string.Format(MumText.Get("VersionFormat"), mod.Version), _errorStyle);
            }

            GUILayout.EndHorizontal();

            // ── Row 1b: NEW chip, until Mark all as seen ──
            DrawVersionChip(mod);

            // ── Row 1c: was it loaded? ──
            DrawLoadedStatus(mod);

            // ── Row 2: summary (if available) ──
            if (!string.IsNullOrEmpty(mod.Summary))
            {
                var truncated = mod.Summary.Length > 120 ? mod.Summary.Substring(0, 117) + "..." : mod.Summary;
                GUILayout.Label("  " + truncated, _summaryStyle);
            }

            // ── Row 3: update / error actions ──
            if (mod.NeedsUpdate)
            {
                GUILayout.BeginHorizontal();
                var updateLabel = mod.IsMajorVersionUpdate ? "  " + MumText.Get("MajorUpdateAvailable") : "  " + MumText.Get("UpdateAvailable");
                GUILayout.Label(updateLabel, mod.IsMajorVersionUpdate ? _majorUpdateStyle : _updateAvailableStyle);
                GUILayout.FlexibleSpace();

                if (!string.IsNullOrEmpty(mod.NexusUrl))
                {
                    if (GUILayout.Button(MumText.Get("DiscoveryFeedOpenPage"), GUILayout.Width(130)))
                        Application.OpenURL(mod.NexusUrl);
                }

                DrawChangelogButton(mod);
                GUILayout.EndHorizontal();
            }
            else if (mod.CheckFailed && !string.IsNullOrEmpty(mod.CheckError))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("  " + mod.CheckError, _errorStyle);
                GUILayout.EndHorizontal();
            }
            else if (!mod.CheckFailed && !string.IsNullOrEmpty(mod.LatestVersion) && mod.LatestVersion != "No Nexus ID")
            {
                // Up to date — still allow changelog viewing
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                DrawChangelogButton(mod);
                GUILayout.EndHorizontal();
            }

            // ── Row 4: inline changelog ──
            DrawInlineChangelog(mod);

            // ── Row 5: notes ──
            DrawInlineNotes(mod);

            // ── Row 5b: Endorse (opt-in, asks first) ──
            DrawEndorseControl(mod);

            // ── Row 6: ignore toggle ──
            GUILayout.BeginHorizontal();
            GUILayout.Space(4);
            var ignoreLabel = mod.IsIgnored ? MumText.Get("Unignore") : MumText.Get("Ignore");
            if (GUILayout.Button(ignoreLabel, GUILayout.Width(70)))
            {
                mod.IsIgnored = !mod.IsIgnored;
                _preferences?.SetIgnored(mod.FolderName, mod.IsIgnored);
            }
            if (_versionLedger != null)
            {
                bool historyOpen = _historyExpanded.Contains(mod.FolderName);
                if (GUILayout.Button(historyOpen ? HideHistoryButtonText : HistoryButtonText, GUILayout.Width(100)))
                {
                    if (historyOpen) _historyExpanded.Remove(mod.FolderName);
                    else _historyExpanded.Add(mod.FolderName);
                }
            }
            GUILayout.EndHorizontal();

            // ── Row 7: version history ──
            DrawHistory(mod);

            GUILayout.EndVertical();
        }

        private void DrawChangelogButton(InstalledModInfo mod)
        {
            if (!_config.HasApiKey) return;

            var nexusId = GetNexusIdForMod(mod);
            if (string.IsNullOrEmpty(nexusId)) return;

            bool expanded = _changelogExpanded.TryGetValue(mod.FolderName, out var e) && e;
            var label = expanded ? MumText.Get("HideChangelog") : MumText.Get("Changelog");

            if (GUILayout.Button(label, GUILayout.Width(110)))
            {
                _changelogExpanded[mod.FolderName] = !expanded;
                if (!expanded && !_changelogCache.ContainsKey(mod.FolderName) && !_changelogFetching.Contains(mod.FolderName))
                {
                    _changelogFetching.Add(mod.FolderName);
                    _nexusClient.GetModChangelogs(nexusId, (data, error) =>
                    {
                        _changelogFetching.Remove(mod.FolderName);
                        _changelogCache[mod.FolderName] = (data, error);
                        var token = CompatibilityChip.FindTestedBuild(data);
                        if (token != null)
                            _testedBuildCache[mod.FolderName] = (token, CompatibilityChip.Compare(token, ModScanner.GetGameVersion()));
                        else
                            _testedBuildCache.Remove(mod.FolderName);
                    });
                }
            }
        }

        private void DrawInlineChangelog(InstalledModInfo mod)
        {
            bool expanded = _changelogExpanded.TryGetValue(mod.FolderName, out var e) && e;
            if (!expanded) return;

            GUILayout.Space(4);
            GUILayout.BeginVertical("box");

            if (_changelogFetching.Contains(mod.FolderName))
            {
                GUILayout.Label(MumText.Get("ChangelogFetching"), _errorStyle);
            }
            else if (_changelogCache.TryGetValue(mod.FolderName, out var cached))
            {
                if (!string.IsNullOrEmpty(cached.error))
                {
                    GUILayout.Label(string.Format(MumText.Get("ChangelogUnavailableFormat"), cached.error), _errorStyle);
                }
                else if (cached.data == null || cached.data.Count == 0)
                {
                    GUILayout.Label(MumText.Get("ChangelogNone"), _errorStyle);
                }
                else
                {
                    if (!_changelogScroll.ContainsKey(mod.FolderName))
                        _changelogScroll[mod.FolderName] = Vector2.zero;

                    _changelogScroll[mod.FolderName] = GUILayout.BeginScrollView(
                        _changelogScroll[mod.FolderName], GUILayout.Height(140));

                    foreach (var (version, entries) in cached.data)
                    {
                        GUILayout.Label(string.Format(MumText.Get("VersionFormat"), version), _modNameStyle);
                        foreach (var entry in entries)
                            GUILayout.Label("  • " + entry, _summaryStyle);
                        GUILayout.Space(4);
                    }

                    GUILayout.EndScrollView();
                    DrawTestedBuildChip(mod);
                }
            }
            else
            {
                GUILayout.Label(MumText.Get("ChangelogClickHint"), _errorStyle);
            }

            GUILayout.EndVertical();
        }

        // English-column templates (Localization/SimpEn.csv + SimpCn.csv):
        //   Mod_Update_Manager_TestedBuild       {0}=build named in the changelog
        //   Mod_Update_Manager_TestedBuildOlder  {0}=build named in the changelog, {1}=running game version
        private static string TestedBuildFormat => MumText.Get("TestedBuild");
        private static string TestedBuildOlderFormat => MumText.Get("TestedBuildOlder");

        // The build the mod's own release notes say it was tested on, read from the changelog just
        // fetched (CompatibilityChip), so no number here is typed by hand. Green when that build is
        // the running one or newer, orange when older, grey when either side does not read as
        // MAJOR.MINOR. Draws nothing when the changelog names no build.
        private void DrawTestedBuildChip(InstalledModInfo mod)
        {
            if (!_testedBuildCache.TryGetValue(mod.FolderName, out var chip)) return;
            switch (chip.Verdict)
            {
                case CompatibilityChip.Verdict.Current:
                    GUILayout.Label(string.Format(TestedBuildFormat, chip.Token), _upToDateStyle);
                    break;
                case CompatibilityChip.Verdict.Older:
                    GUILayout.Label(string.Format(TestedBuildOlderFormat, chip.Token, ModScanner.GetGameVersion()), _updateAvailableStyle);
                    break;
                default:
                    GUILayout.Label(string.Format(TestedBuildFormat, chip.Token), _errorStyle);
                    break;
            }
        }

        private void DrawInlineNotes(InstalledModInfo mod)
        {
            bool expanded = _notesExpanded.TryGetValue(mod.FolderName, out var e) && e;
            var hasNotes = !string.IsNullOrEmpty(mod.Notes);
            var notesLabel = expanded ? MumText.Get("HideNotes") : (hasNotes ? string.Format(MumText.Get("NotesFormat"), mod.Notes.Substring(0, Math.Min(mod.Notes.Length, 30))) : MumText.Get("AddNotes"));

            GUILayout.BeginHorizontal();
            GUILayout.Space(4);
            if (GUILayout.Button(notesLabel, GUILayout.Width(200)))
            {
                _notesExpanded[mod.FolderName] = !expanded;
                if (!expanded)
                {
                    if (!_notesInput.ContainsKey(mod.FolderName))
                        _notesInput[mod.FolderName] = mod.Notes ?? "";
                }
            }
            GUILayout.EndHorizontal();

            if (!expanded) return;

            GUILayout.BeginHorizontal();
            GUILayout.Space(4);
            _notesInput.TryGetValue(mod.FolderName, out var current);
            _notesInput[mod.FolderName] = GUILayout.TextField(current ?? "", GUILayout.Width(320));
            if (GUILayout.Button(MumText.Get("Save"), GUILayout.Width(50)))
            {
                mod.Notes = _notesInput[mod.FolderName];
                _preferences?.SetNotes(mod.FolderName, mod.Notes);
                _notesExpanded[mod.FolderName] = false;
            }
            if (GUILayout.Button(MumText.Get("Cancel"), GUILayout.Width(60)))
                _notesExpanded[mod.FolderName] = false;
            GUILayout.EndHorizontal();
        }

        // ── Endorse (plan feature 17) ───────────────────────────────────────
        // Behind EnableEndorseButton (off by default) and an API key, a My Mods row whose mod has a Nexus id
        // and a sendable version gets an Endorse button (NexusEndorse.ShouldOffer). The button only opens the
        // question "Endorse <mod> on Nexus with your account?"; its Yes button, through EndorseConfirm, is the
        // one way to StartEndorse, the one caller of NexusApiClient.EndorseMod. Never at startup, never from a
        // check, a schedule or a loop over the mods. The line under the row comes from the HTTP status alone
        // (NexusEndorse.Classify); Nexus's reply is never read. Rows Mod_Update_Manager_EndorseButton,
        // _EndorseConfirm ({0}=mod name), _EndorseYes, _EndorseNo, _EndorseSending, _EndorseDone,
        // _EndorseRefused ({0}=HTTP status), _EndorseKeyRejected, _EndorseRateLimited, _EndorseUnconfirmed
        // ({0}=HTTP status), _EndorseUnreachable, _EndorseNotSent and _EnableEndorse in Localization/SimpEn.csv
        // and SimpCn.csv; nothing reads them until runtime localization lands.
        private static string EndorseButtonText => MumText.Get("EndorseButton");
        private static string EndorseConfirmFormat => MumText.Get("EndorseConfirm");
        private static string EndorseYesText => MumText.Get("EndorseYes");
        private static string EndorseNoText => MumText.Get("EndorseNo");
        private static string EndorseSendingText => MumText.Get("EndorseSending");
        private static string EndorseDoneText => MumText.Get("EndorseDone");
        private static string EndorseRefusedFormat => MumText.Get("EndorseRefused");
        private static string EndorseKeyRejectedText => MumText.Get("EndorseKeyRejected");
        private static string EndorseRateLimitedText => MumText.Get("EndorseRateLimited");
        private static string EndorseUnconfirmedFormat => MumText.Get("EndorseUnconfirmed");
        private static string EndorseUnreachableText => MumText.Get("EndorseUnreachable");
        private static string EndorseNotSentText => MumText.Get("EndorseNotSent");
        private static string EnableEndorseText => MumText.Get("EnableEndorse");

        private readonly EndorseConfirm _endorseConfirm = new EndorseConfirm();
        private readonly HashSet<string> _endorseSending = new HashSet<string>();                         // FolderNames with a request in flight
        private readonly Dictionary<string, string> _endorseResult = new Dictionary<string, string>();   // FolderName -> the last answer's line

        private static string DescribeEndorseOutcome(EndorseOutcome outcome, long httpStatus)
        {
            switch (outcome)
            {
                case EndorseOutcome.Endorsed: return EndorseDoneText;
                case EndorseOutcome.Refused: return string.Format(EndorseRefusedFormat, httpStatus);
                case EndorseOutcome.KeyRejected: return EndorseKeyRejectedText;
                case EndorseOutcome.RateLimited: return EndorseRateLimitedText;
                case EndorseOutcome.Unreachable: return EndorseUnreachableText;
                case EndorseOutcome.NotSent: return EndorseNotSentText;
                default: return string.Format(EndorseUnconfirmedFormat, httpStatus);
            }
        }

        private void DrawEndorseControl(InstalledModInfo mod)
        {
            var nexusId = GetNexusIdForMod(mod);
            var offered = NexusEndorse.ShouldOffer(_config.EnableEndorseButton.Value, _config.HasApiKey, nexusId, mod.Version);
            if (!offered)
            {
                if (_endorseConfirm.IsAsking(mod.FolderName)) _endorseConfirm.Cancel();
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Space(4);
            if (_endorseConfirm.IsAsking(mod.FolderName))
            {
                GUILayout.Label(string.Format(EndorseConfirmFormat, mod.Name), _updateAvailableStyle);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(EndorseYesText, GUILayout.Width(60)) && _endorseConfirm.TryConfirm(mod.FolderName))
                    StartEndorse(mod, nexusId);
                if (GUILayout.Button(EndorseNoText, GUILayout.Width(60)))
                    _endorseConfirm.Cancel();
            }
            else
            {
                var sending = _endorseSending.Contains(mod.FolderName);
                GUI.enabled = !sending;
                if (GUILayout.Button(EndorseButtonText, GUILayout.Width(80)))
                    _endorseConfirm.Ask(mod.FolderName);
                GUI.enabled = true;
                if (sending)
                    GUILayout.Label(EndorseSendingText, _errorStyle);
                else if (_endorseResult.TryGetValue(mod.FolderName, out var line))
                    GUILayout.Label(line, _summaryStyle);
            }
            GUILayout.EndHorizontal();
        }

        // The one place an endorsement is sent, reached only from the Yes button above. It asks the policy
        // again, so a click that slipped past a disabled control sends nothing, and it sends one request for
        // one row.
        private void StartEndorse(InstalledModInfo mod, string nexusId)
        {
            if (!NexusEndorse.ShouldOffer(_config.EnableEndorseButton.Value, _config.HasApiKey, nexusId, mod.Version)) return;
            var folder = mod.FolderName;
            if (_endorseSending.Contains(folder)) return;

            _endorseSending.Add(folder);
            _endorseResult.Remove(folder);
            _nexusClient.EndorseMod(nexusId, NexusEndorse.VersionToSend(nexusId, mod.Version), (outcome, httpStatus) =>
            {
                _endorseSending.Remove(folder);
                _endorseResult[folder] = DescribeEndorseOutcome(outcome, httpStatus);
                Plugin.Logger.LogInfo($"Endorse Nexus mod {nexusId}: {outcome} (HTTP {httpStatus}).");
            });
        }
        // ── end Endorse ─────────────────────────────────────────────────────

        // Resolves the Nexus mod ID for a given mod (same priority chain as UpdateChecker)
        private string GetNexusIdForMod(InstalledModInfo mod)
        {
            if (!string.IsNullOrEmpty(mod.NexusModId)) return mod.NexusModId;
            var mapped = _mappingManager.GetNexusModId(mod.Name) ?? _mappingManager.GetNexusModId(mod.FolderName);
            if (mapped != null) return mapped;
            return KnownModRegistry.GetNexusModId(mod.Name) ?? KnownModRegistry.GetNexusModId(mod.FolderName);
        }
    }
}
