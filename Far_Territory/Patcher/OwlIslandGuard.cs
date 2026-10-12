using System;
using System.Collections.Generic;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using HarmonyLib;

namespace FarTerritory.Patcher;

/// <summary>
/// Refuses the Sirus23 Wild Owl's MOVE onto any Far Territory board. The owl's generated night duty
/// (<c>Movement.PlayerAttraction</c>, MovementType Teleport, Sirus23 <c>Animals/Owl.json</c>) teleports it to
/// whichever board the player stands on, with no map check, so it appeared on Cliffbound Cove (owner report,
/// 2026-10-11: "the owl must never be on the island"). Every route onto a board ends in
/// <c>GameManager.MoveNPC(InGameNPC, EnvID)</c>, the same choke point as Community Mod Chest's
/// <c>WildlifeIndoorGuardPatch</c>, so refusing there stops the arrival. It refuses a move and suppresses no
/// encounter. By agent UID and env UID prefix, reflection-only, so Far Territory needs no reference to the
/// Sirus23 mod and the guard is inert when that mod is absent.
///
/// <para>Limits: an owl already standing on an island board (saved there before this fix) is not evicted by a
/// refusal; the owl's own dawn roost relocation moves it to the Spirit World, which is not an island board.</para>
/// </summary>
internal static class OwlIslandGuard
{
    private const string OwlAgentUid = "wildowl_agent";
    private const string IslandEnvPrefix = "ftEnv";

    private static readonly HashSet<string> _warned = new(StringComparer.Ordinal);
    private static bool _initialized;

    public static void Initialize(Harmony harmony)
    {
        if (_initialized) return;
        _initialized = true;

        var gmType = CardUtil.FindGameType("GameManager");
        var inGameNpcType = CardUtil.FindGameType("InGameNPC");
        var envIdType = CardUtil.FindGameType("EnvID");
        var moveNpc = gmType != null && inGameNpcType != null && envIdType != null
            ? AccessTools.Method(gmType, "MoveNPC", new[] { inGameNpcType, envIdType })
            : null;
        if (moveNpc == null)
        {
            Plugin.ModLog.LogWarning("[OwlIslandGuard] GameManager.MoveNPC(InGameNPC, EnvID) not found, so the guard is inactive and the Wild Owl can fly onto the island.");
            return;
        }
        harmony.Patch(moveNpc, prefix: new HarmonyMethod(typeof(OwlIslandGuard), nameof(Prefix)));
    }

    private static bool Prefix(object __0, object __1)
    {
        try
        {
            if (__0 == null) return true;
            var npcModel = Reflect.GetMember(__0, "NPCModel");
            if (npcModel == null)
            {
                WarnOnce("npc-model", "[OwlIslandGuard] InGameNPC.NPCModel could not be read, so no owl move is guarded.");
                return true;
            }
            if (!string.Equals(CardUtil.GetCardUniqueId(npcModel), OwlAgentUid, StringComparison.Ordinal)) return true;

            if (!Reflect.TryGetMember(__1, "EnvCard", out var envCard))
            {
                WarnOnce("dest-envcard", "[OwlIslandGuard] EnvID.EnvCard could not be read, so no owl move is guarded.");
                return true;
            }
            if (envCard == null) return true;

            string destUid = CardUtil.GetCardUniqueId(envCard);
            if (destUid == null)
            {
                WarnOnce("dest-uid", "[OwlIslandGuard] The destination env card's UniqueID could not be read, so no owl move is guarded.");
                return true;
            }
            if (!destUid.StartsWith(IslandEnvPrefix, StringComparison.Ordinal)) return true;

            if (_warned.Add("blocked-" + destUid))
                Plugin.ModLog.LogInfo($"[OwlIslandGuard] Refused the Wild Owl's move onto '{destUid}'.");
            return false;
        }
        catch (Exception ex)
        {
            WarnOnce("exception-" + ex.GetType().Name, $"[OwlIslandGuard] The guard threw, so this move was allowed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            return true;
        }
    }

    private static void WarnOnce(string cause, string message)
    {
        if (_warned.Add(cause)) Plugin.ModLog.LogWarning(message);
    }
}
