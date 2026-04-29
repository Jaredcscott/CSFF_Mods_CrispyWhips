using CSFFModFramework.Discovery;
using CSFFModFramework.Reflection;
using CSFFModFramework.Util;

namespace CSFFModFramework.Loading;

internal static class LocalizationLoader
{
    private static List<ModManifest> _mods;

    public static void LoadAll(List<ModManifest> mods)
    {
        _mods = mods;
        InjectLocalization(mods);
    }

    public static void ReloadForLanguage()
    {
        if (_mods != null)
            InjectLocalization(_mods);
    }

    private static void InjectLocalization(List<ModManifest> mods)
    {
        var currentTexts = GetCurrentTexts();
        if (currentTexts == null)
        {
            Log.Warn("LocalizationLoader: could not access LocalizationManager.CurrentTexts");
            return;
        }

        var langSuffix = GetLanguageSuffix();
        int totalStrings = 0;

        foreach (var mod in mods)
        {
            var locDir = Path.Combine(mod.DirectoryPath, "Localization");
            if (!Directory.Exists(locDir)) continue;

            int modCount = 0;
            foreach (var file in Directory.GetFiles(locDir, "*.csv"))
            {
                var fileName = Path.GetFileNameWithoutExtension(file);

                // Match language by filename
                if (!string.IsNullOrEmpty(langSuffix))
                {
                    if (langSuffix == "En" && fileName.IndexOf("SimpEn", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    if (langSuffix == "Cn" && fileName.IndexOf("SimpCn", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                }
                else
                {
                    // Default to English
                    if (fileName.IndexOf("SimpEn", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                }

                try
                {
                    var lines = File.ReadAllLines(file);
                    // No header row — CSV format: Key,English,Chinese
                    // Determine which column index to use based on language
                    int valueCol = (langSuffix == "Cn") ? 2 : 1;

                    foreach (var line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        // Split CSV columns (respecting quoted fields)
                        var columns = SplitCsvLine(line);
                        if (columns.Count < 2) continue;

                        var key = columns[0].Trim();
                        if (string.IsNullOrEmpty(key)) continue;

                        // Pick the requested language column, fall back to English (col 1)
                        var value = (valueCol < columns.Count && !string.IsNullOrEmpty(columns[valueCol].Trim()))
                            ? columns[valueCol].Trim()
                            : columns[1].Trim();

                        if (!string.IsNullOrEmpty(value))
                        {
                            currentTexts[key] = value;
                            modCount++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn($"LocalizationLoader: failed to load {file}: {ex.Message}");
                }
            }

            if (modCount > 0)
                Log.Debug($"LocalizationLoader: loaded {modCount} strings from {mod.Name}");
            totalStrings += modCount;
        }

        Log.Info($"LocalizationLoader: {totalStrings} total strings loaded");
    }

    /// <summary>
    /// Split a CSV line by commas, respecting double-quoted fields.
    /// </summary>
    private static List<string> SplitCsvLine(string line)
    {
        var result = new List<string>();
        bool inQuotes = false;
        var current = new System.Text.StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++; // skip escaped quote
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        result.Add(current.ToString());
        return result;
    }

    private static Dictionary<string, string> GetCurrentTexts()
    {
        try
        {
            var locMgrType = ReflectionCache.FindType("LocalizationManager");
            if (locMgrType == null) return null;

            var currentTextsField = AccessTools.Field(locMgrType, "CurrentTexts");
            if (currentTextsField != null)
                return currentTextsField.GetValue(null) as Dictionary<string, string>;

            // Try property
            var prop = locMgrType.GetProperty("CurrentTexts",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            return prop?.GetValue(null) as Dictionary<string, string>;
        }
        catch (Exception ex)
        {
            Log.Warn($"LocalizationLoader: reflection error: {ex.Message}");
            return null;
        }
    }

    private static string GetLanguageSuffix()
    {
        try
        {
            var locMgrType = ReflectionCache.FindType("LocalizationManager");
            if (locMgrType == null) return "En";

            // Try to get current language — use raw reflection to avoid HarmonyX warnings
            var bindFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var langField = locMgrType.GetField("CurrentLanguage", bindFlags)
                         ?? locMgrType.GetField("currentLanguage", bindFlags);
            var langProp = langField == null
                ? (locMgrType.GetProperty("CurrentLanguage", bindFlags)
                   ?? locMgrType.GetProperty("currentLanguage", bindFlags))
                : null;

            object lang = null;
            if (langField != null)
                lang = langField.GetValue(null);
            else if (langProp != null)
                lang = langProp.GetValue(null);
            else
            {
                // Fallback: scan for any static field/property of enum type containing "Language"
                foreach (var f in locMgrType.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (f.FieldType.IsEnum && f.Name.IndexOf("lang", StringComparison.OrdinalIgnoreCase) >= 0)
                    { lang = f.GetValue(null); break; }
                }
            }

            if (lang != null)
            {
                var langStr = lang.ToString();
                if (langStr.Contains("Chinese") || langStr.IndexOf("Cn", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Cn";
            }
        }
        catch { }

        return "En";
    }
}
