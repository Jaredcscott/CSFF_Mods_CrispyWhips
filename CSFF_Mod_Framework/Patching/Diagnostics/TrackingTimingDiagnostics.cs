using System;
using System.Collections;
using System.Diagnostics;
using System.Threading;
using HarmonyLib;

namespace CSFFModFramework.Patching.Diagnostics;

/// <summary>
/// Opt-in diagnostic for investigating slow location-loads when tracks spawn.
/// Wraps the two coroutines on the travel path with a Stopwatch and reports
/// CPU time spent inside each (summed across MoveNext calls — wall-clock would
/// include yielded frames where Unity does unrelated work). Also counts
/// GameManager.GiveCard invocations that fire while CheckForTracks is on the
/// stack, so we can tell how many cards were spawned per call.
///
/// Also counts GameManager.ApplyRates(int) invocations that fire while
/// ChangeEnvironment is on the stack, and reports the post-transition
/// AllCards.Count. ApplyRates is vanilla's per-elapsed-game-tick "catch up"
/// simulation step (one call per tick since the destination environment was
/// last visited) — an environment not visited in a long time can require
/// hundreds of catch-up ticks, each re-running every card's stat/passive
/// processing. A high ApplyRates count paired with a high CPU time confirms
/// the catch-up loop (not card-count/load-time) is where the seconds go;
/// a low count with high CPU and a high card count points at LoadCardSet /
/// the AllCards classification pass instead. See CLAUDE.md's Coroutine Timing
/// notes and memory: project_travel_freeze_perf_toggles_disabled.
///
/// Off by default. Enable via BepInEx config:
///   [Diagnostics] LogTrackTiming = true
/// </summary>
internal static class TrackingTimingDiagnostics
{
    private static int _inCheckForTracks;
    private static int _giveCardCount;
    private static int _inChangeEnvironment;
    private static int _applyRatesCount;

    public static void Configure(BepInEx.Configuration.ConfigFile config, Harmony harmony)
    {
        var enabled = config.Bind("Diagnostics", "LogTrackTiming", false,
            "When true, logs CPU time spent in EnvironmentSaveData.CheckForTracks "
            + "and GameManager.ChangeEnvironment on every travel, plus the number "
            + "of GameManager.GiveCard calls that fired during CheckForTracks and "
            + "the number of GameManager.ApplyRates catch-up ticks + resulting "
            + "AllCards.Count during ChangeEnvironment. Use to diagnose long "
            + "location-load times when entering an environment with fresh tracks "
            + "or a large catch-up gap. Off by default.");
        if (!enabled.Value) return;

        var checkForTracksPost = new HarmonyMethod(
            AccessTools.Method(typeof(TrackingTimingDiagnostics), nameof(CheckForTracks_Postfix)));
        var changeEnvPost = new HarmonyMethod(
            AccessTools.Method(typeof(TrackingTimingDiagnostics), nameof(ChangeEnvironment_Postfix)));
        var giveCardPre = new HarmonyMethod(
            AccessTools.Method(typeof(TrackingTimingDiagnostics), nameof(GiveCard_Prefix)));
        var applyRatesPre = new HarmonyMethod(
            AccessTools.Method(typeof(TrackingTimingDiagnostics), nameof(ApplyRates_Prefix)));

        bool a = SafePatcher.TryPatch(harmony, "EnvironmentSaveData", "CheckForTracks", postfix: checkForTracksPost);
        bool b = SafePatcher.TryPatch(harmony, "GameManager", "ChangeEnvironment", postfix: changeEnvPost);
        bool c = SafePatcher.TryPatch(harmony, "GameManager", "GiveCard", prefix: giveCardPre);
        bool d = SafePatcher.TryPatch(harmony, "GameManager", "ApplyRates", prefix: applyRatesPre);

        Util.Log.Info($"TrackingTimingDiagnostics: enabled (CheckForTracks={a}, ChangeEnvironment={b}, GiveCard={c}, ApplyRates={d}). "
                      + "Travel between locations to produce timing entries in this log.");
    }

    static IEnumerator CheckForTracks_Postfix(IEnumerator result)
    {
        Interlocked.Increment(ref _inCheckForTracks);
        int spawnsBefore = Volatile.Read(ref _giveCardCount);
        long cpuTicks = 0;
        int steps = 0;
        try
        {
            while (true)
            {
                bool hasMore;
                var sw = Stopwatch.StartNew();
                try { hasMore = result.MoveNext(); }
                finally { sw.Stop(); cpuTicks += sw.ElapsedTicks; }
                steps++;
                if (!hasMore) break;
                yield return result.Current;
            }
        }
        finally
        {
            Interlocked.Decrement(ref _inCheckForTracks);
            int spawned = Volatile.Read(ref _giveCardCount) - spawnsBefore;
            double ms = cpuTicks * 1000.0 / Stopwatch.Frequency;
            Util.Log.Info($"CheckForTracks: CPU {ms:F1}ms across {steps} step(s), GiveCard×{spawned}");
        }
    }

    static IEnumerator ChangeEnvironment_Postfix(IEnumerator result)
    {
        Interlocked.Increment(ref _inChangeEnvironment);
        int ratesBefore = Volatile.Read(ref _applyRatesCount);
        long cpuTicks = 0;
        int steps = 0;
        try
        {
            while (true)
            {
                bool hasMore;
                var sw = Stopwatch.StartNew();
                try { hasMore = result.MoveNext(); }
                finally { sw.Stop(); cpuTicks += sw.ElapsedTicks; }
                steps++;
                if (!hasMore) break;
                yield return result.Current;
            }
        }
        finally
        {
            Interlocked.Decrement(ref _inChangeEnvironment);
            int catchupTicks = Volatile.Read(ref _applyRatesCount) - ratesBefore;
            double ms = cpuTicks * 1000.0 / Stopwatch.Frequency;
            int cardCount = -1;
            try { cardCount = MBSingleton<GameManager>.Instance ? MBSingleton<GameManager>.Instance.AllCards.Count : -1; }
            catch (Exception ex) { Util.Log.Debug($"TrackingTimingDiagnostics: could not read AllCards.Count: {ex.Message}"); }
            Util.Log.Info($"ChangeEnvironment: CPU {ms:F1}ms across {steps} step(s), "
                          + $"ApplyRates(catch-up ticks)×{catchupTicks}, AllCards={cardCount}");
        }
    }

    static void GiveCard_Prefix()
    {
        if (Volatile.Read(ref _inCheckForTracks) > 0)
            Interlocked.Increment(ref _giveCardCount);
    }

    static void ApplyRates_Prefix()
    {
        if (Volatile.Read(ref _inChangeEnvironment) > 0)
            Interlocked.Increment(ref _applyRatesCount);
    }
}
