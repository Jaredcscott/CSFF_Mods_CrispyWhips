namespace Utility_Belt;

/// <summary>
/// Utility Belt host plugin. It loads, prints one startup line, on request (Diagnostics > Log Module Sources)
/// reports which copy of Quick Transfer, Repeat Action and Skill Speed Boost is running, and hosts Utility
/// Belt's own features, each a small Harmony patch applied from Awake with an explicit harmony.Patch call
/// (Patcher/). It declares no [BepInDependency] of any kind.
/// </summary>
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
internal class Plugin : BaseUnityPlugin
{
    private const string PluginGuid = "crispywhips.utility_belt";
    private const string PluginName = "Utility Belt";
    private const string PluginVersion = "0.1.7";

    internal new static ManualLogSource Logger;

    internal static ConfigEntry<bool> LogModuleSources { get; private set; }
    internal static ConfigEntry<bool> HideWeatherEffects { get; private set; }
    internal static ConfigEntry<Patcher.ClockFormat> ClockFormatSetting { get; private set; }
    internal static ConfigEntry<bool> ShowReadouts { get; private set; }
    internal static ConfigEntry<bool> QuickswapEquip { get; private set; }
    internal static ConfigEntry<bool> ShowSealedMark { get; private set; }
    internal static ConfigEntry<string> SealedCards { get; private set; }
    internal static ConfigEntry<bool> ShowRandomiseButton { get; private set; }
    internal static ConfigEntry<UnityEngine.KeyCode> CycleContainersKey { get; private set; }
    internal static ConfigEntry<UnityEngine.KeyCode>[] OpenContainerKeys { get; private set; }
    internal static ConfigEntry<UnityEngine.KeyCode> LockToggleKey { get; private set; }
    internal static ConfigEntry<UnityEngine.KeyCode> SortContainerKey { get; private set; }
    internal static ConfigEntry<bool> AutoPullMaterials { get; private set; }
    internal static ConfigEntry<bool> StoreProductsIn { get; private set; }
    internal static ConfigEntry<bool> EquipSpareToolOnBreak { get; private set; }
    internal static ConfigEntry<UnityEngine.KeyCode> StoreContainerKey { get; private set; }
    internal static ConfigEntry<CardRowLayout>[] CardRowLayouts { get; private set; }
    internal static ConfigEntry<int> CrowdedAbove { get; private set; }
    internal static ConfigEntry<float> SmallerSize { get; private set; }
    internal static ConfigEntry<int> ExtraHandSlots { get; private set; }
    internal static ConfigEntry<int> ExtraCarryWeight { get; private set; }
    internal static ConfigEntry<UnityEngine.KeyCode> SortHandRowKey { get; private set; }
    internal static ConfigEntry<UnityEngine.KeyCode> SortBaseRowKey { get; private set; }
    internal static ConfigEntry<UnityEngine.KeyCode> TrashCardKey { get; private set; }
    internal static ConfigEntry<bool> ShowCarryWeight { get; private set; }
    internal static ConfigEntry<bool> ShowWornToolMark { get; private set; }
    internal static ConfigEntry<int> WornToolThreshold { get; private set; }
    internal static ConfigEntry<bool> ContainerFilterOn { get; private set; }
    internal static ConfigEntry<UnityEngine.KeyCode> FocusContainerFilterKey { get; private set; }
    internal static ConfigEntry<UnityEngine.KeyCode> PinBlueprintKey { get; private set; }
    internal static ConfigEntry<string> PinnedBlueprints { get; private set; }
    internal static ConfigEntry<bool> NeverFollowCards { get; private set; }

    private Harmony _harmony;

    private void Awake()
    {
        Logger = base.Logger;

        LogModuleSources = Config.Bind(
            "Diagnostics",
            "Log Module Sources",
            false,
            "When true, logs once at startup which file Quick Transfer, Repeat Action and Skill Speed Boost each loaded from, or that one is not installed. For bug reports.");

        HideWeatherEffects = Config.Bind(
            "Accessibility",
            "Hide Rain And Snow Effects",
            false,
            "When true, hides the falling rain, snow and other weather particles drawn over the scene while you are outside. The weather itself, its sound and its effect on your character are unchanged. Takes effect within a frame of changing it.");

        ClockFormatSetting = Config.Bind(
            "Display",
            "Clock Format",
            Patcher.ClockFormat.TwentyFourHour,
            "TwentyFourHour (the game's own 21:45) or TwelveHour (9:45p). Only the main clock beside the day counter changes; save-slot times and the log stay 24-hour. Takes effect within a frame of changing it.");

        ShowReadouts = Config.Bind(
            "Display",
            "Show Capacity And Warmth Readouts",
            false,
            "When true, the inspection popup adds one line under a card's description: a container's weight and liquid against its capacity in numbers (the game shows only a bar and a percentage), and for a bed or cover the warmth it gives while you sleep. Read from the card's own data; no comfort figure is shown. Takes effect the next time a card is inspected.");

        ShowSealedMark = Config.Bind(
            "Display",
            "Show Sealed Mark",
            false,
            "When true, a small red wax seal is drawn in the top right corner of a sealed container's card (a sealed storage pot or fermentation bin), so a sealed one reads apart from an unsealed one on the board without opening either. Display only: nothing about the cards changes. Which cards count as sealed is the Sealed Cards list below. Takes effect within a frame of changing it.");

        SealedCards = Config.Bind(
            "Display",
            "Sealed Cards",
            SealedMark.DefaultSealedCards,
            "The cards that get the sealed mark, as a comma-separated list of card IDs. The default is the game's Sealed Storage Pot, the placed Sealed Storage Pot and the Sealed Fermentation Bin. (The game's own Sealed Container tag is not used: it is also on the Unsealed Storage Pot.) Add the ID of a modded sealed container to mark it too.");

        ShowRandomiseButton = Config.Bind(
            "Character",
            "Show Randomise Button",
            true,
            "When true, the character-creation perk screen has a Randomise button under Submit. Pressing it re-rolls your perks: it keeps the difficulty rating band you had, takes at most one perk from each perk group, only perks you have unlocked, and leaves the perks the game always fills (skin, hair, eyes, start place) as they are. Press it again for another roll, or change the result by hand. It does nothing until pressed. Takes effect the next time the screen opens.");

        QuickswapEquip = Config.Bind(
            "Equipment",
            "Quickswap When Equipping",
            false,
            "When true, equipping a card whose equipment slot is already full (a second coat, a second hat) swaps instead of refusing: the card you are wearing goes back to where the new one came from (the item bar, the open container it was in, or the ground) and the new one is worn. Only for your own drag or double-click, never an NPC. Does nothing when either card cannot be moved by hand, when the worn card is mandatory equipment, or when the container the new card came from cannot take the old one (the game's own refusal shows). Equipment bonuses are the game's own: the result is the same as taking the old card off and putting the new one on.");

        // Never a number key by default: vanilla's bookmarks read Alpha1 to Alpha9 with no modifier test
        // (plan W0.2). BackQuote is read by neither vanilla, Quick Transfer nor Repeat Action (re-read 2026-10-10).
        CycleContainersKey = Config.Bind(
            "Hotkeys",
            "Cycle Equipped Containers",
            UnityEngine.KeyCode.BackQuote,
            "Opens the next equipped card that has an inventory (a bag, a basket), in equipment-slot order, wrapping around to the first. Opening works exactly like clicking the card, so pressing it on your only bag closes it again. None turns it off. Ignored while typing in a text field.");

        OpenContainerKeys = new ConfigEntry<UnityEngine.KeyCode>[ContainerHotkeys.PerBagKeyCount];
        for (var i = 0; i < ContainerHotkeys.PerBagKeyCount; i++)
        {
            OpenContainerKeys[i] = Config.Bind(
                "Hotkeys",
                $"Open Equipped Container {i + 1}",
                UnityEngine.KeyCode.None,
                $"Opens the equipped card with an inventory that is number {i + 1} in equipment-slot order (the same order the cycle key uses), like clicking it. None (the default) turns it off. Avoid the number keys 1 to 9: the game uses them for bookmarks whatever modifier is held.");
        }

        // L is read by neither vanilla, Quick Transfer nor Repeat Action (plan W0.2 and Q8; re-read 2026-10-10:
        // QT X and Z, RA G, N, K, M and U, plus the modifier, Equals, Minus, R, P and F binds).
        LockToggleKey = Config.Bind(
            "Hotkeys",
            "Lock Toggle",
            UnityEngine.KeyCode.L,
            "With a container open, hover a card inside it and press this key to lock it (a small padlock shows on the card) or unlock it. Take Out All leaves locked cards where they are. Cards that arrived together (one harvest, one craft) share a lock. None turns the key off; existing locks still apply. Ignored while typing in a text field.");

        // O is read by neither vanilla, Quick Transfer nor Repeat Action (plan W0.2 and Q8; re-read 2026-10-10:
        // QT X and Z, RA G, N, K, M and U, plus the modifier, Equals, Minus, R, P and F binds).
        SortContainerKey = Config.Bind(
            "Hotkeys",
            "Sort Container",
            UnityEngine.KeyCode.O,
            "With a container open, each press changes its order: first freshest first, then highest quality first, then back to the order it had before the first press. Locked cards keep their slots. Cards of one kind always stay in one pile, so two piles of the same item cannot be split apart by freshness. Does nothing on a small built-in inventory, a cooking container, a fixed-slot station or a container with a saved recipe. None turns it off. Ignored while typing in a text field.");

        AutoPullMaterials = Config.Bind(
            "RepeatAction",
            "Auto-Pull Materials",
            false,
            "When true, a Repeat Action run that is refused because a card it needs in your hand is in a container instead (a chest on the ground or a bag you carry, in the place you are in) takes one such card out into your hand and tries that repeat once more. Example: Train with a Composite Bow needs a Simple Arrow in hand. Cards you locked, cards that cannot be moved by hand, and anything in another place are never taken, and nothing moves when your hands are full. If nothing is found, the run stops as it always has. (Repeat Action already uses ingredients and targets that sit in a container on its own; this is for the hand-only ones.) Needs Repeat Action and Quick Transfer, which Utility Belt includes.");

        StoreProductsIn = Config.Bind(
            "RepeatAction",
            "Store Products In",
            false,
            "When true, you can pick a container (hover it and press the Pick Store Container key) and every card a Repeat Action run makes is moved into it as each repeat finishes. When the container is full, storing stops for that run and a notice says so; the rest stay where they landed. The container is forgotten when you change place, when it is destroyed, or when you press the key on it again. Cards you locked, cards that cannot be moved by hand and liquids are left alone. Needs Repeat Action, which Utility Belt includes; Quick Transfer's click is used when the container is open.");

        EquipSpareToolOnBreak = Config.Bind(
            "RepeatAction",
            "Equip Spare Tool On Break",
            false,
            "When true, a Repeat Action run that is refused because a tool it needs WORN is gone (it broke, as worn Leather Gloves do while you harvest nettles) puts on a spare that does the same job and tries that repeat once more. The spare can be in your hands, on the ground, or in a container here (a bag you carry, a chest); the one with the most use left is worn. Cards you locked, cards that cannot be moved by hand and anything in another place are never used. If the slot is still taken by something else, the spare is worn only while Quickswap When Equipping is on. If no spare is found, the run stops as it always has. (A tool you DRAG onto the target is Repeat Action's own Use Spare Tool On Break.) Needs Repeat Action, which Utility Belt includes.");

        // V is read by neither vanilla, Quick Transfer, Repeat Action nor Utility Belt's other keys (re-read 2026-10-12:
        // vanilla A B C D E H J Q S T W, QT X and Z, RA G N K M U R P F, UB L O BackQuote).
        StoreContainerKey = Config.Bind(
            "Hotkeys",
            "Pick Store Container",
            UnityEngine.KeyCode.V,
            "With Store Products In on, hover a container (a chest on the ground, a bag you carry) and press this key to make it the place Repeat Action puts what it makes; press it again on the same container to stop. Does nothing while Store Products In is off. None turns the key off. Ignored while typing in a text field.");

        // Card Size replaces Card Size Reduce (and with it the ModCore dependency). Every row defaults to Normal.
        CardRowLayouts = new ConfigEntry<CardRowLayout>[CardSize.RowKeys.Length];
        for (var i = 0; i < CardSize.RowKeys.Length; i++)
        {
            CardRowLayouts[i] = Config.Bind(
                "Card Size",
                CardSize.RowKeys[i],
                CardRowLayout.Normal,
                $"{CardSize.RowDescriptions[i]} Normal (the game's own layout), Smaller (one row of smaller cards, so more fit across), TwoRows (two rows of half-size cards) or TwoRowsWhenCrowded (normal until the row holds more cards than Crowded Above, then two rows). Display only. Takes effect within a frame of changing it. Stays off while Card Size Reduce is installed.");
        }

        CrowdedAbove = Config.Bind(
            "Card Size",
            "Crowded Above",
            9,
            new ConfigDescription("A row set to TwoRowsWhenCrowded switches to two rows while it holds more cards than this.", new AcceptableValueRange<int>(1, 50)));

        SmallerSize = Config.Bind(
            "Card Size",
            "Smaller Size",
            0.7f,
            new ConfigDescription("How big cards are drawn in a row set to Smaller, as a fraction of their normal size.", new AcceptableValueRange<float>(0.4f, 0.95f)));

        ExtraHandSlots = Config.Bind(
            "Capacity",
            "Extra Hand Slots",
            0,
            new ConfigDescription("Adds 3, 6 or 9 slots to your item bar (0 for none). Takes effect the next time you load a save or start a run. Empty your extra slots before lowering it. Stays off while Card Size Reduce is installed.", new AcceptableValueList<int>(0, 3, 6, 9)));

        ExtraCarryWeight = Config.Bind(
            "Capacity",
            "Extra Carry Weight",
            0,
            new ConfigDescription("Raises the most weight you can carry by 3000, 6000 or 9000 (0 for none; the game's base limit is 4000). Applies to the run in play within a frame of changing it, and to every run you start or load. Nothing is written to your save. Stays off while Card Size Reduce is installed.", new AcceptableValueList<int>(0, 3000, 6000, 9000)));
        ExtraCarryWeight.SettingChanged += (_, _) => Capacity.ApplyCarryWeight();

        // Utility Belt 0.1.4 (B7 to B13, Documentation/Design/UtilityBelt_0_1_4_As_Built.md). Every option below
        // is off (false) or unbound (None) by default, the charter that UtilityBelt-ConfigDefaults.Tests.ps1
        // gates; Worn Tool Threshold and Pinned Blueprints are the two pinned non-switch values.
        SortHandRowKey = Config.Bind(
            "Hotkeys",
            "Sort Hand Row",
            UnityEngine.KeyCode.None,
            "Each press changes the order of your item bar (the row of cards in your hands): first freshest first, then highest quality first, then back to the order it had before the first press. Locked and pinned cards keep their places, and empty slots stay at the end. The new order is kept when you save. None (the default) turns it off. Ignored while typing, while an action runs and while you drag a card.");

        SortBaseRowKey = Config.Bind(
            "Hotkeys",
            "Sort Base Row",
            UnityEngine.KeyCode.None,
            "The same as Sort Hand Row, for the base row (the cards on the ground and your structures). None (the default) turns it off.");

        TrashCardKey = Config.Bind(
            "Hotkeys",
            "Trash Card",
            UnityEngine.KeyCode.None,
            "Hover a card and press this key to trash ONE card, exactly as the trash button in its window does (anything the game gives back for trashing it is given back). Only cards the trash button would offer: never a pinned card, a card the game will not let you trash, or a card you locked. None (the default) turns it off. Ignored while typing, while an action runs and while you drag a card.");

        ShowCarryWeight = Config.Bind(
            "Display",
            "Show Carry Weight",
            false,
            "When true, hovering the weight bar beside your item bar shows how much you carry against the most you can carry, in numbers (the game shows only the bar). The limit includes Extra Carry Weight. Display only. Takes effect within a second of changing it.");

        ShowWornToolMark = Config.Bind(
            "Display",
            "Show Worn Tool Mark",
            false,
            "When true, a small cracked amber mark is drawn in the bottom left corner of a tool (any item with a Usage bar) whose Usage is below Worn Tool Threshold, so a tool about to break reads apart at a glance. Display only. Takes effect within a frame of changing it.");

        WornToolThreshold = Config.Bind(
            "Display",
            "Worn Tool Threshold",
            25,
            new ConfigDescription("Show Worn Tool Mark marks a tool whose Usage is below this percentage of its maximum.", new AcceptableValueRange<int>(1, 99)));

        ContainerFilterOn = Config.Bind(
            "Display",
            "Container Filter",
            false,
            "When true, an open container's window gets a search box: type part of a card's name (in the language you play in) and only the slots holding a matching card are shown. Clearing the box, closing the window or opening another container shows every slot again. Display only: no card moves. Takes effect the next time a container is opened.");

        FocusContainerFilterKey = Config.Bind(
            "Hotkeys",
            "Focus Container Filter",
            UnityEngine.KeyCode.None,
            "With Container Filter on and a container open, puts the cursor in its search box. None (the default) turns it off; you can always click the box instead.");

        PinBlueprintKey = Config.Bind(
            "Hotkeys",
            "Pin Blueprint",
            UnityEngine.KeyCode.None,
            "In the blueprint screen, hover a blueprint and press this key to pin it: it is shown first in its tab and in Search, with a small pin mark. Press it again to unpin. Order only: research and building are unchanged. None (the default) turns it off. Ignored while typing.");

        PinnedBlueprints = Config.Bind(
            "General",
            "Pinned Blueprints",
            string.Empty,
            "The blueprints Pin Blueprint has pinned, as a comma-separated list of blueprint IDs. Shared by every save. Empty (the default) pins nothing.");

        // Utility Belt 0.1.6. Off by default, like every option since 0.1.4 (UtilityBelt-ConfigDefaults.Tests.ps1).
        NeverFollowCards = Config.Bind(
            "Display",
            "Never Follow Cards",
            false,
            "When true, a card row no longer scrolls by itself to a card that was just made, picked up, dropped, taken out of a container or moved by Quick Transfer: the row stays where you left it. Bookmarks and the game's own find-a-card links still scroll to the card. Display only: where cards go is unchanged. Takes effect at once.");

        _harmony = new Harmony(PluginGuid);
        // One try per patch, so a throw in one never skips the ones after it.
        void Apply(string name, Action<Harmony> apply)
        {
            try { apply(_harmony); }
            catch (Exception ex) { Logger.LogError($"Failed to apply {name}: {ex}"); }
        }
        Apply("WeatherEffectsPatch", Patcher.WeatherEffectsPatch.ApplyPatch);
        Apply("ClockFormatPatch", Patcher.ClockFormatPatch.ApplyPatch);
        Apply("ItemLockPatch", Patcher.ItemLockPatch.ApplyPatch);
        Apply("InspectionReadoutPatch", Patcher.InspectionReadoutPatch.ApplyPatch);
        Apply("QuickswapEquipPatch", Patcher.QuickswapEquipPatch.ApplyPatch);
        Apply("CharacterRandomiserPatch", Patcher.CharacterRandomiserPatch.ApplyPatch);
        Apply("CardSizePatch", Patcher.CardSizePatch.ApplyPatch);
        Apply("ContainerFilterPatch", Patcher.ContainerFilterPatch.ApplyPatch);
        Apply("BlueprintPins", BlueprintPins.ApplyPatch);
        Apply("NeverFollowCardPatch", Patcher.NeverFollowCardPatch.ApplyPatch);

        // Static events that fire once per run start; each subscribed once per process.
        GameManager.OnGMInitialized += Locks.ItemLocks.OnRunStarted;
        GameManager.OnGMInitialized += Capacity.OnRunStarted;

        // The one Info line a mod prints at startup (CLAUDE.md "Mod Logging Norms").
        Logger.LogInfo($"{PluginName} v{PluginVersion} loaded.");
    }

    private void OnDestroy()
    {
        GameManager.OnGMInitialized -= Locks.ItemLocks.OnRunStarted;
        GameManager.OnGMInitialized -= Capacity.OnRunStarted;
        ModuleSeams.Unsubscribe();
        _harmony?.UnpatchSelf();
    }

    private void Update()
    {
        ContainerHotkeys.Tick();
        Locks.ItemLocks.Tick();
        SortContainer.Tick();
        RowSort.Tick();
        TrashHotkey.Tick();
        SealedMark.Tick();
        WornToolMark.Tick();
        CarryWeight.Tick();
        ContainerFilter.Tick();
        BlueprintPins.Tick();
        CardSize.Tick();
        // Resolves Repeat Action's and Quick Transfer's seams on the first frame (never Awake: plan W0.1d), then keeps
        // the requirements-unmet subscription in step with the option.
        AutoPull.Tick();
        StoreProducts.Tick();
        SpareTool.Tick();
    }

    // Start, not Awake: Chainloader.PluginInfos fills in load order, so a module that loads after this
    // plugin is not in it yet during this plugin's own Awake (plan W0.1d).
    private void Start()
    {
        if (LogModuleSources.Value)
        {
            ModuleRegistry.LogSources(Logger);
        }
    }
}
