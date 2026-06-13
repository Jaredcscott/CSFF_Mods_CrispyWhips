using CSFFModFramework.Util;

namespace CSFFModFramework.Api;

/// <summary>
/// Framework-owned game-time and real-time tick events (Centralization Tier 2, P8).
/// Exposes the DTP-tick / day-rollover detection that <c>TriggerService</c> and
/// <c>WildlifeRaidService</c> already compute, so content mods no longer run their own
/// Update polling loops (Sirus wolf tick, WDI fishpond coroutine, ACT HeatHeldLiquid).
///
/// <para>Driven by ONE framework Update loop (<c>Plugin.Update</c>). Every callback is
/// isolated in its own try/catch — one throwing subscriber never starves the others.
/// All callbacks run on Unity's main thread.</para>
///
/// <code>
/// TickEvents.DtpTick += () => { /* fires once per 15 in-game minutes */ };
/// TickEvents.DayRollover += () => { /* fires once per in-game day */ };
/// var handle = TickEvents.Interval(0.5f, () => { /* real-time, every 0.5s unscaled */ });
/// TickEvents.Cancel(handle);
/// </code>
/// </summary>
public static class TickEvents
{
    /// <summary>
    /// Fires when the game's DayTimePoints value changes (one DTP = 15 in-game minutes;
    /// 96 per day). Does not fire before the game is initialized; the first observed
    /// value primes the gate without firing.
    /// </summary>
    public static event Action DtpTick;

    /// <summary>
    /// Fires once per in-game day rollover (DTP wrap from ~0 back up to ~96 — the same
    /// detection TriggerService and WildlifeRaidService use).
    /// </summary>
    public static event Action DayRollover;

    /// <summary>Opaque registration token for <see cref="Interval"/> callbacks.</summary>
    public sealed class IntervalHandle
    {
        internal float Period;
        internal float Accum;
        internal Action Callback;
        internal string Name;
    }

    private static readonly List<IntervalHandle> _intervals = new();
    private static IntervalHandle[] _intervalSnapshot = Array.Empty<IntervalHandle>();
    private static int _lastDtp = int.MinValue;
    private static int _lastDayDtp = int.MinValue;

    /// <summary>
    /// Registers a real-time interval callback (unscaled seconds, accumulated across
    /// frames). Returns a handle for <see cref="Cancel"/>. <paramref name="name"/> is
    /// used in error log lines when the callback throws.
    /// </summary>
    public static IntervalHandle Interval(float seconds, Action callback, string name = null)
    {
        if (callback == null) return null;
        var handle = new IntervalHandle
        {
            Period = Mathf.Max(0.05f, seconds),
            Callback = callback,
            Name = name ?? "Interval",
        };
        lock (_intervals)
        {
            _intervals.Add(handle);
            _intervalSnapshot = _intervals.ToArray();
        }
        return handle;
    }

    /// <summary>Cancels an interval registered with <see cref="Interval"/>. Safe to call from inside the callback.</summary>
    public static void Cancel(IntervalHandle handle)
    {
        if (handle == null) return;
        lock (_intervals)
        {
            if (_intervals.Remove(handle))
                _intervalSnapshot = _intervals.ToArray();
        }
    }

    // ── Framework driver ─────────────────────────────────────────────────────

    /// <summary>Called from <c>Plugin.Update()</c>. Cheap no-op when nothing is subscribed.</summary>
    internal static void PollUpdate()
    {
        var intervals = _intervalSnapshot;
        if (intervals.Length > 0)
        {
            float dt = Time.unscaledDeltaTime;
            foreach (var h in intervals)
            {
                h.Accum += dt;
                if (h.Accum < h.Period) continue;
                h.Accum = 0f;
                try { h.Callback(); }
                catch (Exception ex) { Log.Warn($"[TickEvents] interval '{h.Name}' threw: {Log.ExceptionText(ex)}"); }
            }
        }

        if (DtpTick == null && DayRollover == null) return;

        if (Gate.OncePerDtpTick(ref _lastDtp))
            RaiseIsolated(DtpTick, nameof(DtpTick));
        if (Gate.OncePerDayRollover(ref _lastDayDtp))
            RaiseIsolated(DayRollover, nameof(DayRollover));
    }

    private static void RaiseIsolated(Action handler, string eventName)
    {
        if (handler == null) return;
        foreach (var d in handler.GetInvocationList())
        {
            try { ((Action)d)(); }
            catch (Exception ex) { Log.Warn($"[TickEvents] {eventName} subscriber threw: {Log.ExceptionText(ex)}"); }
        }
    }
}
