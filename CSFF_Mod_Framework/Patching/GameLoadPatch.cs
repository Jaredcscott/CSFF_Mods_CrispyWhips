using CSFFModFramework.Injection;
using CSFFModFramework.Loading;
using CSFFModFramework.Util;

namespace CSFFModFramework.Patching;

internal static class GameLoadPatch
{
    public static void ApplyPatch(Harmony harmony)
    {
        var postfix = new HarmonyMethod(AccessTools.Method(typeof(GameLoadPatch), nameof(Postfix)));
        var patched = SafePatcher.TryPatch(harmony, "GameLoad", "LoadMainGameData", postfix: postfix)
                   || SafePatcher.TryPatch(harmony, "GameLoad", "LoadGameFilesData", postfix: postfix);
        if (!patched)
            Log.Error("CSFFModFramework: failed to hook GameLoad — mod content will NOT load. Check for game version mismatch.");

        // Blueprint tabs don't exist during LoadMainGameData — re-inject when UI opens
        var blueprintPrefix = new HarmonyMethod(AccessTools.Method(typeof(GameLoadPatch), nameof(BlueprintContent_Start_Prefix)));
        SafePatcher.TryPatch(harmony, "NewBlueprintContent", "Start", prefix: blueprintPrefix);

        var blueprintScreenPostfix = new HarmonyMethod(AccessTools.Method(typeof(GameLoadPatch), nameof(BlueprintModelsScreen_Show_Postfix)));
        SafePatcher.TryPatch(harmony, "BlueprintModelsScreen", "Show", postfix: blueprintScreenPostfix);
    }

    static void Postfix()
    {
        LoadOrchestrator.Execute();
        if (LoadOrchestrator.LoadSucceeded)
            Api.FrameworkEvents.RaiseGameDataReady();
    }

    static void BlueprintContent_Start_Prefix()
    {
        BlueprintInjector.InjectFromUI();
    }

    static void BlueprintModelsScreen_Show_Postfix(object __instance)
    {
        BlueprintInjector.InjectFromUI(__instance);
    }
}
