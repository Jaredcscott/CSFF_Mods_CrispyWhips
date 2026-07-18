using System.Collections;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Data;
using CSFFModFramework.Loading;
using CSFFModFramework.Portal;
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
    private static FullMapDefinition _fullMapDef;           // set by PrepareAll; drives replacement mode
    private static object _cachedGlobalTravelDaTemplate;    // fallback template for CT8s with no DAs
    private static Type _gmType;                            // GameManager from Assembly-CSharp (cached; avoids ModCore shadowing)

    // Clone CT4 uid → backup CT8 UID list for CheckAndSpawnMissingLocationCards: run-start safety
    // net ensuring the CT8 appears on the board when loading a save inside a clone env.
    private static readonly Dictionary<string, List<string>> _cloneEnvSpawnList = new(StringComparer.OrdinalIgnoreCase);

    // Mid-game env watch: tracks the last-seen env UID and fires CT8 recovery when entering a
    // clone env mid-game (e.g. via portal) and the CT8 is missing from the board.
    private static TickEvents.IntervalHandle _envWatchHandle;
    private static string _lastWatchedEnv;

    /// <summary>
    /// True if <paramref name="envUid"/> is a MapNodes.json clone-environment node (a CT4/CT8 pair
    /// cloned from a vanilla template). Clone nodes reach their destination by world-map travel and
    /// carry their own injected travel DAs plus a map exit back to the vanilla world, so the
    /// portal-hub "Return to Portal" exit card (<c>csffmfw_hub_exit</c>) must NOT be injected into
    /// them — it would clutter the board (e.g. ACT's mining caves) and duplicate the map exit.
    /// Consumed by <see cref="Portal.PortalService.InjectExitCardsIntoModHubs"/>. Available from
    /// load time (<see cref="PrepareAll"/>) onward; clone nodes always register a CT8 location UID.
    /// </summary>
    internal static bool IsCloneEnvNode(string envUid)
        => !string.IsNullOrEmpty(envUid) && _cloneEnvSpawnList.ContainsKey(envUid);

    // ---------------------------------------------------------- stage 1 ---

    /// <summary>
    /// Stage 1 (data-load time): clone env/location pairs, register them in AllData,
    /// record travel edges in <see cref="Api.WorldMap"/>, and arm the deferred
    /// WorldMapData injection at run start. Must run after <c>Database.InitFromGame()</c>.
    /// </summary>
    /// <param name="additiveNodes">Nodes from MapNodes.json across all mods (additive mode).</param>
    /// <param name="fullMap">FullMap replacement definition from the one mod that ships
    /// FullMap.json, or null for additive-only mode.</param>
    public static void PrepareAll(List<MapNodeDefinition> additiveNodes, FullMapDefinition fullMap = null)
    {
        _prepared.Clear();
        _cloneEnvSpawnList.Clear();
        _injectedIntoWorldMap = false;
        _worldMapCached = null;
        _fullMapDef = fullMap;
        _cachedGlobalTravelDaTemplate = null;
        Api.WorldMap.Clear();

        // FullMap nodes first (they become the base graph), then additive nodes.
        // Processing FullMap nodes first means their entries exist in _prepared when
        // additive nodes try to auto-resolve reverse connections back to them.
        var allNodes = new List<MapNodeDefinition>(
            (fullMap?.Nodes?.Count ?? 0) + (additiveNodes?.Count ?? 0));
        if (fullMap?.Nodes != null) allNodes.AddRange(fullMap.Nodes);
        if (additiveNodes != null)  allNodes.AddRange(additiveNodes);

        // In additive mode with no nodes there is nothing to do.
        if (allNodes.Count == 0 && fullMap == null) return;

        if (!TryResolveTypes())
        {
            Log.Warn("WorldMapInjector: could not resolve WorldMapData types — map injection skipped. " +
                     "This is expected if the game version changed WorldMapData's type name.");
            return;
        }

        // P3 — grid-cell uniqueness across all mods. Runs BEFORE any clone work so a mod with a
        // colliding (x,z) is rejected atomically and skipped below (its items/perks still load).
        CoordRegistry.RegisterAll(allNodes);

        foreach (var node in allNodes)
        {
            if (CoordRegistry.IsRejected(node.SourceMod))
            {
                Log.Debug($"WorldMapInjector: skipping node '{node.EnvironmentUID}' — mod '{node.SourceMod}' rejected by CoordRegistry (grid-cell collision)");
                continue;
            }
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

        // VanillaExits: record the FORWARD edge only (mod node → vanilla env). No reverse edge —
        // a mod map space is reachable only via its portal, never by routing in from vanilla (P2),
        // so map consumers must not see a vanilla → mod adjacency.
        foreach (var prep in _prepared)
        {
            if (prep.Def.VanillaExits == null) continue;
            foreach (var exit in prep.Def.VanillaExits)
            {
                try { RecordVanillaExitEdge(prep, exit); }
                catch (Exception ex)
                {
                    Log.Debug($"WorldMapInjector: vanilla-exit edge record failed for {prep.Def.EnvironmentUID}→{exit.TargetVanillaEnvUID}: {ex.GetType().Name}");
                }
            }
        }

        // Inject travel DAs onto CT8 CardData SOs at load time (Phase 5i), BEFORE any
        // InGameCardBase is created from them and before any game code caches the
        // DismantleActions array reference. Doing this at OnGMInitialized (run start)
        // was too late: the game may have already cached the old array, causing the East
        // DA to disappear after the player left River Clearing and returned — the
        // InGameCardBase was recreated from a pre-injection-cached source, not the SO.
        // WarpResolver has already run at this point (Phase 5), so DefaultEnvCardDrops
        // on target CT4s is resolved and CardCloneService.FindLocationCardFor works.
        if (_prepared.Count > 0)
        {
            // Strip inherited travel DAs from ALL clone CT8s BEFORE any injection.
            // Stripping inside the per-node injection loop broke every connection whose
            // TARGET node came later in the prepared list: the target's still-present
            // template DA blocked the reverse DA (same-direction guard), and even when
            // injection succeeded the target's own strip pass destroyed it afterwards
            // (e.g. High Grove never received North → Mossy Clearing).
            //
            // TryResolveActionTypes MUST run first: StripTravelDAsFromCard silently no-ops
            // while the DA reflection fields are null, and on a fresh process nothing has
            // resolved them yet at this point (the 2.16.1 regression — every clone CT8 kept
            // its template's vanilla travel buttons, e.g. Village Path East → River Clearing).
            if (!TryResolveActionTypes())
            {
                Log.Warn("WorldMapInjector: could not resolve DismantleAction types — inherited travel DAs NOT stripped; clone nodes will keep their template's vanilla travel buttons");
            }
            else
            {
                int strippedTotal = 0, strippedCards = 0;
                foreach (var prep in _prepared)
                {
                    if (!string.IsNullOrEmpty(prep.Def.CloneOfEnvironmentUID) && prep.LocationCard != null)
                    {
                        int stripped = StripTravelDAsFromCard(prep.LocationCard);
                        if (stripped > 0) { strippedTotal += stripped; strippedCards++; }
                    }
                }
                if (strippedTotal > 0)
                    Log.Info($"WorldMapInjector: stripped {strippedTotal} inherited travel DA(s) from {strippedCards} clone CT8 location card(s)");
            }

            int daCount = 0;
            foreach (var prep in _prepared)
            {
                try { daCount += InjectTravelActionsForPrepared(prep); }
                catch (Exception ex)
                {
                    Log.Error($"WorldMapInjector: travel DA load-time injection failed for '{prep.Def.EnvironmentUID}': {Log.ExceptionText(ex)}");
                }
            }
            if (daCount > 0)
                Log.Info($"WorldMapInjector: {daCount} travel DA(s) injected on CT8 location card(s) at load time");

            // Full-replacement: strip orphaned travel DAs now that correct ones are in place.
            if (_fullMapDef?.StripOrphanedTravelDAs == true)
                StripOrphanedTravelDAs();
        }

        string modeLabel = fullMap != null ? "full replacement" : "additive";
        Log.Info($"WorldMapInjector: prepared {_prepared.Count} node(s) [{modeLabel}], {Api.WorldMap.InjectedEdges.Count} travel edge(s) recorded; " +
                 "WorldMapData node injection deferred to run start (WorldMapData is not loaded at data-load time)");

        TrySubscribeGmInitialized();
    }

    /// <summary>
    /// Re-resolve mod-card <c>*WarpData</c> references that point at a clone node's env/location
    /// UID. Clone CardData (e.g. <c>cmcLocVillage</c>) is created and registered by
    /// <see cref="PrepareAll"/> — LoadOrchestrator phase 5i — which runs AFTER
    /// <see cref="Data.WarpResolver.ResolveAll"/> (phase 5). So any mod JSON that references a
    /// clone UID resolved to <c>null</c> on the main pass. The confirmed casualty is a construction
    /// blueprint gate: <c>CardsOnBoard[].CardWarpData: "cmcLocVillage"</c> left the unlock
    /// objective's <c>Card</c> null — the "Have :" tooltip shows no card name, and
    /// <see cref="CardOnBoardSubObjective.CheckForCompletion"/> can never complete a null-card
    /// objective, so the blueprint never unlocks even when the player is standing in the cloned
    /// environment (CMC Miller's/Weaver's Cottage + Village Hall).
    ///
    /// <para><see cref="Data.WarpResolver.Walk"/> is idempotent for WarpType 3 (Reference): a
    /// scalar ref is filled only when it currently needs resolving (null); an already-populated
    /// list ref is skipped. Re-walking therefore fills exactly the previously-unresolvable clone
    /// refs and re-sets everything else to the same value. The single exception is WarpType 4/6
    /// (Add), which APPENDS — re-walking a card that uses an Add-warp would duplicate its
    /// already-added entries — so such cards are skipped here. No current clone-ref use case needs
    /// Add (adding a clone card to a list, e.g. EnvironmentImprovements, is handled by the
    /// dedicated <see cref="ImprovementInjector"/>, which already runs after PrepareAll).</para>
    ///
    /// <para>Called from LoadOrchestrator immediately after the WorldMapInjector phase.</para>
    /// </summary>
    public static void ResolveDeferredCloneRefs()
    {
        if (_prepared.Count == 0) return;

        // UIDs registered by clone nodes this run (CT4 env + CT8 location card).
        var cloneUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prep in _prepared)
        {
            if (string.IsNullOrEmpty(prep.Def?.CloneOfEnvironmentUID)) continue; // clone nodes only
            if (!string.IsNullOrEmpty(prep.Def.EnvironmentUID)) cloneUids.Add(prep.Def.EnvironmentUID);
            if (!string.IsNullOrEmpty(prep.Def.LocationUID))    cloneUids.Add(prep.Def.LocationUID);
        }
        if (cloneUids.Count == 0) return;

        var jsonByUid   = Loading.JsonDataLoader.JsonByUniqueId;
        var parsedByUid = Loading.JsonDataLoader.ParsedJsonByUniqueId;

        int refsFilled = 0, cardsTouched = 0, skippedAdd = 0;
        foreach (var kvp in jsonByUid)
        {
            var raw = kvp.Value;
            if (string.IsNullOrEmpty(raw)) continue;

            // Cheap pre-filter: only consider cards whose raw JSON names a clone UID.
            bool mentionsClone = false;
            foreach (var uid in cloneUids)
            {
                if (raw.IndexOf(uid, StringComparison.OrdinalIgnoreCase) >= 0) { mentionsClone = true; break; }
            }
            if (!mentionsClone) continue;

            if (!parsedByUid.TryGetValue(kvp.Key, out var tree) || tree == null)
            {
                tree = MiniJson.Parse(raw) as Dictionary<string, object>;
                if (tree == null) continue;
            }

            // Re-walking a card with an Add-type warp (WarpType 4/6) would re-append its
            // already-resolved entries. Skip those — no clone-ref use case needs Add.
            if (TreeHasAddWarp(tree))
            {
                skippedAdd++;
                Log.Debug($"WorldMapInjector: deferred clone-ref resolve skipped '{kvp.Key}' (uses an Add-type WarpType; re-walk would double-append)");
                continue;
            }

            var rtObj = GameRegistry.GetByUid(kvp.Key);
            if (rtObj == null) continue;

            try
            {
                int n = WarpResolver.Walk(rtObj, tree);
                if (n > 0) { refsFilled += n; cardsTouched++; }
            }
            catch (Exception ex)
            {
                Log.Warn($"WorldMapInjector: deferred clone-ref resolve failed for '{kvp.Key}': {Log.ExceptionText(ex)}");
            }
        }

        if (cardsTouched > 0 || skippedAdd > 0)
            Log.Info($"WorldMapInjector: deferred clone-ref resolve — {refsFilled} reference(s) filled across {cardsTouched} mod card(s)" +
                     (skippedAdd > 0 ? $"; {skippedAdd} card(s) skipped (Add-type warp)" : ""));
    }

    /// <summary>Recursively true if any <c>*WarpType</c> key in the parsed tree has value 4 (Add) or 6 (AddReference).</summary>
    private static bool TreeHasAddWarp(object node)
    {
        switch (node)
        {
            case Dictionary<string, object> dict:
                foreach (var kv in dict)
                {
                    if (kv.Key.EndsWith("WarpType", StringComparison.Ordinal))
                    {
                        int wt = 0;
                        if (kv.Value is double d) wt = (int)d;
                        else if (kv.Value is long l) wt = (int)l;
                        if (wt == 4 || wt == 6) return true;
                    }
                    if (TreeHasAddWarp(kv.Value)) return true;
                }
                return false;
            case List<object> list:
                foreach (var item in list)
                    if (TreeHasAddWarp(item)) return true;
                return false;
            default:
                return false;
        }
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

            // Instanced (interior) environments CANNOT be world-map travel nodes. A clone of an
            // instanced env (cabin/cave interior) keeps InstancedEnvironment=true, and the node's
            // EnvID is built with empty ParentEnvs, so EnvID.GetRootEnv() self-loops; WorldMapData
            // distance/path calc (RecalculateDistancesToPlayer → GetDistance/GetPath) then recurses
            // forever → a SILENT stack-overflow crash on a later environment transition. Refuse the
            // node loudly instead (decompile-verified EA 0.64f; cost a multi-session retrospective —
            // see Documentation/Retrospectives/cmc-quest-injection-crash.md).
            if (IsInstancedEnvironment(templateEnv))
            {
                Log.Error($"WorldMapInjector: CloneOfEnvironmentUID '{node.CloneOfEnvironmentUID}' (mod {node.SourceMod}) is an INSTANCED " +
                          $"environment (interior/instanced map such as a cabin or cave interior). Instanced environments cannot be world-map " +
                          $"travel nodes — their EnvID.GetRootEnv() self-loops and WorldMapData distance/path calculation recurses infinitely, " +
                          $"causing a silent stack-overflow crash on a later environment transition. Use a non-instanced OUTDOOR environment as " +
                          $"the CloneOfEnvironmentUID template. Skipping node '{node.EnvironmentUID}'.");
                return null;
            }

            if (!CardCloneService.TryCloneEnvironmentPair(
                    node.CloneOfEnvironmentUID, node.EnvironmentUID, node.LocationUID,
                    node.DisplayName, node.EnvNameLocalizationKey, node.NameLocalizationKey,
                    node.SourceMod, out var envClone, out locationCard))
                return null;
            envCard = envClone;

            // Override card face art when the mod declares a CardImage sprite name.
            if (!string.IsNullOrEmpty(node.CardImage))
            {
                ApplyCardImage(locationCard, node.CardImage);
                ApplyCardImage(envClone,     node.CardImage);
            }

            // Register the CT8 location UID ONLY for the run-start / mid-game backup spawn.
            //
            // ⚠ NEVER add ExtraDropUIDs (veins etc.) to this list. The CT8 location card is NOT a
            // persisted board card — it is the env's location card, re-derived on every entry — so
            // the watch's "is CT8 present?" guard fires on EVERY entry. Anything in this list is
            // therefore re-spawned every entry. Veins piggy-backed here accumulate +N per entry on
            // top of the LoadCardSet-restored copies (confirmed 2026-06-20: 3-per-entry → 9 veins
            // after 3 visits). Veins must seed exactly once via the persistent EnvironmentsData
            // path, never the watch. See Documentation/Retrospectives/act-cave-mining-drops.md.
            if (!string.IsNullOrEmpty(node.LocationUID))
                _cloneEnvSpawnList[node.EnvironmentUID] = new List<string> { node.LocationUID };

            // Strip all inherited drops except the CT8 location card when requested.
            // Must run BEFORE AppendExtraDrops so the only remaining drops are explicitly declared.
            if (node.StripAllInheritedDrops && locationCard != null)
                CardCloneService.StripNonLocationDrops(envClone, locationCard.UniqueID);

            // Append ExtraDropUIDs into DefaultEnvCardDrops so the vanilla env save/load system
            // seeds and restores them exactly like other env-specific cards. Once seeded, they are
            // saved on env-leave and restored by LoadCardSet on re-entry as normal board cards — so
            // the mid-game env watch must NOT re-spawn them. (It used to: that put one extra copy on
            // the board every session, because the in-memory session guard reset on each process
            // restart. See Documentation/Retrospectives/act-cave-mining-drops.md.)
            if (node.ExtraDropUIDs != null && node.ExtraDropUIDs.Count > 0)
                CardCloneService.AppendExtraDrops(envClone, node.ExtraDropUIDs);

            // Strip inherited "create X if missing" maintenance actions on the clone CT8 that
            // re-spawn any StripLegacyBoardUIDs card EVERY env entry (e.g. Caves_WaterfallCaves'
            // OnStatsChangeActions "Create a Cave Exit" → d65ae569, "Create a Flint Vein" → 977cc7d7).
            // Without this, the DefaultEnvCardDrops strip above is undone at runtime — the Exit/Flint
            // Vein reappear on every cave board (all 3 caves), and the action firing mid env-transition
            // is the prime suspect for the portal-entry NRE crash in GameManager.CalculateEnvironmentWeight.
            // Scoped to nodes declaring StripLegacyBoardUIDs (ACT caves only). See
            // Documentation/Retrospectives/act-cave-mining-drops.md.
            if (node.StripLegacyBoardUIDs != null && node.StripLegacyBoardUIDs.Count > 0 && locationCard != null)
                CardCloneService.StripActionsProducingUids(locationCard, node.StripLegacyBoardUIDs);

            // Apply env capacity stat overrides (Trees/Overgrowth/Foraging/Fertility bars)
            // declared in CapacityStats. Must run at data-load time so the SO is ready before
            // any InGameCardBase is created from it. See CLAUDE.md §WorldMap Clone Node Env Capacity Stats.
            if (node.CapacityStats != null && node.CapacityStats.Count > 0 && locationCard != null)
                EnvCapacityPatcher.Apply(locationCard, node.CapacityStats);
        }
        else
        {
            envCard = GameRegistry.GetByUid(node.EnvironmentUID);
            if (envCard == null)
            {
                Log.Warn($"WorldMapInjector: EnvironmentUID '{node.EnvironmentUID}' (mod {node.SourceMod}) not found in registry — skipping");
                return null;
            }

            // Same instanced-env hazard as the clone branch above: a direct (non-clone) reference to
            // an instanced env would inject a node whose EnvID.GetRootEnv() self-loops → silent crash.
            if (IsInstancedEnvironment(envCard))
            {
                Log.Error($"WorldMapInjector: EnvironmentUID '{node.EnvironmentUID}' (mod {node.SourceMod}) is an INSTANCED environment and " +
                          $"cannot be a world-map travel node (EnvID.GetRootEnv() self-loops → infinite recursion in WorldMapData distance/path " +
                          $"calculation → silent crash). Use a non-instanced OUTDOOR environment. Skipping node.");
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
    ///
    /// <para><strong>Full-replacement mode</strong> (when <c>_fullMapDef != null</c>):
    /// clears WorldMapData.Environments before injection, then strips orphaned travel
    /// DAs <em>after</em> injecting new ones so that freshly-cloned CT8 cards still
    /// have template DAs available for cloning during the injection step.</para>
    /// </summary>
    internal static void InjectIntoWorldMap()
    {
        if (_injectedIntoWorldMap) return;
        if (_prepared.Count == 0 && _fullMapDef == null) return;
        if (!TryResolveTypes()) return;

        var worldMapSo = FindWorldMapData();
        if (worldMapSo == null)
        {
            Log.Warn("WorldMapInjector: WorldMapData singleton not found at run start — map nodes cannot be injected this run (will retry next run start)");
            return;
        }

        bool replacementMode = _fullMapDef != null;

        // Full-replacement pre-step: clear vanilla Environments so we rebuild from scratch.
        if (replacementMode && _fullMapDef.StripVanillaConnections)
        {
            _environmentsField.SetValue(worldMapSo, Array.CreateInstance(_mapEnvDataType, 0));
            Log.Info("WorldMapInjector: cleared vanilla WorldMapData.Environments for full map replacement");
        }

        // Two-pass injection: register all node entries first, then link connections.
        // Single-pass caused "ONE-WAY" warnings when node A declared a connection to node B
        // that hadn't been registered yet — the reverse link couldn't be added (B not found).
        // Pass 1 puts every MapEnvData into WorldMapData.Environments; pass 2 links connections
        // when all nodes are guaranteed to be findable.
        int injected = 0, already = 0, skipped = 0;
        var registered = new System.Collections.Generic.List<(PreparedNode Prep, object MapEnvData, object TemplateNode)>();

        // Pass 1 — register node entries (no connections yet).
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

                object templateNode = null;
                if (!string.IsNullOrEmpty(prep.Def.CloneOfEnvironmentUID))
                {
                    var templateEnv = GameRegistry.GetByUid(prep.Def.CloneOfEnvironmentUID);
                    if (templateEnv != null)
                        templateNode = FindNodeByEnvCard(environments, templateEnv);
                    if (templateNode == null)
                        Log.Debug($"WorldMapInjector: template env '{prep.Def.CloneOfEnvironmentUID}' has no map node — icon/travel-tag inheritance unavailable");
                }

                var mapEnvData = CreateMapEnvData(prep.EnvCard, prep.Def, templateNode);
                var newEnvironments = AppendToCollection(environments, _mapEnvDataType, mapEnvData);
                if (newEnvironments == null) { skipped++; continue; }
                _environmentsField.SetValue(worldMapSo, newEnvironments);
                registered.Add((prep, mapEnvData, templateNode));
                injected++;
            }
            catch (Exception ex)
            {
                Log.Error($"WorldMapInjector: failed to register node '{prep.Def.EnvironmentUID}': {Log.ExceptionText(ex)}");
                skipped++;
            }
        }

        // Pass 2 — link connections (all mod nodes are now in WorldMapData.Environments).
        foreach (var (prep, mapEnvData, templateNode) in registered)
        {
            try
            {
                int linked = 0;
                var environments = _environmentsField.GetValue(worldMapSo);
                foreach (var conn in prep.Def.Connections)
                {
                    if (LinkConnection(environments, mapEnvData, prep.EnvCard, prep.LocationCard, templateNode, prep.Def, conn))
                        linked++;
                }

                // VanillaExits — forward-only connections to vanilla envs (P2). The vanilla node
                // gains NO reverse link (isVanillaExit:true), so pathfinding cannot route from
                // vanilla into the mod space. Skips instanced targets (stack-overflow hazard).
                // Exception: Bidirectional=true explicitly opts in to a reverse edge + DA.
                if (prep.Def.VanillaExits != null)
                {
                    foreach (var exit in prep.Def.VanillaExits)
                    {
                        var vanillaTarget = GameRegistry.GetByUid(exit.TargetVanillaEnvUID);
                        if (vanillaTarget == null || IsInstancedEnvironment(vanillaTarget)) continue;

                        var synthConn = new ConnectionDefinition
                        {
                            EnvironmentUID   = exit.TargetVanillaEnvUID,
                            PathCardUID      = null,
                            PathCost         = 10f,
                            HideConnection   = false,
                            TravelDirection  = exit.DirectionVector,
                            TravelActionTags = null,
                        };
                        if (LinkConnection(environments, mapEnvData, prep.EnvCard, prep.LocationCard, templateNode, prep.Def, synthConn, isVanillaExit: true))
                            linked++;

                        // Bidirectional: add the reverse WorldMapData edge (vanilla → mod).
                        if (exit.Bidirectional && exit.DirectionVector.HasValue)
                        {
                            var (vanillaNode, vanillaIdx) = FindNodeWithIndex(environments, vanillaTarget);
                            if (vanillaNode == null)
                            {
                                Log.Warn($"WorldMapInjector: Bidirectional reverse edge — vanilla node '{exit.TargetVanillaEnvUID}' not in WorldMapData — skipped");
                            }
                            else
                            {
                                var revDirVec = new UnityEngine.Vector4(
                                    -exit.DirectionVector.Value.x, 0f, -exit.DirectionVector.Value.z, 0f);
                                var revPathCard = prep.LocationCard ?? (CardData)prep.EnvCard;
                                var revTags = CopyTravelTags(FirstConnection(mapEnvData));
                                var revConn = CreateConnectionEntry(prep.EnvCard, revPathCard, revDirVec, 10f, false, revTags);
                                if (revConn != null)
                                {
                                    AppendConnection(vanillaNode, revConn);
                                    if (_mapEnvDataType != null && _mapEnvDataType.IsValueType && environments is Array revArr)
                                        revArr.SetValue(vanillaNode, vanillaIdx);
                                    linked++;
                                    int revDirInt = Api.WorldMap.DirectionFromVector(revDirVec.x, revDirVec.z);
                                    string revDirName = revDirInt >= 0 && revDirInt < _dirNames.Length ? _dirNames[revDirInt] : "?";
                                    Log.Debug($"WorldMapInjector: bidirectional reverse edge: vanilla '{exit.TargetVanillaEnvUID}' → mod '{prep.Def.EnvironmentUID}' dir={revDirName}");
                                }
                            }
                        }
                    }
                }

                // Struct safety: if MapEnvData is a value type, the array holds a copy made at
                // append time — write the mutated boxed instance back over its array slot.
                if (_mapEnvDataType.IsValueType)
                {
                    var envsFinal = _environmentsField.GetValue(worldMapSo);
                    var (_, idx) = FindNodeWithIndex(envsFinal, prep.EnvCard);
                    if (idx >= 0 && envsFinal is Array envArr) envArr.SetValue(mapEnvData, idx);
                }

                Log.Debug($"WorldMapInjector: injected '{prep.Def.EnvironmentUID}' into WorldMapData with {linked}/{prep.Def.Connections.Count} connection(s) from {prep.Def.SourceMod}");
            }
            catch (Exception ex)
            {
                Log.Error($"WorldMapInjector: failed to link connections for '{prep.Def.EnvironmentUID}': {Log.ExceptionText(ex)}");
                skipped++;
            }
        }

        // Fast-path future calls only when every node is accounted for.
        if (skipped == 0) _injectedIntoWorldMap = true;

        string modeLabel = replacementMode ? " (full replacement)" : "";
        Log.Info($"WorldMapInjector: {injected} node(s) injected{modeLabel}, {already} already present, {skipped} skipped");

        // Travel DA injection was moved to PrepareAll (load time, Phase 5i) so the
        // DismantleActions array is on the CardData SO before any InGameCardBase is
        // ever created from it. See the comment block in PrepareAll for the full rationale.

        // NOTE: CheckAndSpawnMissingLocationCards() is started from OnGameManagerInitialized()
        // (not here) so it runs on EVERY run start, not just the first injection. If it were
        // started here, the idempotency gate (_injectedIntoWorldMap) would prevent it from
        // running on runs 2+ within the same process — exactly the case where a player who
        // saved inside a clone env needs it most.
    }

    /// <summary>
    /// Backup for the "loaded a save already inside a clone env" case:
    /// <c>DefaultEnvCardDrops</c> seeding via <see cref="PreCreateCloneEnvSaveData"/> should
    /// handle CT8 restoration on load, but this coroutine fires one frame after
    /// <c>OnGMInitialized</c> and directly spawns the CT8 if it is missing from the board —
    /// ensuring the location card always appears.
    /// </summary>
    private static IEnumerator CheckAndSpawnMissingLocationCards()
    {
        // One frame to let the board settle after OnGMInitialized.
        yield return null;

        // On save-load the game initially sets CurrentEnvironment to the default start area
        // (GrovePine, ~5fb1552d) and then overwrites it with the save-restored env a few frames
        // later. The old loop exited on the FIRST non-null value (GrovePine), which meant saves
        // made while at Village Path were never detected — the backup logged GrovePine, saw it
        // wasn't a clone env, and skipped the spawn. Fix: poll all 120 frames unconditionally,
        // always keeping the LATEST non-null value so save restoration gets 2s to apply.
        string curEnv = null;
        for (int frame = 0; frame < 120; frame++)
        {
            yield return null;
            var candidate = Api.GameQuery.CurrentEnvironmentUniqueId;
            if (candidate != null) curEnv = candidate;
            // Early exit once we've settled on a clone env — no need to wait the full 120 frames.
            if (curEnv != null && _cloneEnvSpawnList.ContainsKey(curEnv)) break;
        }
        Log.Debug($"WorldMapInjector: run-start env resolved — current='{curEnv ?? "null"}'");

        foreach (var kvp in _cloneEnvSpawnList)
        {
            var envUid = kvp.Key;
            var spawnList = kvp.Value;
            if (spawnList.Count == 0) continue;
            var locUid = spawnList[0];  // CT8 is always first in the list

            if (!string.Equals(curEnv, envUid, StringComparison.OrdinalIgnoreCase))
                continue;

            // Player is in this clone env. Check whether the CT8 is already on the board.
            bool found = false;
            foreach (var c in Api.GameQuery.CardsInPlayerEnv())
            {
                if (string.Equals(CardUtil.GetCardUniqueId(c), locUid, StringComparison.OrdinalIgnoreCase))
                { found = true; break; }
            }

            if (found)
            {
                Log.Debug($"WorldMapInjector: CT8 '{locUid}' already on board at run start — OK");
                break;
            }

            Log.Info($"WorldMapInjector: run-start spawn — CT8 '{locUid}' missing in '{envUid}', spawning {spawnList.Count} item(s)");
            foreach (var uid in spawnList)
                Api.SpawnService.Spawn(uid);
            break;  // Only one environment is current at a time
        }
    }

    /// <summary>
    /// Starts a 1-second real-time polling interval that detects mid-game environment changes
    /// and spawns the CT8 location card when the player enters a clone env whose CT8 is missing.
    /// Complements <see cref="CheckAndSpawnMissingLocationCards"/>, which only fires at run start.
    /// </summary>
    private static void StartMidGameEnvWatch()
    {
        TickEvents.Cancel(_envWatchHandle);
        _lastWatchedEnv = null;
        _envWatchHandle = TickEvents.Interval(1.0f, OnEnvWatchTick, "WorldMap:EnvWatch");
        Log.Debug("WorldMapInjector: mid-game env watch started (1s real-time poll)");
    }

    private static void OnEnvWatchTick()
    {
        var curEnv = Api.GameQuery.CurrentEnvironmentUniqueId;
        if (curEnv == null) return;
        if (string.Equals(curEnv, _lastWatchedEnv, StringComparison.OrdinalIgnoreCase)) return;

        var prevEnv = _lastWatchedEnv;
        _lastWatchedEnv = curEnv;

        // Notify conditional drop service on any env change (before the clone-env guard below).
        try { ConditionalDropService.OnEnvArrival(curEnv); }
        catch (Exception ex) { Log.Warn($"WorldMapInjector: ConditionalDropService.OnEnvArrival failed: {ex.GetType().Name}: {ex.Message}"); }

        // Only do CT8 clone-env spawn recovery if we have clone envs.
        if (_cloneEnvSpawnList.Count == 0) return;

        // Only act if we moved INTO a clone env.
        if (!_cloneEnvSpawnList.TryGetValue(curEnv, out var spawnList) || spawnList.Count == 0) return;

        var locUid = spawnList[0];  // CT8 is always first in the list

        // Is the (indestructible) CT8 location card on the board?
        bool ct8Present = false;
        foreach (var c in Api.GameQuery.CardsInPlayerEnv())
        {
            if (string.Equals(CardUtil.GetCardUniqueId(c), locUid, StringComparison.OrdinalIgnoreCase))
            { ct8Present = true; break; }
        }

        if (!ct8Present)
        {
            // CT8 missing after a mid-game env transition → spawn ONLY the location card.
            //
            // ExtraDropUIDs (vein cards etc.) are deliberately NOT spawned here — spawnList contains
            // only the CT8 UID (see PrepareNode). The CT8 is not a persisted board card, so this
            // guard fires on every entry; anything spawned here is re-spawned every entry and
            // accumulates (3-per-entry → 9 veins after 3 visits, confirmed 2026-06-20). The CT8 has
            // no Pack Up / Dismantle action and is presence-guarded, so it can never duplicate.
            // Veins are seeded once via the persistent EnvironmentsData path, never here.
            // See Documentation/Retrospectives/act-cave-mining-drops.md.
            Log.Info($"WorldMapInjector: mid-game env watch — CT8 '{locUid}' missing in '{curEnv}' (entered from '{prevEnv ?? "?"}') — spawning");
            foreach (var uid in spawnList)
                Api.SpawnService.Spawn(uid);
            return;
        }

        // CT8 present — LoadCardSet restored the saved board. Nothing to do.
        Log.Debug($"WorldMapInjector: mid-game env watch — CT8 '{locUid}' confirmed present in '{curEnv}' (entered from '{prevEnv ?? "?"}')");
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

        // Inject hub-exit cards into any MapMod.json worlds whose EnvironmentUID is a clone env.
        // Must run BEFORE PreCreateCloneEnvSaveData so the exit card is included in the seeded
        // EnvironmentsData DefaultEnvCardDrops.
        PortalService.InjectExitCardsIntoModHubs();

        // Register shared Portal Hub travel handlers — one per mod world registered via MapMod.json
        // SacredSiteUID/EnvironmentUID. Requires clone CT4 envs to exist in the registry first.
        PortalService.RegisterHubTravelHandlers();

        // Pre-create EnvironmentsData entries for all clone envs. Without this, clone envs
        // have no entry in EnvironmentsData on first visit and ChangeEnvironment skips its
        // LoadCardSet block entirely — the board is empty and DefaultEnvCardDrops (trees) are
        // never seeded. Vanilla envs are pre-populated at game start; this gives clone envs
        // the same treatment. Idempotent: if the entry already exists (saved game), returns early.
        PreCreateCloneEnvSaveData();

        // Initialize connection gates (declarative ConnectionGates from MapNodes.json + C# callers).
        // Must run AFTER InjectIntoWorldMap so WorldMapData connections exist when we first toggle them.
        try { ConnectionGateService.Initialize(_prepared.Select(p => p.Def)); }
        catch (Exception ex) { Log.Warn($"WorldMapInjector: ConnectionGateService.Initialize failed: {Log.ExceptionText(ex)}"); }

        // Initialize sealable (negative/challenge) gates — must run after ConnectionGateService
        // so Marker-model gates can register into its Env/Edge machinery immediately.
        try { SealableGateService.Initialize(_prepared.Select(p => p.Def)); }
        catch (Exception ex) { Log.Warn($"WorldMapInjector: SealableGateService.Initialize failed: {Log.ExceptionText(ex)}"); }

        // Register conditional drops (declarative ConditionalDrops from MapNodes.json).
        // Patches AlwaysUpdate=false on ForceStay cards, subscribes to TickEvents.DayRollover.
        // OnRunStart clears FirstVisit tracking so a previous save's visits don't leak into
        // this run; RegisterNode itself dedups nodes already registered on an earlier run start.
        ConditionalDropService.OnRunStart();
        foreach (var p in _prepared)
        {
            try { ConditionalDropService.RegisterNode(p.Def); }
            catch (Exception ex) { Log.Warn($"WorldMapInjector: ConditionalDropService.RegisterNode '{p.Def?.EnvironmentUID}' failed: {Log.ExceptionText(ex)}"); }
        }

        // Run-start DA diagnostic: dump travel DAs on every clone CT8 so we can confirm the
        // DismantleActions array (written at load time) is still intact on the live SO instance.
        // Compares runtime GetHashCode to load-time hashes logged by InjectTravelActionsForPrepared.
        var diagnosedLocUids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var prep in _prepared)
        {
            if (prep.LocationCard != null)
            {
                DiagnoseTravelDAs(prep.LocationCard);
                var uid = CardUtil.GetCardUniqueId(prep.LocationCard);
                if (uid != null) diagnosedLocUids.Add(uid);
            }
        }

        // Also diagnose the VANILLA (non-prepared) side of every injected edge — e.g. River
        // Clearing, which only receives a reverse travel DA and is never itself a prepared
        // node, so the loop above never covers it. Without this, a reverse DA injected onto a
        // pre-existing vanilla CT8 has zero post-injection confirmation that it's still present.
        foreach (var edge in Api.WorldMap.InjectedEdges)
        {
            var srcUid = edge.SourceLocationUid;
            if (string.IsNullOrEmpty(srcUid) || !diagnosedLocUids.Add(srcUid)) continue;
            if (GameRegistry.GetByUid(srcUid) is CardData srcCard)
                DiagnoseTravelDAs(srcCard);
        }

        // Backup spawn — runs on EVERY OnGMInitialized (ungated). Checks whether the current env
        // is a clone env missing its CT8 and spawns it if so. Handles the save-load case where the
        // player saves inside a clone env: DefaultEnvCardDrops seeding in PreCreateCloneEnvSaveData
        // is idempotent and ChangeEnvironment restores the saved state, but this is the safety net.
        if (_cloneEnvSpawnList.Count > 0)
        {
            try { Plugin.Instance?.StartCoroutine(CheckAndSpawnMissingLocationCards()); }
            catch (Exception ex) { Log.Warn($"WorldMapInjector: backup spawn coroutine failed to start: {ex.GetType().Name}"); }
        }

        // Mid-game env watch: 1s real-time poll catches env changes after portal travel that
        // CheckAndSpawnMissingLocationCards (run-start only) cannot cover. Also drives
        // ConditionalDropService.OnEnvArrival for any env with ConditionalDrops.
        if (_cloneEnvSpawnList.Count > 0 || ConditionalDropService.HasDrops)
            StartMidGameEnvWatch();
    }

    /// <summary>
    /// Pre-creates <c>EnvironmentsData</c> entries for every clone env so that
    /// <c>GameManager.ChangeEnvironment</c>'s <c>if (EnvironmentsData.ContainsKey(key))</c>
    /// gate passes on the player's FIRST visit, allowing <c>LoadCardSet</c> to spawn the
    /// env's <c>DefaultEnvCardDrops</c> (trees, etc.) onto the board.
    ///
    /// <para>Root cause without this call: vanilla envs are pre-populated in
    /// <c>EnvironmentsData</c> at game start via <c>GetEnvSaveData(_CreateIfNull:true)</c>.
    /// Clone envs are not — so their first visit skips the if-block entirely, the board is
    /// empty, and trees never appear (they would only go into save-data-in-memory via
    /// <c>CreateCardAsSaveData</c>, then get wiped when <c>ChangeEnvironment</c> saves the
    /// leaving env with <c>new EnvironmentSaveDataByReference(...)</c>). This call gives
    /// clone envs the same pre-population treatment.</para>
    ///
    /// <para>Idempotent: if an entry already exists (save-restored game), <c>GetEnvSaveData</c>
    /// returns early without modifying anything.</para>
    /// </summary>
    private static void PreCreateCloneEnvSaveData()
    {
        var cloneNodes = _prepared.Where(p => p.LocationCard != null).ToList();
        if (cloneNodes.Count == 0) return;
        if (!TryResolveTypes()) return;

        try
        {
            // Resolve GameManager from Assembly-CSharp explicitly to avoid ModCore's shadowing
            // type (AccessTools.TypeByName is not deterministic when multiple assemblies define
            // "GameManager"; it may return ModCore's proxy which has no usable Instance getter).
            // Same pattern as SpawnService — scan by assembly name, then type name.
            if (_gmType == null)
            {
                var asmCSharp = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
                if (asmCSharp == null)
                {
                    Log.Warn("WorldMapInjector: PreCreateCloneEnvSaveData — Assembly-CSharp not found");
                    return;
                }
                try
                {
                    foreach (var t in asmCSharp.GetTypes())
                    {
                        if (t == null) continue;
                        if (t.Name == "GameManager") { _gmType = t; break; }
                    }
                }
                catch (ReflectionTypeLoadException rtle)
                {
                    foreach (var t in rtle.Types ?? Array.Empty<Type>())
                    {
                        if (t == null) continue;
                        if (t.Name == "GameManager") { _gmType = t; break; }
                    }
                }
                if (_gmType == null)
                {
                    Log.Warn("WorldMapInjector: PreCreateCloneEnvSaveData — GameManager not found in Assembly-CSharp");
                    return;
                }
            }

            var gmType = _gmType;

            // Instance is on MBSingleton<T> base — requires FlattenHierarchy or base-type walk.
            var instanceProp = gmType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy);
            if (instanceProp == null)
            {
                for (var t = gmType.BaseType; t != null; t = t.BaseType)
                {
                    instanceProp = t.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy);
                    if (instanceProp != null) break;
                }
            }
            var gmInstance = instanceProp?.GetValue(null);
            if (gmInstance == null) { Log.Warn("WorldMapInjector: PreCreateCloneEnvSaveData — GameManager.Instance is null at OnGMInitialized"); return; }

            var getEnvSaveData = gmType.GetMethods(BF)
                .FirstOrDefault(m => m.Name == "GetEnvSaveData" && m.GetParameters().Length == 2);
            if (getEnvSaveData == null) { Log.Warn("WorldMapInjector: PreCreateCloneEnvSaveData — GetEnvSaveData(EnvID,bool) not found"); return; }

            // Resolve EnvironmentsData dictionary field for remove-and-reseed on empty entries.
            // Old-save contamination: if the player visited a clone env on a pre-fix run they got an
            // empty board; ChangeEnvironment saved the leaving env state as an empty entry. The old
            // idempotency guard then saw "entry exists" and never re-seeded it. Detect entries with
            // zero cards and force-remove them so GetEnvSaveData re-seeds from DefaultEnvCardDrops.
            var envDataField = gmType.GetField("EnvironmentsData", BF);
            var envSaveDataType = AccessTools.TypeByName("EnvironmentSaveDataByReference");
            var allCardsCountMethod = envSaveDataType?.GetMethod("AllCardsCount",
                BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(bool) }, null);
            // RegularCardsCount: property (no args) that counts only regular board cards, excluding
            // improvements, inventory cards, and other non-board items. Used as a more conservative
            // zero-card check alongside AllCardsCount(false) — if AllCardsCount > 0 but
            // RegularCardsCount == 0, the entry is effectively an empty board (Unknown 2 safety net).
            var regularCardsCountProp = envSaveDataType?.GetProperty("RegularCardsCount",
                BindingFlags.Instance | BindingFlags.Public);
            // DictionnaryKey property on EnvID (note: typo in game source is intentional).
            var dictKeyProp = _envIdType?.GetProperty("DictionnaryKey", BindingFlags.Instance | BindingFlags.Public);

            int created = 0, reseeded = 0;
            foreach (var prep in cloneNodes)
            {
                var envId = CreateEnvId(prep.EnvCard);
                if (envId == null) continue;
                try
                {
                    // Check for an existing empty entry (old-save contamination).
                    // A properly seeded env always has at least the CT8 location card in AllRegularCards;
                    // an entry saved after an empty-board visit has zero cards across all collections.
                    var existingEntry = getEnvSaveData.Invoke(gmInstance, new object[] { envId, false });
                    if (existingEntry != null && allCardsCountMethod != null && envDataField != null && dictKeyProp != null)
                    {
                        int cardCount = (int)(allCardsCountMethod.Invoke(existingEntry, new object[] { false }) ?? 0);
                        // If AllCardsCount > 0 but RegularCardsCount == 0, the entry contains only
                        // non-board items (improvements, inventory). Treat it as empty so DefaultEnvCardDrops
                        // re-seeding fires and the CT8 + trees appear on the next visit.
                        if (cardCount > 0 && regularCardsCountProp != null)
                        {
                            int regularCount = (int)(regularCardsCountProp.GetValue(existingEntry) ?? 0);
                            if (regularCount == 0)
                            {
                                Log.Debug($"WorldMapInjector: '{prep.Def.EnvironmentUID}' — AllCardsCount={cardCount} but RegularCardsCount=0 — treating as empty board (improvements/inventory only, no regular cards); re-seeding");
                                cardCount = 0;
                            }
                        }
                        if (cardCount == 0)
                        {
                            // Remove the stale empty entry so GetEnvSaveData can re-seed it.
                            var dict = envDataField.GetValue(gmInstance) as System.Collections.IDictionary;
                            var entryKey = dictKeyProp.GetValue(envId);
                            if (dict != null && entryKey != null)
                            {
                                dict.Remove(entryKey);
                                existingEntry = null;
                                Log.Debug($"WorldMapInjector: removed empty EnvironmentsData entry for '{prep.Def.EnvironmentUID}' — will re-seed from DefaultEnvCardDrops (old-save fix)");
                                reseeded++;
                            }
                        }

                        // Stale-save Exit-card fix: if the node has StripAllInheritedDrops=true +
                        // ExtraDropUIDs (e.g. vein cards), but the saved board contains NONE of those
                        // expected drops, the entry was seeded before the strip feature was active and
                        // carries inherited cards (e.g. WaterfallCaves Exit) instead of veins.
                        // Force-remove so the next visit re-seeds from DefaultEnvCardDrops (which now
                        // has CT8 + vein drops only, after strip+append at load time).
                        // Guard: skip if ANY expected ExtraDrop is already present (a mined-cave save
                        // where some veins are gone must not be wiped). Also skip if no ExtraDrop UIDs
                        // can be resolved from the registry (mod may not be loaded this session).
                        if (cardCount > 0 && existingEntry != null
                            && prep.Def.StripAllInheritedDrops
                            && prep.Def.ExtraDropUIDs != null && prep.Def.ExtraDropUIDs.Count > 0)
                        {
                            var containsCardMethod = envSaveDataType?.GetMethod("ContainsCard",
                                BindingFlags.Instance | BindingFlags.Public);
                            if (containsCardMethod != null)
                            {
                                var uniqueExtraUids = new HashSet<string>(
                                    prep.Def.ExtraDropUIDs, StringComparer.OrdinalIgnoreCase);
                                int resolved = 0;
                                bool anyPresent = false;
                                foreach (var uid in uniqueExtraUids)
                                {
                                    var card = GameRegistry.GetByUid(uid) as CardData;
                                    if (card == null) continue;
                                    resolved++;
                                    try
                                    {
                                        if ((bool)(containsCardMethod.Invoke(existingEntry,
                                                new object[] { card }) ?? false))
                                        { anyPresent = true; break; }
                                    }
                                    catch { }
                                }
                                if (resolved > 0 && !anyPresent)
                                {
                                    var dict = envDataField.GetValue(gmInstance) as System.Collections.IDictionary;
                                    var entryKey = dictKeyProp.GetValue(envId);
                                    if (dict != null && entryKey != null)
                                    {
                                        dict.Remove(entryKey);
                                        existingEntry = null;
                                        Log.Debug($"WorldMapInjector: '{prep.Def.EnvironmentUID}' — EnvironmentsData exists (cards={cardCount}) but none of the {uniqueExtraUids.Count} expected ExtraDrop(s) present — re-seeding to clear stale Exit card");
                                        reseeded++;
                                    }
                                }
                            }
                        }

                        // StripLegacyBoardUIDs check: reseed if any declared legacy card is still
                        // in the saved board (handles the case where ExtraDrop veins ARE present
                        // but a stale inherited Exit/path card is also there from an old save).
                        if (cardCount > 0 && existingEntry != null
                            && prep.Def.StripLegacyBoardUIDs != null
                            && prep.Def.StripLegacyBoardUIDs.Count > 0)
                        {
                            var containsLegacyMethod = envSaveDataType?.GetMethod("ContainsCard",
                                BindingFlags.Instance | BindingFlags.Public);
                            if (containsLegacyMethod != null)
                            {
                                foreach (var legacyUid in prep.Def.StripLegacyBoardUIDs)
                                {
                                    if (existingEntry == null) break;
                                    var legacyCard = GameRegistry.GetByUid(legacyUid) as CardData;
                                    if (legacyCard == null) continue;
                                    try
                                    {
                                        if ((bool)(containsLegacyMethod.Invoke(existingEntry,
                                                new object[] { legacyCard }) ?? false))
                                        {
                                            var dict = envDataField.GetValue(gmInstance) as System.Collections.IDictionary;
                                            var entryKey = dictKeyProp.GetValue(envId);
                                            if (dict != null && entryKey != null)
                                            {
                                                dict.Remove(entryKey);
                                                existingEntry = null;
                                                Log.Debug($"WorldMapInjector: '{prep.Def.EnvironmentUID}' — EnvironmentsData contains legacy card '{legacyUid}' (StripLegacyBoardUIDs) — re-seeding to clear stale card");
                                                reseeded++;
                                            }
                                            break;
                                        }
                                    }
                                    catch { }
                                }
                            }
                        }

                        // Contamination check: if the card count exceeds ExtraDropUIDs.Count + 1
                        // (the +1 accounts for the CT8 in the initial seed before first player visit),
                        // the entry was built up by the old session-scoped watch-spawn path accumulating
                        // extra vein copies, or cross-cave contamination (e.g. iron veins in tin cave).
                        // Force-reseed to restore exactly the declared drops.
                        if (cardCount > 0 && existingEntry != null
                            && prep.Def.StripAllInheritedDrops
                            && prep.Def.ExtraDropUIDs != null && prep.Def.ExtraDropUIDs.Count > 0
                            && cardCount > prep.Def.ExtraDropUIDs.Count + 1)
                        {
                            var dict = envDataField.GetValue(gmInstance) as System.Collections.IDictionary;
                            var entryKey = dictKeyProp.GetValue(envId);
                            if (dict != null && entryKey != null)
                            {
                                dict.Remove(entryKey);
                                existingEntry = null;
                                Log.Debug($"WorldMapInjector: '{prep.Def.EnvironmentUID}' — EnvironmentsData has {cardCount} cards but max expected is {prep.Def.ExtraDropUIDs.Count + 1} — re-seeding to clear accumulation/cross-cave contamination");
                                reseeded++;
                            }
                        }
                    }

                    if (existingEntry == null)
                    {
                        getEnvSaveData.Invoke(gmInstance, new object[] { envId, true });
                        created++;
                        Log.Debug($"WorldMapInjector: pre-created EnvironmentsData for '{prep.Def.EnvironmentUID}' (UniqueIDIndex={prep.EnvCard.UniqueIDIndex})");
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn($"WorldMapInjector: PreCreateCloneEnvSaveData — '{prep.Def.EnvironmentUID}' failed: {ex.GetType().Name}: {ex.Message}");
                }
            }

            if (created > 0 || reseeded > 0)
                Log.Info($"WorldMapInjector: pre-created EnvironmentsData for {created} clone env(s) — DefaultEnvCardDrops seeded for first visit" +
                         (reseeded > 0 ? $"; re-seeded {reseeded} stale entry/entries (old-save fix)" : ""));
        }
        catch (Exception ex)
        {
            Log.Error($"WorldMapInjector: PreCreateCloneEnvSaveData failed: {Log.ExceptionText(ex)}");
        }
    }

    /// <summary>
    /// Run-start diagnostic: dumps all travel DAs on a clone CT8 so we can confirm
    /// (a) the DismantleActions written at load time are still on the live SO instance,
    /// (b) each travel DA's DroppedCard destination is non-null, and
    /// (c) the runtime GetHashCode matches the load-time hash logged by InjectTravelActionsForPrepared.
    /// All output at Log.Debug — enable BepInEx verbose logging to surface it when diagnosing travel.
    /// </summary>
    private static void DiagnoseTravelDAs(CardData locCard)
    {
        if (!TryResolveActionTypes()) return;
        Log.Debug($"WorldMapInjector: DIAG CT8='{locCard.UniqueID}' hash={locCard.GetHashCode()}");
        var daCollection = _daField_DismantleActions?.GetValue(locCard) as IEnumerable;
        if (daCollection == null) { Log.Debug($"WorldMapInjector: DIAG '{locCard.UniqueID}' — DismantleActions is null"); return; }
        int idx = 0;
        foreach (var da in daCollection)
        {
            if (da == null) { idx++; continue; }
            bool hasExpDir = _daField_HasExpDir?.GetValue(da) is true;
            if (!hasExpDir) { idx++; continue; }
            int dir = _daField_ExpDir != null ? ToInt(_daField_ExpDir.GetValue(da)) : -1;
            string dirName = dir >= 0 && dir < _dirNames.Length ? _dirNames[dir] : $"dir{dir}";
            var dest = GetTravelDest(da) as CardData;
            string destUid = dest != null ? dest.UniqueID : "NULL";
            int destHash = dest != null ? dest.GetHashCode() : 0;
            Log.Debug($"WorldMapInjector: DIAG '{locCard.UniqueID}' DA[{idx}] travel {dirName} → dest='{destUid}' destHash={destHash}");
            idx++;
        }
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

    // CardData.InstancedEnvironment (bool) — true for interior/instanced map envs (cabin, cave
    // interiors). Resolved against the card's runtime type so it works regardless of how the
    // framework's CardData reference is stripped. Cached after first resolution.
    private static FieldInfo _instancedEnvField;
    private static bool _instancedEnvFieldResolved;

    private static bool IsInstancedEnvironment(UniqueIDScriptable envCard)
    {
        if (envCard == null) return false;
        if (!_instancedEnvFieldResolved)
        {
            _instancedEnvFieldResolved = true;
            _instancedEnvField = FindField(envCard.GetType(), "InstancedEnvironment");
            if (_instancedEnvField == null)
                Log.Debug("WorldMapInjector: CardData.InstancedEnvironment field not found — instanced-env guard inactive");
        }
        return _instancedEnvField?.GetValue(envCard) is true;
    }

    // --------------------------------------------------------- discovery ---

    /// <summary>
    /// Returns the live WorldMapData SO, using the session cache when available.
    /// Called by <see cref="ConnectionGateService"/> to access map connections without
    /// a second <c>Resources.FindObjectsOfTypeAll</c> call.
    /// </summary>
    internal static object GetWorldMapSo() => _worldMapCached ?? FindWorldMapData();

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
        MapNodeDefinition node, ConnectionDefinition conn, bool isVanillaExit = false)
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

        // Forward PathCard: explicit UID → node's own location card → CT4→CT8 lookup.
        // The last fallback handles vanilla-ref nodes (no CloneOf) whose CT8 is already
        // registered in AllData and can be found via CardCloneService.
        object fwdPathCard = null;
        if (!string.IsNullOrEmpty(conn.PathCardUID))
        {
            fwdPathCard = GameRegistry.GetByUid(conn.PathCardUID);
            if (fwdPathCard == null)
                Log.Debug($"WorldMapInjector: PathCardUID '{conn.PathCardUID}' not found — connection will have no path card");
        }
        fwdPathCard ??= locationCard;
        if (fwdPathCard == null && envCard is CardData envCd)
            fwdPathCard = CardCloneService.FindLocationCardFor(envCd);

        // Travel action tags: explicit names, else inherited from the template node's
        // connections (clone case), else from the target node's existing connections —
        // so mod travel actions carry the same tags as vanilla travel.
        var fwdTags = ResolveTravelTags(conn.TravelActionTags)
            ?? CopyTravelTags(FirstConnection(templateNode))
            ?? CopyTravelTags(FirstConnection(targetNode));

        var fwd = CreateConnectionEntry(targetCard, fwdPathCard, dir, conn.PathCost, conn.HideConnection, fwdTags);
        if (fwd == null) return false;
        AppendConnection(mapEnvData, fwd);

        // VanillaExit: forward-only (P2). The vanilla target node must NOT gain a reverse
        // connection back into the mod space — the player returns to mod space only via a portal.
        if (isVanillaExit) return true;

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

    /// <summary>Resolves runtime ActionTag names to a collection matching the target field's
    /// declared type (array or List&lt;T&gt;). Returns null when none resolve.</summary>
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

        return MakeTypedCollection(_travelTagsField.FieldType, elemType, resolved);
    }

    /// <summary>Shallow-copies the TravelActionTags collection from an existing connection
    /// entry, returning a collection whose type matches <see cref="_travelTagsField"/> (array
    /// or List&lt;T&gt;). Always returns null when empty, so the caller's null-guard skips
    /// the SetValue call instead of assigning a wrong-typed container.</summary>
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

        return MakeTypedCollection(_travelTagsField.FieldType, elemType, items);
    }

    /// <summary>
    /// Creates a collection of the same declared type as <paramref name="fieldType"/>
    /// (array or generic List) populated with <paramref name="items"/>. This ensures
    /// <c>FieldInfo.SetValue</c> receives a compatible object — SetValue throws
    /// ArgumentException when given an <c>ActionTag[]</c> for a <c>List&lt;ActionTag&gt;</c>
    /// field, which is the exact failure observed in EA 0.64f (2026-06-13 log).
    /// </summary>
    private static object MakeTypedCollection(Type fieldType, Type elemType, IList<object> items)
    {
        if (fieldType.IsArray)
        {
            var arr = Array.CreateInstance(elemType, items.Count);
            for (int i = 0; i < items.Count; i++) arr.SetValue(items[i], i);
            return arr;
        }
        if (fieldType.IsGenericType && typeof(IList).IsAssignableFrom(fieldType))
        {
            var list = (IList)Activator.CreateInstance(fieldType);
            foreach (var item in items) list.Add(item);
            return list;
        }
        return null;
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

    /// <summary>
    /// Records the FORWARD-only edge for a <see cref="VanillaExitDefinition"/> (mod node →
    /// vanilla env) in <see cref="Api.WorldMap"/> for map-consuming mods. No reverse edge is
    /// recorded (P2 — vanilla must not route into mod space). Skipped (with a Debug log, never a
    /// silent cap) when a location card can't be resolved or the direction is non-cardinal.
    /// </summary>
    private static void RecordVanillaExitEdge(PreparedNode prep, VanillaExitDefinition exit)
    {
        var targetCard = GameRegistry.GetByUid(exit.TargetVanillaEnvUID) as CardData;
        if (targetCard == null)
        {
            Log.Warn($"WorldMapInjector: VanillaExit target '{exit.TargetVanillaEnvUID}' not found — edge not recorded for map consumers");
            return;
        }
        if (IsInstancedEnvironment(targetCard)) return; // guarded loudly in the DA loop

        var srcLocUid = prep.LocationCard != null
            ? prep.LocationCard.UniqueID
            : CardCloneService.FindLocationCardFor(prep.EnvCard as CardData)?.UniqueID;
        var dstLocUid = CardCloneService.FindLocationCardFor(targetCard)?.UniqueID;

        var dir = exit.DirectionVector ?? UnityEngine.Vector4.zero;
        var direction = Api.WorldMap.DirectionFromVector(dir.x, dir.z);

        if (string.IsNullOrEmpty(srcLocUid) || string.IsNullOrEmpty(dstLocUid)
            || srcLocUid == dstLocUid || direction < 0)
        {
            Log.Debug($"WorldMapInjector: vanilla-exit edge {prep.Def.EnvironmentUID}→{exit.TargetVanillaEnvUID} not recorded " +
                      $"(srcLoc={(srcLocUid ?? "?")}, dstLoc={(dstLocUid ?? "?")}, dir={direction})");
            return;
        }

        Api.WorldMap.Record(new Api.WorldMap.MapEdge
        {
            SourceEnvUid = prep.Def.EnvironmentUID,
            SourceLocationUid = srcLocUid,
            Direction = direction,
            DestinationEnvUid = exit.TargetVanillaEnvUID,
            DestinationLocationUid = dstLocUid,
            PathCost = 10f,
            HiddenOnInGameMap = false,
            SourceMod = prep.Def.SourceMod,
        });

        // Bidirectional: also record the reverse edge (vanilla → mod) for Api.WorldMap consumers.
        if (exit.Bidirectional)
        {
            var vanillaLocUid = !string.IsNullOrEmpty(exit.TargetVanillaLocUID)
                ? exit.TargetVanillaLocUID
                : dstLocUid;
            var revDir = Api.WorldMap.DirectionFromVector(-dir.x, -dir.z);
            if (revDir >= 0 && !string.IsNullOrEmpty(vanillaLocUid))
            {
                Api.WorldMap.Record(new Api.WorldMap.MapEdge
                {
                    SourceEnvUid = exit.TargetVanillaEnvUID,
                    SourceLocationUid = vanillaLocUid,
                    Direction = revDir,
                    DestinationEnvUid = prep.Def.EnvironmentUID,
                    DestinationLocationUid = srcLocUid,
                    PathCost = 10f,
                    HiddenOnInGameMap = false,
                    SourceMod = prep.Def.SourceMod,
                });
            }
        }
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

    /// <summary>Initializes every null array-typed or List&lt;T&gt;-typed field on an object
    /// WE created to an empty collection. Never call this on vanilla objects (CLAUDE.md
    /// §Vanilla Data Protection). Handles both array fields and generic List fields so the
    /// game's foreach-over-TravelActionTags doesn't NullRef when the field is List&lt;T&gt;.</summary>
    private static void InitNullArrayFields(object obj)
    {
        if (obj == null) return;
        foreach (var f in obj.GetType().GetFields(BF))
        {
            try
            {
                if (f.GetValue(obj) != null) continue;
                if (f.FieldType.IsArray)
                    f.SetValue(obj, Array.CreateInstance(f.FieldType.GetElementType(), 0));
                else if (f.FieldType.IsGenericType && typeof(IList).IsAssignableFrom(f.FieldType))
                    f.SetValue(obj, Activator.CreateInstance(f.FieldType));
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

    /// <summary>
    /// Sets the <c>CardImage</c> (Sprite) field on a cloned card from <see cref="Database.SpriteDict"/>.
    /// Only called on cards WE created (clones) — never on vanilla cards (CLAUDE.md §Vanilla Data Protection).
    /// </summary>
    private static void ApplyCardImage(CardData card, string spriteName)
    {
        if (card == null || string.IsNullOrEmpty(spriteName)) return;
        if (!Database.SpriteDict.TryGetValue(spriteName, out var sprite))
        {
            Log.Debug($"WorldMapInjector: CardImage sprite '{spriteName}' not found in SpriteDict for '{card.name}' — art not applied");
            return;
        }
        var fi = FindField(typeof(CardData), "CardImage")
              ?? FindField(typeof(CardData), "cardImage");
        if (fi == null)
        {
            Log.Warn($"WorldMapInjector: CardImage field not found on CardData — card art not applied for '{card.name}'");
            return;
        }
        fi.SetValue(card, sprite);
        Log.Debug($"WorldMapInjector: set CardImage '{spriteName}' on '{card.name}'");
    }

    // ======================================================= travel DA injection ===
    //
    // Travel in CSFF is driven by DismantleActions on CT8 cards that have
    // HasExplorationDirection=true and ProducedCards[0].DroppedCards[0].DroppedCard =
    // the destination CT4 environment card.  WorldMapData connections drive the map UI
    // and pathfinding graph, but the actual travel BUTTON and destination are encoded in
    // the CT8 card's DismantleActions.
    //
    // For a new mod location (e.g. Village Path east of River Clearing), the injector
    // must ALSO append a reverse travel DA to the target CT8 card (River Clearing).
    // Verified empirically: River Clearing CT8 has North/South/West DAs but no East DA,
    // so east travel never appeared despite 6 attempts at WorldMapData-only injection.
    //
    // Called at the end of InjectIntoWorldMap() (Stage 2, OnGMInitialized) after the
    // WorldMapData node has been appended, so all SO refs are resolved and the clone
    // CT4 env card exists in AllData/GameRegistry.
    //
    // Idempotent: each InjectTravelDA call checks whether a DA for that direction already
    // targets the new env before appending.
    //
    // ExplorationDirection enum (decompile-verified, EA 0.64f):
    //   0 = North, 1 = South, 2 = East, 3 = West
    // These match Api.WorldMap.DirectionNorth/South/East/West exactly.

    // — cached types/fields for DA injection —
    private static bool   _actionTypesResolved;
    private static Type   _dismantleActionType;
    private static FieldInfo _daField_DismantleActions;  // CardData.DismantleActions
    private static FieldInfo _daField_CardType;           // CardData.CardType (for CT8 filter)
    private static FieldInfo _daField_HasExpDir;          // DismantleAction.HasExplorationDirection
    private static FieldInfo _daField_ExpDir;             // DismantleAction.ExplorationDirection
    private static FieldInfo _daField_FadeMessage;        // DismantleAction.FadeMessage
    private static FieldInfo _daField_ActionName;         // DismantleAction.ActionName
    private static FieldInfo _daField_NotBaseAction;      // DismantleAction.NotBaseAction
    private static FieldInfo _daField_ExpValue;           // DismantleAction.ExplorationValue (float 0–1; 1=100%)
    private static FieldInfo _daField_ProducedCards;      // DismantleAction.ProducedCards
    private static Type   _pcEntryType;                   // element type of ProducedCards
    private static FieldInfo _pcField_DroppedCards;       // <ProducedCardsEntry>.DroppedCards
    private static Type   _cdType;                        // element type of DroppedCards (CardDrop)
    private static FieldInfo _cdField_DroppedCard;        // CardDrop.DroppedCard
    private static FieldInfo _cdField_WarpData;           // CardDrop.DroppedCardWarpData

    private static readonly string[] _dirNames  = { "North", "South", "East",  "West"  };
    private static readonly string[] _dirFades  = { "Travelling North...", "Travelling South...",
                                                     "Travelling East...", "Travelling West..." };

    private static bool TryResolveActionTypes()
    {
        if (_actionTypesResolved) return _dismantleActionType != null;
        _actionTypesResolved = true;

        var cardDataType = AccessTools.TypeByName("CardData");
        if (cardDataType == null) return false;

        _daField_DismantleActions = FindField(cardDataType, "DismantleActions");
        if (_daField_DismantleActions == null) return false;

        _daField_CardType = FindField(cardDataType, "CardType");

        var daArrType = _daField_DismantleActions.FieldType;
        _dismantleActionType = daArrType.IsArray ? daArrType.GetElementType()
            : daArrType.IsGenericType ? daArrType.GetGenericArguments()[0] : null;
        if (_dismantleActionType == null) return false;

        _daField_HasExpDir    = FindField(_dismantleActionType, "HasExplorationDirection");
        _daField_ExpDir       = FindField(_dismantleActionType, "ExplorationDirection");
        _daField_ExpValue     = FindField(_dismantleActionType, "ExplorationValue");
        _daField_FadeMessage  = FindField(_dismantleActionType, "FadeMessage");
        _daField_ActionName   = FindField(_dismantleActionType, "ActionName");
        _daField_NotBaseAction= FindField(_dismantleActionType, "NotBaseAction");
        _daField_ProducedCards= FindField(_dismantleActionType, "ProducedCards");

        if (_daField_ProducedCards != null)
        {
            var pcArrType = _daField_ProducedCards.FieldType;
            _pcEntryType = pcArrType.IsArray ? pcArrType.GetElementType()
                : pcArrType.IsGenericType ? pcArrType.GetGenericArguments()[0] : null;
            if (_pcEntryType != null)
            {
                _pcField_DroppedCards = FindField(_pcEntryType, "DroppedCards");
                if (_pcField_DroppedCards != null)
                {
                    var dcArrType = _pcField_DroppedCards.FieldType;
                    _cdType = dcArrType.IsArray ? dcArrType.GetElementType()
                        : dcArrType.IsGenericType ? dcArrType.GetGenericArguments()[0] : null;
                    if (_cdType != null)
                    {
                        _cdField_DroppedCard = FindField(_cdType, "DroppedCard");
                        _cdField_WarpData    = FindField(_cdType, "DroppedCardWarpData");
                    }
                }
            }
        }

        Log.Debug($"WorldMapInjector: travel-DA types resolved — DismantleAction={_dismantleActionType.Name}, " +
                 $"CardDrop={_cdType?.Name ?? "null"}, DroppedCardField={_cdField_DroppedCard?.Name ?? "NULL(ProducedCards will not produce)"}");
        return true;
    }

    /// <summary>
    /// Injects travel DAs for all connections declared by <paramref name="prep"/>:
    /// <list type="bullet">
    ///   <item><description>
    ///     <strong>Forward DA</strong> on the new node's own CT8 card: "from here, travel
    ///     &lt;fwdDir&gt; to reach &lt;target CT4&gt;". Requires the new node to have a
    ///     location card (clone-based nodes always do; non-clone nodes must have an existing
    ///     CT8 found via the game registry).
    ///   </description></item>
    ///   <item><description>
    ///     <strong>Reverse DA</strong> on the target's CT8 card: "from target, travel
    ///     &lt;revDir&gt; to reach &lt;new CT4&gt;".
    ///   </description></item>
    /// </list>
    /// For clone-based nodes, all inherited travel DAs are stripped from the clone CT8
    /// BEFORE injection so the template's directions (which point to the template's
    /// vanilla neighbours) do not conflict with the correct mod-declared directions.
    /// Returns the count of DAs actually appended.
    /// </summary>
    private static int InjectTravelActionsForPrepared(PreparedNode prep)
    {
        if (!TryResolveActionTypes()) return 0;

        // NOTE: inherited template travel DAs are stripped from ALL prepared clone CT8s
        // in a single pass in PrepareAll BEFORE this runs. Stripping here (per node)
        // destroyed reverse DAs that earlier-processed nodes had already injected onto
        // this node's CT8.

        int count = 0;
        var destEnvCard = prep.EnvCard as CardData;

        foreach (var conn in prep.Def.Connections)
        {
            var fwdVec = conn.TravelDirection;
            if (!fwdVec.HasValue)
            {
                Log.Warn($"WorldMapInjector: skipping travel DAs for {prep.Def.EnvironmentUID}→{conn.EnvironmentUID}: no explicit TravelDirection (derive-from-coords not yet supported for DA injection) — the map connection is added but no button will appear");
                continue;
            }

            int fwdDir = Api.WorldMap.DirectionFromVector(fwdVec.Value.x, fwdVec.Value.z);
            if (fwdDir < 0)
            {
                Log.Warn($"WorldMapInjector: TravelDirection {fwdVec} is not cardinal — travel DAs not injected for {conn.EnvironmentUID}");
                continue;
            }
            int revDir = fwdDir ^ 1; // N↔S (0↔1), E↔W (2↔3)

            var targetEnvCard = GameRegistry.GetByUid(conn.EnvironmentUID) as CardData;
            if (targetEnvCard == null)
            {
                Log.Debug($"WorldMapInjector: target env '{conn.EnvironmentUID}' not found as CardData — skipping travel DAs");
                continue;
            }

            if (destEnvCard == null)
            {
                Log.Warn($"WorldMapInjector: new env '{prep.Def.EnvironmentUID}' is not a CardData instance — travel DAs not injected");
                continue;
            }

            // Forward DA: new node CT8 → target CT4 env (player leaves the new location heading fwdDir)
            if (prep.LocationCard != null)
            {
                Log.Debug($"WorldMapInjector: forward DA — CT8='{prep.LocationCard.UniqueID}' hash={prep.LocationCard.GetHashCode()} → dest='{conn.EnvironmentUID}' dir={_dirNames[fwdDir]}");
                if (InjectTravelDA(prep.LocationCard, targetEnvCard, fwdDir, conn.EnvironmentUID))
                    count++;
            }

            // Reverse DA: target CT8 → new CT4 env (player at the target location heads back revDir)
            var targetLocCard = CardCloneService.FindLocationCardFor(targetEnvCard);
            if (targetLocCard == null)
            {
                Log.Warn($"WorldMapInjector: no CT8 location card for target env '{conn.EnvironmentUID}' — reverse travel DA not injected");
                continue;
            }
            Log.Debug($"WorldMapInjector: reverse DA — CT8='{targetLocCard.UniqueID}' hash={targetLocCard.GetHashCode()} → dest='{prep.Def.EnvironmentUID}' dir={_dirNames[revDir]}");
            if (InjectTravelDA(targetLocCard, destEnvCard, revDir, prep.Def.EnvironmentUID))
                count++;
        }

        // VanillaExits — FORWARD-only travel DAs from this mod node's CT8 to a vanilla env.
        // NEVER inject a reverse DA on the vanilla CT8 (P2): the mod space is portal-only entry.
        if (prep.Def.VanillaExits != null && prep.Def.VanillaExits.Count > 0)
        {
            // The exit DA must live on THIS node's CT8 (clone CT8, or the resolved CT8 for a
            // non-clone node). prep.LocationCard is the clone CT8 when CloneOf was used.
            var modCt8 = prep.LocationCard ?? CardCloneService.FindLocationCardFor(destEnvCard);
            if (modCt8 == null)
            {
                Log.Warn($"WorldMapInjector: node '{prep.Def.EnvironmentUID}' declares VanillaExits but has no CT8 location card — exits not injected");
            }
            else
            {
                foreach (var exit in prep.Def.VanillaExits)
                {
                    if (!exit.DirectionVector.HasValue)
                    {
                        Log.Warn($"WorldMapInjector: VanillaExit on '{prep.Def.EnvironmentUID}' → '{exit.TargetVanillaEnvUID}' has no parsed direction — skipped");
                        continue;
                    }
                    int exitDir = Api.WorldMap.DirectionFromVector(exit.DirectionVector.Value.x, exit.DirectionVector.Value.z);
                    if (exitDir < 0)
                    {
                        Log.Warn($"WorldMapInjector: VanillaExit on '{prep.Def.EnvironmentUID}' has non-cardinal direction '{exit.Direction}' — skipped");
                        continue;
                    }

                    var vanillaTarget = GameRegistry.GetByUid(exit.TargetVanillaEnvUID) as CardData;
                    if (vanillaTarget == null)
                    {
                        Log.Warn($"WorldMapInjector: VanillaExit target env '{exit.TargetVanillaEnvUID}' not found as CardData — exit not injected from '{prep.Def.EnvironmentUID}'");
                        continue;
                    }
                    if (IsInstancedEnvironment(vanillaTarget))
                    {
                        Log.Error($"WorldMapInjector: VanillaExit target '{exit.TargetVanillaEnvUID}' (from '{prep.Def.EnvironmentUID}', mod {prep.Def.SourceMod}) is an INSTANCED " +
                                  $"environment — cannot be a travel target (EnvID.GetRootEnv() self-loops → infinite recursion in WorldMapData pathfinding). Use a non-instanced OUTDOOR env. Exit skipped.");
                        continue;
                    }

                    Log.Debug($"WorldMapInjector: vanilla-exit DA — CT8='{modCt8.UniqueID}' → vanilla env '{exit.TargetVanillaEnvUID}' dir={_dirNames[exitDir]}");
                    if (InjectTravelDA(modCt8, vanillaTarget, exitDir, exit.TargetVanillaEnvUID))
                        count++;

                    // Bidirectional: also inject the reverse DA on the vanilla CT8 (vanilla → mod).
                    if (exit.Bidirectional)
                    {
                        var vanillaCt8 = !string.IsNullOrEmpty(exit.TargetVanillaLocUID)
                            ? GameRegistry.GetByUid(exit.TargetVanillaLocUID) as CardData
                            : CardCloneService.FindLocationCardFor(vanillaTarget);
                        if (vanillaCt8 == null)
                        {
                            Log.Warn($"WorldMapInjector: Bidirectional reverse DA — vanilla CT8 not found for '{exit.TargetVanillaEnvUID}' " +
                                     $"(TargetVanillaLocUID='{exit.TargetVanillaLocUID ?? "null"}') — skipped");
                        }
                        else
                        {
                            int revDir = exitDir ^ 1; // N(0)↔S(1), E(2)↔W(3)
                            Log.Debug($"WorldMapInjector: bidirectional reverse DA — vanilla CT8='{vanillaCt8.UniqueID}' → mod env '{prep.Def.EnvironmentUID}' dir={_dirNames[revDir]}" +
                                      (string.IsNullOrEmpty(exit.ReverseActionName) ? "" : $" label='{exit.ReverseActionName}'"));
                            if (InjectTravelDA(vanillaCt8, destEnvCard, revDir, prep.Def.EnvironmentUID, exit.ReverseActionName))
                                count++;
                        }
                    }
                }
            }
        }

        return count;
    }

    /// <summary>
    /// Removes all <c>HasExplorationDirection=true</c> DismantleActions from a CT8 card.
    /// Used before injecting correct travel DAs on clone nodes so inherited template DAs
    /// (pointing to the template's vanilla neighbours) don't conflict with the correct ones.
    /// Non-travel DAs (Forage, Clear, etc.) are preserved. Returns the number stripped.
    /// Caller must have run <see cref="TryResolveActionTypes"/> — with the DA reflection
    /// fields unresolved this method cannot see any DAs and returns 0.
    /// </summary>
    private static int StripTravelDAsFromCard(CardData card)
    {
        if (_daField_DismantleActions == null || _daField_HasExpDir == null) return 0;
        var daCollection = _daField_DismantleActions.GetValue(card);
        if (daCollection == null) return 0;

        var toKeep = new List<object>();
        bool anyRemoved = false;

        foreach (var da in (IEnumerable)daCollection)
        {
            if (da == null) continue;
            if (_daField_HasExpDir.GetValue(da) is true)
            {
                anyRemoved = true;
            }
            else
            {
                toKeep.Add(da);
            }
        }

        if (!anyRemoved) return 0;

        var daArrType = _daField_DismantleActions.FieldType;
        var elemType  = CollectionElementType(daArrType);
        if (elemType == null) return 0;

        object newCollection;
        if (daArrType.IsArray)
        {
            var arr = Array.CreateInstance(elemType, toKeep.Count);
            for (int i = 0; i < toKeep.Count; i++) arr.SetValue(toKeep[i], i);
            newCollection = arr;
        }
        else if (daArrType.IsGenericType && typeof(IList).IsAssignableFrom(daArrType))
        {
            var list = (IList)Activator.CreateInstance(daArrType);
            foreach (var da in toKeep) list.Add(da);
            newCollection = list;
        }
        else return 0;

        _daField_DismantleActions.SetValue(card, newCollection);
        int stripped = (daCollection is ICollection col) ? (col.Count - toKeep.Count) : 0;
        Log.Debug($"WorldMapInjector: stripped {stripped} inherited travel DA(s) from clone CT8 '{card.UniqueID}'");
        return stripped;
    }

    /// <summary>
    /// Appends a travel DismantleAction to <paramref name="targetLocCard"/> (a CT8 location
    /// card) that carries the player to <paramref name="destEnvCard"/> (CT4 env card) in the
    /// given cardinal direction. Idempotent: skips if the direction already has a DA pointing
    /// to <paramref name="destEnvCard"/>.
    /// </summary>
    private static bool InjectTravelDA(CardData targetLocCard, CardData destEnvCard,
        int dirCode, string destEnvUid, string actionNameOverride = null)
    {
        if (_daField_DismantleActions == null || _dismantleActionType == null) return false;
        if (dirCode < 0 || dirCode >= _dirNames.Length) return false;

        var daCollection = _daField_DismantleActions.GetValue(targetLocCard);
        if (daCollection == null) return false;

        string dirName = _dirNames[dirCode];

        // Scan existing DAs: find a travel template AND check idempotency.
        object template = null;
        if (daCollection is IEnumerable daEnum)
        {
            foreach (var da in daEnum)
            {
                if (da == null) continue;
                bool hasExpDir = _daField_HasExpDir?.GetValue(da) is true;

                if (hasExpDir)
                {
                    int existingDir = _daField_ExpDir != null ? ToInt(_daField_ExpDir.GetValue(da)) : -1;
                    if (existingDir == dirCode)
                    {
                        // Same direction — is it already pointing to our new env?
                        var existingDest = GetTravelDest(da);
                        if (ReferenceEquals(existingDest, destEnvCard))
                        {
                            Log.Debug($"WorldMapInjector: {targetLocCard.UniqueID} already has {dirName} travel DA → '{destEnvUid}' — skipping");
                            return false;
                        }
                        // Different destination for the same direction — vanilla owns it;
                        // cannot add a second cardinal button without UI conflicts.
                        Log.Debug($"WorldMapInjector: {targetLocCard.UniqueID} already has a {dirName} travel DA pointing elsewhere — cannot inject reverse for '{destEnvUid}'");
                        return false;
                    }
                    template ??= da; // use first travel DA as template
                }
            }
        }

        // No travel DA on this card — use a global travel DA from AllData rather than a
        // non-travel DA (e.g. Forage), which would clone the wrong description, stat changes,
        // and empty ProducedCards (Produced: Nothing in-game). This is the common path for
        // clone CT8s after StripTravelDAsFromCard removes their inherited travel DAs.
        if (template == null)
            template = FindGlobalTravelDaTemplate();

        // Absolute last resort: any DA on this card (should not happen in practice — every
        // CT8 should have at least one travel DA found by FindGlobalTravelDaTemplate above).
        if (template == null)
        {
            foreach (var da in (IEnumerable)daCollection)
            {
                if (da != null) { template = da; break; }
            }
        }

        if (template == null)
        {
            Log.Warn($"WorldMapInjector: {targetLocCard.UniqueID} has no DismantleActions at all — cannot inject {dirName} travel DA for '{destEnvUid}'");
            return false;
        }

        // Diagnostic: identify which template is being used and confirm RequiredStatValues strip.
        // Log.Debug — enable BepInEx verbose logging to surface it when diagnosing travel DA injection.
        {
            var tName = _daField_ActionName?.GetValue(template);
            var tText = tName != null ? (FindField(tName.GetType(), "DefaultText")?.GetValue(tName) as string ?? "?") : "?";
            var hasReqStat = FindField(template.GetType(), "RequiredStatValues");
            var reqCount = (hasReqStat?.GetValue(template) as System.Collections.IEnumerable)?.Cast<object>().Count() ?? -1;
            Log.Debug($"WorldMapInjector: InjectTravelDA template for {dirName} on '{targetLocCard.UniqueID}' = '{tText}' (RequiredStatValues before-strip={reqCount})");
        }

        // Clone template via shallow field copy (CLAUDE.md runtime action injection pattern).
        var daType = template.GetType();
        var newDA = Activator.CreateInstance(daType);
        foreach (var fi in daType.GetFields(BF))
        {
            try { fi.SetValue(newDA, fi.GetValue(template)); } catch { }
        }

        // Strip inherited stat gates. The template DA (e.g. River Clearing's West DA) may carry
        // HideAllWhenNotMet=true on its Light gate, which would hide the injected travel DA until
        // dawn — causing a red-X in the compass all night. Injected mod travel DAs should always
        // be accessible regardless of light or stamina levels.
        try
        {
            var reqStatField = FindField(daType, "RequiredStatValues");
            if (reqStatField != null)
            {
                var ft = reqStatField.FieldType;
                object empty = ft.IsArray
                    ? Array.CreateInstance(ft.GetElementType(), 0)
                    : Activator.CreateInstance(ft);
                reqStatField.SetValue(newDA, empty);
                var afterCount = (reqStatField.GetValue(newDA) as System.Collections.IEnumerable)?.Cast<object>().Count() ?? -1;
                Log.Debug($"WorldMapInjector: InjectTravelDA RequiredStatValues stripped on '{targetLocCard.UniqueID}' {dirName}: {afterCount} gate(s) remaining");
            }
            else
            {
                Log.Warn($"WorldMapInjector: InjectTravelDA '{targetLocCard.UniqueID}' {dirName}: RequiredStatValues field not found on DA type '{daType.Name}'");
            }
        }
        catch (Exception stripEx)
        {
            Log.Warn($"WorldMapInjector: InjectTravelDA RequiredStatValues strip FAILED on '{targetLocCard.UniqueID}' {dirName}: {stripEx.GetType().Name}: {stripEx.Message} — gate may remain active");
        }

        // Fresh ActionName so we don't mutate the template's LocalizedString.
        ReplaceLocalizedString(_daField_ActionName, newDA, template, actionNameOverride ?? dirName);

        // Travel direction flags.
        _daField_HasExpDir?.SetValue(newDA, true);
        _daField_ExpDir?.SetValue(newDA, dirCode);

        // Fresh FadeMessage.
        ReplaceLocalizedString(_daField_FadeMessage, newDA, template, _dirFades[dirCode]);

        // NotBaseAction = true (travel DAs are hidden from base action list).
        _daField_NotBaseAction?.SetValue(newDA, true);

        // Rebuild ProducedCards so DroppedCard points to the new destination env.
        BuildTravelProducedCards(newDA, template, destEnvCard);

        // Append the new DA (handles Array or List<T>).
        var newCollection = AppendToCollection(daCollection, daType, newDA);
        if (newCollection == null)
        {
            Log.Warn($"WorldMapInjector: failed to append {dirName} travel DA to {targetLocCard.UniqueID}");
            return false;
        }
        _daField_DismantleActions.SetValue(targetLocCard, newCollection);
        Log.Debug($"WorldMapInjector: injected '{dirName}' travel DA on '{targetLocCard.UniqueID}' → '{destEnvUid}'");
        return true;
    }

    /// <summary>
    /// Replaces the LocalizedString at <paramref name="field"/> on <paramref name="target"/>
    /// with a fresh instance that only overrides DefaultText. Avoids mutating the template's
    /// shared LocalizedString reference.
    /// </summary>
    private static void ReplaceLocalizedString(FieldInfo field, object target, object template, string text)
    {
        if (field == null) return;
        var templateStr = field.GetValue(template);
        if (templateStr == null) return;
        var strType = templateStr.GetType();
        var fresh = Activator.CreateInstance(strType);
        foreach (var fi in strType.GetFields(BF))
        {
            try { fi.SetValue(fresh, fi.GetValue(templateStr)); } catch { }
        }
        FindField(strType, "ParentObjectID")?.SetValue(fresh, "");
        FindField(strType, "LocalizationKey")?.SetValue(fresh, "");
        FindField(strType, "DefaultText")?.SetValue(fresh, text);
        field.SetValue(target, fresh);
    }

    /// <summary>
    /// Rebuilds the ProducedCards on the cloned DA so that DroppedCards[0].DroppedCard
    /// points to <paramref name="destEnvCard"/> instead of the template's original destination.
    /// All other ProducedCards fields (stat mods, coolections, drop chance, etc.) are
    /// shallow-copied from the template.
    /// </summary>
    private static void BuildTravelProducedCards(object newDA, object template, CardData destEnvCard)
    {
        if (_daField_ProducedCards == null || _pcEntryType == null
            || _pcField_DroppedCards == null || _cdType == null || _cdField_DroppedCard == null)
            return;

        var templatePCs = _daField_ProducedCards.GetValue(template);
        var templatePCList = templatePCs as IEnumerable;
        if (templatePCList == null) return;

        object templatePC = null;
        foreach (var pc in templatePCList) { if (pc != null) { templatePC = pc; break; } }
        if (templatePC == null) return;

        // Clone ProducedCards entry.
        var freshPC = Activator.CreateInstance(_pcEntryType);
        foreach (var fi in _pcEntryType.GetFields(BF))
        {
            try { fi.SetValue(freshPC, fi.GetValue(templatePC)); } catch { }
        }

        // Clone the first CardDrop and swap its DroppedCard.
        var templateDCs = _pcField_DroppedCards.GetValue(templatePC) as IEnumerable;
        object templateDC = null;
        if (templateDCs != null)
            foreach (var dc in templateDCs) { if (dc != null) { templateDC = dc; break; } }

        object freshDC;
        if (templateDC != null)
        {
            freshDC = Activator.CreateInstance(_cdType);
            foreach (var fi in _cdType.GetFields(BF))
            {
                try { fi.SetValue(freshDC, fi.GetValue(templateDC)); } catch { }
            }
            _cdField_DroppedCard.SetValue(freshDC, destEnvCard);
            _cdField_WarpData?.SetValue(freshDC, ""); // WarpData is stale post-WarpResolver
        }
        else
        {
            freshDC = Activator.CreateInstance(_cdType);
            _cdField_DroppedCard.SetValue(freshDC, destEnvCard);
            InitNullArrayFields(freshDC);
        }

        // Rebuild DroppedCards with just the one entry.
        var dcArrType = _pcField_DroppedCards.FieldType;
        object freshDCArr;
        if (dcArrType.IsArray)
        {
            var arr = Array.CreateInstance(_cdType, 1);
            arr.SetValue(freshDC, 0);
            freshDCArr = arr;
        }
        else
        {
            var list = (IList)Activator.CreateInstance(dcArrType);
            list.Add(freshDC);
            freshDCArr = list;
        }
        _pcField_DroppedCards.SetValue(freshPC, freshDCArr);

        // Rebuild ProducedCards with just this entry.
        var pcArrType = _daField_ProducedCards.FieldType;
        object freshPCArr;
        if (pcArrType.IsArray)
        {
            var arr = Array.CreateInstance(_pcEntryType, 1);
            arr.SetValue(freshPC, 0);
            freshPCArr = arr;
        }
        else
        {
            var list = (IList)Activator.CreateInstance(pcArrType);
            list.Add(freshPC);
            freshPCArr = list;
        }
        _daField_ProducedCards.SetValue(newDA, freshPCArr);
    }

    /// <summary>
    /// Full-replacement post-step: scans every CT8 card in AllData and removes
    /// DismantleActions with <c>HasExplorationDirection=true</c> whose destination CT4
    /// is NOT in the new map's node list. Called after <see cref="InjectTravelActionsForPrepared"/>
    /// so freshly-injected DAs survive and only inherited orphan DAs are removed.
    /// </summary>
    private static void StripOrphanedTravelDAs()
    {
        if (!TryResolveActionTypes()) return;

        // The valid set: all CT4 env UIDs in the new map (prepared nodes + raw FullMap decls).
        var validEnvUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prep in _prepared)
            validEnvUids.Add(prep.Def.EnvironmentUID);
        if (_fullMapDef?.Nodes != null)
            foreach (var node in _fullMapDef.Nodes)
                if (!string.IsNullOrEmpty(node.EnvironmentUID))
                    validEnvUids.Add(node.EnvironmentUID);

        int stripped = 0;
        var allData = GameRegistry.AllData;
        if (allData == null) return;

        foreach (var obj in allData)
        {
            if (obj is not CardData card) continue;

            // Only CT8 (location / construction) cards.
            if (_daField_CardType != null)
            {
                try { if (Convert.ToInt32(_daField_CardType.GetValue(card)) != 8) continue; }
                catch { continue; }
            }

            var daCollection = _daField_DismantleActions?.GetValue(card);
            if (daCollection == null) continue;

            var toKeep = new List<object>();
            bool anyRemoved = false;

            foreach (var da in (IEnumerable)daCollection)
            {
                if (da == null) continue;
                bool hasExpDir = _daField_HasExpDir?.GetValue(da) is true;
                if (!hasExpDir) { toKeep.Add(da); continue; }

                var destCard = GetTravelDest(da) as CardData;
                if (destCard == null || validEnvUids.Contains(destCard.UniqueID))
                {
                    toKeep.Add(da); // destination valid or unresolvable — keep
                }
                else
                {
                    int dirCode  = _daField_ExpDir != null ? ToInt(_daField_ExpDir.GetValue(da)) : -1;
                    string dName = dirCode >= 0 && dirCode < _dirNames.Length ? _dirNames[dirCode] : "?";
                    Log.Debug($"WorldMapInjector: stripping orphaned {dName} travel DA from '{card.UniqueID}' → dest '{destCard.UniqueID}' (not in new map)");
                    anyRemoved = true;
                    stripped++;
                }
            }

            if (!anyRemoved) continue;

            var daArrType = _daField_DismantleActions.FieldType;
            var elemType  = CollectionElementType(daArrType);
            object newCollection;
            if (daArrType.IsArray)
            {
                var arr = Array.CreateInstance(elemType, toKeep.Count);
                for (int i = 0; i < toKeep.Count; i++) arr.SetValue(toKeep[i], i);
                newCollection = arr;
            }
            else if (daArrType.IsGenericType && typeof(IList).IsAssignableFrom(daArrType))
            {
                var list = (IList)Activator.CreateInstance(daArrType);
                foreach (var da in toKeep) list.Add(da);
                newCollection = list;
            }
            else continue;

            _daField_DismantleActions.SetValue(card, newCollection);
        }

        if (stripped > 0)
            Log.Info($"WorldMapInjector: stripped {stripped} orphaned travel DA(s) from CT8 location cards");
    }

    /// <summary>
    /// Returns any travel DA (HasExplorationDirection=true) found on any CT8 card in AllData.
    /// Used as a structural clone template when a specific CT8 card has no DAs of its own
    /// (e.g., freshly-cloned CT8 whose inherited template DAs were all orphans). Cached for
    /// the lifetime of a single injection pass. Returns null only when AllData has no travel
    /// DAs at all, which would mean no map travel is possible.
    /// </summary>
    private static object FindGlobalTravelDaTemplate()
    {
        if (_cachedGlobalTravelDaTemplate != null) return _cachedGlobalTravelDaTemplate;
        if (!TryResolveActionTypes()) return null;
        var allData = GameRegistry.AllData;
        if (allData == null) return null;
        foreach (var obj in allData)
        {
            if (obj is not CardData card) continue;
            var daCollection = _daField_DismantleActions?.GetValue(card);
            if (daCollection == null) continue;
            foreach (var da in (IEnumerable)daCollection)
            {
                if (da != null && _daField_HasExpDir?.GetValue(da) is true)
                    return _cachedGlobalTravelDaTemplate = da;
            }
        }
        return null;
    }

    /// <summary>Returns DroppedCard from ProducedCards[0].DroppedCards[0] of a DA, or null.</summary>
    private static object GetTravelDest(object da)
    {
        if (_daField_ProducedCards == null || _pcField_DroppedCards == null
            || _cdField_DroppedCard == null) return null;
        var pcs = _daField_ProducedCards.GetValue(da) as IEnumerable;
        if (pcs == null) return null;
        foreach (var pc in pcs)
        {
            if (pc == null) continue;
            var dcs = _pcField_DroppedCards.GetValue(pc) as IEnumerable;
            if (dcs == null) continue;
            foreach (var dc in dcs)
            {
                if (dc != null) return _cdField_DroppedCard.GetValue(dc);
            }
        }
        return null;
    }
}
