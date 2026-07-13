using System.Text;

namespace CSFFModFramework.Api;

/// <summary>
/// Public registry of the world-map travel edges created by the framework's
/// WorldMap injection phase (<c>WorldMap/MapNodes.json</c>). Map-consuming mods
/// (e.g. WaterDrivenInfrastructure's mill-race system) query this after load to
/// extend their own map graphs with mod-added locations — without re-parsing
/// every mod's MapNodes.json or reflecting into WorldMapData.
///
/// <para>Every connection injected into the vanilla WorldMapData singleton is
/// recorded here twice — once per direction (forward and reverse) — matching the
/// bidirectional-pair requirement of world-map adjacency consumers (the
/// mill-race lesson: a single-direction edge must not create one-way
/// connectivity).</para>
///
/// <para><see cref="GetInjectedEdgesJson"/> serializes the edges in the same
/// schema as WDI's <c>Data/MillRaceMapEdges.json</c>
/// (<c>{"Version":…,"GeneratedFrom":…,"Edges":[…]}</c>) so reflection-based
/// consumers can reuse their existing edge-file parser unchanged.</para>
/// </summary>
public static class WorldMap
{
    /// <summary>Cardinal direction codes shared with WDI's edge schema.</summary>
    public const int DirectionNorth = 0, DirectionSouth = 1, DirectionEast = 2, DirectionWest = 3;

    private static readonly string[] DirectionNames = { "North", "South", "East", "West" };

    /// <summary>One directed travel edge between two map locations.</summary>
    public sealed class MapEdge
    {
        /// <summary>UniqueID of the CT4 environment card the edge starts from.</summary>
        public string SourceEnvUid;
        /// <summary>UniqueID of the source environment's CT8 explorable-location card.</summary>
        public string SourceLocationUid;
        /// <summary>Cardinal direction of travel: 0=North, 1=South, 2=East, 3=West.</summary>
        public int Direction;
        /// <summary>UniqueID of the CT4 environment card the edge arrives at.</summary>
        public string DestinationEnvUid;
        /// <summary>UniqueID of the destination environment's CT8 explorable-location card.</summary>
        public string DestinationLocationUid;
        /// <summary>Pathfinding cost of the travel link (vanilla standard is 10).</summary>
        public float PathCost;
        /// <summary>True if the connection is hidden on the in-game map.</summary>
        public bool HiddenOnInGameMap;
        /// <summary>Name of the mod whose MapNodes.json produced this edge.</summary>
        public string SourceMod;

        /// <summary>"North"/"South"/"East"/"West", or "" for an out-of-range code.</summary>
        public string DirectionName =>
            Direction >= 0 && Direction < DirectionNames.Length ? DirectionNames[Direction] : "";
    }

    private static readonly List<MapEdge> _edges = new();

    /// <summary>All edges injected this load, in injection order. Empty until the
    /// WorldMapInjector phase has run (subscribe to <see cref="FrameworkEvents"/> or
    /// query from a LoadMainGameData postfix, which runs after framework loading).</summary>
    public static IReadOnlyList<MapEdge> InjectedEdges => _edges;

    /// <summary>
    /// Serializes <see cref="InjectedEdges"/> as a WDI-schema edge file:
    /// <c>{"Version":"&lt;framework version&gt;","GeneratedFrom":"CSFFModFramework.WorldMapInjector","Edges":[…]}</c>.
    /// Returns an empty-Edges document when nothing was injected.
    /// </summary>
    public static string GetInjectedEdgesJson()
    {
        var sb = new StringBuilder(256 + _edges.Count * 256);
        sb.Append("{\"Version\":\"").Append(JsonEscape(Framework.Version))
          .Append("\",\"GeneratedFrom\":\"CSFFModFramework.WorldMapInjector\",\"Edges\":[");
        for (int i = 0; i < _edges.Count; i++)
        {
            var e = _edges[i];
            if (i > 0) sb.Append(',');
            sb.Append("{\"SourceEnvUid\":\"").Append(JsonEscape(e.SourceEnvUid))
              .Append("\",\"SourceLocationUid\":\"").Append(JsonEscape(e.SourceLocationUid))
              .Append("\",\"Direction\":").Append(e.Direction)
              .Append(",\"DirectionName\":\"").Append(e.DirectionName)
              .Append("\",\"DestinationEnvUid\":\"").Append(JsonEscape(e.DestinationEnvUid))
              .Append("\",\"DestinationLocationUid\":\"").Append(JsonEscape(e.DestinationLocationUid))
              .Append("\",\"PathCost\":").Append(e.PathCost.ToString(System.Globalization.CultureInfo.InvariantCulture))
              .Append(",\"HiddenOnInGameMap\":").Append(e.HiddenOnInGameMap ? "true" : "false")
              .Append('}');
        }
        sb.Append("]}");
        return sb.ToString();
    }

    /// <summary>
    /// Ensures mod-defined map nodes have been injected into the live WorldMapData
    /// singleton. Idempotent — safe to call repeatedly. The framework also calls this
    /// at run start via <c>GameManager.OnGMInitialized</c>; a patch that runs at the
    /// same hook (e.g. a perk gate toggling a node's visibility) should call this
    /// FIRST so the node exists before it is touched, since OnGMInitialized subscriber
    /// order is not guaranteed. No-op until the framework's load phase has prepared
    /// nodes, and on game versions where WorldMapData cannot be resolved.
    /// </summary>
    public static void EnsureInjected() => Injection.WorldMapInjector.InjectIntoWorldMap();

    /// <summary>
    /// Registers a programmatic gate for a world-map connection. The gate is evaluated
    /// at run start and whenever an environment improvement is completed. Use this as a
    /// C# escape hatch when the JSON <c>ConnectionGates</c> field cannot express the
    /// condition (e.g. complex cross-mod state).
    ///
    /// <para>Example (hide the path to the Goblin Caves until the player has the
    /// "Cave Explorer" perk):</para>
    /// <code>
    /// Api.WorldMap.RegisterGate("cmcEnvGoblinCaves", () => player.HasPerk("cave_explorer"));
    /// </code>
    ///
    /// <para>Safe to call at any time after the framework loads. Gates registered before
    /// run start are evaluated on <c>OnGMInitialized</c>.</para>
    /// </summary>
    /// <param name="connectionUID">UniqueID of the CT4 env whose map visibility this gate controls.</param>
    /// <param name="condition">Predicate returning <c>true</c> when the connection should
    /// be shown. Must be thread-safe (invoked on the main Unity thread).</param>
    /// <param name="restoreOnUnlock">When true, travel DAs stripped at lock-time are re-added
    /// when the gate opens. When false (default), the strip is permanent.</param>
    /// <param name="neighborCt8UID">CT8 location card from which to strip the reverse travel DA
    /// when the gate is locked. Null = strip all injected edges touching the target (bidirectional).</param>
    public static void RegisterGate(string connectionUID, Func<bool> condition,
        bool restoreOnUnlock = false, string neighborCt8UID = null)
        => Injection.ConnectionGateService.RegisterGate(connectionUID, condition, restoreOnUnlock, neighborCt8UID);

    /// <summary>
    /// Forces re-evaluation of all registered and declarative gates. Normally called
    /// automatically on run start and when an improvement is completed. Call this manually
    /// after any state change that affects gate conditions — this is what
    /// <c>Injection.SealableGateService</c> does after writing a persistent "cleared" marker
    /// (declarative <c>SealableGates</c> in <c>WorldMap/MapNodes.json</c> superseded the
    /// hand-rolled per-mod gate patches this API doc used to point to as an example).
    /// </summary>
    public static void EvaluateGates() => Injection.ConnectionGateService.EvaluateAll();

    /// <summary>
    /// Hides or shows only the specific bidirectional edge between two environments in
    /// <c>WorldMapData</c>. More precise than <see cref="RegisterGate"/> — only the A↔B
    /// connection lines are affected; neither node's icon is changed, and other connections
    /// from either node are untouched. Useful when a full-env gate would also hide unrelated
    /// connections (e.g. gating only the WaterfallCave↔TinCave edge without affecting
    /// TinCave's Copper/Iron connections).
    /// </summary>
    public static void ToggleEdge(string envUidA, string envUidB, bool hidden)
        => Injection.ConnectionGateService.ToggleEdgeBidirectional(
               envUidA, envUidB, hidden, Injection.WorldMapInjector.GetWorldMapSo());

    /// <summary>
    /// Strips a travel DA at a specific direction from a CT8 location card, caching it for
    /// a later <see cref="RestoreTravelDa"/> call. Idempotent. Use this (with
    /// <see cref="ToggleEdge"/>) when you need per-edge DA control that
    /// <see cref="RegisterGate"/> cannot express.
    /// </summary>
    /// <param name="ct8LocUid">UniqueID of the CT8 location card carrying the DA.</param>
    /// <param name="direction">Cardinal direction (0=N, 1=S, 2=E, 3=W).</param>
    /// <param name="cacheKey">Arbitrary restore-cache key (e.g. the edge name).</param>
    /// <returns>True when a DA was found and stripped.</returns>
    public static bool StripTravelDa(string ct8LocUid, int direction, string cacheKey)
        => Injection.ConnectionGateService.StripTravelDaPublic(ct8LocUid, direction, cacheKey);

    /// <summary>
    /// Restores a travel DA previously stripped by <see cref="StripTravelDa"/>.
    /// </summary>
    /// <returns>True when the DA was found in the cache and re-added.</returns>
    public static bool RestoreTravelDa(string ct8LocUid, int direction, string cacheKey)
        => Injection.ConnectionGateService.RestoreTravelDaPublic(ct8LocUid, direction, cacheKey);

    // ------------------------------------------------------------- internal ---

    internal static void Record(MapEdge edge)
    {
        if (edge != null) _edges.Add(edge);
    }

    internal static void Clear() => _edges.Clear();

    /// <summary>Maps a vanilla TravelDirection unit vector to the cardinal code, or -1.</summary>
    internal static int DirectionFromVector(float x, float z)
    {
        if (z > 0.5f) return DirectionNorth;
        if (z < -0.5f) return DirectionSouth;
        if (x > 0.5f) return DirectionEast;
        if (x < -0.5f) return DirectionWest;
        return -1;
    }

    private static string JsonEscape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
