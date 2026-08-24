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
            TickEvents.Interval(PollIntervalSeconds, Run, "GuardChaseUrgency");
            Plugin.Logger.LogDebug("[GuardChaseUrgencyPatch] initialized.");
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

                    _startOrUpdateMethod ??= npc.GetType()
                        .GetMethod("StartOrUpdateCurrentDuty", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (_startOrUpdateMethod == null)
                    {
                        Plugin.Logger.LogWarning("[GuardChaseUrgencyPatch] InGameNPC.StartOrUpdateCurrentDuty not found — chase urgency inactive.");
                        return;
                    }

                    if (_startOrUpdateMethod.Invoke(npc, null) is not IEnumerator routine) continue;
                    npcBehaviour.StartCoroutine(routine);
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
