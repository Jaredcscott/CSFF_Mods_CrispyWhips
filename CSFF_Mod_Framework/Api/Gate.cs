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
        // No run (main menu, loading): forget the last reading, so the next run's first value
        // primes the gate instead of being compared with the previous run's last one (a night
        // save followed by a morning load used to fire a phantom rollover).
        if (dtp < 0) { lastDtp = int.MinValue; return false; }
        if (lastDtp == int.MinValue) { lastDtp = dtp; return false; }
        if (dtp == lastDtp) return false;
        lastDtp = dtp;
        return true;
    }

    /// <summary>
    /// True once per in-game day rollover (the same detection TriggerService, TickEvents and
    /// WildlifeRaidService use; see <see cref="DaysRolledOver"/>). Initialize the state field to
    /// <c>int.MinValue</c>; the first observed value primes the gate without firing. The field
    /// now holds a day number, not a DayTimePoints value; callers only pass it back in.
    /// </summary>
    public static bool OncePerDayRollover(ref int lastDtp) => DaysRolledOver(ref lastDtp) > 0;

    /// <summary>
    /// Number of in-game days that began since the last call (0 on most calls). The state field
    /// holds the last observed <c>GameManager.CurrentDay</c>, which vanilla's NightRoutine
    /// increments exactly once per day as it resets DayTimePoints. Before 2.26.8 the rollover was
    /// inferred from DayTimePoints jumping up by more than 50 between two polls, which cannot see a
    /// day that began inside a gap of about 46 or more ticks (11.5 in-game hours) between two polls,
    /// since the jump then reads 50 or less, and cannot count two days in one gap.
    /// </summary>
    internal static int DaysRolledOver(ref int lastDay)
    {
        // No run (main menu, loading): forget the last reading, so the next run's first value
        // primes the gate instead of being compared with the previous run's last one.
        if (GameQuery.DayTimePoints < 0) { lastDay = int.MinValue; return 0; }
        int day = GameQuery.CurrentDay;
        if (day <= 0) return 0;
        if (lastDay == int.MinValue) { lastDay = day; return 0; }
        int rolled = day - lastDay;
        lastDay = day;
        return rolled > 0 ? rolled : 0;
    }
}
