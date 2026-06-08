using BepInEx.Configuration;
using BepInEx.Logging;

namespace Quick_Transfer;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public class Plugin : BaseUnityPlugin
{
    private const string PluginGuid = "crispywhips.quick_transfer";
    public const string PluginName = "Quick_Transfer";
    public const string PluginVersion = "1.6.1";

    internal new static ManualLogSource Logger;
    private static Harmony _harmony;

    public static Plugin Instance { get; private set; }

    // Configuration — existing
    public static ConfigEntry<int> TransferAmount { get; private set; }
    public static ConfigEntry<KeyCode> ModifierKey { get; private set; }
    public static ConfigEntry<KeyCode> IncreaseKey { get; private set; }
    public static ConfigEntry<KeyCode> DecreaseKey { get; private set; }

    // Configuration — modifier presets
    public static ConfigEntry<bool> EnableModifierPresets { get; private set; }
    public static ConfigEntry<int> ShiftPresetAmount { get; private set; }
    public static ConfigEntry<int> CtrlPresetAmount { get; private set; }

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
    private const float InitialRepeatDelay = 0.4f;
    private const float RepeatInterval = 0.05f;

    private void Awake()
    {
        Instance = this;
        Logger = base.Logger;

        TransferAmount = Config.Bind(
            "Transfer Settings",
            "Transfer Amount",
            5,
            new ConfigDescription(
                "Cards transferred per Modifier+Right-Click when modifier presets are disabled, or when using a non-Ctrl/Shift modifier key. Adjust in-game with Modifier+Plus/Minus.",
                new AcceptableValueRange<int>(1, 1000)));

        ModifierKey = Config.Bind(
            "Keybindings",
            "Modifier Key",
            KeyCode.LeftControl,
            "Modifier key to hold while right-clicking. Used for amount adjustment and as fallback when modifier presets are disabled. Common options: LeftControl, LeftShift, LeftAlt");

        IncreaseKey = Config.Bind(
            "Keybindings",
            "Increase Amount Key",
            KeyCode.Equals,
            "Key to increase transfer amount (hold Modifier + this key). When modifier presets are enabled, adjusts the active preset (Ctrl preset or Shift preset).");

        DecreaseKey = Config.Bind(
            "Keybindings",
            "Decrease Amount Key",
            KeyCode.Minus,
            "Key to decrease transfer amount (hold Modifier + this key). When modifier presets are enabled, adjusts the active preset.");

        EnableModifierPresets = Config.Bind(
            "Modifier Presets",
            "Enable Modifier Presets",
            true,
            "When enabled: Shift+Right-Click transfers ShiftPresetAmount, Ctrl+Right-Click transfers CtrlPresetAmount, Ctrl+Shift+Right-Click transfers the entire stack. Adjust preset amounts in-game with the modifier held + Plus/Minus.");

        ShiftPresetAmount = Config.Bind(
            "Modifier Presets",
            "Shift Preset Amount",
            5,
            new ConfigDescription(
                "Cards transferred per Shift+Right-Click (requires Enable Modifier Presets). Adjust in-game: hold Shift + Plus/Minus.",
                new AcceptableValueRange<int>(1, 1000)));

        CtrlPresetAmount = Config.Bind(
            "Modifier Presets",
            "Ctrl Preset Amount",
            10,
            new ConfigDescription(
                "Cards transferred per Ctrl+Right-Click (requires Enable Modifier Presets). Adjust in-game: hold Ctrl + Plus/Minus.",
                new AcceptableValueRange<int>(1, 1000)));

        CurrentTransferAmount = TransferAmount.Value;

        TransferAmount.SettingChanged += (sender, args) => {
            CurrentTransferAmount = TransferAmount.Value;
        };

        Logger.LogInfo($"{PluginName} v{PluginVersion} loaded.");

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
        bool modHeld = IsModifierKeyHeld();
        bool increaseHeld = Input.GetKey(IncreaseKey.Value);
        bool decreaseHeld = Input.GetKey(DecreaseKey.Value);

        if (modHeld && (increaseHeld || decreaseHeld))
        {
            bool shouldTrigger = false;

            if (Input.GetKeyDown(IncreaseKey.Value) || Input.GetKeyDown(DecreaseKey.Value))
            {
                _keyHoldTime = 0f;
                _lastRepeatTime = 0f;
                shouldTrigger = true;
            }
            else
            {
                _keyHoldTime += Time.deltaTime;
                if (_keyHoldTime >= InitialRepeatDelay && Time.time - _lastRepeatTime >= RepeatInterval)
                {
                    _lastRepeatTime = Time.time;
                    shouldTrigger = true;
                }
            }

            if (shouldTrigger)
            {
                int delta = increaseHeld ? 1 : -1;

                if (EnableModifierPresets.Value)
                {
                    bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                    bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

                    if (ctrl && shift)
                    {
                        ShowNotification("Quick Transfer: All (Ctrl+Shift)");
                    }
                    else if (ctrl)
                    {
                        CtrlPresetAmount.Value = Mathf.Clamp(CtrlPresetAmount.Value + delta, 1, 1000);
                        ShowNotification($"Quick Transfer: {CtrlPresetAmount.Value} (Ctrl)");
                    }
                    else if (shift)
                    {
                        ShiftPresetAmount.Value = Mathf.Clamp(ShiftPresetAmount.Value + delta, 1, 1000);
                        ShowNotification($"Quick Transfer: {ShiftPresetAmount.Value} (Shift)");
                    }
                    else
                    {
                        CurrentTransferAmount = Mathf.Clamp(CurrentTransferAmount + delta, 1, 1000);
                        TransferAmount.Value = CurrentTransferAmount;
                        ShowNotification($"Quick Transfer: {CurrentTransferAmount}");
                    }
                }
                else
                {
                    CurrentTransferAmount = Mathf.Clamp(CurrentTransferAmount + delta, 1, 1000);
                    TransferAmount.Value = CurrentTransferAmount;
                    ShowNotification($"Quick Transfer: {CurrentTransferAmount}");
                }
            }
        }

        // Persistent hint while any transfer-triggering modifier is held
        if (modHeld)
        {
            int eff = GetEffectiveTransferAmount();
            string hint = eff >= 9999 ? "Quick Transfer: All" : $"Quick Transfer: {eff}";
            // Only refresh if text changed or timer nearly expired (avoids overwriting a 2s notification with 0.15s)
            if (hint != _notificationText || _notificationEndTime - Time.time < 0.1f)
            {
                _notificationText = hint;
                _notificationEndTime = Time.time + 0.15f;
            }
        }
    }

    /// <summary>Returns true if the configured modifier key is held. Used for backward-compat amount adjustment.</summary>
    private static bool IsConfiguredModifierHeld()
    {
        var key = ModifierKey.Value;
        return key switch
        {
            KeyCode.LeftControl  => Input.GetKey(KeyCode.LeftControl)  || Input.GetKey(KeyCode.RightControl),
            KeyCode.RightControl => Input.GetKey(KeyCode.RightControl),
            KeyCode.LeftShift    => Input.GetKey(KeyCode.LeftShift)    || Input.GetKey(KeyCode.RightShift),
            KeyCode.RightShift   => Input.GetKey(KeyCode.RightShift),
            KeyCode.LeftAlt      => Input.GetKey(KeyCode.LeftAlt)      || Input.GetKey(KeyCode.RightAlt),
            KeyCode.RightAlt     => Input.GetKey(KeyCode.RightAlt),
            _                    => Input.GetKey(key)
        };
    }

    /// <summary>Returns true if any transfer-triggering modifier is held (presets or configured key).</summary>
    public static bool IsModifierKeyHeld()
    {
        if (EnableModifierPresets.Value)
        {
            bool ctrl  = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift)   || Input.GetKey(KeyCode.RightShift);
            if (ctrl || shift) return true;
        }
        return IsConfiguredModifierHeld();
    }

    /// <summary>Returns the effective transfer count based on the currently held modifier combo.</summary>
    public static int GetEffectiveTransferAmount()
    {
        if (!EnableModifierPresets.Value)
            return CurrentTransferAmount;

        bool ctrl  = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool shift = Input.GetKey(KeyCode.LeftShift)   || Input.GetKey(KeyCode.RightShift);

        if (ctrl && shift) return 9999; // entire stack
        if (ctrl)  return CtrlPresetAmount.Value;
        if (shift) return ShiftPresetAmount.Value;

        // Non-preset modifier (e.g. Alt when configured) → custom amount
        return CurrentTransferAmount;
    }

    public static void ShowNotification(string text)
    {
        _notificationText = text;
        _notificationEndTime = Time.time + NotificationDuration;
    }

    private void OnGUI()
    {
        if (Time.time < _notificationEndTime)
        {
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

            float boxWidth = 350f;
            float boxHeight = 50f;
            float x = (Screen.width - boxWidth) / 2f;
            float y = 100f;

            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.Box(new Rect(x - 10, y - 10, boxWidth + 20, boxHeight + 20), "");
            GUI.color = Color.white;

            GUI.Label(new Rect(x + 2, y + 2, boxWidth, boxHeight), _notificationText, _shadowStyle);
            GUI.Label(new Rect(x, y, boxWidth, boxHeight), _notificationText, _notificationStyle);
        }
    }
}
