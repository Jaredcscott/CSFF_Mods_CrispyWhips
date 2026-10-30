namespace CSFFModFramework.Util;

internal static class Log
{
    private const string Prefix = "[CSFFModFramework] ";
    private static ManualLogSource _source;

    /// <summary>
    /// When false (default), Debug() messages are suppressed.
    /// Set to true via BepInEx config or code to enable verbose logging.
    /// </summary>
    public static bool Verbose { get; set; } = false;

    public static void Init(ManualLogSource source) => _source = source;

    public static void Info(string msg) => _source?.Log(LogLevel.Info, Prefix + msg);
    public static void Warn(string msg) => _source?.Log(LogLevel.Warning, Prefix + msg);
    public static void Error(string msg) => _source?.Log(LogLevel.Error, Prefix + msg);

    public static string ExceptionText(Exception ex) => ex?.InnerException?.ToString() ?? ex?.ToString() ?? "<null exception>";

    /// <summary>
    /// A key for "log once per cause" latches: the unwrapped exception's type plus the method that
    /// threw it. One shared bool per call site lets the first cause silence every different later one.
    /// </summary>
    public static string CauseKey(Exception ex)
    {
        var cause = ex?.InnerException ?? ex;
        var site = cause?.TargetSite;
        return $"{cause?.GetType().FullName}@{site?.DeclaringType?.FullName}.{site?.Name}";
    }

    /// <summary>
    /// Diagnostic logging — only emitted when Verbose is true.
    /// Use for per-item traces, timing info, and diagnostic dumps.
    /// </summary>
    public static void Debug(string msg)
    {
        if (Verbose) _source?.Log(LogLevel.Info, Prefix + msg);
    }

    /// <summary>
    /// A line at BepInEx Debug level, written whatever <see cref="Verbose"/> says, as a mod's own
    /// <c>Logger.LogDebug</c> is. For a line that took over from a mod's own Debug line and that a
    /// TestHarness scenario asserts on (KitContentsService, 2.29.0); everything else uses <see cref="Debug"/>.
    /// </summary>
    public static void DebugAlways(string msg) => _source?.Log(LogLevel.Debug, Prefix + msg);

    private static readonly System.Collections.Generic.Dictionary<string, int> _sampleCounts = new();
    private const int SampleCap = 15;

    /// <summary>
    /// Like <see cref="Debug"/>, but for the confirmatory per-item traces of STABLE, finished
    /// features (clone/inject/register loops) where the per-item detail is only useful while
    /// debugging the feature itself. Emits at most <see cref="SampleCap"/> lines per label per
    /// process, then one "further lines suppressed" note; the feature's own aggregate Info line
    /// is expected to carry the full count. Keeps a verbose log readable without a per-loop
    /// counter at each call site. Do NOT use for a feature's primary failure diagnostic
    /// (e.g. WarpResolver): those stay on plain Debug so every line survives, and never for
    /// a line a TestHarness scenario asserts on (Tests/Log-DebugSampledNotScenarioEvidence.Tests.ps1).
    /// </summary>
    public static void DebugSampled(string label, string msg)
    {
        if (!Verbose) return;
        int n = _sampleCounts.TryGetValue(label, out var c) ? c : 0;
        _sampleCounts[label] = n + 1;
        if (n < SampleCap) _source?.Log(LogLevel.Info, Prefix + msg);
        else if (n == SampleCap) _source?.Log(LogLevel.Info, Prefix + $"{label}: ... (further per-item lines suppressed this session; see the aggregate count)");
    }
}
