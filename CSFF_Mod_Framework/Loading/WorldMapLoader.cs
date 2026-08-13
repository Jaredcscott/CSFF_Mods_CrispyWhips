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
        /// <summary>Sprite name (no extension) for the card face art. Applied to both
        /// the cloned CT4 environment card and the cloned CT8 location card after
        /// cloning. Non-clone nodes are not affected (they ship their own CardData JSON).
        /// Null = inherits the template card's art.</summary>
        public string CardImage;
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

        /// <summary>
        /// Optional list of additional card UniqueIDs to spawn on the board when the player
        /// first arrives at this clone environment (spawned alongside the CT8 location card
        /// via the deferred arrival mechanism). Use to place trees, resource piles, etc.
        /// that would normally come from the template's DefaultEnvCardDrops but land on the
        /// wrong board before the env transition completes.
        /// </summary>
        public List<string> ExtraDropUIDs;

        /// <summary>
        /// Optional one-way compass exits from THIS mod node back to a vanilla environment
        /// (design principle P2). Each exit injects a forward-only travel DA onto this node's
        /// CT8 card pointing at the target vanilla CT4 — NEVER a reverse DA on the vanilla CT8
        /// (the mod space is reachable only via its portal, never by routing in from vanilla).
        /// Multiple mods may exit to the same vanilla env without conflict; the DAs live on
        /// different mod CT8 cards. Null when the node declares no vanilla exits.
        /// </summary>
        public List<VanillaExitDefinition> VanillaExits;

        /// <summary>
        /// When true, ALL DefaultEnvCardDrops entries inherited from the clone template are
        /// removed from the clone env EXCEPT the newly-cloned CT8 location card. Any drops
        /// declared in <see cref="ExtraDropUIDs"/> are then appended as the only extras.
        /// Use this when the template carries path/exit cards (e.g. WaterfallCaves exit path)
        /// that must not appear in the mod's clone envs. Default: false.
        /// </summary>
        public bool StripAllInheritedDrops;

        /// <summary>
        /// Optional list of vanilla card UniqueIDs (or GUIDs) that should never appear on this
        /// clone env's board. On each run start, if the player's saved EnvironmentsData for this
        /// clone contains any of these cards, the entry is cleared and re-seeded from
        /// DefaultEnvCardDrops on the next visit.
        /// Use this to clear stale inherited board cards that were saved before
        /// <see cref="StripAllInheritedDrops"/> was active — e.g. the Caves_WaterfallCaves
        /// "Exit" card (b956acdc…) that leaked into old saves of the ACT vein caves.
        /// Null when not declared.
        /// </summary>
        public List<string> StripLegacyBoardUIDs;

        public string SourceMod;

    // ── New declarative fields (Framework v2.9) ────────────────────────────

    /// <summary>
    /// Optional capacity-bar overrides for the cloned CT8 location card's
    /// SpecialDurability1–4 (Trees / Overgrowth / Foraging / Fertility). Keys
    /// are human-readable stat names matched case-insensitively against each
    /// DurabilityStat's <c>CardStatName.DefaultText</c>. Applied at data-load
    /// time in <see cref="Injection.EnvCapacityPatcher.Apply"/>, before any
    /// InGameCardBase is created from the SO.
    /// <para>Example: <c>"Foraging": { "MaxValue": 1500, "InitialValue": 1500, "RatePerDTP": 1.5 }</c></para>
    /// </summary>
    public Dictionary<string, CapacityStatOverride> CapacityStats;

    /// <summary>
    /// Declarative perk / improvement gates on map connections originating from
    /// or targeting this node. Evaluated at run start (<c>OnGMInitialized</c>)
    /// and again whenever an improvement is completed mid-run.
    /// Replaces per-mod <c>VillagePathUnlockPatch</c>-style Harmony patches.
    /// </summary>
    public List<ConnectionGateDefinition> ConnectionGates;

    /// <summary>
    /// Runtime-conditional board drops for this clone environment. Unlike
    /// <see cref="ExtraDropUIDs"/> (always seeded into DefaultEnvCardDrops),
    /// these cards are spawned / removed at runtime when their conditions change.
    /// Use for seasonal crops, perk-unlocked NPCs, quest items, etc.
    /// <para><strong>Note:</strong> ConditionalDrops are NOT seeded into
    /// <c>EnvironmentsData</c> — they do not persist across sessions. Use
    /// <see cref="ExtraDropUIDs"/> for resource cards that must persist their
    /// depletion state (veins, trees).</para>
    /// </summary>
    public List<ConditionalDropDefinition> ConditionalDrops;

    /// <summary>
    /// Negative/challenge gates: "open by default, a trigger (usually taking a perk) seals
    /// it, a dig/hack action on a challenge card clears it, optionally reseals on a timer."
    /// Replaces per-mod hand-rolled Harmony gate patches (ACT's Cave Prospector cave network,
    /// H&amp;F's Forest Scout trail). See <see cref="Injection.SealableGateService"/>.
    /// </summary>
    public List<SealableGateDefinition> SealableGates;
    }

    // ── New DTOs for declarative map features ─────────────────────────────

    /// <summary>Override for one SpecialDurability stat on a cloned CT8 location card.
    /// Null fields are left at their template-inherited values.</summary>
    internal sealed class CapacityStatOverride
    {
        /// <summary>New cap value for this stat bar (e.g. 1500 for Foraging).</summary>
        public float? MaxValue;
        /// <summary>Starting value. Defaults to <see cref="MaxValue"/> when null.</summary>
        public float? InitialValue;
        /// <summary>Passive rate per DTP (negative = drain, positive = regen).</summary>
        public float? RatePerDTP;
    }

    /// <summary>One condition inside a <see cref="ConnectionGateDefinition"/>.</summary>
    internal sealed class GateConditionDefinition
    {
        /// <summary>"Always", "PerkEquipped", "ImprovementBuilt", "StatThreshold", or "Season".
        /// "Always" is unconditionally true — for a <c>SealTrigger</c>, use it when a gate should
        /// be sealed by default for every player rather than only once some perk/state is
        /// reached (e.g. a challenge wall meant to block ALL players, including those on an
        /// existing save who install the mod without ever taking a specific perk).</summary>
        public string Type;
        /// <summary>Perk UniqueID (PerkEquipped), improvement UniqueID (ImprovementBuilt), GameStat
        /// UniqueID (StatThreshold), or a season name — "Spring"|"Summer"|"Autumn"|"Winter",
        /// case-insensitive (Season) — compared against <c>Api.GameQuery.CurrentSeason</c>.
        /// <para>Not the same shape as <see cref="ConditionalDropDefinition"/>'s "SeasonRange" (a
        /// numeric 1-4 index range used in a different JSON context, <c>ConditionalDrops</c>, not
        /// <c>SealTrigger</c>/<c>ConnectionGates.GateConditions</c>) — don't conflate the two.</para>
        /// </summary>
        public string UID;
        /// <summary>Environment UniqueID where the improvement must be built (ImprovementBuilt only).</summary>
        public string EnvUID;
        /// <summary>Threshold value to compare the GameStat's current value against (StatThreshold only).</summary>
        public float Value;
        /// <summary>"GreaterOrEqual" (default) | "LessOrEqual" | "GreaterThan" | "LessThan" | "Equal"
        /// (StatThreshold only).</summary>
        public string Comparison = "GreaterOrEqual";
    }

    /// <summary>
    /// Declarative gate on a map connection. When the conditions are not met, the map
    /// node and all touching connections are hidden from the in-game map. Optionally
    /// strips the reverse travel DA from a neighbor CT8 so no compass button appears.
    /// </summary>
    internal sealed class ConnectionGateDefinition
    {
        /// <summary>UniqueID of the CT4 environment whose map visibility this gate controls.</summary>
        public string ConnectionUID;
        /// <summary>Conditions evaluated to determine gate state.</summary>
        public List<GateConditionDefinition> GateConditions = new();
        /// <summary>"ANY" (any condition met → open) or "ALL" (all conditions must be met).</summary>
        public string GateLogic = "ANY";
        /// <summary>Conditions that force the gate LOCKED when met, overriding
        /// <see cref="GateConditions"/> entirely (any one met → locked; no logic mode).
        /// Lets a second, negative axis close a connection an improvement/perk gate would
        /// otherwise hold open — e.g. a StatThreshold on a crime/notoriety stat (CMC Village
        /// Guards banishment) — without registering a second, conflicting gate on the same
        /// <see cref="ConnectionUID"/>. Empty = no lock override. (2.20.0)</summary>
        public List<GateConditionDefinition> LockConditions = new();
        /// <summary>When true (default), removes the travel DA pointing toward
        /// <see cref="ConnectionUID"/> from <see cref="NeighborCt8UID"/> while the gate is closed.</summary>
        public bool HideTravelDA = true;
        /// <summary>When true, the stripped travel DA is re-added when the gate opens.
        /// When false (default), the strip is permanent — the DA stays removed even after unlock.</summary>
        public bool RestoreDAOnUnlock = false;
        /// <summary>UniqueID of the CT8 location card that carries the reverse travel DA to strip.
        /// Null = auto-detected from the connections list (bidirectional strip of all edges).</summary>
        public string NeighborCt8UID;
        /// <summary>"Env" (default — hides the whole target env + all its connections) or "Edge"
        /// (hides only the single connection between this node and <see cref="ConnectionUID"/>,
        /// via <c>ConnectionGateService.RegisterEdgeGate</c> — use when the target env has other
        /// connections that must stay unaffected, e.g. a multi-spoke cave hub).</summary>
        public string Granularity = "Env";
    }

    /// <summary>Condition for a <see cref="ConditionalDropDefinition"/>.</summary>
    internal sealed class DropConditionDefinition
    {
        /// <summary>
        /// Condition type: "AlwaysTrue" | "HasPerk" | "ImprovementBuilt" |
        /// "SeasonRange" | "DayRange" | "FirstVisit" | "QuestActive".
        /// </summary>
        public string Type;
        /// <summary>Perk UniqueID (HasPerk) or improvement UniqueID (ImprovementBuilt).</summary>
        public string UID;
        /// <summary>Environment where the improvement must be built (ImprovementBuilt).</summary>
        public string EnvUID;
        /// <summary>Quest UniqueID (QuestActive).</summary>
        public string QuestUID;
        /// <summary>Start of range — season index 1–4 (SeasonRange) or day number (DayRange).</summary>
        public int From;
        /// <summary>End of range — inclusive. Season indices: 1=Spring 2=Summer 3=Autumn 4=Winter.</summary>
        public int To;
    }

    /// <summary>One runtime-conditional board drop for a clone environment.</summary>
    internal sealed class ConditionalDropDefinition
    {
        /// <summary>UniqueID of the card to conditionally spawn on the env board.</summary>
        public string UID;
        /// <summary>Condition that gates this card's presence on the board.</summary>
        public DropConditionDefinition Condition;
        /// <summary>Maximum simultaneous board copies allowed (default 1).</summary>
        public int MaxOnBoard = 1;
        /// <summary>
        /// When true (default), sets <c>AlwaysUpdate = false</c> on the card's CardData SO so
        /// CT2 placed cards with <c>AlwaysUpdate=true</c> (e.g. FlaxFieldMature) do not follow
        /// the player out of the env on environment transitions. See CLAUDE.md §AlwaysUpdate.
        /// </summary>
        public bool ForceStay = true;
    }

    /// <summary>
    /// One negative/challenge gate: an env (<c>Granularity:"Env"</c>) or a single connection
    /// (<c>Granularity:"Edge"</c>) that is open by default, sealed once <see cref="SealTrigger"/>
    /// is met, and reopened when the player clears <see cref="ChallengeCardUID"/>.
    /// </summary>
    internal sealed class SealableGateDefinition
    {
        /// <summary>UniqueID of the CT4 environment this gate's edge/env-gate targets — the
        /// OTHER end of the connection, viewed from the node that declares this entry.</summary>
        public string ConnectionUID;
        /// <summary>
        /// "Marker" (default) — a persistent boolean marker in <see cref="MarkerEnvUID"/>'s
        /// <c>CurrentlyBuiltImprovements</c> records "cleared"; symmetric strip/restore on both
        /// sides; permanent once cleared unless <see cref="ResealCondition"/> is set. Matches
        /// ACT's cave-passage model.
        /// <para>"CardPresence" — no marker; sealed/open state is derived from which of
        /// <see cref="ChallengeCardUID"/>/<see cref="ClearedTransformInto"/> is currently present
        /// on each <see cref="DirectionalGates"/> side's board/env-save. Each side can seal
        /// independently, and a native card-JSON regrowth timer (not this service) handles
        /// reseal by transforming the card back. Matches H&amp;F's forest-trail model.</para>
        /// </summary>
        public string StateModel = "Marker";
        /// <summary>"Env" (default — hides the whole target env + all its connections, via
        /// <c>ConnectionGateService.RegisterGate</c>) or "Edge" (hides only the single connection
        /// between this node and <see cref="ConnectionUID"/>, via <c>RegisterEdgeGate</c> — use
        /// when the target env has other connections that must stay unaffected). Ignored when
        /// <see cref="StateModel"/> is "CardPresence" — <see cref="DirectionalGates"/> controls
        /// DA/map-line visibility directly in that model.</summary>
        public string Granularity = "Env";
        /// <summary>Optional override for which CT8 carries the DA to strip. Null = auto-detected
        /// from <c>Api.WorldMap.InjectedEdges</c> (same convention as <c>ConnectionGates</c>).</summary>
        public string NeighborCt8UID;
        /// <summary>Condition that causes this gate to seal. Reuses the same condition shape as
        /// <c>ConnectionGates</c> (Type: "PerkEquipped" | "ImprovementBuilt" | "StatThreshold" |
        /// "Season"). Players who never meet this condition are never gated.
        /// <para><b>"Season" is the first non-monotonic trigger shipped</b> — unlike
        /// PerkEquipped/ImprovementBuilt, it legitimately flips true→false→true every year. This
        /// is why <see cref="Injection.SealableGateService"/>'s <c>OnPoll</c> tracks a per-gate
        /// trigger-active transition (forces one extra <c>ConnectionGateService.EvaluateAll()</c>
        /// when the trigger goes false, so a road nobody dug through still reopens once the
        /// season ends) and why <c>CheckResealTimer</c> no longer gates on a session-scoped
        /// "cleared" flag before checking elapsed days — both needed once a trigger can go false
        /// again within the same play session.</para></summary>
        public GateConditionDefinition SealTrigger;
        /// <summary>UniqueID of the challenge card (wall/barrier/overgrowth) that appears while
        /// sealed and not yet cleared.</summary>
        public string ChallengeCardUID;
        /// <summary>LocalizationKey prefix of the DismantleAction on <see cref="ChallengeCardUID"/>
        /// that clears the gate (e.g. a dig or hack action already shipped on that card's own JSON —
        /// this construct does not change the challenge card's CardData, only who reacts to it).</summary>
        public string ClearActionKeyPrefix;
        /// <summary>When true, the clear action may fire multiple times before the gate actually
        /// opens (e.g. a multi-hit durability wall) — the handler re-checks the challenge card's
        /// UsageDurability/board-presence before treating it as cleared. False (default) = the
        /// action's first firing clears it immediately (e.g. a single hack/transform action).</summary>
        public bool MultiHit;
        /// <summary>UID the challenge card transforms into once cleared (e.g. H&amp;F's Overgrown→
        /// Cleared trail). Null (default) = the challenge card is destroyed/removed by its own
        /// native mechanic instead of transforming (e.g. ACT's dig-to-destroy walls).</summary>
        public string ClearedTransformInto;
        /// <summary>Optional distinct marker key for this gate's persistent cleared-state flag.
        /// Defaults to <c>ChallengeCardUID + "_cleared"</c> — set explicitly only when multiple
        /// gates share a ChallengeCardUID (unusual).</summary>
        public string MarkerUID;
        /// <summary>Environment whose <c>CurrentlyBuiltImprovements</c> holds the persistent
        /// cleared-state marker. Usually the node's own hub/approach env — may differ from
        /// <see cref="ConnectionUID"/> (e.g. ACT stores all three cave passage markers on the
        /// Tin Cave hub env, not on Copper/Iron/Waterfall).</summary>
        public string MarkerEnvUID;
        /// <summary>Every environment where the service should watch for and (re)spawn
        /// <see cref="ChallengeCardUID"/> while sealed. Must include every board a player could
        /// arrive at that needs the challenge card present — e.g. both the vanilla approach side
        /// AND the portal-landing side, so a direct-portal entrant cannot bypass the gate.</summary>
        public List<string> SeedOnEnvUIDs = new();
        /// <summary>
        /// "Marker" state model only. Optional reseal timer. Null (default) = permanent once
        /// cleared. "CardPresence" gates never need this — their regrowth is a native card-JSON
        /// timer the service simply observes via <see cref="DirectionalGates"/>.
        /// </summary>
        public GateResealDefinition ResealCondition;

        /// <summary>
        /// "CardPresence" state model only. One entry per travel direction that needs
        /// independent DA control (e.g. H&amp;F's entry DA on the vanilla Primeval Woods side
        /// and exit DA on its own Foraging Path side, each sealed by a separately-evaluated
        /// board/env-save check). Null/empty for "Marker" gates.
        /// </summary>
        public List<DirectionalGateSide> DirectionalGates;
    }

    /// <summary>
    /// One travel direction's independent DA control for a "CardPresence" <see cref="SealableGateDefinition"/>.
    /// </summary>
    internal sealed class DirectionalGateSide
    {
        /// <summary>Environment whose board (if the player is currently there) or env-save
        /// (otherwise) is checked for the presence of the gate's ChallengeCardUID/
        /// ClearedTransformInto to determine sealed vs. open for this side.</summary>
        public string WatchEnvUID;
        /// <summary>Known CT8 location card UID carrying this side's travel DA. Null = resolve
        /// dynamically at run start by scanning for the CT8 whose own DA travels to the
        /// declaring node's env/location (use for a vanilla neighbor whose CT8 UID isn't
        /// stable/documented — e.g. H&amp;F's Primeval Woods entry side).</summary>
        public string Ct8Uid;
        /// <summary>Cardinal direction (0=N,1=S,2=E,3=W) of the DA to strip/restore on
        /// <see cref="Ct8Uid"/>. Required when <see cref="Ct8Uid"/> is set (static match).
        /// Null when resolving dynamically — the DA is instead matched by destination
        /// (travels to the declaring node), which is more robust than a fixed direction
        /// on an env whose layout this mod does not own.</summary>
        public int? Direction;
        /// <summary>Dynamic resolution only: known clone CT8 UIDs to exclude while scanning,
        /// so a mod with multiple spokes into the same neighbor doesn't match the wrong one.</summary>
        public List<string> ExcludeCt8Uids;
        /// <summary>When true, this side's sealed state also toggles the shared
        /// ConnectionUID↔(declaring node) map line. Exactly one side should normally set this
        /// (a map line cannot be "half hidden") — matches H&amp;F, where only the
        /// vanilla-approach side controls map-line visibility.</summary>
        public bool ControlsMapLine;
    }

    /// <summary>Reseal condition for a <see cref="SealableGateDefinition"/>.</summary>
    internal sealed class GateResealDefinition
    {
        /// <summary>Only "TimerRegrowth" is currently supported.</summary>
        public string Type = "TimerRegrowth";
        /// <summary>In-game days since the clear marker was written before resealing.</summary>
        public int Days;
        /// <summary>UID to respawn as the challenge card when resealing. Defaults to the
        /// gate's own <c>ChallengeCardUID</c> when omitted.</summary>
        public string TransformInto;
    }

    /// <summary>
    /// Parsed content of <c>WorldMap/FullMap.json</c>. When present, the framework
    /// discards all vanilla WorldMapData.Environments and rebuilds the map graph
    /// entirely from the declared nodes. Only one mod may ship FullMap.json per session.
    /// </summary>
    internal sealed class FullMapDefinition
    {
        /// <summary>Name of the mod that owns this replacement map.</summary>
        public string OwnerMod;
        /// <summary>When true (default), all vanilla WorldMapData.Environments entries are
        /// removed before injecting FullMap nodes. Set false only if you want to overlay
        /// on top of the vanilla graph (unusual).</summary>
        public bool StripVanillaConnections = true;
        /// <summary>When true (default), travel DismantleActions on CT8 cards whose
        /// destination CT4 is not declared in this FullMap are removed after injection.</summary>
        public bool StripOrphanedTravelDAs = true;
        /// <summary>All map nodes for the replacement graph. Parsed via the same
        /// <see cref="MapNodeDefinition"/> schema as MapNodes.json.</summary>
        public List<MapNodeDefinition> Nodes = new();
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

    /// <summary>One forward-only compass exit from a mod node to a vanilla environment (P2).</summary>
    internal sealed class VanillaExitDefinition
    {
        /// <summary>Compass button shown on the mod node's location card: North/South/East/West.</summary>
        public string Direction;
        /// <summary>UniqueID of the vanilla CT4 environment this exit travels to.</summary>
        public string TargetVanillaEnvUID;
        /// <summary>Parsed cardinal unit vector for <see cref="Direction"/> (x=+1 East/−1 West,
        /// z=+1 North/−1 South). Computed at load time from the Direction string.</summary>
        public UnityEngine.Vector4? DirectionVector;
        /// <summary>
        /// When true, also injects the reverse travel DA on the vanilla CT8 and adds the reverse
        /// WorldMapData edge so players can travel from the vanilla env into this mod env.
        /// Requires <see cref="TargetVanillaLocUID"/> to identify the vanilla CT8 location card.
        /// </summary>
        public bool Bidirectional;
        /// <summary>UniqueID of the vanilla CT8 location card. Required when
        /// <see cref="Bidirectional"/> is true so the reverse travel DA can be injected.</summary>
        public string TargetVanillaLocUID;
        /// <summary>
        /// Optional override for the reverse travel DA's button/tooltip text (injected on the
        /// vanilla CT8 when <see cref="Bidirectional"/> is true). Null/empty = default cardinal
        /// direction name (e.g. "East"), matching vanilla convention.
        /// </summary>
        public string ReverseActionName;
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

        ValidateCrossModOwnership(result);
        return result;
    }

    /// <summary>
    /// Warns when a mod's <c>ConnectionGates</c>/<c>SealableGates</c>/<c>ConditionalDrops</c>
    /// targets an <c>EnvironmentUID</c> declared by a DIFFERENT mod's node — nothing currently
    /// prevents mod B from shipping a gate that silently hides/shows mod A's content, or a
    /// conditional drop that reads mod A's improvement state (design doc §3.F "navigation
    /// isolation" gap). Targeting a vanilla or unrecognized env is not flagged — only known
    /// cross-mod targeting is. Diagnostic only (Log.Warn); does not reject the mod or the entry,
    /// since a deliberate cross-mod integration is a legitimate (if rare) use case — the
    /// <c>/audit-mod</c> map-JSON validator promotes this to a build-time-visible finding.
    /// </summary>
    private static void ValidateCrossModOwnership(List<MapNodeDefinition> allNodes)
    {
        var ownerByEnv = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in allNodes)
            if (!string.IsNullOrEmpty(node.EnvironmentUID) && !string.IsNullOrEmpty(node.SourceMod))
                ownerByEnv[node.EnvironmentUID] = node.SourceMod;

        foreach (var node in allNodes)
        {
            if (node.ConnectionGates != null)
                foreach (var gate in node.ConnectionGates)
                    WarnIfCrossModTarget(node, gate.ConnectionUID, ownerByEnv, "ConnectionGates.ConnectionUID");

            if (node.SealableGates != null)
                foreach (var gate in node.SealableGates)
                    WarnIfCrossModTarget(node, gate.ConnectionUID, ownerByEnv, "SealableGates.ConnectionUID");

            if (node.ConditionalDrops != null)
                foreach (var drop in node.ConditionalDrops)
                    if ("ImprovementBuilt".Equals(drop.Condition?.Type, StringComparison.OrdinalIgnoreCase))
                        WarnIfCrossModTarget(node, drop.Condition.EnvUID, ownerByEnv, "ConditionalDrops.Condition.EnvUID");
        }
    }

    private static void WarnIfCrossModTarget(MapNodeDefinition node, string targetUid,
        Dictionary<string, string> ownerByEnv, string fieldLabel)
    {
        if (string.IsNullOrEmpty(targetUid)) return;
        if (!ownerByEnv.TryGetValue(targetUid, out var ownerMod)) return;          // vanilla/unrecognized — fine
        if (ownerMod.Equals(node.SourceMod, StringComparison.Ordinal)) return;      // targets its own node — fine
        Log.Warn($"WorldMapLoader: {node.SourceMod} node '{node.EnvironmentUID}' {fieldLabel} targets '{targetUid}', owned by mod '{ownerMod}' — cross-mod gate/drop targeting is unusual; confirm this is intentional");
    }

    /// <summary>
    /// Reads <c>WorldMap/FullMap.json</c> from mods. At most one mod may ship this file;
    /// if two do, logs an error and returns null (falls back to additive mode). Returns
    /// null when no mod ships FullMap.json.
    /// </summary>
    public static FullMapDefinition LoadFullMap(IReadOnlyList<ModManifest> mods)
    {
        var candidates = new List<(string modName, string path)>();
        foreach (var mod in mods)
        {
            if (!mod.HasFullMap) continue;
            var path = System.IO.Path.Combine(mod.DirectoryPath, "WorldMap", "FullMap.json");
            if (System.IO.File.Exists(path))
                candidates.Add((mod.Name, path));
        }

        if (candidates.Count == 0) return null;

        if (candidates.Count > 1)
        {
            Log.Error($"WorldMap full replacement conflict: {candidates[0].modName} and {candidates[1].modName} both ship WorldMap/FullMap.json — falling back to additive mode. Only one mod may own a full map replacement per session.");
            return null;
        }

        var (ownerName, filePath) = candidates[0];
        try
        {
            var json = System.IO.File.ReadAllText(filePath);
            var def = ParseFullMap(json, ownerName);
            if (def != null)
                Log.Debug($"WorldMapLoader: FullMap from {ownerName}: {def.Nodes.Count} node(s), StripVanillaConnections={def.StripVanillaConnections}, StripOrphanedTravelDAs={def.StripOrphanedTravelDAs}");
            return def;
        }
        catch (Exception ex)
        {
            Log.Error($"WorldMapLoader: failed to read {filePath}: {Log.ExceptionText(ex)}");
            return null;
        }
    }

    private static FullMapDefinition ParseFullMap(string json, string modName)
    {
        var parsed = MiniJson.Parse(json);
        if (parsed is not Dictionary<string, object> dict)
        {
            Log.Warn($"WorldMapLoader: {modName} WorldMap/FullMap.json root must be a JSON object (got {parsed?.GetType().Name ?? "null"})");
            return null;
        }

        var def = new FullMapDefinition
        {
            OwnerMod = modName,
            StripVanillaConnections = GetBool(dict, "StripVanillaConnections", true),
            StripOrphanedTravelDAs  = GetBool(dict, "StripOrphanedTravelDAs",  true),
        };

        if (dict.TryGetValue("Nodes", out var rawNodes) && rawNodes is List<object> arr)
        {
            foreach (var item in arr)
            {
                if (item is not Dictionary<string, object> nodeDict) continue;
                try
                {
                    var node = ParseNode(nodeDict, modName);
                    if (node != null) def.Nodes.Add(node);
                }
                catch (Exception ex)
                {
                    Log.Warn($"WorldMapLoader: {modName} FullMap — skipping malformed node: {Log.ExceptionText(ex)}");
                }
            }
        }

        return def;
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
            CardImage      = GetString(dict, "CardImage"),
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

        // Parse ExtraDropUIDs array (optional; items spawned deferred on first arrival)
        node.ExtraDropUIDs = ParseStringList(dict, "ExtraDropUIDs");

        // Parse StripAllInheritedDrops flag (optional; strips inherited template drops)
        node.StripAllInheritedDrops = GetBool(dict, "StripAllInheritedDrops", false);

        // Parse StripLegacyBoardUIDs array (optional; clears stale inherited cards from old saves)
        node.StripLegacyBoardUIDs = ParseStringList(dict, "StripLegacyBoardUIDs");

        // Parse CapacityStats (optional; overrides SD1-4 on cloned CT8)
        node.CapacityStats = ParseCapacityStats(dict);

        // Parse ConnectionGates (optional; declarative perk/improvement gates)
        node.ConnectionGates = ParseConnectionGates(dict, modName, envUID);

        // Parse ConditionalDrops (optional; runtime-conditional board content)
        node.ConditionalDrops = ParseConditionalDrops(dict, modName);

        // Parse SealableGates (optional; negative/challenge gates)
        node.SealableGates = ParseSealableGates(dict, modName, envUID);

        // Parse VanillaExits array (optional; forward-only compass exits to vanilla envs, P2)
        node.VanillaExits = ParseVanillaExits(dict, modName, envUID);

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
            var v = CardinalStringToVector(s);
            if (v == null)
                Log.Warn($"WorldMapLoader: unknown TravelDirection '{s}' — expected North/South/East/West or an x/z object");
            return v;
        }

        if (raw is Dictionary<string, object> td)
            return new UnityEngine.Vector4(
                GetFloat(td, "x", 0f), GetFloat(td, "y", 0f), GetFloat(td, "z", 0f), GetFloat(td, "w", 0f));

        return null;
    }

    /// <summary>Converts a cardinal name (North/South/East/West, case-insensitive) to a unit
    /// vector (x=+1 East / −1 West, z=+1 North / −1 South). Null when not a cardinal.</summary>
    private static UnityEngine.Vector4? CardinalStringToVector(string s)
    {
        switch (s?.Trim().ToLowerInvariant())
        {
            case "north": return new UnityEngine.Vector4(0, 0, 1, 0);
            case "south": return new UnityEngine.Vector4(0, 0, -1, 0);
            case "east":  return new UnityEngine.Vector4(1, 0, 0, 0);
            case "west":  return new UnityEngine.Vector4(-1, 0, 0, 0);
            default:      return null;
        }
    }

    private static Dictionary<string, CapacityStatOverride> ParseCapacityStats(Dictionary<string, object> d)
    {
        if (!d.TryGetValue("CapacityStats", out var raw) || raw is not Dictionary<string, object> stats)
            return null;
        var result = new Dictionary<string, CapacityStatOverride>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in stats)
        {
            if (kv.Value is not Dictionary<string, object> od) continue;
            var ov = new CapacityStatOverride();
            if (od.TryGetValue("MaxValue",     out var mv)) ov.MaxValue     = ToFloat(mv);
            if (od.TryGetValue("InitialValue", out var iv)) ov.InitialValue = ToFloat(iv);
            if (od.TryGetValue("RatePerDTP",   out var rp)) ov.RatePerDTP   = ToFloat(rp);
            result[kv.Key] = ov;
        }
        return result.Count > 0 ? result : null;
    }

    private static List<ConnectionGateDefinition> ParseConnectionGates(
        Dictionary<string, object> d, string modName, string envUID)
    {
        if (!d.TryGetValue("ConnectionGates", out var raw) || raw is not List<object> arr) return null;
        var list = new List<ConnectionGateDefinition>(arr.Count);
        foreach (var item in arr)
        {
            if (item is not Dictionary<string, object> gd) continue;
            var connUID = GetString(gd, "ConnectionUID");
            if (string.IsNullOrEmpty(connUID))
            {
                Log.Warn($"WorldMapLoader: {modName} node '{envUID}' ConnectionGate missing ConnectionUID — skipped");
                continue;
            }
            var gate = new ConnectionGateDefinition
            {
                ConnectionUID     = connUID,
                GateLogic         = GetString(gd, "GateLogic") ?? "ANY",
                HideTravelDA      = GetBool(gd, "HideTravelDA", true),
                RestoreDAOnUnlock = GetBool(gd, "RestoreDAOnUnlock", false),
                NeighborCt8UID    = GetString(gd, "NeighborCt8UID"),
                Granularity       = GetString(gd, "Granularity") ?? "Env",
            };
            ReadGateConditions(gd, "GateConditions", gate.GateConditions);
            ReadGateConditions(gd, "LockConditions", gate.LockConditions);
            list.Add(gate);
        }
        return list.Count > 0 ? list : null;
    }

    private static void ReadGateConditions(
        Dictionary<string, object> gd, string key, List<GateConditionDefinition> into)
    {
        if (!gd.TryGetValue(key, out var raw) || raw is not List<object> conds) return;
        foreach (var ci in conds)
            if (ci is Dictionary<string, object> cd) into.Add(ParseGateCondition(cd));
    }

    private static GateConditionDefinition ParseGateCondition(Dictionary<string, object> cd) =>
        new()
        {
            Type       = GetString(cd, "Type"),
            UID        = GetString(cd, "UID"),
            EnvUID     = GetString(cd, "EnvUID"),
            Value      = GetFloat(cd, "Value", 0f),
            Comparison = GetString(cd, "Comparison") ?? "GreaterOrEqual",
        };

    private static List<ConditionalDropDefinition> ParseConditionalDrops(
        Dictionary<string, object> d, string modName)
    {
        if (!d.TryGetValue("ConditionalDrops", out var raw) || raw is not List<object> arr) return null;
        var list = new List<ConditionalDropDefinition>(arr.Count);
        foreach (var item in arr)
        {
            if (item is not Dictionary<string, object> dd) continue;
            var uid = GetString(dd, "UID");
            if (string.IsNullOrEmpty(uid)) { Log.Warn($"WorldMapLoader: {modName} ConditionalDrop missing UID — skipped"); continue; }

            var drop = new ConditionalDropDefinition
            {
                UID        = uid,
                MaxOnBoard = dd.TryGetValue("MaxOnBoard", out var mb) ? (int)ToFloat(mb) : 1,
                ForceStay  = GetBool(dd, "ForceStay", true),
            };

            if (dd.TryGetValue("Condition", out var rawCond) && rawCond is Dictionary<string, object> cd)
            {
                drop.Condition = new DropConditionDefinition
                {
                    Type     = GetString(cd, "Type") ?? "AlwaysTrue",
                    UID      = GetString(cd, "UID"),
                    EnvUID   = GetString(cd, "EnvUID"),
                    QuestUID = GetString(cd, "QuestUID"),
                    From     = cd.TryGetValue("From", out var cf) ? (int)ToFloat(cf) : 0,
                    To       = cd.TryGetValue("To",   out var ct) ? (int)ToFloat(ct) : 0,
                };
            }
            else
            {
                drop.Condition = new DropConditionDefinition { Type = "AlwaysTrue" };
            }
            list.Add(drop);
        }
        return list.Count > 0 ? list : null;
    }

    private static List<SealableGateDefinition> ParseSealableGates(
        Dictionary<string, object> d, string modName, string envUID)
    {
        if (!d.TryGetValue("SealableGates", out var raw) || raw is not List<object> arr) return null;
        var list = new List<SealableGateDefinition>(arr.Count);
        foreach (var item in arr)
        {
            if (item is not Dictionary<string, object> gd) continue;
            var connUID = GetString(gd, "ConnectionUID");
            var challengeUID = GetString(gd, "ChallengeCardUID");
            var clearKey = GetString(gd, "ClearActionKeyPrefix");
            var stateModel = GetString(gd, "StateModel") ?? "Marker";
            bool isCardPresence = "CardPresence".Equals(stateModel, StringComparison.OrdinalIgnoreCase);

            if (string.IsNullOrEmpty(connUID) || string.IsNullOrEmpty(challengeUID) || string.IsNullOrEmpty(clearKey))
            {
                Log.Warn($"WorldMapLoader: {modName} node '{envUID}' SealableGate missing a required field (ConnectionUID/ChallengeCardUID/ClearActionKeyPrefix) — skipped");
                continue;
            }

            var markerEnv = GetString(gd, "MarkerEnvUID");
            if (!isCardPresence && string.IsNullOrEmpty(markerEnv))
            {
                Log.Warn($"WorldMapLoader: {modName} node '{envUID}' SealableGate '{connUID}' (StateModel=Marker) missing required MarkerEnvUID — skipped");
                continue;
            }

            var gate = new SealableGateDefinition
            {
                ConnectionUID         = connUID,
                StateModel            = stateModel,
                Granularity           = GetString(gd, "Granularity") ?? "Env",
                NeighborCt8UID        = GetString(gd, "NeighborCt8UID"),
                ChallengeCardUID      = challengeUID,
                ClearActionKeyPrefix  = clearKey,
                MultiHit              = GetBool(gd, "MultiHit", false),
                ClearedTransformInto  = GetString(gd, "ClearedTransformInto"),
                MarkerUID             = GetString(gd, "MarkerUID") ?? (challengeUID + "_cleared"),
                MarkerEnvUID          = markerEnv,
                SeedOnEnvUIDs         = ParseStringList(gd, "SeedOnEnvUIDs") ?? new List<string>(),
            };

            if (gd.TryGetValue("SealTrigger", out var rawTrig) && rawTrig is Dictionary<string, object> td)
            {
                gate.SealTrigger = ParseGateCondition(td);
            }
            else
            {
                Log.Warn($"WorldMapLoader: {modName} node '{envUID}' SealableGate '{connUID}' missing SealTrigger — this gate will never seal");
            }

            if (isCardPresence)
            {
                gate.DirectionalGates = ParseDirectionalGates(gd, modName, envUID, connUID);
                if (gate.DirectionalGates == null || gate.DirectionalGates.Count == 0)
                {
                    Log.Warn($"WorldMapLoader: {modName} node '{envUID}' SealableGate '{connUID}' (StateModel=CardPresence) has no DirectionalGates — skipped");
                    continue;
                }
                if (string.IsNullOrEmpty(gate.ClearedTransformInto))
                    Log.Warn($"WorldMapLoader: {modName} node '{envUID}' SealableGate '{connUID}' (StateModel=CardPresence) has no ClearedTransformInto — sealed state can never be detected as open");
            }
            else if (gate.SeedOnEnvUIDs.Count == 0)
            {
                Log.Warn($"WorldMapLoader: {modName} node '{envUID}' SealableGate '{connUID}' has no SeedOnEnvUIDs — the challenge card will never be spawned");
            }

            if (gd.TryGetValue("ResealCondition", out var rawReseal) && rawReseal is Dictionary<string, object> rd)
            {
                gate.ResealCondition = new GateResealDefinition
                {
                    Type          = GetString(rd, "Type") ?? "TimerRegrowth",
                    Days          = rd.TryGetValue("Days", out var dv) ? (int)ToFloat(dv) : 0,
                    TransformInto = GetString(rd, "TransformInto") ?? challengeUID,
                };
            }

            list.Add(gate);
        }
        return list.Count > 0 ? list : null;
    }

    private static List<DirectionalGateSide> ParseDirectionalGates(
        Dictionary<string, object> gd, string modName, string envUID, string connUID)
    {
        if (!gd.TryGetValue("DirectionalGates", out var raw) || raw is not List<object> arr) return null;
        var list = new List<DirectionalGateSide>(arr.Count);
        foreach (var item in arr)
        {
            if (item is not Dictionary<string, object> sd) continue;
            var watchEnv = GetString(sd, "WatchEnvUID");
            if (string.IsNullOrEmpty(watchEnv))
            {
                Log.Warn($"WorldMapLoader: {modName} node '{envUID}' SealableGate '{connUID}' DirectionalGate missing WatchEnvUID — skipped");
                continue;
            }
            var side = new DirectionalGateSide
            {
                WatchEnvUID     = watchEnv,
                Ct8Uid          = GetString(sd, "Ct8Uid"),
                Direction       = sd.TryGetValue("Direction", out var dv) && dv is string ds
                    ? DirectionStringToIndex(ds) : null,
                ExcludeCt8Uids  = ParseStringList(sd, "ExcludeCt8Uids") ?? new List<string>(),
                ControlsMapLine = GetBool(sd, "ControlsMapLine", false),
            };
            if (!string.IsNullOrEmpty(side.Ct8Uid) && side.Direction == null)
                Log.Warn($"WorldMapLoader: {modName} node '{envUID}' SealableGate '{connUID}' DirectionalGate has Ct8Uid but no Direction — DA matching falls back to destination-match");
            list.Add(side);
        }
        return list.Count > 0 ? list : null;
    }

    private static int? DirectionStringToIndex(string s) => s?.Trim().ToLowerInvariant() switch
    {
        "north" => 0, "south" => 1, "east" => 2, "west" => 3, _ => null,
    };

    /// <summary>Parses the optional <c>VanillaExits</c> array on a node:
    /// <c>[{ "Direction": "East", "TargetVanillaEnvUID": "&lt;uid&gt;" }]</c>. Entries with a
    /// missing target or a non-cardinal direction are logged and skipped. Returns null when none.</summary>
    private static List<VanillaExitDefinition> ParseVanillaExits(Dictionary<string, object> d, string modName, string envUID)
    {
        if (!d.TryGetValue("VanillaExits", out var raw) || raw is not List<object> arr) return null;
        var list = new List<VanillaExitDefinition>(arr.Count);
        foreach (var item in arr)
        {
            if (item is not Dictionary<string, object> ed) continue;
            var target = GetString(ed, "TargetVanillaEnvUID");
            var dirStr = GetString(ed, "Direction");
            if (string.IsNullOrEmpty(target))
            {
                Log.Warn($"WorldMapLoader: {modName} node '{envUID}' VanillaExit missing TargetVanillaEnvUID — skipped");
                continue;
            }
            var dirVec = CardinalStringToVector(dirStr);
            if (dirVec == null)
            {
                Log.Warn($"WorldMapLoader: {modName} node '{envUID}' VanillaExit has invalid/missing Direction '{dirStr ?? "null"}' (need North/South/East/West) — skipped");
                continue;
            }
            list.Add(new VanillaExitDefinition
            {
                Direction = dirStr,
                TargetVanillaEnvUID = target,
                DirectionVector = dirVec,
                Bidirectional = GetBool(ed, "Bidirectional", false),
                TargetVanillaLocUID = GetString(ed, "TargetVanillaLocUID"),
                ReverseActionName = GetString(ed, "ReverseActionName"),
            });
        }
        return list.Count > 0 ? list : null;
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
        try { return Convert.ToSingle(v); }
        catch (Exception ex) { Log.Debug($"WorldMapLoader.ToFloat: conversion failed for value '{v}': {ex.GetType().Name} {ex.Message}"); return def; }
    }
}
