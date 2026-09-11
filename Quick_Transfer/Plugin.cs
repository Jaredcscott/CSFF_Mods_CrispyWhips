using BepInEx.Configuration;
using BepInEx.Logging;

namespace Quick_Transfer;

/// <summary>
/// Mouse button that triggers a quick transfer. Values match
/// <c>UnityEngine.EventSystems.PointerEventData.InputButton</c>.
/// </summary>
/// <remarks>
/// Left is deliberately absent. Vanilla's <c>InGameCardBase.OnPointerClick</c> routes a left-click to
/// <c>GraphicsM.InspectCard</c>, never to <c>SwapCard</c>, and the inspected card then fails
/// <c>SwapCard</c>'s own <c>GraphicsM.InspectedCard != this</c> guard - so a left-button trigger would
/// open the inspection popup and then transfer nothing at all.
/// </remarks>
public enum TransferButton
{
    Right = 1,
    Middle = 2
}

/// <summary>What the Ctrl+Shift preset combo transfers.</summary>
public enum CtrlShiftPresetMode
{
    /// <summary>The entire clicked stack.</summary>
    All,
    /// <summary>Half the clicked stack, rounded up (8 -> 4, 7 -> 4, 1 -> 1). Fills the gap between the Ctrl preset and All.</summary>
    Half
}

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency("crispywhips.CSFFModFramework", BepInDependency.DependencyFlags.SoftDependency)]
public class Plugin : BaseUnityPlugin
{
    private const string PluginGuid = "crispywhips.quick_transfer";
    public const string PluginName = "Quick_Transfer";
    public const string PluginVersion = "1.8.0";

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

    // Configuration — full stack mode
    public static ConfigEntry<bool> FullStackMode { get; private set; }

    // Configuration - trigger button, preset reset, batch sound
    public static ConfigEntry<TransferButton> TransferMouseButton { get; private set; }
    public static ConfigEntry<KeyCode> ResetPresetsKey { get; private set; }
    public static ConfigEntry<bool> ConsolidateBatchSound { get; private set; }

    // Configuration - Ctrl+Shift half preset, sibling-slot draining
    public static ConfigEntry<CtrlShiftPresetMode> CtrlShiftMode { get; private set; }
    public static ConfigEntry<bool> DrainMatchingStacks { get; private set; }

    /// <summary>Value <see cref="GetEffectiveTransferAmount"/> returns for "the entire stack".</summary>
    public const int AllSentinel = 9999;

    /// <summary>
    /// Value <see cref="GetEffectiveTransferAmount"/> returns for the Ctrl+Shift combo in Half mode.
    /// Deliberately not a count: the overlay in <see cref="Update"/> has no slot in hand, so only the
    /// click prefix, which holds the clicked slot, can turn it into ceil(pile / 2). Never store it
    /// as a transfer count.
    /// </summary>
    public const int HalfSentinel = int.MinValue;

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
                new AcceptableValueRange<int>(1, 9999)));

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
                new AcceptableValueRange<int>(1, 9999)));

        CtrlPresetAmount = Config.Bind(
            "Modifier Presets",
            "Ctrl Preset Amount",
            10,
            new ConfigDescription(
                "Cards transferred per Ctrl+Right-Click (requires Enable Modifier Presets). Adjust in-game: hold Ctrl + Plus/Minus.",
                new AcceptableValueRange<int>(1, 9999)));

        CtrlShiftMode = Config.Bind(
            "Modifier Presets",
            "Ctrl+Shift Preset Mode",
            CtrlShiftPresetMode.All,
            "What Ctrl+Shift+click transfers (requires Enable Modifier Presets). All moves the entire stack. Half moves half of the clicked stack, rounded up (8 -> 4, 7 -> 4, 1 -> 1), filling the gap between the Ctrl preset and All. The overlay reads 'Half' while the combo is held, since the number depends on which stack you click.");

        FullStackMode = Config.Bind(
            "Transfer Settings",
            "Full Stack Mode",
            false,
            "When enabled, modifier+right-click always transfers the entire stack. Count adjustment keys and preset amounts are ignored.");

        TransferMouseButton = Config.Bind(
            "Transfer Settings",
            "Transfer Mouse Button",
            TransferButton.Right,
            "Mouse button that triggers a bulk transfer while a modifier is held. Right matches the game's own quick-move click. Middle leaves right-click untouched, so a modifier+right-click still moves exactly one card the vanilla way. Left is not offered: the game binds it to card inspection, not transfer.");

        ConsolidateBatchSound = Config.Bind(
            "Transfer Settings",
            "Consolidate Batch Sound",
            true,
            "When enabled, the per-card move sound is muted for the cards this mod moves and one sound plays when the batch finishes, instead of one sound per card on consecutive frames.");

        DrainMatchingStacks = Config.Bind(
            "Transfer Settings",
            "Drain Matching Stacks",
            false,
            "When enabled, a bulk transfer that empties the clicked slot keeps going with the same item from the other slots of the same container (the same open inventory, or the same board area), up to the requested count. When disabled, only the clicked slot is drained.");

        ResetPresetsKey = Config.Bind(
            "Keybindings",
            "Reset Presets Key",
            KeyCode.Backspace,
            "Hold the modifier and press this key to reset Shift/Ctrl/custom transfer amounts to their defaults (5 / 10 / 5). Set to None to disable.");

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

        if (modHeld && ResetPresetsKey.Value != KeyCode.None && Input.GetKeyDown(ResetPresetsKey.Value))
        {
            ResetPresetsToDefaults();
            return;
        }

        if (!FullStackMode.Value && modHeld && (increaseHeld || decreaseHeld))
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
                    bool ctrl = CtrlHeld();
                    bool shift = ShiftHeld();

                    if (ctrl && shift)
                    {
                        // Ctrl+Shift is All or Half by config, never a number the keys can nudge -
                        // just restate what it will do.
                        ShowNotification(AmountText(GetEffectiveTransferAmount()) + ActiveComboSuffix());
                    }
                    else if (ctrl)
                    {
                        CtrlPresetAmount.Value = Mathf.Clamp(CtrlPresetAmount.Value + delta, 1, 9999);
                        ShowNotification(AmountText(CtrlPresetAmount.Value) + ActiveComboSuffix());
                    }
                    else if (shift)
                    {
                        ShiftPresetAmount.Value = Mathf.Clamp(ShiftPresetAmount.Value + delta, 1, 9999);
                        ShowNotification(AmountText(ShiftPresetAmount.Value) + ActiveComboSuffix());
                    }
                    else
                    {
                        CurrentTransferAmount = Mathf.Clamp(CurrentTransferAmount + delta, 1, 9999);
                        TransferAmount.Value = CurrentTransferAmount;
                        ShowNotification(AmountText(CurrentTransferAmount));
                    }
                }
                else
                {
                    CurrentTransferAmount = Mathf.Clamp(CurrentTransferAmount + delta, 1, 9999);
                    TransferAmount.Value = CurrentTransferAmount;
                    ShowNotification(AmountText(CurrentTransferAmount));
                }
            }
        }

        // Persistent hint while any transfer-triggering modifier is held
        if (modHeld)
        {
            int eff = GetEffectiveTransferAmount();
            string hint = AmountText(eff) + ActiveComboSuffix();
            // Only refresh if text changed or timer nearly expired (avoids overwriting a 2s notification with 0.15s)
            if (hint != _notificationText || _notificationEndTime - Time.time < 0.1f)
            {
                _notificationText = hint;
                _notificationEndTime = Time.time + 0.15f;
            }
        }
    }

    private static bool CtrlHeld()  => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
    private static bool ShiftHeld() => Input.GetKey(KeyCode.LeftShift)   || Input.GetKey(KeyCode.RightShift);

    /// <summary>
    /// Returns the " (Ctrl)" / " (Shift)" / " (Ctrl+Shift)" suffix naming the combo that
    /// <see cref="GetEffectiveTransferAmount"/> is currently reading, or "" when no preset combo
    /// decides the count. Mirrors that method's branch structure exactly - if one grows a case the
    /// other must too, or the overlay starts labelling a number it didn't produce.
    /// </summary>
    private static string ActiveComboSuffix()
    {
        if (FullStackMode.Value || !EnableModifierPresets.Value) return "";

        bool ctrl  = CtrlHeld();
        bool shift = ShiftHeld();

        // The loader trims CSV values, so the separating space is ours, not the translation's.
        if (ctrl && shift) return " " + OverlayText.Get(OverlayText.ComboCtrlShiftKey, OverlayText.ComboCtrlShiftDefault);
        if (ctrl)  return " " + OverlayText.Get(OverlayText.ComboCtrlKey, OverlayText.ComboCtrlDefault);
        if (shift) return " " + OverlayText.Get(OverlayText.ComboShiftKey, OverlayText.ComboShiftDefault);

        return "";
    }

    /// <summary>"All" / "Half" / the number itself, localized, for an effective amount or a resolved count.</summary>
    public static string AmountLabel(int amount)
    {
        if (amount == HalfSentinel) return OverlayText.Get(OverlayText.HalfKey, OverlayText.HalfDefault);
        if (amount >= AllSentinel)  return OverlayText.Get(OverlayText.AllKey, OverlayText.AllDefault);
        return amount.ToString();
    }

    /// <summary>The overlay's "Quick Transfer: X" line for an amount, localized.</summary>
    public static string AmountText(int amount)
        => OverlayText.Format(OverlayText.AmountKey, OverlayText.AmountDefault, AmountLabel(amount));

    /// <summary>Restores both presets and the custom amount to the values their config entries were bound with.</summary>
    private static void ResetPresetsToDefaults()
    {
        // Read the defaults off the entries rather than repeating literals - these can't drift from
        // the Config.Bind calls above.
        ShiftPresetAmount.Value = (int)ShiftPresetAmount.DefaultValue;
        CtrlPresetAmount.Value  = (int)CtrlPresetAmount.DefaultValue;
        TransferAmount.Value    = (int)TransferAmount.DefaultValue;
        CurrentTransferAmount   = TransferAmount.Value;

        ShowNotification(OverlayText.Format(OverlayText.PresetsResetKey, OverlayText.PresetsResetDefault,
            ShiftPresetAmount.Value, CtrlPresetAmount.Value, CurrentTransferAmount));
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
            if (CtrlHeld() || ShiftHeld()) return true;
        }
        return IsConfiguredModifierHeld();
    }

    /// <summary>Returns the effective transfer count based on the currently held modifier combo.</summary>
    public static int GetEffectiveTransferAmount()
    {
        if (FullStackMode.Value) return AllSentinel;

        if (!EnableModifierPresets.Value)
            return CurrentTransferAmount;

        bool ctrl  = CtrlHeld();
        bool shift = ShiftHeld();

        // Entire stack, or the Half sentinel the click prefix resolves against the clicked slot.
        if (ctrl && shift) return CtrlShiftMode.Value == CtrlShiftPresetMode.Half ? HalfSentinel : AllSentinel;
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
