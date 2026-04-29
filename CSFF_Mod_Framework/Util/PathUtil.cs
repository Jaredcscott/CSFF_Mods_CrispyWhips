namespace CSFFModFramework.Util;

internal static class PathUtil
{
    private static string _pluginsDir;
    private static string _frameworkDir;

    public static string PluginsDir
    {
        get
        {
            if (_pluginsDir == null)
            {
                _frameworkDir = Path.GetDirectoryName(Plugin.Instance.Info.Location);
                _pluginsDir = Path.GetDirectoryName(_frameworkDir);
            }
            return _pluginsDir;
        }
    }

    public static string FrameworkDir
    {
        get
        {
            if (_frameworkDir == null) _ = PluginsDir; // triggers init
            return _frameworkDir;
        }
    }

    public static IEnumerable<string> GetModDirectories()
    {
        if (!Directory.Exists(PluginsDir)) yield break;
        foreach (var dir in Directory.GetDirectories(PluginsDir))
        {
            if (File.Exists(Path.Combine(dir, "ModInfo.json")))
            {
                yield return dir;
            }
            else
            {
                // One level deeper: plugins/FOO/BAR/ModInfo.json (e.g. PortableRefrigerator-IceBall-1.07/IceCrystalball/)
                foreach (var subDir in Directory.GetDirectories(dir))
                {
                    if (File.Exists(Path.Combine(subDir, "ModInfo.json")))
                        yield return subDir;
                }
            }
        }
    }

    /// <summary>
    /// Extracts a top-level string value from JSON without full parsing.
    /// Returns null if not found.
    /// </summary>
    public static string QuickExtractString(string json, string key)
    {
        if (string.IsNullOrEmpty(json)) return null;
        var needle = $"\"{key}\"";
        int idx = json.IndexOf(needle, StringComparison.Ordinal);
        if (idx < 0) return null;
        idx += needle.Length;
        // skip whitespace and colon
        while (idx < json.Length && (json[idx] == ' ' || json[idx] == ':' || json[idx] == '\t' || json[idx] == '\r' || json[idx] == '\n'))
            idx++;
        if (idx >= json.Length) return null;

        // Handle quoted string values
        if (json[idx] == '"')
        {
            idx++; // skip opening quote
            int start = idx;
            while (idx < json.Length && json[idx] != '"')
            {
                if (json[idx] == '\\') idx++; // skip escaped char
                idx++;
            }
            return json.Substring(start, idx - start);
        }

        // Handle unquoted values (numbers, booleans, null)
        int ustart = idx;
        while (idx < json.Length && json[idx] != ',' && json[idx] != '}' && json[idx] != ']'
               && json[idx] != '\r' && json[idx] != '\n' && json[idx] != ' ')
            idx++;
        return json.Substring(ustart, idx - ustart).Trim();
    }
}
