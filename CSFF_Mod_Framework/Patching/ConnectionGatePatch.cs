using System.Reflection;
using CSFFModFramework.Injection;
using CSFFModFramework.Util;
using HarmonyLib;

namespace CSFFModFramework.Patching;

/// <summary>
/// Triggers a <see cref="ConnectionGateService.EvaluateAll"/> pass when the player
/// completes building an environment improvement, so that any <c>ImprovementBuilt</c>-gated
/// map connections are shown/hidden in the same tick as the improvement completion.
/// </summary>
internal static class ConnectionGatePatch
{
    internal static void ApplyPatch(Harmony harmony)
    {
        var targetType = AccessTools.TypeByName("InGameCardBase");
        if (targetType == null)
        {
            Log.Warn("ConnectionGatePatch: InGameCardBase not found — improvement-gated connections will only update at run start");
            return;
        }

        var method = targetType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(m => m.Name == "CompleteImprovement");
        if (method == null)
        {
            Log.Warn("ConnectionGatePatch: InGameCardBase.CompleteImprovement not found — improvement-gated connections will only update at run start");
            return;
        }

        var postfix = new HarmonyMethod(typeof(ConnectionGatePatch)
            .GetMethod(nameof(CompleteImprovement_Postfix),
                BindingFlags.Static | BindingFlags.NonPublic));
        harmony.Patch(method, postfix: postfix);
        Log.Debug("ConnectionGatePatch: patched InGameCardBase.CompleteImprovement");
    }

    private static void CompleteImprovement_Postfix()
    {
        try { ConnectionGateService.EvaluateAll(); }
        catch (Exception ex) { Log.Warn($"ConnectionGatePatch: EvaluateAll failed: {ex.GetType().Name}: {ex.Message}"); }
    }
}
