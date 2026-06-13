using System.Collections;
using System.Reflection;
using CSFFModFramework.Data;
using CSFFModFramework.Loading;
using CSFFModFramework.Util;
using static CSFFModFramework.Loading.WorldMapLoader;

namespace CSFFModFramework.Injection;

/// <summary>
/// WorldMap injection — adds mod-defined environment nodes to the vanilla
/// WorldMapData singleton. Runs in two stages because the data the injector
/// produces has two different consumers with two different timings.
///
/// <para><strong>Stage 1 — <see cref="PrepareAll"/> (data-load time, LoadMainGameData
/// postfix):</strong> clones the CT4+CT8 environment pair (registering the clones in
/// <c>DataBase.AllData</c>) and records the travel edges in <see cref="Api.WorldMap"/>.
/// Both must happen at load time: map-consuming mods (e.g. WDI's mill-race system)
/// scan AllData for the cloned location card and read <see cref="Api.WorldMap"/> from
/// their OWN LoadMainGameData postfix, which runs after the framework's.</para>
///
/// <para><strong>Stage 2 — <see cref="InjectIntoWorldMap"/> (deferred to run start,
/// GameManager.OnGMInitialized):</strong> appends the MapEnvData node and wires its
/// connections into <c>WorldMapData.Environments[]</c>. This is deferred because the
/// <c>WorldMapData</c> ScriptableObject is NOT loaded into memory at data-load time
/// (verified EA 0.64f: <c>Resources.FindObjectsOfTypeAll(WorldMapData)</c> returns
/// empty during the framework's LoadMainGameData postfix; the SO is loaded later,
/// around GameManager initialization). Injecting the node before WorldMapData exists
/// silently did nothing — the original single-stage injector bailed out entirely, so
/// no clone, no edge, and no travel node were produced.</para>
///
/// <para>Stage 2 is idempotent (WorldMapData is session-static — injecting once per
/// process suffices) and exposed publicly via <see cref="Api.WorldMap.EnsureInjected"/>
/// so a perk-gating patch that also runs at OnGMInitialized can guarantee the node
/// exists before it toggles visibility, regardless of OnGMInitialized subscription
/// order.</para>
///
/// <para><strong>Vanilla node anatomy</strong> (verified against the EA 0.64f
/// DefaultWorldMap export): each MapEnvData entry carries Environment (EnvID →
/// MainEnvCard CT4), Coords (Coordinates — integer grid, 10 units per step), HideFromInGameMap,
/// Icon (Sprite), SpatialAmbientSounds, and ConnectedEnvironments[] where every
/// connection has PathCard (the node's OWN CT8 location card), InitialPathCost (10),
/// TravelDirection (unit vector: z=+1 North, x=+1 East), TravelActionTags, and
/// HiddenOnInGameMap. This injector reproduces all of those fields; clone-based
/// nodes inherit Icon and TravelActionTags from their template's map node.</para>
///
/// <para><strong>Bidirectionality:</strong> For each connection A→B, a reverse
/// connection B→A is automatically added to the existing environment's
/// ConnectedEnvironments list with the travel direction negated (the mill-race
/// lesson — single-direction edges must not create one-way travel on the world
/// map). Per the vanilla convention, the reverse connection uses B's own existing
/// PathCard, not A's.</para>
/// </summary>
internal static class WorldMapInjector
{
    private const BindingFlags BF =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // Cached reflected types resolved once on first inject call.
    private static Type _worldMapDataType;
    private static Type _mapEnvDataType;
    private static Type _envIdType;
    private static Type _connEnvType;         // connected environment entry type
    private static FieldInfo _environmentsField;   // WorldMapData.Environments (array)
    private static FieldInfo _envField;            // MapEnvData.Environment (EnvID)
    private static FieldInfo _connEnvsField;       // MapEnvData.ConnectedEnvironments (array)
    private static FieldInfo _coordsField;         // MapEnvData.Coords (Coordinates, int)
    private static FieldInfo _hideFromMapField;    // MapEnvData.HideFromInGameMap (bool)
    private static FieldInfo _iconField;           // MapEnvData.Icon (Sprite)
    private static FieldInfo _mainEnvCardField;    // EnvID.MainEnvCard (CardData)
    private static FieldInfo _parentEnvsField;     // EnvID.ParentEnvs (EnvID[])
    private static FieldInfo _pathCardField;       // ConnEnv.PathCard (CardData)
    private static FieldInfo _connectsToField;     // ConnEnv.ConnectsTo (EnvID)
    private static FieldInfo _pathCostField;       // ConnEnv.InitialPathCost (float)
    private static FieldInfo _hiddenConnField;     // ConnEnv.HiddenOnInGameMap (bool)
    private static FieldInfo _travelDirField;      // ConnEnv.TravelDirection (Coordinates)
    private static FieldInfo _travelTagsField;     // ConnEnv.TravelActionTags (ActionTag[])
    // Coordinates is the game's integer {x,y,z,w} struct used by BOTH MapEnvData.Coords
    // and TravelLink.TravelDirection — NOT UnityEngine.Vector4 (verified EA 0.64f
    // ScriptableObjectTypeJsonData/Coordinates.json). Writing a Vector4 into either field
    // throws ArgumentException, so we convert through these.
    private static Type _coordinatesType;
    private static FieldInfo _coordXField, _coordYField, _coordZField, _coordWField;
    private static bool _typesResolved;

    /// <summary>A node prepared at load time, awaiting WorldMapData injection at run start.</summary>
    private sealed class PreparedNode
    {
        public MapNodeDefinition Def;
        public UniqueIDScriptable EnvCard;   // resolved or cloned CT4 environment card
        public CardData LocationCard;        // cloned CT8 location card (null for non-clone nodes)
    }

    private static readonly List<PreparedNode> _prepared = new();
    private static bool _injectedIntoWorldMap;   // session-once fast path for stage 2
    private static bool _subscribed;
    private static Action _gmInitializedHandler;
    private static object _worldMapCached;

    // ---------------------------------------------------------- stage 1 ---

    /// <summary>
    /// Stage 1 (data-load time): clone env/location pairs, register them in AllData,
    /// record travel edges in <see cref="Api.WorldMap"/>, and arm the deferred
    /// WorldMapData injection at run start. Must run after <c>Database.InitFromGame()</c>.
    /// </summary>
    public static void PrepareAll(List<MapNodeDefinition> nodes)
    {
        _prepared.Clear();
        _injectedIntoWorldMap = false;
        _worldMapCached = null;
        Api.WorldMap.Clear();

        if (nodes == null || nodes.Count == 0) return;

        if (!TryResolveTypes())
        {
            Log.Warn("WorldMapInjector: could not resolve WorldMapData types — map injection skipped. " +
                     "This is expected if the game version changed WorldMapData's type name.");
            return;
        }

        foreach (var node in nodes)
        {
            try
            {
                var prep = PrepareNode(node);
                if (prep != null) _prepared.Add(prep);
            }
            catch (Exception ex)
            {
                Log.Error($"WorldMapInjector: failed to prepare node '{node.EnvironmentUID}' from {node.SourceMod}: {Log.ExceptionText(ex)}");
            }
        }

        // Record travel edges now so map-consuming mods (WDI) that read Api.WorldMap
        // during their own LoadMainGameData postfix — which runs after ours — see them.
        foreach (var prep in _prepared)
            foreach (var conn in prep.Def.Connections)
            {
                try { RecordEdgeLoadTime(prep, conn); }
                catch (Exception ex)
                {
                    Log.Debug($"WorldMapInjector: edge record failed for {prep.Def.EnvironmentUID}→{conn.EnvironmentUID}: {ex.GetType().Name}");
                }
            }

        Log.Info($"WorldMapInjector: prepared {_prepared.Count} node(s), {Api.WorldMap.InjectedEdges.Count} travel edge(s) recorded; " +
                 "WorldMapData node injection deferred to run start (WorldMapData is not loaded at data-load time)");

        TrySubscribeGmInitialized();
    }

    /// <summary>Resolves (or clones) the env/location cards for one node. Null on failure.</summary>
    private static PreparedNode PrepareNode(MapNodeDefinition node)
    {
        UniqueIDScriptable envCard;
        CardData locationCard = null;

        if (!string.IsNullOrEmpty(node.CloneOfEnvironmentUID))
        {
            // Already cloned this session (duplicate load / re-prepare)? Reuse — never re-clone.
            var existing = GameRegistry.GetByUid(node.EnvironmentUID);
            if (existing != null)
            {
                Log.Debug($"WorldMapInjector: clone '{node.EnvironmentUID}' already registered — reusing");
                return new PreparedNode
                {
                    Def = node,
                    EnvCard = existing,
                    LocationCard = GameRegistry.GetByUid(node.LocationUID) as CardData,
                };
            }

            var templateEnv = GameRegistry.GetByUid(node.CloneOfEnvironmentUID);
            if (templateEnv == null)
            {
                Log.Warn($"WorldMapInjector: CloneOfEnvironmentUID '{node.CloneOfEnvironmentUID}' (mod {node.SourceMod}) not found — skipping node '{node.EnvironmentUID}'");
                return null;
            }

            if (!CardCloneService.TryCloneEnvironmentPair(
                    node.CloneOfEnvironmentUID, node.EnvironmentUID, node.LocationUID,
                    node.DisplayName, node.EnvNameLocalizationKey, node.NameLocalizationKey,
                    node.SourceMod, out var envClone, out locationCard))
                return null;
            envCard = envClone;
        }
        else
        {
            envCard = GameRegistry.GetByUid(node.EnvironmentUID);
            if (envCard == null)
            {
                Log.Warn($"WorldMapInjector: EnvironmentUID '{node.EnvironmentUID}' (mod {node.SourceMod}) not found in registry — skipping");
                return null;
            }
        }

        return new PreparedNode { Def = node, EnvCard = envCard, LocationCard = locationCard };
    }

    // ---------------------------------------------------------- stage 2 ---

    /// <summary>
    /// Stage 2 (run start / on demand): appends prepared nodes into the live
    /// WorldMapData singleton and wires their bidirectional connections. Idempotent —
    /// safe to call repeatedly; nodes already present are skipped. Public entry point
    /// is <see cref="Api.WorldMap.EnsureInjected"/>.
    /// </summary>
    internal static void InjectIntoWorldMap()
    {
        if (_injectedIntoWorldMap || _prepared.Count == 0) return;
        if (!TryResolveTypes()) return;

        var worldMapSo = FindWorldMapData();
        if (worldMapSo == null)
        {
            Log.Warn("WorldMapInjector: WorldMapData singleton not found at run start — map nodes cannot be injected this run (will retry next run start)");
            return;
        }

        int injected = 0, already = 0, skipped = 0;
        foreach (var prep in _prepared)
        {
            try
            {
                var environments = _environmentsField.GetValue(worldMapSo);
                if (FindNodeByEnvCard(environments, prep.EnvCard) != null)
                {
                    already++;
                    continue;   // already injected into this session-static map
                }

                if (InjectPreparedNode(worldMapSo, prep)) injected++;
                else skipped++;
            }
            catch (Exception ex)
            {
                Log.Error($"WorldMapInjector: failed to inject node '{prep.Def.EnvironmentUID}': {Log.ExceptionText(ex)}");
                skipped++;
            }
        }

        // Fast-path future calls only when every node is accounted for; leave the gate
        // open to retry if something was skipped (the per-node guard keeps retries safe).
        if (skipped == 0) _injectedIntoWorldMap = true;

        Log.Info($"WorldMapInjector: {injected} node(s) injected into WorldMapData, {already} already present, {skipped} skipped");
    }

    private static bool InjectPreparedNode(object worldMapSo, PreparedNode prep)
    {
        var node = prep.Def;
        var envCard = prep.EnvCard;
        var locationCard = prep.LocationCard;

        // For clone nodes, re-find the template's map node (now that WorldMapData is
        // loaded) for icon / travel-tag inheritance.
        object templateNode = null;
        if (!string.IsNullOrEmpty(node.CloneOfEnvironmentUID))
        {
            var templateEnv = GameRegistry.GetByUid(node.CloneOfEnvironmentUID);
            if (templateEnv != null)
                templateNode = FindNodeByEnvCard(_environmentsField.GetValue(worldMapSo), templateEnv);
            if (templateNode == null)
                Log.Debug($"WorldMapInjector: template env '{node.CloneOfEnvironmentUID}' has no map node — icon/travel-tag inheritance unavailable");
        }

        // Build and append the node entry first (connections fill in below) so a later
        // node in the same batch can connect to this one.
        var environments = _environmentsField.GetValue(worldMapSo);
        var mapEnvData = CreateMapEnvData(envCard, node, templateNode);
        var newEnvironments = AppendToCollection(environments, _mapEnvDataType, mapEnvData);
        if (newEnvironments == null) return false;
        _environmentsField.SetValue(worldMapSo, newEnvironments);

        int linked = 0;
        foreach (var conn in node.Connections)
        {
            if (LinkConnection(newEnvironments, mapEnvData, envCard, locationCard, templateNode, node, conn))
                linked++;
        }

        // Struct safety: if MapEnvData is a value type, the array holds a copy made at
        // append time — write the mutated boxed instance back over it (last index).
        if (_mapEnvDataType.IsValueType && newEnvironments is Array envArr && envArr.Length > 0)
            envArr.SetValue(mapEnvData, envArr.Length - 1);

        Log.Debug($"WorldMapInjector: injected '{node.EnvironmentUID}' into WorldMapData with {linked}/{node.Connections.Count} connection(s) from {node.SourceMod}");
        return true;
    }

    // -------------------------------------------------- run-start hook ---

    private static void TrySubscribeGmInitialized()
    {
        if (_subscribed) return;
        try
        {
            var gmType = AccessTools.TypeByName("GameManager");
            var field = gmType?.GetField("OnGMInitialized",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null || field.FieldType != typeof(Action))
            {
                Log.Warn("WorldMapInjector: GameManager.OnGMInitialized not found — map nodes cannot be injected (run-start deferral unavailable)");
                return;
            }

            _gmInitializedHandler = OnGameManagerInitialized;
            field.SetValue(null, (Action)Delegate.Combine((Action)field.GetValue(null), _gmInitializedHandler));
            _subscribed = true;
            Log.Debug("WorldMapInjector: subscribed to GameManager.OnGMInitialized for deferred map injection");
        }
        catch (Exception ex)
        {
            Log.Warn($"WorldMapInjector: failed to subscribe to OnGMInitialized: {Log.ExceptionText(ex)}");
        }
    }

    private static void OnGameManagerInitialized()
    {
        try { InjectIntoWorldMap(); }
        catch (Exception ex) { Log.Error($"WorldMapInjector: deferred injection failed: {Log.ExceptionText(ex)}"); }
    }

    // --------------------------------------------------------- type setup ---

    private static bool TryResolveTypes()
    {
        if (_typesResolved) return _worldMapDataType != null;
        _typesResolved = true;

        _worldMapDataType = AccessTools.TypeByName("WorldMapData");
        if (_worldMapDataType == null) return false;

        _environmentsField = FindField(_worldMapDataType, "Environments");
        if (_environmentsField == null) return false;

        var envArrType = _environmentsField.FieldType;
        _mapEnvDataType = envArrType.IsArray ? envArrType.GetElementType()
            : envArrType.IsGenericType ? envArrType.GetGenericArguments()[0]
            : null;
        if (_mapEnvDataType == null) return false;

        _envField        = FindField(_mapEnvDataType, "Environment");
        _connEnvsField   = FindField(_mapEnvDataType, "ConnectedEnvironments");
        _coordsField     = FindField(_mapEnvDataType, "Coords");
        _hideFromMapField = FindField(_mapEnvDataType, "HideFromInGameMap");
        _iconField       = FindField(_mapEnvDataType, "Icon");

        if (_envField == null || _connEnvsField == null)
        {
            Log.Warn("WorldMapInjector: MapEnvData missing expected fields (Environment/ConnectedEnvironments). Struct layout may have changed.");
            return false;
        }

        _envIdType = _envField.FieldType;
        _mainEnvCardField = FindField(_envIdType, "MainEnvCard");
        _parentEnvsField  = FindField(_envIdType, "ParentEnvs");

        var connArrType = _connEnvsField.FieldType;
        _connEnvType = connArrType.IsArray ? connArrType.GetElementType()
            : connArrType.IsGenericType ? connArrType.GetGenericArguments()[0]
            : null;

        if (_connEnvType != null)
        {
            _pathCardField   = FindField(_connEnvType, "PathCard");
            _connectsToField = FindField(_connEnvType, "ConnectsTo");
            _pathCostField   = FindField(_connEnvType, "InitialPathCost");
            _hiddenConnField = FindField(_connEnvType, "HiddenOnInGameMap");
            _travelDirField  = FindField(_connEnvType, "TravelDirection");
            _travelTagsField = FindField(_connEnvType, "TravelActionTags");
        }

        // Coordinates type (int x/y/z/w) shared by Coords and TravelDirection.
        _coordinatesType = _travelDirField?.FieldType ?? _coordsField?.FieldType
            ?? AccessTools.TypeByName("Coordinates");
        if (_coordinatesType != null)
        {
            _coordXField = FindField(_coordinatesType, "x");
            _coordYField = FindField(_coordinatesType, "y");
            _coordZField = FindField(_coordinatesType, "z");
            _coordWField = FindField(_coordinatesType, "w");
        }

        Log.Debug($"WorldMapInjector: types resolved — MapEnvData={_mapEnvDataType.Name}, EnvID={_envIdType?.Name}, ConnEnv={_connEnvType?.Name}, Coordinates={_coordinatesType?.Name}");
        return true;
    }

    /// <summary>Boxes a Coordinates (int x/y/z/w) value; null when the type is unavailable.</summary>
    private static object MakeCoordinates(int x, int y, int z, int w)
    {
        if (_coordinatesType == null) return null;
        var c = Activator.CreateInstance(_coordinatesType);
        _coordXField?.SetValue(c, x);
        _coordYField?.SetValue(c, y);
        _coordZField?.SetValue(c, z);
        _coordWField?.SetValue(c, w);
        return c;
    }

    /// <summary>Reads a boxed Coordinates back into a Vector4 (for coordinate math).</summary>
    private static UnityEngine.Vector4 CoordinatesToVector4(object coord)
    {
        if (coord == null) return UnityEngine.Vector4.zero;
        return new UnityEngine.Vector4(
            ToInt(_coordXField?.GetValue(coord)), ToInt(_coordYField?.GetValue(coord)),
            ToInt(_coordZField?.GetValue(coord)), ToInt(_coordWField?.GetValue(coord)));
    }

    private static int ToInt(object v)
    {
        try { return v == null ? 0 : Convert.ToInt32(v); } catch { return 0; }
    }

    private static FieldInfo FindField(Type type, string name)
    {
        for (var t = type; t != null; t = t.BaseType)
        {
            var f = t.GetField(name, BF);
            if (f != null) return f;
        }
        return null;
    }

    // --------------------------------------------------------- discovery ---

    private static object FindWorldMapData()
    {
        if (_worldMapCached != null) return _worldMapCached;

        // The name-keyed dict is built at data-load time, when WorldMapData usually
        // isn't loaded yet — check anyway in case a warm reload captured it.
        if (Database.AllScriptableObjectDict.TryGetValue("DefaultWorldMap", out var so)
            && _worldMapDataType.IsInstanceOfType(so))
            return _worldMapCached = so;

        // Scan loaded objects (one call; WorldMapData isn't a UniqueIDScriptable so it's
        // not in AllData). Prefer the instance with the most Environments — the real
        // vanilla map (123 nodes), not an empty stub another loader may have registered.
        try
        {
            var all = UnityEngine.Resources.FindObjectsOfTypeAll(_worldMapDataType);
            if (all == null || all.Length == 0) return null;

            object best = null;
            int bestCount = -1;
            foreach (var candidate in all)
            {
                if (candidate == null) continue;
                int count = (_environmentsField.GetValue(candidate) as IList)?.Count ?? 0;
                if (count > bestCount) { best = candidate; bestCount = count; }
            }

            if (best != null)
                Log.Debug($"WorldMapInjector: WorldMapData found via Resources scan ({all.Length} candidate(s), selected one with {bestCount} environments)");
            return _worldMapCached = best;
        }
        catch (Exception ex)
        {
            Log.Debug($"WorldMapInjector: Resources scan failed: {ex.GetType().Name}");
        }
        return null;
    }

    // ---------------------------------------------------------- node build ---

    private static object CreateMapEnvData(object envCard, MapNodeDefinition node, object templateNode)
    {
        var mapEnvData = Activator.CreateInstance(_mapEnvDataType);

        // Set Environment (EnvID): MainEnvCard = resolved CT4 card, ParentEnvs = empty.
        _envField.SetValue(mapEnvData, CreateEnvId(envCard));

        // Coords (Coordinates — integer grid, NOT Vector4)
        if (_coordsField != null)
        {
            var coord = MakeCoordinates(
                (int)Math.Round(node.CoordX), (int)Math.Round(node.CoordY),
                (int)Math.Round(node.CoordZ), (int)Math.Round(node.CoordW));
            if (coord != null) _coordsField.SetValue(mapEnvData, coord);
        }

        // HideFromInGameMap (bool)
        _hideFromMapField?.SetValue(mapEnvData, node.HideFromMap);

        // Icon (Sprite): explicit name → SpriteDict; else inherit the template node's icon.
        if (_iconField != null)
        {
            if (!string.IsNullOrEmpty(node.Icon))
            {
                if (Database.SpriteDict.TryGetValue(node.Icon, out var sprite))
                    _iconField.SetValue(mapEnvData, sprite);
                else
                    Log.Debug($"WorldMapInjector: icon sprite '{node.Icon}' not found — node will use default icon");
            }
            else if (templateNode != null)
            {
                _iconField.SetValue(mapEnvData, _iconField.GetValue(templateNode));
            }
        }

        // Empty connections container — populated by LinkConnection.
        if (_connEnvType != null)
            _connEnvsField.SetValue(mapEnvData, EmptyCollection(_connEnvsField));

        // Never leave null arrays on an entry we created — the map UI iterates
        // SpatialAmbientSounds and friends without null checks. (This is OUR object,
        // not a vanilla one, so initializing defaults is allowed.)
        InitNullArrayFields(mapEnvData);

        return mapEnvData;
    }

    private static object CreateEnvId(object envCard)
    {
        if (_envIdType == null) return null;
        var envId = Activator.CreateInstance(_envIdType);
        _mainEnvCardField?.SetValue(envId, envCard);
        if (_parentEnvsField != null)
            _parentEnvsField.SetValue(envId, EmptyCollection(_parentEnvsField));
        return envId;
    }

    // --------------------------------------------------------- connections ---

    /// <summary>
    /// Creates the forward connection on the new node and the reverse connection on the
    /// existing target node. (Edge recording for map consumers happens at load time in
    /// <see cref="RecordEdgeLoadTime"/>, not here.)
    /// </summary>
    private static bool LinkConnection(object environments, object mapEnvData,
        object envCard, CardData locationCard, object templateNode,
        MapNodeDefinition node, ConnectionDefinition conn)
    {
        if (_connEnvType == null || _connectsToField == null) return false;

        var targetCard = GameRegistry.GetByUid(conn.EnvironmentUID);
        if (targetCard == null)
        {
            Log.Warn($"WorldMapInjector: connection EnvironmentUID '{conn.EnvironmentUID}' not found — skipping connection");
            return false;
        }

        var (targetNode, targetIndex) = FindNodeWithIndex(environments, targetCard);
        if (targetNode == null)
            Log.Warn($"WorldMapInjector: env '{conn.EnvironmentUID}' has no map node — connection from '{node.EnvironmentUID}' will be ONE-WAY (no reverse link)");

        // Travel direction: explicit, else derived from the two nodes' coordinates.
        var dir = conn.TravelDirection ?? DeriveDirection(mapEnvData, targetNode);

        // Forward PathCard: explicit UID, else the node's own location card (vanilla convention).
        object fwdPathCard = null;
        if (!string.IsNullOrEmpty(conn.PathCardUID))
        {
            fwdPathCard = GameRegistry.GetByUid(conn.PathCardUID);
            if (fwdPathCard == null)
                Log.Debug($"WorldMapInjector: PathCardUID '{conn.PathCardUID}' not found — connection will have no path card");
        }
        fwdPathCard ??= locationCard;

        // Travel action tags: explicit names, else inherited from the template node's
        // connections (clone case), else from the target node's existing connections —
        // so mod travel actions carry the same tags as vanilla travel.
        var fwdTags = ResolveTravelTags(conn.TravelActionTags)
            ?? CopyTravelTags(FirstConnection(templateNode))
            ?? CopyTravelTags(FirstConnection(targetNode));

        var fwd = CreateConnectionEntry(targetCard, fwdPathCard, dir, conn.PathCost, conn.HideConnection, fwdTags);
        if (fwd == null) return false;
        AppendConnection(mapEnvData, fwd);

        if (targetNode == null) return true; // forward-only; already warned above

        // Reverse connection on the existing node: negated direction, the target's OWN
        // PathCard (vanilla convention — each side travels via its own location card),
        // and the target's own travel tags.
        var targetFirstConn   = FirstConnection(targetNode);
        var revPathCard       = targetFirstConn != null ? _pathCardField?.GetValue(targetFirstConn) : null;
        revPathCard         ??= fwdPathCard;
        var revTags           = CopyTravelTags(targetFirstConn) ?? fwdTags;

        var rev = CreateConnectionEntry(envCard, revPathCard, -dir, conn.PathCost, conn.HideConnection, revTags);
        if (rev != null)
        {
            AppendConnection(targetNode, rev);
            // Struct safety: write the mutated boxed copy back over its array slot.
            if (_mapEnvDataType.IsValueType && environments is Array arr && targetIndex >= 0)
                arr.SetValue(targetNode, targetIndex);
            Log.Debug($"WorldMapInjector: added reverse connection {conn.EnvironmentUID}→{node.EnvironmentUID}");
        }

        return true;
    }

    private static object CreateConnectionEntry(object connectsToCard, object pathCard,
        UnityEngine.Vector4 direction, float pathCost, bool hidden, object travelTags)
    {
        var entry = Activator.CreateInstance(_connEnvType);

        _connectsToField.SetValue(entry, CreateEnvId(connectsToCard));
        if (pathCard != null) _pathCardField?.SetValue(entry, pathCard);
        _pathCostField?.SetValue(entry, pathCost);
        _hiddenConnField?.SetValue(entry, hidden);
        if (_travelDirField != null)
        {
            // TravelDirection is Coordinates (int), not Vector4 — convert the unit vector.
            var coord = MakeCoordinates(
                (int)Math.Round(direction.x), (int)Math.Round(direction.y),
                (int)Math.Round(direction.z), (int)Math.Round(direction.w));
            if (coord != null) _travelDirField.SetValue(entry, coord);
        }
        if (travelTags != null) _travelTagsField?.SetValue(entry, travelTags);

        InitNullArrayFields(entry);
        return entry;
    }

    private static void AppendConnection(object mapEnvData, object connEntry)
    {
        var conns = _connEnvsField.GetValue(mapEnvData);
        var newConns = AppendToCollection(conns, _connEnvType, connEntry);
        if (newConns != null) _connEnvsField.SetValue(mapEnvData, newConns);
    }

    /// <summary>
    /// Unit travel direction from the new node toward the target node, derived from
    /// map coordinates (dominant axis wins; vanilla nodes sit on a 10-unit grid).
    /// Zero when the target node is unknown.
    /// </summary>
    private static UnityEngine.Vector4 DeriveDirection(object fromNode, object toNode)
    {
        if (fromNode == null || toNode == null || _coordsField == null)
            return UnityEngine.Vector4.zero;

        // Coords are Coordinates (int), not Vector4 — read through the converter.
        var from = CoordinatesToVector4(_coordsField.GetValue(fromNode));
        var to   = CoordinatesToVector4(_coordsField.GetValue(toNode));
        float dx = to.x - from.x, dz = to.z - from.z;

        if (dx == 0f && dz == 0f) return UnityEngine.Vector4.zero;
        if (Math.Abs(dx) >= Math.Abs(dz))
            return new UnityEngine.Vector4(Math.Sign(dx), 0, 0, 0);
        return new UnityEngine.Vector4(0, 0, Math.Sign(dz), 0);
    }

    // ------------------------------------------------------------- tags ---

    /// <summary>Resolves runtime ActionTag names to a typed array, or null when none resolve.</summary>
    private static object ResolveTravelTags(List<string> tagNames)
    {
        if (tagNames == null || tagNames.Count == 0 || _travelTagsField == null) return null;

        var elemType = CollectionElementType(_travelTagsField.FieldType);
        if (elemType == null) return null;

        var resolved = new List<object>(tagNames.Count);
        foreach (var name in tagNames)
        {
            if (Database.AllScriptableObjectDict.TryGetValue(name, out var so)
                && elemType.IsInstanceOfType(so))
                resolved.Add(so);
            else
                Log.Warn($"WorldMapInjector: TravelActionTag '{name}' not found as a runtime {elemType.Name} — skipping tag");
        }
        if (resolved.Count == 0) return null;

        var arr = Array.CreateInstance(elemType, resolved.Count);
        for (int i = 0; i < resolved.Count; i++) arr.SetValue(resolved[i], i);
        return arr;
    }

    /// <summary>Shallow-copies the TravelActionTags collection from an existing connection
    /// entry (same SO references, fresh container). Null when unavailable or empty.</summary>
    private static object CopyTravelTags(object connEntry)
    {
        if (connEntry == null || _travelTagsField == null) return null;
        if (_travelTagsField.GetValue(connEntry) is not IEnumerable src) return null;

        var elemType = CollectionElementType(_travelTagsField.FieldType);
        if (elemType == null) return null;

        var items = new List<object>();
        foreach (var item in src)
            if (item != null) items.Add(item);
        if (items.Count == 0) return null;

        var arr = Array.CreateInstance(elemType, items.Count);
        for (int i = 0; i < items.Count; i++) arr.SetValue(items[i], i);
        return arr;
    }

    private static Type CollectionElementType(Type collectionType)
        => collectionType.IsArray ? collectionType.GetElementType()
         : collectionType.IsGenericType ? collectionType.GetGenericArguments()[0]
         : null;

    // ------------------------------------------------------- edge records ---

    /// <summary>
    /// Records both directions of a connection in <see cref="Api.WorldMap"/> for
    /// map-consuming mods, at load time (before WorldMapData exists). Source and
    /// destination CT8 location UIDs are resolved from each environment's
    /// DefaultEnvCardDrops (the same CT4→CT8 pairing CardCloneService uses), so no live
    /// WorldMapData is required. Skipped (with a log — no silent caps) when the travel
    /// direction is not an explicit cardinal or a location card cannot be resolved.
    /// </summary>
    private static void RecordEdgeLoadTime(PreparedNode prep, ConnectionDefinition conn)
    {
        var targetCard = GameRegistry.GetByUid(conn.EnvironmentUID) as CardData;
        if (targetCard == null)
        {
            Log.Warn($"WorldMapInjector: connection EnvironmentUID '{conn.EnvironmentUID}' not found — edge not recorded for map consumers");
            return;
        }

        var srcLocUid = prep.LocationCard != null
            ? prep.LocationCard.UniqueID
            : CardCloneService.FindLocationCardFor(prep.EnvCard as CardData)?.UniqueID;
        var dstLocUid = CardCloneService.FindLocationCardFor(targetCard)?.UniqueID;

        var dir = conn.TravelDirection ?? UnityEngine.Vector4.zero;
        var direction = Api.WorldMap.DirectionFromVector(dir.x, dir.z);

        // srcLoc == dstLoc / unresolved UID / non-cardinal direction would corrupt a
        // consumer's adjacency graph. An omitted TravelDirection cannot be derived here
        // (the target node's coords live in WorldMapData, not yet loaded) — supply an
        // explicit TravelDirection to record the edge for map consumers.
        if (string.IsNullOrEmpty(srcLocUid) || string.IsNullOrEmpty(dstLocUid)
            || srcLocUid == dstLocUid || direction < 0)
        {
            Log.Debug($"WorldMapInjector: edge {prep.Def.EnvironmentUID}→{conn.EnvironmentUID} not recorded for map consumers " +
                      $"(srcLoc={(srcLocUid ?? "?")}, dstLoc={(dstLocUid ?? "?")}, dir={direction})");
            return;
        }

        Api.WorldMap.Record(new Api.WorldMap.MapEdge
        {
            SourceEnvUid = prep.Def.EnvironmentUID,
            SourceLocationUid = srcLocUid,
            Direction = direction,
            DestinationEnvUid = conn.EnvironmentUID,
            DestinationLocationUid = dstLocUid,
            PathCost = conn.PathCost,
            HiddenOnInGameMap = conn.HideConnection,
            SourceMod = prep.Def.SourceMod,
        });
        Api.WorldMap.Record(new Api.WorldMap.MapEdge
        {
            SourceEnvUid = conn.EnvironmentUID,
            SourceLocationUid = dstLocUid,
            Direction = Api.WorldMap.DirectionFromVector(-dir.x, -dir.z),
            DestinationEnvUid = prep.Def.EnvironmentUID,
            DestinationLocationUid = srcLocUid,
            PathCost = conn.PathCost,
            HiddenOnInGameMap = conn.HideConnection,
            SourceMod = prep.Def.SourceMod,
        });
    }

    // ----------------------------------------------------------- helpers ---

    /// <summary>Finds the MapEnvData entry whose EnvID.MainEnvCard is <paramref name="envCard"/>.</summary>
    private static object FindNodeByEnvCard(object environments, object envCard)
        => FindNodeWithIndex(environments, envCard).entry;

    private static (object entry, int index) FindNodeWithIndex(object environments, object envCard)
    {
        var envArr = environments as IList ?? CollectionToList(environments);
        if (envArr == null) return (null, -1);

        for (int i = 0; i < envArr.Count; i++)
        {
            var entry = envArr[i];
            if (entry == null) continue;
            if (ReferenceEquals(GetMainEnvCard(entry), envCard)) return (entry, i);
        }
        return (null, -1);
    }

    private static object FirstConnection(object mapEnvData)
    {
        if (mapEnvData == null) return null;
        if (_connEnvsField.GetValue(mapEnvData) is not IEnumerable conns) return null;
        foreach (var c in conns)
            if (c != null) return c;
        return null;
    }

    private static object GetMainEnvCard(object mapEnvData)
    {
        if (_envField == null || _mainEnvCardField == null) return null;
        var envId = _envField.GetValue(mapEnvData);
        return envId == null ? null : _mainEnvCardField.GetValue(envId);
    }

    /// <summary>Initializes every null array-typed field on an object WE created to an
    /// empty array. Never call this on vanilla objects (CLAUDE.md §Vanilla Data Protection).</summary>
    private static void InitNullArrayFields(object obj)
    {
        if (obj == null) return;
        foreach (var f in obj.GetType().GetFields(BF))
        {
            if (!f.FieldType.IsArray) continue;
            try
            {
                if (f.GetValue(obj) == null)
                    f.SetValue(obj, Array.CreateInstance(f.FieldType.GetElementType(), 0));
            }
            catch { /* read-only or incompatible — leave as-is */ }
        }
    }

    // --------------------------------------------------------- collection ---

    /// <summary>
    /// Creates an empty array or List instance matching the declared type of <paramref name="field"/>.
    /// Handles both array fields (returns empty array) and generic List fields (returns empty List&lt;T&gt;).
    /// </summary>
    private static object EmptyCollection(FieldInfo field)
    {
        var ft = field.FieldType;
        if (ft.IsArray)
            return Array.CreateInstance(ft.GetElementType(), 0);
        if (ft.IsGenericType && typeof(IList).IsAssignableFrom(ft))
            return Activator.CreateInstance(ft);
        return null;
    }

    /// <summary>
    /// Appends <paramref name="item"/> to a collection that may be an array or List.
    /// Returns a new array (or the same list) with the item added.
    /// </summary>
    private static object AppendToCollection(object collection, Type elementType, object item)
    {
        if (collection is IList list && !(collection is Array))
        {
            try { list.Add(item); return list; }
            catch (Exception ex)
            {
                Log.Debug($"WorldMapInjector: IList.Add failed ({ex.GetType().Name}) — trying array resize");
            }
        }

        // Treat as array (most common for Unity SO fields).
        Array oldArr = collection as Array ?? Array.CreateInstance(elementType, 0);
        var newArr = Array.CreateInstance(elementType, oldArr.Length + 1);
        Array.Copy(oldArr, newArr, oldArr.Length);
        newArr.SetValue(item, oldArr.Length);
        return newArr;
    }

    private static IList CollectionToList(object collection)
    {
        if (collection is IList l) return l;
        if (collection is Array arr)
        {
            var list = new System.Collections.ArrayList();
            foreach (var item in arr) list.Add(item);
            return list;
        }
        return null;
    }
}
