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
    Half,
    /// <summary>The clicked stack minus one card (8 -> 7), so one stays where it was. A stack of one does not move.</summary>
    AllButOne
}

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency("crispywhips.CSFFModFramework", BepInDependency.DependencyFlags.SoftDependency)]
public class Plugin : BaseUnityPlugin
{
    private const string PluginGuid = "crispywhips.quick_transfer";
    public const string PluginName = "Quick_Transfer";
    public const string PluginVersion = "1.9.1";

    internal new static ManualLogSource Logger;
    private static Harmony _harmony;

    public static Plugin Instance { get; private set; }

    // Configuration — existing
    public static ConfigEntry<int> TransferAmount { get; private set; }
    public static ConfigEntry<KeyCode> ModifierKey { get; private set; }
    public static ConfigEntry<KeyCode> IncreaseKey { get; private set; }
    public static ConfigEntry<KeyCode> DecreaseKey { get; private set; }
    public static ConfigEntry<bool> YieldCountKeysToRepeatAction { get; private set; }

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

    // Configuration - hover-and-press transfer key (plan Feature 9)
    public static ConfigEntry<KeyCode> TransferHotkey { get; private set; }

    // Configuration - undo last batch (plan Feature 10)
    public static ConfigEntry<KeyCode> UndoKey { get; private set; }

    // Configuration - Ctrl+Shift half preset, sibling-slot draining
    public static ConfigEntry<CtrlShiftPresetMode> CtrlShiftMode { get; private set; }
    public static ConfigEntry<bool> DrainMatchingStacks { get; private set; }
    public static ConfigEntry<bool> WidenForNonPilingCards { get; private set; }

    // Configuration - tag-filtered transfer, spoilage-priority ordering
    public static ConfigEntry<KeyCode> TagFilterKey { get; private set; }
    public static ConfigEntry<string> TagFilterTags { get; private set; }
    public static ConfigEntry<bool> PrioritizeMostSpoiled { get; private set; }

    // Configuration - spoiled-only filter (plan Feature 8)
    public static ConfigEntry<KeyCode> SpoiledOnlyKey { get; private set; }
    public static ConfigEntry<int> SpoiledOnlyThreshold { get; private set; }

    // Configuration - per-container exclusion list and remembered amounts (plan Features 11 and 12)
    public static ConfigEntry<string> ExcludedContainers { get; private set; }
    public static ConfigEntry<KeyCode> ExcludeContainerKey { get; private set; }
    public static ConfigEntry<string> ContainerAmounts { get; private set; }
    public static ConfigEntry<KeyCode> RememberAmountKey { get; private set; }

    /// <summary>Value <see cref="GetEffectiveTransferAmount"/> returns for "the entire stack".</summary>
    public const int AllSentinel = 9999;

    /// <summary>
    /// Value <see cref="GetEffectiveTransferAmount"/> returns for the Ctrl+Shift combo in Half mode.
    /// Deliberately not a count: the overlay in <see cref="Update"/> has no slot in hand, so only the
    /// click prefix, which holds the clicked slot, can turn it into ceil(pile / 2). Never store it
    /// as a transfer count.
    /// </summary>
    public const int HalfSentinel = int.MinValue;

    /// <summary>
    /// Value <see cref="GetEffectiveTransferAmount"/> returns for the Ctrl+Shift combo in AllButOne mode
    /// (plan Feature 6). Like <see cref="HalfSentinel"/> it is not a count: only the click prefix, which
    /// holds the clicked slot, turns it into pile - 1. Never store it as a transfer count.
    /// </summary>
    public const int AllButOneSentinel = int.MinValue + 1;

    // Runtime state
    public static int CurrentTransferAmount { get; set; } = 5;

    // Visual indicator
    private static float _notificationEndTime = 0f;
    private static string _notificationText = "";
    // True while the overlay shows the live hint rather than a ShowNotification notice.
    private static bool _notificationIsHint = false;
    // The hint the held combo read when the current notice went up (see Update's hint block).
    private static string _hintAtNotice = null;
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
            "Key to increase transfer amount (hold Modifier + this key). When modifier presets are enabled, adjusts the active preset: Ctrl + this key the Ctrl preset, Shift or Alt + this key the Shift preset. With Repeat Action installed, Shift + Equals, Minus or their keypad keys belong to Repeat Action's repeat count (see Yield Count Keys To Repeat Action), so use Alt for the Shift preset.");

        DecreaseKey = Config.Bind(
            "Keybindings",
            "Decrease Amount Key",
            KeyCode.Minus,
            "Key to decrease transfer amount (hold Modifier + this key). When modifier presets are enabled, adjusts the active preset: Ctrl + this key the Ctrl preset, Shift or Alt + this key the Shift preset. With Repeat Action installed, Shift + Equals, Minus or their keypad keys belong to Repeat Action's repeat count (see Yield Count Keys To Repeat Action), so use Alt for the Shift preset.");

        YieldCountKeysToRepeatAction = Config.Bind(
            "Keybindings",
            "Yield Count Keys To Repeat Action",
            true,
            "When Repeat Action is installed and one of its modifier keys is held (its Repeat Modifier Key and Repeat Modifier Key (Alt), read from Repeat Action's own config: Left Shift and Right Shift by default), Quick Transfer ignores Equals, Keypad Plus, Minus and Keypad Minus, so the press changes only Repeat Action's repeat count. Ctrl+Shift is never yielded: there the keys only restate what Ctrl+Shift moves. Alt + Plus/Minus nudges the Shift preset instead. No effect without Repeat Action. Off: both mods react to the same Shift+Plus/Minus press, as in Quick Transfer 1.8.1 and earlier.");

        EnableModifierPresets = Config.Bind(
            "Modifier Presets",
            "Enable Modifier Presets",
            true,
            "When enabled: Shift+Right-Click transfers ShiftPresetAmount, Ctrl+Right-Click transfers CtrlPresetAmount, Ctrl+Shift+Right-Click transfers the entire stack. Adjust preset amounts in-game with Plus/Minus: Ctrl+Plus/Minus for the Ctrl preset, Alt+Plus/Minus (or Shift+Plus/Minus when Repeat Action is not installed) for the Shift preset.");

        ShiftPresetAmount = Config.Bind(
            "Modifier Presets",
            "Shift Preset Amount",
            5,
            new ConfigDescription(
                "Cards transferred per Shift+Right-Click (requires Enable Modifier Presets). Adjust in-game: hold Alt + Plus/Minus, or Shift + Plus/Minus when Repeat Action is not installed (with Repeat Action, Shift + Plus/Minus changes its repeat count instead; see Yield Count Keys To Repeat Action). When Modifier Key is set to an Alt key, Alt + Plus/Minus adjusts Transfer Amount instead.",
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
            "What Ctrl+Shift+click transfers (requires Enable Modifier Presets). All moves the entire stack. Half moves half of the clicked stack, rounded up (8 -> 4, 7 -> 4, 1 -> 1), filling the gap between the Ctrl preset and All. AllButOne moves the clicked stack minus one card (8 -> 7), so one stays where it was; a stack of one does not move at all. The overlay reads 'Half' or 'All but one' while the combo is held, since the number depends on which stack you click.");

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
            "When enabled, a bulk transfer that empties the clicked slot keeps going with the same item from the other slots of the same container (the same open inventory, or the same board area), up to the requested count. When disabled, only the clicked slot is drained (but see Widen For Non-Piling Cards).");

        WidenForNonPilingCards = Config.Bind(
            "Transfer Settings",
            "Widen For Non-Piling Cards",
            true,
            "When enabled, a bulk transfer on a card that cannot pile keeps going with the same item from the other slots of the same container, as Drain Matching Stacks does, even with that setting off. A card cannot pile when the slot it sits in keeps one card per slot (the Water-Driven Workshop's storage, a cooking container) or when the card itself never stacks (a tool, a basket). Without this, pulling ten such cards moved only the one you clicked. Cards that pile behave exactly as before. Decided when you click, so changing it does not affect a batch already running.");

        PrioritizeMostSpoiled = Config.Bind(
            "Transfer Settings",
            "Prioritize Most Spoiled",
            false,
            "When enabled, a bulk transfer that draws on more than one stack (Drain Matching Stacks, a tag-filtered transfer, or a card that cannot pile under Widen For Non-Piling Cards) takes the stack closest to spoiling first, even inside a container that slows or stops spoiling. Stacks that cannot spoil come after those, in the usual order. Only the order changes: which cards may move is the same.");

        TagFilterKey = Config.Bind(
            "Tag Filter",
            "Tag Filter Key",
            KeyCode.CapsLock,
            "Hold this key together with a modifier+click to widen the batch from the clicked item to every card in the same container that shares the clicked card's first listed tag (see Tag Filter Tags). Pressing Caps Lock also switches your keyboard's Caps Lock on or off as usual; pick another key here if that gets in the way. Set to None to disable.");

        TagFilterTags = Config.Bind(
            "Tag Filter",
            "Tag Filter Tags",
            "tag_HumanFood,tag_FoodRaw",
            "Comma-separated card tags a tag-filtered transfer may widen along, in priority order: the first one the clicked card carries decides the batch. A clicked card carrying none of them transfers only its own item, exactly as without the key. Keep the list narrow: a tag most cards carry sweeps unrelated items into the batch. Empty disables the tag filter.");

        SpoiledOnlyKey = Config.Bind(
            "Spoiled Only",
            "Spoiled Only Key",
            KeyCode.LeftAlt,
            "Hold this key together with a modifier+click and the batch moves only cards at least Spoiled Only Threshold percent of the way to spoiling, from every slot of that container holding the clicked item (or, with the Tag Filter Key, the clicked tag). If the card you click is not that far gone, it stays and the batch still moves the ones that are. Cards that cannot spoil never move. Ignored when it is the same key as Modifier Key. Set to None to disable.");

        SpoiledOnlyThreshold = Config.Bind(
            "Spoiled Only",
            "Spoiled Only Threshold",
            50,
            new ConfigDescription(
                "How far toward spoiling, in percent, a card must be for a Spoiled Only batch to move it (1 to 100). A starting value, not a balanced one: tune it to taste.",
                new AcceptableValueRange<int>(1, 100)));

        TransferHotkey = Config.Bind(
            "Keybindings",
            "Transfer Hotkey",
            KeyCode.X,
            "Hold a transfer modifier (Ctrl or Shift, or Modifier Key), rest the pointer on a card and press this key: the same batch as modifier+Transfer Mouse Button click, without clicking. Made for one-handed play, a trackpad, or a controller mapped to keyboard keys through Steam Input. Held keys such as Tag Filter Key and Spoiled Only Key apply as they do to a click. Does nothing while a text box has focus. Set to None to disable.");

        UndoKey = Config.Bind(
            "Keybindings",
            "Undo Key",
            KeyCode.Z,
            "Hold a transfer modifier (Ctrl or Shift, or Modifier Key) and press this key to move the last batch back: the same number of each item, the opposite way, while the container, board, character window or trade it went to is still the one open. One level: a second press, a new modifier+click, or closing or switching that container leaves nothing to undo (the overlay reads 'Nothing to undo'). It restores how many of each item, not the identical cards: two cards of one item can differ in spoilage or durability, and the undo takes whichever ones sit on the other side. Does nothing while a text box has focus. Set to None to disable.");

        ExcludedContainers = Config.Bind(
            "Containers",
            "Excluded Containers",
            "",
            "Comma-separated UniqueIDs of containers where bulk transfer is off: a modifier+click with one of them open moves just the one card, the game's own way. Normally filled by Exclude Container Key rather than by hand. Empty by default.");

        ExcludeContainerKey = Config.Bind(
            "Containers",
            "Exclude Container Key",
            KeyCode.None,
            "Hold a transfer modifier (Ctrl or Shift, or Modifier Key), open a container and press this key to switch bulk transfer off for it, or back on if it is already off. The overlay names the container. Off by default (None); a good choice is a key you do not otherwise use, such as F7. Does nothing while a text box has focus or with no container open.");

        ContainerAmounts = Config.Bind(
            "Containers",
            "Container Amounts",
            "",
            "Amounts remembered per container, written as uniqueID/Shift=5;uniqueID/Ctrl=all (a count or the word all). Normally filled by Remember Amount Key rather than by hand. A remembered amount replaces that modifier's preset for that container only; Ctrl+Shift and Full Stack Mode are never replaced. Empty by default.");

        RememberAmountKey = Config.Bind(
            "Containers",
            "Remember Amount Key",
            KeyCode.None,
            "Hold Shift or Ctrl (not both), open a container and press this key to make that container always use the modifier's current preset amount; press it again with the same amount to forget it. Off by default (None). Presets still nudge as usual (Plus/Minus) and keep applying everywhere else. Does nothing while a text box has focus, with no container open, or with presets off.");

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
        // Every plugin is loaded by the first frame, which Awake cannot promise (RepeatActionBridge remarks).
        RepeatActionBridge.EnsureResolved();
        UtilityBeltLockBridge.EnsureResolved();

        bool modHeld = IsModifierKeyHeld();
        bool altNudge = AltNudgeHeld();

        // Repeat Action reads Plus/Minus under its own modifier on this same frame. A key it owns right
        // now is dropped here, per key, before anything below reads it, so one press changes one number.
        var increaseKey = IncreaseKey.Value;
        var decreaseKey = DecreaseKey.Value;
        bool increaseOwned = RepeatActionBridge.OwnsKey(increaseKey);
        bool decreaseOwned = RepeatActionBridge.OwnsKey(decreaseKey);
        bool increaseHeld    = !increaseOwned && Input.GetKey(increaseKey);
        bool decreaseHeld    = !decreaseOwned && Input.GetKey(decreaseKey);
        bool increasePressed = !increaseOwned && Input.GetKeyDown(increaseKey);
        bool decreasePressed = !decreaseOwned && Input.GetKeyDown(decreaseKey);

        if (modHeld && ResetPresetsKey.Value != KeyCode.None && Input.GetKeyDown(ResetPresetsKey.Value))
        {
            ResetPresetsToDefaults();
            return;
        }

        // Hover-and-press (plan Feature 9): the modifier+click without the click. A modifier is required, so a
        // bare X never moves a card, and a focused text box keeps its own keystrokes.
        if (modHeld && TransferHotkey.Value != KeyCode.None && Input.GetKeyDown(TransferHotkey.Value) && !TextInputFocused())
            Quick_Transfer.Patcher.QuickTransferPatch.RequestHoverTransfer();

        // Undo last batch (plan Feature 10). The record is dropped as soon as its destination changes, so a closed
        // and reopened chest leaves nothing to undo.
        Quick_Transfer.Patcher.QuickTransferPatch.WatchUndoContext();
        if (modHeld && UndoKey.Value != KeyCode.None && Input.GetKeyDown(UndoKey.Value) && !TextInputFocused())
            Quick_Transfer.Patcher.QuickTransferPatch.RequestUndo();

        // Per-container settings (plan Features 11 and 12): both read the open container, so each needs a modifier held.
        if (modHeld && ExcludeContainerKey.Value != KeyCode.None && Input.GetKeyDown(ExcludeContainerKey.Value) && !TextInputFocused())
            Quick_Transfer.Patcher.QuickTransferPatch.RequestToggleExcluded();
        if (modHeld && RememberAmountKey.Value != KeyCode.None && Input.GetKeyDown(RememberAmountKey.Value) && !TextInputFocused())
            Quick_Transfer.Patcher.QuickTransferPatch.RequestToggleRemembered();

        if (!FullStackMode.Value && (modHeld || altNudge) && (increaseHeld || decreaseHeld))
        {
            bool shouldTrigger = false;

            if (increasePressed || decreasePressed)
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

                if (altNudge)
                {
                    // Alt + Plus/Minus nudges the Shift preset: the chord that stays Quick Transfer's
                    // own with Repeat Action installed, since Repeat Action reads bare Shift.
                    ShiftPresetAmount.Value = Mathf.Clamp(ShiftPresetAmount.Value + delta, 1, 9999);
                    ShowNotification(AmountText(ShiftPresetAmount.Value) + " " + OverlayText.Get(OverlayText.ComboShiftKey, OverlayText.ComboShiftDefault));
                }
                else if (EnableModifierPresets.Value)
                {
                    bool ctrl = CtrlHeld();
                    bool shift = ShiftHeld();

                    if (ctrl && shift)
                    {
                        // Ctrl+Shift is All, Half or All but one by config, never a number the keys
                        // can nudge - just restate what it will do.
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
            string hint = CurrentHint();
            // A 2s notice (the post-click count, a preset change, "Presets reset") stays up while the
            // player still holds the combo that produced it; a different combo brings the hint straight
            // back. Before 1.9.0 any notice that differed from the hint was overwritten on the next
            // frame the modifier was held, so the reset confirmation and the Half preset's post-click
            // count were never visible in real play (found by the T2.213 scenario, 2026-09-26).
            bool noticeUp = !_notificationIsHint && Time.time < _notificationEndTime && hint == _hintAtNotice;
            // Otherwise refresh only if the text changed or the timer nearly expired.
            if (!noticeUp && (hint != _notificationText || _notificationEndTime - Time.time < 0.1f))
            {
                _notificationText = hint;
                _notificationEndTime = Time.time + 0.15f;
                _notificationIsHint = true;
            }
        }
    }

    /// <summary>
    /// True while a text input (a rename or description box) is the selected UI object, so a hotkey press is
    /// typing and not a transfer. A string test on the component type names, so the csproj gains no reference
    /// to TMP_InputField's assembly. Unreadable state counts as "not typing" and warns once.
    /// </summary>
    private static bool TextInputFocused()
    {
        try
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            var selected = es != null ? es.currentSelectedGameObject : null;
            if (selected == null) return false;
            foreach (var component in selected.GetComponents<Component>())
            {
                if (component != null && component.GetType().Name.Contains("InputField")) return true;
            }
            return false;
        }
        catch (System.Exception ex)
        {
            Quick_Transfer.Patcher.QuickTransferPatch.WarnFeatureOnce("Transfer Hotkey", "the selected UI object (" + ex.GetType().Name + ")");
            return false;
        }
    }

    internal static bool CtrlHeld()  => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
    internal static bool ShiftHeld() => Input.GetKey(KeyCode.LeftShift)   || Input.GetKey(KeyCode.RightShift);
    private static bool AltHeld()    => Input.GetKey(KeyCode.LeftAlt)     || Input.GetKey(KeyCode.RightAlt);

    /// <summary>
    /// True when Alt + Plus/Minus should nudge the Shift preset: presets on, Full Stack Mode off, Alt
    /// held, and Ctrl, Shift and the configured modifier all up. The chord never contains Shift,
    /// because standalone Repeat Action reads bare Shift. Excluding the configured modifier keeps a
    /// Modifier Key of LeftAlt on its old meaning (Alt + Plus/Minus adjusts Transfer Amount).
    /// Never a transfer modifier: <see cref="IsModifierKeyHeld"/> does not read it.
    /// </summary>
    private static bool AltNudgeHeld()
    {
        if (!EnableModifierPresets.Value || FullStackMode.Value) return false;
        return AltHeld() && !CtrlHeld() && !ShiftHeld() && !IsConfiguredModifierHeld();
    }

    /// <summary>True while the configured Tag Filter Key is held (never when it is None).</summary>
    public static bool TagFilterHeld() => TagFilterKey.Value != KeyCode.None && Input.GetKey(TagFilterKey.Value);

    /// <summary>
    /// True while the configured Spoiled Only Key is held (never when it is None, and never when it is the same
    /// key as Modifier Key, which would turn every modifier+click into a spoiled-only batch). Alt + Plus/Minus
    /// (<see cref="AltNudgeHeld"/>) needs Ctrl and Shift up and no click, and this needs a modifier+click, so
    /// one Alt press never does both.
    /// </summary>
    public static bool SpoiledOnlyHeld()
    {
        var key = SpoiledOnlyKey.Value;
        return key != KeyCode.None && key != ModifierKey.Value && Input.GetKey(key);
    }

    /// <summary>The configured Tag Filter Tags, trimmed, blanks dropped, in the order written.</summary>
    public static List<string> TagFilterTagList()
    {
        var list = new List<string>();
        var raw = TagFilterTags.Value;
        if (string.IsNullOrEmpty(raw)) return list;
        foreach (var part in raw.Split(','))
        {
            var tag = part.Trim();
            if (tag.Length > 0) list.Add(tag);
        }
        return list;
    }

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

    /// <summary>"All" / "Half" / "All but one" / the number itself, localized, for an effective amount or a resolved count.</summary>
    public static string AmountLabel(int amount)
    {
        if (amount == HalfSentinel) return OverlayText.Get(OverlayText.HalfKey, OverlayText.HalfDefault);
        if (amount == AllButOneSentinel) return OverlayText.Get(OverlayText.AllButOneKey, OverlayText.AllButOneDefault);
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

        // Entire stack, or the Half / All-but-one sentinel the click prefix resolves against the clicked slot.
        if (ctrl && shift)
        {
            switch (CtrlShiftMode.Value)
            {
                case CtrlShiftPresetMode.Half:      return HalfSentinel;
                case CtrlShiftPresetMode.AllButOne: return AllButOneSentinel;
                default:                            return AllSentinel;
            }
        }
        if (ctrl)  return CtrlPresetAmount.Value;
        if (shift) return ShiftPresetAmount.Value;

        // Non-preset modifier (e.g. Alt when configured) → custom amount
        return CurrentTransferAmount;
    }

    public static void ShowNotification(string text)
    {
        _notificationText = text;
        _notificationEndTime = Time.time + NotificationDuration;
        _notificationIsHint = false;
        _hintAtNotice = CurrentHint();
    }

    /// <summary>The live hint for the modifiers held right now: the effective amount plus the combo suffix.</summary>
    private static string CurrentHint() => AmountText(GetEffectiveTransferAmount()) + ActiveComboSuffix();

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
