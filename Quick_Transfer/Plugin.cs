using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Quick_Transfer;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public class Plugin : BaseUnityPlugin
{
    private const string PluginGuid = "crispywhips.quick_transfer";
    public const string PluginName = "Quick_Transfer";
    public const string PluginVersion = "1.5.5";

    internal new static ManualLogSource Logger;
    private static Harmony _harmony;
    
    // Static instance for coroutine access from patches
    public static Plugin Instance { get; private set; }

    // Configuration
    public static ConfigEntry<int> TransferAmount { get; private set; }
    public static ConfigEntry<KeyCode> ModifierKey { get; private set; }
    public static ConfigEntry<KeyCode> IncreaseKey { get; private set; }
    public static ConfigEntry<KeyCode> DecreaseKey { get; private set; }

    // Runtime state
    public static int CurrentTransferAmount { get; set; } = 5;
    
    // Visual indicator
    private static float _notificationEndTime = 0f;
    private static string _notificationText = "";
    private static GUIStyle _notificationStyle;
    private static GUIStyle _shadowStyle;
    private const float NotificationDuration = 2.0f;
    
    // Key repeat timing
    private static float _keyHoldTime = 0f;
    private static float _lastRepeatTime = 0f;
    private const float InitialRepeatDelay = 0.4f;  // Delay before repeat starts
    private const float RepeatInterval = 0.05f;     // Time between repeats (20 per second)

    private void Awake()
    {
        Instance = this;
        Logger = base.Logger;
        
        // Initialize configuration
        TransferAmount = Config.Bind(
            "Transfer Settings",
            "Transfer Amount",
            5,
            new ConfigDescription(
                "Number of cards to transfer when using Modifier+Right-Click (1-1000)",
                new AcceptableValueRange<int>(1, 1000)));

        ModifierKey = Config.Bind(
            "Keybindings",
            "Modifier Key",
            KeyCode.LeftControl,
            "The modifier key to hold while right-clicking to trigger quick transfer. Common options: LeftControl, RightControl, LeftShift, LeftAlt");

        IncreaseKey = Config.Bind(
            "Keybindings",
            "Increase Amount Key",
            KeyCode.Equals,
            "Key to increase transfer amount (hold Modifier + this key). Default is = (plus key)");

        DecreaseKey = Config.Bind(
            "Keybindings",
            "Decrease Amount Key",
            KeyCode.Minus,
            "Key to decrease transfer amount (hold Modifier + this key)");

        CurrentTransferAmount = TransferAmount.Value;
        
        // Sync CurrentTransferAmount when config changes
        TransferAmount.SettingChanged += (sender, args) => {
            CurrentTransferAmount = TransferAmount.Value;
            Logger.LogDebug($"Transfer amount updated from config: {CurrentTransferAmount}");
        };

        Logger.LogInfo($"{PluginName} v{PluginVersion} loaded.");

        // Initialize and apply Harmony patches

        _harmony = new Harmony(PluginGuid);
        try
        {
            Quick_Transfer.Patcher.QuickTransferPatch.ApplyPatch(_harmony);
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Failed to apply Harmony patches: {ex}");
        }
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }

    private void Update()
    {
        // Handle Modifier+Plus/Minus to adjust stack size
        if (IsModifierKeyHeld())
        {
            bool increaseHeld = Input.GetKey(IncreaseKey.Value);
            bool decreaseHeld = Input.GetKey(DecreaseKey.Value);
            
            if (increaseHeld || decreaseHeld)
            {
                bool shouldTrigger = false;
                
                // First press - trigger immediately
                if (Input.GetKeyDown(IncreaseKey.Value) || Input.GetKeyDown(DecreaseKey.Value))
                {
                    _keyHoldTime = 0f;
                    _lastRepeatTime = 0f;
                    shouldTrigger = true;
                }
                else
                {
                    // Key being held
                    _keyHoldTime += Time.deltaTime;
                    
                    // After initial delay, repeat at interval
                    if (_keyHoldTime >= InitialRepeatDelay)
                    {
                        if (Time.time - _lastRepeatTime >= RepeatInterval)
                        {
                            _lastRepeatTime = Time.time;
                            shouldTrigger = true;
                        }
                    }
                }
                
                if (shouldTrigger)
                {
                    if (increaseHeld)
                    {
                        CurrentTransferAmount = Mathf.Min(CurrentTransferAmount + 1, 1000);
                    }
                    else if (decreaseHeld)
                    {
                        CurrentTransferAmount = Mathf.Max(CurrentTransferAmount - 1, 1);
                    }
                    
                    // Save to config so it persists
                    TransferAmount.Value = CurrentTransferAmount;
                    
                    ShowTransferAmountNotification();
                }
            }
        }
    }

    /// <summary>
    /// Check if the configured modifier key is held (handles both left/right variants for Ctrl/Shift/Alt)
    /// </summary>
    public static bool IsModifierKeyHeld()
    {
        var key = ModifierKey.Value;
        
        // Support both left and right variants when left variant is configured
        return key switch
        {
            KeyCode.LeftControl => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl),
            KeyCode.RightControl => Input.GetKey(KeyCode.RightControl),
            KeyCode.LeftShift => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift),
            KeyCode.RightShift => Input.GetKey(KeyCode.RightShift),
            KeyCode.LeftAlt => Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt),
            KeyCode.RightAlt => Input.GetKey(KeyCode.RightAlt),
            _ => Input.GetKey(key)
        };
    }

    private void OnGUI()
    {
        // Only show if notification is active
        if (Time.time < _notificationEndTime)
        {
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
            float boxWidth = 300f;
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
    }

    private void ShowTransferAmountNotification()
    {
        _notificationText = $"Quick Transfer: {CurrentTransferAmount}";
        _notificationEndTime = Time.time + NotificationDuration;
    }
}
