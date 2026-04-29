using CSFFModFramework.Loading;

namespace CSFFModFramework.Patching;

internal static class LocalizationPatch
{
    public static void ApplyPatch(Harmony harmony)
    {
        var postfix = new HarmonyMethod(AccessTools.Method(typeof(LocalizationPatch), nameof(Postfix)));
        SafePatcher.TryPatch(harmony, "LocalizationManager", "LoadLanguage", postfix: postfix);
    }

    static void Postfix()
    {
        LocalizationLoader.ReloadForLanguage();
    }
}
