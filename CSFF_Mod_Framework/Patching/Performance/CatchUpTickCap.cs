using System;
using BepInEx.Configuration;
using HarmonyLib;

namespace CSFFModFramework.Patching.Performance;

// GameManager.ChangeEnvironment "catches up" the destination environment by
// replaying vanilla's per-game-tick simulation step (ApplyRates) once for EVERY
// tick elapsed since that environment was last visited — one tick = 15 in-game
// minutes, 96 per day — with NO upper bound, and the loop drains synchronously
// (ApplyRates finishes without yielding a real frame in catch-up mode). On a
// late-game save, re-entering a location last visited a year ago replays
// ~38,000 ticks at ~2 ms each: a 70+ second hard freeze on one frame (measured
// 2026-08-09 via TrackingTimingDiagnostics; see
// Documentation/Retrospectives/travel-changeenvironment-freeze-2026-08-09.md).
//
// This prefix clamps EnvironmentsData[NextEnvironment].LastUpdatedTick forward
// before the coroutine body reads it, bounding the loop to the most recent N
// ticks. Simulation older than the cap window is skipped entirely rather than
// replayed. That is behaviorally safe for almost everything:
//   * spoilage/fuel/evaporation rates saturate well within the default 14-day
//     window (food is fully rotted / fires are long dead either way);
//   * card-attached counters (tree/plant growth) do not crawl tick-by-tick in
//     catch-up mode — vanilla jumps them straight to the live global counter
//     value on the first catch-up tick (ApplyRates' Updated-counter branch),
//     so they fast-forward correctly no matter how large the skipped gap is.
// The fidelity loss is rate-driven decay/production beyond the window on boards
// left un-visited longer than the cap. Set the cap to 0 to restore vanilla's
// unbounded replay.
//
// LastUpdatedTick is written only by the EnvironmentSaveDataByReference
// constructor (stamped when the player leaves an environment) and read only by
// the catch-up loop and its cooking companion (LastCookingUpdateCatchupTick),
// so the forward clamp cannot affect any other system. First-ever visits have
// no EnvironmentsData entry and skip catch-up entirely — TryGetValue guards.
internal static class CatchUpTickCap
{
    private static int _maxTicks;

    public static void Configure(ConfigFile config, Harmony harmony)
    {
        var capCfg = config.Bind(
            "Performance", "CatchUpTickCap", 1344,
            "Maximum number of elapsed game ticks (15 in-game minutes each, 96/day) "
            + "ChangeEnvironment may re-simulate when entering an environment. "
            + "Default 1344 = 14 in-game days. Prevents the multi-second-to-minute "
            + "'Not Responding' freeze when traveling to a location not visited in a "
            + "long time on old saves. Elapsed time beyond the cap is skipped, not "
            + "simulated (plant/tree growth still fast-forwards via vanilla's counter "
            + "jump; only rate-driven decay beyond the window is lost). "
            + "0 = vanilla unbounded catch-up.");
        _maxTicks = capCfg.Value;
        if (_maxTicks <= 0)
        {
            Util.Log.Debug("CatchUpTickCap: disabled via config (vanilla unbounded catch-up).");
            return;
        }

        var prefix = new HarmonyMethod(AccessTools.Method(typeof(CatchUpTickCap), nameof(ChangeEnvironment_Prefix)));
        bool ok = SafePatcher.TryPatch(harmony, typeof(GameManager), "ChangeEnvironment", prefix: prefix);
        if (ok)
            Util.Log.Debug($"CatchUpTickCap: enabled (max {_maxTicks} catch-up ticks per travel).");
        else
            Util.Log.Warn("CatchUpTickCap: failed to patch GameManager.ChangeEnvironment; "
                          + "travels to long-unvisited environments will freeze as before.");
    }

    // Runs when ChangeEnvironment() is invoked, before the coroutine body.
    // NextEnvironment is already set by the travel initiator at that point, and
    // nothing between here and the catch-up loop touches the DESTINATION env's
    // save data — the leave-block only rewrites the CURRENT env's entry.
    private static void ChangeEnvironment_Prefix(GameManager __instance)
    {
        if (_maxTicks <= 0 || __instance == null) return;
        try
        {
            // Mirror vanilla's own gate: the CurrentEnvironment.IsNull path
            // (initial placement on load) exits before any catch-up runs —
            // leave the destination's data untouched there.
            if (__instance.CurrentEnvironment.IsNull) return;
            EnvID next = __instance.NextEnvironment;
            if (next.IsNull || __instance.EnvironmentsData == null) return;
            if (!__instance.EnvironmentsData.TryGetValue(next.DictionnaryKey, out var envData) || envData == null) return;

            int now = __instance.CurrentTickInfo.z;
            int gap = now - envData.LastUpdatedTick;
            if (gap <= _maxTicks) return;

            envData.LastUpdatedTick = now - _maxTicks;
            Util.Log.Info($"CatchUpTickCap: '{next}' was {gap} ticks (~{gap / 96} in-game days) behind; "
                          + $"simulating the most recent {_maxTicks}, fast-forwarding past {gap - _maxTicks}.");
        }
        catch (Exception ex)
        {
            Util.Log.Warn($"CatchUpTickCap: clamp failed, travel proceeds uncapped: {ex}");
        }
    }
}
