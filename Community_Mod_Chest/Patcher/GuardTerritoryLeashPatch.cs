using System;
using System.Collections.Generic;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using HarmonyLib;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Holds Thorne's and Corrin's chase at the edge of <c>cmcTagVillageTerritory</c>. Reported
    /// 2026-10-13: Old Corrin followed the player one step past the territory's edge and kept offering
    /// a fight there.
    ///
    /// The chase's own gate (<c>RequiredEnvironmentTags</c> on the guard's env, GuardDutyPatch.
    /// BuildChaseDuty) is read only when a duty is SELECTED, and three engine facts let a chase run on
    /// past it (.decomp/InGameNPC.cs <c>SelectDuty</c>, <c>CheckForDuties</c>,
    /// <c>StartOrUpdateCurrentDuty</c>, <c>UpdateTick</c>):
    ///   1. a running duty is never re-checked against its conditions, only its current action's
    ///      <c>CanBePerformed</c>;
    ///   2. when selection finds nothing selectable it returns null and the running duty simply
    ///      carries on (Corrin has no patrol outside 05:00-20:00 and no warden duty outside her
    ///      17:00-23:00 shift or during an absence window);
    ///   3. the move step and the encounter step run in the same tick (<c>UpdateTick</c> loops while
    ///      <c>ReadyForNextAction</c>), so the hop across the edge and the fight there are one tick.
    /// <see cref="GuardChaseUrgencyPatch"/>'s bonus steps call <c>StartOrUpdateCurrentDuty</c> directly
    /// and skip selection altogether. By day the patrol then pulled her home, the chase was selectable
    /// again on the edge node, and she went straight back out: the repeated fight offer.
    ///
    /// Two refusals, both scoped to the territory-gated chase duties
    /// (<see cref="GuardDutyPatch.IsTerritoryGatedChase"/>; Vane, the unrestricted seeker, and Sterling's
    /// summon duties are untouched):
    ///   1. a prefix on <c>GameManager.MoveNPC</c> refuses a hop whose destination is not a territory
    ///      environment while the guard's current duty is such a chase. A refused hop leaves the move
    ///      step incomplete (<c>MoveDutyAction.ActionComplete</c> is false until she stands at the
    ///      target), so she waits on the edge, still hunting, and steps out only if the player comes
    ///      back inside.
    ///   2. a postfix on <c>StartEncounterDutyAction.CanBePerformed</c> refuses the fight step of such a
    ///      chase while the guard stands outside the territory (a save from before this fix, or any
    ///      route onto that node this prefix did not see). The engine then gives the duty up
    ///      (<c>GiveUpInstantly</c>) instead of offering the fight.
    ///
    /// This refuses a move and one duty step; it never cancels an encounter that has started, and the
    /// player can still Attack a guard from her own card (root CLAUDE.md "Encounter System Patching").
    /// Reflection-only, as in <see cref="WildlifeIndoorGuardPatch"/>, and every miss warns once per
    /// cause: a leash that silently lets everything through looks exactly like the old bug.
    /// </summary>
    internal static class GuardTerritoryLeashPatch
    {
        private static bool _initialized;
        private static readonly HashSet<string> _warned = new(StringComparer.Ordinal);

        public static void Initialize(Harmony harmony)
        {
            if (_initialized) return;
            _initialized = true;

            var gmType = CardUtil.FindGameType("GameManager");
            var inGameNpcType = CardUtil.FindGameType("InGameNPC");
            var envIdType = CardUtil.FindGameType("EnvID");
            var encounterStepType = CardUtil.FindGameType("StartEncounterDutyAction");
            if (gmType == null || inGameNpcType == null || envIdType == null || encounterStepType == null)
            {
                Plugin.Logger.LogWarning("[GuardTerritoryLeashPatch] Required game types not found, so Thorne and Corrin can follow you out of the village territory and fight there.");
                return;
            }

            var moveNpc = AccessTools.Method(gmType, "MoveNPC", new[] { inGameNpcType, envIdType });
            if (moveNpc == null)
                Plugin.Logger.LogWarning("[GuardTerritoryLeashPatch] GameManager.MoveNPC(InGameNPC, EnvID) not found, so a chasing guard can step out of the village territory.");
            else
                harmony.Patch(moveNpc, prefix: new HarmonyMethod(typeof(GuardTerritoryLeashPatch), nameof(MoveNpcPrefix)));

            var canBePerformed = AccessTools.Method(encounterStepType, "CanBePerformed");
            if (canBePerformed == null)
                Plugin.Logger.LogWarning("[GuardTerritoryLeashPatch] StartEncounterDutyAction.CanBePerformed not found, so a guard already outside the village territory can still start her chase fight there.");
            else
                harmony.Patch(canBePerformed, postfix: new HarmonyMethod(typeof(GuardTerritoryLeashPatch), nameof(EncounterStepPostfix)));

            Plugin.Logger.LogDebug("[GuardTerritoryLeashPatch] initialized.");
        }

        /// <summary>Refuses a territory-gated chase's hop onto a node outside the village territory.</summary>
        private static bool MoveNpcPrefix(object __0, object __1)
        {
            try
            {
                if (__0 == null) return true;
                string chase = TerritoryGatedChaseOf(__0);
                if (chase == null) return true;

                if (!Reflect.TryGetMember(__1, "EnvCard", out var envCard))
                {
                    WarnOnce("dest-envcard", "[GuardTerritoryLeashPatch] EnvID.EnvCard could not be read on a chase move's destination, so that move was allowed.");
                    return true;
                }
                if (envCard == null) return true; // an empty EnvID: vanilla MoveNPC ignores it too
                string destUid = CardUtil.GetCardUniqueId(envCard);
                if (destUid == null)
                {
                    WarnOnce("dest-uid", "[GuardTerritoryLeashPatch] A chase move's destination UniqueID could not be read, so that move was allowed.");
                    return true;
                }
                if (GuardDutyPatch.IsVillageTerritoryEnv(destUid)) return true;

                Plugin.Logger.LogDebug($"[GuardTerritoryLeashPatch] '{chase}' held at the edge: refused the hop to '{destUid}' (outside the village territory).");
                return false;
            }
            catch (Exception ex)
            {
                WarnOnce("move-" + ex.GetType().Name, $"[GuardTerritoryLeashPatch] The move check threw, so this move was allowed: {ex.InnerException?.ToString() ?? ex.ToString()}");
                return true;
            }
        }

        /// <summary>Refuses a territory-gated chase's fight step while the guard stands outside the territory.</summary>
        private static void EncounterStepPostfix(object __0, object __1, ref bool __result)
        {
            if (!__result) return;
            try
            {
                if (__0 == null || __1 == null) return;
                string chase = Reflect.GetMember(__1, "name") as string;
                if (!GuardDutyPatch.IsTerritoryGatedChase(chase)) return;

                string envUid = CurrentEnvUid(__0);
                if (envUid == null)
                {
                    WarnOnce("npc-env", "[GuardTerritoryLeashPatch] A chasing guard's environment could not be read, so her fight step was allowed wherever she stands.");
                    return;
                }
                if (GuardDutyPatch.IsVillageTerritoryEnv(envUid)) return;

                __result = false;
                Plugin.Logger.LogDebug($"[GuardTerritoryLeashPatch] '{chase}' is outside the village territory at '{envUid}': no chase fight there, the duty is given up.");
            }
            catch (Exception ex)
            {
                WarnOnce("step-" + ex.GetType().Name, $"[GuardTerritoryLeashPatch] The fight-step check threw, so the step was allowed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        /// <summary>The NPC's current duty name when it is a territory-gated chase, else null.</summary>
        private static string TerritoryGatedChaseOf(object npc)
        {
            var current = Reflect.GetMember(npc, "CurrentDuty");
            if (current == null) return null;
            var duty = Reflect.GetMember(current, "Duty");
            if (duty is UnityEngine.Object dutyObj && !dutyObj) return null;
            string name = duty == null ? null : Reflect.GetMember(duty, "name") as string;
            return GuardDutyPatch.IsTerritoryGatedChase(name) ? name : null;
        }

        private static string CurrentEnvUid(object npc)
        {
            if (!Reflect.TryGetMember(npc, "CurrentEnvironment", out var env) || env == null) return null;
            if (!Reflect.TryGetMember(env, "EnvCard", out var envCard) || envCard == null) return null;
            return CardUtil.GetCardUniqueId(envCard);
        }

        private static void WarnOnce(string cause, string message)
        {
            if (_warned.Add(cause)) Plugin.Logger.LogWarning(message);
        }
    }
}
