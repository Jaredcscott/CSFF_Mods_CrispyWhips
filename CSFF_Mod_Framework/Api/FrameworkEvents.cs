using CSFFModFramework.Util;

namespace CSFFModFramework.Api;

/// <summary>
/// Lifecycle hooks plugins can subscribe to. <see cref="Loaded"/> and <see cref="GameDataReady"/> each fire at
/// most once per game session; <see cref="RunStarted"/> fires on EVERY run start, same as
/// <c>GameManager.OnGMInitialized</c> itself (a save load or a menu round trip re-fires it, CLAUDE.md "Per-Run
/// Event Registration"). Subscribe in your plugin's <c>Awake</c>: events fire after the framework finishes its
/// load steps later in the boot sequence.
/// </summary>
public static class FrameworkEvents
{
    /// <summary>
    /// Fires once after the framework finishes <see cref="Loading.LoadOrchestrator.Execute"/>.
    /// At this point: mod content is registered, sprites are loaded,
    /// blueprints/perks are injected, and <see cref="ModRegistry"/> is populated.
    /// </summary>
    public static event Action Loaded;

    /// <summary>
    /// Fires after the game's <c>GameLoad</c> postfix completes — vanilla AllData
    /// is fully populated and indexed. Use for plugins that need to query or patch
    /// game state once everything is in memory.
    /// </summary>
    public static event Action GameDataReady;

    /// <summary>
    /// Fires on <c>GameManager.OnGMInitialized</c> (fleet plan section 10, W0.10 row 6): every run start, so
    /// every save load or menu round trip, not once per process. A mod that used to reflect-subscribe its own
    /// handler to <c>OnGMInitialized</c> can subscribe here instead; the subscription still happens once per
    /// process (<see cref="ArmRunStarted"/>, called once from <see cref="Loading.LoadOrchestrator.Execute"/>), and
    /// the event re-fires each run the same way the game's own field does.
    /// </summary>
    public static event Action RunStarted;

    private static bool _runStartedArmed;

    internal static void RaiseLoaded()
    {
        var handler = Loaded;
        if (handler == null) return;
        foreach (var d in handler.GetInvocationList())
        {
            try { ((Action)d)(); }
            catch (Exception ex) { Log.Warn($"FrameworkEvents.Loaded subscriber threw: {ex}"); }
        }
    }

    internal static void RaiseGameDataReady()
    {
        var handler = GameDataReady;
        if (handler == null) return;
        foreach (var d in handler.GetInvocationList())
        {
            try { ((Action)d)(); }
            catch (Exception ex) { Log.Warn($"FrameworkEvents.GameDataReady subscriber threw: {ex}"); }
        }
    }

    /// <summary>Subscribes <see cref="RaiseRunStarted"/> to <c>GameManager.OnGMInitialized</c>, once per process.</summary>
    internal static void ArmRunStarted()
    {
        if (_runStartedArmed) return;
        _runStartedArmed = true;
        try
        {
            GameManager.OnGMInitialized -= RaiseRunStarted;
            GameManager.OnGMInitialized += RaiseRunStarted;
        }
        catch (Exception ex)
        {
            Log.Warn($"FrameworkEvents.ArmRunStarted: could not subscribe to GameManager.OnGMInitialized ({ex.GetType().Name}: {ex.Message}); RunStarted will never fire.");
        }
    }

    private static void RaiseRunStarted()
    {
        var handler = RunStarted;
        if (handler == null) return;
        foreach (var d in handler.GetInvocationList())
        {
            try { ((Action)d)(); }
            catch (Exception ex) { Log.Warn($"FrameworkEvents.RunStarted subscriber threw: {ex}"); }
        }
    }
}
