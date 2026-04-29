namespace CSFFModFramework;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "crispywhips.CSFFModFramework";
    public const string PluginName = "CSFF Mod Framework";
    public const string PluginVersion = "2.0.2";

    public static Plugin Instance { get; private set; }
    internal static Harmony Harmony { get; private set; }

    private void Awake()
    {
        Instance = this;
        Util.Log.Init(Logger);

        // Config: verbose logging (default off — suppresses per-item diagnostic spam)
        var verboseConfig = Config.Bind("General", "VerboseLogging", false,
            "Enable verbose/debug logging. Shows per-item diagnostic traces in LogOutput.log.");
        Util.Log.Verbose = verboseConfig.Value;

        // Config: load-time diagnostics (default off — two full AllData scans around WarpResolver)
        var diagConfig = Config.Bind("General", "EnableLoadDiagnostics", false,
            "Run blueprint research diagnostic passes around WarpResolver. "
            + "Off by default — enable only when investigating research-timer regressions.");
        Loading.LoadOrchestrator.EnableLoadDiagnostics = diagConfig.Value;

        // Config: wildlife raid system (opt-in; inherits tag_NotSafeFromAnimals on vanilla
        // open-storage containers and periodically spoils food in them).
        var raidEnabled = Config.Bind("Wildlife", "WildlifeRaidsEnabled", false,
            "Once per in-game day, roll a chance that wildlife raids an unguarded container "
            + "(any structure tagged tag_NotSafeFromAnimals) in the player's environment. "
            + "Disabled by default — enable to opt in.");
        var raidChance = Config.Bind("Wildlife", "WildlifeRaidDailyChance", 0.35f,
            "Probability (0-1) per in-game day that a raid is attempted when this feature is enabled.");
        var raidStress = Config.Bind("Wildlife", "WildlifeRaidStressPenalty", 2f,
            "Stress added to the player when a raid succeeds.");
        Wildlife.WildlifeRaidService.Enabled = raidEnabled.Value;
        Wildlife.WildlifeRaidService.DailyChance = raidChance.Value;
        Wildlife.WildlifeRaidService.StressPenalty = raidStress.Value;
        Wildlife.WildlifeRaidService.Init();

        Harmony = new Harmony(PluginGuid);

        // Performance: pre-warm DOTween pool so mid-session expansion doesn't GC-spike
        // when many cards animate at once (inventory shuffles, blueprint open, etc).
        Patching.Performance.DOTweenCapacityPrewarm.Configure(Config);

        // Performance: strip Debug.LogWarning calls from DynamicLayoutSlot.AssignCard —
        // late-game saves with many improvements spam this per-frame during drag/drop.
        Patching.Performance.SlotAssignmentLogSuppress.ApplyPatch(Harmony);

        // Performance: reuse a cached float[3] inside AmbienceImageEffect.Update instead of
        // allocating a fresh array per frame (~1.4 KB/sec of GC churn eliminated).
        Patching.Performance.AmbienceArrayReuse.ApplyPatch(Harmony);

        // Performance: throttle InGameCardBase.LateUpdate for off-screen, non-animating cards.
        // Biggest remaining win for card-heavy saves — hundreds of cards in other environments
        // stop paying per-frame CheckIfVisibleOnScreen cost.
        Patching.Performance.OffScreenCardThrottle.Configure(Config, Harmony);

        // Core patches
        Patching.GameLoadPatch.ApplyPatch(Harmony);
        Patching.LocalizationPatch.ApplyPatch(Harmony);
        Patching.BlueprintFlagFix.ApplyPatch(Harmony);
        Patching.BugFixes.WikiModQuickFindFix.ApplyPatch(Harmony);

        // Bug-fix / compat patches
        // Finalizer that swallows third-party NREs in BlueprintModelsScreen.Show/Toggle
        // (CardSizeReduce's Show_Postfix throws every frame on blueprint unlock).
        Patching.BugFixes.BlueprintScreenFix.ApplyPatch(Harmony);
        // CardSizeReduce 3.3.0 compat: patches AccessTools.Field to find auto-property
        // backing fields when CSR is installed; falls back to internal scaling shim
        // when CSR config is present but the DLL is missing. Self-no-ops without CSR.
        Patching.BugFixes.CardScaleCompat.Configure(Config, Harmony);
        // CheatsPatch 1.1.0 compat: postfix AccessTools.TypeByName so CheatsPatch's
        // patches resolve "UCheatsManager" → "CheatsManager" (CSFF rename).
        Patching.BugFixes.CheatsPatchCompat.Configure(Harmony);

        // GIF animation support — patches CardGraphics.Setup / RefreshCookingStatus.
        // Self-skips registration when no mod ships CardData/Gif/*.json.
        Patching.GifAnimationPatch.ApplyPatch(Harmony);

        // Opt-in diagnostic: logs GameLoad.AutoSaveGame calls (first 8) so we can
        // see what CheckpointTypes value EA 0.62b passes for the 4 AM autosave
        // and whether CSFFStopAutoSave's stack-walk reaches ActionRoutine.
        Patching.Diagnostics.AutoSaveDiagnostics.Configure(Config, Harmony);

        // Opt-in diagnostic: times CheckForTracks + ChangeEnvironment coroutines on
        // every travel and counts GiveCard invocations during CheckForTracks. Used
        // to investigate the 1–2 s freeze when entering a location with fresh tracks.
        Patching.Diagnostics.TrackingTimingDiagnostics.Configure(Config, Harmony);

        Util.Log.Info($"{PluginName} v{PluginVersion} loaded. ({Harmony.GetPatchedMethods().Count()} methods patched)");
    }

    private void Update()
    {
        Wildlife.WildlifeRaidService.PollUpdate();
    }

    private void OnDestroy()
    {
        Harmony?.UnpatchSelf();
    }
}
