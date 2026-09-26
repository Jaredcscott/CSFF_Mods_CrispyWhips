using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Repeat_Action;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public class Plugin : BaseUnityPlugin
{
    private const string PluginGuid = "crispywhips.repeat_action";
    public const string PluginName = "Repeat_Action";
    public const string PluginVersion = "2.1.6";

    internal new static ManualLogSource Logger;
    private static Harmony _harmony;
    
    // Static instance for coroutine access from patches
    public static Plugin Instance { get; private set; }

    // Configuration
    public static ConfigEntry<KeyCode> RepeatKey { get; private set; }
    public static ConfigEntry<KeyCode> RepeatModifierKey { get; private set; }
    public static ConfigEntry<KeyCode> RepeatKey2 { get; private set; }
    public static ConfigEntry<KeyCode> RepeatModifierKey2 { get; private set; }
    public static ConfigEntry<int> DefaultRepeatCount { get; private set; }
    public static ConfigEntry<int> MaxRepeatCount { get; private set; }
    public static ConfigEntry<int> MaxUnboundedIterations { get; private set; }
    public static ConfigEntry<bool> PerCardGroupRepeat { get; private set; }
    public static ConfigEntry<bool> VerboseRunDiagnostics { get; private set; }
    public static ConfigEntry<bool> ShowNotifications { get; private set; }
    public static ConfigEntry<bool> ShowCountIndicator { get; private set; }
    public static ConfigEntry<bool> ShowProgressBar { get; private set; }
    public static ConfigEntry<bool> StopOnLowStats { get; private set; }
    public static ConfigEntry<bool> StopOnToolBreak { get; private set; }
    public static ConfigEntry<bool> StopOnInventoryFull { get; private set; }
    public static ConfigEntry<int> StaminaStopThreshold { get; private set; }
    public static ConfigEntry<int> SatiationStopThreshold { get; private set; }
    public static ConfigEntry<int> HydrationStopThreshold { get; private set; }
    public static ConfigEntry<float> ActionCompletionTimeout { get; private set; }
    public static ConfigEntry<string> ExtraBlockedActions { get; private set; }
    public static ConfigEntry<string> ExtraStatThresholds { get; private set; }

    // Runtime state - Current repeat count setting. 0 is the "unlimited" sentinel:
    // repeat until a safety stop, a requirement failure, cancel, or the
    // MaxUnboundedIterations cap fires.
    public static int CurrentRepeatCount { get; set; } = 5;

    public const int UnlimitedCount = 0;
    public static bool IsUnlimited => CurrentRepeatCount <= UnlimitedCount;
    /// <summary>Player-facing count text - "Unlimited" for the 0 sentinel.</summary>
    public static string CountLabel => IsUnlimited ? "Unlimited" : CurrentRepeatCount.ToString();

    // Visual notification
    private static float _notificationEndTime = 0f;
    private static string _notificationText = "";
    private static GUIStyle _notificationStyle;
    private static GUIStyle _shadowStyle;
    private const float NotificationDuration = 2.0f;

    // Persistent count indicator (Show Count Indicator) - smaller styles derived from the toast's
    private static GUIStyle _indicatorStyle;
    private static GUIStyle _indicatorShadowStyle;

    // In-run progress bar (Show Progress Bar). Written by the replay loop via ReportRunProgress /
    // EndRunProgress, read by OnGUI. _runProgress is 0-1 for a bounded run and -1 for Unlimited
    // (no fixed denominator, drawn as an indeterminate sweep). After the run ends the bar lingers
    // until _progressHoldUntil so a completed run is seen reaching full alongside its final toast.
    private static float _runProgress = -1f;
    private static bool _runProgressActive;
    private static float _progressHoldUntil;
    private static Texture2D _barTexture;
    private const float ProgressBarWidth = 500f;
    private const float ProgressBarHeight = 10f;

    // Hold-to-repeat for +/- keys
    private float _adjustHoldTime = 0f;
    private float _adjustNextFireTime = 0f;
    private const float AdjustInitialDelay = 0.4f;  // seconds before repeat starts
    private const float AdjustRepeatRate = 0.08f;    // seconds between repeats while held

    private void Awake()
    {
        Instance = this;
        Logger = base.Logger;
        
        // Initialize configuration
        RepeatKey = Config.Bind(
            "Keybindings",
            "Repeat Action Key",
            KeyCode.R,
            "Key to repeat the last action (requires modifier key to be held)");

        RepeatModifierKey = Config.Bind(
            "Keybindings",
            "Repeat Modifier Key",
            KeyCode.LeftShift,
            "Modifier key that must be held while pressing Repeat Key");

        RepeatKey2 = Config.Bind(
            "Keybindings",
            "Repeat Action Key (Alt)",
            KeyCode.R,
            "Alternate key to repeat the last action (for right-hand usage)");

        RepeatModifierKey2 = Config.Bind(
            "Keybindings",
            "Repeat Modifier Key (Alt)",
            KeyCode.RightShift,
            "Alternate modifier key (for right-hand usage)");

        DefaultRepeatCount = Config.Bind(
            "Repeat Settings",
            "Default Repeat Count",
            5,
            "Default number of times to repeat an action (0 = Unlimited)");

        MaxRepeatCount = Config.Bind(
            "Repeat Settings",
            "Maximum Repeat Count",
            50,
            "Maximum number of times an action can be repeated");

        MaxUnboundedIterations = Config.Bind(
            "Repeat Settings",
            "Maximum Unlimited Iterations",
            500,
            new ConfigDescription(
                "Hard ceiling for Unlimited mode (count lowered below 1). A backstop only - Unlimited normally ends on a safety stop, a requirement failure, or cancel.",
                new AcceptableValueRange<int>(1, 10000)));

        PerCardGroupRepeat = Config.Bind(
            "Repeat Settings",
            "Per-Card Group Repeat",
            false,
            "Group actions (Eat All, group harvests, ...) normally replay as one whole-group sweep per iteration, so the repeat count means sweeps. Turn this on to process exactly ONE card of the captured group per iteration instead, giving the count a hard per-card meaning; the run stops with 'no more targets' once the group is exhausted. Off = the original whole-group behavior.");

        ExtraBlockedActions = Config.Bind(
            "Repeat Settings",
            "Extra Blocked Actions",
            "",
            "Comma-separated action names that must never be repeated, merged with the built-in blocklist (\"continue\"). Whole-word, case-insensitive - e.g. \"sleep, butcher\".");

        VerboseRunDiagnostics = Config.Bind(
            "Repeat Settings",
            "Verbose Run Diagnostics",
            false,
            "While a run is active, log an Info-level line for every stop/abort decision (which safety gate tripped, which card could not be found, what the game's availability check said) to BepInEx/LogOutput.log, so you can read why a run stopped without enabling BepInEx debug logging. Off (default) keeps the log to the usual start/stop summary lines. Never logs outside a run.");

        ShowNotifications = Config.Bind(
            "Display",
            "Show Notifications",
            true,
            "Show on-screen notifications when repeating actions");

        ShowCountIndicator = Config.Bind(
            "Display",
            "Show Count Indicator",
            false,
            "Show a small persistent 'Repeat: x5' label in the top-right corner whenever a card popup is open, so the configured count is readable without starting a run. Updates live with Shift+wheel / Shift+Plus/Minus; hidden when no popup is open.");

        ShowProgressBar = Config.Bind(
            "Display",
            "Show Progress Bar",
            true,
            "Draw a fill bar under the '{completed}/{count}' notification while a run is active. Fills as iterations complete and vanishes shortly after the run ends. Unlimited runs have no fixed total, so the bar shows a moving sweep instead of a fill. Independent of Show Notifications.");

        StopOnLowStats = Config.Bind(
            "Safety",
            "Stop On Low Stats",
            true,
            "Automatically stop repeating when one of your status conditions blocks actions (starving, dehydrated, exhausted, ...). The stop message is the game's own reason.");

        StopOnToolBreak = Config.Bind(
            "Safety",
            "Stop On Tool Break",
            true,
            "Stop repeating when a drag-drop tool transforms (e.g. axe wears out and changes state)");

        StopOnInventoryFull = Config.Bind(
            "Safety",
            "Stop On Inventory Full",
            false,
            "Stop repeating once every container you are carrying is full. Off by default: many actions drop their output on the ground rather than into your inventory, so this would stop those early. Ignored when you carry no container.");

        StaminaStopThreshold = Config.Bind(
            "Safety",
            "Stamina Stop Threshold (%)",
            0,
            new BepInEx.Configuration.ConfigDescription(
                "Stop repeating when Stamina drops below this percentage (0 = disabled)",
                new BepInEx.Configuration.AcceptableValueRange<int>(0, 100)));

        SatiationStopThreshold = Config.Bind(
            "Safety",
            "Satiation Stop Threshold (%)",
            0,
            new BepInEx.Configuration.ConfigDescription(
                "Stop repeating when Satiation drops below this percentage (0 = disabled)",
                new BepInEx.Configuration.AcceptableValueRange<int>(0, 100)));

        HydrationStopThreshold = Config.Bind(
            "Safety",
            "Hydration Stop Threshold (%)",
            0,
            new BepInEx.Configuration.ConfigDescription(
                "Stop repeating when Hydration drops below this percentage (0 = disabled)",
                new BepInEx.Configuration.AcceptableValueRange<int>(0, 100)));

        ExtraStatThresholds = Config.Bind(
            "Safety",
            "Extra Stat Thresholds",
            "",
            "Stop floors for ANY stat, vanilla or modded, checked alongside the three above. Comma-separated 'guid:percent' pairs, where guid is the stat's UniqueID and percent (1-100) is the share of the stat's current maximum below which the run stops - e.g. \"888d2d2a99e3f044291c6748a0fa8d78:30, 4a27fb5da9326b545a5ef73f2b80316e:25\" stops when Body Temperature falls under 30% or Morale under 25%. The stop message uses the stat's own name. A malformed entry is skipped (breadcrumb in the log) without affecting the others. Empty = no extra stops. Vanilla stat UniqueIDs: see the README.");

        ActionCompletionTimeout = Config.Bind(
            "Timeout Settings",
            "Action Completion Timeout (seconds)",
            30f,
            "Maximum time to wait for the game to be ready before aborting the repeat sequence");

        CurrentRepeatCount = DefaultRepeatCount.Value;

        Logger.LogInfo($"{PluginName} v{PluginVersion} loaded.");

        // Initialize and apply Harmony patches
        _harmony = new Harmony(PluginGuid);
        try
        {
            Repeat_Action.Patcher.ActionPatch.ApplyPatch(_harmony);
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Failed to apply Harmony patches: {ex}");
        }
    }

    private void Update()
    {
        bool modifier1Held = Input.GetKey(RepeatModifierKey.Value);
        bool modifier2Held = Input.GetKey(RepeatModifierKey2.Value);
        bool modifierHeld = modifier1Held || modifier2Held;
        
        if (modifierHeld)
        {
            // Adjust repeat count with +/- (hold to repeat) — either shift key works
            bool plusHeld = Input.GetKey(KeyCode.Equals) || Input.GetKey(KeyCode.KeypadPlus);
            bool minusHeld = Input.GetKey(KeyCode.Minus) || Input.GetKey(KeyCode.KeypadMinus);
            bool plusDown = Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus);
            bool minusDown = Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus);

            if (plusDown || minusDown)
            {
                // First press — apply immediately and start hold timer
                AdjustRepeatCount(plusDown ? 1 : -1);
                _adjustHoldTime = 0f;
                _adjustNextFireTime = AdjustInitialDelay;
            }
            else if (plusHeld || minusHeld)
            {
                // Held — after initial delay, fire at repeat rate
                _adjustHoldTime += Time.unscaledDeltaTime;
                if (_adjustHoldTime >= _adjustNextFireTime)
                {
                    AdjustRepeatCount(plusHeld ? 1 : -1);
                    _adjustNextFireTime += AdjustRepeatRate;
                }
            }
            else
            {
                _adjustHoldTime = 0f;
                _adjustNextFireTime = 0f;
            }

            // Scroll wheel while the modifier is held adjusts the same count.
            float scroll = Input.mouseScrollDelta.y;
            if (scroll > 0.01f) AdjustRepeatCount(1);
            else if (scroll < -0.01f) AdjustRepeatCount(-1);

            // Trigger repeat — check both bindings
            if ((modifier1Held && Input.GetKeyDown(RepeatKey.Value)) ||
                (modifier2Held && Input.GetKeyDown(RepeatKey2.Value)))
            {
                TriggerRepeat();
            }
        }
        else
        {
            _adjustHoldTime = 0f;
            _adjustNextFireTime = 0f;
        }
    }

    /// <summary>
    /// Move the repeat count by one step and toast the result. The floor is the
    /// <see cref="UnlimitedCount"/> sentinel (0), so stepping below 1 enters Unlimited mode
    /// rather than sticking at 1.
    /// </summary>
    private static void AdjustRepeatCount(int delta)
    {
        int next = Mathf.Clamp(CurrentRepeatCount + delta, UnlimitedCount, MaxRepeatCount.Value);
        if (next == CurrentRepeatCount) return;
        CurrentRepeatCount = next;
        ShowNotification($"Repeat Count: {CountLabel}");
    }

    private void TriggerRepeat()
    {
        if (Patcher.ActionPatch.IsRepeating)
        {
            // Cancel current repeat
            Patcher.ActionPatch.CancelRepeat();
            ShowNotification("Repeat Cancelled");
            return;
        }

        if (!Patcher.ActionPatch.HasLastAction)
        {
            // Show specific "not supported" message if the action name is known
            string lastAction = Patcher.ActionPatch.LastActionName;
            if (lastAction != "Unknown" && !string.IsNullOrEmpty(lastAction))
            {
                ShowNotification($"'{lastAction}' is not supported");
                Logger.LogInfo($"[Repeat] Action '{lastAction}' is not permitted for repeat");
            }
            else
            {
                ShowNotification("No action to repeat");
                Logger.LogInfo("[Repeat] No action recorded to repeat");
            }
            return;
        }

        Logger.LogInfo($"[Repeat] Starting repeat of last action ({CountLabel})");
        StartCoroutine(Patcher.ActionPatch.RepeatLastAction(CurrentRepeatCount));
    }

    public static void ShowNotification(string text)
    {
        if (!ShowNotifications.Value) return;
        
        _notificationText = text;
        _notificationEndTime = Time.time + NotificationDuration;
        Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Notification] {text}");
    }

    private void OnGUI()
    {
        EnsureStyles();

        // Persistent count indicator: drawn independently of the transient toast and its timer,
        // so the early-return below governs ONLY the toast.
        if (ShowCountIndicator != null && ShowCountIndicator.Value && Patcher.ActionPatch.IsCardPopupOpen())
            DrawCountIndicator();

        // In-run progress bar: lives with the run, then lingers for the final toast's duration.
        if (_runProgressActive)
        {
            bool runLive = Patcher.ActionPatch.IsRepeating;
            if (!runLive && Time.time >= _progressHoldUntil) _runProgressActive = false;
            else if (ShowProgressBar != null && ShowProgressBar.Value) DrawProgressBar();
        }

        if (Time.time > _notificationEndTime || string.IsNullOrEmpty(_notificationText))
            return;
        DrawToast();
    }

    /// <summary>
    /// Called by the replay loop at run start (completed = 0) and after every completed iteration.
    /// Unlimited runs publish -1: their only denominator is the MaxUnboundedIterations backstop,
    /// which is not what the player is counting toward, so the bar sweeps instead of filling.
    /// </summary>
    public static void ReportRunProgress(int completed, int count, bool unlimited)
    {
        _runProgressActive = true;
        _runProgress = (unlimited || count <= 0) ? -1f : Mathf.Clamp01(completed / (float)count);
    }

    /// <summary>Called when the run ends (any reason): keep the bar up as long as the final toast.</summary>
    public static void EndRunProgress()
    {
        _progressHoldUntil = Time.time + NotificationDuration;
    }

    /// <summary>Fill bar just under the toast's background box (which spans y 90-160).</summary>
    private static void DrawProgressBar()
    {
        if (_barTexture == null)
        {
            _barTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _barTexture.SetPixel(0, 0, Color.white);
            _barTexture.Apply();
        }

        float x = (Screen.width - ProgressBarWidth) / 2f;
        float y = 168f;
        var bar = new Rect(x, y, ProgressBarWidth, ProgressBarHeight);

        GUI.color = new Color(0f, 0f, 0f, 0.7f);
        GUI.DrawTexture(new Rect(bar.x - 2f, bar.y - 2f, bar.width + 4f, bar.height + 4f), _barTexture);

        if (_runProgress >= 0f)
        {
            GUI.color = new Color(0.35f, 0.85f, 0.35f, 0.95f);
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * _runProgress, bar.height), _barTexture);
        }
        else
        {
            // Unlimited: a 20%-wide segment ping-pongs across the track. Unscaled time, so it keeps
            // moving while the game sits at timeScale 0 waiting on the player.
            float segment = bar.width * 0.2f;
            float t = Mathf.PingPong(Time.unscaledTime * 0.5f, 1f);
            GUI.color = new Color(0.55f, 0.75f, 1f, 0.9f);
            GUI.DrawTexture(new Rect(bar.x + (bar.width - segment) * t, bar.y, segment, bar.height), _barTexture);
        }
        GUI.color = Color.white;
    }

    private static void EnsureStyles()
    {
        if (_notificationStyle != null) return;

        _notificationStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 28,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        _notificationStyle.normal.textColor = Color.white;

        _shadowStyle = new GUIStyle(_notificationStyle);
        _shadowStyle.normal.textColor = Color.black;

        _indicatorStyle = new GUIStyle(_notificationStyle)
        {
            fontSize = 16,
            alignment = TextAnchor.MiddleRight
        };
        _indicatorShadowStyle = new GUIStyle(_indicatorStyle);
        _indicatorShadowStyle.normal.textColor = Color.black;
    }

    /// <summary>The transient center-top toast (ShowNotification), visible for NotificationDuration.</summary>
    private static void DrawToast()
    {
        // Calculate position (center-top of screen)
        float boxWidth = 500f;
        float boxHeight = 50f;
        float x = (Screen.width - boxWidth) / 2f;
        float y = 100f;

        // Draw background box
        GUI.color = new Color(0f, 0f, 0f, 0.7f);
        GUI.Box(new Rect(x - 10, y - 10, boxWidth + 20, boxHeight + 20), "");
        GUI.color = Color.white;

        // Draw shadow
        GUI.Label(new Rect(x + 2, y + 2, boxWidth, boxHeight), _notificationText, _shadowStyle);
        // Draw text
        GUI.Label(new Rect(x, y, boxWidth, boxHeight), _notificationText, _notificationStyle);
    }

    /// <summary>
    /// Small always-on "Repeat: x5" label in the top-right corner while a card popup is open, so
    /// the configured count is readable without starting a run. Reads CountLabel live, so
    /// Shift+wheel / Shift+Plus/Minus update it immediately. Sits clear of the center-top toast
    /// (which spans y 90-160). Position is fixed; tune the constants here if it collides with a HUD.
    /// </summary>
    private static void DrawCountIndicator()
    {
        const float w = 170f;
        const float h = 26f;
        float x = Screen.width - w - 16f;
        float y = 64f;
        string text = IsUnlimited ? "Repeat: Unlimited" : $"Repeat: x{CountLabel}";

        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.Box(new Rect(x - 8, y - 4, w + 16, h + 8), "");
        GUI.color = Color.white;

        GUI.Label(new Rect(x + 1, y + 1, w, h), text, _indicatorShadowStyle);
        GUI.Label(new Rect(x, y, w, h), text, _indicatorStyle);
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }
}
