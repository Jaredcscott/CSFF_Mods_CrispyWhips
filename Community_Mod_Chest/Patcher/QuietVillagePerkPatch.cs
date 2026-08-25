using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// "Quiet Village" perk (cmcperkquietvillage) — a performance/accessibility trait that trades
    /// away the village's roaming NPC content for reduced simulation overhead. Two independent
    /// effects, both gated on <see cref="IsActive"/>:
    ///
    /// <list type="number">
    /// <item>The four Town Watch guards never take up their posts at all — <see cref="GuardSpawnPatch"/>'s
    /// own arrival-check and reload-restore entry points early-return when this perk is equipped, so
    /// no guard NPCDuty (patrol/chase/warden/summon) is ever selected or ticked for the whole run.
    /// Cheaper and simpler than spawning them and despawning immediately: a guard that never gets an
    /// InGameNPC costs nothing, no coroutine-based <c>GameManager.RemoveNPC</c> call needed.</item>
    /// <item>Miller, Weaver, the Apothecary, and the Professor stay at their home/Academy/Inn instead
    /// of commuting or foraging — each resident scheduler (<see cref="CottageResidentSchedulePatch"/>,
    /// <see cref="ApothecarySchedulePatch"/>, <see cref="ProfessorSchedulePatch"/>) short-circuits its
    /// own hour-clock destination logic to "home" when this perk is active, reusing each file's own
    /// proven <c>RunCommute</c>/<c>WalkTowards</c> call sites verbatim rather than inventing a new
    /// movement path. The Inn Keeper needs no change — he never leaves the Inn under any schedule.</item>
    /// </list>
    ///
    /// <para><b>The one seam that needed more than a scheduler-side check</b>: Miller/Weaver's daytime
    /// "at work" leg is NOT driven by <see cref="CottageResidentSchedulePatch"/> at all — it's a
    /// separate engine <c>NPCDuty</c> built by <see cref="CottageResidentWorkDutyPatch"/>
    /// (<c>cmcMillerWork_Duty</c>/<c>cmcWeaverWork_Duty</c>), the ONLY caller of <c>MoveNPC</c> for
    /// these two residents during work hours by design (see that file's own class doc). Forcing
    /// <c>Dest.Cottage</c> in the hand-rolled scheduler without also disabling that duty would just
    /// make the two systems fight over the same NPC every tick — worse for overhead than doing
    /// nothing, and it wouldn't reliably keep them home either. <see cref="SyncResidentWorkDuties"/>
    /// closes this by writing each duty's own <c>NPCDutyRef.PreferenceWeights.BaseWeight</c> directly
    /// (the same live <c>NPCDutyWeights</c> object instance the engine reads every duty-selection
    /// tick — no rebuild needed) to a negative value, satisfying the engine's own "TotalWeight &lt;= 0
    /// ⇒ not selectable" rule (see reference_npcduty_authoring_footguns) with no JSON/engine change.
    /// Restored to the duty's real weight when the perk is not active, because <c>NPCAgent</c>/
    /// <c>NPCDuty</c> ScriptableObjects are shared game data, not per-save — a mutation made for one
    /// character must not leak into a different character loaded later in the same process.</para>
    /// </summary>
    internal static class QuietVillagePerkPatch
    {
        internal const string PerkUid = "cmcperkquietvillage";

        // Must stay in sync with CottageResidentWorkDutyPatch's own agent roster — that file has no
        // shared constant to reuse (its Beats array is a private nested type), and every other file
        // in this mod that needs these two UIDs (e.g. VillageFounderPerkPatch.TrustBoosts) already
        // hardcodes them the same way, so this matches established convention rather than diverging.
        private static readonly string[] ResidentWorkAgentUids = { "cmcMillerAgent", "cmcWeaverAgent" };

        private const int SuppressedWorkWeight = -1;

        private static bool _initialized;
        private static MethodInfo _getFromIdMethod;

        // null = not yet synced this process. Tracks which state (perk on/off) is CURRENTLY applied
        // to the live NPCDutyWeights objects, so a save/character switch that flips CardUtil.IsPerkEquipped
        // gets re-synced on the next poll instead of leaving a prior character's suppression stuck.
        private static bool? _appliedSuppressed;

        public static bool IsActive() => CardUtil.IsPerkEquipped(PerkUid);

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            TickEvents.Interval(3f, SyncResidentWorkDuties, "QuietVillageWorkDutySync");
            Plugin.Logger.LogDebug("[QuietVillagePerkPatch] initialized.");
        }

        private static bool ResolveGetFromId()
        {
            if (_getFromIdMethod != null) return true;
            var uidType = CardUtil.FindGameType("UniqueIDScriptable");
            if (uidType == null) return false;
            _getFromIdMethod = uidType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "GetFromID" && !m.IsGenericMethodDefinition
                    && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
            return _getFromIdMethod != null;
        }

        // Writes NormalWorkWeight or SuppressedWorkWeight onto every duty ref in each resident's
        // AgentDuties (currently exactly one — their work duty — per CottageResidentWorkDutyPatch's
        // own "only duty on either agent" comment; iterating all of them rather than assuming index 0
        // costs nothing and doesn't break if that ever changes). Only writes when the target state
        // actually differs from what's currently applied, so a stable state costs one perk check and
        // nothing else.
        private static void SyncResidentWorkDuties()
        {
            try
            {
                bool active = IsActive();
                if (_appliedSuppressed == active) return;
                if (!ResolveGetFromId()) return;

                int targetWeight = active ? SuppressedWorkWeight : CottageResidentWorkDutyPatch.WorkBaseWeight;
                bool allResolved = true;

                foreach (var agentUid in ResidentWorkAgentUids)
                {
                    var agent = _getFromIdMethod.Invoke(null, new object[] { agentUid });
                    if (agent == null) { allResolved = false; continue; }

                    if (Reflect.GetMember(agent, "AgentDuties") is not Array duties) { allResolved = false; continue; }
                    if (duties.Length == 0) { allResolved = false; continue; } // CottageResidentWorkDutyPatch hasn't built it yet

                    foreach (var dutyRef in duties)
                    {
                        if (dutyRef == null) continue;
                        var weights = Reflect.GetMember(dutyRef, "PreferenceWeights");
                        if (weights == null) continue;
                        Reflect.SetMember(weights, "BaseWeight", targetWeight);
                    }
                }

                if (allResolved)
                {
                    _appliedSuppressed = active;
                    Plugin.Logger.LogInfo(active
                        ? "[QuietVillagePerkPatch] Miller/Weaver's work duty suppressed — they will stay home instead of heading to the mill/loom."
                        : "[QuietVillagePerkPatch] Miller/Weaver's work duty restored to its normal weight.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[QuietVillagePerkPatch] SyncResidentWorkDuties failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }
    }
}
