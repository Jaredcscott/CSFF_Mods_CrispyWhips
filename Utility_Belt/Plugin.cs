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
    private const string PluginVersion = "0.1.2";

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

        // A static event that fires once per run start; subscribed once per process.
        GameManager.OnGMInitialized += Locks.ItemLocks.OnRunStarted;

        // The one Info line a mod prints at startup (CLAUDE.md "Mod Logging Norms").
        Logger.LogInfo($"{PluginName} v{PluginVersion} loaded.");
    }

    private void OnDestroy()
    {
        GameManager.OnGMInitialized -= Locks.ItemLocks.OnRunStarted;
        ModuleSeams.Unsubscribe();
        _harmony?.UnpatchSelf();
    }

    private void Update()
    {
        ContainerHotkeys.Tick();
        Locks.ItemLocks.Tick();
        SortContainer.Tick();
        SealedMark.Tick();
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
