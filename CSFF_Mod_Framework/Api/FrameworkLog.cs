namespace CSFFModFramework.Api;

/// <summary>
/// Optional logging passthrough for plugins that want their messages prefixed
/// with the framework's tag and gated by the framework's <c>VerboseLogging</c>
/// config. Plugins that need their own log prefix should keep using
/// <c>BepInEx.Logging</c> directly.
/// </summary>
public static class FrameworkLog
{
    public static void Info(string message)  => Util.Log.Info(message);
    public static void Warn(string message)  => Util.Log.Warn(message);
    public static void Error(string message) => Util.Log.Error(message);

    /// <summary>
    /// Suppressed unless the framework's <c>General/VerboseLogging</c> config is true.
    /// Use for diagnostic traces that would otherwise spam <c>LogOutput.log</c>.
    /// </summary>
    public static void Debug(string message) => Util.Log.Debug(message);
}
