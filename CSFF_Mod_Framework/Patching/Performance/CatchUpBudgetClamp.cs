using System.Diagnostics;
using BepInEx.Configuration;
using HarmonyLib;

namespace CSFFModFramework.Patching.Performance;

// Adaptive companion to CatchUpTickCap: bounds a single ChangeEnvironment
// catch-up replay by WALL-CLOCK time instead of a fixed tick count, so replay
// depth self-tunes to the actual machine/save instead of always paying the
// same worst case. CatchUpTickCap.ChangeEnvironment_Prefix stashes the
// resolved destination envData into this class via ArmForHop right after it
// applies its own cap/chain-discount clamp; a plain (non-IEnumerator) prefix
// on the private GameManager.ApplyRates stub then fires once per replayed
// tick (it's a stub prefix, not an IEnumerator-postfix, so the IEnumerator
// timing pitfalls in CLAUDE.md do not apply here — same shape already proven
// by TrackingTimingDiagnostics' ApplyRates_Prefix).
//
// Mechanically safe because GameManager.ChangeEnvironment's catch-up loop
// re-reads EnvironmentsData[envKey].LastUpdatedTick on every iteration for
// BOTH the loop bound and the tick argument (.decomp/GameManager.cs:10238/
// 10240) — raising it here from outside makes the loop's own next condition
// check false, so the loop exits after the in-flight tick. Never LOWER
// LastUpdatedTick (would repeat a tick number and double-apply cooking via
// UpdateCookingRecipes' LastCookingUpdateCatchupTick equality-dedup guard).
//
// A single ChangeEnvironment call is never re-entrant (the whole engine is
// single-threaded and the catch-up loop drains synchronously within one
// frame — see the sync-drain note in CatchUpTickCap.cs / memory
// reference_synchronous_coroutine_drain_freeze), so there is exactly one
// "armed" hop in flight at a time; the reference-identity check below is a
// belt-and-suspenders guard against a future refactor breaking that
// invariant, not a currently-reachable race.
internal static class CatchUpBudgetClamp
{
    private static int _budgetMs;
    private static int _minTicks;

    private static readonly Stopwatch Sw = new();
    private static EnvironmentSaveDataByReference _armedEnvData;
    private static int _ticksThisHop;

    // Cached reflection into GameManager's private CatchingUpEnvData field, used
    // only as an extra reference-identity guard (per CSFFModFramework/CLAUDE.md:
    // cache AccessTools.Field lookups rather than re-resolving per call).
    private static FieldInfo _catchingUpEnvDataField;

    public static void Configure(ConfigFile config, Harmony harmony)
    {
        var budgetCfg = config.Bind(
            "Performance", "CatchUpBudgetMs", 2000,
            "Wall-clock budget (milliseconds) for a single ChangeEnvironment catch-up "
            + "replay, on top of CatchUpTickCap's fixed tick ceiling. Once a replay has "
            + "run this long AND simulated at least CatchUpMinTicks, the remaining "
            + "elapsed-but-unreplayed ticks for that hop are skipped (same class of "
            + "fidelity loss CatchUpTickCap already accepts, just adaptive to machine "
            + "speed and save weight). Self-tunes: on a light save this rarely binds "
            + "and full CatchUpTickCap fidelity is kept; on a heavy save it clamps "
            + "replay depth to whatever fits the budget. Trade-off: replay depth (and "
            + "therefore exact decay amount) becomes machine-load-dependent instead of "
            + "deterministic. 0 disables the budget clamp entirely.");
        _budgetMs = budgetCfg.Value;

        var minTicksCfg = config.Bind(
            "Performance", "CatchUpMinTicks", 384,
            "Floor under CatchUpBudgetMs: never cut a replay shorter than this many "
            + "ticks even if the budget has elapsed, so raw food/milk/fruit/cooked meat "
            + "(all ≤300-tick saturation windows) always fully rot on any visit long "
            + "enough to trigger catch-up at all.");
        _minTicks = minTicksCfg.Value;

        if (_budgetMs <= 0)
        {
            Util.Log.Debug("CatchUpBudgetClamp: disabled via config (CatchUpBudgetMs=0).");
            return;
        }

        _catchingUpEnvDataField = AccessTools.Field(typeof(GameManager), "CatchingUpEnvData");

        // Postfix, not prefix: Harmony guarantees every prefix on a method runs
        // before any postfix, regardless of patch owner/order — so this always
        // observes CatchUpTickBatching's CurrentChunkTicks for the call that
        // just ran, rather than racing its own prefix against batching's for
        // "who sets the chunk size first" (both patch the same ApplyRates
        // method; a plain postfix on an iterator stub fires at essentially the
        // same instant as a prefix would — right around the trivial stub call,
        // long before the returned enumerator's body actually executes — so
        // moving this to a postfix costs nothing timing-wise while removing the
        // ordering hazard).
        var postfix = new HarmonyMethod(AccessTools.Method(typeof(CatchUpBudgetClamp), nameof(ApplyRates_Postfix)));
        bool ok = SafePatcher.TryPatch(harmony, typeof(GameManager), "ApplyRates", postfix: postfix);
        if (ok)
            Util.Log.Debug($"CatchUpBudgetClamp: enabled (budget {_budgetMs}ms, floor {_minTicks} ticks).");
        else
            Util.Log.Warn("CatchUpBudgetClamp: failed to patch GameManager.ApplyRates; "
                          + "catch-up replay will not be wall-clock bounded.");
    }

    // Called from CatchUpTickCap.ChangeEnvironment_Prefix, once per travel,
    // right after the fixed-tick cap/chain-discount decision for this hop.
    internal static void ArmForHop(EnvironmentSaveDataByReference envData)
    {
        if (_budgetMs <= 0) { _armedEnvData = null; return; }
        _armedEnvData = envData;
        _ticksThisHop = 0;
        Sw.Restart();
    }

    private static void ApplyRates_Postfix(GameManager __instance)
    {
        if (_armedEnvData == null || __instance == null || !__instance.IsCatchingUp) return;

        // Reference-identity guard: only act while this call's actual catching-up
        // env data is the one we armed for (see class remarks — not currently
        // reachable to differ, kept as a hard safety net).
        if (_catchingUpEnvDataField != null
            && !ReferenceEquals(_catchingUpEnvDataField.GetValue(__instance), _armedEnvData))
        {
            return;
        }

        // CurrentChunkTicks is 1 when CatchUpTickBatching is off/unarmed, or the
        // real-tick-equivalent size of the chunk that just ran otherwise — keeps
        // CatchUpMinTicks meaning "real ticks" even once batching compresses
        // many ticks into one ApplyRates call (a flat +1 here would make the
        // floor permanently unreachable at the K=16 default, ~84 calls per
        // capped hop instead of ~1344).
        _ticksThisHop += CatchUpTickBatching.CurrentChunkTicks;
        if (_ticksThisHop < _minTicks || Sw.ElapsedMilliseconds <= _budgetMs) return;

        int now = __instance.CurrentTickInfo.z;
        if (now > _armedEnvData.LastUpdatedTick) // never lower it
        {
            _armedEnvData.LastUpdatedTick = now;
            Util.Log.Info($"CatchUpBudgetClamp: replay exceeded {_budgetMs}ms after {_ticksThisHop} ticks; "
                          + "stopping this hop's catch-up early.");
        }
        _armedEnvData = null; // one cutoff per hop is enough; avoid re-logging every remaining tick
    }
}
