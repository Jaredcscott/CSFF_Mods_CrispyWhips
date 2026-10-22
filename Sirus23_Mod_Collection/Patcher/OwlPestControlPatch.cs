using System;
using System.Collections.Generic;
using BepInEx.Logging;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace Sirus23ModCollection.Patcher;

/// <summary>
/// N3, the owl as pest control (Lever B of Documentation/Research/Sirus23_Mod_Collection/owl_pest_control_spike_2026-10.md):
/// while the Owl Companion is on the player's current board, vanilla mouse raids come about half as often.
///
/// Vanilla's raid timer is the player stat MouseDamageCounter (GUID c5bcafedf20fb85449fc13c1344ea044, range 0..192,
/// base rate +1 per tick plus a self-rate status of +4..+8 while it reads 1..191). When it reaches 192 the
/// SelfTriggeredAction Tgr_MouseRaid rolls a raid ("Mouse" against "Nothing", weighted by Pop_Mouse) and takes 192 off
/// the counter. The stat sets DoesNotAffectPassiveEffects, so a card passive on it is a silent no-op; this is the C# route.
///
/// Once per DTP tick (TickEvents.DtpTick) the patch reads the counter and, with the owl on the board, gives back
/// <see cref="OwlCounterCut"/> of the climb since the last tick through StatAccess.Modify (vanilla's own permanent
/// ChangeStatValue, so the stat's statuses update). The counter still climbs at half speed, so a raid roll still comes,
/// half as often: rarer, not never. Raid severity (Pop_Mouse) is untouched; the spike says pick ONE lever first.
///
/// "On the player's board" is GameQuery.CardsInPlayerEnv (every AllCards card whose CardEnvironment.MatchesPlayerEnv),
/// the same test OwlRoostPatch uses: an owl following the player at night, or one told to Stay Here at the camp the
/// player is standing in. An owl left on another board, or roosting in a cave by day, is not in that list, so it cuts
/// nothing: the stat is the player's, and the owl must be beside the player for the cut to apply. A climb measured
/// across a board change is never cut (the baseline re-primes while the game is travelling).
///
/// Registered once per process; per-run state is cleared the first tick that sees a new GameManager.
/// </summary>
internal static class OwlPestControlPatch
{
    private const string MouseRaidCounterUid = "c5bcafedf20fb85449fc13c1344ea044";   // vanilla MouseDamageCounter
    private const float RaidCounterTop = 192f;

    // BALANCE PLACEHOLDER (tunable, not balanced): the share of each tick's raid-counter climb the owl gives back.
    // 0.5 = raids roll about half as often while the owl is on the board.
    internal const float OwlCounterCut = 0.5f;

    private static ManualLogSource Logger => Plugin.Logger;

    private static readonly HashSet<string> _warned = new(StringComparer.Ordinal);
    private static bool _subscribed;

    // Per-run state, cleared when a new GameManager is first seen.
    private static int _runId;
    private static float _last = float.NaN;    // the counter at the last tick, NaN = re-prime on the next tick
    private static bool _owlWasHere;
    private static float _cutSinceArrival;
    private static int _ticksSinceArrival;

    internal static void Register()
    {
        if (_subscribed) return;
        _subscribed = true;
        TickEvents.DtpTick += OnTick;
    }

    private static void WarnOnce(string cause, string message)
    {
        if (_warned.Add(cause)) Logger?.LogWarning($"[OwlPestControl] {message} Logged once per cause.");
    }

    private static void OnTick()
    {
        try
        {
            if (CardUtil.GetGameManagerInstance() is not GameManager gm)
            {
                _last = float.NaN;   // silent-ok: no run (DtpTick does not fire before one starts)
                return;
            }
            ResetIfNewRun(gm);

            if (CardUtil.GetMemberValue(gm, "EnvironmentTransition") is true || CardUtil.GetMemberValue(gm, "LeavingEnvironment") is true)
            {
                _last = float.NaN;   // travelling: re-prime afterwards, never cut a climb measured across a board change
                return;
            }
            if (gm.CurrentEnvironment.EnvCard == null)
            {
                WarnOnce("no-env", "the player's environment has no card yet; the owl's pest control waits for it.");
                _last = float.NaN;
                return;
            }

            float value = StatAccess.Get(MouseRaidCounterUid);
            if (float.IsNaN(value))
            {
                WarnOnce("read", $"the vanilla mouse raid counter (MouseDamageCounter, {MouseRaidCounterUid}) could not be read (StatAccess's own warning says why), so the owl does not hold mouse raids back.");
                _last = float.NaN;
                return;
            }

            bool owlHere = OwlOnPlayerBoard();
            if (owlHere != _owlWasHere)
            {
                if (owlHere)
                {
                    _cutSinceArrival = 0f;
                    _ticksSinceArrival = 0;
                    Logger?.LogInfo($"[OwlPestControl] The Owl Companion is on your board: mouse raids come about half as often while it stays (the raid counter keeps {1f - OwlCounterCut:P0} of its climb; counter {value:0.#}/{RaidCounterTop:0}).");
                }
                else
                {
                    Logger?.LogInfo($"[OwlPestControl] The Owl Companion is no longer on your board: the mouse raid counter climbs at the vanilla rate again (the owl held it back by {_cutSinceArrival:0.#} over {_ticksSinceArrival} tick(s); counter {value:0.#}/{RaidCounterTop:0}).");
                }
                _owlWasHere = owlHere;
            }

            float prev = _last;
            _last = value;
            if (float.IsNaN(prev)) return;   // silent-ok: first reading after a load, a travel or a failure primes the baseline

            float climb = value - prev;
            if (climb < 0f)
            {
                // Tgr_MouseRaid takes 192 off the counter when it rolls, so a drop is a raid roll (a raid or "Nothing").
                Logger?.LogInfo($"[OwlPestControl] A mouse raid roll fired (counter {prev:0.#} -> {value:0.#}); Owl Companion on your board: {(owlHere ? "yes" : "no")}.");
                return;
            }
            if (climb == 0f || !owlHere) return;   // silent-ok: no climb this tick, or no owl to hold it back
            // silent-ok: the counter has reached the top, so let the roll fire. The stat clamps at 192, so near the top
            // each tick's climb is cut short by the clamp; giving back half of that short climb every tick only ever
            // halves the gap and the counter never reaches 192 (measured 2026-10-20, T2.585 rig 8: 189.57, 190.79,
            // 191.39 ... 191.98), which stopped raids entirely while the owl stayed. The cut only slows the climb below it.
            if (value >= RaidCounterTop - 0.001f) return;

            float cut = climb * OwlCounterCut;
            float after = StatAccess.Modify(MouseRaidCounterUid, -cut);
            if (float.IsNaN(after))
            {
                WarnOnce("write", $"the mouse raid counter could not be lowered (StatAccess's own warning says why), so the owl does not hold mouse raids back.");
                _last = float.NaN;
                return;
            }
            _last = after;
            _cutSinceArrival += cut;
            _ticksSinceArrival++;
            Logger?.LogDebug($"[OwlPestControl] owl on board: raid counter climbed {climb:0.##}, gave back {cut:0.##}, now {after:0.##}/{RaidCounterTop:0}.");
        }
        catch (Exception ex)
        {
            WarnOnce("tick:" + ex.GetType().Name, $"the pest-control tick threw {ex.GetType().Name}: {ex.InnerException?.ToString() ?? ex.ToString()}");
            _last = float.NaN;
        }
    }

    private static void ResetIfNewRun(GameManager gm)
    {
        int id = gm.GetInstanceID();
        if (id == _runId) return;
        _runId = id;
        _last = float.NaN;
        _owlWasHere = false;
        _cutSinceArrival = 0f;
        _ticksSinceArrival = 0;
    }

    // The owl on the player's own board only: GameQuery.CardsInPlayerEnv is the AllCards scan paired with the
    // environment test (CardEnvironment.MatchesPlayerEnv), so an owl on another board or in a cave is never counted.
    private static bool OwlOnPlayerBoard()
    {
        foreach (var c in GameQuery.CardsInPlayerEnv())
        {
            if (c is InGameCardBase cb && cb && !cb.Destroyed && CardUtil.GetCardUniqueId(c) == WolfTickPatch.OwlId)
                return true;
        }
        return false;
    }
}
