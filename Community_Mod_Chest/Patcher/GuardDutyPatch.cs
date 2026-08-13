using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using UnityEngine;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Village Guards — Wave 1 patrol chassis (Village_Master_Plan.md §10.8.1).
    ///
    /// Builds one <c>Duty_VillagePatrol</c> NPCDuty per guard and attaches it to that
    /// guard's <c>NPCAgent.AgentDuties</c>. Duties MUST be built in C#:
    /// <c>NPCDuty.ActionSequence</c> holds Unity sub-assets that serialize to obfuscated
    /// names, so it is not JSON-authorable (§10.8.0.1 rule 1). This class is a direct
    /// copy of <c>AshPartnerDutyPatch</c>'s reflection idiom — CMC's Assembly-CSharp
    /// reference is the nstripped compile-time stub, so every game type is reached by
    /// reflection (root CLAUDE.md "Harmony Patching Pitfalls").
    ///
    /// The five silent duty-authoring footguns from §10.8.0.1, and how this file avoids
    /// each one:
    ///   1. ActionSequence is C#-built (above).
    ///   2. Weights live on <c>NPCDutyRef.PreferenceWeights</c>, never on the NPCDuty —
    ///      <see cref="BuildDutyRef"/> is the only place a weight is set.
    ///   3. <c>BaseWeight</c> is always &gt; 0. <c>NPCDutySelectionInfo.TotalWeight &lt;= 0</c>
    ///      makes a duty permanently unselectable with no error.
    ///   4. Second gate layer: <c>NPCDutyRef.ActivatingMode</c> = ActivateAutomatically with
    ///      an EMPTY <c>ActivatingConditions</c>. This is load-bearing, not cosmetic — see
    ///      the reload note below.
    ///   5. <c>RequiredForSelectingDuty</c> stays false on every action step.
    /// Plus rule 6 (AgentDuties is snapshotted at InGameNPC init): <see cref="DutiesReady"/>
    /// gates GuardSpawnPatch so no guard is ever created before its duties exist.
    ///
    /// Reload behaviour: these duties are NOT registered in the game's UID table, so
    /// <c>InGameNPC.Init</c>'s save-restore path (<c>_FromData.ActiveDuties</c> →
    /// <c>GetFromID&lt;NPCDuty&gt;</c>, .decomp/InGameNPC.cs:705-708) cannot re-activate
    /// them after a load. What saves us is footgun-4's setting: the duty-selection tick
    /// re-evaluates <c>ActivatingConditions</c> for every non-AlwaysActive ref every tick
    /// (.decomp/InGameNPC.cs:2026-2031), so an empty condition re-activates the patrol on
    /// the first tick after any load. Do not switch these refs to AlwaysActive — that
    /// branch is SKIPPED by that loop and the duty would stay out of ActiveDuties forever
    /// after a reload.
    ///
    /// Beat coverage is honest about the map: guard beats in §10.8.1 are written in terms
    /// of landmarks (Village Hall, Market Stall, Well) that are CARDS on the Village board,
    /// not WorldMap nodes, and duty movement is env-to-env only (MoveDutyAction ends in
    /// <c>GameManager.MoveNPC(npc, EnvID)</c>). All four guards hold a single patrol node
    /// (<c>cmcEnvVillage</c>) as of the §10.8.11 role split — Thorne and Corrin were relocated
    /// from their original multi-node river/farm beats to the shared Town Square, where their
    /// day-shift <c>ValidTimesOfDay</c> window now does the work their old beat separation did.
    /// </summary>
    internal static class GuardDutyPatch
    {
        /// <summary>Per-guard patrol configuration. One shared chassis, four beats.</summary>
        private sealed class GuardBeat
        {
            public string Label;            // log label only
            public string AgentUid;
            public string DutyUid;
            public string DutyDisplayText;  // NPCDuty.DutyName, shown in the NPC inspection UI
            public string[] PatrolEnvUids;  // MoveToEnvironments, picked at random each cycle
            public int BaseWeight;
            public bool NightOnly;          // Iris Vane — ValidTimesOfDay gate, 20:00-05:00
            public bool DayShiftOnly;       // Thorne & Corrin — ValidTimesOfDay gate, 05:00-20:00 (§10.8.11.3)
            public object Agent;            // NPCAgent, resolved lazily

            // Pursuit (§10.8.5/§10.8.6, reshaped by §10.8.11). Thorne, Corrin and Vane carry the
            // shared territory-gated chase built by BuildChaseDuty; Sterling does not (see
            // HasLocalChase below). They need no chaining code: StartEncounterDutyAction.CanBePerformed
            // refuses to start while EncounterPopup.OngoingEncounter is true
            // (.decomp/StartEncounterDutyAction.cs:21-24), so whoever arrives first fights while the
            // rest keep closing — the "back-to-back guards" feel is emergent from independent duties,
            // not an authored sequence.
            public string ChaseDutyUid;
            public string ChaseDutyDisplayText;
            public string ChaseEncounterUid;
            public object ChaseEncounter;   // Encounter, resolved lazily

            /// <summary>
            /// False only for Captain Sterling (§10.8.11.4) — he no longer gets the shared
            /// territory-gated Duty_GuardChasePlayer built by BuildChaseDuty at all; he only
            /// responds to a summon (see SummonDutyUid/FinishArrestDutyUid below). True for the
            /// other three, whose local chase is otherwise unchanged.
            /// </summary>
            public bool HasLocalChase = true;

            /// <summary>
            /// True only for Guard Vane (§10.8.11.2) — the one permanent exception to
            /// cmcTagVillageTerritory. BuildChaseDuty drops her RequiredEnvironmentTags block and
            /// swaps her chase's second action from a fight to a ChangeStatDutyAction alarm call.
            /// </summary>
            public bool IsUnrestrictedSeeker;

            // Captain Sterling's three summon-response duties (§10.8.11.4/§10.8.11.9), built by
            // BuildSummonDuty. Null on the other three guards.
            public string SummonDutyUid;
            public string SummonDutyDisplayText;
            public string FinishArrestDutyUid;
            public string FinishArrestDutyDisplayText;

            /// <summary>
            /// §10.8.11.9 — the third Sterling duty. Same gate as FinishArrest PLUS
            /// "the player has already used up all three chances", routing to
            /// <see cref="ArrestEncounterUid"/> instead of the lenient encounter.
            /// </summary>
            public string ForceArrestDutyUid;
            public string ForceArrestDutyDisplayText;

            /// <summary>
            /// §10.8.11.9 — the two extra Encounter assets Sterling's paths route to.
            /// <c>Converge</c> is the whole-Watch gauntlet (ForceFight, no leniency);
            /// <c>Arrest</c> is the forced arrest after the third decline (ForceFight plus a
            /// non-damaging subdual action that forces <c>EncounterResult.PlayerDemoralized</c>).
            /// Both are null on the other three guards, who keep their single encounter.
            /// </summary>
            public string ConvergeEncounterUid;
            public object ConvergeEncounter;   // Encounter, resolved lazily
            public string ArrestEncounterUid;
            public object ArrestEncounter;     // Encounter, resolved lazily

            // Jail warden rotation (§10.8.7.1). One shift block per guard, non-overlapping and
            // covering all 24 hours between them, so at least one guard is always rostered.
            public string WardenDutyUid;
            public string WardenDutyDisplayText;
            public int WardenShiftStartHour;
            public int WardenShiftEndHour;
        }

        private static readonly GuardBeat[] Beats =
        {
            new GuardBeat
            {
                Label = "Captain Reeve Sterling", AgentUid = "cmcGuardSterlingAgent",
                DutyUid = "cmcGuardSterlingPatrol_Duty", DutyDisplayText = "Watching the square",
                PatrolEnvUids = new[] { "cmcEnvVillage" }, BaseWeight = 20,
                // §10.8.11.4 — no local chase; he only responds when summoned.
                HasLocalChase = false,
                ChaseEncounterUid = "cmcEncounterGuardSterling",
                // §10.8.11.9 — one encounter per path. The base UID stays the lenient one so
                // the Attack button on his own NPCAgent keeps working unchanged.
                ConvergeEncounterUid = "cmcEncounterSterlingConverge",
                ArrestEncounterUid = "cmcEncounterSterlingArrest",
                SummonDutyUid = "cmcGuardSterlingSummon_Duty", SummonDutyDisplayText = "Answering the call for the Watch",
                FinishArrestDutyUid = "cmcGuardSterlingFinishArrest_Duty", FinishArrestDutyDisplayText = "Coming to finish an arrest",
                ForceArrestDutyUid = "cmcGuardSterlingForceArrest_Duty", ForceArrestDutyDisplayText = "Done giving chances",
                WardenDutyUid = "cmcGuardSterlingWarden_Duty", WardenDutyDisplayText = "Standing warden at the jail",
                WardenShiftStartHour = 5, WardenShiftEndHour = 11
            },
            new GuardBeat
            {
                Label = "Guard Nella Thorne", AgentUid = "cmcGuardThorneAgent",
                DutyUid = "cmcGuardThornePatrol_Duty", DutyDisplayText = "Walking the town square beat",
                // §10.8.11.3 — relocated from the river/path beat to the shared Town Square.
                PatrolEnvUids = new[] { "cmcEnvVillage" }, BaseWeight = 20, DayShiftOnly = true,
                ChaseDutyUid = "cmcGuardThorneChase_Duty", ChaseDutyDisplayText = "Hunting a criminal",
                ChaseEncounterUid = "cmcEncounterGuardThorne",
                WardenDutyUid = "cmcGuardThorneWarden_Duty", WardenDutyDisplayText = "Standing warden at the jail",
                WardenShiftStartHour = 11, WardenShiftEndHour = 17
            },
            new GuardBeat
            {
                Label = "Guard Old Corrin", AgentUid = "cmcGuardCorrinAgent",
                DutyUid = "cmcGuardCorrinPatrol_Duty", DutyDisplayText = "Walking the town square beat",
                // §10.8.11.3 — relocated from the farm/clay-flats beat to the shared Town Square.
                PatrolEnvUids = new[] { "cmcEnvVillage" }, BaseWeight = 20, DayShiftOnly = true,
                ChaseDutyUid = "cmcGuardCorrinChase_Duty", ChaseDutyDisplayText = "Going after a criminal",
                ChaseEncounterUid = "cmcEncounterGuardCorrin",
                WardenDutyUid = "cmcGuardCorrinWarden_Duty", WardenDutyDisplayText = "Standing warden at the jail",
                WardenShiftStartHour = 17, WardenShiftEndHour = 23
            },
            new GuardBeat
            {
                Label = "Guard Iris Vane", AgentUid = "cmcGuardVaneAgent",
                DutyUid = "cmcGuardVanePatrol_Duty", DutyDisplayText = "Walking the night watch",
                PatrolEnvUids = new[] { "cmcEnvVillage" }, BaseWeight = 20, NightOnly = true,
                // §10.8.11.2 — the seeker: no territory gate, calls the Watch in instead of fighting.
                IsUnrestrictedSeeker = true,
                ChaseDutyUid = "cmcGuardVaneChase_Duty", ChaseDutyDisplayText = "Running down a criminal",
                ChaseEncounterUid = "cmcEncounterGuardVane",
                // Vane's shift sits inside her own 20:00-05:00 ValidTimesOfDay window; a warden
                // block outside it would be unselectable and would leave the jail uncovered.
                WardenDutyUid = "cmcGuardVaneWarden_Duty", WardenDutyDisplayText = "Standing warden at the jail",
                WardenShiftStartHour = 23, WardenShiftEndHour = 5
            },
        };

        // Iris Vane's shift. InGameTimeCondition.IsInHourRange treats Start > End as a
        // wrap-around window, so 20 -> 5 means hours 20:00-23:59 and 00:00-04:59
        // (.decomp/InGameTimeCondition.cs IsInHourRange).
        private const int NightShiftStartHour = 20;
        private const int NightShiftEndHour = 5;

        // Thorne & Corrin's day shift (§10.8.11.3) — the exact complement of Vane's night window.
        private const int DayShiftStartHour = 5;
        private const int DayShiftEndHour = 20;

        // ── pursuit tuning (§10.8.5) ────────────────────────────────────────────
        /// <summary>Player-facing Crime stat. 60+ is the Enemy/Banished band (§10.8.2).</summary>
        internal const string CrimeStatUid = "cmcStatVillageCrime";
        private const float EnemyCrimeThreshold = 60f;
        // The stat is clamped 0-100 by its own MinMaxValue, so the upper bound only has to
        // be "anything at or above 100". Deliberately not 1e9 — a TriggerRange that large
        // loses integer precision as a float and the range check misbehaves
        // (reference_triggerrange_fractional_overflow).
        private const float CrimeRangeMax = 999999f;

        // The chase must outrank Patrol (BaseWeight 20) deterministically rather than by a
        // tie-break. §10.8.0.1 rule 3 / NPCDutySelectionInfo.cs:372.
        private const int ChaseBaseWeight = 100000;

        // ── warden rotation (§10.8.7.1) ─────────────────────────────────────────
        /// <summary>
        /// Outranks Patrol (20) inside the guard's shift block, and is outranked by the chase
        /// (100000) — an active manhunt always beats standing warden.
        /// </summary>
        private const int WardenBaseWeight = 60;

        /// <summary>
        /// Where "on warden post" physically is. This is the VILLAGE node, not the jail cell's
        /// own interior env, and that is an engine constraint rather than a design preference:
        /// <c>MoveDutyAction.FindDestination</c> skips every <c>MoveToEnvironments</c> entry whose
        /// card has <c>InstancedEnvironment</c> set (.decomp/MoveDutyAction.cs:380/408/779/822),
        /// and for a non-instanced env it still resolves the destination through
        /// <c>WorldMapData.GetOneStepPath</c> → <c>GetEnvData</c> → <c>MapDict</c>
        /// (.decomp/WorldMapData.cs:353-380), which only ever contains WorldMap nodes. The jail
        /// cell is deliberately neither, so no duty can put a guard literally inside it — the
        /// warden stands on the jail's step, in the village square where <c>cmcJail</c> sits.
        /// </summary>
        private const string WardenPostEnvUid = "cmcEnvVillage";

        /// <summary>
        /// Hours remaining in the current warden-absence window (§10.8.8.2), owned by
        /// <see cref="JailPatch"/>. Every warden duty requires it to be 0, so when the window
        /// opens the guards genuinely abandon the post rather than a flag merely claiming so.
        /// </summary>
        internal const string WardenGapStatUid = "cmcStatJailWardenGap";

        // ── summon signal stats (§10.8.11.1) ────────────────────────────────────
        /// <summary>
        /// Set to 1 by Vane's untagged seek duty once she reaches the player's environment
        /// (a ChangeStatDutyAction, not a fight); read by Sterling's Duty_SterlingRespondToSummon;
        /// cleared by his own Encounter once it resolves. Deliberately NOT cmcStatArrestPending —
        /// that stat self-clears the same tick it fires (JailPatch.OnArrestPendingRaised) and
        /// cannot hold a durable state across a multi-hour cross-map walk.
        /// </summary>
        internal const string GuardsSummonedStatUid = "cmcStatGuardsSummoned";

        /// <summary>
        /// Set to 1 by Thorne's or Corrin's PlayerDemoralizedEffects the moment a spear guard wins
        /// locally; read by Sterling's Duty_SterlingFinishArrest; cleared by his own Encounter.
        /// </summary>
        internal const string CaptainSummonedStatUid = "cmcStatCaptainSummoned";

        /// <summary>
        /// §10.8.11.9 — how many times the player has taken Sterling's "Think better of it"
        /// option instead of fighting him. Incremented declaratively by
        /// <c>cmcEncounterGuardSterling</c>'s <c>StartingOptions.EscapeOptionStatChanges</c>
        /// (<c>EncounterStartPopup.SelectOptionRoutine</c> runs them through the normal
        /// PerformAction pipeline and AWAITS them before <c>CancelEncounter()</c>, so the
        /// increment has landed by the time the duty is re-selected — no C# writes it), and
        /// reset to 0 by <c>cmcEncounterSterlingArrest</c>'s <c>PlayerDemoralizedEffects</c>.
        ///
        /// <para>Clamped 0-3 by its own <c>MinMaxValue</c>. That clamp is load-bearing, not
        /// cosmetic: this is a repeatable offer whose stat also gates a duty, which is exactly
        /// the unclamped-counter softlock the root CLAUDE.md's NPC-errand rule describes. At the
        /// clamp the value simply stays 3 and the forced-arrest gate stays satisfied.</para>
        /// </summary>
        internal const string SterlingEscapeCountStatUid = "cmcStatSterlingEscapeCount";

        /// <summary>
        /// Chances the player gets before Sterling stops offering. Lenient duty requires
        /// strictly fewer than this; the forced-arrest duty requires at least this.
        /// MUST equal <c>CMC_SterlingEscapeCount.json</c>'s <c>MinMaxValue.y</c> — a mismatch
        /// either strands the counter below the gate (never arrests) or opens both gates at once.
        /// </summary>
        private const int SterlingChancesAllowed = 3;

        private const string VillageTerritoryTagName = "cmcTagVillageTerritory";

        /// <summary>
        /// Per-guard "the player has bested me" marker (§10.8.6). Declared on each guard through
        /// their NPCCharacterPerk's <c>AddedNPCStats</c> (the same route cmcNpcStatSuspicion
        /// already takes) and written declaratively by that guard's own Encounter from its
        /// <c>EnemyDefeatedEffects</c>/<c>EnemyEscapedEffects</c> — no C# writes it.
        /// <see cref="GuardOutcomePatch"/> only ever CLEARS it, once the season timer expires.
        /// </summary>
        internal const string GuardDownedStatUid = "cmcNpcStatGuardDowned";

        /// <summary>
        /// Every CMC village-side WorldMap node, as (CT4 environment UID, CT8 location UID)
        /// pairs. Guards pursue INSIDE this set and stop at its edge — the "guards won't cross
        /// the river" rule is literally this list.
        ///
        /// MUST stay in sync with <c>Community_Mod_Chest/WorldMap/MapNodes.json</c>: a node
        /// added there but missed here is territory the guards silently will not defend.
        /// The tag is stamped onto BOTH cards of each pair because
        /// <c>GameManager.EnvironmentHasTag</c> checks the env card first and the explorable
        /// card second (.decomp/GameManager.cs:11889-11908) and which one answers depends on
        /// how the EnvID was constructed.
        /// </summary>
        private static readonly string[] VillageTerritoryCardUids =
        {
            "cmcEnvVillagePath",      "cmcLocVillagePath",
            "cmcEnvHighGrove",        "cmcLocHighGrove",
            "cmcEnvPineTrail",        "cmcLocPineTrail",
            "cmcEnvVillage",          "cmcLocVillage",
            "cmcEnvVillageFarm",      "cmcLocVillageFarm",
            "cmcEnvClayFlats",        "cmcLocClayFlats",
            "cmcEnvMarshHollow",      "cmcLocMarshHollow",
            "cmcEnvMossyClearing",    "cmcLocMossyClearing",
            "cmcEnvForagingForest",   "cmcLocForagingForest",
            "cmcEnvHuntersCrossing",  "cmcLocHuntersCrossing",
            "cmcEnvDeerMeadow",       "cmcLocDeerMeadow",
            "cmcEnvBadgerWarren",     "cmcLocBadgerWarren",
        };

        private static bool _initialized;
        private static bool _dutiesBuilt;
        private static object _villageTerritoryTag;   // CardTag, created in C# (see EnsureVillageTerritoryTag)
        private static object _crimeStat;             // GameStat SO for CrimeStatUid
        private static object _guardDownedStat;       // NPCStat SO for GuardDownedStatUid
        private static object _wardenGapStat;         // GameStat SO for WardenGapStatUid
        private static object _guardsSummonedStat;     // GameStat SO for GuardsSummonedStatUid
        private static object _captainSummonedStat;    // GameStat SO for CaptainSummonedStatUid
        private static object _sterlingEscapeCountStat; // GameStat SO for SterlingEscapeCountStatUid
        private static int _pursuitBuildAttempts;

        /// <summary>
        /// Each guard's <c>Duty_JailWarden</c> instance, keyed by agent UniqueID. <see cref="JailPatch"/>
        /// compares these against the live <c>InGameNPC.CurrentPerformingDuty</c> to decide whether
        /// anyone is actually standing warden. Empty until <see cref="DutiesReady"/> flips.
        /// </summary>
        internal static IReadOnlyDictionary<string, object> WardenDuties => _wardenDuties;

        private static readonly Dictionary<string, object> _wardenDuties =
            new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        /// <summary>~30s at the 2s build poll before the patrol chassis stops waiting on pursuit.</summary>
        private const int PursuitBuildAttemptLimit = 15;

        // ── reflection handles, resolved once ──────────────────────────────────
        private static Type _uidType, _npcDutyType, _npcDutyActionType, _moveDutyActionType,
            _npcDutyRefType, _generalConditionType, _npcDutyWeightsType, _cardDataType,
            _cardTagType, _cardOrTagRefWithDurabilitiesType, _npcStatInstantModifierType,
            _localizedStringType, _inGameTimeConditionType, _npcDutyTagType,
            _startEncounterDutyActionType, _statValueTriggerType, _npcStatConditionType,
            _changeStatDutyActionType, _statModifierType;
        private static MethodInfo _getFromIdMethod;
        private static MethodInfo _checkForDutiesMethod;

        /// <summary>
        /// True once every guard's AgentDuties array has been attached. GuardSpawnPatch
        /// gates its spawn on this — <c>InGameNPC</c> snapshots AgentDuties into DutiesDict
        /// once at init (.decomp/InGameNPC.cs:638-661), so a guard created before this flips
        /// would have no duties for the rest of the session, silently.
        /// </summary>
        public static bool DutiesReady => _dutiesBuilt;

        /// <summary>
        /// Builds the duty set right now if it is not built yet. GuardSpawnPatch calls this
        /// from its save-restore postfix so a reload can never re-create a guard in the
        /// window before the 2s build poll has run (that guard would come back with an
        /// empty DutiesDict and never patrol again for the session, with no error).
        /// </summary>
        internal static void EnsureBuiltNow() => EnsureDutiesBuilt();

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            TickEvents.Interval(2f, EnsureDutiesBuilt, "GuardDutyBuild");
            TickEvents.Interval(10f, GuardDutyDiagnostics.Run, "GuardDutyDiagnostics");
            // §10.8.4.2 — close the gap left by GuardWitnessPatch's unconsumed AttackWitnessed
            // event: without this, a witnessing guard only re-evaluates their chase duty on the
            // next natural InGameNPC.UpdateTick (once per in-game DTP tick,
            // .decomp/GameManager.cs:5817 — up to 15 in-game minutes, not "immediate").
            // Subscribing here is order-independent: it's a static event, GuardWitnessPatch does
            // not need to be initialized yet for this to attach.
            GuardWitnessPatch.AttackWitnessed += OnAttackWitnessed;
            Plugin.Logger.LogDebug("[GuardDutyPatch] initialized.");
        }

        /// <summary>
        /// Forces the witnessing guard's own duty selection to re-run RIGHT NOW instead of
        /// waiting for the next tick, by starting the exact same private coroutine the engine's
        /// own tick loop calls (<c>InGameNPC.CheckForDuties(bool)</c>, .decomp/InGameNPC.cs:1994)
        /// via reflection, on the guard's own MonoBehaviour. Started, never synchronously drained
        /// (reference_synchronous_coroutine_drain_freeze) — it runs across normal frames exactly
        /// like the engine's own call does, it just doesn't wait for the next tick to begin.
        ///
        /// Safe to call before pursuit duties exist (<c>_dutiesBuilt</c> false — bails, the guard
        /// has nothing to select yet), before the crime gate actually passes (CheckForDuties'
        /// own condition evaluation just finds nothing selectable and no-ops, same as any regular
        /// tick), and for the attacked guard's own witness entry (FindWitnesses does not exclude
        /// the victim — but that guard is IsInCombat, and CheckForDuties' own first check yields
        /// break immediately, so no extra guard is needed here).
        /// </summary>
        private static void OnAttackWitnessed(GuardWitnessPatch.Witness witness)
        {
            if (!_dutiesBuilt) return;
            if (witness?.GuardNpc is not MonoBehaviour npcBehaviour) return;

            try
            {
                _checkForDutiesMethod ??= witness.GuardNpc.GetType()
                    .GetMethod("CheckForDuties", BindingFlags.Instance | BindingFlags.NonPublic);
                if (_checkForDutiesMethod == null)
                {
                    Plugin.Logger.LogWarning("[GuardDutyPatch] InGameNPC.CheckForDuties not found — witnessed-attack immediate re-check inactive.");
                    return;
                }

                if (_checkForDutiesMethod.Invoke(witness.GuardNpc, new object[] { false }) is not IEnumerator routine)
                    return;

                npcBehaviour.StartCoroutine(routine);
                Plugin.Logger.LogDebug($"[GuardDutyPatch] {witness.GuardLabel} re-checking duties immediately (witnessed attack).");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[GuardDutyPatch] OnAttackWitnessed failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static bool ResolveTypes()
        {
            if (_npcDutyType != null) return true;

            _uidType = CardUtil.FindGameType("UniqueIDScriptable");
            _npcDutyType = CardUtil.FindGameType("NPCDuty");
            _npcDutyActionType = CardUtil.FindGameType("NPCDutyAction");
            _moveDutyActionType = CardUtil.FindGameType("MoveDutyAction");
            _npcDutyRefType = CardUtil.FindGameType("NPCDutyRef");
            _generalConditionType = CardUtil.FindGameType("GeneralCondition");
            _npcDutyWeightsType = CardUtil.FindGameType("NPCDutyWeights");
            _cardDataType = CardUtil.FindGameType("CardData");
            _cardTagType = CardUtil.FindGameType("CardTag");
            _cardOrTagRefWithDurabilitiesType = CardUtil.FindGameType("CardOrTagRefWithDurabilities");
            _npcStatInstantModifierType = CardUtil.FindGameType("NPCStatInstantModifier");
            _localizedStringType = CardUtil.FindGameType("LocalizedString");
            _inGameTimeConditionType = CardUtil.FindGameType("InGameTimeCondition");
            _npcDutyTagType = CardUtil.FindGameType("NPCDutyTag");
            _startEncounterDutyActionType = CardUtil.FindGameType("StartEncounterDutyAction");
            _statValueTriggerType = CardUtil.FindGameType("StatValueTrigger");
            _npcStatConditionType = CardUtil.FindGameType("NPCStatCondition");
            _changeStatDutyActionType = CardUtil.FindGameType("ChangeStatDutyAction");
            _statModifierType = CardUtil.FindGameType("StatModifier");

            if (_startEncounterDutyActionType == null || _statValueTriggerType == null
                || _npcStatConditionType == null || _changeStatDutyActionType == null
                || _statModifierType == null) return false;

            if (_uidType == null || _npcDutyType == null || _npcDutyActionType == null
                || _moveDutyActionType == null || _npcDutyRefType == null || _generalConditionType == null
                || _npcDutyWeightsType == null || _cardDataType == null || _cardTagType == null
                || _cardOrTagRefWithDurabilitiesType == null || _npcStatInstantModifierType == null
                || _localizedStringType == null || _inGameTimeConditionType == null
                || _npcDutyTagType == null)
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

                // All-or-nothing: every guard's agent and every patrol env must resolve
                // before ANY duty is attached, so DutiesReady never reports a half-built
                // set to the spawn patch.
                foreach (var beat in Beats)
                {
                    beat.Agent ??= _getFromIdMethod.Invoke(null, new object[] { beat.AgentUid });
                    if (beat.Agent == null) return; // agent JSON not loaded yet — retry next poll
                }

                // Pursuit prerequisites. These are OPTIONAL relative to patrolling: if the
                // Crime stat, the territory tag or an Encounter asset is not resolvable yet we
                // retry for a while, but we never block the patrol chassis on them forever —
                // a guard with no duties at all is a worse failure than a guard who patrols
                // but never chases (feedback_subsystem_graceful_degradation).
                _crimeStat ??= _getFromIdMethod.Invoke(null, new object[] { CrimeStatUid });
                _guardDownedStat ??= _getFromIdMethod.Invoke(null, new object[] { GuardDownedStatUid });
                _wardenGapStat ??= _getFromIdMethod.Invoke(null, new object[] { WardenGapStatUid });
                _guardsSummonedStat ??= _getFromIdMethod.Invoke(null, new object[] { GuardsSummonedStatUid });
                _captainSummonedStat ??= _getFromIdMethod.Invoke(null, new object[] { CaptainSummonedStatUid });
                _sterlingEscapeCountStat ??= _getFromIdMethod.Invoke(null, new object[] { SterlingEscapeCountStatUid });
                EnsureVillageTerritoryTag();
                bool pursuitReady = _crimeStat != null && _villageTerritoryTag != null
                    && _guardDownedStat != null && _guardsSummonedStat != null && _captainSummonedStat != null;
                foreach (var beat in Beats)
                {
                    if (beat.ChaseEncounterUid == null) continue;
                    beat.ChaseEncounter ??= _getFromIdMethod.Invoke(null, new object[] { beat.ChaseEncounterUid });
                    if (beat.ChaseEncounter == null) pursuitReady = false;

                    // §10.8.11.9 — Sterling's two extra path-specific encounters. Deliberately
                    // NOT folded into pursuitReady: if either fails to resolve the leniency
                    // mechanic degrades to the pre-1.48.0 behaviour (both summon duties run the
                    // base encounter) rather than taking the whole Watch down with it.
                    if (beat.ConvergeEncounterUid != null)
                        beat.ConvergeEncounter ??= _getFromIdMethod.Invoke(null, new object[] { beat.ConvergeEncounterUid });
                    if (beat.ArrestEncounterUid != null)
                        beat.ArrestEncounter ??= _getFromIdMethod.Invoke(null, new object[] { beat.ArrestEncounterUid });
                }
                if (!pursuitReady && ++_pursuitBuildAttempts <= PursuitBuildAttemptLimit) return;
                if (!pursuitReady)
                    Plugin.Logger.LogWarning(
                        $"[GuardDutyPatch] Pursuit prerequisites never resolved (crimeStat={_crimeStat != null}, " +
                        $"downedStat={_guardDownedStat != null}, " +
                        $"territoryTag={_villageTerritoryTag != null}, " +
                        $"guardsSummonedStat={_guardsSummonedStat != null}, captainSummonedStat={_captainSummonedStat != null}, encounters=" +
                        $"{Beats.Count(b => b.ChaseEncounterUid != null && b.ChaseEncounter != null)}/" +
                        $"{Beats.Count(b => b.ChaseEncounterUid != null)}) — guards will patrol but never chase.");

                // The warden post. Unlike the patrol beats this is one shared env, so it is
                // resolved once; a null here only costs the warden rotation, not the patrol.
                var wardenEnv = CardUtil.GetCardDataById(WardenPostEnvUid);
                if (wardenEnv == null)
                    Plugin.Logger.LogWarning(
                        $"[GuardDutyPatch] Warden post env '{WardenPostEnvUid}' not resolvable — " +
                        "no guard will stand watch at the jail this session.");

                var built = new List<(GuardBeat Beat, object Refs)>(Beats.Length);
                int chases = 0;
                foreach (var beat in Beats)
                {
                    var envCards = new List<object>(beat.PatrolEnvUids.Length);
                    foreach (var envUid in beat.PatrolEnvUids)
                    {
                        var env = CardUtil.GetCardDataById(envUid);
                        if (env == null)
                        {
                            Plugin.Logger.LogWarning($"[GuardDutyPatch] Patrol env '{envUid}' for {beat.Label} not resolvable yet — retrying.");
                            return;
                        }
                        envCards.Add(env);
                    }

                    var duty = BuildPatrolDuty(beat, envCards);
                    if (duty == null)
                    {
                        Plugin.Logger.LogWarning($"[GuardDutyPatch] Failed to build the patrol duty for {beat.Label} — no guard duties attached.");
                        return;
                    }

                    object chaseRef = null;
                    if (pursuitReady && beat.ChaseEncounterUid != null && beat.HasLocalChase)
                    {
                        // A null here is an authoring failure, not a load-order one (every asset
                        // it needs was resolved above), so degrade to patrol-only rather than
                        // blocking DutiesReady — which would also stop the guard from spawning.
                        var chaseDuty = BuildChaseDuty(beat);
                        if (chaseDuty == null)
                        {
                            Plugin.Logger.LogWarning($"[GuardDutyPatch] Could not build the chase duty for {beat.Label} — they will patrol but never pursue.");
                        }
                        else
                        {
                            // The coarse "is the player Banished at all" gate lives on the ref
                            // (evaluated every tick, InGameNPC.cs:2027-2033); the duty's own
                            // DutyConditions carry the same check PLUS the per-tick territory
                            // check. §10.8.0.1 rule 4 — both layers gate selection, ref first.
                            chaseRef = BuildDutyRef(chaseDuty, ChaseBaseWeight, CrimeGateCondition());
                            chases++;
                        }
                    }

                    // Captain Sterling's two summon-response duties (§10.8.11.4) — same shape as
                    // the chase minus the territory tag, gated on the two new signal stats instead
                    // of Crime. Nobody else has SummonDutyUid set, so this is a no-op for them.
                    var summonRefs = new List<object>();
                    if (pursuitReady && beat.SummonDutyUid != null && beat.ChaseEncounter != null)
                    {
                        // §10.8.11.9 — the leniency split only arms when BOTH the counter stat and
                        // the arrest encounter resolved. Either missing and we fall back to the
                        // pre-1.48.0 shape: one lenient encounter on both paths, no forced arrest.
                        bool leniencyArmed = _sterlingEscapeCountStat != null
                                             && beat.ArrestEncounter != null
                                             && beat.ForceArrestDutyUid != null;
                        if (beat.ForceArrestDutyUid != null && !leniencyArmed)
                            Plugin.Logger.LogWarning(
                                $"[GuardDutyPatch] {beat.Label}'s three-chances arrest is NOT armed " +
                                $"(escapeCountStat={_sterlingEscapeCountStat != null}, " +
                                $"arrestEncounter={beat.ArrestEncounter != null}) — he will keep offering " +
                                "'Think better of it' forever and never force the arrest.");

                        if (_guardsSummonedStat != null)
                        {
                            // The whole Watch has converged (§10.8.11.9 rule 6): no leniency, so
                            // this path gets the ForceFight asset when it resolved.
                            var summonDuty = BuildSummonDuty(beat, beat.SummonDutyUid, beat.SummonDutyDisplayText,
                                _guardsSummonedStat, beat.ConvergeEncounter ?? beat.ChaseEncounter);
                            if (summonDuty == null)
                                Plugin.Logger.LogWarning($"[GuardDutyPatch] Could not build {beat.Label}'s summon-response duty.");
                            else
                                summonRefs.Add(BuildDutyRef(summonDuty, ChaseBaseWeight, TriggerStatGateCondition(_guardsSummonedStat)));
                        }
                        if (_captainSummonedStat != null && beat.FinishArrestDutyUid != null)
                        {
                            // Caught alone. While the player still has a chance left, this duty is
                            // the selectable one and it opens with Attack / Think better of it.
                            var finishArrestDuty = BuildSummonDuty(beat, beat.FinishArrestDutyUid, beat.FinishArrestDutyDisplayText,
                                _captainSummonedStat, beat.ChaseEncounter,
                                leniencyArmed ? ChancesRemainingTrigger() : null);
                            if (finishArrestDuty == null)
                                Plugin.Logger.LogWarning($"[GuardDutyPatch] Could not build {beat.Label}'s finish-arrest duty.");
                            else
                                summonRefs.Add(BuildDutyRef(finishArrestDuty, ChaseBaseWeight,
                                    TriggerStatGateCondition(_captainSummonedStat,
                                        leniencyArmed ? ChancesRemainingTrigger() : null)));
                        }
                        if (leniencyArmed && _captainSummonedStat != null)
                        {
                            // Chances used up. Identical gate PLUS "counter is at the cap", routed
                            // to the ForceFight arrest asset. The two finish-arrest duties are
                            // mutually exclusive by construction — their counter ranges do not
                            // overlap — so exactly one is ever selectable at a time.
                            var forceArrestDuty = BuildSummonDuty(beat, beat.ForceArrestDutyUid, beat.ForceArrestDutyDisplayText,
                                _captainSummonedStat, beat.ArrestEncounter, ChancesExhaustedTrigger());
                            if (forceArrestDuty == null)
                                Plugin.Logger.LogWarning($"[GuardDutyPatch] Could not build {beat.Label}'s forced-arrest duty.");
                            else
                                summonRefs.Add(BuildDutyRef(forceArrestDuty, ChaseBaseWeight,
                                    TriggerStatGateCondition(_captainSummonedStat, ChancesExhaustedTrigger())));
                        }
                    }

                    // Standing warden at the jail (§10.8.7.1). Optional in the same sense the chase
                    // is: a guard who only patrols is a far better failure than a guard with no
                    // duties at all (feedback_subsystem_graceful_degradation).
                    object wardenRef = null;
                    if (beat.WardenDutyUid != null && wardenEnv != null)
                    {
                        var wardenDuty = BuildWardenDuty(beat, wardenEnv);
                        if (wardenDuty == null)
                            Plugin.Logger.LogWarning(
                                $"[GuardDutyPatch] Could not build the warden duty for {beat.Label} — they will " +
                                "never stand watch at the jail, and the jail reads as unguarded during their shift.");
                        else
                        {
                            _wardenDuties[beat.AgentUid] = wardenDuty;
                            wardenRef = BuildDutyRef(wardenDuty, WardenBaseWeight);
                        }
                    }

                    var refList = new List<object> { BuildDutyRef(duty, beat.BaseWeight) };
                    if (wardenRef != null) refList.Add(wardenRef);
                    if (chaseRef != null) refList.Add(chaseRef);
                    refList.AddRange(summonRefs);
                    var refs = Array.CreateInstance(_npcDutyRefType, refList.Count);
                    for (int i = 0; i < refList.Count; i++) refs.SetValue(refList[i], i);
                    built.Add((beat, refs));
                }

                foreach (var (beat, refs) in built)
                {
                    if (!Reflect.SetMember(beat.Agent, "AgentDuties", refs))
                    {
                        Plugin.Logger.LogWarning($"[GuardDutyPatch] NPCAgent.AgentDuties field not found on {beat.AgentUid} — guard duties not attached.");
                        return;
                    }
                }

                _dutiesBuilt = true;
                Plugin.Logger.LogInfo(
                    $"[GuardDutyPatch] Village Watch duties attached to {built.Count} guards " +
                    $"({chases} with a pursuit duty, {_wardenDuties.Count} on the jail warden rotation).");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[GuardDutyPatch] EnsureDutiesBuilt failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // ── duty construction ───────────────────────────────────────────────────

        private static object BuildPatrolDuty(GuardBeat beat, List<object> envCards)
        {
            var duty = ScriptableObject.CreateInstance(_npcDutyType);
            duty.name = beat.DutyUid;
            Reflect.SetMemberAny(duty, beat.DutyUid, "UniqueID", "uniqueID", "m_UniqueID");

            var localizedName = Activator.CreateInstance(_localizedStringType);
            Reflect.SetMember(localizedName, "ParentObjectID", "");
            Reflect.SetMember(localizedName, "LocalizationKey", "IGNOREKEY");
            Reflect.SetMember(localizedName, "DefaultText", beat.DutyDisplayText);
            Reflect.SetMember(duty, "DutyName", localizedName);

            Reflect.SetMember(duty, "MaxPerformPerDay", 0);
            Reflect.SetMember(duty, "CanOnlyPerformAtHome", false);
            Reflect.SetMember(duty, "CannotPerformWhileHidden", true);
            Reflect.SetMember(duty, "CanPerformWhileInDialog", false);
            SetEnumField(duty, "DutyExecutionOptions", "Early");
            SetEnumField(duty, "GiveUpOptions", "GiveUpInstantly");

            // Null-array hygiene — the engine indexes these without guards.
            Reflect.SetMember(duty, "DutyTags", Array.CreateInstance(_npcDutyTagType, 0));
            Reflect.SetMember(duty, "OnCancelActions", Array.CreateInstance(_npcDutyActionType, 0));

            // Patrolling is unconditional; the only per-guard gate in Wave 1 is Vane's
            // shift, which belongs on ValidTimesOfDay (NPCDuty), NOT on the ref's
            // NPCDutyWeights.TimeOfDayWeightModifiers — the latter only nudges weight,
            // it cannot make a duty unselectable (§10.8.0.1, ValidTimesOfDay note).
            Reflect.SetMember(duty, "DutyConditions", EmptyCondition());
            Reflect.SetMember(duty, "ValidTimesOfDay", beat.NightOnly
                ? BuildHourWindow(NightShiftStartHour, NightShiftEndHour)
                : beat.DayShiftOnly
                    ? BuildHourWindow(DayShiftStartHour, DayShiftEndHour)
                    : Array.CreateInstance(_inGameTimeConditionType, 0));

            var move = NewMoveAction(beat.DutyUid + "_Move", envCards);
            if (move == null) return null;

            var sequence = Array.CreateInstance(_npcDutyActionType, 1);
            sequence.SetValue(move, 0);
            Reflect.SetMember(duty, "ActionSequence", sequence);
            return duty;
        }

        private static object NewMoveAction(string soName, List<object> envCards)
        {
            var action = ScriptableObject.CreateInstance(_moveDutyActionType);
            action.name = soName;

            // §10.8.0.1 rule 5 — never flag a step for selection-time checking beyond
            // step 0 (which is always checked anyway).
            Reflect.SetMember(action, "RequiredForSelectingDuty", false);

            SetEnumField(action, "MovementType", "Pathfind");
            SetEnumField(action, "MoveDestination", "MoveToSpecificEnvironment");
            // Random, NOT Closest: with Closest, MoveDutyAction.FindDestination short-circuits
            // to the guard's CURRENT env the moment it is one of the listed beats
            // (.decomp/MoveDutyAction.cs:831-838) and the guard never walks anywhere again.
            SetEnumField(action, "DestinationSelection", "Random");
            Reflect.SetMember(action, "MoveAwayFromDestination", false);
            Reflect.SetMember(action, "IgnoreCurrentLocation", false);
            Reflect.SetMember(action, "LeaveTracks", false);
            Reflect.SetMember(action, "DontMoveToThisDuty", true);

            var envArray = Array.CreateInstance(_cardDataType, envCards.Count);
            for (int i = 0; i < envCards.Count; i++) envArray.SetValue(envCards[i], i);
            Reflect.SetMember(action, "MoveToEnvironments", envArray);

            // Null-array hygiene — MoveDutyAction iterates all of these without null
            // guards (mirrors DutyBuilder.NewAction<T> and AshPartnerDutyPatch).
            Reflect.SetMember(action, "MoveToTags", Array.CreateInstance(_cardTagType, 0));
            Reflect.SetMember(action, "MoveToItems", Array.CreateInstance(_cardOrTagRefWithDurabilitiesType, 0));
            Reflect.SetMember(action, "MoveToDutyCards", Array.CreateInstance(_npcDutyType, 0));
            Reflect.SetMember(action, "MoveCosts", Array.CreateInstance(_npcStatInstantModifierType, 0));
            SetEnumField(action, "CostRequirements", "IgnoreCost");

            return action;
        }

        private static object BuildDutyRef(object duty, int baseWeight, object activatingConditions = null)
        {
            var dutyRef = Activator.CreateInstance(_npcDutyRefType);
            Reflect.SetMember(dutyRef, "TargetDuty", duty);
            // See the class doc: ActivateAutomatically (not AlwaysActive) is what
            // re-activates the patrol after a save/reload.
            SetEnumField(dutyRef, "ActivatingMode", "ActivateAutomatically");
            Reflect.SetMember(dutyRef, "StartActive", true);
            Reflect.SetMember(dutyRef, "ActivatingConditions", activatingConditions ?? EmptyCondition());
            Reflect.SetMember(dutyRef, "PreferenceWeights", EmptyWeights(baseWeight));
            return dutyRef;
        }

        // ── warden rotation (§10.8.7.1) ─────────────────────────────────────────

        /// <summary>
        /// "Stand watch at the jail." Structurally the patrol duty with three differences: a
        /// single fixed destination (<see cref="WardenPostEnvUid"/>), a higher weight so it wins
        /// inside its shift, and a shift block on <c>ValidTimesOfDay</c> so it is unselectable
        /// outside it.
        ///
        /// <para>The four shift blocks are fixed and non-overlapping and together tile all 24
        /// hours, which is how "at least one guard is normally on warden duty" is guaranteed —
        /// deliberately NOT cross-agent <c>RequiredNPCActiveDuties</c> coordination, which §10.8.7.1
        /// rules out for this purpose (<c>NPCDutyRef.IsActive</c> reports selection eligibility,
        /// not what the guard is currently doing).</para>
        ///
        /// <para>Two gates beyond the shift, both on <c>DutyConditions</c> (arrays are AND'd):
        /// the warden-absence window must be closed (<see cref="WardenGapStatUid"/> == 0), and a
        /// guard the player has already put down does not report for duty. The absence window is
        /// enforced HERE rather than by having <see cref="JailPatch"/> lie about the flag, so the
        /// guards visibly walk off the post when the window opens.</para>
        /// </summary>
        private static object BuildWardenDuty(GuardBeat beat, object wardenEnv)
        {
            var duty = ScriptableObject.CreateInstance(_npcDutyType);
            duty.name = beat.WardenDutyUid;
            Reflect.SetMemberAny(duty, beat.WardenDutyUid, "UniqueID", "uniqueID", "m_UniqueID");

            var localizedName = Activator.CreateInstance(_localizedStringType);
            Reflect.SetMember(localizedName, "ParentObjectID", "");
            Reflect.SetMember(localizedName, "LocalizationKey", "IGNOREKEY");
            Reflect.SetMember(localizedName, "DefaultText", beat.WardenDutyDisplayText);
            Reflect.SetMember(duty, "DutyName", localizedName);

            Reflect.SetMember(duty, "MaxPerformPerDay", 0);
            Reflect.SetMember(duty, "CanOnlyPerformAtHome", false);
            Reflect.SetMember(duty, "CannotPerformWhileHidden", true);
            Reflect.SetMember(duty, "CanPerformWhileInDialog", false);
            SetEnumField(duty, "DutyExecutionOptions", "Early");
            SetEnumField(duty, "GiveUpOptions", "GiveUpInstantly");

            Reflect.SetMember(duty, "DutyTags", Array.CreateInstance(_npcDutyTagType, 0));
            Reflect.SetMember(duty, "OnCancelActions", Array.CreateInstance(_npcDutyActionType, 0));
            Reflect.SetMember(duty, "ValidTimesOfDay",
                BuildHourWindow(beat.WardenShiftStartHour, beat.WardenShiftEndHour));

            var conditions = EmptyCondition();
            if (_wardenGapStat != null)
            {
                var trigger = Activator.CreateInstance(_statValueTriggerType);
                Reflect.SetMember(trigger, "Stat", _wardenGapStat);
                Reflect.SetMember(trigger, "TriggerRange", new Vector2(-0.5f, 0.5f));
                var triggerArray = Array.CreateInstance(_statValueTriggerType, 1);
                triggerArray.SetValue(trigger, 0);
                Reflect.SetMember(conditions, "RequiredStatValues", triggerArray);
            }
            else
            {
                // Not fatal — the rotation still works, there is just never an escape window.
                Plugin.Logger.LogWarning(
                    $"[GuardDutyPatch] GameStat '{WardenGapStatUid}' not found — {beat.Label} will never " +
                    "leave the jail for a warden-absence window.");
            }
            if (_guardDownedStat != null)
                Reflect.SetMember(conditions, "RequiredNPCStatValues", NotDownedCondition());
            if (!Reflect.SetMember(duty, "DutyConditions", conditions))
                Plugin.Logger.LogWarning(
                    $"[GuardDutyPatch] NPCDuty.DutyConditions not found on {beat.WardenDutyUid} — the warden " +
                    "post will be held even during an absence window, so the jail is never diggable.");

            var envCards = new List<object> { wardenEnv };
            var move = NewMoveAction(beat.WardenDutyUid + "_Move", envCards);
            if (move == null) return null;

            var sequence = Array.CreateInstance(_npcDutyActionType, 1);
            sequence.SetValue(move, 0);
            Reflect.SetMember(duty, "ActionSequence", sequence);
            return duty;
        }

        // ── pursuit (§10.8.5) ───────────────────────────────────────────────────

        /// <summary>
        /// The chase: pathfind node-to-node toward the player's LIVE environment, then start
        /// this guard's own Encounter once actually standing in it.
        ///
        /// Both gates are the engine's, not ours: <c>DutyConditions</c> is re-evaluated every
        /// duty-selection tick (<c>NPCDutySelectionInfo.PopulateReport</c>, .decomp line 199),
        /// so the moment the guard's own env stops carrying the village-territory tag the duty
        /// leaves the candidate list and Patrol pulls her home. Expected and accepted quirk:
        /// selection is checked per tick but movement already happened, so she can overshoot
        /// the boundary by ONE node before turning back.
        /// </summary>
        private static object BuildChaseDuty(GuardBeat beat)
        {
            var duty = ScriptableObject.CreateInstance(_npcDutyType);
            duty.name = beat.ChaseDutyUid;
            Reflect.SetMemberAny(duty, beat.ChaseDutyUid, "UniqueID", "uniqueID", "m_UniqueID");

            var localizedName = Activator.CreateInstance(_localizedStringType);
            Reflect.SetMember(localizedName, "ParentObjectID", "");
            Reflect.SetMember(localizedName, "LocalizationKey", "IGNOREKEY");
            Reflect.SetMember(localizedName, "DefaultText", beat.ChaseDutyDisplayText);
            Reflect.SetMember(duty, "DutyName", localizedName);

            Reflect.SetMember(duty, "MaxPerformPerDay", 0);
            Reflect.SetMember(duty, "CanOnlyPerformAtHome", false);
            Reflect.SetMember(duty, "CannotPerformWhileHidden", true);
            Reflect.SetMember(duty, "CanPerformWhileInDialog", false);
            SetEnumField(duty, "DutyExecutionOptions", "Early");
            SetEnumField(duty, "GiveUpOptions", "GiveUpInstantly");

            Reflect.SetMember(duty, "DutyTags", Array.CreateInstance(_npcDutyTagType, 0));
            Reflect.SetMember(duty, "OnCancelActions", Array.CreateInstance(_npcDutyActionType, 0));
            Reflect.SetMember(duty, "ValidTimesOfDay", Array.CreateInstance(_inGameTimeConditionType, 0));

            // Crime >= 60 (the player's own GameStat — GeneralCondition falls through to
            // StatsDict for a mod stat with no NPCStat mapping, .decomp/GeneralCondition.cs:766-773)
            // AND [unless this is Vane, the one permanent exception, §10.8.11.2] the guard's own
            // env carries the village-territory tag AND this guard is not currently down.
            // Arrays are AND'd.
            var conditions = CrimeGateCondition();
            if (!beat.IsUnrestrictedSeeker)
            {
                var tagArray = Array.CreateInstance(_cardTagType, 1);
                tagArray.SetValue(_villageTerritoryTag, 0);
                Reflect.SetMember(conditions, "RequiredEnvironmentTags", tagArray);
            }
            if (!Reflect.SetMember(conditions, "RequiredNPCStatValues", NotDownedCondition()))
                Plugin.Logger.LogWarning(
                    $"[GuardDutyPatch] GeneralCondition.RequiredNPCStatValues not found — {beat.Label} " +
                    "will keep chasing after being beaten, and the gauntlet can never complete.");
            if (!Reflect.SetMember(duty, "DutyConditions", conditions))
            {
                Plugin.Logger.LogWarning($"[GuardDutyPatch] NPCDuty.DutyConditions not found on {beat.ChaseDutyUid} — the chase would be UNGATED. Refusing to build it.");
                return null;
            }

            // [0] walk toward the player, [1] fight them once in the same env — or, for Vane,
            // call the rest of the Watch in instead of fighting (§10.8.11.2).
            var move = NewMoveAction(beat.ChaseDutyUid + "_Move", new List<object>());
            if (move == null) return null;
            SetEnumField(move, "MoveDestination", "MoveToPlayer");
            // MoveToPlayer ignores MoveToEnvironments entirely, so DestinationSelection is
            // irrelevant here; DontMoveToThisDuty likewise only applies to duty-card targets.
            Reflect.SetMember(move, "DontMoveToThisDuty", false);

            object secondStep;
            if (beat.IsUnrestrictedSeeker)
            {
                secondStep = BuildAlarmCallAction(beat.ChaseDutyUid + "_Alarm", _guardsSummonedStat);
                if (secondStep == null)
                {
                    Plugin.Logger.LogWarning($"[GuardDutyPatch] Could not build {beat.Label}'s alarm-call action. Refusing to build the chase.");
                    return null;
                }
            }
            else
            {
                var startEncounter = ScriptableObject.CreateInstance(_startEncounterDutyActionType);
                startEncounter.name = beat.ChaseDutyUid + "_Encounter";
                // §10.8.0.1 rule 5 — MUST stay false. CanBePerformed returns false until the guard
                // is already in the player's env, so flagging it makes the chase unselectable
                // forever (.decomp/NPCDuty.cs:100-110 + StartEncounterDutyAction.cs:16-20).
                Reflect.SetMember(startEncounter, "RequiredForSelectingDuty", false);
                Reflect.SetMember(startEncounter, "CanStartFromDifferentLocation", false);
                Reflect.SetMember(startEncounter, "SkipEncounterEvent", false);
                Reflect.SetMember(startEncounter, "DroppedEncounter", beat.ChaseEncounter);
                secondStep = startEncounter;
            }

            var sequence = Array.CreateInstance(_npcDutyActionType, 2);
            sequence.SetValue(move, 0);
            sequence.SetValue(secondStep, 1);
            Reflect.SetMember(duty, "ActionSequence", sequence);
            return duty;
        }

        /// <summary>
        /// Vane's alarm call (§10.8.11.2) — a <c>ChangeStatDutyAction</c> that sets
        /// <see cref="GuardsSummonedStatUid"/> to 1 instead of starting a fight.
        /// <c>ChangeOverTicks</c> is deliberately left at its zeroed/inactive default so the
        /// modifier applies instantly on one pass (.decomp/ChangeStatDutyAction.cs:11-25,60-67) —
        /// no waiting step, no JSON-authorable counterpart (this action type has no string-typed
        /// fields the framework's WarpResolver could target, same as every other NPCDutyAction in
        /// this file — §10.8.0.1 rule 1).
        /// </summary>
        private static object BuildAlarmCallAction(string soName, object triggerStat)
        {
            if (triggerStat == null) return null;

            var action = ScriptableObject.CreateInstance(_changeStatDutyActionType);
            action.name = soName;
            // §10.8.0.1 rule 5.
            Reflect.SetMember(action, "RequiredForSelectingDuty", false);

            var statMod = Activator.CreateInstance(_statModifierType);
            Reflect.SetMember(statMod, "Stat", triggerStat);
            Reflect.SetMember(statMod, "ValueModifier", new Vector2(1f, 1f));
            Reflect.SetMember(statMod, "InstantModifier", true);
            Reflect.SetMember(statMod, "IgnoreNovelty", true);
            var statModArray = Array.CreateInstance(_statModifierType, 1);
            statModArray.SetValue(statMod, 0);
            if (!Reflect.SetMember(action, "StatModifiers", statModArray))
                return null;

            return action;
        }

        /// <summary>
        /// Captain Sterling's three summon-response duties (§10.8.11.4/§10.8.11.9) — the same
        /// shape as <see cref="BuildChaseDuty"/> minus the territory tag, gated on one of the two
        /// signal stats (== 1) instead of Crime, and optionally on a second
        /// <c>StatValueTrigger</c> (<paramref name="extraGateTrigger"/>) that splits the
        /// caught-alone path into its lenient and forced halves.
        /// </summary>
        private static object BuildSummonDuty(GuardBeat beat, string dutyUid, string dutyDisplayText,
            object triggerStat, object targetEncounter, object extraGateTrigger = null)
        {
            var duty = ScriptableObject.CreateInstance(_npcDutyType);
            duty.name = dutyUid;
            Reflect.SetMemberAny(duty, dutyUid, "UniqueID", "uniqueID", "m_UniqueID");

            var localizedName = Activator.CreateInstance(_localizedStringType);
            Reflect.SetMember(localizedName, "ParentObjectID", "");
            Reflect.SetMember(localizedName, "LocalizationKey", "IGNOREKEY");
            Reflect.SetMember(localizedName, "DefaultText", dutyDisplayText);
            Reflect.SetMember(duty, "DutyName", localizedName);

            Reflect.SetMember(duty, "MaxPerformPerDay", 0);
            Reflect.SetMember(duty, "CanOnlyPerformAtHome", false);
            Reflect.SetMember(duty, "CannotPerformWhileHidden", true);
            Reflect.SetMember(duty, "CanPerformWhileInDialog", false);
            SetEnumField(duty, "DutyExecutionOptions", "Early");
            SetEnumField(duty, "GiveUpOptions", "GiveUpInstantly");

            Reflect.SetMember(duty, "DutyTags", Array.CreateInstance(_npcDutyTagType, 0));
            Reflect.SetMember(duty, "OnCancelActions", Array.CreateInstance(_npcDutyActionType, 0));
            Reflect.SetMember(duty, "ValidTimesOfDay", Array.CreateInstance(_inGameTimeConditionType, 0));

            // Trigger stat == 1 AND this guard is not currently down. Deliberately no
            // RequiredEnvironmentTags — Sterling responds to a summon regardless of where in the
            // world it was raised (§10.8.11.4).
            var conditions = TriggerStatGateCondition(triggerStat, extraGateTrigger);
            if (!Reflect.SetMember(conditions, "RequiredNPCStatValues", NotDownedCondition()))
                Plugin.Logger.LogWarning(
                    $"[GuardDutyPatch] GeneralCondition.RequiredNPCStatValues not found — {beat.Label} " +
                    "will keep responding to a summon after being beaten.");
            if (!Reflect.SetMember(duty, "DutyConditions", conditions))
            {
                Plugin.Logger.LogWarning($"[GuardDutyPatch] NPCDuty.DutyConditions not found on {dutyUid} — the summon duty would be UNGATED. Refusing to build it.");
                return null;
            }

            var move = NewMoveAction(dutyUid + "_Move", new List<object>());
            if (move == null) return null;
            SetEnumField(move, "MoveDestination", "MoveToPlayer");
            Reflect.SetMember(move, "DontMoveToThisDuty", false);

            var startEncounter = ScriptableObject.CreateInstance(_startEncounterDutyActionType);
            startEncounter.name = dutyUid + "_Encounter";
            Reflect.SetMember(startEncounter, "RequiredForSelectingDuty", false);
            Reflect.SetMember(startEncounter, "CanStartFromDifferentLocation", false);
            Reflect.SetMember(startEncounter, "SkipEncounterEvent", false);
            Reflect.SetMember(startEncounter, "DroppedEncounter", targetEncounter);

            var sequence = Array.CreateInstance(_npcDutyActionType, 2);
            sequence.SetValue(move, 0);
            sequence.SetValue(startEncounter, 1);
            Reflect.SetMember(duty, "ActionSequence", sequence);
            return duty;
        }

        /// <summary>A <c>GeneralCondition</c> carrying "this stat is (approximately) 1" — the
        /// same width-1-range "== N" idiom <see cref="BuildWardenDuty"/> uses for "== 0" — plus,
        /// optionally, one extra <c>StatValueTrigger</c> ANDed onto it. <c>RequiredStatValues</c>
        /// entries are conjunctive, which is what makes the two finish-arrest duties in
        /// §10.8.11.9 mutually exclusive off a single shared summon stat.</summary>
        private static object TriggerStatGateCondition(object stat, object extraTrigger = null)
        {
            var condition = EmptyCondition();
            var trigger = Activator.CreateInstance(_statValueTriggerType);
            Reflect.SetMember(trigger, "Stat", stat);
            Reflect.SetMember(trigger, "TriggerRange", new Vector2(0.5f, 1.5f));

            var triggerArray = Array.CreateInstance(_statValueTriggerType, extraTrigger == null ? 1 : 2);
            triggerArray.SetValue(trigger, 0);
            if (extraTrigger != null) triggerArray.SetValue(extraTrigger, 1);
            Reflect.SetMember(condition, "RequiredStatValues", triggerArray);
            return condition;
        }

        /// <summary>
        /// §10.8.11.9 — "the player still has at least one 'Think better of it' left", i.e.
        /// <see cref="SterlingEscapeCountStatUid"/> is 0, 1 or 2.
        ///
        /// <para>The range is bounded at 3.5 rather than an open-ended sentinel because the stat
        /// is clamped 0-3 by its own <c>MinMaxValue</c> — a huge upper bound would only reintroduce
        /// the float-precision hazard the <see cref="CrimeRangeMax"/> comment warns about
        /// (reference_triggerrange_fractional_overflow) for no reachable values.</para>
        /// </summary>
        private static object ChancesRemainingTrigger()
        {
            if (_sterlingEscapeCountStat == null) return null;
            var trigger = Activator.CreateInstance(_statValueTriggerType);
            Reflect.SetMember(trigger, "Stat", _sterlingEscapeCountStat);
            Reflect.SetMember(trigger, "TriggerRange", new Vector2(-0.5f, SterlingChancesAllowed - 0.5f));
            return trigger;
        }

        /// <summary>§10.8.11.9 — "all three chances are used up". The exact complement of
        /// <see cref="ChancesRemainingTrigger"/>, so exactly one of Sterling's two finish-arrest
        /// duties is selectable at any moment.</summary>
        private static object ChancesExhaustedTrigger()
        {
            if (_sterlingEscapeCountStat == null) return null;
            var trigger = Activator.CreateInstance(_statValueTriggerType);
            Reflect.SetMember(trigger, "Stat", _sterlingEscapeCountStat);
            Reflect.SetMember(trigger, "TriggerRange", new Vector2(SterlingChancesAllowed - 0.5f, SterlingChancesAllowed + 0.5f));
            return trigger;
        }

        /// <summary>
        /// "This guard's own <c>cmcNpcStatGuardDowned</c> is exactly 0" — a bested guard drops out
        /// of the chase until their season timer expires (§10.8.6).
        ///
        /// <para><c>UseAssociatedAgent</c> resolves against the evaluated card's owner:
        /// <c>NPCDutySelectionInfo.PopulateReport</c> calls
        /// <c>ConditionsValid(..., NPC.AssociatedCard, null, NPC)</c>, and
        /// <c>GeneralCondition</c>'s RequiredNPCStatValues branch takes the
        /// <c>_FromCard.NPCModel</c> path (.decomp/GeneralCondition.cs:786-800) — i.e. the guard
        /// being asked whether to chase. An EXPLICIT TargetAgent would be wrong here: that branch
        /// scans <c>AllNPCs</c> and returns false outright if the named agent has not spawned
        /// (:817-831), which would silently disable the chase before the guards are placed.</para>
        ///
        /// <para>Range (0,0) rather than (0,0.5): <c>ExtraMath.FloatIsInRange</c> with
        /// <c>RoundingMethods.Round</c> compares rounded ints when the range width is 0, so this
        /// is an exact "== 0" with no sub-unit scaling to reason about
        /// (.decomp/ExtraMath.cs:144-166).</para>
        /// </summary>
        private static Array NotDownedCondition()
        {
            var condition = Activator.CreateInstance(_npcStatConditionType);
            Reflect.SetMember(condition, "UseAssociatedAgent", true);
            Reflect.SetMember(condition, "TargetStat", _guardDownedStat);
            Reflect.SetMember(condition, "ConditionRange", Vector2.zero);
            var arr = Array.CreateInstance(_npcStatConditionType, 1);
            arr.SetValue(condition, 0);
            return arr;
        }

        /// <summary>A <c>GeneralCondition</c> carrying only "player's Crime is in the Enemy band".</summary>
        private static object CrimeGateCondition()
        {
            var condition = EmptyCondition();
            var trigger = Activator.CreateInstance(_statValueTriggerType);
            Reflect.SetMember(trigger, "Stat", _crimeStat);
            Reflect.SetMember(trigger, "TriggerRange", new Vector2(EnemyCrimeThreshold, CrimeRangeMax));
            var triggerArray = Array.CreateInstance(_statValueTriggerType, 1);
            triggerArray.SetValue(trigger, 0);
            Reflect.SetMember(condition, "RequiredStatValues", triggerArray);
            return condition;
        }

        /// <summary>
        /// Creates the village-territory CardTag and stamps it onto every CMC village node.
        ///
        /// The tag is built in C# rather than shipped as <c>ScriptableObject/CardTag/*.json</c>
        /// on purpose: <c>CardData.HasTag</c> compares tags by INSTANCE
        /// (.decomp/CardData.cs:1896-1902), and this pass is the only consumer on both sides
        /// (it stamps the cards and it fills <c>RequiredEnvironmentTags</c>), so owning the one
        /// instance outright removes the loader-instance-identity class of failure entirely
        /// (reference_ingamecard_cache_divergence).
        ///
        /// Each card gets a NEW array rather than an in-place append: these nodes are CLONES of
        /// vanilla envs and their <c>CardTags</c> array may still be the template's own
        /// reference — appending in place would tag vanilla environments too.
        /// </summary>
        private static void EnsureVillageTerritoryTag()
        {
            if (_villageTerritoryTag != null) return;

            var tag = ScriptableObject.CreateInstance(_cardTagType);
            if (tag == null) return;
            tag.name = VillageTerritoryTagName;
            // Null-array hygiene, same doctrine as the duty actions above. Today's two
            // consumers (CardAction.cs:616, BlueprintConstructionPopup.cs:519) do null-check
            // these, but a purely-marker tag costs nothing to make safe for the ones that
            // might not.
            foreach (var field in _cardTagType.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!field.FieldType.IsArray) continue;
                field.SetValue(tag, Array.CreateInstance(field.FieldType.GetElementType(), 0));
            }

            // Pass 1: resolve every card and stage its replacement CardTags array WITHOUT
            // writing anything yet. If ANY card in this batch fails to resolve, `tag` is
            // destroyed below with zero cards holding a reference to it — safe to retry
            // next poll with a fresh instance. Writing as each card resolved (the prior
            // single-pass version) let an earlier success in the SAME failed attempt keep
            // a live reference to `tag` even after it was destroyed on the later failure,
            // and every retry stamped a NEW tag on top without ever clearing the stale one.
            var missing = new List<string>();
            var stagedCards = new List<object>();
            var stagedExisting = new List<Array>();
            var stagedReplacements = new List<Array>();
            foreach (var cardUid in VillageTerritoryCardUids)
            {
                var card = CardUtil.GetCardDataById(cardUid);
                if (card == null) { missing.Add(cardUid); continue; }

                if (Reflect.GetMember(card, "CardTags") is not Array existing)
                {
                    missing.Add(cardUid + "(no CardTags field)");
                    continue;
                }
                var replacement = Array.CreateInstance(_cardTagType, existing.Length + 1);
                Array.Copy(existing, replacement, existing.Length);
                replacement.SetValue(tag, existing.Length);
                stagedCards.Add(card);
                stagedExisting.Add(existing);
                stagedReplacements.Add(replacement);
            }

            if (missing.Count > 0)
            {
                // Partial resolution is a correctness bug, not a degradation: an untagged node
                // is a hole the guard will refuse to chase into. Retry on the next poll instead.
                // Nothing was written above, so discarding `tag` here leaves no dangling refs.
                Plugin.Logger.LogWarning($"[GuardDutyPatch] Village territory tag not stamped on {missing.Count} card(s) yet: {string.Join(", ", missing)}");
                UnityEngine.Object.Destroy(tag);
                return;
            }

            // Pass 2: everything resolved — commit all writes together with the SAME tag
            // instance. If a write unexpectedly fails here (readable-but-not-writable field,
            // effectively unseen in practice), roll back every card already committed this
            // pass before destroying `tag`, so no card is left holding it.
            int stamped = 0;
            for (int i = 0; i < stagedCards.Count; i++)
            {
                if (Reflect.SetMember(stagedCards[i], "CardTags", stagedReplacements[i])) { stamped++; continue; }

                for (int j = 0; j < i; j++)
                {
                    Reflect.SetMember(stagedCards[j], "CardTags", stagedExisting[j]);
                }
                Plugin.Logger.LogWarning($"[GuardDutyPatch] Village territory tag commit failed writing CardTags on card {i} of {stagedCards.Count} — rolled back {i} already-committed card(s) this attempt and will retry next poll.");
                UnityEngine.Object.Destroy(tag);
                return;
            }

            _villageTerritoryTag = tag;
            Plugin.Logger.LogInfo($"[GuardDutyPatch] '{VillageTerritoryTagName}' stamped onto {stamped} village map cards — guards will not pursue past them.");
        }

        // ── shared struct builders ──────────────────────────────────────────────

        /// <summary>
        /// A single wrap-around hour window (TimeType = HourIs). Start &gt; End is read by
        /// the engine as "overnight" — 20 to 5 means 20:00-04:59.
        /// </summary>
        private static Array BuildHourWindow(int startHour, int endHour)
        {
            object condition = Activator.CreateInstance(_inGameTimeConditionType);
            SetEnumField(condition, "TimeType", "HourIs");
            Reflect.SetMember(condition, "StartValue", startHour);
            Reflect.SetMember(condition, "EndValue", endHour);
            var arr = Array.CreateInstance(_inGameTimeConditionType, 1);
            arr.SetValue(condition, 0);
            return arr;
        }

        private static object EmptyCondition()
        {
            object boxed = Activator.CreateInstance(_generalConditionType);
            foreach (var field in _generalConditionType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!field.FieldType.IsArray) continue;
                field.SetValue(boxed, Array.CreateInstance(field.FieldType.GetElementType(), 0));
            }
            return boxed;
        }

        private static object EmptyWeights(int baseWeight)
        {
            object boxed = Activator.CreateInstance(_npcDutyWeightsType);
            foreach (var field in _npcDutyWeightsType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!field.FieldType.IsArray) continue;
                field.SetValue(boxed, Array.CreateInstance(field.FieldType.GetElementType(), 0));
            }
            // §10.8.0.1 rule 3 — TotalWeight <= 0 is permanently unselectable, silently.
            var baseWeightField = _npcDutyWeightsType.GetField("BaseWeight", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            baseWeightField?.SetValue(boxed, baseWeight);
            return boxed;
        }

        private static void SetEnumField(object instance, string fieldName, string enumValueName)
        {
            var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
            {
                Plugin.Logger.LogWarning($"[GuardDutyPatch] Enum field '{fieldName}' not found on {instance.GetType().Name}.");
                return;
            }
            try
            {
                field.SetValue(instance, Enum.Parse(field.FieldType, enumValueName));
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[GuardDutyPatch] Failed to set {instance.GetType().Name}.{fieldName} = {enumValueName}: {ex.Message}");
            }
        }

        // ── diagnostics ─────────────────────────────────────────────────────────

        /// <summary>
        /// One-shot per-guard duty dump, at Info (never Debug — BepInEx's default filter
        /// suppresses Debug, and a suppressed diagnostic costs a whole restart cycle).
        /// The engine builds its own human-readable reason string in
        /// <c>NPCDutySelectionInfo.GetNotSelectableReport</c>, which distinguishes all of
        /// §10.8.0.1's silent failure modes by name ("Weight is negative", "Required
        /// actions are not doable", "Daytime conditions are not valid", ...), so this
        /// prints that verbatim rather than re-deriving it.
        ///
        /// Fires once per guard per session, the first tick that guard's live NPC has a
        /// populated WeightInfo (the engine only fills it inside its own duty-selection
        /// tick, for ACTIVE duties — before then it reports "SPEND A TICK TO FILL
        /// REPORTS"). Set the EnableGuardDiagnostics config flag to keep re-reporting
        /// every poll while tuning.
        /// </summary>
        private static class GuardDutyDiagnostics
        {
            private static readonly HashSet<string> _reported = new HashSet<string>();
            private static MethodInfo _notSelectableReport;

            public static void Run()
            {
                try
                {
                    if (!_dutiesBuilt) return;
                    bool verbose = Plugin.EnableGuardDiagnostics != null && Plugin.EnableGuardDiagnostics.Value;
                    if (!verbose && _reported.Count >= Beats.Length) return;

                    var gm = CardUtil.GetGameManagerInstance();
                    if (gm == null) return;
                    if (Reflect.GetMember(gm, "AllNPCs") is not IEnumerable allNpcs) return;

                    foreach (var beat in Beats)
                    {
                        if (!verbose && _reported.Contains(beat.AgentUid)) continue;

                        object npc = null;
                        foreach (var candidate in allNpcs)
                        {
                            if (candidate == null) continue;
                            if (ReferenceEquals(Reflect.GetMember(candidate, "NPCModel"), beat.Agent)) { npc = candidate; break; }
                        }
                        if (npc == null) continue; // not spawned yet

                        var report = Describe(beat, npc, out bool weightInfoReady);
                        if (!verbose && !weightInfoReady) continue; // wait for the engine to fill its report

                        if (!verbose) _reported.Add(beat.AgentUid);
                        Plugin.Logger.LogDebug(report);
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogWarning($"[GuardDutyPatch] Diagnostics failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
                }
            }

            private static string Describe(GuardBeat beat, object npc, out bool weightInfoReady)
            {
                weightInfoReady = false;
                bool inCombat = Reflect.GetBool(npc, "IsInCombat");
                var sb = new StringBuilder();
                var npcEnv = Reflect.GetMember(npc, "CurrentEnvironment");
                sb.Append($"[GuardDutyPatch] {beat.Label} ({beat.AgentUid}) env=")
                  .Append(Reflect.GetMember(npcEnv, "SimpleEnvName") ?? "?")
                  .Append(" suspicion=").Append(NpcStatOf(npc, GuardSuspicionStatUid))
                  .Append(" downed=").Append(NpcStatOf(npc, GuardDownedStatUid))
                  .Append(" inCombat=").Append(inCombat)
                  // The two inputs to the chase's DutyConditions, printed separately so a
                  // "Conditions are not valid" report below can be attributed to one of them
                  // rather than guessed at across a restart cycle. Deliberately NOT
                  // StatAccess.GetCurrentValue(_crimeStat) — _crimeStat is the raw GameStat SO
                  // template, not the per-player runtime instance; reading it directly returns
                  // NaN/garbage. The engine's own DutyConditions check resolves the live value
                  // correctly via instance.StatsDict[Stat] (.decomp/GeneralCondition.cs:761), so
                  // pursuit gating was never affected by this — only this diagnostic line was.
                  .Append(" playerCrime=").Append(_crimeStat == null ? "<stat unresolved>" : VillageCrimePatch.CurrentCrime().ToString("0.##"))
                  .Append(" guardEnvIsVillageTerritory=").Append(EnvHasTerritoryTag(npcEnv))
                  .Append(" playerEnv=").Append(GameQuery.CurrentEnvironmentUniqueId ?? "?");

                if (Reflect.GetMember(npc, "AllDuties") is not IEnumerable duties)
                {
                    sb.Append(" | AllDuties unreadable");
                    return sb.ToString();
                }

                int count = 0;
                foreach (var dutyRef in duties)
                {
                    if (dutyRef == null) continue;
                    count++;
                    var targetDuty = Reflect.GetMember(dutyRef, "TargetDuty");
                    string dutyName = targetDuty == null ? "<null duty>" : (Reflect.GetMember(targetDuty, "name") as string ?? "?");
                    bool active = Reflect.GetBool(dutyRef, "IsActive");

                    sb.AppendLine().Append("    duty '").Append(dutyName).Append("' active=").Append(active)
                      .Append(" currentWeight=").Append(Reflect.GetFloat(dutyRef, "GetCurrentWeight"));

                    var weightInfo = Reflect.GetMember(dutyRef, "WeightInfo");
                    if (weightInfo == null) { sb.Append(" (no WeightInfo yet)"); continue; }

                    if (Reflect.GetBool(weightInfo, "Initialized")) weightInfoReady = true;
                    sb.Append(" totalWeight=").Append(Reflect.GetInt(weightInfo, "TotalWeight"))
                      .Append(" notSelectable=").Append(Reflect.GetBool(weightInfo, "NotSelectable"));

                    _notSelectableReport ??= weightInfo.GetType()
                        .GetMethod("GetNotSelectableReport", BindingFlags.Instance | BindingFlags.Public);
                    if (_notSelectableReport == null) continue;
                    if (_notSelectableReport.Invoke(weightInfo, new object[] { active, inCombat }) is string reason
                        && !string.IsNullOrWhiteSpace(reason))
                    {
                        sb.AppendLine().Append("      ").Append(reason.Replace("\r", "").Replace("\n", " ").Trim());
                        // "Required actions are not doable" is always step 0's CanBePerformed()
                        // (.decomp/NPCDuty.cs:106 checks index 0 unconditionally, regardless of
                        // RequiredForSelectingDuty). Every guard duty starts with a MoveDutyAction
                        // (BuildPatrolDuty/BuildWardenDuty/BuildChaseDuty), so re-run its own
                        // FindDestination as a read-only probe to see exactly where the pathfind
                        // breaks down — temporary, until the root cause of the guards never moving
                        // is confirmed against a live log.
                        if (reason.Contains("Required actions are not doable") && targetDuty != null)
                            sb.AppendLine().Append("      ").Append(ProbeMove(npc, targetDuty));
                    }
                }

                if (count == 0) sb.Append(" | NO DUTIES ATTACHED (spawned before DutiesReady?)");
                return sb.ToString();
            }

            private static MethodInfo _findDestinationMethod;
            private static bool _findDestinationMissingLogged;

            /// <summary>
            /// Read-only probe: re-runs step 0's own <c>MoveDutyAction.FindDestination</c>
            /// (.decomp/MoveDutyAction.cs:327) exactly as the engine's own
            /// <c>CanBePerformed</c> does (.decomp/MoveDutyAction.cs:248), and reports the
            /// resulting <c>MapPath</c>'s validity/step count/end env — the three fields that
            /// decide whether "Required actions are not doable" fires. Diagnostic only; does
            /// not mutate NPC or world state.
            /// </summary>
            private static string ProbeMove(object npc, object targetDuty)
            {
                try
                {
                    if (Reflect.GetMember(targetDuty, "ActionSequence") is not Array sequence || sequence.Length == 0)
                        return "move-probe: duty has no ActionSequence";
                    var step0 = sequence.GetValue(0);
                    if (step0 == null) return "move-probe: step 0 is null";
                    if (_moveDutyActionType == null || step0.GetType() != _moveDutyActionType)
                        return $"move-probe: step 0 is a {step0.GetType().Name}, not MoveDutyAction — skipping";

                    _findDestinationMethod ??= _moveDutyActionType.GetMethod("FindDestination",
                        BindingFlags.Instance | BindingFlags.Public);
                    if (_findDestinationMethod == null)
                    {
                        if (!_findDestinationMissingLogged)
                        {
                            _findDestinationMissingLogged = true;
                            Plugin.Logger.LogWarning("[GuardDutyPatch] MoveDutyAction.FindDestination not found — move-probe disabled.");
                        }
                        return "move-probe: FindDestination method not found";
                    }

                    var mapPath = _findDestinationMethod.Invoke(step0, new object[] { npc, targetDuty, null });
                    if (mapPath == null) return "move-probe: FindDestination returned null";

                    bool isValid = Reflect.GetBool(mapPath, "IsValid");
                    int stepCount = Reflect.GetInt(mapPath, "StepCount");
                    var end = Reflect.GetMember(mapPath, "End");
                    bool endIsNull = end == null || Reflect.GetBool(end, "IsNull");
                    var npcEnv = Reflect.GetMember(npc, "CurrentEnvironment");
                    bool sameEnvAsNpc = npcEnv != null && end != null
                        && npcEnv.GetType().GetMethod("MatchesEnv", BindingFlags.Instance | BindingFlags.Public) is { } matchesEnv
                        && matchesEnv.Invoke(npcEnv, new[] { end }) is true;

                    return $"move-probe: MoveDestination={Reflect.GetMember(step0, "MoveDestination")} " +
                           $"MovementType={Reflect.GetMember(step0, "MovementType")} " +
                           $"mapPath.IsValid={isValid} StepCount={stepCount} End.IsNull={endIsNull} " +
                           $"End.MatchesNpcEnv={sameEnvAsNpc}";
                }
                catch (Exception ex)
                {
                    return $"move-probe FAILED: {ex.InnerException?.Message ?? ex.Message}";
                }
            }

            private static MethodInfo _envHasTag;

            /// <summary>
            /// Asks the ENGINE whether the guard's env carries the territory tag, via the same
            /// <c>GameManager.EnvironmentHasTag</c> call <c>GeneralCondition</c> itself uses
            /// (.decomp/GeneralCondition.cs:441-461) — so this reports what the gate sees, not
            /// what our own stamping pass believes it did.
            /// </summary>
            private static string EnvHasTerritoryTag(object npcEnv)
            {
                if (_villageTerritoryTag == null) return "<tag not built>";
                if (npcEnv == null) return "<no env>";
                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return "<no GameManager>";
                _envHasTag ??= gm.GetType().GetMethod("EnvironmentHasTag", BindingFlags.Instance | BindingFlags.Public);
                if (_envHasTag == null) return "<EnvironmentHasTag missing>";
                return _envHasTag.Invoke(gm, new[] { npcEnv, _villageTerritoryTag, null }).ToString();
            }

            private static string NpcStatOf(object npc, string statUid)
            {
                var stat = _getFromIdMethod?.Invoke(null, new object[] { statUid });
                if (stat == null) return "<stat unresolved>";
                var getStatValue = npc.GetType().GetMethod("GetStatValue", BindingFlags.Instance | BindingFlags.Public,
                    null, new[] { stat.GetType() }, null);
                if (getStatValue == null) return "<GetStatValue missing>";
                var value = getStatValue.Invoke(npc, new[] { stat });
                return value == null ? "<null>" : value.ToString();
            }
        }

        /// <summary>The per-guard detection tuning knob later Wave 2/3 chunks read.</summary>
        internal const string GuardSuspicionStatUid = "cmcNpcStatSuspicion";
    }
}
