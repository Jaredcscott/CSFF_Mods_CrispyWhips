using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Repeat_Action;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public class Plugin : BaseUnityPlugin
{
    private const string PluginGuid = "crispywhips.repeat_action";
    public const string PluginName = "Repeat_Action";
    public const string PluginVersion = "2.2.2";

    internal new static ManualLogSource Logger;
    private static Harmony _harmony;
    
    // Static instance for coroutine access from patches
    public static Plugin Instance { get; private set; }

    // Configuration
    public static ConfigEntry<KeyCode> RepeatKey { get; private set; }
    public static ConfigEntry<KeyCode> RepeatModifierKey { get; private set; }
    public static ConfigEntry<KeyCode> RepeatKey2 { get; private set; }
    public static ConfigEntry<KeyCode> RepeatModifierKey2 { get; private set; }
    public static ConfigEntry<KeyCode> PinActionKey { get; private set; }
    public static ConfigEntry<KeyCode> FirePinnedActionKey { get; private set; }
    public static ConfigEntry<KeyCode> PauseKey { get; private set; }
    public static ConfigEntry<KeyCode> NextPresetKey { get; private set; }
    public static ConfigEntry<KeyCode> RecordSequenceKey { get; private set; }
    public static ConfigEntry<KeyCode> PlaySequenceKey { get; private set; }
    public static ConfigEntry<KeyCode> NextSequenceKey { get; private set; }
    public static ConfigEntry<string> SavedSequences { get; private set; }
    public static ConfigEntry<KeyCode> CountUpKey { get; private set; }
    public static ConfigEntry<KeyCode> CountUpKeyAlt { get; private set; }
    public static ConfigEntry<KeyCode> CountDownKey { get; private set; }
    public static ConfigEntry<KeyCode> CountDownKeyAlt { get; private set; }
    public static ConfigEntry<string> RepeatPresets { get; private set; }
    public static ConfigEntry<int> DefaultRepeatCount { get; private set; }
    public static ConfigEntry<int> MaxRepeatCount { get; private set; }
    public static ConfigEntry<bool> RememberLastCount { get; private set; }
    public static ConfigEntry<int> LastCount { get; private set; }
    public static ConfigEntry<int> MaxUnboundedIterations { get; private set; }
    public static ConfigEntry<bool> PerCardGroupRepeat { get; private set; }
    public static ConfigEntry<float> IterationDelaySeconds { get; private set; }
    public static ConfigEntry<bool> LockToOriginalTarget { get; private set; }
    public static ConfigEntry<bool> VerboseRunDiagnostics { get; private set; }
    public static ConfigEntry<bool> ShowNotifications { get; private set; }
    public static ConfigEntry<bool> ShowCountIndicator { get; private set; }
    public static ConfigEntry<bool> ShowProgressBar { get; private set; }
    public static ConfigEntry<bool> StopOnLowStats { get; private set; }
    public static ConfigEntry<bool> StopOnToolBreak { get; private set; }
    public static ConfigEntry<int> StopBelowToolDurability { get; private set; }
    public static ConfigEntry<bool> UseSpareToolOnBreak { get; private set; }
    public static ConfigEntry<bool> StopOnInventoryFull { get; private set; }
    public static ConfigEntry<bool> StopAtNightfall { get; private set; }
    public static ConfigEntry<bool> StopAtSeasonChange { get; private set; }
    public static ConfigEntry<bool> StopAfterEncounter { get; private set; }
    public static ConfigEntry<float> StopAfterInGameHours { get; private set; }
    public static ConfigEntry<int> StaminaStopThreshold { get; private set; }
    public static ConfigEntry<int> SatiationStopThreshold { get; private set; }
    public static ConfigEntry<int> HydrationStopThreshold { get; private set; }
    public static ConfigEntry<float> ActionCompletionTimeout { get; private set; }
    public static ConfigEntry<string> ExtraBlockedActions { get; private set; }
    public static ConfigEntry<string> ExtraRestLikeActions { get; private set; }
    public static ConfigEntry<string> ExtraStatThresholds { get; private set; }
    public static ConfigEntry<string> StopAtQuantity { get; private set; }
    public static ConfigEntry<bool> RefuelFromPinnedAction { get; private set; }
    public static ConfigEntry<int> MaxRefuelsPerRun { get; private set; }
    public static ConfigEntry<bool> PlayCompletionSound { get; private set; }

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

    // Set after the first failed read of GraphicsManager.PlayerIsTyping (see PlayerIsTyping), so the
    // warning is logged once and not once a frame.
    private static bool _typingProbeFailedOnce;

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

        // PUBLISHED CONTRACT with Quick Transfer. Quick Transfer reads three things from this mod, so
        // renaming, retyping or moving any of them breaks its yield to this mod's Shift keys silently:
        //   1. The config entries "Repeat Modifier Key" and "Repeat Modifier Key (Alt)" below: section
        //      "Keybindings", these exact names, type KeyCode.
        //   2. The plugin GUID crispywhips.repeat_action (PluginGuid, above).
        //   3. The four count-key entries bound below: section "Keybindings", names "Count Up Key",
        //      "Count Up Key (Alt)", "Count Down Key" and "Count Down Key (Alt)", type KeyCode, defaults
        //      Equals, KeypadPlus, Minus and KeypadMinus. Quick Transfer reads them live (None owns nothing).
        // Quick Transfer's drift gate, Development_Tools/Tests/QuickTransfer-RepeatActionYield.Tests.ps1,
        // compares these Config.Bind calls (section, names, type, defaults) with Quick Transfer's copies, so
        // change none of them without pairing the change with Quick Transfer. On this side that comment is the
        // only guard: no test here can see a rename.
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

        // Published contract entry (see the comment above RepeatModifierKey): do not rename or retype.
        RepeatModifierKey2 = Config.Bind(
            "Keybindings",
            "Repeat Modifier Key (Alt)",
            KeyCode.RightShift,
            "Alternate modifier key (for right-hand usage)");

        PinActionKey = Config.Bind(
            "Keybindings",
            "Pin Action Key",
            KeyCode.P,
            "Held with either modifier key: pin the action Repeat would replay right now, so you can fire it again later with the Fire Pinned Action Key, even after doing other things or traveling. One pin at a time; it lasts until you pin another action, it is found to be gone when fired, or you quit the game.");

        FirePinnedActionKey = Config.Bind(
            "Keybindings",
            "Fire Pinned Action Key",
            KeyCode.F,
            "Held with either modifier key: repeat the pinned action with the current repeat count. If its card or action is gone, the run says so and the pin is cleared.");

        PauseKey = Config.Bind(
            "Keybindings",
            "Pause Key",
            KeyCode.G,
            "Held with either modifier key: pause the running repeat after its current iteration, and resume it with the same key. While paused you can eat, rest or craft by hand; the run carries on with the count and action it had. Repeat Key still cancels a paused run.");

        NextPresetKey = Config.Bind(
            "Keybindings",
            "Next Preset Key",
            KeyCode.N,
            "Held with either modifier key: load the next preset from Repeat Presets (wrapping to the first after the last). Does nothing while a run is active. A preset writes its values over whatever the settings hold now.");

        RecordSequenceKey = Config.Bind(
            "Keybindings",
            "Record Sequence Key",
            KeyCode.K,
            "Held with either modifier key: start recording a sequence; every action you then do that Repeat would capture is added in order. Press it again to stop: the sequence is kept under its first action's name plus a number (\"Plant 1\") and chosen for the Play Sequence Key. Recording and a repeat never run at the same time.");

        PlaySequenceKey = Config.Bind(
            "Keybindings",
            "Play Sequence Key",
            KeyCode.M,
            "Held with either modifier key: play the chosen sequence, all its steps in order, repeated by the current repeat count. Every step passes the same safety checks a repeat does, and a step that cannot run stops the play and names it (a step is never skipped). The Repeat Key cancels a playing sequence.");

        NextSequenceKey = Config.Bind(
            "Keybindings",
            "Next Sequence Key",
            KeyCode.U,
            "Held with either modifier key: choose the next recorded sequence for the Play Sequence Key (wrapping to the first after the last).");

        // Published contract entries (see the comment above RepeatModifierKey): do not rename or retype.
        CountUpKey = Config.Bind(
            "Keybindings",
            "Count Up Key",
            KeyCode.Equals,
            "Held with either modifier key: raise the repeat count by one (hold to repeat). None disables this key.");

        CountUpKeyAlt = Config.Bind(
            "Keybindings",
            "Count Up Key (Alt)",
            KeyCode.KeypadPlus,
            "Alternate key to raise the repeat count. None disables this key.");

        CountDownKey = Config.Bind(
            "Keybindings",
            "Count Down Key",
            KeyCode.Minus,
            "Held with either modifier key: lower the repeat count by one (below 1 is Unlimited; hold to repeat). None disables this key.");

        CountDownKeyAlt = Config.Bind(
            "Keybindings",
            "Count Down Key (Alt)",
            KeyCode.KeypadMinus,
            "Alternate key to lower the repeat count. None disables this key.");

        DefaultRepeatCount = Config.Bind(
            "Repeat Settings",
            "Default Repeat Count",
            5,
            new ConfigDescription(
                "Default number of times to repeat an action (0 = Unlimited). If it is higher than Maximum Repeat Count, the count starts at the maximum instead.",
                new AcceptableValueRange<int>(0, 10000)));

        MaxRepeatCount = Config.Bind(
            "Repeat Settings",
            "Maximum Repeat Count",
            50,
            new ConfigDescription(
                "Maximum number of times an action can be repeated",
                new AcceptableValueRange<int>(1, 10000)));

        RememberLastCount = Config.Bind(
            "Repeat Settings",
            "Remember Last Count",
            false,
            "When on, the repeat count you set with Shift+Plus/Minus or the mouse wheel is still there the next time you start the game, instead of going back to Default Repeat Count. Turn it off, or change Default Repeat Count and delete the Last Count line below, to go back to the default.");

        LastCount = Config.Bind(
            "Repeat Settings",
            "Last Count",
            -1,
            new ConfigDescription(
                "Managed by the mod: the last repeat count, written about a second after you stop changing it while Remember Last Count is on. -1 means none saved yet; 0 is Unlimited.",
                new AcceptableValueRange<int>(-1, 10000)));

        SavedSequences = Config.Bind(
            "Repeat Settings",
            "Saved Sequences",
            "",
            "Managed by the mod: the sequences recorded with the Record Sequence Key that survive quitting the game (the 10 most recent). Leave it alone, or empty it to forget them all. Sequences with a step that cannot be saved (an action with no card, such as Rest; a liquid pour; an action the card does not list among its own buttons) last until you quit and are never written here.");

        RepeatPresets = Config.Bind(
            "Repeat Settings",
            "Repeat Presets",
            "",
            "Named sets of settings that Next Preset Key loads one after another. Format: name: key=value, key=value; name: key=value. For example \"Chop: count=20, Stop At Nightfall=true; Snack: count=3, Lock To Original Target=true\". 'count' sets the repeat count (0 is Unlimited, capped by Maximum Repeat Count). Any other key is the name of a true/false, whole-number or decimal setting of this mod outside Keybindings (the names in this file); its value is checked against the setting's own limits. Settings that hold lists (Extra Stat Thresholds, Extra Blocked Actions, Extra Rest-Like Actions, Stop At Quantity) cannot be set by a preset. A preset writes over values you set by hand, and a pair that cannot be applied is skipped (breadcrumb in the log) while the rest of the preset still applies. Empty = no presets.");

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

        IterationDelaySeconds = Config.Bind(
            "Repeat Settings",
            "Iteration Delay (seconds)",
            0f,
            new ConfigDescription(
                "Real seconds to pause between the iterations of a run (never after the last one), so you can watch a batch or slow it down. 0 = no pause. The pause does not count toward Action Completion Timeout, and the Repeat key still cancels during it.",
                new AcceptableValueRange<float>(0f, 30f)));

        LockToOriginalTarget = Config.Bind(
            "Repeat Settings",
            "Lock To Original Target",
            false,
            "Keep a run on the exact card you clicked. Off (default): when that card is used up or gone, the run moves on to another card of the same kind, or finds the same action on another card (such as North on the next location). On: the run stops with 'locked target is gone' instead. It locks the card being acted on, never the item you drag onto it, so a drag-drop still takes its next log or seed from your stack. Travel repeats stop after one move while this is on.");

        ExtraBlockedActions = Config.Bind(
            "Repeat Settings",
            "Extra Blocked Actions",
            "",
            "Comma-separated action names that must never be repeated, merged with the built-in blocklist (\"continue\"). Whole-word, case-insensitive - e.g. \"sleep, butcher\".");

        ExtraRestLikeActions = Config.Bind(
            "Repeat Settings",
            "Extra Rest-Like Actions",
            "",
            "Comma-separated action names treated like Rest and Relax (the built-ins): doing one of them does NOT replace the action Shift+R repeats, so a quick Eat or Drink between chops leaves Shift+R chopping. One is still recorded when there is nothing else to repeat, or when the last action was itself rest-like. Matches are whole words of the action's English name, case-insensitive, in every game language - e.g. \"eat, drink\". Careful: a word that also matches your main action makes it rest-like too, so it no longer replaces the action Shift+R repeats. An action that is also in Extra Blocked Actions is blocked (never recorded). Empty = only Rest and Relax.");

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
            "Stop repeating when a drag-drop tool breaks: its usage bar runs out and the game turns it into another card or removes it. A dragged card the action itself uses up (food, a material) never counts as a broken tool.");

        StopBelowToolDurability = Config.Bind(
            "Safety",
            "Stop Below Tool Durability (%)",
            0,
            new BepInEx.Configuration.ConfigDescription(
                "Stop a drag-drop run before an iteration whose dragged card is below this share of its full durability (0 = disabled). It reads the dragged card's usage bar (the wear bar on a tool) as a percentage of that card's full durability, so 20 stops before using a tool with less than a fifth of its durability left. Only the card you drag is checked, never the card you drop it on, and a dragged card with no usage bar never stops the run. A card whose usage bar counts something other than wear (charges, uses left) stops the run the same way.",
                new BepInEx.Configuration.AcceptableValueRange<int>(0, 99)));

        UseSpareToolOnBreak = Config.Bind(
            "Safety",
            "Use Spare Tool On Break",
            false,
            "When Stop On Tool Break would stop a drag-drop run because the dragged tool broke, carry on with another tool of the same kind if you have one in your current location (carried, in a container you have here, or on the board) instead of stopping. With no spare the run stops as before. Has no effect while Stop On Tool Break is off, since a run then carries on with whatever card of that kind it finds anyway. It never equips anything: it only changes which card the next drag-drop uses.");

        StopOnInventoryFull = Config.Bind(
            "Safety",
            "Stop On Inventory Full",
            false,
            "Stop repeating once every container you are carrying is full. Off by default: many actions drop their output on the ground rather than into your inventory, so this would stop those early. Ignored when you carry no container.");

        StopAtNightfall = Config.Bind(
            "Safety",
            "Stop At Nightfall",
            false,
            "Stop a run when night falls: the game's dusk, 17:00 in most seasons and 19:00 in summer. Only a run that has been in daylight stops, so one you start at night runs on, and stops at the next dusk if it lasts that long.");

        StopAtSeasonChange = Config.Bind(
            "Safety",
            "Stop At Season Change",
            false,
            "Stop a run when the season changes during it.");

        StopAfterEncounter = Config.Bind(
            "Safety",
            "Stop After Encounter",
            false,
            "Stop a run before its next iteration once an encounter (an animal or a visitor confronting you) has interrupted it, instead of carrying on after you deal with it. The encounter itself is never skipped or answered for you: the run waits until it is over, then stops with \"an encounter interrupted the run\". Off, a run carries on as before once the encounter ends.");

        StopAfterInGameHours = Config.Bind(
            "Safety",
            "Stop After In-Game Hours",
            0f,
            new ConfigDescription(
                "Stop a run once it has used this much in-game time (0 = disabled, up to 240). Combine it with Unlimited to repeat for a stretch of game time, e.g. 3 to chop for three in-game hours. Counted in whole game ticks since the run began (one in-game hour is four ticks of 15 minutes), never real time. The run stops before an iteration that would pass the limit, judged by the longest iteration so far, so a run whose iterations vary in length can stop one iteration early. The first iteration always runs.",
                new AcceptableValueRange<float>(0f, 240f)));

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
            "Stop limits for ANY stat, vanilla or modded, checked alongside the three above. Comma-separated entries, where guid is the stat's UniqueID and the percent is always a share of the stat's CURRENT MAXIMUM. 'guid:percent' (1-100) is a floor: the run stops when the stat falls below that share. 'guid:>percent' (1-99) is a ceiling for a stat where high is the danger: the run stops when the stat rises above that share. E.g. \"888d2d2a99e3f044291c6748a0fa8d78:30, 3b79a4c6d7e151044a1c56fbbd401d78:>60\" stops when Body Temperature falls under 30% of its maximum or Stress rises over 60% of its maximum. The stop message uses the stat's own name (\"Body Temperature below 30%\", \"Stress above 60%\"). A malformed entry is skipped (breadcrumb in the log) without affecting the others. Empty = no extra stops. Vanilla stat UniqueIDs: see the README.");

        StopAtQuantity = Config.Bind(
            "Safety",
            "Stop At Quantity",
            "",
            "Stop a run once your location holds at least this many of a card, so you can repeat a craft until you have enough. Comma-separated 'uniqueid:count' entries (count 1 or more), where uniqueid is the card's UniqueID; any one entry reaching its count stops the run. Counted: every card of that UniqueID on the board here and in what you carry, each card of a pile once. Checked before every iteration and as soon as one finishes, so a run that already has enough does not start. E.g. \"a1620f9713805d941b515c2b60a04169:10\" stops at ten Rye Bread Slices with \"Rye Bread Slice reached 10\". A malformed entry, or a UniqueID no card has, is skipped (breadcrumb in the log) without affecting the others. Empty = no quantity stops. Vanilla UniqueIDs: see the README.");

        // Both defaults are the planner's inference (plan Feature 17, Open questions) and reversible.
        RefuelFromPinnedAction = Config.Bind(
            "Safety",
            "Refuel From Pinned Action",
            false,
            "When a stat floor stops a run (Satiation, Hydration or Stamina Stop Threshold, or an Extra Stat Thresholds floor), fire your pinned action once, such as Eat on the food you pinned with the Pin Action Key, and carry on if that brought the stat back over its floor. The pin is used exactly as the Fire Pinned Action Key would use it: nothing else is picked for you, and if no card of the pinned kind is left the run stops with \"(pinned action unavailable)\". If the stat is still under its floor afterwards, the run stops with \"(refuel did not help)\". Never answers a ceiling ('guid:>percent'), and never fires while the run is the pinned action itself. The refuel waits until every other stop has been checked, fires the pinned action's whole action (a pinned stack or group action may use up the whole stack or group), and is not held by Lock To Original Target. A rest cannot be pinned, so this does not recover Stamina.");

        MaxRefuelsPerRun = Config.Bind(
            "Safety",
            "Maximum Refuels Per Run",
            3,
            new ConfigDescription(
                "How many times one run may fire the pinned action under Refuel From Pinned Action (1 to 20). Once a run has used them all, the next stat floor stops it with \"(refuel limit reached)\".",
                new AcceptableValueRange<int>(1, 20)));

        ActionCompletionTimeout = Config.Bind(
            "Timeout Settings",
            "Action Completion Timeout (seconds)",
            30f,
            new ConfigDescription(
                "Maximum time to wait for the game to be ready before aborting the repeat sequence",
                new AcceptableValueRange<float>(1f, 300f)));

        PlayCompletionSound = Config.Bind(
            "Sound",
            "Play Completion Sound",
            false,
            "Play a short game sound when a run that did at least one iteration ends on its own (finished, stopped, or the Unlimited backstop), so you hear that a long batch is done without watching the screen. Not played when you cancel the run yourself.");

        // A default above the maximum is a cross-entry case no per-entry range can catch.
        int startCount = RememberLastCount.Value && LastCount.Value >= UnlimitedCount ? LastCount.Value : DefaultRepeatCount.Value;
        CurrentRepeatCount = Mathf.Clamp(startCount, UnlimitedCount, MaxRepeatCount.Value);

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

        // A new game starting (a load) ends a run in progress. Subscribed here, once per process, and
        // unsubscribed in OnDestroy: the game's run-start events re-fire on every run, so the subscription
        // must never be made from anything that runs per run. See ActionPatch.OnRunStart.
        Repeat_Action.Patcher.ActionPatch.SubscribeRunStart();
        Repeat_Action.Patcher.ActionPatch.LoadSavedSequences();
    }

    /// <summary>
    /// True while the player has a text field focused: a card rename, a recipe name, Edit Description,
    /// or the blueprint or guide search box. Typing a capital R, P or F holds Shift, which is exactly a
    /// Repeat chord, so Update stands down entirely while this is true, as vanilla's own hotkeys do
    /// (<c>GraphicsManager.Hotkeys</c> returns early on the same flag). Every named field sets it (Wave 0
    /// finding W0.1 in Documentation/Design/RepeatAction_Content_Update_Round_2_As_Built.md). One refinement over vanilla: a box
    /// that lost focus to Enter no longer counts (see <see cref="ReadPlayerIsTyping"/>).
    /// </summary>
    /// <remarks>
    /// A game update could rename the member. A missing member surfaces when <see cref="ReadPlayerIsTyping"/>
    /// is JIT-compiled, i.e. inside this method's try, which is why the one typed game call sits in its own
    /// non-inlined method (the shape Quick Transfer's OverlayText uses). The probe then reports false and says
    /// so once, instead of throwing every frame.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool PlayerIsTyping()
    {
        // After one failed read (a renamed or removed member is permanent) stop paying for a caught
        // exception every frame; the one warning below was already logged.
        if (_typingProbeFailedOnce) return false;
        try
        {
            return ReadPlayerIsTyping();
        }
        catch (System.Exception ex)
        {
            if (!_typingProbeFailedOnce)
            {
                _typingProbeFailedOnce = true;
                Logger?.LogWarning($"[Typing] Could not read GraphicsManager.PlayerIsTyping ({ex.GetType().Name}: {ex.Message}); the Repeat hotkeys will not pause while a text field is focused.");
            }
            return false;
        }
    }

    // GraphicsManager.CurrentTypingInput (a TMP_InputField; read by reflection so this mod needs no TextMeshPro
    // reference) and its isFocused property.
    private static System.Reflection.FieldInfo _typingInputField;
    private static System.Reflection.PropertyInfo _typingIsFocused;

    /// <summary>
    /// The game's flag is "a text box was selected and not yet deselected". Enter in a single-line box ends
    /// editing (TMP's DeactivateInputField) without deselecting it, so the flag stays true until the player
    /// clicks elsewhere, and the Repeat hotkeys would stay dead after a rename confirmed with Enter (walkthrough
    /// T2.701: count stuck at 3, PlayerIsTyping still true). So a selected box counts as typing only while it
    /// still has focus; when focus cannot be read, the game's own answer stands.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool ReadPlayerIsTyping()
    {
        if (!GraphicsManager.PlayerIsTyping) return false;
        if (_typingInputField == null)
            _typingInputField = typeof(GraphicsManager).GetField("CurrentTypingInput",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        object input = _typingInputField?.GetValue(null);
        if (input == null) return true;
        if (_typingIsFocused == null || !_typingIsFocused.DeclaringType.IsInstanceOfType(input))
            _typingIsFocused = input.GetType().GetProperty("isFocused");
        if (_typingIsFocused == null) return true;
        return _typingIsFocused.GetValue(input, null) is bool focused ? focused : true;
    }

    private void Update()
    {
        UtilityBeltLockBridge.EnsureResolved();
        // Typing in a text field: nothing below may act. The hold timers are cleared so a Plus or Minus
        // held when typing began does not carry its repeat delay into the next press.
        if (PlayerIsTyping()) { _adjustHoldTime = 0f; _adjustNextFireTime = 0f; return; }

        FlushRememberedCount(false);

        bool modifier1Held = Input.GetKey(RepeatModifierKey.Value);
        bool modifier2Held = Input.GetKey(RepeatModifierKey2.Value);
        bool modifierHeld = modifier1Held || modifier2Held;
        
        if (modifierHeld)
        {
            // Adjust repeat count with +/- (hold to repeat) — either shift key works
            // Quick Transfer reads the four count-key entries (published contract, see Awake).
            bool plusHeld = Input.GetKey(CountUpKey.Value) || Input.GetKey(CountUpKeyAlt.Value);
            bool minusHeld = Input.GetKey(CountDownKey.Value) || Input.GetKey(CountDownKeyAlt.Value);
            bool plusDown = Input.GetKeyDown(CountUpKey.Value) || Input.GetKeyDown(CountUpKeyAlt.Value);
            bool minusDown = Input.GetKeyDown(CountDownKey.Value) || Input.GetKeyDown(CountDownKeyAlt.Value);

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

            // Pin / fire the pinned action - either modifier works, like the count keys.
            if (Input.GetKeyDown(PinActionKey.Value))
                Patcher.ActionPatch.PinCurrentAction();
            if (Input.GetKeyDown(FirePinnedActionKey.Value))
                TriggerPinnedAction();
            if (Input.GetKeyDown(PauseKey.Value))
                Patcher.ActionPatch.TogglePause();
            if (Input.GetKeyDown(NextPresetKey.Value))
                Presets.LoadNext();
            if (Input.GetKeyDown(RecordSequenceKey.Value))
                Patcher.ActionPatch.ToggleRecording();
            if (Input.GetKeyDown(PlaySequenceKey.Value))
                TriggerSequence();
            if (Input.GetKeyDown(NextSequenceKey.Value))
                Patcher.ActionPatch.SelectNextSequence();
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
        SetRepeatCount(next);
        ShowNotification(Messages.Text("Repeat Count: {0}", IsUnlimited ? Messages.Text("Unlimited") : CountLabel));
    }

    /// <summary>
    /// Set the repeat count (clamped to 0 through Maximum Repeat Count, as Awake clamps) and queue the Remember
    /// Last Count write. Returns the count that was set. No toast: callers say what they did themselves.
    /// </summary>
    internal static int SetRepeatCount(int count)
    {
        int next = Mathf.Clamp(count, UnlimitedCount, MaxRepeatCount.Value);
        CurrentRepeatCount = next;
        _countDirty = true;
        _countChangedAt = Time.unscaledTime;
        return next;
    }

    // Remember Last Count: BepInEx saves the .cfg on every assignment and a held Plus/Minus changes the
    // count about twelve times a second, so the write waits until the count has been still for a second.
    private const float CountSaveDelay = 1f;
    private static bool _countDirty;
    private static float _countChangedAt;

    private static void FlushRememberedCount(bool force)
    {
        if (!_countDirty) return;
        if (!force && Time.unscaledTime - _countChangedAt < CountSaveDelay) return;
        _countDirty = false;
        if (RememberLastCount == null || !RememberLastCount.Value || LastCount == null) return;
        if (LastCount.Value != CurrentRepeatCount) LastCount.Value = CurrentRepeatCount;
    }

    private void TriggerRepeat()
    {
        if (Patcher.ActionPatch.IsRepeating)
        {
            // Cancel current repeat
            Patcher.ActionPatch.CancelRepeat();
            ShowNotification(Messages.Text("Repeat Cancelled"));
            return;
        }

        if (Patcher.ActionPatch.IsRecording)
        {
            ShowNotification(Messages.Text("Recording: stop it with {0} first", RecordSequenceKey.Value));
            return;
        }

        if (!Patcher.ActionPatch.HasLastAction)
        {
            // Show specific "not supported" message if the action name is known
            string lastAction = Patcher.ActionPatch.LastActionName;
            if (lastAction != "Unknown" && !string.IsNullOrEmpty(lastAction))
            {
                ShowNotification(Messages.Text("'{0}' is not supported", Patcher.ActionPatch.RejectedActionDisplay ?? lastAction));
                Logger.LogInfo($"[Repeat] Action '{lastAction}' is not permitted for repeat");
            }
            else
            {
                ShowNotification(Messages.Text("No action to repeat"));
                Logger.LogInfo("[Repeat] No action recorded to repeat");
            }
            return;
        }

        Logger.LogInfo($"[Repeat] Starting repeat of last action ({CountLabel})");
        StartCoroutine(Patcher.ActionPatch.RepeatLastAction(CurrentRepeatCount));
    }

    /// <summary>
    /// Fire the pinned action with the same count and Unlimited setting Shift+R uses. Unlike the
    /// Repeat key it never cancels: a run in progress is reported and left alone.
    /// </summary>
    private void TriggerPinnedAction()
    {
        if (Patcher.ActionPatch.IsRepeating)
        {
            ShowNotification(Messages.Text("A repeat is already running"));
            return;
        }

        if (Patcher.ActionPatch.IsRecording)
        {
            ShowNotification(Messages.Text("Recording: stop it with {0} first", RecordSequenceKey.Value));
            return;
        }

        if (!Patcher.ActionPatch.HasPinnedAction)
        {
            ShowNotification(Messages.Text("No pinned action"));
            Logger.LogInfo("[Repeat] No pinned action to fire");
            return;
        }

        Logger.LogInfo($"[Repeat] Firing pinned action ({CountLabel})");
        StartCoroutine(Patcher.ActionPatch.FirePinnedAction(CurrentRepeatCount));
    }

    /// <summary>
    /// Play the chosen recorded sequence (Feature 20) with the same count Shift+R uses. Like the Fire Pinned Action
    /// Key it never cancels: a run in progress is reported and left alone, and the Repeat Key cancels the play.
    /// </summary>
    private void TriggerSequence()
    {
        if (Patcher.ActionPatch.IsRepeating)
        {
            ShowNotification(Messages.Text("A repeat is already running"));
            return;
        }
        if (Patcher.ActionPatch.IsRecording)
        {
            ShowNotification(Messages.Text("Recording: stop it with {0} first", RecordSequenceKey.Value));
            return;
        }
        Logger.LogInfo($"[Repeat] Playing the chosen sequence ({CountLabel})");
        StartCoroutine(Patcher.ActionPatch.PlaySelectedSequence(CurrentRepeatCount));
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
    /// (which spans y 90-160, centered, so a box moved down stays beside it). Position is fixed apart from
    /// stepping below the popup's close button; tune the constants here if it collides with a HUD.
    /// </summary>
    private static void DrawCountIndicator()
    {
        // Layout constants live here only. Width fits about 28 characters at font 16.
        const float w = 250f;
        const float lineH = 22f;
        const int maxChars = 28;

        string[] lines = IndicatorLines();
        float h = lineH * lines.Length;
        float x = Screen.width - w - 16f;
        float y = 64f;
        // The inspection popup's close "x" can sit in this corner: when the box would cover it, drop the box to just
        // below the button, keeping its right-hand place. The button is read live, so this follows any resolution.
        if (Patcher.ActionPatch.TryGetPopupCloseButtonRect(out Rect close)
            && new Rect(x - 8, y - 4, w + 16, h + 8).Overlaps(close))
            y = close.yMax + 8f;

        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.Box(new Rect(x - 8, y - 4, w + 16, h + 8), "");
        GUI.color = Color.white;

        for (int i = 0; i < lines.Length; i++)
        {
            string text = Ellipsize(lines[i], maxChars);
            float ly = y + i * lineH;
            GUI.Label(new Rect(x + 1, ly + 1, w, lineH), text, _indicatorShadowStyle);
            GUI.Label(new Rect(x, ly, w, lineH), text, _indicatorStyle);
        }
    }

    private static string Ellipsize(string s, int maxChars)
        => s.Length <= maxChars ? s : s.Substring(0, maxChars - 3) + "...";

    /// <summary>
    /// The count indicator's lines: the count, what Shift+R would replay ("Next: none" when nothing
    /// is captured), and "Pinned: X" only while a pin is set.
    /// </summary>
    private static string[] IndicatorLines()
    {
        var lines = new System.Collections.Generic.List<string>(3)
        {
            IsUnlimited ? "Repeat: Unlimited" : $"Repeat: x{CountLabel}",
            "Next: " + (Patcher.ActionPatch.NextActionDisplay ?? "none")
        };
        string pinned = Patcher.ActionPatch.PinnedActionName;
        if (pinned != null) lines.Add("Pinned: " + pinned);
        if (Patcher.ActionPatch.IsPaused) lines.Add("Paused");
        if (Presets.LoadedName != null) lines.Add("Preset: " + Presets.LoadedName);
        return lines.ToArray();
    }

    /// <summary>The indicator text, one line per row (read by the harness through reflect/get).</summary>
    public static string IndicatorText() => string.Join("\n", IndicatorLines());

    private void OnDestroy()
    {
        FlushRememberedCount(true);
        Repeat_Action.Patcher.ActionPatch.UnsubscribeRunStart();
        _harmony?.UnpatchSelf();
    }
}
