using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using UnityEngine;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Miller/Weaver daytime "at work" duty — the engine <c>NPCDuty</c>/<c>MoveDutyAction</c>
    /// chassis (same reflection-built pattern as <c>GuardDutyPatch</c>), NOT the hand-rolled
    /// hour-clock <c>CottageResidentSchedulePatch</c> uses for their Cottage(night)/Inn(evening)/
    /// Academy(weekly) legs.
    ///
    /// Owner request (2026-08-21): give Miller/Weaver a real "at work" state during the day,
    /// like the Guards' patrol, instead of the old cosmetic <c>Wander</c> roll to generic outdoor
    /// nodes (no profession-specific action ever fired there). Built as a SEPARATE engine duty
    /// rather than folded into the hand-rolled system, matching the established split — duty-
    /// building always lives apart from the hour-clock/portrait scheduler that consumes it
    /// (<c>GuardDutyPatch.cs</c> vs. nothing that plays the guards' portraits/dialog; this mod's
    /// own <c>AshPartnerDutyPatch.cs</c> is the single-companion precedent for the same split).
    ///
    /// <para><strong>Hard constraint this duty is built around:</strong> the engine's
    /// <c>MoveDutyAction.FindDestination</c> skips any <c>MoveToEnvironments</c> entry whose card
    /// has <c>InstancedEnvironment</c> set (.decomp/MoveDutyAction.cs:380/408/779/822), and its
    /// non-instanced path resolves only through <c>WorldMapData.GetOneStepPath</c>, which only
    /// knows WorldMap nodes — exactly the same limitation <c>GuardDutyPatch.BuildWardenDuty</c>'s
    /// own doc comment describes for why a guard can stand only OUTSIDE the jail, never inside it.
    /// Miller/Weaver's cottage INTERIORS (used for their nighttime sleep leg) are non-instanced
    /// but still not WorldMap nodes, so this duty can only ever target <c>cmcEnvVillage</c> itself
    /// — exactly where their cottage CT2 structures physically sit. The nighttime interior-hop
    /// leg needs the hand-rolled system's own tile-walker and stays there, untouched.</para>
    ///
    /// <para><strong>Coexistence with <see cref="CottageResidentSchedulePatch"/>:</strong> two
    /// independent pollers (this file's engine duty-selection tick vs. that file's real-time
    /// <c>TickEvents.Interval</c>) could in principle both try to move the same NPC. Resolved by
    /// removing the contention at the source rather than relying on duty weight: that file's
    /// <c>Dest.Work</c> case is a no-op — during this duty's active hours, THIS is the only
    /// caller of <c>MoveNPC</c> for these two residents. The one seam needing explicit gating is
    /// the resident's one weekly Academy day: <c>ValidTimesOfDay</c> can only express fixed hour
    /// windows (<c>HourIs</c>), not day-of-week, so it cannot itself exclude "not my Academy day."
    /// <see cref="WorkerBeat.AcademyTodayStat"/> — a plain GameStat written once/tick by
    /// <c>CottageResidentSchedulePatch.RunScheduler</c> for the resident's ENTIRE Academy day, not
    /// just the visit window — closes that gap via this duty's own <c>DutyConditions</c>.</para>
    ///
    /// Reload-safety is identical to <c>GuardDutyPatch</c>: these duties are not in the game's UID
    /// table, so <c>ActivatingMode = ActivateAutomatically</c> (never <c>AlwaysActive</c> — that
    /// branch is skipped by the per-tick reactivation loop, .decomp/InGameNPC.cs:2026-2031) is
    /// what brings the duty back after a load. <see cref="CottageResidentSpawnPatch"/> gates every
    /// Miller/Weaver spawn/restore call site on <see cref="DutiesReady"/>, the same way
    /// <c>GuardSpawnPatch</c> gates on <c>GuardDutyPatch.DutiesReady</c> — <c>InGameNPC.Init</c>
    /// snapshots <c>AgentDuties</c> once, so a resident created one tick early would silently
    /// never work again for the session.
    /// </summary>
    internal static class CottageResidentWorkDutyPatch
    {
        private sealed class WorkerBeat
        {
            public string Label;             // log label only
            public string AgentUid;
            public string DutyUid;
            public string DutyDisplayText;   // NPCDuty.DutyName, shown in the NPC inspection UI
            public string AcademyTodayStatUid;

            public object Agent;             // NPCAgent, resolved lazily
            public object AcademyTodayStat;  // GameStat SO, resolved lazily
        }

        private static readonly WorkerBeat[] Beats =
        {
            new WorkerBeat
            {
                Label = "Miller", AgentUid = "cmcMillerAgent", DutyUid = "cmcMillerWork_Duty",
                DutyDisplayText = "Grinding grain", AcademyTodayStatUid = "cmcStatMillerAcademyToday",
            },
            new WorkerBeat
            {
                Label = "Weaver", AgentUid = "cmcWeaverAgent", DutyUid = "cmcWeaverWork_Duty",
                DutyDisplayText = "Working the loom", AcademyTodayStatUid = "cmcStatWeaverAcademyToday",
            },
        };

        // Where "at work" physically is — same node the Guards already patrol, and where the
        // Miller/Weaver cottage CT2 structures (their grindstone/loom, via
        // ContainedBlueprintCardsWarpData) sit. See the class doc for why this can't be the
        // cottage interior instead.
        private const string WorkPostEnvUid = "cmcEnvVillage";
        private const int WorkStartHour = 6;
        private const int WorkEndHour = 18; // ends exactly when CottageResidentSchedulePatch's guaranteed Inn visit begins
        // Only duty on either agent — value is inconsequential, EXCEPT that it must stay > 0 (see
        // reference_npcduty_authoring_footguns). internal so QuietVillagePerkPatch can restore this
        // exact value after suppressing it for the "Quiet Village" perk, instead of duplicating the
        // literal and risking drift.
        internal const int WorkBaseWeight = 20;

        private static bool _initialized;
        private static bool _dutiesBuilt;
        private static object _villageEnvCard; // CardData

        // Reflection handles, resolved once: NpcDuties owns NPCDuty/NPCDutyAction/MoveDutyAction/
        // NPCDutyRef/GeneralCondition/NPCDutyWeights/LocalizedString/InGameTimeCondition construction
        // now; only the destination CardData array type and this file's own DutyTags/gate-stat types
        // still need resolving here.
        private static Type _uidType, _cardDataType, _npcDutyActionType, _npcDutyTagType, _statValueTriggerType;
        private static MethodInfo _getFromIdMethod;

        /// <summary>
        /// True once every resident's AgentDuties array has been attached. CottageResidentSpawnPatch
        /// gates Miller/Weaver spawn/restore on this — InGameNPC snapshots AgentDuties into
        /// DutiesDict once at init (.decomp/InGameNPC.cs:638-661), so a resident created before
        /// this flips would have no work duty for the rest of the session, silently.
        /// </summary>
        public static bool DutiesReady => _dutiesBuilt;

        /// <summary>
        /// Builds the duty set right now if it is not built yet. CottageResidentSpawnPatch calls
        /// this from its own spawn/restore call sites so a reload can never re-create a resident
        /// in the window before the 2s build poll has run.
        /// </summary>
        internal static void EnsureBuiltNow() => EnsureDutiesBuilt();

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            TickEvents.Interval(2f, EnsureDutiesBuilt, "CottageResidentWorkDutyBuild");
            Plugin.Logger.LogDebug("[CottageResidentWorkDutyPatch] initialized.");
        }

        private static bool ResolveTypes()
        {
            if (_uidType != null) return true;

            _uidType = CardUtil.FindGameType("UniqueIDScriptable");
            _cardDataType = CardUtil.FindGameType("CardData");
            _npcDutyActionType = CardUtil.FindGameType("NPCDutyAction");
            _npcDutyTagType = CardUtil.FindGameType("NPCDutyTag");
            _statValueTriggerType = CardUtil.FindGameType("StatValueTrigger");

            if (_uidType == null || _cardDataType == null || _npcDutyActionType == null
                || _npcDutyTagType == null || _statValueTriggerType == null)
                return false;

            _getFromIdMethod = _uidType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "GetFromID" && !m.IsGenericMethodDefinition
                    && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
            return _getFromIdMethod != null;
        }

        private static void EnsureDutiesBuilt()
        {
            if (_dutiesBuilt) return;
            try
            {
                if (!ResolveTypes()) return;

                foreach (var beat in Beats)
                {
                    beat.Agent ??= _getFromIdMethod.Invoke(null, new object[] { beat.AgentUid });
                    if (beat.Agent == null) return; // agent JSON not loaded yet — retry next poll
                }

                _villageEnvCard ??= CardUtil.GetCardDataById(WorkPostEnvUid);
                if (_villageEnvCard == null) return;

                foreach (var beat in Beats)
                {
                    beat.AcademyTodayStat ??= _getFromIdMethod.Invoke(null, new object[] { beat.AcademyTodayStatUid });
                    if (beat.AcademyTodayStat == null) return; // GameStat JSON not loaded yet — retry next poll
                }

                // All-or-nothing across BOTH residents: stage everything first, mutate nothing
                // until every beat has succeeded (same two-pass shape GuardDutyPatch uses for its
                // village-territory tag, for the same reason — a partial attempt must never leave
                // one resident's AgentDuties set while the other silently failed).
                var built = new List<(WorkerBeat Beat, object Refs)>(Beats.Length);
                foreach (var beat in Beats)
                {
                    var duty = BuildWorkDuty(beat);
                    if (duty == null)
                    {
                        Plugin.Logger.LogWarning($"[CottageResidentWorkDutyPatch] Failed to build the work duty for {beat.Label} — no duty attached.");
                        return;
                    }

                    var dutyRef = NpcDuties.BuildDutyRef(duty, WorkBaseWeight);
                    var refs = NpcDuties.ToDutyRefArray(dutyRef);
                    built.Add((beat, refs));
                }

                foreach (var (beat, refs) in built)
                {
                    if (!Reflect.SetMember(beat.Agent, "AgentDuties", refs))
                    {
                        Plugin.Logger.LogWarning($"[CottageResidentWorkDutyPatch] NPCAgent.AgentDuties field not found on {beat.AgentUid} — work duty not attached.");
                        return;
                    }
                }

                _dutiesBuilt = true;
                Plugin.Logger.LogInfo($"[CottageResidentWorkDutyPatch] Work duties attached to {built.Count} resident(s).");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[CottageResidentWorkDutyPatch] EnsureDutiesBuilt failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // ── duty construction ───────────────────────────────────────────────────

        private static object BuildWorkDuty(WorkerBeat beat)
        {
            var duty = NpcDuties.NewDuty(beat.DutyUid, beat.DutyUid, beat.DutyDisplayText);

            Reflect.SetMember(duty, "CannotPerformWhileHidden", true);
            // Engine-native equivalent of the hand-rolled system's SharesPlayerEnv guard — never
            // yank a resident out of a live conversation.
            Reflect.SetMember(duty, "CanPerformWhileInDialog", false);

            // Null-array hygiene — the engine indexes these without guards.
            Reflect.SetMember(duty, "DutyTags", Array.CreateInstance(_npcDutyTagType, 0));
            Reflect.SetMember(duty, "OnCancelActions", Array.CreateInstance(_npcDutyActionType, 0));
            Reflect.SetMember(duty, "ValidTimesOfDay", NpcDuties.BuildHourWindow(WorkStartHour, WorkEndHour));

            var conditions = AcademyOffCondition(beat);
            if (!NpcDuties.SetDutyConditions(duty, conditions))
            {
                Plugin.Logger.LogWarning($"[CottageResidentWorkDutyPatch] NPCDuty.DutyConditions not found on {beat.DutyUid} — the work duty would be UNGATED against their Academy day. Refusing to build it.");
                return null;
            }

            var move = NewMoveAction(beat.DutyUid + "_Move", new List<object> { _villageEnvCard });
            NpcDuties.SetActionSequence(duty, move);
            return duty;
        }

        /// <summary>"This resident's AcademyToday stat is (approximately) 0" — the one seam
        /// ValidTimesOfDay can't close on its own (see class doc). Same width-1-range "== N"
        /// idiom GuardDutyPatch.BuildWardenDuty uses for its own WardenGapStatUid gate.</summary>
        private static object AcademyOffCondition(WorkerBeat beat)
        {
            var condition = NpcDuties.EmptyCondition();
            var trigger = Activator.CreateInstance(_statValueTriggerType);
            Reflect.SetMember(trigger, "Stat", beat.AcademyTodayStat);
            Reflect.SetMember(trigger, "TriggerRange", new Vector2(-0.5f, 0.5f));
            var triggerArray = Array.CreateInstance(_statValueTriggerType, 1);
            triggerArray.SetValue(trigger, 0);
            Reflect.SetMember(condition, "RequiredStatValues", triggerArray);
            return condition;
        }

        private static object NewMoveAction(string soName, List<object> envCards)
        {
            var action = NpcDuties.NewMoveAction(soName);

            NpcDuties.SetEnumField(action, "MovementType", "Pathfind");
            NpcDuties.SetEnumField(action, "MoveDestination", "MoveToSpecificEnvironment");
            // Random, NOT Closest: with Closest, MoveDutyAction.FindDestination short-circuits to
            // the resident's CURRENT env the moment it is the listed destination
            // (.decomp/MoveDutyAction.cs:831-838) and they never walk anywhere again — same
            // footgun GuardDutyPatch.NewMoveAction documents, even though there's only one
            // candidate env here.
            NpcDuties.SetEnumField(action, "DestinationSelection", "Random");
            Reflect.SetMember(action, "MoveAwayFromDestination", false);
            Reflect.SetMember(action, "IgnoreCurrentLocation", false);
            Reflect.SetMember(action, "LeaveTracks", false);
            Reflect.SetMember(action, "DontMoveToThisDuty", true);

            var envArray = Array.CreateInstance(_cardDataType, envCards.Count);
            for (int i = 0; i < envCards.Count; i++) envArray.SetValue(envCards[i], i);
            Reflect.SetMember(action, "MoveToEnvironments", envArray);

            return action;
        }
    }
}
