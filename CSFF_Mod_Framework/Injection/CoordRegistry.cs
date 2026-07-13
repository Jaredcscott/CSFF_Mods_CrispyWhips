using CSFFModFramework.Util;
using static CSFFModFramework.Loading.WorldMapLoader;

namespace CSFFModFramework.Injection;

/// <summary>
/// Enforces WorldMap grid-cell uniqueness (design principle P3) across every mod that ships
/// <c>WorldMap/MapNodes.json</c>. <c>WorldMapData</c> is an integer (x, z) grid; two nodes on
/// the same cell produce undefined pathfinding and corrupt map rendering (CLAUDE.md §WorldMap
/// Node Coordinates). This registry is the framework's <em>only</em> load-time mutual-exclusion
/// constraint — portal anchor envs and vanilla-exit targets are explicitly NON-exclusive
/// (multiple mods may share them).
///
/// <para><strong>Policy:</strong> first-registered coordinate wins. A mod with ANY cell that
/// collides with a different, already-accepted mod — or with one of its own other nodes — is
/// rejected <em>atomically</em> from map injection: none of its nodes are cloned or injected,
/// and it claims no cells (so it cannot spuriously reject a later mod). A rejected mod's items,
/// perks, blueprints, and quests still load normally — only its WorldMap injection is skipped
/// (see <see cref="WorldMapInjector.PrepareAll"/>, which consults <see cref="IsRejected"/>).
/// The shared Portal Hub System does not consult this registry — every mod's portal button is
/// non-exclusive regardless of coordinate collisions (see <see cref="Portal.PortalService"/>).</para>
///
/// <para>Mods are processed in <c>ModDiscovery</c> order (framework first, then alphabetical by
/// mod name), which is deterministic within a session — so collision outcomes are reproducible.
/// The node list passed to <see cref="RegisterAll"/> is already grouped per-mod in that order
/// (<c>WorldMapLoader.LoadAll</c> appends each mod's nodes in turn).</para>
/// </summary>
internal static class CoordRegistry
{
    private static readonly Dictionary<(int x, int z), string> _claimedCoords = new();
    private static readonly HashSet<string> _rejectedMods = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Clears all claims and rejections. Called at the start of every
    /// <see cref="RegisterAll"/> so a re-prepare within the session starts clean.</summary>
    internal static void Reset()
    {
        _claimedCoords.Clear();
        _rejectedMods.Clear();
    }

    /// <summary>
    /// Validates every node's (x, z) cell across all mods and records which mods are rejected.
    /// Must run BEFORE any clone work in <see cref="WorldMapInjector.PrepareAll"/>.
    /// </summary>
    internal static void RegisterAll(IEnumerable<MapNodeDefinition> allNodes)
    {
        Reset();
        if (allNodes == null) return;

        // Group nodes by source mod, preserving first-appearance (= discovery) order.
        var order = new List<string>();
        var byMod = new Dictionary<string, List<MapNodeDefinition>>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in allNodes)
        {
            if (node == null) continue;
            var mod = node.SourceMod ?? "<unknown>";
            if (!byMod.TryGetValue(mod, out var list))
            {
                list = new List<MapNodeDefinition>();
                byMod[mod] = list;
                order.Add(mod);
            }
            list.Add(node);
        }

        foreach (var mod in order)
        {
            var nodes = byMod[mod];

            // Phase 1 — validate WITHOUT claiming, so a rejected mod commits nothing.
            bool reject = false;
            var selfSeen = new HashSet<(int x, int z)>();
            foreach (var node in nodes)
            {
                var coord = ((int)Math.Round(node.CoordX), (int)Math.Round(node.CoordZ));

                if (_claimedCoords.TryGetValue(coord, out var owner))
                {
                    Log.Error($"[CoordRegistry] mod '{mod}' rejected from WorldMap injection — grid cell " +
                              $"({coord.Item1},{coord.Item2}) already claimed by '{owner}' (node '{node.EnvironmentUID}'). " +
                              $"Items/perks/blueprints still load; only this mod's map nodes are skipped. " +
                              $"Fix: choose a free (x,z) cell — see CLAUDE.md §WorldMap Node Coordinates.");
                    reject = true;
                    break;
                }
                if (!selfSeen.Add(coord))
                {
                    Log.Error($"[CoordRegistry] mod '{mod}' rejected — two of its own nodes occupy grid cell " +
                              $"({coord.Item1},{coord.Item2}) (node '{node.EnvironmentUID}'). Each map node needs a unique (x,z).");
                    reject = true;
                    break;
                }
            }

            if (reject) { _rejectedMods.Add(mod); continue; }

            // Phase 2 — commit (validated; no collisions possible now).
            foreach (var node in nodes)
                _claimedCoords[((int)Math.Round(node.CoordX), (int)Math.Round(node.CoordZ))] = mod;
        }

        if (_rejectedMods.Count == 0)
            Log.Debug($"[CoordRegistry] {_claimedCoords.Count} grid cell(s) claimed across {order.Count} mod(s); no collisions");
        else
            Log.Warn($"[CoordRegistry] {_rejectedMods.Count} mod(s) rejected for grid-cell collision; {_claimedCoords.Count} cell(s) claimed");
    }

    /// <summary>True when <paramref name="modId"/> had a grid-cell collision and must be
    /// skipped during map + portal injection. Returns false for any unknown mod.</summary>
    internal static bool IsRejected(string modId)
        => modId != null && _rejectedMods.Contains(modId);
}
