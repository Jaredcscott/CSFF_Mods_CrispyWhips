// GameManager.Awake postfix that re-asserts the two blueprint research flags.
//
// `BlueprintPurchasing = true` keeps the "+" research button visible so players can unlock
// blueprints. `PurchasingWithTime = true` makes those unlocks consume `BlueprintUnlockTicksCost`
// rather than complete instantly. Both flags default to true in vanilla; this patch is here to
// reinforce them in case any other mod or future game change flips them off.
//
// Must use compile-time `typeof(GameManager)` (via the framework's Assembly-CSharp reference) —
// `SafePatcher` cannot find `GameManager.Awake` reliably at framework Awake time.

using HarmonyLib;

namespace CSFFModFramework.Patching;

internal static class BlueprintFlagFix
{
    public static void ApplyPatch(Harmony harmony)
    {
        var awakeMethod = AccessTools.Method(typeof(GameManager), "Awake");
        if (awakeMethod == null)
        {
            Util.Log.Warn("BlueprintFlagFix: GameManager.Awake not found; blueprint flags will not be reinforced.");
            return;
        }
        var postfix = new HarmonyMethod(typeof(BlueprintFlagFix), nameof(Postfix));
        harmony.Patch(awakeMethod, postfix: postfix);
    }

    private static void Postfix(GameManager __instance)
    {
        if (__instance == null) return;
        try
        {
            __instance.BlueprintPurchasing = true;
            __instance.PurchasingWithTime = true;
        }
        catch (System.Exception ex)
        {
            Util.Log.Warn($"BlueprintFlagFix: failed to set blueprint flags: {ex.Message}");
        }
    }
}
