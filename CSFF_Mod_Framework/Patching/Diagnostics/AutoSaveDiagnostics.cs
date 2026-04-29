using System.Diagnostics;

namespace CSFFModFramework.Patching.Diagnostics;

/// <summary>
/// Opt-in diagnostic for investigating why CSFFStopAutoSave's prefix lets the
/// 4 AM autosave through. Logs the CheckpointTypes value passed to
/// GameLoad.AutoSaveGame and the top stack frames so we can see whether the
/// mod's `_Checkpoint != 0` early-out fires, or whether `frames[2]` no longer
/// reaches ActionRoutine in EA 0.62b. Throttled to first 8 invocations so the
/// log doesn't grow unbounded.
/// </summary>
internal static class AutoSaveDiagnostics
{
    private static int _logCount;
    private const int MaxLogs = 8;

    public static void Configure(BepInEx.Configuration.ConfigFile config, Harmony harmony)
    {
        var enabled = config.Bind("Diagnostics", "LogAutoSaveCalls", false,
            "When true, logs the CheckpointTypes value and call-stack of every "
            + "GameLoad.AutoSaveGame invocation (first 8 calls). Used to diagnose "
            + "third-party autosave-blocker mods. Off by default.");
        if (!enabled.Value) return;

        var prefix = new HarmonyMethod(AccessTools.Method(typeof(AutoSaveDiagnostics), nameof(Prefix)));
        SafePatcher.TryPatch(harmony, "GameLoad", "AutoSaveGame", prefix: prefix);
        Util.Log.Info("AutoSaveDiagnostics: logging GameLoad.AutoSaveGame calls (first 8).");
    }

    static void Prefix(object[] __args)
    {
        if (_logCount >= MaxLogs) return;
        _logCount++;

        string ckptDesc = "<no args>";
        if (__args != null && __args.Length > 0 && __args[0] != null)
        {
            var v = __args[0];
            ckptDesc = $"{v.GetType().Name}.{v} (int={(int)v})";
        }

        var trace = new StackTrace(false);
        var frames = trace.GetFrames();
        var sb = new System.Text.StringBuilder();
        sb.Append($"AutoSaveDiagnostics #{_logCount}: _Checkpoint={ckptDesc} | stack: ");
        if (frames != null)
        {
            int max = System.Math.Min(frames.Length, 8);
            for (int i = 0; i < max; i++)
            {
                var m = frames[i]?.GetMethod();
                var dt = m?.DeclaringType?.Name ?? "?";
                var name = m?.Name ?? "?";
                if (i > 0) sb.Append(" -> ");
                sb.Append($"[{i}]{dt}.{name}");
            }
        }
        Util.Log.Info(sb.ToString());
    }
}
