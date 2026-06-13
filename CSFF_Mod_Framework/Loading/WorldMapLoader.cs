using CSFFModFramework.Discovery;
using CSFFModFramework.Util;

namespace CSFFModFramework.Loading;

/// <summary>
/// Reads <c>WorldMap/MapNodes.json</c> from each mod and returns the parsed node
/// definitions. The actual injection into the live WorldMapData ScriptableObject
/// is performed by <see cref="Injection.WorldMapInjector"/> in a later phase.
///
/// <para><strong>WorldMap/MapNodes.json format:</strong></para>
/// <code>
/// [
///   {
///     "EnvironmentUID":  "&lt;CT4 card UniqueID — mod JSON card, or the NEW UID when cloning&gt;",
///     "CloneOfEnvironmentUID": "&lt;optional: vanilla CT4 UID to mirror at runtime&gt;",
///     "LocationUID":     "&lt;required with CloneOf: UID assigned to the cloned CT8 location card&gt;",
///     "DisplayName":     "&lt;required with CloneOf: player-visible name for both clones&gt;",
///     "NameLocalizationKey":    "&lt;optional: CSV key for the location card name&gt;",
///     "EnvNameLocalizationKey": "&lt;optional: CSV key for the environment card name&gt;",
///     "Coords":         {"x": 10.0, "y": 0.0, "z": 0.0},
///     "HideFromMap":    false,
///     "Icon":           "&lt;optional sprite name; clone nodes default to the template node's icon&gt;",
///     "Connections": [
///       {
///         "EnvironmentUID": "&lt;CT4 card UID of connected env&gt;",
///         "PathCardUID":    "&lt;optional: travel/location card UID; clone nodes default to the cloned CT8&gt;",
///         "PathCost":       10.0,
///         "TravelDirection": "West"            // or {"x": -1, "z": 0}; omitted = derived from Coords
///         "TravelActionTags": ["..."],         // optional; defaults copied from the connected node
///         "HideConnection": false
///       }
///     ]
///   }
/// ]
/// </code>
///
/// <para><strong>Clone-based locations</strong> (<c>CloneOfEnvironmentUID</c> set): the
/// injector clones the loaded vanilla CT4+CT8 pair via <see cref="CardCloneService"/> so
/// the new location meets the full vanilla minimum definition (tags, ambience,
/// improvements, tree drops, blueprint lists) by construction. Without CloneOf, the
/// EnvironmentUID must resolve to a card the mod ships as ordinary CardData JSON.</para>
///
/// <para>Each entry in <c>Connections</c> is automatically made bidirectional by
/// <see cref="Injection.WorldMapInjector"/>: adding A→B also adds B→A with the travel
/// direction negated. Following the vanilla convention, each side of the link uses its
/// OWN node's location card as PathCard (the reverse side reuses the connected node's
/// existing PathCard). The mill-race lesson applies: single-direction links must not
/// create one-way connectivity on the world map.</para>
///
/// <para>MiniJson is used for parsing because JsonUtility.FromJson silently nulls
/// arrays of objects — CLAUDE.md rule.</para>
/// </summary>
internal static class WorldMapLoader
{
    /// <summary>
    /// Parsed representation of one entry from <c>WorldMap/MapNodes.json</c>.
    /// All UIDs are strings; resolution to game objects happens in WorldMapInjector.
    /// </summary>
    internal sealed class MapNodeDefinition
    {
        /// <summary>UniqueID of the CT4 environment card for this map node.</summary>
        public string EnvironmentUID;
        /// <summary>x/y/z world-map coordinates (vanilla 10-unit grid; w is 0 on all
        /// vanilla nodes).</summary>
        public float CoordX, CoordY, CoordZ, CoordW;
        /// <summary>If true, this node is not shown on the in-game map UI.</summary>
        public bool HideFromMap;
        /// <summary>Sprite name (no extension) for the map icon. Null = template/connected
        /// node's icon (clone nodes), or the map default.</summary>
        public string Icon;
        /// <summary>Travel connections from this node to other environments.</summary>
        public List<ConnectionDefinition> Connections = new();

        /// <summary>Vanilla CT4 environment UID to clone at runtime. Null = the mod ships
        /// EnvironmentUID as its own CardData JSON.</summary>
        public string CloneOfEnvironmentUID;
        /// <summary>UID assigned to the cloned CT8 location card (required with CloneOf).</summary>
        public string LocationUID;
        /// <summary>Player-visible name for the cloned env + location cards.</summary>
        public string DisplayName;
        /// <summary>CSV localization key for the cloned location card's name.</summary>
        public string NameLocalizationKey;
        /// <summary>CSV localization key for the cloned environment card's name.</summary>
        public string EnvNameLocalizationKey;

        public string SourceMod;
    }

    /// <summary>One travel link from a node to an existing or mod-defined environment.</summary>
    internal sealed class ConnectionDefinition
    {
        /// <summary>UniqueID of the CT4 environment card to connect to.</summary>
        public string EnvironmentUID;
        /// <summary>UniqueID of the travel/location card for this link. Null = the node's
        /// own location card (clone nodes) or no path card.</summary>
        public string PathCardUID;
        /// <summary>Pathfinding cost for this travel link (vanilla standard is 10).</summary>
        public float PathCost = 10f;
        /// <summary>If true, this connection is hidden from the in-game map.</summary>
        public bool HideConnection;
        /// <summary>Unit travel direction (x=+1 East/−1 West, z=+1 North/−1 South).
        /// Null = derived from the two nodes' Coords.</summary>
        public UnityEngine.Vector4? TravelDirection;
        /// <summary>Runtime names of ActionTag SOs applied to the travel action. Null =
        /// copied from the connected node's existing connections.</summary>
        public List<string> TravelActionTags;
    }

    // ---------------------------------------------------------------- API ---

    /// <summary>
    /// Parses WorldMap/MapNodes.json from every mod that ships one and returns all
    /// valid node definitions, grouped by source mod for error attribution.
    /// Returns an empty list if no mod ships WorldMap content.
    /// </summary>
    public static List<MapNodeDefinition> LoadAll(IReadOnlyList<ModManifest> mods)
    {
        var result = new List<MapNodeDefinition>();

        foreach (var mod in mods)
        {
            var path = System.IO.Path.Combine(mod.DirectoryPath, "WorldMap", "MapNodes.json");
            if (!System.IO.File.Exists(path)) continue;

            try
            {
                var json = System.IO.File.ReadAllText(path);
                var nodes = ParseFile(json, mod.Name);
                result.AddRange(nodes);
                Log.Debug($"WorldMapLoader: {nodes.Count} node(s) from {mod.Name}");
            }
            catch (Exception ex)
            {
                Log.Error($"WorldMapLoader: failed to read {path}: {Log.ExceptionText(ex)}");
            }
        }

        if (result.Count > 0)
            Log.Debug($"WorldMapLoader: {result.Count} total map node(s) from {mods.Count(m => m.HasWorldMapNodes)} mod(s)");

        return result;
    }

    // ---------------------------------------------------------------- parse ---

    private static List<MapNodeDefinition> ParseFile(string json, string modName)
    {
        var result = new List<MapNodeDefinition>();
        var parsed = MiniJson.Parse(json);

        if (parsed is not List<object> arr)
        {
            Log.Warn($"WorldMapLoader: {modName} WorldMap/MapNodes.json root must be a JSON array");
            return result;
        }

        foreach (var item in arr)
        {
            if (item is not Dictionary<string, object> dict) continue;
            try
            {
                var node = ParseNode(dict, modName);
                if (node != null) result.Add(node);
            }
            catch (Exception ex)
            {
                Log.Warn($"WorldMapLoader: {modName} — skipping malformed node entry: {Log.ExceptionText(ex)}");
            }
        }
        return result;
    }

    private static MapNodeDefinition ParseNode(Dictionary<string, object> dict, string modName)
    {
        var envUID = GetString(dict, "EnvironmentUID");
        if (string.IsNullOrEmpty(envUID))
        {
            Log.Warn($"WorldMapLoader: {modName} — node missing required EnvironmentUID, skipping");
            return null;
        }

        var node = new MapNodeDefinition
        {
            EnvironmentUID = envUID,
            HideFromMap    = GetBool(dict, "HideFromMap", false),
            Icon           = GetString(dict, "Icon"),
            SourceMod      = modName,

            CloneOfEnvironmentUID  = GetString(dict, "CloneOfEnvironmentUID"),
            LocationUID            = GetString(dict, "LocationUID"),
            DisplayName            = GetString(dict, "DisplayName"),
            NameLocalizationKey    = GetString(dict, "NameLocalizationKey"),
            EnvNameLocalizationKey = GetString(dict, "EnvNameLocalizationKey"),
        };

        if (!string.IsNullOrEmpty(node.CloneOfEnvironmentUID))
        {
            if (string.IsNullOrEmpty(node.LocationUID))
            {
                Log.Warn($"WorldMapLoader: {modName} — node '{envUID}' declares CloneOfEnvironmentUID but no LocationUID, skipping");
                return null;
            }
            if (string.IsNullOrEmpty(node.DisplayName))
                Log.Warn($"WorldMapLoader: {modName} — clone node '{envUID}' has no DisplayName; it will show the template's name");
            // Default localization keys follow the vanilla "<name>_CardName" convention.
            node.NameLocalizationKey    ??= node.LocationUID + "_CardName";
            node.EnvNameLocalizationKey ??= node.EnvironmentUID + "_CardName";
        }

        // Parse Coords: accept {"x":…, "y":…, "z":…, "w":…} or [x, y, z].
        if (dict.TryGetValue("Coords", out var rawCoords))
        {
            if (rawCoords is Dictionary<string, object> cd)
            {
                node.CoordX = GetFloat(cd, "x", 0f);
                node.CoordY = GetFloat(cd, "y", 0f);
                node.CoordZ = GetFloat(cd, "z", 0f);
                node.CoordW = GetFloat(cd, "w", 0f);
            }
            else if (rawCoords is List<object> ca && ca.Count >= 2)
            {
                node.CoordX = ToFloat(ca[0]);
                node.CoordY = ca.Count > 1 ? ToFloat(ca[1]) : 0f;
                node.CoordZ = ca.Count > 2 ? ToFloat(ca[2]) : 0f;
                node.CoordW = ca.Count > 3 ? ToFloat(ca[3]) : 0f;
            }
        }

        // Parse Connections array
        if (dict.TryGetValue("Connections", out var rawConns) && rawConns is List<object> connArr)
        {
            foreach (var connItem in connArr)
            {
                if (connItem is not Dictionary<string, object> cd) continue;
                var cenvUID = GetString(cd, "EnvironmentUID");
                if (string.IsNullOrEmpty(cenvUID)) continue;

                node.Connections.Add(new ConnectionDefinition
                {
                    EnvironmentUID  = cenvUID,
                    PathCardUID     = GetString(cd, "PathCardUID"),
                    PathCost        = GetFloat(cd, "PathCost", 10f),
                    HideConnection  = GetBool(cd, "HideConnection", false),
                    TravelDirection = ParseTravelDirection(cd),
                    TravelActionTags = ParseStringList(cd, "TravelActionTags"),
                });
            }
        }

        return node;
    }

    /// <summary>
    /// Accepts "North"/"South"/"East"/"West" (case-insensitive) or a {"x":…, "z":…}
    /// object. Returns null when absent — the injector derives the direction from
    /// the two nodes' map coordinates.
    /// </summary>
    private static UnityEngine.Vector4? ParseTravelDirection(Dictionary<string, object> d)
    {
        if (!d.TryGetValue("TravelDirection", out var raw) || raw == null) return null;

        if (raw is string s)
        {
            switch (s.Trim().ToLowerInvariant())
            {
                case "north": return new UnityEngine.Vector4(0, 0, 1, 0);
                case "south": return new UnityEngine.Vector4(0, 0, -1, 0);
                case "east":  return new UnityEngine.Vector4(1, 0, 0, 0);
                case "west":  return new UnityEngine.Vector4(-1, 0, 0, 0);
                default:
                    Log.Warn($"WorldMapLoader: unknown TravelDirection '{s}' — expected North/South/East/West or an x/z object");
                    return null;
            }
        }

        if (raw is Dictionary<string, object> td)
            return new UnityEngine.Vector4(
                GetFloat(td, "x", 0f), GetFloat(td, "y", 0f), GetFloat(td, "z", 0f), GetFloat(td, "w", 0f));

        return null;
    }

    private static List<string> ParseStringList(Dictionary<string, object> d, string key)
    {
        if (!d.TryGetValue(key, out var raw) || raw is not List<object> arr) return null;
        var list = new List<string>(arr.Count);
        foreach (var item in arr)
            if (item is string s && !string.IsNullOrEmpty(s)) list.Add(s);
        return list.Count > 0 ? list : null;
    }

    // -------------------------------------------------------- dict helpers ---

    private static string GetString(Dictionary<string, object> d, string key)
        => d.TryGetValue(key, out var v) ? v as string : null;

    private static bool GetBool(Dictionary<string, object> d, string key, bool def)
        => d.TryGetValue(key, out var v) && v is bool b ? b : def;

    private static float GetFloat(Dictionary<string, object> d, string key, float def)
        => d.TryGetValue(key, out var v) ? ToFloat(v, def) : def;

    private static float ToFloat(object v, float def = 0f)
    {
        try { return Convert.ToSingle(v); } catch { return def; }
    }
}
