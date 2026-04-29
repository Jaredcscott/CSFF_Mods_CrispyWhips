using CSFFModFramework.Util;

namespace CSFFModFramework.Api;

/// <summary>
/// Lifecycle hooks plugins can subscribe to. Each event fires at most once per
/// game session. Subscribe in your plugin's <c>Awake</c> — events fire after
/// the framework finishes its load steps later in the boot sequence.
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
}
