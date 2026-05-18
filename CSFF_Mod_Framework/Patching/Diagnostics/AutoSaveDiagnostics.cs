using System.Diagnostics;

namespace CSFFModFramework.Patching.Diagnostics;

/// <summary>
/// Opt-in diagnostic for investigating third-party autosave blockers. Logs the
/// CheckpointTypes value passed to GameLoad.AutoSaveGame and the top stack
/// frames so beta checkpoint behavior can be compared without installing a
/// behavior-changing checkpoint patch. Throttled to first 8 invocations so the
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
        try
        {
            if (_logCount >= MaxLogs) return;
            _logCount++;

            string ckptDesc = DescribeCheckpoint(__args);

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
        catch (System.Exception ex)
        {
            Util.Log.Warn($"AutoSaveDiagnostics: diagnostic logging failed: {Util.Log.ExceptionText(ex)}");
        }
    }

    private static string DescribeCheckpoint(object[] args)
    {
        if (args == null || args.Length == 0) return "<no args>";
        var value = args[0];
        if (value == null) return "<null>";

        string intValue;
        try { intValue = System.Convert.ToInt64(value).ToString(); }
        catch { intValue = "?"; }

        return $"{value.GetType().Name}.{value} (int={intValue})";
    }
}
