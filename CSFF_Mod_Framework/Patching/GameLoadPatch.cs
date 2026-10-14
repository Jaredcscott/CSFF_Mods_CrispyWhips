using CSFFModFramework.Injection;
using CSFFModFramework.Loading;
using CSFFModFramework.Util;

namespace CSFFModFramework.Patching;

internal static class GameLoadPatch
{
    public static void ApplyPatch(Harmony harmony)
    {
        try
        {
            // Priority.First: mod content must exist before ANY other plugin's LoadMainGameData postfix
            // reads CardData. BepInEx loads plugins alphabetically by GUID (dependencies pulled ahead), so
            // a plugin with no dependency on us and a GUID before "crispywhips" (Chiwei's
            // ch.CSFF.NoQualityDecay) would otherwise patch, and run, first and never see a mod card.
            // Content mods that soft-depend on us are unaffected: they ran after us already.
            var postfix = new HarmonyMethod(AccessTools.Method(typeof(GameLoadPatch), nameof(Postfix))) { priority = Priority.First };
            var patched = SafePatcher.TryPatch(harmony, "GameLoad", "LoadMainGameData", postfix: postfix)
                       || SafePatcher.TryPatch(harmony, "GameLoad", "LoadGameFilesData", postfix: postfix);
            if (!patched)
                Log.Error("CSFFModFramework: failed to hook GameLoad — mod content will NOT load. Check for game version mismatch.");

            // Blueprint tabs don't exist during LoadMainGameData — re-inject when UI opens
            var blueprintPrefix = new HarmonyMethod(AccessTools.Method(typeof(GameLoadPatch), nameof(BlueprintContent_Start_Prefix)));
            SafePatcher.TryPatch(harmony, "NewBlueprintContent", "Start", prefix: blueprintPrefix);

            var blueprintScreenPostfix = new HarmonyMethod(AccessTools.Method(typeof(GameLoadPatch), nameof(BlueprintModelsScreen_Show_Postfix)));
            // Prefix too: mod-declared subtabs (BlueprintTabs.json "NewSubTabs") must be in the tree
            // before Show selects a tab and builds its subtab buttons.
            var blueprintScreenPrefix = new HarmonyMethod(AccessTools.Method(typeof(GameLoadPatch), nameof(BlueprintModelsScreen_Show_Prefix)));
            SafePatcher.TryPatch(harmony, "BlueprintModelsScreen", "Show", prefix: blueprintScreenPrefix, postfix: blueprintScreenPostfix);
        }
        catch (Exception ex)
        {
            Log.Error($"GameLoadPatch: patch setup failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
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

    static void BlueprintModelsScreen_Show_Prefix(object __instance)
    {
        BlueprintInjector.EnsureSubTabsFromUI(__instance);
    }

    static void BlueprintModelsScreen_Show_Postfix(object __instance)
    {
        BlueprintInjector.InjectFromUI(__instance);
    }
}
