namespace CSFFModFramework.Patching.BugFixes;

/// <summary>
/// Prevents third-party mod postfix crashes on BlueprintModelsScreen.Show
/// from breaking the blueprint/research UI. CardSizeReduce's Show_Postfix
/// throws NullReferenceException which disrupts the blueprint screen.
/// A Harmony finalizer catches these exceptions so the screen still works.
/// </summary>
internal static class BlueprintScreenFix
{
    private static bool _loggedShow;
    private static bool _loggedToggle;

    public static void ApplyPatch(Harmony harmony)
    {
        var finalizer = new HarmonyMethod(AccessTools.Method(typeof(BlueprintScreenFix), nameof(ShowFinalizer)));
        SafePatcher.TryPatch(harmony, "BlueprintModelsScreen", "Show", finalizer: finalizer);

        var toggleFinalizer = new HarmonyMethod(AccessTools.Method(typeof(BlueprintScreenFix), nameof(ToggleFinalizer)));
        SafePatcher.TryPatch(harmony, "BlueprintModelsScreen", "Toggle", finalizer: toggleFinalizer);
    }

    static Exception ShowFinalizer(Exception __exception)
    {
        if (__exception != null && !_loggedShow)
        {
            _loggedShow = true;
            Util.Log.Warn($"BlueprintScreenFix: swallowed exception in BlueprintModelsScreen.Show (CardSizeReduce): {__exception.Message}");
        }
        return null;
    }

    static Exception ToggleFinalizer(Exception __exception)
    {
        if (__exception != null && !_loggedToggle)
        {
            _loggedToggle = true;
            Util.Log.Warn($"BlueprintScreenFix: swallowed exception in BlueprintModelsScreen.Toggle (CardSizeReduce): {__exception.Message}");
        }
        return null;
    }
}
