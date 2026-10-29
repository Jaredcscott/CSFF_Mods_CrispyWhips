using CSFFModFramework.Discovery;
using CSFFModFramework.Reflection;
using CSFFModFramework.Util;

namespace CSFFModFramework.Loading;

internal static class LocalizationLoader
{
    internal static bool ForceChineseMode = false;
    private static List<ModManifest> _mods;

    internal static bool HasFrameworkLocalization
        => Directory.Exists(Path.Combine(PathUtil.FrameworkDir, "Localization"));

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
        Log.Info($"LocalizationLoader: language='{langSuffix}' (Cn=Chinese, En=English)");
        int totalStrings = 0;

        // Load framework-owned localization first. ModDiscovery skips the framework's own
        // directory, so it never appears in `mods` — load it separately here.
        var fwLocDir = Path.Combine(PathUtil.FrameworkDir, "Localization");
        if (Directory.Exists(fwLocDir))
        {
            int fwCount = LoadLocalizationDir(fwLocDir, langSuffix, currentTexts);
            if (fwCount > 0)
                Log.Debug($"LocalizationLoader: loaded {fwCount} strings from framework");
            totalStrings += fwCount;
        }

        foreach (var mod in mods)
        {
            var locDir = Path.Combine(mod.DirectoryPath, "Localization");
            if (!Directory.Exists(locDir)) continue;

            int modCount = LoadLocalizationDir(locDir, langSuffix, currentTexts);
            if (modCount > 0)
                Log.Debug($"LocalizationLoader: loaded {modCount} strings from {mod.Name} (lang={langSuffix})");
            totalStrings += modCount;
        }

        foreach (var pair in _derivedRows)
            WriteDerivedRow(pair.Key, pair.Value, currentTexts);

        Log.Info($"LocalizationLoader: {totalStrings} total strings loaded");
    }

    // Rows built at runtime from two loaded rows: a template row formatted with a name row (the
    // generated animal encounter's "You see a {0}!"). Re-derived on every language load, after the CSVs,
    // so both halves are in the same language.
    private sealed class DerivedRow
    {
        public string TemplateKey;
        public string VowelTemplateKey;
        public string NameKey;
        public string FallbackName;
    }

    private static readonly Dictionary<string, DerivedRow> _derivedRows = new();

    /// <summary>
    /// Registers <paramref name="key"/> as <c>string.Format(template, name)</c>, where the template is the
    /// current language's <paramref name="templateKey"/> row (or <paramref name="vowelTemplateKey"/>'s when the
    /// name starts with an English vowel, for "an Owl") and the name is <paramref name="nameKey"/>'s row, or
    /// <paramref name="fallbackName"/> when that key is empty or has no row. Writes the row now and again on
    /// every language load; a row registered before the first load (the Portal Hub's travel buttons, injected
    /// at an earlier load phase) is written by that load, since its template row is not loaded yet. Returns
    /// <paramref name="englishTemplate"/> formatted with
    /// <paramref name="fallbackName"/>, for a <c>DefaultText</c> that stays English whatever the language.
    /// </summary>
    internal static string RegisterDerivedRow(string key, string templateKey, string vowelTemplateKey,
        string nameKey, string fallbackName, string englishTemplate, string englishVowelTemplate)
    {
        var row = new DerivedRow { TemplateKey = templateKey, VowelTemplateKey = vowelTemplateKey, NameKey = nameKey, FallbackName = fallbackName };
        _derivedRows[key] = row;
        var currentTexts = _mods != null ? GetCurrentTexts() : null;
        if (currentTexts != null) WriteDerivedRow(key, row, currentTexts);
        return FormatDerived(englishTemplate, englishVowelTemplate, fallbackName) ?? fallbackName;
    }

    private static string WriteDerivedRow(string key, DerivedRow row, Dictionary<string, string> currentTexts)
    {
        if (!currentTexts.TryGetValue(row.TemplateKey, out var template) || string.IsNullOrEmpty(template))
        {
            Log.Warn($"LocalizationLoader: derived row '{key}' skipped: template row '{row.TemplateKey}' is not loaded");
            return null;
        }
        currentTexts.TryGetValue(row.VowelTemplateKey, out var vowelTemplate);
        string name = !string.IsNullOrEmpty(row.NameKey) && currentTexts.TryGetValue(row.NameKey, out var n) && !string.IsNullOrEmpty(n)
            ? n
            : row.FallbackName;
        string text = FormatDerived(template, vowelTemplate, name);
        if (text != null) currentTexts[key] = text;
        return text;
    }

    private static string FormatDerived(string template, string vowelTemplate, string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        bool vowel = "AEIOUaeiou".IndexOf(name[0]) >= 0;
        string chosen = vowel && !string.IsNullOrEmpty(vowelTemplate) ? vowelTemplate : template;
        try { return string.Format(chosen, name); }
        catch (FormatException ex)
        {
            Log.Warn($"LocalizationLoader: template '{chosen}' could not be formatted with '{name}': {ex.Message}");
            return null;
        }
    }

    // No header row — CSV format: Key,English[,Chinese]
    private static int LoadLocalizationDir(string locDir, string langSuffix, Dictionary<string, string> currentTexts)
    {
        int count = 0;
        foreach (var file in Directory.GetFiles(locDir, "*.csv"))
        {
            var fileName = Path.GetFileNameWithoutExtension(file);

            bool skipFile = false;
            if (!string.IsNullOrEmpty(langSuffix))
            {
                if (langSuffix == "En" && fileName.IndexOf("SimpEn", StringComparison.OrdinalIgnoreCase) < 0)
                    skipFile = true;
                if (langSuffix == "Cn" && fileName.IndexOf("SimpCn", StringComparison.OrdinalIgnoreCase) < 0)
                    skipFile = true;
            }
            else
            {
                if (fileName.IndexOf("SimpEn", StringComparison.OrdinalIgnoreCase) < 0)
                    skipFile = true;
            }
            if (skipFile) { Log.Debug($"LocalizationLoader: skip {fileName} (lang={langSuffix})"); continue; }
            Log.Debug($"LocalizationLoader: reading {fileName} col={((langSuffix == "Cn") ? 2 : 1)}");

            try
            {
                var records = ReadCsvRecords(file);
                int valueCol = (langSuffix == "Cn") ? 2 : 1;

                foreach (var line in records)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var columns = SplitCsvLine(line);
                    if (columns.Count < 2) continue;
                    var key = columns[0].Trim();
                    if (string.IsNullOrEmpty(key)) continue;
                    var value = (valueCol < columns.Count && !string.IsNullOrEmpty(columns[valueCol].Trim()))
                        ? columns[valueCol].Trim()
                        : columns[1].Trim();
                    if (!string.IsNullOrEmpty(value))
                    {
                        currentTexts[key] = value;
                        count++;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"LocalizationLoader: failed to load {file}: {Log.ExceptionText(ex)}");
            }
        }
        return count;
    }

    /// <summary>
    /// The keys a mod's English localization files define, read the way <see cref="LoadLocalizationDir"/>
    /// reads them for English: every <c>*SimpEn*.csv</c> in the mod's Localization folder, the same record
    /// splitting and key trimming, and a key counts only when its English column is non-empty (the loader
    /// registers nothing otherwise). Null when the mod has no such file. Used by the Animals validator to
    /// check that a localization key a manifest names has a row to resolve to.
    /// </summary>
    internal static HashSet<string> ReadEnglishKeys(string modDir)
    {
        var locDir = Path.Combine(modDir, "Localization");
        if (!Directory.Exists(locDir)) return null;

        HashSet<string> keys = null;
        foreach (var file in Directory.GetFiles(locDir, "*.csv"))
        {
            if (Path.GetFileNameWithoutExtension(file).IndexOf("SimpEn", StringComparison.OrdinalIgnoreCase) < 0) continue;
            keys ??= new HashSet<string>(StringComparer.Ordinal);
            try
            {
                foreach (var record in ReadCsvRecords(file))
                {
                    if (string.IsNullOrWhiteSpace(record)) continue;
                    var columns = SplitCsvLine(record);
                    if (columns.Count < 2) continue;
                    var key = columns[0].Trim();
                    if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(columns[1].Trim())) continue;
                    keys.Add(key);
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"LocalizationLoader: could not read English keys from {file}: {Log.ExceptionText(ex)}");
            }
        }
        return keys;
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

    private static List<string> ReadCsvRecords(string file)
    {
        var records = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;

        foreach (var physicalLine in File.ReadLines(file))
        {
            if (current.Length > 0 && inQuotes && IsLikelyLocalizationRecordStart(physicalLine))
            {
                records.Add(current.ToString());
                current.Clear();
                inQuotes = false;
            }

            if (current.Length > 0)
                current.Append('\n');
            current.Append(physicalLine);

            for (int i = 0; i < physicalLine.Length; i++)
            {
                if (physicalLine[i] != '"') continue;

                if (inQuotes && i + 1 < physicalLine.Length && physicalLine[i + 1] == '"')
                {
                    i++;
                    continue;
                }

                inQuotes = !inQuotes;
            }

            if (inQuotes) continue;

            records.Add(current.ToString());
            current.Clear();
        }

        if (current.Length > 0)
            records.Add(current.ToString());

        return records;
    }

    private static bool IsLikelyLocalizationRecordStart(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;

        int comma = line.IndexOf(',');
        if (comma <= 0) return false;

        for (int i = 0; i < comma; i++)
        {
            char c = line[i];
            if (char.IsLetterOrDigit(c) || c == '_' || c == '[' || c == ']') continue;
            return false;
        }

        return true;
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
            Log.Warn($"LocalizationLoader: reflection error: {Log.ExceptionText(ex)}");
            return null;
        }
    }

    private static string GetLanguageSuffix()
    {
        if (ForceChineseMode) return "Cn";

        try
        {
            var locMgrType = ReflectionCache.FindType("LocalizationManager");
            if (locMgrType == null) return CheckOptionsJson();

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
                foreach (var f in locMgrType.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (f.FieldType.IsEnum && f.Name.IndexOf("lang", StringComparison.OrdinalIgnoreCase) >= 0)
                    { lang = f.GetValue(null); break; }
                }
            }

            if (lang != null)
            {
                // CurrentLanguage/currentLanguage is an int index into LocalizationManager.Languages
                // (0=English, 1=Chinese in vanilla EA 0.66b) -- NOT a language-name string, so
                // ToString().Contains("Chinese"/"Cn") can never match here. Compare the index directly.
                if (lang is int langIndex)
                    return langIndex == 1 ? "Cn" : "En";

                var langStr = lang.ToString();
                if (langStr.Contains("Chinese") || langStr.IndexOf("Cn", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Cn";
            }
        }
        catch (Exception ex) { Log.Debug($"LocalizationLoader: game-language detection via reflection failed: {ex.GetType().Name} {ex.Message}"); }

        return CheckOptionsJson();
    }

    private static string CheckOptionsJson()
    {
        try
        {
            var optionsPath = Path.Combine(Application.persistentDataPath, "Options.json");
            if (File.Exists(optionsPath))
            {
                var json = File.ReadAllText(optionsPath);
                var match = System.Text.RegularExpressions.Regex.Match(json,
                    @"""SelectedLanguage""\s*:\s*(\d+)");
                if (match.Success && int.TryParse(match.Groups[1].Value, out int idx) && idx != 0)
                    return "Cn";
            }
        }
        catch (Exception ex) { Log.Debug($"LocalizationLoader: Options.json language check failed: {ex.GetType().Name} {ex.Message}"); }
        return "En";
    }
}
