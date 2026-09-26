namespace CSFFModFramework.Patching.BugFixes;

/// <summary>
/// Prevents third-party mod postfix crashes on BlueprintModelsScreen.Show
/// from breaking the blueprint/research UI. CardSizeReduce's Show_Postfix
/// throws NullReferenceException which disrupts the blueprint screen.
/// A Harmony finalizer catches these exceptions so the screen still works.
/// </summary>
internal static class BlueprintScreenFix
{
    // One line per distinct cause (method + exception type + throwing method). Before 2.26.8 one bool
    // per method let the first exception silence every later, different one, and only the message was logged.
    private static readonly HashSet<string> _loggedCauses = new();

    public static void ApplyPatch(Harmony harmony)
    {
        try
        {
            var finalizer = new HarmonyMethod(AccessTools.Method(typeof(BlueprintScreenFix), nameof(ShowFinalizer)));
            SafePatcher.TryPatch(harmony, "BlueprintModelsScreen", "Show", finalizer: finalizer);

            var toggleFinalizer = new HarmonyMethod(AccessTools.Method(typeof(BlueprintScreenFix), nameof(ToggleFinalizer)));
            SafePatcher.TryPatch(harmony, "BlueprintModelsScreen", "Toggle", finalizer: toggleFinalizer);
        }
        catch (Exception ex)
        {
            Util.Log.Warn($"BlueprintScreenFix: patch setup failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }

    static Exception ShowFinalizer(Exception __exception)
    {
        Report("Show", __exception);
        return null;
    }

    static Exception ToggleFinalizer(Exception __exception)
    {
        Report("Toggle", __exception);
        return null;
    }

    private static void Report(string method, Exception ex)
    {
        if (ex == null || !_loggedCauses.Add(method + "|" + Util.Log.CauseKey(ex))) return;
        Util.Log.Warn($"BlueprintScreenFix: swallowed an exception in BlueprintModelsScreen.{method} so the screen still works "
                    + $"(CardSizeReduce's postfix is the known source; the trace names the actual one): {Util.Log.ExceptionText(ex)}");
    }
}
