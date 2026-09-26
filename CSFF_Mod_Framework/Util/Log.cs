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
}
