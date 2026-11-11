using System;
using System.Collections;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using HarmonyLib;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Lets any allied companion (vanilla EA 0.66 Partner, or any future NPCAgent flagged
    /// <c>AlliedWithPlayer</c>) follow the player through the Village Inn / Village Academy
    /// front doors, in both directions.
    ///
    /// Vanilla's own follow mechanism for an allied NPC is <c>NPCDuty</c> +
    /// <c>MoveDutyAction(MoveToPlayer)</c>, which reaches its destination via
    /// <c>WorldMapData.GetPathNonAlloc</c> — real A* pathfinding over the WorldMap graph (tracker
    /// <c>T2.237</c> confirmed, 2026-10-05 and again 2026-10-14, that this duty does path across
    /// the base map into CMC's outdoor nodes after the framework's WorldMap pathfinding fix,
    /// 2.23.1; the diagnostic that watched for it, <c>CompanionFollowDiagnostics</c>, is retired).
    /// Building interiors are never WorldMap graph nodes, though — <c>cmcInnInterior</c>/
    /// <c>cmcAcademyInterior</c> sit behind a single instantaneous "Enter"/"Exit"
    /// DismantleAction hop off <c>cmcEnvVillage</c> (same shape as the player's own doors; see
    /// <c>NpcTileWalker</c>'s doc comment) — so no duty, however well-tuned, can ever route a
    /// companion through them. That is a categorical gap, not a tuning problem, independent of
    /// outdoor pathing.
    ///
    /// The fix bypasses pathfinding entirely and mirrors what CMC's own resident schedulers
    /// (Professor/Miller/Weaver/Apothecary/Inn Keeper — see <c>ProfessorSchedulePatch</c>'s
    /// <c>MoveNpcTo</c>) already do for every interior transition: poll the player's current
    /// environment, and on a village&lt;-&gt;interior boundary crossing, directly
    /// <c>GameManager.MoveNPC</c> every allied NPC that was standing in the environment the
    /// player just left. A companion who wasn't with the player (off elsewhere on the map) is
    /// left alone rather than teleported in.
    /// </summary>
    internal static class PartnerIndoorFollowPatch
    {
        private const string VillageEnvUid = "cmcEnvVillage";
        private const string InnInteriorUid = "cmcInnInterior";
        private const string AcademyInteriorUid = "cmcAcademyInterior";

        private static bool _initialized;
        private static string _lastPlayerEnvUid;

        // Reflection handles, resolved once.
        private static bool _typesResolved;
        private static Type _gmType;

        public static void Initialize(Harmony harmony)
        {
            if (_initialized) return;
            _initialized = true;

            if (!ResolveTypes()) return;

            var initStatsMethod = AccessTools.Method(_gmType, "InitializeStatsAndActions");
            if (initStatsMethod == null)
            {
                Plugin.Logger.LogWarning("[PartnerIndoorFollowPatch] GameManager.InitializeStatsAndActions not found — session-boundary reset inactive.");
            }
            else
            {
                harmony.Patch(initStatsMethod, postfix: new HarmonyMethod(typeof(PartnerIndoorFollowPatch), nameof(ResetOnLoad_Postfix)));
            }

            TickEvents.Interval(1f, CheckFollow, "PartnerIndoorFollow");
            Plugin.Logger.LogDebug("[PartnerIndoorFollowPatch] initialized.");
        }

        private static bool ResolveTypes()
        {
            if (_typesResolved) return _gmType != null;
            _typesResolved = true;

            _gmType = CardUtil.FindGameType("GameManager");
            if (_gmType == null)
            {
                Plugin.Logger.LogWarning("[PartnerIndoorFollowPatch] Required game types not found — indoor follow inactive.");
                return false;
            }

            return true;
        }

        private static void ResetOnLoad_Postfix()
        {
            // A fresh session's first CheckFollow tick re-seeds this from the loaded save's
            // actual environment instead of comparing against a previous session's value.
            _lastPlayerEnvUid = null;
        }

        private static void CheckFollow()
        {
            try
            {
                string currentEnv = GameQuery.CurrentEnvironmentUniqueId;
                if (string.IsNullOrEmpty(currentEnv)) return;

                if (_lastPlayerEnvUid == null)
                {
                    _lastPlayerEnvUid = currentEnv;
                    return;
                }

                if (string.Equals(_lastPlayerEnvUid, currentEnv, StringComparison.OrdinalIgnoreCase)) return;

                string fromEnv = _lastPlayerEnvUid;
                _lastPlayerEnvUid = currentEnv;

                bool enteringInn = Is(fromEnv, VillageEnvUid) && Is(currentEnv, InnInteriorUid);
                bool enteringAcademy = Is(fromEnv, VillageEnvUid) && Is(currentEnv, AcademyInteriorUid);
                bool leavingInn = Is(fromEnv, InnInteriorUid) && Is(currentEnv, VillageEnvUid);
                bool leavingAcademy = Is(fromEnv, AcademyInteriorUid) && Is(currentEnv, VillageEnvUid);
                if (!enteringInn && !enteringAcademy && !leavingInn && !leavingAcademy) return;

                string label = enteringInn ? "the Inn" : enteringAcademy ? "the Academy" : "the village";
                Plugin.Logger.LogInfo($"[PartnerIndoorFollowPatch] boundary crossed: '{fromEnv}' -> '{currentEnv}' (heading into {label}).");

                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null)
                {
                    Plugin.Logger.LogWarning("[PartnerIndoorFollowPatch] GameManager instance unavailable — cannot relocate partners for this crossing.");
                    return;
                }

                string targetEnvUid = enteringInn ? InnInteriorUid : enteringAcademy ? AcademyInteriorUid : VillageEnvUid;
                MoveFollowingPartners(gm, fromEnv, targetEnvUid, label);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[PartnerIndoorFollowPatch] CheckFollow failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static bool Is(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static void MoveFollowingPartners(object gm, string fromEnvUid, string targetEnvUid, string label)
        {
            if (Reflect.GetMember(gm, "AlliedNPCs") is not IEnumerable allied)
            {
                Plugin.Logger.LogWarning("[PartnerIndoorFollowPatch] GameManager.AlliedNPCs unavailable — cannot relocate partners for this crossing.");
                return;
            }

            int alliedCount = 0, movedCount = 0;
            foreach (var npc in allied)
            {
                if (npc == null) continue;
                alliedCount++;
                string npcUid = CardUtil.GetCardUniqueId(Reflect.GetMember(npc, "NPCModel")) ?? "<partner>";
                string npcEnv = EnvUidOf(npc);
                if (!Is(npcEnv, fromEnvUid))
                {
                    Plugin.Logger.LogInfo($"[PartnerIndoorFollowPatch] {npcUid} not relocated: currently in '{npcEnv}', expected '{fromEnvUid}'.");
                    continue;
                }

                if (Npc.MoveTo(npc, targetEnvUid))
                {
                    movedCount++;
                    Plugin.Logger.LogInfo($"[PartnerIndoorFollowPatch] {npcUid} followed the player into {label}.");
                }
                else
                {
                    Plugin.Logger.LogWarning($"[PartnerIndoorFollowPatch] MoveTo failed for {npcUid} (see Npc.MoveTo's own warning for the cause).");
                }
            }

            if (alliedCount == 0)
            {
                Plugin.Logger.LogInfo("[PartnerIndoorFollowPatch] no allied NPCs exist right now (nothing to relocate).");
            }
            else if (movedCount == 0)
            {
                Plugin.Logger.LogInfo($"[PartnerIndoorFollowPatch] {alliedCount} allied NPC(s) checked, none were co-located with the player.");
            }
        }

        /// <summary>InGameNPC.CurrentEnvironment is an EnvID struct; its EnvCard is the CT8/CT4
        /// CardData whose UniqueID is comparable with GameQuery.CurrentEnvironmentUniqueId
        /// (.decomp/EnvID.cs:21). Mirrors GuardWitnessPatch.EnvUidOf.</summary>
        private static string EnvUidOf(object npc)
        {
            var envId = Reflect.GetMember(npc, "CurrentEnvironment");
            if (envId == null) return null;
            var envCard = Reflect.GetMember(envId, "EnvCard");
            if (envCard == null) return null;
            return CardUtil.GetCardUniqueId(envCard);
        }
    }
}
