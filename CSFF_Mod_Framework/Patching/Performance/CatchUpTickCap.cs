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
// replayed. Decay/production ONLY ever happens during a replay window — an
// unvisited board's rate-driven stats (spoilage, fuel, evaporation) do not move
// at all between visits, so "the cap" is the entirety of the decay a stale board
// will ever receive, not a floor under decay that also happens some other way.
// Within the default 1344-tick (14-day) window, every ordinary perishable and
// structure/cave timer fully saturates: raw food ≤3.1d, cooked food ≤7d, dried
// meat 8.8d, bread/pies/cave timers 10.4–11.2d. Long-shelf-life preserved goods
// (hard cheese 60d, hardtack/grains/wine 120d) advance only partially within the
// window BY DESIGN — no finite cap could saturate them without discarding weeks
// of intended gameplay pacing, and there is nothing between 1344 and the next
// tier (5760) for a larger cap to usefully reach anyway. Card-attached counters
// (tree/plant growth) do not crawl tick-by-tick in catch-up mode — vanilla jumps
// them straight to the live global counter value on the first catch-up tick
// (ApplyRates' Updated-counter branch), so they fast-forward correctly no matter
// how large the skipped gap is. Set the cap to 0 to restore vanilla's unbounded
// replay (CatchUpBudgetClamp/CatchUpTickBatching below stay independently
// toggleable either way — see the hand-off note in the prefix).
//
// LastUpdatedTick is written only by the EnvironmentSaveDataByReference
// constructor (stamped when the player leaves an environment) and read only by
// the catch-up loop and its cooking companion (LastCookingUpdateCatchupTick),
// so the forward clamp cannot affect any other system. First-ever visits have
// no EnvironmentsData entry and skip catch-up entirely — TryGetValue guards.
internal static class CatchUpTickCap
{
    private static int _maxTicks;
    private static int _chainCapTicks;
    private static float _chainWindowSeconds;

    // Session-local only (never touches save data); -1 = no clamp has fired yet
    // this session.
    private static float _lastCapHitRealtime = -1f;

    public static void Configure(ConfigFile config, Harmony harmony)
    {
        var capCfg = config.Bind(
            "Performance", "CatchUpTickCap", 1344,
            "Maximum number of elapsed game ticks (15 in-game minutes each, 96/day) "
            + "ChangeEnvironment may re-simulate when entering an environment. Default "
            + "1344 = 14 in-game days, the smallest cap that fully saturates every "
            + "ordinary perishable and structure/cave timer (raw food ≤3.1d, cooked "
            + "food ≤7d, dried meat 8.8d, bread/pies/cave timers 10.4-11.2d). Decay "
            + "ONLY ever happens during a replay window — an unvisited board does not "
            + "decay at all between visits — so this cap is the entire decay a stale "
            + "board will ever receive, not a floor under some other always-on decay. "
            + "Long-shelf-life preserved goods (hard cheese 60d, hardtack/grains/wine "
            + "120d) advance only partially within the window by design; lowering the "
            + "cap below 1344 widens that under-decayed class further (672 also stops "
            + "fully rotting dried meat/bread/cave timers; 384 also stops fully "
            + "rotting cooked food; 192 leaves raw meat/fish/fruit permanently ~80% "
            + "fresh on any long absence — not recommended). Plant/tree growth "
            + "counters always fast-forward to their live value regardless of this "
            + "setting; only rate-driven decay/production beyond the cap is skipped. "
            + "0 = vanilla unbounded catch-up (CatchUpBudgetMs and CatchUpBatchTicks "
            + "below still apply independently even when this is 0).");
        _maxTicks = capCfg.Value;

        var chainCapCfg = config.Bind(
            "Performance", "CatchUpChainCapTicks", 672,
            "When traveling through several long-unvisited environments in quick "
            + "succession (a 'chain' of capped hops), every hop after the first pays "
            + "the full CatchUpTickCap replay again. If the PREVIOUS hop in this chain "
            + "also clamped within CatchUpChainWindowSeconds, this hop uses this "
            + "smaller cap instead — 672 = 7 in-game days, which still fully rots raw "
            + "food but stops short of dried meat/bread/cave timers on that one hop. "
            + "The skipped decay on a discounted pass-through board is permanent (it "
            + "will not be replayed later), but is always player-favorable (things are "
            + "fresher than they should be, never destroyed). 0 disables the discount "
            + "(every hop always pays the full CatchUpTickCap).");
        _chainCapTicks = chainCapCfg.Value;

        var chainWindowCfg = config.Bind(
            "Performance", "CatchUpChainWindowSeconds", 180f,
            "How long after a capped hop the CatchUpChainCapTicks discount stays "
            + "armed for the next hop. Resets every time a hop clamps; a hop more than "
            + "this many real seconds after the last clamp always pays the full cap.");
        _chainWindowSeconds = chainWindowCfg.Value;

        if (_maxTicks <= 0)
            Util.Log.Debug("CatchUpTickCap: cap disabled via config (vanilla unbounded catch-up); "
                          + "CatchUpBudgetMs/CatchUpBatchTicks remain independently active if configured.");

        // The prefix is installed even when the cap itself is disabled (_maxTicks
        // <= 0) — it also hands off to CatchUpBudgetClamp and CatchUpTickBatching,
        // which have their own independent on/off configs and must stay reachable
        // regardless of whether this particular cap is armed.
        var prefix = new HarmonyMethod(AccessTools.Method(typeof(CatchUpTickCap), nameof(ChangeEnvironment_Prefix)));
        bool ok = SafePatcher.TryPatch(harmony, typeof(GameManager), "ChangeEnvironment", prefix: prefix);
        if (ok)
            Util.Log.Debug($"CatchUpTickCap: enabled (max {_maxTicks} catch-up ticks per travel, "
                          + $"chain discount {_chainCapTicks} within {_chainWindowSeconds}s).");
        else
            Util.Log.Warn("CatchUpTickCap: failed to patch GameManager.ChangeEnvironment; "
                          + "travels to long-unvisited environments will freeze as before, and the "
                          + "budget-clamp/batching extensions will not run either.");
    }

    // Runs when ChangeEnvironment() is invoked, before the coroutine body.
    // NextEnvironment is already set by the travel initiator at that point, and
    // nothing between here and the catch-up loop touches the DESTINATION env's
    // save data — the leave-block only rewrites the CURRENT env's entry.
    private static void ChangeEnvironment_Prefix(GameManager __instance)
    {
        if (__instance == null) return;
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

            if (_maxTicks > 0)
            {
                int gap = now - envData.LastUpdatedTick;
                if (gap > _maxTicks)
                {
                    int effectiveCap = _maxTicks;
                    if (_chainCapTicks > 0 && _lastCapHitRealtime >= 0f
                        && Time.realtimeSinceStartup - _lastCapHitRealtime <= _chainWindowSeconds)
                    {
                        effectiveCap = Math.Min(_maxTicks, _chainCapTicks);
                    }
                    _lastCapHitRealtime = Time.realtimeSinceStartup;

                    envData.LastUpdatedTick = now - effectiveCap;
                    Util.Log.Info($"CatchUpTickCap: '{next}' was {gap} ticks (~{gap / 96} in-game days) behind; "
                                  + $"simulating the most recent {effectiveCap}, fast-forwarding past {gap - effectiveCap}.");
                }
            }

            // Hand off to the composable catch-up extensions. Both run
            // regardless of whether the hard cap above is enabled or fired
            // this hop — CatchUpBudgetClamp and CatchUpTickBatching each have
            // their own independent 0-disables config and must stay reachable
            // even with CatchUpTickCap itself set to 0 (vanilla unbounded).
            CatchUpBudgetClamp.ArmForHop(envData);
            CatchUpTickBatching.ArmForHop(envData, now);
        }
        catch (Exception ex)
        {
            Util.Log.Warn($"CatchUpTickCap: clamp failed, travel proceeds uncapped: {ex}");
        }
    }
}
