using CSFFModFramework.Discovery;
using CSFFModFramework.Util;

namespace CSFFModFramework.Loading;

internal sealed class MapCacheRecord
{
    public string ModName;
    public string ModDirectory;
    public string RelativePath;
    public string AbsolutePath;
    public string CacheKey;
    public string Version;
    public string GeneratedFrom;
    public DateTime SourceLastWriteUtc;
    public long SourceLength;
    public Dictionary<string, object> Root;
}

internal static class MapCacheLoader
{
    private static readonly List<MapCacheRecord> RecordsList = new();
    private static readonly Dictionary<string, MapCacheRecord> RecordsByKey = new(StringComparer.OrdinalIgnoreCase);

    internal static IReadOnlyList<MapCacheRecord> Records => RecordsList;

    internal static void Clear()
    {
        RecordsList.Clear();
        RecordsByKey.Clear();
    }

    internal static void LoadAll(IReadOnlyList<ModManifest> mods)
    {
        Clear();
        if (mods == null || mods.Count == 0)
            return;

        int parsed = 0;
        int failed = 0;
        foreach (var mod in mods)
        {
            foreach (var file in ModAssets.ResolveMapCaches(mod))
            {
                try
                {
                    var json = ReadAllTextDetectEncoding(file);
                    if (!(MiniJson.Parse(json) is Dictionary<string, object> root))
                    {
                        failed++;
                        Log.Warn($"MapCacheLoader: '{file}' did not parse to a JSON object");
                        continue;
                    }

                    var relativePath = ToRelativePath(mod.DirectoryPath, file);
                    var key = MakeKey(mod.Name, relativePath);
                    if (RecordsByKey.ContainsKey(key))
                    {
                        Log.Warn($"MapCacheLoader: duplicate map cache ignored for {mod.Name}: {relativePath}");
                        continue;
                    }

                    var info = new FileInfo(file);
                    var record = new MapCacheRecord
                    {
                        ModName = mod.Name,
                        ModDirectory = mod.DirectoryPath,
                        RelativePath = relativePath,
                        AbsolutePath = file,
                        CacheKey = key,
                        Version = GetString(root, "Version"),
                        GeneratedFrom = GetString(root, "GeneratedFrom"),
                        SourceLastWriteUtc = info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue,
                        SourceLength = info.Exists ? info.Length : 0,
                        Root = root
                    };

                    RecordsList.Add(record);
                    RecordsByKey[key] = record;
                    parsed++;
                }
                catch (Exception ex)
                {
                    failed++;
                    Log.Warn($"MapCacheLoader: failed to cache '{file}': {Log.ExceptionText(ex)}");
                }
            }
        }

        Log.Debug($"MapCacheLoader: cached {parsed} map cache file(s); failed={failed}");
    }

    internal static bool TryGet(string modName, string relativePath, out MapCacheRecord record)
        => RecordsByKey.TryGetValue(MakeKey(modName, relativePath), out record);

    internal static bool TryGetByFileName(string modName, string fileName, out MapCacheRecord record)
    {
        record = null;
        if (string.IsNullOrWhiteSpace(modName) || string.IsNullOrWhiteSpace(fileName))
            return false;

        foreach (var candidate in RecordsList)
        {
            if (!string.Equals(candidate.ModName, modName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.Equals(Path.GetFileName(candidate.RelativePath), fileName, StringComparison.OrdinalIgnoreCase))
            {
                record = candidate;
                return true;
            }
        }
        return false;
    }

    private static string MakeKey(string modName, string relativePath)
        => $"{modName ?? string.Empty}|{NormalizeRelativePath(relativePath)}";

    private static string ToRelativePath(string modRoot, string file)
    {
        var root = Path.GetFullPath(modRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var full = Path.GetFullPath(file);
        if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return NormalizeRelativePath(full.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return NormalizeRelativePath(file);
    }

    private static string NormalizeRelativePath(string path)
        => (path ?? string.Empty).Replace('\\', '/').TrimStart('/');

    private static string ReadAllTextDetectEncoding(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return System.Text.Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return System.Text.Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return System.Text.Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    private static string GetString(Dictionary<string, object> root, string key)
        => root.TryGetValue(key, out var value) ? value as string : null;
}