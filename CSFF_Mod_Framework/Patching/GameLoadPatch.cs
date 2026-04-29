using CSFFModFramework.Injection;
using CSFFModFramework.Loading;

namespace CSFFModFramework.Patching;

internal static class GameLoadPatch
{
    public static void ApplyPatch(Harmony harmony)
    {
        var postfix = new HarmonyMethod(AccessTools.Method(typeof(GameLoadPatch), nameof(Postfix)));
        if (!SafePatcher.TryPatch(harmony, "GameLoad", "LoadMainGameData", postfix: postfix))
            SafePatcher.TryPatch(harmony, "GameLoad", "LoadGameFilesData", postfix: postfix);

        // Blueprint tabs don't exist during LoadMainGameData — re-inject when UI opens
        var blueprintPrefix = new HarmonyMethod(AccessTools.Method(typeof(GameLoadPatch), nameof(BlueprintContent_Start_Prefix)));
        SafePatcher.TryPatch(harmony, "NewBlueprintContent", "Start", prefix: blueprintPrefix);
    }

    static void Postfix()
    {
        LoadOrchestrator.Execute();
        Api.FrameworkEvents.RaiseGameDataReady();
    }

    static void BlueprintContent_Start_Prefix()
    {
        BlueprintInjector.InjectFromUI();
    }
}
