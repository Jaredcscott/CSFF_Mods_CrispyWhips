using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
/// Also reports a monotonic per-call index and GC.CollectionCount(0/1/2) +
/// GC.GetTotalMemory deltas around each ChangeEnvironment call, to test
/// whether cost climbs across a session independent of catch-up-tick count
/// (session 5 of thicketpine-north-loadtimes-2026-08-24.md found a same-env
/// repeat visit with a near-identical catch-up-tick count costing ~3x more
/// CPU than the first visit — not explained by catch-up replay or
/// CheckForTracks, both measured cheap that session).
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
    private static int _changeEnvCallIndex;

    // Catch-up performance plan Phase 0 (shipped 2.25.15; that phase has since been pruned from
    // Documentation/Plans/CSFFModFramework/CatchUp_Performance_Plan.md - see CHANGELOG [2.25.15]):
    // per-phase
    // sub-timers (LoadCardSet, UpdatePassiveEffects — each can fire multiple times per single
    // ChangeEnvironment call, so these are accumulators snapshotted before/after like
    // _applyRatesCount) plus an AllCards-by-mod-prefix census, to test whether
    // AlwaysUpdateService's blanket AlwaysUpdate=true on every mod card (AlwaysUpdateService.cs:72)
    // is what's inflating AllCards on heavily-modded saves.
    private static long _loadCardSetCpuTicksAccum;
    private static int _loadCardSetCallCount;
    private static long _updatePassiveEffectsCpuTicksAccum;
    private static int _updatePassiveEffectsCallCount;

    public static void Configure(BepInEx.Configuration.ConfigFile config, Harmony harmony)
    {
        var enabled = config.Bind("Diagnostics", "LogTrackTiming", false,
            "When true, logs CPU time spent in EnvironmentSaveDataByReference.CheckForTracks "
            + "and GameManager.ChangeEnvironment on every travel, tagged with the "
            + "destination environment, plus the number "
            + "of GameManager.GiveCard calls that fired during CheckForTracks and "
            + "the number of GameManager.ApplyRates catch-up ticks + resulting "
            + "AllCards.Count during ChangeEnvironment, plus sub-timers for LoadCardSet and "
            + "UpdatePassiveEffects and an AllCards census broken down by owning mod (with "
            + "AlwaysUpdate/IndependentFromEnv counts per mod). Use to diagnose long "
            + "location-load times when entering an environment with fresh tracks, "
            + "a large catch-up gap, or a large modded AllCards population. Off by default.");
        if (!enabled.Value) return;

        var checkForTracksPost = new HarmonyMethod(
            AccessTools.Method(typeof(TrackingTimingDiagnostics), nameof(CheckForTracks_Postfix)));
        var changeEnvPost = new HarmonyMethod(
            AccessTools.Method(typeof(TrackingTimingDiagnostics), nameof(ChangeEnvironment_Postfix)));
        var giveCardPre = new HarmonyMethod(
            AccessTools.Method(typeof(TrackingTimingDiagnostics), nameof(GiveCard_Prefix)));
        var applyRatesPre = new HarmonyMethod(
            AccessTools.Method(typeof(TrackingTimingDiagnostics), nameof(ApplyRates_Prefix)));
        var loadCardSetPost = new HarmonyMethod(
            AccessTools.Method(typeof(TrackingTimingDiagnostics), nameof(LoadCardSet_Postfix)));
        var updatePassiveEffectsPost = new HarmonyMethod(
            AccessTools.Method(typeof(TrackingTimingDiagnostics), nameof(UpdatePassiveEffects_Postfix)));

        // NOTE: the live gameplay dictionary is GameManager.EnvironmentsData,
        // typed Dictionary<EnvDictKey, EnvironmentSaveDataByReference> — a
        // separate, unrelated class from EnvironmentSaveData (same method name/
        // signature, no shared base type). Patching "EnvironmentSaveData" finds
        // and patches a real method (SafePatcher reports success), but that
        // class's CheckForTracks is never invoked by actual travel, so the
        // postfix silently never logs. Confirmed 2026-08-24: a live diagnostic
        // session logged 10 ChangeEnvironment entries and zero CheckForTracks
        // entries despite the patch reporting "enabled". See
        // Documentation/Retrospectives/thicketpine-north-loadtimes-2026-08-24.md.
        bool a = SafePatcher.TryPatch(harmony, "EnvironmentSaveDataByReference", "CheckForTracks", postfix: checkForTracksPost);
        bool b = SafePatcher.TryPatch(harmony, "GameManager", "ChangeEnvironment", postfix: changeEnvPost);
        bool c = SafePatcher.TryPatch(harmony, "GameManager", "GiveCard", prefix: giveCardPre);
        bool d = SafePatcher.TryPatch(harmony, "GameManager", "ApplyRates", prefix: applyRatesPre);
        // LoadCardSet is private (5-param overload, unambiguous); UpdatePassiveEffects is public.
        bool e = SafePatcher.TryPatch(harmony, "GameManager", "LoadCardSet", postfix: loadCardSetPost);
        bool f = SafePatcher.TryPatch(harmony, typeof(GameManager), "UpdatePassiveEffects", postfix: updatePassiveEffectsPost);

        Util.Log.Info($"TrackingTimingDiagnostics: enabled (CheckForTracks={a}, ChangeEnvironment={b}, GiveCard={c}, ApplyRates={d}, "
                      + $"LoadCardSet={e}, UpdatePassiveEffects={f}). Travel between locations to produce timing entries in this log.");
    }

    // __instance is read here (not inside the while loop) because this whole
    // method is itself the compiled iterator body — none of it runs until the
    // wrapper's first MoveNext(), which happens synchronously when the caller
    // starts the coroutine, before anything else can touch the env identity.
    // Same timing this class already relies on for the Interlocked counters.
    static IEnumerator CheckForTracks_Postfix(IEnumerator result, EnvironmentSaveDataByReference __instance)
    {
        string envLabel = __instance?.DictionaryKey ?? "?";
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
            Util.Log.Info($"CheckForTracks[{envLabel}]: CPU {ms:F1}ms across {steps} step(s), GiveCard×{spawned}");
        }
    }

    // GC.CollectionCount / GetTotalMemory are read around the whole call (not
    // per MoveNext step) — added 2026-08-24 to test a session-progressive-cost
    // hypothesis: the confirmed WolfPassQEE repeat-visit (same env, ~identical
    // catch-up-tick count, ~3x CPU cost on the second visit) can't be explained
    // by catch-up replay or CheckForTracks (both ruled out/measured cheap that
    // session — see thicketpine-north-loadtimes-2026-08-24.md Session 5). If
    // Gen0/1/2 counts or heap size climb call-over-call independent of
    // catch-up-tick count, that points at GC pressure accumulating over the
    // session as the real driver instead.
    static IEnumerator ChangeEnvironment_Postfix(IEnumerator result, GameManager __instance)
    {
        string envLabel = "?";
        try { envLabel = __instance != null ? __instance.NextEnvironment.ToString() : "?"; }
        catch (Exception ex) { Util.Log.Debug($"TrackingTimingDiagnostics: could not read NextEnvironment: {ex.Message}"); }
        int callIndex = Interlocked.Increment(ref _changeEnvCallIndex);
        Interlocked.Increment(ref _inChangeEnvironment);
        int ratesBefore = Volatile.Read(ref _applyRatesCount);
        long loadCardSetTicksBefore = Volatile.Read(ref _loadCardSetCpuTicksAccum);
        int loadCardSetCallsBefore = Volatile.Read(ref _loadCardSetCallCount);
        long updatePassiveTicksBefore = Volatile.Read(ref _updatePassiveEffectsCpuTicksAccum);
        int updatePassiveCallsBefore = Volatile.Read(ref _updatePassiveEffectsCallCount);
        int gen0Before = GC.CollectionCount(0);
        int gen1Before = GC.CollectionCount(1);
        int gen2Before = GC.CollectionCount(2);
        long memBefore = GC.GetTotalMemory(false);
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
            int gen0Delta = GC.CollectionCount(0) - gen0Before;
            int gen1Delta = GC.CollectionCount(1) - gen1Before;
            int gen2Delta = GC.CollectionCount(2) - gen2Before;
            long memDeltaKb = (GC.GetTotalMemory(false) - memBefore) / 1024;
            double loadCardSetMs = (Volatile.Read(ref _loadCardSetCpuTicksAccum) - loadCardSetTicksBefore) * 1000.0 / Stopwatch.Frequency;
            int loadCardSetCalls = Volatile.Read(ref _loadCardSetCallCount) - loadCardSetCallsBefore;
            double updatePassiveMs = (Volatile.Read(ref _updatePassiveEffectsCpuTicksAccum) - updatePassiveTicksBefore) * 1000.0 / Stopwatch.Frequency;
            int updatePassiveCalls = Volatile.Read(ref _updatePassiveEffectsCallCount) - updatePassiveCallsBefore;
            // Phase 3 (CatchUpTickBatching) reduces ApplyRates call COUNT by grouping real ticks
            // into chunks — once armed, "×N" below is chunk count, not real-tick count, and reading
            // it as "only N ticks of decay were simulated" would understate the actual replay window
            // by up to K×. Flag it rather than silently mislabeling (fleet-overhead explorer finding,
            // 2026-08-25) — CatchUpTickBatching.Armed is a stable, already-built property, safe to
            // read here regardless of that class's own in-flight work elsewhere.
            string batchNote = Performance.CatchUpTickBatching.Armed
                ? " [BATCHED: × counts chunks, not real ticks]" : "";
            Util.Log.Info($"ChangeEnvironment#{callIndex}[{envLabel}]: CPU {ms:F1}ms across {steps} step(s), "
                          + $"ApplyRates(catch-up ticks)×{catchupTicks}{batchNote}, AllCards={cardCount}, "
                          + $"GC(gen0/1/2)={gen0Delta}/{gen1Delta}/{gen2Delta}, HeapDelta={memDeltaKb}KB, "
                          + $"LoadCardSet={loadCardSetMs:F1}ms×{loadCardSetCalls}, UpdatePassiveEffects={updatePassiveMs:F1}ms×{updatePassiveCalls}");
            try { Util.Log.Info(BuildAllCardsCensus(__instance)); }
            catch (Exception ex) { Util.Log.Debug($"TrackingTimingDiagnostics: census log failed: {ex.Message}"); }
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

    // Sub-timers for the floor-cost explorer's "itemize the ~360-420ms fixed per-travel floor"
    // question (Documentation/Plans/Fleet/Game_Performance_Options_2026-08-25.md, floor-cost
    // option 1). Both LoadCardSet and UpdatePassiveEffects can fire more than once per single
    // ChangeEnvironment call (UpdatePassiveEffects runs at LoadCardSet:3674 AND ChangeEnvironment
    // :10230 per the survey), so these accumulate — ChangeEnvironment_Postfix snapshots
    // before/after totals the same way it already does for _applyRatesCount.
    static IEnumerator LoadCardSet_Postfix(IEnumerator result)
    {
        long cpuTicks = 0;
        try
        {
            while (true)
            {
                bool hasMore;
                var sw = Stopwatch.StartNew();
                try { hasMore = result.MoveNext(); }
                finally { sw.Stop(); cpuTicks += sw.ElapsedTicks; }
                if (!hasMore) break;
                yield return result.Current;
            }
        }
        finally
        {
            Interlocked.Add(ref _loadCardSetCpuTicksAccum, cpuTicks);
            Interlocked.Increment(ref _loadCardSetCallCount);
        }
    }

    static IEnumerator UpdatePassiveEffects_Postfix(IEnumerator result)
    {
        long cpuTicks = 0;
        try
        {
            while (true)
            {
                bool hasMore;
                var sw = Stopwatch.StartNew();
                try { hasMore = result.MoveNext(); }
                finally { sw.Stop(); cpuTicks += sw.ElapsedTicks; }
                if (!hasMore) break;
                yield return result.Current;
            }
        }
        finally
        {
            Interlocked.Add(ref _updatePassiveEffectsCpuTicksAccum, cpuTicks);
            Interlocked.Increment(ref _updatePassiveEffectsCallCount);
        }
    }

    // AllCards-by-mod-prefix census. Opt-in-only, O(AllCards) — acceptable since this whole class
    // is gated behind [Diagnostics] LogTrackTiming=false by default. Tests AlwaysUpdateService's
    // blanket AlwaysUpdate=true (AlwaysUpdateService.cs:72) as the mods-off-A/B-puzzle candidate:
    // if fleet mods dominate AllCards, that's the case for Phase 5's scoped AlwaysUpdate diet.
    static string BuildAllCardsCensus(GameManager gm)
    {
        try
        {
            if (gm == null) return "AllCards census unavailable (no GameManager)";
            var byMod = new Dictionary<string, (int total, int alwaysUpdate, int independent)>(StringComparer.OrdinalIgnoreCase);
            var allCards = gm.AllCards;
            for (int i = 0; i < allCards.Count; i++)
            {
                var card = allCards[i];
                if (!card || !card.CardModel) continue;
                string uid = card.CardModel.UniqueID;
                string modName = (!string.IsNullOrEmpty(uid)
                                   && Loading.JsonDataLoader.UniqueIdToModName.TryGetValue(uid, out var m))
                    ? m : "vanilla";
                bool alwaysUpdate = card.CardModel.AlwaysUpdate;
                bool independent;
                try { independent = card.IndependentFromEnv; }
                catch (Exception ex)
                {
                    Util.Log.Debug($"TrackingTimingDiagnostics: IndependentFromEnv read failed for '{uid}': {ex.Message}");
                    independent = false;
                }
                byMod.TryGetValue(modName, out var tally);
                byMod[modName] = (tally.total + 1, tally.alwaysUpdate + (alwaysUpdate ? 1 : 0), tally.independent + (independent ? 1 : 0));
            }
            var ordered = byMod.OrderByDescending(kv => kv.Value.total)
                                .Select(kv => $"{kv.Key}={kv.Value.total}(AlwaysUpdate={kv.Value.alwaysUpdate},IndependentFromEnv={kv.Value.independent})");
            return $"AllCards census: {allCards.Count} total | " + string.Join(" | ", ordered);
        }
        catch (Exception ex)
        {
            Util.Log.Debug($"TrackingTimingDiagnostics: AllCards census failed: {ex.Message}");
            return "AllCards census failed (see debug log)";
        }
    }
}
