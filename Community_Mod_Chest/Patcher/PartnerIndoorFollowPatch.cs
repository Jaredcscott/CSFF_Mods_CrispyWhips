using System;
using System.Collections;
using System.Reflection;
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
    /// <c>WorldMapData.GetPathNonAlloc</c> — real A* pathfinding over the WorldMap graph
    /// (<see cref="CompanionFollowDiagnostics"/> is the still-open investigation into whether
    /// that duty can path across the base map into CMC's outdoor nodes at all). Building
    /// interiors are never WorldMap graph nodes, though — <c>cmcInnInterior</c>/
    /// <c>cmcAcademyInterior</c> sit behind a single instantaneous "Enter"/"Exit"
    /// DismantleAction hop off <c>cmcEnvVillage</c> (same shape as the player's own doors; see
    /// <c>NpcTileWalker</c>'s doc comment) — so no duty, however well-tuned, can ever route a
    /// companion through them. That is a categorical gap, not a tuning problem, and it exists
    /// independently of whatever CompanionFollowDiagnostics finds about outdoor pathing.
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
        private static Type _envIdType;
        private static Type _cardDataType;
        private static MethodInfo _moveNpcMethod;          // GameManager.MoveNPC(InGameNPC, EnvID)
        private static ConstructorInfo _envIdFromCardCtor; // EnvID(CardData, bool)

        // Cached refs, resolved once the mod's own CardData/EnvIDs exist.
        private static object _villageCard, _innCard, _academyCard;
        private static object _villageEnvId, _innEnvId, _academyEnvId;

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
            if (_typesResolved) return _moveNpcMethod != null && _envIdFromCardCtor != null;
            _typesResolved = true;

            _gmType = CardUtil.FindGameType("GameManager");
            _envIdType = CardUtil.FindGameType("EnvID");
            _cardDataType = CardUtil.FindGameType("CardData");
            var inGameNpcType = CardUtil.FindGameType("InGameNPC");

            if (_gmType == null || _envIdType == null || _cardDataType == null || inGameNpcType == null)
            {
                Plugin.Logger.LogWarning("[PartnerIndoorFollowPatch] Required game types not found — indoor follow inactive.");
                return false;
            }

            _moveNpcMethod = AccessTools.Method(_gmType, "MoveNPC", new[] { inGameNpcType, _envIdType });
            _envIdFromCardCtor = _envIdType.GetConstructor(new[] { _cardDataType, typeof(bool) });

            if (_moveNpcMethod == null || _envIdFromCardCtor == null)
            {
                Plugin.Logger.LogWarning("[PartnerIndoorFollowPatch] MoveNPC method or EnvID constructor not found — indoor follow inactive.");
                return false;
            }

            return true;
        }

        private static bool ResolveRefs()
        {
            if (_villageEnvId != null && _innEnvId != null && _academyEnvId != null) return true;

            _villageCard ??= CardUtil.GetCardDataById(VillageEnvUid);
            _innCard ??= CardUtil.GetCardDataById(InnInteriorUid);
            _academyCard ??= CardUtil.GetCardDataById(AcademyInteriorUid);
            if (_villageCard == null || _innCard == null || _academyCard == null) return false;

            try
            {
                // Non-instanced outdoor/interior envs both use the (CardData, false) ctor —
                // cmcInnInterior/cmcAcademyInterior are InstancedEnvironment:false (1.44.x+).
                _villageEnvId ??= _envIdFromCardCtor.Invoke(new object[] { _villageCard, false });
                _innEnvId ??= _envIdFromCardCtor.Invoke(new object[] { _innCard, false });
                _academyEnvId ??= _envIdFromCardCtor.Invoke(new object[] { _academyCard, false });
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[PartnerIndoorFollowPatch] Building EnvIDs failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
                return false;
            }

            return _villageEnvId != null && _innEnvId != null && _academyEnvId != null;
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

                if (!ResolveRefs()) return;

                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;

                object targetEnvId = enteringInn ? _innEnvId : enteringAcademy ? _academyEnvId : _villageEnvId;
                string label = enteringInn ? "the Inn" : enteringAcademy ? "the Academy" : "the village";
                MoveFollowingPartners(gm, fromEnv, targetEnvId, label);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[PartnerIndoorFollowPatch] CheckFollow failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static bool Is(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static void MoveFollowingPartners(object gm, string fromEnvUid, object targetEnvId, string label)
        {
            if (Reflect.GetMember(gm, "AlliedNPCs") is not IEnumerable allied) return;

            foreach (var npc in allied)
            {
                if (npc == null) continue;
                if (!Is(EnvUidOf(npc), fromEnvUid)) continue;

                try
                {
                    _moveNpcMethod.Invoke(gm, new object[] { npc, targetEnvId });
                    string npcUid = CardUtil.GetCardUniqueId(Reflect.GetMember(npc, "NPCModel")) ?? "<partner>";
                    Plugin.Logger.LogDebug($"[PartnerIndoorFollowPatch] {npcUid} followed the player into {label}.");
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogWarning($"[PartnerIndoorFollowPatch] MoveNPC failed for a partner: {ex.InnerException?.ToString() ?? ex.ToString()}");
                }
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
