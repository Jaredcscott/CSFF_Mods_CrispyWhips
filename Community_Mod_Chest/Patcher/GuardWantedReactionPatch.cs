using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using UnityEngine;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Wanted-tier escalate-on-sight (Village_Master_Plan.md §10.8.2 Wanted band, 25-59) — one of
    /// two follow-ups the 2026-08-06 pass explicitly deferred (the other is Captain Sterling's
    /// one-time warning line, pure JSON: GameStat/CMC_SterlingWarningGiven.json +
    /// ScriptableObject/DialogScene/CMC_SterlingTalk.json).
    ///
    /// While the player's crime is in the Wanted band, any of our four guards who shares the
    /// player's CURRENT environment reacts short of pursuit: that guard's own Suspicion NPCStat
    /// (cmcNpcStatSuspicion, §10.8.1's per-guard detection tuning knob) rises, feeding the
    /// existing detection modifiers. This is deliberately NOT an Encounter and NOT the chase duty
    /// — those stay gated at Banished (>=60), GuardDutyPatch.CrimeGateCondition. Captain
    /// Sterling's dialog additionally carries a player-visible hostile line at this same band
    /// (CMC_SterlingTalk_Wanted) — this class owns only the mechanical, silent half.
    ///
    /// Reuses GuardWitnessPatch.FindGuardsAt (built for the witnessed-attack check, §10.8.4.2)
    /// rather than re-scanning AllNPCs a second way — that class already owns "which of our four
    /// guards is standing in environment X."
    ///
    /// Polled, not event-driven: unlike an attack there is no discrete "incident" moment for
    /// merely standing near a guard, so this reuses the same TickEvents.Interval 5s poll idiom as
    /// VillageChroniclePatch/VillageReputationPatch.
    ///
    /// Per-guard, per-sighting latch, not per-tick: a guard's Suspicion bumps once when the
    /// player is first seen in the Wanted band, then waits until either the player leaves that
    /// guard's environment or the player drops out of the Wanted band before reacting again —
    /// otherwise a player standing still would be bumped every 5s indefinitely.
    ///
    /// Suspicion is written the same way every other IEnumerator-backed stat change in this
    /// codebase is (root CLAUDE.md "Runtime Stat Change Hook"): InGameNPC.ChangeStat(
    /// NPCStatInstantModifier, ...) returns an IEnumerator that must be STARTED, never
    /// synchronously drained (reference_synchronous_coroutine_drain_freeze) — on the guard's own
    /// MonoBehaviour, same idiom GuardDutyPatch.OnAttackWitnessed already uses to force an
    /// immediate CheckForDuties re-check.
    /// </summary>
    internal static class GuardWantedReactionPatch
    {
        private const float SuspicionBumpAmount = 5f;

        private static bool _initialized;
        private static Type _inGameNPCType, _npcStatInstantModifierType, _uidType;
        private static MethodInfo _changeStatMethod, _getFromIdMethod;
        private static object _suspicionStat;
        private static bool _loggedTypeFailure;

        /// <summary>Guards currently latched as "already reacted to this sighting."</summary>
        private static readonly HashSet<string> _reactedGuardUids = new(StringComparer.OrdinalIgnoreCase);

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            TickEvents.Interval(5f, RunTick, "GuardWantedReaction");
            Plugin.Logger.LogDebug("[GuardWantedReactionPatch] initialized.");
        }

        private static void RunTick()
        {
            try
            {
                if (VillageCrimePatch.CurrentBand() != VillageCrimePatch.CrimeBand.Wanted)
                {
                    _reactedGuardUids.Clear();
                    return;
                }

                var playerEnv = GameQuery.CurrentEnvironmentUniqueId;
                if (string.IsNullOrEmpty(playerEnv)) return;

                var witnesses = GuardWitnessPatch.FindGuardsAt(playerEnv);
                if (witnesses.Count == 0) return;

                if (!ResolveTypes()) return;
                if (!ResolveSuspicionStat()) return;

                var stillPresent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var witness in witnesses)
                {
                    stillPresent.Add(witness.GuardAgentUid);
                    if (_reactedGuardUids.Contains(witness.GuardAgentUid)) continue;
                    if (BumpSuspicion(witness)) _reactedGuardUids.Add(witness.GuardAgentUid);
                }
                // Drop the latch for any guard no longer co-located, so a later re-sighting reacts again.
                _reactedGuardUids.RemoveWhere(uid => !stillPresent.Contains(uid));
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[GuardWantedReactionPatch] RunTick failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static bool ResolveTypes()
        {
            if (_changeStatMethod != null) return true;

            _inGameNPCType = CardUtil.FindGameType("InGameNPC");
            _npcStatInstantModifierType = CardUtil.FindGameType("NPCStatInstantModifier");
            _uidType = CardUtil.FindGameType("UniqueIDScriptable");
            if (_inGameNPCType == null || _npcStatInstantModifierType == null || _uidType == null)
            {
                if (!_loggedTypeFailure)
                {
                    _loggedTypeFailure = true;
                    Plugin.Logger.LogWarning("[GuardWantedReactionPatch] Could not resolve InGameNPC/NPCStatInstantModifier/UniqueIDScriptable — Wanted-tier reaction inactive.");
                }
                return false;
            }

            _changeStatMethod = _inGameNPCType
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(m => m.Name == "ChangeStat" && m.GetParameters().Length == 4
                    && m.GetParameters()[0].ParameterType == _npcStatInstantModifierType);
            _getFromIdMethod = _uidType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "GetFromID" && !m.IsGenericMethodDefinition
                    && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));

            if (_changeStatMethod == null || _getFromIdMethod == null)
            {
                if (!_loggedTypeFailure)
                {
                    _loggedTypeFailure = true;
                    Plugin.Logger.LogWarning("[GuardWantedReactionPatch] InGameNPC.ChangeStat(NPCStatInstantModifier,...) or UniqueIDScriptable.GetFromID not found — Wanted-tier reaction inactive.");
                }
                return false;
            }
            return true;
        }

        private static bool ResolveSuspicionStat()
        {
            if (_suspicionStat != null) return true;
            _suspicionStat = _getFromIdMethod.Invoke(null, new object[] { GuardDutyPatch.GuardSuspicionStatUid });
            return _suspicionStat != null;
        }

        /// <summary>
        /// Starts (never synchronously drains) the guard's own ChangeStat coroutine with a +5
        /// instant Suspicion modifier. Returns true only once the coroutine is actually started,
        /// so a failed cast/invoke does not falsely latch the per-sighting cooldown.
        /// </summary>
        private static bool BumpSuspicion(GuardWitnessPatch.Witness witness)
        {
            if (witness?.GuardNpc is not MonoBehaviour npcBehaviour) return false;
            try
            {
                object modifier = Activator.CreateInstance(_npcStatInstantModifierType);
                Reflect.SetMember(modifier, "TargetStat", _suspicionStat);
                Reflect.SetMember(modifier, "ValueChange", new Vector2(SuspicionBumpAmount, SuspicionBumpAmount));
                Reflect.SetMember(modifier, "IgnoreNovelty", true);

                int tick = 0;
                var gm = CardUtil.GetGameManagerInstance();
                if (gm != null && Reflect.GetMember(gm, "CurrentTickInfo") is Vector3Int tickInfo) tick = tickInfo.z;

                if (_changeStatMethod.Invoke(witness.GuardNpc, new object[] { modifier, null, "GuardWantedReaction", tick })
                    is not IEnumerator routine)
                    return false;

                npcBehaviour.StartCoroutine(routine);
                Plugin.Logger.LogInfo($"[GuardWantedReactionPatch] {witness.GuardLabel} spots a Wanted face nearby — Suspicion +{SuspicionBumpAmount:0}.");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[GuardWantedReactionPatch] BumpSuspicion failed for {witness.GuardLabel}: {ex.InnerException?.ToString() ?? ex.ToString()}");
                return false;
            }
        }
    }
}
