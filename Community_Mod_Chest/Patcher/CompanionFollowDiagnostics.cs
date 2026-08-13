using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// TEMPORARY diagnostic — remove once the root cause below is confirmed against a live
    /// log and fixed. Reported 2026-08-10: a recruited vanilla human Partner (EA 0.66's own
    /// companion system, engine: generic <c>NPCDuty</c> + <c>MoveDutyAction(MoveToPlayer)</c>,
    /// same chassis <see cref="AshPartnerDutyPatch"/> and <see cref="GuardDutyPatch"/> already
    /// use) never crosses from the base map into ANY CMC WorldMap node — not "gets stuck at
    /// building doors," never leaves the vanilla side at all — even though player DA-click
    /// travel into CMC works fine.
    ///
    /// Player travel and NPC follow use DIFFERENT mechanisms: a travel DA directly triggers
    /// <c>ChangeEnvironment</c> at the destination named in the button, no graph search
    /// involved. <c>MoveDutyAction</c>'s MoveToPlayer step instead calls
    /// <c>WorldMapData.GetPathNonAlloc</c> — real A* pathfinding over
    /// <c>WorldMapData.Environments[].ConnectedEnvironments</c> — so a companion can fail to
    /// follow for reasons a working travel button says nothing about: the follow duty never
    /// gets selected (negative weight / failed DutyConditions / not-yet-active), or duty
    /// selection succeeds but <c>FindDestination</c>'s underlying WorldMap graph search comes
    /// back invalid. These look identical in-game ("companion stayed behind") and CLAUDE.md's
    /// debugging discipline forbids guessing between them without a log — this reuses
    /// <c>GuardDutyPatch.GuardDutyDiagnostics</c>'s exact reflection idiom (that class's own
    /// <c>ProbeMove</c> helper) against whichever companion(s) are actually present: any
    /// <c>NPCAgent.AlliedWithPlayer</c> NPC (the vanilla Partner) plus Ash
    /// (<c>cmcAshPartnerAgent</c>, CMC's own dog, built on the identical MoveToPlayer chassis —
    /// if Ash ALSO can't cross the boundary, that points at a WorldMap-graph-level cause
    /// affecting every Duty-driven follower, not something specific to the vanilla Partner
    /// preset's JSON).
    /// </summary>
    internal static class CompanionFollowDiagnostics
    {
        private const string AshAgentUid = "cmcAshPartnerAgent";

        private static bool _initialized;
        private static bool _typesResolved;
        private static Type _uidType;
        private static Type _moveDutyActionType;
        private static MethodInfo _getFromIdMethod;
        private static MethodInfo _findDestinationMethod;
        private static object _ashAgent; // resolved lazily; may stay null if Ash isn't shipped/found

        private static readonly Dictionary<string, string> _lastLoggedState =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            TickEvents.Interval(15f, Run, "CompanionFollowDiagnostics");
        }

        private static void ResolveTypes()
        {
            if (_typesResolved) return;
            _typesResolved = true;
            _uidType = Reflect.TryGetType("UniqueIDScriptable");
            _moveDutyActionType = Reflect.TryGetType("MoveDutyAction");
            _findDestinationMethod = _moveDutyActionType?.GetMethod("FindDestination", BindingFlags.Instance | BindingFlags.Public);
            if (_uidType != null)
            {
                _getFromIdMethod = _uidType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "GetFromID" && !m.IsGenericMethodDefinition
                        && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
            }
        }

        private static void Run()
        {
            try
            {
                ResolveTypes();
                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;
                if (Reflect.GetMember(gm, "AllNPCs") is not IEnumerable allNpcs) return;

                if (_ashAgent == null && _getFromIdMethod != null)
                    _ashAgent = _getFromIdMethod.Invoke(null, new object[] { AshAgentUid });

                foreach (var npc in allNpcs)
                {
                    if (npc == null) continue;
                    var npcModel = Reflect.GetMember(npc, "NPCModel");
                    if (npcModel == null) continue;

                    bool allied = Reflect.GetBool(npcModel, "AlliedWithPlayer");
                    bool isAsh = _ashAgent != null && ReferenceEquals(npcModel, _ashAgent);
                    if (!allied && !isAsh) continue;

                    string label = isAsh ? "Ash" : (Reflect.GetMember(npcModel, "name") as string ?? "<partner>");

                    var npcEnv = Reflect.GetMember(npc, "CurrentEnvironment");
                    if (npcEnv == null) continue;
                    bool sameEnv = Reflect.GetBool(npcEnv, "MatchesPlayerEnv");
                    string npcEnvName = Reflect.GetMember(npcEnv, "SimpleEnvName") as string ?? "?";
                    string playerEnvUid = GameQuery.CurrentEnvironmentUniqueId ?? "?";
                    string state = sameEnv ? "same" : $"diff:{npcEnvName}->{playerEnvUid}";

                    if (_lastLoggedState.TryGetValue(label, out var last) && last == state) continue;
                    _lastLoggedState[label] = state;

                    if (sameEnv)
                    {
                        Plugin.Logger.LogDebug($"[CompanionFollowDiagnostics] {label} is now in the player's env (caught up).");
                        continue;
                    }

                    Plugin.Logger.LogDebug(Describe(npc, label, npcEnvName, playerEnvUid));
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[CompanionFollowDiagnostics] failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static string Describe(object npc, string label, string npcEnvName, string playerEnvUid)
        {
            var sb = new StringBuilder();
            sb.Append("[CompanionFollowDiagnostics] ").Append(label)
              .Append(" is NOT in the player's env. npcEnv=").Append(npcEnvName)
              .Append(" playerEnv=").Append(playerEnvUid)
              .Append(" inCombat=").Append(Reflect.GetBool(npc, "IsInCombat"));

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

                sb.Append(" totalWeight=").Append(Reflect.GetInt(weightInfo, "TotalWeight"))
                  .Append(" notSelectable=").Append(Reflect.GetBool(weightInfo, "NotSelectable"));

                var notSelectableMethod = weightInfo.GetType().GetMethod("GetNotSelectableReport", BindingFlags.Instance | BindingFlags.Public);
                if (notSelectableMethod != null &&
                    notSelectableMethod.Invoke(weightInfo, new object[] { active, Reflect.GetBool(npc, "IsInCombat") }) is string reason &&
                    !string.IsNullOrWhiteSpace(reason))
                {
                    sb.AppendLine().Append("      ").Append(reason.Replace("\r", "").Replace("\n", " ").Trim());
                }

                // Probe this step's own FindDestination regardless of selectability — tells us
                // definitively whether the WorldMap graph itself has a path, independent of
                // whatever the duty-selection weight/condition report above says.
                sb.AppendLine().Append("      ").Append(ProbeMove(npc, targetDuty));
            }

            if (count == 0) sb.Append(" | NO DUTIES ATTACHED");
            return sb.ToString();
        }

        private static string ProbeMove(object npc, object targetDuty)
        {
            try
            {
                if (targetDuty == null) return "move-probe: duty is null";
                if (Reflect.GetMember(targetDuty, "ActionSequence") is not Array sequence || sequence.Length == 0)
                    return "move-probe: duty has no ActionSequence";
                var step0 = sequence.GetValue(0);
                if (step0 == null) return "move-probe: step 0 is null";
                if (_moveDutyActionType == null || step0.GetType() != _moveDutyActionType)
                    return $"move-probe: step 0 is a {step0.GetType().Name}, not MoveDutyAction — skipping";
                if (_findDestinationMethod == null) return "move-probe: FindDestination method not found";

                var mapPath = _findDestinationMethod.Invoke(step0, new object[] { npc, targetDuty, null });
                if (mapPath == null) return "move-probe: FindDestination returned null";

                bool isValid = Reflect.GetBool(mapPath, "IsValid");
                int stepCount = Reflect.GetInt(mapPath, "StepCount");
                var end = Reflect.GetMember(mapPath, "End");
                bool endIsNull = end == null || Reflect.GetBool(end, "IsNull");
                string endName = end == null ? "?" : Reflect.GetMember(end, "SimpleEnvName") as string ?? "?";

                return $"move-probe: MoveDestination={Reflect.GetMember(step0, "MoveDestination")} " +
                       $"mapPath.IsValid={isValid} StepCount={stepCount} End={endName} End.IsNull={endIsNull}";
            }
            catch (Exception ex)
            {
                return $"move-probe FAILED: {ex.InnerException?.Message ?? ex.Message}";
            }
        }
    }
}
