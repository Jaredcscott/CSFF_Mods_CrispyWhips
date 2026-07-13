using CSFFModFramework.Util;

namespace CSFFModFramework.Portal;

/// <summary>A registered portal world — world 0 is always Fantasy Forest (vanilla).</summary>
internal sealed class PortalWorld
{
    public int               Index;
    public string            WorldName;
    public string            ModName;
    /// <summary>
    /// UniqueID of the CT4 Environment card for direct portal teleportation.
    /// When set, PortalService gives this env card directly, triggering ChangeEnvironment
    /// immediately. Slot items (hand/equipment) travel with the player; floor/placed items
    /// stay at the source (default game behavior). Populated from MapMod.json "EnvironmentUID".
    /// </summary>
    public string            EnvironmentUID;
    /// <summary>
    /// UniqueID of the CT8 location card (legacy fallback). Used when EnvironmentUID is
    /// absent — the loc card is placed on the board and the player navigates manually.
    /// Populated from MapMod.json "SacredSiteUID".
    /// </summary>
    public string            SacredSiteUID;
}

/// <summary>
/// Central registry of portal worlds populated by <see cref="MapModLoader"/> at Phase 5k.
/// World 0 is the implicit Fantasy Forest world (vanilla connections).
/// Mod worlds start at index 1 in discovery order (alphabetical by mod name).
/// </summary>
internal static class PortalRegistry
{
    internal static readonly PortalWorld FantasyForest = new()
    {
        Index = 0,
        WorldName = "Fantasy Forest",
        ModName = "vanilla"
    };

    private static readonly List<PortalWorld> _worlds = new() { FantasyForest };

    internal static IReadOnlyList<PortalWorld> Worlds => _worlds;

    internal static void Register(PortalWorld world)
    {
        world.Index = _worlds.Count;
        _worlds.Add(world);
        Log.Debug($"[PortalRegistry] world {world.Index}: '{world.WorldName}' from {world.ModName}" +
                  $" (envUID={world.EnvironmentUID ?? "none"}, sacredSite={world.SacredSiteUID ?? "none"})");
    }

    internal static void Clear()
    {
        _worlds.Clear();
        _worlds.Add(FantasyForest);
    }
}
