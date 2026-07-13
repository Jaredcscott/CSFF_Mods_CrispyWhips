using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Data;
using CSFFModFramework.Loading;
using CSFFModFramework.Util;
using static CSFFModFramework.Loading.WorldMapLoader;

namespace CSFFModFramework.Injection;

/// <summary>
/// Evaluates declarative <c>ConnectionGates</c> from <c>WorldMap/MapNodes.json</c> and
/// C#-registered gates at run start (and whenever an improvement is completed) to show or
/// hide world-map connections and their travel DAs.
///
/// <para>JSON example:
/// <code>
/// "ConnectionGates": [{
///   "ConnectionUID": "cmcEnvVillagePath",
///   "GateConditions": [
///     { "Type": "ImprovementBuilt", "EnvUID": "river_clearing_ct4_uid", "UID": "cmcimpriverbridge" },
///     { "Type": "PerkEquipped", "UID": "traitsperkvillagepath" }
///   ],
///   "GateLogic": "ANY",
///   "HideTravelDA": true,
///   "RestoreDAOnUnlock": false,
///   "NeighborCt8UID": "river_clearing_ct8_uid"
/// }]
/// </code>
/// </para>
///
/// <para>A gate is considered <em>unlocked</em> when its logic condition is satisfied
/// (ANY = at least one; ALL = all). When unlocked:
/// <list type="bullet">
///   <item>The target env node and all touching connections are shown on the map.</item>
///   <item>If <c>RestoreDAOnUnlock</c> is true, previously-stripped travel DAs are re-added.</item>
/// </list>
/// When locked:
/// <list type="bullet">
///   <item>The target env node and all touching connections are hidden.</item>
///   <item>If <c>HideTravelDA</c> is true, the travel DA pointing toward the target is stripped
///     from <c>NeighborCt8UID</c> (or from all injected edges when unspecified).</item>
/// </list>
/// </para>
///
/// <para>C# callers may also register a gate programmatically via
/// <see cref="RegisterGate(string, Func{bool}, bool, string)"/>.</para>
///
/// <para>For targeted single-edge gating (e.g. hide only the WF↔Tin connection without
/// affecting other Tin Cave connections) use <see cref="ToggleEdgeBidirectional"/> +
/// <see cref="StripTravelDaPublic"/> / <see cref="RestoreTravelDaPublic"/>.</para>
/// </summary>
internal static class ConnectionGateService
{
    private const BindingFlags BF =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private sealed class GateEntry
    {
        public string ConnectionUID;
        public Func<bool> Condition;
        public bool HideTravelDA;
        public string NeighborCt8UID;   // null = auto-detect from InjectedEdges (bidirectional)
        public bool RestoreDAOnUnlock;   // true = re-add DA when gate opens; false = permanent strip
        public bool? LastState;          // null = never evaluated
        public string Granularity = "Env";  // "Env" (default) or "Edge" — see RegisterEdgeGate
        public string OwnerEnvUID;          // Edge granularity only: the near side of the edge
    }

    private sealed class StrippedDa
    {
        public string LocUid;
        public int Dir;
        public object Da;
    }

    private static readonly List<GateEntry> _gates = new();

    // Cache of stripped DAs, keyed by connectionUID / cacheKey.
    // Each entry lists (locUid, dir, da) tuples that can be re-added on unlock.
    private static readonly Dictionary<string, List<StrippedDa>> _strippedDas = new();

    private static bool _initialized;

    // Per-type field caches for DA direction matching (avoids per-call reflection).
    private static readonly Dictionary<Type, FieldInfo> _hasExpDirCache = new();
    private static readonly Dictionary<Type, FieldInfo> _expDirCache = new();
    private static readonly Dictionary<Type, FieldInfo> _daListCache = new();

    // ── public API ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers a C# gate for a connection UID.
    /// </summary>
    /// <param name="connectionUID">CT4 env UID of the node to gate.</param>
    /// <param name="condition">Returns true when the connection should be shown.</param>
    /// <param name="restoreOnUnlock">If true, stripped travel DAs are re-added when the gate opens.</param>
    /// <param name="neighborCt8UID">CT8 card to strip the reverse DA from. Null = all injected edges.</param>
    internal static void RegisterGate(string connectionUID, Func<bool> condition,
        bool restoreOnUnlock = false, string neighborCt8UID = null)
    {
        if (string.IsNullOrEmpty(connectionUID) || condition == null) return;
        _gates.Add(new GateEntry
        {
            ConnectionUID  = connectionUID,
            Condition      = condition,
            HideTravelDA   = true,
            NeighborCt8UID = neighborCt8UID,
            RestoreDAOnUnlock = restoreOnUnlock,
            Granularity    = "Env",
        });
        if (_initialized) EvaluateAll();
    }

    /// <summary>
    /// Registers a gate scoped to a single connection rather than the whole target env —
    /// use when the target env has other connections that must stay unaffected (e.g. ACT's
    /// Waterfall↔Tin edge, where Tin Cave also has separate Copper/Iron connections that a
    /// whole-env <see cref="RegisterGate"/> would incorrectly hide too).
    /// </summary>
    /// <param name="ownerEnvUID">The near side of the edge (the node registering this gate).</param>
    /// <param name="neighborEnvUID">The far side of the edge — the connection this gate controls.</param>
    /// <param name="condition">Returns true when the connection should be shown.</param>
    /// <param name="restoreOnUnlock">If true, stripped travel DAs are re-added when the gate opens.</param>
    /// <param name="ownCt8UID">Optional override for the CT8 that carries the DA on the owner side.
    /// Null = auto-detected from <see cref="WorldMap.InjectedEdges"/>.</param>
    internal static void RegisterEdgeGate(string ownerEnvUID, string neighborEnvUID, Func<bool> condition,
        bool restoreOnUnlock = false, string ownCt8UID = null)
    {
        if (string.IsNullOrEmpty(ownerEnvUID) || string.IsNullOrEmpty(neighborEnvUID) || condition == null) return;
        _gates.Add(new GateEntry
        {
            ConnectionUID  = neighborEnvUID,
            OwnerEnvUID    = ownerEnvUID,
            Condition      = condition,
            HideTravelDA   = true,
            NeighborCt8UID = ownCt8UID,
            RestoreDAOnUnlock = restoreOnUnlock,
            Granularity    = "Edge",
        });
        if (_initialized) EvaluateAll();
    }

    /// <summary>
    /// Parses all <c>ConnectionGates</c> from prepared nodes and evaluates them.
    /// Called from <see cref="WorldMapInjector.OnGameManagerInitialized"/> after
    /// <c>PreCreateCloneEnvSaveData</c> and <c>InjectIntoWorldMap</c>.
    /// </summary>
    internal static void Initialize(IEnumerable<MapNodeDefinition> defs)
    {
        // OnGMInitialized fires on every run start — the JSON gates are already registered
        // after the first call; re-adding them would duplicate every GateEntry. Re-evaluate
        // instead: the newly loaded save's marker/improvement state can differ from the
        // previous run's.
        if (_initialized) { EvaluateAll(); return; }

        foreach (var def in defs)
        {
            if (def?.ConnectionGates == null) continue;
            foreach (var gate in def.ConnectionGates)
            {
                if (string.IsNullOrEmpty(gate.ConnectionUID)) continue;
                bool isEdge = "Edge".Equals(gate.Granularity, StringComparison.OrdinalIgnoreCase);
                _gates.Add(new GateEntry
                {
                    ConnectionUID     = gate.ConnectionUID,
                    OwnerEnvUID       = isEdge ? def.EnvironmentUID : null,
                    Granularity       = isEdge ? "Edge" : "Env",
                    Condition         = BuildCondition(gate),
                    HideTravelDA      = gate.HideTravelDA,
                    NeighborCt8UID    = gate.NeighborCt8UID,
                    RestoreDAOnUnlock = gate.RestoreDAOnUnlock
                });
            }
        }

        _initialized = true;
        if (_gates.Count == 0) return;
        EvaluateAll();
    }

    /// <summary>Re-evaluates every registered gate. Called on improvement completion.</summary>
    internal static void EvaluateAll()
    {
        if (!_initialized || _gates.Count == 0) return;

        var worldMapSo = WorldMapInjector.GetWorldMapSo();
        if (worldMapSo == null)
        {
            Log.Warn("ConnectionGateService.EvaluateAll: WorldMapData not available — gates deferred");
            return;
        }

        int unlockedCount = 0, lockedCount = 0;
        foreach (var gate in _gates)
        {
            try
            {
                bool unlocked = gate.Condition?.Invoke() ?? true;
                if (gate.LastState.HasValue && gate.LastState.Value == unlocked) continue;
                gate.LastState = unlocked;
                ApplyGate(gate, unlocked, worldMapSo);
                if (unlocked) unlockedCount++; else lockedCount++;
            }
            catch (Exception ex)
            {
                Log.Warn($"ConnectionGateService: error evaluating gate '{gate.ConnectionUID}': {ex.GetType().Name}: {ex.Message}");
            }
        }

        if (unlockedCount > 0 || lockedCount > 0)
            Log.Info($"ConnectionGateService: evaluated {_gates.Count} gate(s) — {unlockedCount} unlocked, {lockedCount} locked");
    }

    /// <summary>
    /// Hides or shows only the specific bidirectional edge between two environments.
    /// Unlike <c>ToggleConnection</c>, this does NOT hide the target node icon or other
    /// connections — it affects only the A↔B connection in WorldMapData.
    /// </summary>
    internal static void ToggleEdgeBidirectional(string envA, string envB, bool hidden, object worldMapSo)
    {
        if (string.IsNullOrEmpty(envA) || string.IsNullOrEmpty(envB) || worldMapSo == null) return;
        try
        {
            if (FindField(worldMapSo.GetType(), "Environments")?.GetValue(worldMapSo) is not IList envList)
                return;

            for (int i = 0; i < envList.Count; i++)
            {
                var node = envList[i];
                if (node == null) continue;
                var nodeType = node.GetType();
                string nodeUID = GetNodeEnvUID(node, nodeType);
                if (!envA.Equals(nodeUID, StringComparison.Ordinal)
                 && !envB.Equals(nodeUID, StringComparison.Ordinal)) continue;

                string targetUID = envA.Equals(nodeUID, StringComparison.Ordinal) ? envB : envA;
                bool nodeMutated = false;

                var connsField = FindField(nodeType, "ConnectedEnvironments");
                if (connsField?.GetValue(node) is IList conns)
                {
                    for (int c = 0; c < conns.Count; c++)
                    {
                        var conn = conns[c];
                        if (conn == null) continue;
                        if (!ConnectionPointsTo(conn, conn.GetType(), targetUID)) continue;
                        var ct = conn.GetType();
                        SetBoolField(conn, ct, "HiddenOnInGameMap", hidden);
                        SetBoolField(conn, ct, "HideFromInGameMap", hidden);
                        if (ct.IsValueType) conns[c] = conn;
                        nodeMutated = true;
                    }
                }

                if (nodeMutated && nodeType.IsValueType) envList[i] = node;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"ConnectionGateService.ToggleEdge '{envA}'↔'{envB}': {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Public helper for mods that need to strip a single travel DA and cache it for later
    /// restore (e.g. ACT's WF↔Tin edge which cannot use RegisterGate because gating the
    /// entire TinCave env would also affect Copper/Iron connections).
    /// </summary>
    /// <param name="ct8LocUid">UniqueID of the CT8 location card.</param>
    /// <param name="direction">Cardinal direction (0=N, 1=S, 2=E, 3=W).</param>
    /// <param name="cacheKey">Arbitrary key used for the restore cache.</param>
    /// <returns>True when a DA was found and stripped.</returns>
    internal static bool StripTravelDaPublic(string ct8LocUid, int direction, string cacheKey)
    {
        if (!_strippedDas.TryGetValue(cacheKey, out var cache))
            _strippedDas[cacheKey] = cache = new List<StrippedDa>();
        int before = cache.Count;
        StripOneDa(ct8LocUid, direction, cache);
        return cache.Count > before;
    }

    /// <summary>
    /// Restores a travel DA that was previously stripped via <see cref="StripTravelDaPublic"/>.
    /// </summary>
    /// <returns>True when the DA was found in the cache and re-added.</returns>
    internal static bool RestoreTravelDaPublic(string ct8LocUid, int direction, string cacheKey)
    {
        if (!_strippedDas.TryGetValue(cacheKey, out var cache)) return false;
        var entry = cache.FirstOrDefault(e =>
            e.LocUid == ct8LocUid && e.Dir == direction);
        if (entry?.Da == null) return false;

        var ct8 = LookupCt8(ct8LocUid);
        if (ct8 == null) return false;
        var daList = GetDaList(ct8);
        if (daList == null) return false;
        if (FindTravelDaIndex(daList, direction) >= 0) return false;  // already present

        daList.Add(entry.Da);
        cache.Remove(entry);
        Log.Debug($"ConnectionGateService: restored travel DA '{ct8LocUid}' dir={direction} (cache '{cacheKey}')");
        ResyncInGameDaCacheIfPresent(ct8LocUid);
        return true;
    }

    // ── private: condition building ─────────────────────────────────────────────

    private static Func<bool> BuildCondition(ConnectionGateDefinition gate)
    {
        if (gate.GateConditions == null || gate.GateConditions.Count == 0)
            return () => true;

        var conds = gate.GateConditions.ToList();
        bool anyLogic = !"ALL".Equals(gate.GateLogic, StringComparison.OrdinalIgnoreCase);

        return () =>
        {
            if (anyLogic)
            {
                foreach (var c in conds) if (EvalSingleGateCond(c)) return true;
                return false;
            }
            else
            {
                foreach (var c in conds) if (!EvalSingleGateCond(c)) return false;
                return true;
            }
        };
    }

    /// <summary>
    /// Evaluates one gate condition. Shared with <see cref="SealableGateService"/>'s
    /// <c>SealTrigger</c> evaluation — both use the same <see cref="GateConditionDefinition"/>
    /// shape.
    /// </summary>
    internal static bool EvalSingleGateCond(GateConditionDefinition cond)
    {
        if (cond == null) return false;
        switch (cond.Type?.Trim().ToLowerInvariant())
        {
            case "perkequipped":
                return CardUtil.IsPerkEquipped(cond.UID);
            case "improvementbuilt":
                return CardUtil.IsImprovementBuilt(cond.EnvUID, cond.UID);
            case "statthreshold":
                return EvalStatThreshold(cond);
            default:
                Log.Warn($"ConnectionGateService: unknown gate condition type '{cond.Type}'");
                return false;
        }
    }

    /// <summary>
    /// Compares a <c>GameStat</c>'s current value (<c>GameManager.StatsDict[stat].SimpleCurrentValue</c>,
    /// the same read path <c>StatValueTrigger.IsInRange</c> uses for vanilla travel DA gating —
    /// CLAUDE.md §No first-class travel-cost gating) against <see cref="GateConditionDefinition.Value"/>.
    /// </summary>
    private static bool EvalStatThreshold(GateConditionDefinition cond)
    {
        if (string.IsNullOrEmpty(cond.UID)) return false;
        try
        {
            var gm = CardUtil.GetGameManagerInstance();
            if (gm == null) return false;

            var stat = GameRegistry.GetByUid(cond.UID);
            if (stat == null || stat.GetType().Name != "GameStat")
            {
                Log.Warn($"ConnectionGateService: StatThreshold UID '{cond.UID}' does not resolve to a GameStat");
                return false;
            }

            var statsDictField = CardUtil.GetCachedField(gm.GetType(), "StatsDict");
            if (statsDictField?.GetValue(gm) is not IDictionary statsDict) return false;
            if (!statsDict.Contains(stat)) return false;
            var statValueObj = statsDict[stat];
            if (statValueObj == null) return false;

            var currentValueObj = CardUtil.GetMemberValue(statValueObj, "SimpleCurrentValue");
            if (currentValueObj == null) return false;
            float value = Convert.ToSingle(currentValueObj);

            return cond.Comparison?.Trim().ToLowerInvariant() switch
            {
                "lessthan"     => value < cond.Value,
                "lessorequal"  => value <= cond.Value,
                "greaterthan"  => value > cond.Value,
                "equal"        => Math.Abs(value - cond.Value) < 0.0001f,
                _              => value >= cond.Value,   // default: GreaterOrEqual
            };
        }
        catch (Exception ex)
        {
            Log.Warn($"ConnectionGateService: StatThreshold '{cond.UID}' evaluation failed: {ex.Message}");
            return false;
        }
    }

    // ── private: gate application ───────────────────────────────────────────────

    private static void ApplyGate(GateEntry gate, bool unlocked, object worldMapSo)
    {
        bool isEdge = "Edge".Equals(gate.Granularity, StringComparison.OrdinalIgnoreCase);
        Log.Debug($"ConnectionGateService: gate '{gate.ConnectionUID}' ({(isEdge ? "Edge" : "Env")}) → {(unlocked ? "UNLOCKED" : "LOCKED")}");

        if (isEdge)
            ToggleEdgeBidirectional(gate.OwnerEnvUID, gate.ConnectionUID, !unlocked, worldMapSo);
        else
            ToggleConnection(gate.ConnectionUID, !unlocked, worldMapSo);

        if (!unlocked && gate.HideTravelDA)
            StripTravelDas(gate);
        else if (unlocked && gate.HideTravelDA && gate.RestoreDAOnUnlock)
            RestoreTravelDas(gate);
    }

    // ── private: world-map connection toggle ────────────────────────────────────

    // Hides/shows the target env node (HideFromInGameMap) and all connections touching it
    // (both outbound from the node and inbound from other nodes).
    private static void ToggleConnection(string targetUID, bool hidden, object worldMapSo)
    {
        if (string.IsNullOrEmpty(targetUID) || worldMapSo == null) return;
        try
        {
            if (FindField(worldMapSo.GetType(), "Environments")?.GetValue(worldMapSo) is not IList envList)
                return;

            for (int i = 0; i < envList.Count; i++)
            {
                var node = envList[i];
                if (node == null) continue;
                var nodeType = node.GetType();
                bool nodeMutated = false;

                if (targetUID.Equals(GetNodeEnvUID(node, nodeType), StringComparison.Ordinal))
                {
                    // This IS the target node: hide the icon and all its outbound connections.
                    SetBoolField(node, nodeType, "HideFromInGameMap", hidden);
                    var connsField = FindField(nodeType, "ConnectedEnvironments");
                    if (connsField?.GetValue(node) is IList outConns)
                    {
                        for (int c = 0; c < outConns.Count; c++)
                        {
                            var conn = outConns[c];
                            if (conn == null) continue;
                            var ct = conn.GetType();
                            SetBoolField(conn, ct, "HiddenOnInGameMap", hidden);
                            SetBoolField(conn, ct, "HideFromInGameMap", hidden);
                            if (ct.IsValueType) outConns[c] = conn;
                        }
                    }
                    nodeMutated = true;
                }
                else
                {
                    // Check for inbound connections on this node that point TO the target.
                    var connsField = FindField(nodeType, "ConnectedEnvironments");
                    if (connsField?.GetValue(node) is IList inConns)
                    {
                        for (int c = 0; c < inConns.Count; c++)
                        {
                            var conn = inConns[c];
                            if (conn == null) continue;
                            if (!ConnectionPointsTo(conn, conn.GetType(), targetUID)) continue;
                            var ct = conn.GetType();
                            SetBoolField(conn, ct, "HiddenOnInGameMap", hidden);
                            SetBoolField(conn, ct, "HideFromInGameMap", hidden);
                            if (ct.IsValueType) inConns[c] = conn;
                            nodeMutated = true;
                        }
                    }
                }

                if (nodeMutated && nodeType.IsValueType) envList[i] = node;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"ConnectionGateService.ToggleConnection '{targetUID}': {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string GetNodeEnvUID(object node, Type nodeType)
    {
        var envId = FindField(nodeType, "Environment")?.GetValue(node);
        return GetMainEnvCardUID(envId);
    }

    private static string GetMainEnvCardUID(object envId)
    {
        if (envId == null) return null;
        var card = FindField(envId.GetType(), "MainEnvCard")?.GetValue(envId)
                ?? envId.GetType().GetProperty("MainEnvCard", BF)?.GetValue(envId);
        return card != null ? GetMember(card, "UniqueID") as string : null;
    }

    private static bool ConnectionPointsTo(object conn, Type connType, string targetUID)
    {
        // Primary check: ConnectsTo.MainEnvCard.UniqueID (CT4 env UID)
        var connectsTo = FindField(connType, "ConnectsTo")?.GetValue(conn)
                      ?? connType.GetProperty("ConnectsTo", BF)?.GetValue(conn);
        if (connectsTo != null)
        {
            var uid = GetMainEnvCardUID(connectsTo);
            if (targetUID.Equals(uid, StringComparison.Ordinal)) return true;
        }
        // Fallback: PathCard.UniqueID (CT8 location card UID)
        var pathCard = FindField(connType, "PathCard")?.GetValue(conn);
        if (pathCard != null)
        {
            var uid = GetMember(pathCard, "UniqueID") as string;
            if (targetUID.Equals(uid, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    // ── private: travel DA strip / restore ──────────────────────────────────────

    // Edge-granularity gates cache under "OwnerEnvUID=>ConnectionUID" so they never collide
    // with an Env-granularity gate that happens to target the same ConnectionUID.
    private static string CacheKeyFor(GateEntry gate) =>
        "Edge".Equals(gate.Granularity, StringComparison.OrdinalIgnoreCase)
            ? $"{gate.OwnerEnvUID}=>{gate.ConnectionUID}"
            : gate.ConnectionUID;

    private static void StripTravelDas(GateEntry gate)
    {
        if (!gate.HideTravelDA) return;

        var cacheKey = CacheKeyFor(gate);
        if (!_strippedDas.TryGetValue(cacheKey, out var cache))
            _strippedDas[cacheKey] = cache = new List<StrippedDa>();

        var edges = WorldMap.InjectedEdges;
        bool isEdge = "Edge".Equals(gate.Granularity, StringComparison.OrdinalIgnoreCase);

        if (isEdge)
        {
            // Edge granularity: strip BOTH directed edges directly between OwnerEnvUID and
            // ConnectionUID (and only those) — never the env-wide "all edges touching
            // ConnectionUID" sweep, which would also strip unrelated connections on a
            // multi-connection env (e.g. ACT's Tin Cave also connects to Copper/Iron — gating
            // Waterfall↔Tin must not touch those). NeighborCt8UID does not apply here: a true
            // edge gate always strips symmetrically on both sides.
            foreach (var edge in edges)
            {
                bool forward = gate.OwnerEnvUID.Equals(edge.SourceEnvUid, StringComparison.Ordinal)
                            && gate.ConnectionUID.Equals(edge.DestinationEnvUid, StringComparison.Ordinal);
                bool reverse = gate.ConnectionUID.Equals(edge.SourceEnvUid, StringComparison.Ordinal)
                            && gate.OwnerEnvUID.Equals(edge.DestinationEnvUid, StringComparison.Ordinal);
                if (!forward && !reverse) continue;
                StripOneDa(edge.SourceLocationUid, edge.Direction, cache);
            }
        }
        else if (!string.IsNullOrEmpty(gate.NeighborCt8UID))
        {
            // Strip only the specific neighbor's DA that points toward ConnectionUID.
            foreach (var edge in edges)
            {
                if (!gate.NeighborCt8UID.Equals(edge.SourceLocationUid, StringComparison.Ordinal)) continue;
                if (!gate.ConnectionUID.Equals(edge.DestinationEnvUid, StringComparison.Ordinal)) continue;
                StripOneDa(edge.SourceLocationUid, edge.Direction, cache);
                break;
            }
        }
        else
        {
            // No explicit neighbor: strip ALL DAs on edges touching ConnectionUID (bidirectional).
            foreach (var edge in edges)
            {
                bool involves =
                    gate.ConnectionUID.Equals(edge.SourceEnvUid, StringComparison.Ordinal) ||
                    gate.ConnectionUID.Equals(edge.DestinationEnvUid, StringComparison.Ordinal);
                if (!involves) continue;
                StripOneDa(edge.SourceLocationUid, edge.Direction, cache);
            }
        }
    }

    private static void StripOneDa(string locUid, int dir, List<StrippedDa> cache)
    {
        // Idempotent: don't strip the same direction twice.
        if (cache.Any(e => e.LocUid == locUid && e.Dir == dir)) return;

        var ct8 = LookupCt8(locUid);
        if (ct8 == null) return;
        var daList = GetDaList(ct8);
        if (daList == null) return;

        int idx = FindTravelDaIndex(daList, dir);
        if (idx < 0) return;   // already stripped or not present

        var stripped = daList[idx];
        daList.RemoveAt(idx);
        cache.Add(new StrippedDa { LocUid = locUid, Dir = dir, Da = stripped });
        Log.Debug($"ConnectionGateService: stripped travel DA from '{locUid}' dir={dir}");
        ResyncInGameDaCacheIfPresent(locUid);
    }

    private static void RestoreTravelDas(GateEntry gate)
    {
        var cacheKey = CacheKeyFor(gate);
        if (!_strippedDas.TryGetValue(cacheKey, out var cache) || cache.Count == 0) return;

        foreach (var entry in cache)
        {
            if (entry.Da == null) continue;
            var ct8 = LookupCt8(entry.LocUid);
            if (ct8 == null) continue;
            var daList = GetDaList(ct8);
            if (daList == null) continue;
            if (FindTravelDaIndex(daList, entry.Dir) >= 0) continue;  // already present

            daList.Add(entry.Da);
            Log.Debug($"ConnectionGateService: restored travel DA to '{entry.LocUid}' dir={entry.Dir}");
            ResyncInGameDaCacheIfPresent(entry.LocUid);
        }
        cache.Clear();   // clear so a subsequent re-lock can re-strip cleanly
    }

    // Per-type field cache for the InGameCardBase cached-array resync (distinct from
    // _daListCache, which caches the CardModel's List<DismantleCardAction> field instead).
    private static readonly Dictionary<Type, FieldInfo> _inGameDaFieldCache = new();

    /// <summary>
    /// Rebuilds the cached <c>InGameCardBase.DismantleActions</c> array from the live
    /// <c>CardModel.DismantleActions</c> List for a CT8 currently on the player's board, if
    /// present. The engine caches this array at <c>SetupCardSource</c> time; strip/restore
    /// above only mutate the underlying List, so a stale cached array silently breaks travel
    /// button clicks (<c>ExplorationPopup.OnActionButtonClicked</c> hits "Length &lt;= _Index"
    /// and no-ops) until the player leaves and re-enters. Originally an ad-hoc per-mod
    /// workaround (ACT/H&amp;F each reimplemented this); fixed once here so every gate/edge
    /// mutation is safe by construction. No-op (and cheap — one board scan) when the card
    /// isn't on the player's current board.
    /// </summary>
    internal static void ResyncInGameDaCacheIfPresent(string locUid)
    {
        if (string.IsNullOrEmpty(locUid)) return;
        try
        {
            if (GameQuery.IsTransitioning) return;
            foreach (var card in GameQuery.CardsInPlayerEnv())
            {
                if (!locUid.Equals(CardUtil.GetCardUniqueId(card), StringComparison.Ordinal)) continue;
                var t = card.GetType();
                if (!_inGameDaFieldCache.TryGetValue(t, out var daField))
                    _inGameDaFieldCache[t] = daField = FindField(t, "DismantleActions");
                if (daField == null) return;
                if (CardUtil.GetCardData(card) is not CardData model) return;
                if (GetDaList(model) is not IList daList) return;
                var elemType = daField.FieldType.GetElementType();
                if (elemType == null) return;   // not an array field on this game version
                var arr = Array.CreateInstance(elemType, daList.Count);
                for (int i = 0; i < daList.Count; i++) arr.SetValue(daList[i], i);
                daField.SetValue(card, arr);
                Log.Debug($"ConnectionGateService: resynced in-game DA cache for '{locUid}' ({daList.Count} DAs)");
                return;
            }
        }
        catch (Exception ex) { Log.Warn($"ConnectionGateService: ResyncInGameDaCacheIfPresent('{locUid}') failed: {ex.Message}"); }
    }

    private static CardData LookupCt8(string locUid)
    {
        if (string.IsNullOrEmpty(locUid)) return null;
        return UniqueIDScriptable.GetFromID<CardData>(locUid);
    }

    /// <summary>Gets the CardModel's <c>List&lt;DismantleCardAction&gt;</c> field (not the
    /// engine's separately-cached array — see <see cref="ResyncInGameDaCacheIfPresent"/>).
    /// Shared with <see cref="SealableGateService"/>'s CardPresence-model DA control.</summary>
    internal static IList GetDaList(CardData ct8)
    {
        var t = ct8.GetType();
        if (!_daListCache.TryGetValue(t, out var fi))
            _daListCache[t] = fi = FindField(t, "DismantleActions");
        return fi?.GetValue(ct8) as IList;
    }

    private static int FindTravelDaIndex(IList daList, int dir)
    {
        for (int i = 0; i < daList.Count; i++)
        {
            var da = daList[i];
            if (da == null) continue;
            var t = da.GetType();

            if (!_hasExpDirCache.TryGetValue(t, out var hf))
                _hasExpDirCache[t] = hf = FindField(t, "HasExplorationDirection");
            if (hf?.GetValue(da) is not true) continue;

            if (!_expDirCache.TryGetValue(t, out var ef))
                _expDirCache[t] = ef = FindField(t, "ExplorationDirection");
            var d = ef?.GetValue(da);
            if (d != null && Convert.ToInt32(d) == dir) return i;
        }
        return -1;
    }

    // ── private: reflection helpers ──────────────────────────────────────────────

    private static FieldInfo FindField(Type type, string name)
    {
        for (var t = type; t != null; t = t.BaseType)
        {
            var f = t.GetField(name, BF);
            if (f != null) return f;
        }
        return null;
    }

    private static object GetMember(object target, string name)
    {
        if (target == null) return null;
        for (var t = target.GetType(); t != null; t = t.BaseType)
        {
            var fi = t.GetField(name, BF);
            if (fi != null) return fi.GetValue(target);
            var p = t.GetProperty(name, BF);
            if (p != null && p.CanRead) return p.GetValue(target, null);
            var bf = t.GetField($"<{name}>k__BackingField", BF);
            if (bf != null) return bf.GetValue(target);
        }
        return null;
    }

    private static void SetBoolField(object target, Type type, string name, bool value)
    {
        var fi = FindField(type, name);
        if (fi != null) { fi.SetValue(target, value); return; }
        var pi = type.GetProperty(name, BF);
        if (pi?.CanWrite == true) pi.SetValue(target, value);
    }
}
