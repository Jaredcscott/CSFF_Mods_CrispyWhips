using System;
using System.Reflection;
using HarmonyLib;

namespace CSFFModFramework.Patching.BugFixes;

// CheatsPatch 1.1.0 was compiled against CSTI and uses the old class name "UCheatsManager".
// CSFF renamed it to "CheatsManager". Harmony calls AccessTools.TypeByName("UCheatsManager")
// which returns null, silently skipping every CheatsPatch patch method.
//
// Fix: postfix AccessTools.TypeByName so that when it returns null for "UCheatsManager" we
// retry with the current CSFF name "CheatsManager". Scoped to that one name to be safe.
internal static class CheatsPatchCompat
{
    public static void Configure(Harmony harmony)
    {
        try
        {
            var typeByName = typeof(AccessTools).GetMethod(
                "TypeByName",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string) },
                null);

            if (typeByName == null)
            {
                Util.Log.Warn("CheatsPatchCompat: AccessTools.TypeByName(string) not found — skipped.");
                return;
            }

            harmony.Patch(typeByName,
                postfix: new HarmonyMethod(typeof(CheatsPatchCompat), nameof(TypeByName_Postfix)));
            Util.Log.Info("CheatsPatchCompat: patched AccessTools.TypeByName — UCheatsManager → CheatsManager alias active.");
        }
        catch (Exception ex)
        {
            Util.Log.Warn($"CheatsPatchCompat: failed to patch AccessTools.TypeByName: {ex.Message}");
        }
    }

    static void TypeByName_Postfix(string name, ref Type __result)
    {
        if (__result != null || name != "UCheatsManager") return;
        __result = AccessTools.TypeByName("CheatsManager");
        if (__result != null)
            Util.Log.Debug("[CheatsPatchCompat] AccessTools.TypeByName: UCheatsManager → CheatsManager");
    }
}
