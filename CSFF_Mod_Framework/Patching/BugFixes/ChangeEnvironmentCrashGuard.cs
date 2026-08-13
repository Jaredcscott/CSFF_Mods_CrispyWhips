namespace CSFFModFramework.Patching.BugFixes;

/// <summary>
/// Vanilla WorldMapData.AddInstancedEnv can throw ArgumentException ("An item with the same
/// key has already been added") when two independently-registered instanced environments
/// (e.g. a player-built cabin/mine/mud-hut construction site) both compute default map
/// Coordinates (0,0,0,0) in the same session. GameManager.ChangeEnvironment calls
/// AddInstancedEnv synchronously from its own IEnumerator MoveNext, so an unhandled throw
/// there aborts the ChangeEnvironment coroutine mid-flight — which means it never reaches
/// the code that later clears GameManager.RootAction, so GameManager.PerformingAction stays
/// true forever and every subsequent action shows "I can't do two things at once..."
/// permanently, with no recovery short of quitting the app.
///
/// A Harmony finalizer on ChangeEnvironment's compiler-generated MoveNext swallows this
/// specific class of exception so the coroutine ends gracefully instead of throwing — the
/// offending instanced env just doesn't get pre-registered this time (same graceful-skip
/// outcome vanilla already uses elsewhere in WorldMapData, e.g. LoadInstancedEnvironment's
/// "Root environment doesn't exist" early return), instead of permanently locking the player.
/// </summary>
internal static class ChangeEnvironmentCrashGuard
{
    public static void ApplyPatch(Harmony harmony)
    {
        try
        {
            var original = AccessTools.Method(typeof(GameManager), "ChangeEnvironment");
            if (original == null)
            {
                Util.Log.Warn("ChangeEnvironmentCrashGuard: GameManager.ChangeEnvironment not found — skipping.");
                return;
            }

            var moveNext = AccessTools.EnumeratorMoveNext(original);
            if (moveNext == null)
            {
                Util.Log.Warn("ChangeEnvironmentCrashGuard: could not resolve ChangeEnvironment's MoveNext — skipping.");
                return;
            }

            var finalizer = new HarmonyMethod(AccessTools.Method(typeof(ChangeEnvironmentCrashGuard), nameof(MoveNextFinalizer)));
            harmony.Patch(moveNext, finalizer: finalizer);
            Util.Log.Debug("ChangeEnvironmentCrashGuard: patched GameManager.ChangeEnvironment MoveNext.");
        }
        catch (Exception ex)
        {
            Util.Log.Warn($"ChangeEnvironmentCrashGuard: patch setup failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }

    static Exception MoveNextFinalizer(Exception __exception, ref bool __result)
    {
        if (__exception == null)
        {
            return null;
        }

        string currentEnvName = "<unknown>";
        try
        {
            var instance = MBSingleton<GameManager>.Instance;
            if (instance != null)
            {
                currentEnvName = instance.CurrentEnvironment.SimpleEnvName;
            }
        }
        catch (Exception ex)
        {
            Util.Log.Debug($"ChangeEnvironmentCrashGuard: MoveNextFinalizer could not read CurrentEnvironment.SimpleEnvName: {ex.Message}");
        }

        Util.Log.Error(
            $"ChangeEnvironmentCrashGuard: GameManager.ChangeEnvironment threw and was suppressed "
            + $"to prevent a permanent action-lock (\"I can't do two things at once\"). "
            + $"CurrentEnvironment='{currentEnvName}'. Exception: {__exception}");

        __result = false;
        return null;
    }
}
