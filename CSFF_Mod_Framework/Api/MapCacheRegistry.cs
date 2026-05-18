using CSFFModFramework.Loading;

namespace CSFFModFramework.Api;

/// <summary>
/// Access parsed JSON map caches supplied by mods. The framework loads these
/// once during startup, using MiniJson rather than Unity JsonUtility so object
/// arrays remain intact.
/// </summary>
public static class MapCacheRegistry
{
    public static IReadOnlyList<MapCacheInfo> All
    {
        get
        {
            if (!LoadOrchestrator.LoadSucceeded)
                FrameworkLog.Warn("MapCacheRegistry.All queried before framework load completed — returning current partial cache list");

            var src = MapCacheLoader.Records;
            if (src == null || src.Count == 0)
                return Array.Empty<MapCacheInfo>();

            var list = new List<MapCacheInfo>(src.Count);
            foreach (var record in src)
                list.Add(new MapCacheInfo(record));
            return list;
        }
    }

    public static MapCacheInfo Get(string modName, string relativePath)
    {
        return TryGet(modName, relativePath, out var cache) ? cache : null;
    }

    public static bool TryGet(string modName, string relativePath, out MapCacheInfo cache)
    {
        cache = null;
        if (MapCacheLoader.TryGet(modName, relativePath, out var record))
        {
            cache = new MapCacheInfo(record);
            return true;
        }
        return false;
    }

    public static bool TryGetByFileName(string modName, string fileName, out MapCacheInfo cache)
    {
        cache = null;
        if (MapCacheLoader.TryGetByFileName(modName, fileName, out var record))
        {
            cache = new MapCacheInfo(record);
            return true;
        }
        return false;
    }
}