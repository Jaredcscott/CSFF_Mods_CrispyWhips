namespace CSFFModFramework.Api;

/// <summary>
/// Frame and game-time dedup gates (Centralization Tier 1). Replaces the
/// frame-count dedup guards and per-DTP tick detection re-implemented in WDI,
/// ACT, CMC, and Sirus.
///
/// <para>Callers own the state field (one static int per call site), so independent
/// handlers never share or corrupt each other's gates:</para>
/// <code>
/// private static int _lastFrame = -1;
/// if (!Gate.OncePerFrame(ref _lastFrame)) return; // duplicate fire this frame
/// </code>
/// </summary>
public static class Gate
{
    /// <summary>
    /// True the first time it is called in a given rendered frame; false for every
    /// subsequent call in the same frame. Use to guard DismantleAction handlers that
    /// can fire through multiple Harmony patches (PerformStackActionRoutine + ActionRoutine).
    /// Initialize the state field to -1.
    /// <para>NOT safe for a handler that must run once per CARD of a stack action. EA 0.68a's
    /// default inline coroutine runner runs a stack's cards 2..N (and a dropped stack's cards
    /// onto one receiver) inside card 1's frame, so this gate lets card 1 through and drops
    /// the rest. ActionRouter hit exactly that and now keys its dedup on (frame, card, given
    /// card): see ActionRouter.ClaimDispatch and tracker row T2.260. Key on the card as well
    /// whenever a stack can reach the guarded handler. No caller in this repo uses this gate
    /// (checked 2026-09-23).</para>
    /// </summary>
    public static bool OncePerFrame(ref int lastFrame)
    {
        int now = Time.frameCount;
        if (lastFrame == now) return false;
        lastFrame = now;
        return true;
    }

    /// <summary>
    /// True when the game's DayTimePoints value has changed since the last call
    /// (one DTP = 15 in-game minutes; 96 per day, counting down). False while the
    /// DTP is unchanged or the game is not initialized yet. Initialize the state
    /// field to <c>int.MinValue</c>; the first observed tick primes the gate without firing.
    /// </summary>
    public static bool OncePerDtpTick(ref int lastDtp)
    {
        int dtp = GameQuery.DayTimePoints;
        if (dtp < 0) return false;
        if (lastDtp == int.MinValue) { lastDtp = dtp; return false; }
        if (dtp == lastDtp) return false;
        lastDtp = dtp;
        return true;
    }

    /// <summary>
    /// True once per in-game day rollover (DTP wrap from ~0 back up to ~96 — the same
    /// detection TriggerService and WildlifeRaidService use). Initialize the state
    /// field to <c>int.MinValue</c>; the first observed value primes the gate without firing.
    /// </summary>
    public static bool OncePerDayRollover(ref int lastDtp)
    {
        int dtp = GameQuery.DayTimePoints;
        if (dtp < 0) return false;
        if (lastDtp == int.MinValue) { lastDtp = dtp; return false; }
        bool wrapped = dtp > lastDtp + 50;
        lastDtp = dtp;
        return wrapped;
    }
}
