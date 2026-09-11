using System;
using System.Linq;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using HarmonyLib;

namespace Sirus23ModCollection.Patcher;

/// <summary>
/// Stops the wild (untamed) Owl's nightly "visit the player" duty from teleporting it into a
/// cave, mine, tunnel, or man-made structure — reported 2026-08-22: the Owl appeared inside
/// AdvancedCopperTools' fully-sealed "Metal Mines" (actTinCaveEnv) purely because the player
/// was standing there.
///
/// This is a DIFFERENT engine entry point from <see cref="CompanionStayPatch"/>, which only
/// covers the TAMED companion's AlwaysUpdate follow-along on GameManager.ChangeEnvironment.
/// The wild agent's night-visit behavior is Animals/Owl.json's generated
/// <c>Movement.PlayerAttraction</c> duty (framework <c>DutyBuilder.cs</c>) — a native engine
/// <c>MoveDutyAction</c> with <c>MoveDestination = MoveToPlayer</c>,
/// <c>MovementType = Teleport</c>. Per <c>MoveDutyAction.FindDestination</c> (decompiled), a
/// Teleport MoveToPlayer duty always resolves a "one step path" straight to
/// <c>GameManager.CurrentEnvironment</c> — no map-connectivity or environment-type check at
/// all — so <c>MoveDutyAction.CanBePerformed</c> unconditionally returns true whenever the
/// duty is otherwise eligible (exists==1, not in the dead band). <c>NPCDuty</c> gates whole-duty
/// selection on step 0's <c>CanBePerformed</c>, so forcing it false here makes the engine skip
/// straight to the Owl's next-highest-weight duty (Aggression, or the roost Wait) instead —
/// exactly the same "duty infeasible this tick" fallback the engine already uses when e.g. a
/// MoveToSpecificEnvironment duty's target list is empty.
///
/// Reflection-only (this mod's Assembly-CSharp reference is the nstrip build, which renames
/// fields — CLAUDE.md §Harmony Patching Pitfalls: nstrip renames cause MissingFieldException on
/// direct typed access).
/// </summary>
internal static class WildOwlDutyGuardPatch
{
    private const string WildOwlAgentUid = "wildowl_agent";

    private static FieldInfo _currentEnvironmentField;
    private static PropertyInfo _envCardProperty;

    public static void ApplyPatch(Harmony harmony)
    {
        var moveDutyActionType = AccessTools.TypeByName("MoveDutyAction");
        if (moveDutyActionType == null)
        {
            Plugin.Logger?.LogError("[WildOwlDutyGuard] MoveDutyAction type not found");
            return;
        }

        var method = moveDutyActionType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name == "CanBePerformed" && m.GetParameters().Length == 4);
        if (method == null)
        {
            Plugin.Logger?.LogError("[WildOwlDutyGuard] MoveDutyAction.CanBePerformed not found");
            return;
        }

        harmony.Patch(method, postfix: new HarmonyMethod(typeof(WildOwlDutyGuardPatch), nameof(Postfix)));
    }

    private static void Postfix(object __instance, object _FromNPC, ref bool __result)
    {
        if (!__result) return;
        try
        {
            if (Reflect.GetMember(__instance, "MoveDestination")?.ToString() != "MoveToPlayer") return;
            if (Reflect.GetMember(__instance, "MovementType")?.ToString() != "Teleport") return;

            var model = Reflect.GetMember(_FromNPC, "NPCModel");
            if (Reflect.GetMember(model, "UniqueID") as string != WildOwlAgentUid) return;

            if (!DestinationIsIndoorOrCave()) return;

            Plugin.Logger?.LogInfo("[WildOwlDutyGuard] Wild Owl's seek-player duty suppressed — player is in a cave/indoor environment.");
            __result = false;
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogError($"[WildOwlDutyGuard] postfix error: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }

    /// <summary>
    /// True when GameManager.CurrentEnvironment (the player's current board — this duty's own
    /// teleport target) is a known cave/mine/tunnel or man-made structure. Field/property
    /// lookups are cached per CLAUDE.md performance rules.
    /// </summary>
    private static bool DestinationIsIndoorOrCave()
    {
        var gm = CardUtil.GetGameManagerInstance();
        if (gm == null) return false;

        _currentEnvironmentField ??= gm.GetType().GetField("CurrentEnvironment",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        object envId = _currentEnvironmentField?.GetValue(gm);
        if (envId == null) return false;

        _envCardProperty ??= envId.GetType().GetProperty("EnvCard",
            BindingFlags.Instance | BindingFlags.Public);
        if (_envCardProperty?.GetValue(envId) is not UnityEngine.Object envCard || envCard == null) return false;

        return IndoorOrCaveEnv.Matches(envCard);
    }
}
