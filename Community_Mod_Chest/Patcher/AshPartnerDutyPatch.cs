using System;
using System.Linq;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using HarmonyLib;
using UnityEngine;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Phase 0 spike (Documentation/Design plan "Convert Ash the Cat into a hands-free
    /// Partner companion") — proves two primitives before any of Ash's real content is
    /// built on top of them: (1) a freshly-authored NPCDuty + MoveDutyAction(MoveToPlayer)
    /// actually makes an NPCAgent walk toward the player across environment transitions;
    /// (2) two competing NPCDutyRef entries, gated by NPCDuty.DutyConditions on a hidden
    /// GameStat, correctly hand control to whichever one's condition is true.
    ///
    /// <c>NPCDuty.ActionSequence</c> is NOT JSON-authorable — confirmed by reading the
    /// framework's own <c>CSFFModFramework/Animals/DutyBuilder.cs</c>, which builds these
    /// exact SO graphs via <c>ScriptableObject.CreateInstance</c> + direct field
    /// assignment rather than JSON deserialization (vanilla duty ActionSequences are
    /// binary Unity sub-assets). This class mirrors that same idiom via reflection
    /// (CMC's Assembly-CSharp reference is the nstripped compile-time stub, same as every
    /// other CMC patch — see root CLAUDE.md "Harmony Patching Pitfalls").
    ///
    /// <c>Agent_AshPartner.json</c> ships with an empty <c>AgentDuties: []</c>; this patch
    /// populates it at runtime, lazily, the first time it finds the agent loaded — mirrors
    /// <c>InnKeeperSpawnPatch.ResolveRefs</c>'s lazy-resolve pattern rather than hooking
    /// LoadMainGameData, because nothing reads AgentDuties before this mod itself calls
    /// GameManager.CreateNPC for cmcAshPartnerAgent (it is never in WorldSettings.NPCAgents).
    /// </summary>
    internal static class AshPartnerDutyPatch
    {
        private const string AgentUid = "cmcAshPartnerAgent";
        private const string NeglectedStatUid = "cmcStatAshNeglected";
        // Phase 0 fixed test wander destination — a real, already-shipped CMC env.
        private const string DebugWanderEnvUid = "cmcEnvVillage";

        private static bool _initialized;
        private static bool _dutiesBuilt;

        // ── reflection handles, resolved once: NpcDuties owns NPCDuty/NPCDutyAction/MoveDutyAction/
        // NPCDutyRef/GeneralCondition/NPCDutyWeights/LocalizedString construction now; only the
        // destination CardData array type still needs resolving here. ─────────────────────────
        private static Type _uidType, _statValueTriggerType, _cardDataType;
        private static MethodInfo _getFromIdMethod;

        /// <summary>True once AgentDuties has been attached — AshPartnerSpawnPatch gates its
        /// spawn on this so InGameNPC.Init never snapshots an empty AgentDuties array (the
        /// duty list is read once at Init/CreateDutiesDict time — see class doc).</summary>
        public static bool DutiesReady => _dutiesBuilt;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            TickEvents.Interval(2f, EnsureDutiesBuilt, "AshPartnerDutyBuild");
            Plugin.Logger.LogDebug("[AshPartnerDutyPatch] initialized.");
        }

        private static bool ResolveTypes()
        {
            // Resolved means the LAST handle is in hand. This tested _uidType until 2026-11-12, so a poll after a
            // partial resolve (the first type found, a later one or GetFromID missing) returned true and
            // EnsureDutiesBuilt then threw on the null handle every 2 s.
            if (_getFromIdMethod != null) return true;

            _uidType = CardUtil.FindGameType("UniqueIDScriptable");
            _statValueTriggerType = CardUtil.FindGameType("StatValueTrigger");
            _cardDataType = CardUtil.FindGameType("CardData");

            if (_uidType == null || _statValueTriggerType == null || _cardDataType == null)
            {
                // Name the first one that did not resolve (companions G5): FindGameType is silent on a plain
                // not-found, and AshPartnerSpawnPatch waits on DutiesReady, so without this line a renamed type
                // meant the spike never spawned with nothing to say which type moved.
                string missing = _uidType == null ? "UniqueIDScriptable"
                    : _statValueTriggerType == null ? "StatValueTrigger" : "CardData";
                WarnResolveOnce("type:" + missing, $"game type '{missing}' not found (renamed by a game update?); the Ash Partner duties are not built, so the spike never spawns.");
                return false;
            }

            _getFromIdMethod = _uidType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "GetFromID" && !m.IsGenericMethodDefinition
                    && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
            if (_getFromIdMethod == null)
                WarnResolveOnce("method:GetFromID", "UniqueIDScriptable.GetFromID(string) not found (renamed or re-typed by a game update?); the Ash Partner duties are not built, so the spike never spawns.");
            return _getFromIdMethod != null;
        }

        private static readonly System.Collections.Generic.HashSet<string> _resolveWarned = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

        private static void WarnResolveOnce(string cause, string message)
        {
            if (_resolveWarned.Add(cause)) Plugin.Logger.LogWarning("[AshPartnerDutyPatch] " + message + " Logged once.");
        }

        private static void EnsureDutiesBuilt()
        {
            if (_dutiesBuilt) return;
            try
            {
                if (!ResolveTypes()) return;

                var agent = _getFromIdMethod.Invoke(null, new object[] { AgentUid });
                if (agent == null) return; // Agent_AshPartner.json not loaded yet — retry next poll.

                var neglectedStat = _getFromIdMethod.Invoke(null, new object[] { NeglectedStatUid });
                var wanderEnv = CardUtil.GetCardDataById(DebugWanderEnvUid);
                if (neglectedStat == null || wanderEnv == null)
                {
                    Plugin.Logger.LogWarning("[AshPartnerDutyPatch] cmcStatAshNeglected or cmcEnvVillage not resolvable yet — retrying.");
                    return;
                }

                var followDuty = BuildFollowPlayerDuty(neglectedStat);
                var wanderDuty = BuildWanderDuty(neglectedStat, wanderEnv);
                if (followDuty == null || wanderDuty == null)
                {
                    Plugin.Logger.LogWarning("[AshPartnerDutyPatch] Failed to build one or both spike duties.");
                    return;
                }

                // Wander outranks Follow — whichever duty's own DutyConditions currently
                // matches cmcStatAshNeglected is the only one actually selectable, so the
                // weight only matters as a tie-breaker/fallback.
                var refs = NpcDuties.ToDutyRefArray(
                    NpcDuties.BuildDutyRef(wanderDuty, baseWeight: 20),
                    NpcDuties.BuildDutyRef(followDuty, baseWeight: 10));

                if (Reflect.SetMember(agent, "AgentDuties", refs))
                {
                    _dutiesBuilt = true;
                    Plugin.Logger.LogInfo("[AshPartnerDutyPatch] Ash's Follow/Wander spike duties attached.");
                }
                else
                {
                    Plugin.Logger.LogWarning("[AshPartnerDutyPatch] NPCAgent.AgentDuties field not found — duties not attached.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[AshPartnerDutyPatch] EnsureDutiesBuilt failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // ── duty construction ───────────────────────────────────────────────────

        private static object BuildFollowPlayerDuty(object neglectedStat)
        {
            var duty = NewDuty("cmcAshFollowPlayer_Duty", "Following", neglectedStat, gateRange: new Vector2(-0.5f, 0.5f));
            if (duty == null) return null;

            var move = NpcDuties.NewMoveAction("cmcAshFollowPlayer_Move");
            NpcDuties.SetEnumField(move, "MovementType", "Pathfind");
            NpcDuties.SetEnumField(move, "MoveDestination", "MoveToPlayer");

            NpcDuties.SetActionSequence(duty, move);
            return duty;
        }

        private static object BuildWanderDuty(object neglectedStat, object wanderEnv)
        {
            var duty = NewDuty("cmcAshWander_Duty", "Wandering off", neglectedStat, gateRange: new Vector2(0.5f, 1.5f));
            if (duty == null) return null;

            var move = NpcDuties.NewMoveAction("cmcAshWander_Move");
            NpcDuties.SetEnumField(move, "MovementType", "Pathfind");
            NpcDuties.SetEnumField(move, "MoveDestination", "MoveToSpecificEnvironment");
            NpcDuties.SetEnumField(move, "DestinationSelection", "Closest");

            var envArray = Array.CreateInstance(_cardDataType, 1);
            envArray.SetValue(wanderEnv, 0);
            Reflect.SetMember(move, "MoveToEnvironments", envArray);

            NpcDuties.SetActionSequence(duty, move);
            return duty;
        }

        private static object NewDuty(string soName, string displayText, object gateStat, Vector2 gateRange)
        {
            var duty = NpcDuties.NewDuty(soName, soName, displayText);

            var condition = NpcDuties.EmptyCondition();
            var trigger = Activator.CreateInstance(_statValueTriggerType);
            Reflect.SetMember(trigger, "Stat", gateStat);
            Reflect.SetMember(trigger, "TriggerRange", gateRange);
            var triggerArray = Array.CreateInstance(_statValueTriggerType, 1);
            triggerArray.SetValue(trigger, 0);
            Reflect.SetMember(condition, "RequiredStatValues", triggerArray);

            if (!NpcDuties.SetDutyConditions(duty, condition))
            {
                Plugin.Logger.LogWarning($"[AshPartnerDutyPatch] NPCDuty.DutyConditions not found on {soName} — duty will be ungated.");
            }

            return duty;
        }
    }
}
