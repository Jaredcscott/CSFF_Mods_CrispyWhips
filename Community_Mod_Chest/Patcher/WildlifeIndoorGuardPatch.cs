using System;
using System.Collections.Generic;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using HarmonyLib;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Stops vanilla hostile wildlife (Bear, Boar, Forest Beast, Moontouched, Wolf/Primeval
    /// Wolf/Wolf Pack) from physically walking into CMC's village building interiors — reported
    /// 2026-08-28 (Sirus23 screenshot: a Bear standing at the Inn Counter/Academy/Jail).
    ///
    /// Root cause: every one of these agents' "approach the player" AgentAction sets
    /// <c>MoveToPlayerEnvironment: true</c>. Per <c>.decomp/InGameNPC.cs</c>
    /// (<c>MoveNPCFromNPCAction</c> and the inline branch in <c>PerformNPCAction</c>, depending on
    /// the action's MoveTiming), that field unconditionally calls
    /// <c>GameManager.MoveNPC(this, GM.CurrentEnvironment)</c> — no map-connectivity or
    /// environment-type check at all, same engine gap as
    /// <see cref="WildOwlDutyGuardPatch"/>/<see cref="IndoorOrCaveEnv"/> in
    /// Sirus23_Mod_Collection, just reached through the vanilla NPCAction system instead of the
    /// Animals-manifest DutyBuilder. Vanilla never anticipated a buildable interior being a real
    /// environment the player can stand in, so this never mattered before CMC's village.
    ///
    /// <c>Community_Mod_Chest/EncounterGuards/CMC_InteriorsNoWildlife.json</c> already suppresses
    /// the COMBAT ENCOUNTER these agents would otherwise trigger once inside — that guard is
    /// unaffected and stays in place. This patch is the missing other half: it stops the physical
    /// board card from ever arriving in the first place. Keep this UID list in sync with that
    /// guard's <c>GuardEnvironmentUids</c>.
    ///
    /// Reflection-only, same convention as <c>PartnerIndoorFollowPatch</c> — CMC's Assembly-CSharp
    /// reference is the nstrip build (via HerbsAndFungi), which renames fields, so all game-side
    /// member access here goes through <see cref="Reflect"/>/<see cref="CardUtil"/> rather than
    /// direct typed calls.
    /// </summary>
    internal static class WildlifeIndoorGuardPatch
    {
        private static readonly HashSet<string> HostileWildlifeUids = new(StringComparer.Ordinal)
        {
            "61dd2882b59388346b42628f5e76432b", // Bear
            "80cf123c1b0d049459ab4e3fb1b316ff", // Boar1
            "5957ab4b2d66bca44b6502b40eb30a9c", // Boar2
            "eb65ebb44e0cf304fa58b9af3ca9f311", // Forest Beast
            "dc995406694a50b4e8e7bfabd196b63e", // Moontouched
            "0cb137c5a8daaed47ba291779d8146f8", // Wolf
            "d1602c74669f2e642983a004355a3f81", // Primeval Wolf
            "c3ac5867e8021ce43a5094a2bb469935", // Wolf Pack
        };

        // Mirrors EncounterGuards/CMC_InteriorsNoWildlife.json's GuardEnvironmentUids.
        private static readonly HashSet<string> ProtectedInteriorUids = new(StringComparer.OrdinalIgnoreCase)
        {
            "cmcInnInterior",
            "cmcAcademyInterior",
            "cmcApothecaryCabinInterior",
            "cmcMillerCottageInterior",
            "cmcWeaverCottageInterior",
            "cmcVillageHallInterior",
            "cmcEnvJailCell",
        };

        private static bool _initialized;

        public static void Initialize(Harmony harmony)
        {
            if (_initialized) return;
            _initialized = true;

            var gmType = CardUtil.FindGameType("GameManager");
            var inGameNpcType = CardUtil.FindGameType("InGameNPC");
            var envIdType = CardUtil.FindGameType("EnvID");
            if (gmType == null || inGameNpcType == null || envIdType == null)
            {
                Plugin.Logger.LogWarning("[WildlifeIndoorGuardPatch] Required game types not found — guard inactive.");
                return;
            }

            var moveNpcMethod = AccessTools.Method(gmType, "MoveNPC", new[] { inGameNpcType, envIdType });
            if (moveNpcMethod == null)
            {
                Plugin.Logger.LogWarning("[WildlifeIndoorGuardPatch] GameManager.MoveNPC(InGameNPC, EnvID) not found — guard inactive.");
                return;
            }

            harmony.Patch(moveNpcMethod, prefix: new HarmonyMethod(typeof(WildlifeIndoorGuardPatch), nameof(Prefix)));
            Plugin.Logger.LogDebug("[WildlifeIndoorGuardPatch] initialized.");
        }

        private static bool Prefix(object __0, object __1)
        {
            try
            {
                var npcModel = Reflect.GetMember(__0, "NPCModel");
                string npcUid = npcModel != null ? CardUtil.GetCardUniqueId(npcModel) : null;
                if (npcUid == null || !HostileWildlifeUids.Contains(npcUid)) return true;

                var envCard = Reflect.GetMember(__1, "EnvCard");
                string destUid = envCard != null ? CardUtil.GetCardUniqueId(envCard) : null;
                if (destUid == null || !ProtectedInteriorUids.Contains(destUid)) return true;

                Plugin.Logger.LogInfo($"[WildlifeIndoorGuardPatch] Blocked '{npcUid}' from entering protected interior '{destUid}'.");
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogDebug($"[WildlifeIndoorGuardPatch] Prefix error: {ex.InnerException?.ToString() ?? ex.ToString()}");
                return true;
            }
        }
    }
}
