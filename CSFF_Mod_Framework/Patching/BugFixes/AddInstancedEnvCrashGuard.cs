namespace CSFFModFramework.Patching.BugFixes;

/// <summary>
/// Root-cause guard for the same vanilla bug <see cref="ChangeEnvironmentCrashGuard"/> covers:
/// WorldMapData.AddInstancedEnv throws an unhandled ArgumentException ("An item with the same
/// key has already been added") when two independently-registered instanced environments both
/// compute the default map Coordinates (0,0,0,0) in the same session (CoordsDict.Add).
///
/// ChangeEnvironmentCrashGuard only covers the GameManager.ChangeEnvironment call path.
/// AddInstancedEnv has several other unguarded call sites in vanilla — confirmed in the wild via
/// GameManager.ProduceCards (fired while crafting/placing any card, e.g. building a kit into a
/// placed structure; 2026-08-14 player report of a permanent lock while placing a Rain Cistern
/// Kit) — and any of them leaves GameManager.RootAction stuck, showing "I can't do two things
/// at once..." on every action forever, same failure shape as the ChangeEnvironment case.
///
/// Patching AddInstancedEnv itself instead of each caller covers every current AND future call
/// site in one place. Harmony finalizers only fire at the outermost patched frame, so before this
/// patch the exception already propagated all the way up to ChangeEnvironmentCrashGuard's
/// finalizer for that one path — this patch produces the identical "colliding env doesn't get
/// registered this time" outcome, just from the correct location for every path.
/// </summary>
internal static class AddInstancedEnvCrashGuard
{
    public static void ApplyPatch(Harmony harmony)
    {
        try
        {
            var original = AccessTools.Method(typeof(WorldMapData), "AddInstancedEnv");
            if (original == null)
            {
                Util.Log.Warn("AddInstancedEnvCrashGuard: WorldMapData.AddInstancedEnv not found — skipping.");
                return;
            }

            var finalizer = new HarmonyMethod(AccessTools.Method(typeof(AddInstancedEnvCrashGuard), nameof(Finalizer)));
            harmony.Patch(original, finalizer: finalizer);
            Util.Log.Debug("AddInstancedEnvCrashGuard: patched WorldMapData.AddInstancedEnv.");
        }
        catch (Exception ex)
        {
            Util.Log.Warn($"AddInstancedEnvCrashGuard: patch setup failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }

    // object[] __args over named EnvID param — safer against parameter-name drift across game
    // updates (see CLAUDE.md § CardInteraction Action Handlers for the same convention).
    static Exception Finalizer(object[] __args, Exception __exception)
    {
        if (__exception == null)
        {
            return null;
        }

        string envName = "<unknown>";
        try
        {
            if (__args != null && __args.Length > 0 && __args[0] is EnvID env)
            {
                envName = env.SimpleEnvName;
            }
        }
        catch (Exception ex)
        {
            Util.Log.Debug($"AddInstancedEnvCrashGuard: could not read _Env.SimpleEnvName: {ex.Message}");
        }

        Util.Log.Error(
            $"AddInstancedEnvCrashGuard: WorldMapData.AddInstancedEnv threw and was suppressed "
            + $"to prevent a permanent action-lock (\"I can't do two things at once\"). "
            + $"Env='{envName}'. Exception: {__exception}");

        return null;
    }
}
