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
    private static bool _logged;

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
        if (__exception != null && !_logged)
        {
            _logged = true;
            Util.Log.Warn($"ExplorationPopupFix: swallowed exception in ExplorationPopup.Setup (WikiMod minimap): {__exception.Message}");
        }
        return null;
    }
}
