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
    public const string PluginVersion = "1.3.7";

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
    public static ConfigEntry<bool> ShowNotifications { get; private set; }
    public static ConfigEntry<bool> StopOnLowStats { get; private set; }
    public static ConfigEntry<float> ActionCompletionTimeout { get; private set; }
    public static ConfigEntry<int> Gate1TimeoutFrames { get; private set; }
    public static ConfigEntry<float> PreTravelRestTimeout { get; private set; }

    // Runtime state - Current repeat count setting
    public static int CurrentRepeatCount { get; set; } = 5;

    // Mouse click tracking for player-action detection fallback
    private static int _lastMouseClickFrame = -999;
    /// <summary>
    /// True if the player physically clicked the mouse within the last ~0.5 seconds.
    /// Used as a fallback gate for actions that bypass InspectionPopup.OnButtonClicked.
    /// </summary>
    public static bool PlayerRecentlyClicked => Time.frameCount - _lastMouseClickFrame < 30;
    
    // Visual notification
    private static float _notificationEndTime = 0f;
    private static string _notificationText = "";
    private static GUIStyle _notificationStyle;
    private static GUIStyle _shadowStyle;
    private const float NotificationDuration = 2.0f;

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
            "Default number of times to repeat an action");

        MaxRepeatCount = Config.Bind(
            "Repeat Settings",
            "Maximum Repeat Count",
            50,
            "Maximum number of times an action can be repeated");

        ShowNotifications = Config.Bind(
            "Display",
            "Show Notifications",
            true,
            "Show on-screen notifications when repeating actions");

        StopOnLowStats = Config.Bind(
            "Safety",
            "Stop On Low Stats",
            true,
            "Automatically stop repeating when the game detects a critical condition (health, hunger, or event popup)");

        ActionCompletionTimeout = Config.Bind(
            "Timeout Settings",
            "Action Completion Timeout (seconds)",
            30f,
            "Maximum time to wait for an action to complete before aborting the repeat sequence");

        Gate1TimeoutFrames = Config.Bind(
            "Timeout Settings",
            "Gate 1 Timeout (frames)",
            60,
            "Number of frames to wait for ActionRoutine to fire after a button click (at 60fps, 60 frames ≈ 1 second)");

        PreTravelRestTimeout = Config.Bind(
            "Timeout Settings",
            "Pre-Travel Rest Timeout (seconds)",
            15f,
            "Maximum time to wait for a rest action to complete before travel");

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
        // Track mouse clicks for player-action detection
        if (Input.GetMouseButtonDown(0))
            _lastMouseClickFrame = Time.frameCount;

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
                int delta = plusDown ? 1 : -1;
                CurrentRepeatCount = Mathf.Clamp(CurrentRepeatCount + delta, 1, MaxRepeatCount.Value);
                ShowNotification($"Repeat Count: {CurrentRepeatCount}");
                _adjustHoldTime = 0f;
                _adjustNextFireTime = AdjustInitialDelay;
            }
            else if (plusHeld || minusHeld)
            {
                // Held — after initial delay, fire at repeat rate
                _adjustHoldTime += Time.unscaledDeltaTime;
                if (_adjustHoldTime >= _adjustNextFireTime)
                {
                    int delta = plusHeld ? 1 : -1;
                    CurrentRepeatCount = Mathf.Clamp(CurrentRepeatCount + delta, 1, MaxRepeatCount.Value);
                    ShowNotification($"Repeat Count: {CurrentRepeatCount}");
                    _adjustNextFireTime += AdjustRepeatRate;
                }
            }
            else
            {
                _adjustHoldTime = 0f;
                _adjustNextFireTime = 0f;
            }
            
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
                Logger.Log(BepInEx.Logging.LogLevel.Debug, $"Action '{lastAction}' is not permitted for repeat");
            }
            else
            {
                ShowNotification("No action to repeat");
                Logger.Log(BepInEx.Logging.LogLevel.Debug, "No action recorded to repeat");
            }
            return;
        }

        Logger.Log(BepInEx.Logging.LogLevel.Debug, $"Starting repeat of last action ({CurrentRepeatCount} times)");
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
        if (Time.time > _notificationEndTime || string.IsNullOrEmpty(_notificationText))
            return;

        // Initialize styles if needed
        if (_notificationStyle == null)
        {
            _notificationStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 28,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            _notificationStyle.normal.textColor = Color.white;
            
            _shadowStyle = new GUIStyle(_notificationStyle);
            _shadowStyle.normal.textColor = Color.black;
        }

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

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }
}
