using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using HarmonyLib;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Village Guards — witnessed-attack detection (Village_Master_Plan.md §10.8.4.2).
    ///
    /// The Attack affordance itself is pure JSON: each guard's <c>NPCAgent.Interactions[]</c>
    /// carries an <c>NPCInteractionButton</c> with <c>ButtonType: Encounter</c> and a
    /// <c>DroppedEncounterWarpData</c> pointing at that guard's own Encounter asset
    /// (Encounter/cmcEncounterGuard*.json), and the crime penalties are declarative
    /// <c>StatModifier</c>s on those assets — +35 on <c>StartingOptions.FightOptionStatChanges</c>
    /// the instant Fight is confirmed (.decomp/EncounterStartPopup.cs:107-120), +100 on
    /// <c>EnemyDefeatedEffects.StatChanges</c> for a kill. Nothing here starts, scores, or
    /// resolves a fight.
    ///
    /// What this class adds is the ONE thing the effect blocks cannot express: "who saw it."
    /// A StatModifier can raise a global stat, but it cannot enumerate the NPCs standing in the
    /// player's environment at the moment the fight starts. §10.8.4.2 wants a guard who is
    /// present when an attack begins to re-evaluate its own pursuit-arming state immediately
    /// rather than on the next duty tick, so this postfixes
    /// <c>EncounterPopup.StartEncounter(Encounter, InGameNPC, bool)</c> — the same 3-param
    /// method the framework's own EncounterGuardPatch/WildlifeRaidPatch use — and raises
    /// <see cref="AttackStarted"/> plus one <see cref="AttackWitnessed"/> per co-located guard.
    ///
    /// <see cref="AttackWitnessed"/> is consumed by <c>GuardDutyPatch.OnAttackWitnessed</c>,
    /// which force-starts the witnessing guard's own duty-selection coroutine right away instead
    /// of waiting for the next natural tick (chase duty gates on the player's own
    /// <c>cmcStatVillageCrime</c> via <c>GeneralCondition</c>, re-evaluated only once per DTP
    /// tick otherwise — up to 15 in-game minutes of lag). <see cref="ReportIncident"/> stays
    /// callable for any incident in any environment (the Copper Chest theft roll, §10.8.3.6, is
    /// the other intended caller) — victim- and environment-generic by design, not
    /// guard-chase-specific.
    /// </summary>
    internal static class GuardWitnessPatch
    {
        /// <summary>Guard NPCAgent UID → that guard's Encounter UID. Both directions are needed:
        /// the encounter identifies the victim, the agent identifies potential witnesses.</summary>
        private static readonly (string AgentUid, string EncounterUid, string Label)[] Guards =
        {
            ("cmcGuardSterlingAgent", "cmcEncounterGuardSterling", "Captain Reeve Sterling"),
            ("cmcGuardThorneAgent",  "cmcEncounterGuardThorne",  "Guard Nella Thorne"),
            ("cmcGuardCorrinAgent",  "cmcEncounterGuardCorrin",  "Guard Old Corrin"),
            ("cmcGuardVaneAgent",    "cmcEncounterGuardVane",    "Guard Iris Vane"),
        };

        /// <summary>An attack (or other crime) the Watch may react to.</summary>
        internal sealed class Incident
        {
            /// <summary>NPCAgent UID of whoever was attacked, or null for a victimless
            /// incident (theft with no one present).</summary>
            public string VictimAgentUid;

            /// <summary>Encounter UID that started the fight, or null for a non-combat incident.</summary>
            public string EncounterUid;

            /// <summary>CardData UniqueID of the environment the incident happened in.</summary>
            public string EnvironmentUid;
        }

        /// <summary>A guard who was standing in <see cref="Incident.EnvironmentUid"/> when it happened.</summary>
        internal sealed class Witness
        {
            public Incident Incident;
            public string GuardAgentUid;
            public string GuardLabel;

            /// <summary>The live InGameNPC. Typed as object so this file stays reflection-only,
            /// matching GuardSpawnPatch/GuardDutyPatch.</summary>
            public object GuardNpc;
        }

        /// <summary>Raised for every incident, witnessed or not. §10.8.4.2: an unwitnessed
        /// attack is not automatic forgiveness — the crime penalty already applied.</summary>
        internal static event Action<Incident> AttackStarted;

        /// <summary>Raised once per guard co-located with the incident, immediately, so a
        /// pursuit duty can re-arm without waiting for its normal tick cadence.</summary>
        internal static event Action<Witness> AttackWitnessed;

        private static bool _initialized;
        private static bool _bodyTemplateChecked;

        public static void Initialize(Harmony harmony)
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                var encounterPopupType = CardUtil.FindGameType("EncounterPopup");
                if (encounterPopupType == null)
                {
                    Plugin.Logger.LogWarning("[GuardWitnessPatch] EncounterPopup type not found — witnessed-attack detection inactive.");
                    return;
                }

                var startEncounter = encounterPopupType
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == "StartEncounter" && m.GetParameters().Length == 3);
                if (startEncounter == null)
                {
                    Plugin.Logger.LogWarning("[GuardWitnessPatch] EncounterPopup.StartEncounter(3-param) not found — witnessed-attack detection inactive.");
                    return;
                }

                harmony.Patch(startEncounter, postfix: new HarmonyMethod(
                    typeof(GuardWitnessPatch), nameof(StartEncounter_Postfix)));
                Plugin.Logger.LogDebug("[GuardWitnessPatch] initialized.");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[GuardWitnessPatch] Initialize failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // __0 = Encounter, __1 = InGameNPC (null for wildlife), __2 = skip-event flag.
        private static void StartEncounter_Postfix(object __0, object __1)
        {
            if (__0 == null) return;
            try
            {
                var encounterUid = CardUtil.GetCardUniqueId(__0);
                if (string.IsNullOrEmpty(encounterUid)) return;

                var guard = Guards.FirstOrDefault(g =>
                    string.Equals(g.EncounterUid, encounterUid, StringComparison.OrdinalIgnoreCase));
                if (guard.EncounterUid == null) return; // not one of ours

                WarnIfBodyTemplateMissing(__0, guard.Label);
                ReportIncident(guard.AgentUid, encounterUid, GameQuery.CurrentEnvironmentUniqueId);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[GuardWitnessPatch] StartEncounter_Postfix failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        /// <summary>
        /// Environment-generic incident entry point. Raises <see cref="AttackStarted"/>, then
        /// <see cref="AttackWitnessed"/> once per guard whose current environment matches
        /// <paramref name="environmentUid"/>. Safe to call for non-combat incidents
        /// (<paramref name="encounterUid"/> null) and for incidents with no NPC victim.
        /// </summary>
        internal static void ReportIncident(string victimAgentUid, string encounterUid, string environmentUid)
        {
            var incident = new Incident
            {
                VictimAgentUid = victimAgentUid,
                EncounterUid = encounterUid,
                EnvironmentUid = environmentUid,
            };

            try { AttackStarted?.Invoke(incident); }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[GuardWitnessPatch] AttackStarted subscriber threw: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }

            if (string.IsNullOrEmpty(environmentUid))
            {
                Plugin.Logger.LogDebug("[GuardWitnessPatch] incident environment unknown — no witness pass.");
                return;
            }

            foreach (var witness in FindWitnesses(incident))
            {
                Plugin.Logger.LogDebug($"[GuardWitnessPatch] {witness.GuardLabel} witnessed the incident at {environmentUid}.");
                try { AttackWitnessed?.Invoke(witness); }
                catch (Exception ex)
                {
                    Plugin.Logger.LogWarning($"[GuardWitnessPatch] AttackWitnessed subscriber threw: {ex.InnerException?.ToString() ?? ex.ToString()}");
                }
            }
        }

        /// <summary>
        /// Every one of our four guards currently standing in <paramref name="environmentUid"/>,
        /// with no incident involved — the ambient "who's here right now" query
        /// (GuardWantedReactionPatch's Wanted-tier escalate-on-sight poll, §10.8.2). Reuses
        /// <see cref="FindWitnesses"/> rather than re-scanning AllNPCs a second way; the returned
        /// <see cref="Witness.Incident"/> only carries the environment, no victim/encounter.
        /// </summary>
        internal static System.Collections.Generic.List<Witness> FindGuardsAt(string environmentUid)
        {
            if (string.IsNullOrEmpty(environmentUid)) return new System.Collections.Generic.List<Witness>();
            return FindWitnesses(new Incident { EnvironmentUid = environmentUid });
        }

        /// <summary>Every live guard standing in the incident's environment. Never null.</summary>
        private static System.Collections.Generic.List<Witness> FindWitnesses(Incident incident)
        {
            var found = new System.Collections.Generic.List<Witness>();
            var gm = CardUtil.GetGameManagerInstance();
            if (gm == null)
            {
                Plugin.Logger.LogDebug("[GuardWitnessPatch] GameManager unavailable — witness pass skipped.");
                return found;
            }

            if (Reflect.GetMember(gm, "AllNPCs") is not IEnumerable allNpcs)
            {
                Plugin.Logger.LogDebug("[GuardWitnessPatch] GameManager.AllNPCs unreadable — witness pass skipped.");
                return found;
            }

            foreach (var npc in allNpcs)
            {
                if (npc == null) continue;

                var agentUid = CardUtil.GetCardUniqueId(Reflect.GetMember(npc, "NPCModel"));
                if (string.IsNullOrEmpty(agentUid)) continue;

                var guard = Guards.FirstOrDefault(g =>
                    string.Equals(g.AgentUid, agentUid, StringComparison.OrdinalIgnoreCase));
                if (guard.AgentUid == null) continue;

                if (!string.Equals(EnvUidOf(npc), incident.EnvironmentUid, StringComparison.OrdinalIgnoreCase))
                    continue;

                found.Add(new Witness
                {
                    Incident = incident,
                    GuardAgentUid = guard.AgentUid,
                    GuardLabel = guard.Label,
                    GuardNpc = npc,
                });
            }

            return found;
        }

        /// <summary>InGameNPC.CurrentEnvironment is an EnvID struct; its EnvCard is the CT8/CT4
        /// CardData whose UniqueID is comparable with GameQuery.CurrentEnvironmentUniqueId
        /// (.decomp/EnvID.cs:21).</summary>
        private static string EnvUidOf(object npc)
        {
            var envId = Reflect.GetMember(npc, "CurrentEnvironment");
            if (envId == null) return null;
            var envCard = Reflect.GetMember(envId, "EnvCard");
            if (envCard == null) return null;
            return CardUtil.GetCardUniqueId(envCard);
        }

        /// <summary>
        /// One-shot sanity line the first time a guard encounter starts. The guard Encounters
        /// borrow the vanilla "Combat_ BTHuntsman" BodyTemplate, and it is that template's
        /// wound table — not the Encounter asset — that drains Morale and Blood. If the
        /// name-keyed WarpData lookup missed, the fight would misbehave with no other clue.
        /// </summary>
        private static void WarnIfBodyTemplateMissing(object encounter, string label)
        {
            if (_bodyTemplateChecked) return;
            _bodyTemplateChecked = true;

            var template = Reflect.GetMember(encounter, "EnemyBodyTemplate");
            if (template is UnityEngine.Object uo && uo != null) return;
            if (template != null && template is not UnityEngine.Object) return;

            Plugin.Logger.LogWarning(
                $"[GuardWitnessPatch] {label}'s Encounter has no EnemyBodyTemplate — " +
                "'Combat_ BTHuntsman' did not resolve, so wounds will not drain Morale or Blood " +
                "and the fight cannot reach EnemyEscaped or EnemyDefeated.");
        }
    }
}
