using CSFFModFramework.Loading;

namespace CSFFModFramework.Api;

/// <summary>
/// Read-only view of mods discovered and loaded by the framework.
/// Populated by the time <see cref="FrameworkEvents.Loaded"/> fires.
/// Returns an empty list if queried before framework load completes.
/// </summary>
public static class ModRegistry
{
    /// <summary>All mods the framework discovered this session, sorted by name.</summary>
    public static IReadOnlyList<ModInfo> All
    {
        get
        {
            var src = LoadOrchestrator.LoadedMods;
            if (src == null || src.Count == 0)
                return Array.Empty<ModInfo>();

            var list = new List<ModInfo>(src.Count);
            foreach (var m in src) list.Add(new ModInfo(m));
            return list;
        }
    }

    /// <summary>Find a mod by its <see cref="ModInfo.Name"/>. Case-insensitive. Returns null if not found.</summary>
    public static ModInfo Get(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var src = LoadOrchestrator.LoadedMods;
        if (src == null) return null;

        foreach (var m in src)
        {
            if (string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
                return new ModInfo(m);
        }
        return null;
    }

    /// <summary>True if a mod with the given name was discovered this session.</summary>
    public static bool IsLoaded(string name) => Get(name) != null;
}
