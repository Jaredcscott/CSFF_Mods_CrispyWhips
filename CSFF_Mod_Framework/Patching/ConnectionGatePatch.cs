using System.Reflection;
using CSFFModFramework.Injection;
using CSFFModFramework.Util;
using HarmonyLib;

namespace CSFFModFramework.Patching;

/// <summary>
/// Triggers a <see cref="ConnectionGateService.EvaluateAll"/> pass the moment an environment
/// improvement's completion state can change, so <c>ImprovementBuilt</c>-gated map connections
/// open in the same tick — the player can build a gating improvement (e.g. CMC's River Bridge)
/// and immediately travel through the connection it opens, with no walk-away-and-back and no
/// wait for the 5 s periodic re-check.
///
/// Two vanilla paths mutate improvement completion, and BOTH must be hooked:
/// <list type="bullet">
///   <item><c>InGameCardBase.CompleteImprovement()</c> — the insta-complete path only (the
///     auto-complete queue consumed by <c>SpawnCard</c>, perk pre-builds).</item>
///   <item><c>InGameCardBase.SetBlueprintStage(int)</c> — the HAND-BUILD path: every
///     construction stage advance (<c>BlueprintConstructionPopup</c> →
///     <c>IncreaseBlueprintStage</c>) and every stage decrease funnel through here. A hand-built
///     final stage never calls <c>CompleteImprovement</c>, so patching that alone (the
///     pre-2.23.0 state) never fired for a player-built improvement — root cause of "built the
///     River Bridge but East travel stayed locked until I walked away and came back"
///     (Documentation/Retrospectives/river-bridge.md).</item>
/// </list>
///
/// The stage postfix also idempotently registers a COMPLETED improvement in the env save data's
/// <c>CurrentlyBuiltImprovements</c> via vanilla's own <c>GM.StartBuildingImprovement</c> (which
/// no-ops when already present), so the persisted list is correct at the completion moment and
/// the gate stays open even if the player saves and quits immediately after the final stage.
/// </summary>
internal static class ConnectionGatePatch
{
    private const BindingFlags BF =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // CardTypes.EnvImprovement — framework convention is the reflected-int compare (no
    // compile-time CardTypes reference), matching WorldMapInjector's CT8 filter.
    private const int CardTypeEnvImprovement = 10;

    private static MethodInfo _startBuildingImprovement;

    internal static void ApplyPatch(Harmony harmony)
    {
        var targetType = AccessTools.TypeByName("InGameCardBase");
        if (targetType == null)
        {
            Log.Warn("ConnectionGatePatch: InGameCardBase not found — improvement-gated connections will only update at run start");
            return;
        }

        PatchOne(harmony, targetType, "CompleteImprovement", nameof(CompleteImprovement_Postfix));
        PatchOne(harmony, targetType, "SetBlueprintStage", nameof(SetBlueprintStage_Postfix));
    }

    private static void PatchOne(Harmony harmony, Type targetType, string methodName, string postfixName)
    {
        var method = targetType.GetMethods(BF).FirstOrDefault(m => m.Name == methodName);
        if (method == null)
        {
            Log.Warn($"ConnectionGatePatch: InGameCardBase.{methodName} not found — improvement-gated connections may not update until run start");
            return;
        }

        var postfix = new HarmonyMethod(typeof(ConnectionGatePatch)
            .GetMethod(postfixName, BindingFlags.Static | BindingFlags.NonPublic));
        harmony.Patch(method, postfix: postfix);
        Log.Debug($"ConnectionGatePatch: patched InGameCardBase.{methodName}");
    }

    private static void CompleteImprovement_Postfix()
    {
        try { ConnectionGateService.EvaluateAll(); }
        catch (Exception ex) { Log.Warn($"ConnectionGatePatch: EvaluateAll failed: {ex.GetType().Name}: {ex.Message}"); }
    }

    private static void SetBlueprintStage_Postfix(object __instance)
    {
        try
        {
            var model = CardUtil.GetCardData(__instance);
            if (model == null) return;
            var cardType = CardUtil.GetMemberValue(model, "CardType");
            if (cardType == null || Convert.ToInt32(cardType) != CardTypeEnvImprovement) return;

            RegisterIfComplete(__instance, model);
            ConnectionGateService.EvaluateAll();
        }
        catch (Exception ex)
        {
            Log.Warn($"ConnectionGatePatch: SetBlueprintStage postfix failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Replays vanilla's own idempotent CurrentlyBuiltImprovements registration for an
    // improvement whose blueprint just reached its final stage. Hand-build flows register at
    // construction start via other vanilla paths, but this is the only spot that guarantees
    // the persisted list is correct at the exact completion moment.
    private static void RegisterIfComplete(object card, object model)
    {
        var blueprintData = CardUtil.GetMemberValue(card, "BlueprintData");
        var currentStage = blueprintData == null ? null : CardUtil.GetMemberValue(blueprintData, "CurrentStage");
        var blueprintSteps = CardUtil.GetMemberValue(card, "BlueprintSteps");
        if (currentStage == null || blueprintSteps == null) return;
        if (Convert.ToInt32(currentStage) < Convert.ToInt32(blueprintSteps)) return;

        var gm = CardUtil.GetGameManagerInstance();
        if (gm == null) return;
        _startBuildingImprovement ??= gm.GetType().GetMethod("StartBuildingImprovement", BF);
        var cardEnv = CardUtil.GetMemberValue(card, "CardEnvironment");
        if (_startBuildingImprovement == null || cardEnv == null) return;

        _startBuildingImprovement.Invoke(gm, new[] { model, cardEnv, (object)false });
        Log.Debug($"ConnectionGatePatch: improvement '{CardUtil.GetCardUniqueId(card)}' completed — registered in CurrentlyBuiltImprovements");
    }
}
