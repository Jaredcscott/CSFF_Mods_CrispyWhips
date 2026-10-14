using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using UnityEngine;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Village Guards — chase urgency (owner-directed, 2026-08-15: "we need to fix pursuit too").
    ///
    /// Root cause of "guards never overtake me," confirmed via decompile + a direct graph analysis
    /// of <c>WorldMap/MapNodes.json</c>, not guessed: a chasing guard moves at EXACTLY the player's
    /// own speed. <c>InGameNPC.UpdateTick</c> — the only thing that advances a duty's current
    /// action — fires once per DTP tick, and DTP ticks are a side effect of SOME action (almost
    /// always the player's own) spending daytime points (.decomp/GameManager.cs:4283/5482/5823).
    /// A guard's <c>MoveDutyAction</c> step (<c>MoveDestination: MoveToPlayer</c>) advances exactly
    /// one environment node per such tick (<c>MoveDutyAction.PickDestinationAndMoveThere</c>,
    /// .decomp/MoveDutyAction.cs:151-169) — the same rate the player themselves moves at.
    ///
    /// The <c>cmcTagVillageTerritory</c> the guards patrol/chase within (<c>GuardDutyPatch.
    /// VillageTerritoryCardUids</c>) is a fully CLOSED graph with exactly one exit, already locked
    /// while Banished (confirmed this session by enumerating every <c>Connections[]</c> entry —
    /// an earlier research pass's "11 open exits" theory was checked and found false). But a closed
    /// graph does not by itself guarantee a catch: it contains THREE overlapping induced 4-node
    /// cycles (cmcEnvVillagePath-cmcEnvHighGrove-cmcEnvPineTrail-cmcEnvHuntersCrossing;
    /// cmcEnvVillagePath-cmcEnvHighGrove-cmcEnvMossyClearing-cmcEnvClayFlats;
    /// cmcEnvClayFlats-cmcEnvMossyClearing-cmcEnvForagingForest-cmcEnvMarshHollow — verified via a
    /// Python adjacency dump of the same JSON, no chord/shortcut in any of the three), and classical
    /// pursuit-evasion graph theory is unambiguous that a SINGLE pursuer moving at exactly the
    /// evader's own speed can never force a catch on any chordless cycle of length >= 4 — the
    /// evader just keeps circling. Guard Iris Vane, the sole night-shift chaser, is exactly this
    /// single-pursuer case. Thorne and Corrin (day shift, two simultaneous chasers) are not
    /// mathematically doomed the same way, but their independently-selected, uncoordinated "beeline
    /// straight at the player's current position" duties don't implement the coordinated pincer
    /// strategy the 2-pursuer graph-theory guarantee actually requires either.
    ///
    /// Fix: give any guard whose currently-PERFORMING duty is one of the three chase duties one
    /// extra <c>InGameNPC.StartOrUpdateCurrentDuty()</c> pass per real-time poll interval, on top of
    /// whatever the engine's own DTP-tick cadence already grants — a genuine speed edge over the
    /// player, which trivially breaks the equal-speed cycle-evasion problem regardless of topology
    /// or coordination. Confirmed via decompile that this is NOT a no-op: <c>MoveDutyAction.
    /// UpdateDutyAction</c> (.decomp/MoveDutyAction.cs:172-180) unconditionally re-invokes
    /// <c>PickDestinationAndMoveThere</c> every single call, which for <c>MoveToPlayer</c> wipes the
    /// cached target and re-paths fresh against the player's live position every time
    /// (.decomp/MoveDutyAction.cs:89-92) — so each extra call is a real, fresh-targeted node-hop,
    /// not a repeat of stale progress.
    ///
    /// Same reflection + <c>StartCoroutine</c> idiom already proven in <see cref="GuardDutyPatch"/>.
    /// <see cref="GuardDutyPatch.OnAttackWitnessed"/> — private-method invocation via reflection,
    /// started and never synchronously drained (reference_synchronous_coroutine_drain_freeze).
    /// Guarded on <c>SelectedNPCDuty.IsBeingUpdated</c> (set true for the whole duration of a
    /// <c>StartOrUpdateCurrentDuty</c> pass, .decomp/InGameNPC.cs:1550) so this never races the
    /// engine's own per-tick call to the identical coroutine, and on <c>!IsInCombat</c> — the engine
    /// itself skips duty progression entirely once a fight has started
    /// (.decomp/InGameNPC.cs:1480), and this patch must not interfere with an ongoing Encounter.
    /// </summary>
    internal static class GuardChaseUrgencyPatch
    {
        /// <summary>Real-time seconds between bonus pursuit passes — matches the cadence of the
        /// other periodic guard polls in this mod (GuardDutyDiagnostics 10s, ConnectionGateService
        /// 5s, GuardDutyBuild 2s); fast enough to matter, not so fast it floods the coroutine
        /// scheduler with redundant re-path calls.</summary>
        private const float PollIntervalSeconds = 3f;

        /// <summary>The three chase duties built by <see cref="GuardDutyPatch"/>'s BuildChaseDuty —
        /// deliberately NOT Sterling's summon-response duties (<c>Duty_SterlingRespondToSummon</c>/
        /// <c>Duty_SterlingFinishArrest</c>/<c>Duty_SterlingForceArrest</c>): those already close
        /// distance the instant they're armed and aren't part of the cycle-evasion problem this
        /// patch targets, and speeding up an already-summoned Captain wasn't asked for.</summary>
        private static readonly string[] ChaseDutyUids =
        {
            "cmcGuardThorneChase_Duty",
            "cmcGuardCorrinChase_Duty",
            "cmcGuardVaneChase_Duty",
        };

        private static bool _initialized;
        private static MethodInfo _startOrUpdateMethod;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            TickEvents.DtpTick += () => _ticks++;
            TickEvents.Interval(PollIntervalSeconds, Run, "GuardChaseUrgency");
            Plugin.Logger.LogDebug("[GuardChaseUrgencyPatch] initialized.");
        }

        // DTP ticks seen this session, so "stuck for N ticks" is measured in game time (a paused game polls forever).
        private static int _ticks;
        private static readonly Dictionary<object, int> _freshSince = new Dictionary<object, int>();
        private static MethodInfo _cancelMethod;
        private const int StuckTicks = 3;

        /// <summary>
        /// A chase duty that sits at step -1 with its first action never started for several game ticks is
        /// wedged: the engine only advances a fresh duty in the tick it was selected, and only re-selects a
        /// duty whose required actions are doable from the CURRENT step (-1 reads as not doable), so the guard
        /// holds the duty, is never re-selected and never updated again (harness T4.28: Iris Vane after the
        /// player's first arrest, 'NOT SELECTABLE - Required actions are not doable' every tick with a valid
        /// path). Cancelling it through the engine's own CancelCurrentDuty clears the slot so the next tick
        /// selects the duty afresh.
        /// </summary>
        private static void HealStuckDuty(object npc, MonoBehaviour npcBehaviour, object gm, object currentDuty, string dutyName)
        {
            if (Reflect.GetBool(currentDuty, "ActionStarted")) { _freshSince.Remove(npc); return; }
            if (!(Reflect.GetMember(currentDuty, "CurrentActionIndex") is int step) || step >= 0) { _freshSince.Remove(npc); return; }
            if (!_freshSince.TryGetValue(npc, out int since)) { _freshSince[npc] = _ticks; return; }
            if (_ticks - since < StuckTicks) return;

            _cancelMethod ??= npc.GetType().GetMethod("CancelCurrentDuty", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_cancelMethod == null)
            {
                Plugin.Logger.LogWarning("[GuardChaseUrgencyPatch] InGameNPC.CancelCurrentDuty not found - a wedged chase duty cannot be cleared.");
                return;
            }
            _freshSince.Remove(npc);
            if (_cancelMethod.Invoke(npc, null) is IEnumerator cancel)
            {
                (gm as MonoBehaviour ?? npcBehaviour).StartCoroutine(cancel);
                Plugin.Logger.LogWarning($"[GuardChaseUrgencyPatch] '{dutyName}' sat unstarted for {StuckTicks}+ ticks (wedged); cancelled it so the guard selects it afresh.");
            }
        }

        private static void Run()
        {
            // Guards have no duties to be mid-chase on before GuardDutyPatch finishes building
            // them — same readiness gate GuardSpawnPatch itself waits on.
            if (!GuardDutyPatch.DutiesReady) return;

            try
            {
                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;
                if (Reflect.GetMember(gm, "AllNPCs") is not IEnumerable allNpcs) return;

                foreach (var npc in allNpcs)
                {
                    if (npc is not MonoBehaviour npcBehaviour) continue;
                    if (Reflect.GetBool(npc, "IsInCombat")) continue;

                    var currentDuty = Reflect.GetMember(npc, "CurrentDuty");
                    if (currentDuty == null) continue;

                    var duty = Reflect.GetMember(currentDuty, "Duty");
                    string dutyName = duty == null ? null : Reflect.GetMember(duty, "name") as string;
                    if (dutyName == null || Array.IndexOf(ChaseDutyUids, dutyName) < 0) continue;

                    // The engine's own tick (or an earlier poll of ours still in flight) is
                    // already mid-pass on this exact coroutine — skip rather than race it.
                    if (Reflect.GetBool(currentDuty, "IsBeingUpdated")) continue;

                    // SelectedNPCDuty.CurrentAction is null while the step index sits outside the duty's
                    // ActionSequence (reset to -1 around a duty ending or an encounter starting), and the
                    // engine's StartOrUpdateCurrentDuty dereferences it unguarded: a bonus pass started in
                    // that window threw a NullReferenceException (harness T4.28, 2026-10-13, from the first
                    // pass after the chase's encounter). The engine's own per-tick pass is already past
                    // that window by the time it runs, so skipping is always safe.
                    var currentAction = Reflect.GetMember(currentDuty, "CurrentAction");
                    if (currentAction == null || (currentAction is UnityEngine.Object actionObj && !actionObj))
                    {
                        HealStuckDuty(npc, npcBehaviour, gm, currentDuty, dutyName);
                        Plugin.Logger.LogDebug($"[GuardChaseUrgencyPatch] '{dutyName}' has no current action this poll; no bonus step.");
                        continue;
                    }

                    _startOrUpdateMethod ??= npc.GetType()
                        .GetMethod("StartOrUpdateCurrentDuty", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (_startOrUpdateMethod == null)
                    {
                        Plugin.Logger.LogWarning("[GuardChaseUrgencyPatch] InGameNPC.StartOrUpdateCurrentDuty not found — chase urgency inactive.");
                        return;
                    }

                    if (_startOrUpdateMethod.Invoke(npc, null) is not IEnumerator routine) continue;
                    // Run it on the GameManager, as the engine's own calls do (GM.StartCoroutineEx), NOT on the NPC's
                    // component: StartOrUpdateCurrentDuty raises SelectedNPCDuty.IsBeingUpdated for its whole run and only
                    // lowers it at the end, so a coroutine owned by the NPC's GameObject that is killed mid-pass (the
                    // object goes inactive when the player changes environment, e.g. on being arrested) leaves the flag
                    // up for good and the guard's duty never advances again: Iris Vane never raised the alarm after the
                    // player's first arrest (harness T4.28 DAY leg, found 2026-10-13).
                    var runner = gm as MonoBehaviour ?? npcBehaviour;
                    runner.StartCoroutine(routine);
                    Plugin.Logger.LogDebug($"[GuardChaseUrgencyPatch] '{dutyName}' gets a bonus pursuit step.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[GuardChaseUrgencyPatch] Run failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }
    }
}
