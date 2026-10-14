using System;
using System.Linq;
using System.Reflection;
using CSFFModFramework.Util;
using HarmonyLib;

namespace Sirus23ModCollection.Patcher;

/// <summary>
/// Stops the wild (untamed) Owl's nightly "visit the player" duty from teleporting it into a
/// cave, mine, tunnel, or man-made structure - reported 2026-08-22: the Owl appeared inside
/// AdvancedCopperTools' fully-sealed "Metal Mines" (actTinCaveEnv) purely because the player
/// was standing there.
///
/// This is a DIFFERENT engine entry point from <see cref="CompanionStayPatch"/>, which only
/// covers the TAMED companion's AlwaysUpdate follow-along on GameManager.ChangeEnvironment.
/// The wild agent's night-visit behavior is Animals/Owl.json's generated
/// <c>Movement.PlayerAttraction</c> duty (framework <c>DutyBuilder.cs</c>) - a native engine
/// <c>MoveDutyAction</c> with <c>MoveDestination = MoveToPlayer</c>,
/// <c>MovementType = Teleport</c>. Per <c>MoveDutyAction.FindDestination</c> (decompiled), a
/// Teleport MoveToPlayer duty always resolves a "one step path" straight to
/// <c>GameManager.CurrentEnvironment</c> - no map-connectivity or environment-type check at
/// all - so <c>MoveDutyAction.CanBePerformed</c> unconditionally returns true whenever the
/// duty is otherwise eligible (exists==1, not in the dead band). <c>NPCDuty</c> gates whole-duty
/// selection on step 0's <c>CanBePerformed</c>, so forcing it false here makes the engine skip
/// straight to the Owl's next-highest-weight duty (Aggression, or the roost Wait) instead -
/// exactly the same "duty infeasible this tick" fallback the engine already uses when e.g. a
/// MoveToSpecificEnvironment duty's target list is empty.
///
/// <c>CanBePerformed</c> runs for every wandering agent's every eligible move duty, so the postfix
/// tests the two enum fields first (typed, no reflection and no allocation), then the agent's
/// UniqueID, and only then the player's environment. The Info line is written once per environment
/// the player is in when the duty is first refused there; later refusals in the same place are Debug.
/// </summary>
internal static class WildOwlDutyGuardPatch
{
    private const string WildOwlAgentUid = "wildowl_agent";

    // The environment card the last Info line named, so a player sitting in one cave all night
    // gets one line, not one per duty ranking.
    private static CardData _loggedEnvCard;

    public static void ApplyPatch(Harmony harmony)
    {
        var method = typeof(MoveDutyAction).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name == "CanBePerformed" && m.GetParameters().Length == 4);
        if (method == null)
        {
            Plugin.Logger?.LogError("[WildOwlDutyGuard] MoveDutyAction.CanBePerformed not found");
            return;
        }

        harmony.Patch(method, postfix: new HarmonyMethod(typeof(WildOwlDutyGuardPatch), nameof(Postfix)));
    }

    private static void Postfix(MoveDutyAction __instance, InGameNPC _FromNPC, ref bool __result)
    {
        if (!__result || __instance == null) return;
        if (__instance.MoveDestination != MoveDutyAction.MoveDutyOptions.MoveToPlayer) return;
        if (__instance.MovementType != MoveDutyAction.MovementTypes.Teleport) return;
        try
        {
            var model = _FromNPC != null ? _FromNPC.NPCModel : null;
            if (model == null || model.UniqueID != WildOwlAgentUid) return;

            var envCard = PlayerEnvCard();
            if (envCard == null || !IndoorOrCaveEnv.Matches(envCard)) return;

            __result = false;
            if (!ReferenceEquals(envCard, _loggedEnvCard))
            {
                _loggedEnvCard = envCard;
                Plugin.Logger?.LogInfo($"[WildOwlDutyGuard] Wild Owl's seek-player duty suppressed - player is in a cave/indoor environment ({envCard.name}); further refusals here are logged at Debug.");
            }
            else
            {
                Plugin.Logger?.LogDebug("[WildOwlDutyGuard] Wild Owl's seek-player duty suppressed again in the same environment.");
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogError($"[WildOwlDutyGuard] postfix error: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }

    /// <summary>The environment card of GameManager.CurrentEnvironment (the player's current board,
    /// this duty's own teleport target), or null when there is no run.</summary>
    private static CardData PlayerEnvCard()
    {
        if (CardUtil.GetGameManagerInstance() is not GameManager gm || gm == null) return null;
        var envCard = gm.CurrentEnvironment.EnvCard;
        return envCard != null ? envCard : null;
    }
}
