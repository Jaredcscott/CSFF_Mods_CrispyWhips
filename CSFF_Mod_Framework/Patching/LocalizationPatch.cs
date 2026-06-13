using CSFFModFramework.Loading;

namespace CSFFModFramework.Patching;

internal static class LocalizationPatch
{
    public static void ApplyPatch(Harmony harmony)
    {
        try
        {
            var postfix = new HarmonyMethod(AccessTools.Method(typeof(LocalizationPatch), nameof(Postfix)));
            SafePatcher.TryPatch(harmony, "LocalizationManager", "LoadLanguage", postfix: postfix);
        }
        catch (Exception ex)
        {
            Util.Log.Warn($"LocalizationPatch: patch setup failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }

    static void Postfix()
    {
        LocalizationLoader.ReloadForLanguage();
    }
}
