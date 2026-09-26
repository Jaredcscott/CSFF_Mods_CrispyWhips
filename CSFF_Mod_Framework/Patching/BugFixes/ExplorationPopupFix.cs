namespace CSFFModFramework.Patching.BugFixes;

/// <summary>
/// Prevents third-party mod postfix crashes on ExplorationPopup.Setup from
/// breaking the exploration / travel UI. WikiMod 2.6.5.1's SetupPostfix calls
/// ExplorationMapRenderer.UpdateDirectionButtonLabels, which throws NullReferenceException
/// when the exploration card is a framework clone CT8 (e.g. cmc_loc_village_path) because
/// WikiMod's internal node data store has no entry for mod-injected WorldMap connections.
/// The unhandled exception aborts Setup and leaves direction travel buttons non-functional.
/// A Harmony finalizer catches these exceptions so the popup still opens and travel works.
/// </summary>
internal static class ExplorationPopupFix
{
    // One line per distinct cause (exception type + throwing method). Before 2.26.8 a single bool
    // let WikiMod's known exception silence every later, different one, and only the message was logged.
    private static readonly HashSet<string> _loggedCauses = new();

    public static void ApplyPatch(Harmony harmony)
    {
        try
        {
            var finalizer = new HarmonyMethod(AccessTools.Method(typeof(ExplorationPopupFix), nameof(SetupFinalizer)));
            SafePatcher.TryPatch(harmony, "ExplorationPopup", "Setup", finalizer: finalizer);
        }
        catch (Exception ex)
        {
            Util.Log.Warn($"ExplorationPopupFix: patch setup failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }

    static Exception SetupFinalizer(Exception __exception)
    {
        if (__exception != null && _loggedCauses.Add(Util.Log.CauseKey(__exception)))
            Util.Log.Warn("ExplorationPopupFix: swallowed an exception in ExplorationPopup.Setup so the popup still opens "
                        + $"(WikiMod's minimap postfix is the known source; the trace names the actual one): {Util.Log.ExceptionText(__exception)}");
        return null;
    }
}
