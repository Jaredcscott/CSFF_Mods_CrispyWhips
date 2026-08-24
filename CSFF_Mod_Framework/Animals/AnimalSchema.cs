namespace CSFFModFramework.Animals;

/// <summary>
/// Typed model of one Animals/&lt;Species&gt;.json manifest (schema v1) plus the MiniJson-dict →
/// model mapping. Schema reference: Documentation/Design/Animals_Schema.md.
///
/// <para>Milestone coverage: M1 (species card, HomeEnv spawn, activity window, Encounter Ref,
/// Approach) + M2 (Movement, CustomDuties, full ActivityWindow, Spawn lifecycle timers, Carcass,
/// Agent stat overrides / Ref-path stat map) + M3 (Tracks). Sections landing in later milestones are recorded
/// in <see cref="DeferredSections"/> so the loader can warn (never silently ignore) per file.
/// Keys starting with "//" are author comments and skipped everywhere.</para>
/// </summary>
internal sealed class AnimalManifest
{
    public const int SupportedSchemaVersion = 1;

    // Provenance
    public string SourceMod;
    public string SourceFile;

    // Top level
    public int SchemaVersion = -1;          // -1 = key missing
    public string SpeciesId;
    public string DisplayName;
    public string LocalizationKey;
    public string Sprite;
    public string WeightCategory;

    // Agent
    public string AgentRef;                 // escape hatch: hand-authored NPCAgent UID
    /// <summary>Ref path only: maps lifecycle roles to the hand-authored agent's NPCStat UIDs
    /// so framework-generated duties and the lifecycle ticker know which stats to gate/tick.
    /// Recognized keys: Exists, Blood, RespawnTimer, SuppressRespawnTimer.</summary>
    public Dictionary<string, string> AgentStatMap;
    // Generated path: per-agent overrides on the shared vanilla NPCStats (AgentBlood etc.).
    public StatOverride AgentBlood;
    public StatOverride AgentSatiation;
    public StatOverride AgentPoisonResistance;

    public sealed class StatOverride
    {
        public double? Start;
        public double? Max;
        public double? RatePerTick;
    }

    // Spawn (M1: HomeEnv; M2: lifecycle timers)
    public string HomeEnv;
    /// <summary>DTP ticks from the death (blood-depleted) state until the agent heals and is
    /// eligible to reappear. 0 = no kill-respawn timer (a combat-killed agent stays gone).</summary>
    public int DeathRespawnTicks;
    /// <summary>While a card with this UID is on the board (e.g. a tamed companion), the wild
    /// agent is kept retired (exists forced to 0). Enables the suppressed-respawn timer below.</summary>
    public string SuppressWhileCardOnBoard;
    /// <summary>DTP ticks after the suppressor card leaves the board until the wild agent's
    /// exists flag is restored (fresh wild spawn eligible). Only meaningful with
    /// SuppressWhileCardOnBoard. 0 = never respawns after suppression.</summary>
    public int SuppressedRespawnTicks;

    // ActivityWindow (M1: SpiritWorld roost; M2: on-stage roost + HiddenWhileRoosting)
    public bool HasActivityWindow;
    public int ActiveStart;
    public int ActiveEnd;
    public string Roost = "SpiritWorld";
    public bool HiddenWhileRoosting;

    // Movement (M2)
    public bool HasMovement;
    public List<string> WanderEnvs = new();
    public int WanderWeight = 1;
    public bool PlayerAttractionEnabled;
    public int PlayerAttractionBaseWeight;
    public bool HasPlayerAttractionDistanceWeight;
    public double PADistanceMin, PADistanceMax;
    public int PAWeightNear, PAWeightFar;
    public bool PAOnlyDuringActiveHours = true;
    /// <summary>"Pathfind" (default; one map edge per tick, wolf-style) or "Teleport"
    /// (instant, owl-style — matches the proven Wild Owl night-visit behavior).</summary>
    public string PAMovementType = "Pathfind";
    public bool FleeFromPlayer;

    // Tracks (M3) — compiled by TrackBuilder into NPCAgent.DefaultTracks (NPCTrackingSetup).
    /// <summary>True when the file carries a "Tracks" section at all. A manifest with NO Tracks
    /// section leaves a Ref-path agent's own authored DefaultTracks alone; a manifest WITH one is
    /// authoritative and overwrites it.</summary>
    public bool HasTracks;
    /// <summary>Tracks.Enabled — the species-wide master switch (→ DefaultTracks.Active AND the
    /// LeaveTracks flag on generated move actions). Defaults to true when the section exists.</summary>
    public bool TracksEnabled = true;
    /// <summary>CardData UID of the track card. Null/omitted = the vanilla DefaultTracksCard
    /// (the engine substitutes it when the record carries no override).</summary>
    public string TrackCard;
    /// <summary>DTP ticks a laid track stays discoverable, rolled per track INCLUSIVE of Max
    /// (NPCTrackingInfo.cs:70). Min must be >= 1 — a 0 lifetime means "never expires".</summary>
    public int TrackLifetimeMin = 32, TrackLifetimeMax = 48;
    /// <summary>Tracks.AddedDiscoverChance — flat percentage POINTS added to the global discovery
    /// curve, rolled per track between Min and Max. A bare number sets both.</summary>
    public double TrackAddedDiscoverMin, TrackAddedDiscoverMax;
    /// <summary>Tracks.RevealSkill — GameStat UID or runtime name whose value drives
    /// RevealSkillCurve. Null = vanilla Skill_Tracking.</summary>
    public string TrackRevealSkill;
    /// <summary>True when Tracks.RevealSkillCurve is present. The curve is ADDITIVE on top of the
    /// engine's global 50%-at-skill-0 → 100%-at-150 curve, so making a species harder to find than
    /// vanilla requires NEGATIVE ChanceRange values.</summary>
    public bool HasTrackRevealCurve;
    public double TrackRevealStatMin, TrackRevealStatMax;
    public double TrackRevealChanceMin, TrackRevealChanceMax;
    /// <summary>Tracks.BloodTrail — snapshot the agent's blood NPCStat onto the track card's
    /// Special1 at lay time (drives the vanilla card's "Blood Trail" AlternateName).</summary>
    public bool TrackBloodTrail;

    // Traps (M4) — compiled by TrapIntegrator into the vanilla traps' TriggerInteractors, the
    // agent's six trap NPCStats, and its "Interact with a Trap" catch action; the bait fields
    // additionally drive DutyBuilder's generated feed duty.
    /// <summary>True when the file carries a "Traps" section at all. No section = the species is
    /// invisible to every trap (it is never added to any vanilla TriggerInteractors list).</summary>
    public bool HasTraps;
    /// <summary>Traps.Enabled — species-wide master switch. Defaults true when the section exists.</summary>
    public bool TrapsEnabled = true;
    /// <summary>Traps.Wariness — base AgentTrapCunning 0-100, SUBTRACTED from the catch chance
    /// across all trap types (vanilla: hare 5, fox 30, wolf 25, bear 0).</summary>
    public double TrapWariness;
    /// <summary>Traps.PerType — per-trap-type AgentTrapCunning* override keyed by trap name
    /// (Snare | Deadfall | LogTrap | PitTrap). 0 = fully catchable, 10000 = hard immune. A trap
    /// type ABSENT from this map is treated as immune, so a species is only ever joined to the
    /// traps its author explicitly opted into.</summary>
    public Dictionary<string, double> TrapPerType = new(StringComparer.Ordinal);
    /// <summary>Traps.BaitTags / Traps.BaitCards — what the generated feed duty hunts for inside
    /// a baited trap. Tags are runtime CardTag names; cards are CardData UIDs. The trap-CONTAINER
    /// tag is never authored here — it is derived off the live vanilla trap cards at load,
    /// because the exported names are unstable Unity asset names.</summary>
    public List<string> BaitTags = new();
    public List<string> BaitCards = new();
    public bool HasBait => BaitTags.Count > 0 || BaitCards.Count > 0;
    /// <summary>Traps.Bait.DutyWeight — selection weight of the generated feed duty.</summary>
    public int FeedDutyWeight = 20;
    /// <summary>Traps.Bait.WhenSatiationBelow — the AgentSatiation level under which the species
    /// counts as hungry enough to go raid a baited trap. Default 75 matches vanilla's own feed
    /// duties (AgentDuty_Bird_EatHerbiOmniTiny et al. gate on AgentSatiation in [0, 75]).</summary>
    public double FeedHungerBelow = 75;
    /// <summary>Traps.CatchResults — AgentTrapType-gated drop sets. Each Drops entry becomes its
    /// OWN weighted collection so alive and carcass forms COMPETE rather than both dropping.</summary>
    public List<CatchResult> CatchResults = new();

    public sealed class CatchResult
    {
        public string TrapType;
        public List<CatchDrop> Drops = new();
    }

    public sealed class CatchDrop
    {
        public string Card;
        public int Weight = 1;
    }

    // Carcass (M2) — consumed by the generated lifecycle's death action (generated agents only;
    // Ref-path agents author their own death action's drops).
    public string CarcassCard;
    public List<CarcassDrop> CarcassExtraDrops = new();

    public sealed class CarcassDrop
    {
        public string Card;
        public int Min = 1, Max = 1;
    }

    // Encounter (M1 subset: Ref/ApproachButton; M5: full generation + Aggression)
    public bool ApproachButton = true;
    public string EncounterRef;
    /// <summary>Runtime BodyTemplate name (e.g. "Combat_ BTDuck", leading space is vanilla's).
    /// On the generated path this becomes Encounter.EnemyBodyTemplate; on the Ref path it OVERRIDES
    /// the referenced encounter's own BodyTemplate (schema: "When set, only BodyTemplate + Aggression
    /// below still apply" on the Ref path).</summary>
    public string EncounterBodyTemplate;
    public double EncounterBlood = 40, EncounterSize = 10;
    public double EncounterAwarenessMin = 25, EncounterAwarenessMax = 75;
    public double EncounterCoverMin = 60, EncounterCoverMax = 60;
    public double EncounterStealthMin, EncounterStealthMax;
    /// <summary>true = duck shape: every generated EnemyAction is DoesNotAttack (notice + escape
    /// only, no attack action ever emitted).</summary>
    public bool EncounterPassive = true;
    public bool EncounterForceFight;
    /// <summary>True when the manifest's Encounter section requests GENERATION (no Ref, and at
    /// least one real generation key beyond Ref/ApproachButton/Aggression). Mutually exclusive
    /// with EncounterRef in practice — the Ref path bypasses generation entirely.</summary>
    public bool HasEncounterGeneration;

    // Aggression (M5) — a generated night-attack-style duty via DutyBuilder's StartEncounter
    // action, targeting whichever Encounter this manifest resolves to (Ref or generated).
    public bool AggressionEnabled;
    public bool HasAggressionHours;
    public int AggressionStart, AggressionEnd;
    public int AggressionBaseWeight = 100;
    public bool AggressionRequirePlayerInEnv = true;
    public int AggressionMaxPerDay = 1;

    // Interactions (M6) — custom attempt-interactions (e.g. Tame). Compiled by
    // TameInteractionBuilder into NPCAgent.DragAndDropActions (BaitCards/BaitTags present —
    // drag-to-attempt, the proven owl vanilla path) or NPCAgent.DismantleActions (no bait —
    // click-button). Success/fail is resolved by the ENGINE's own native weighted
    // ProducedCards-collection selection (Mechanism B, the Track_EventBoar pattern) — no
    // custom C# roll. CompanionService watches OnSuccess.GiveCard to retire the wild agent
    // and init the spawned companion's zeroed stats.
    public List<Interaction> Interactions = new();

    public sealed class Interaction
    {
        public string Name;                 // [A-Za-z0-9_ ]+, unique per manifest; ActionName fallback
        public string LocalizationKey;
        public string ActionName;           // display text; defaults to Name
        public string ActionDescription;
        public int DaytimeCost = 1;

        // Delivery: BaitCards/BaitTags non-empty -> CardOnCardAction (drag bait onto the
        // agent); empty -> DismantleCardAction (click button, no item consumed).
        public List<string> BaitCards = new();
        public List<string> BaitTags = new();
        public bool HasBait => BaitCards.Count > 0 || BaitTags.Count > 0;

        // Success-chance roll: SuccessWeight (+ optional skill-scaled bonus) vs FailWeight,
        // resolved natively by the engine's own weighted-collection selection.
        public string Skill;                // GameStat uid or runtime name
        public int SuccessWeight = 1;
        public bool HasSkillBonus;
        public double SkillStatMin, SkillStatMax;
        public int SkillWeightMin, SkillWeightMax;
        public int FailWeight = 1;

        // OnSuccess
        public string GiveCard;
        public bool DespawnAgent = true;

        // OnFail
        public bool Flee;
        public double AttackChance;         // 0-100: fraction of fails that fire Encounter.Ref instead
    }

    // Companion (M6) — post-tame retirement/init service, keyed to the spawned companion
    // card. Compiled by CompanionService. Container-guard/auto-feed upkeep stays mod-side
    // (e.g. Sirus23's WolfTickPatch/CompanionContainerGuardPatch) — not yet generalized here.
    public string CompanionCard;
    public bool CompanionInitStatsOnSpawn = true;
    public bool HasCompanion => CompanionCard != null;

    // CustomDuties (M2)
    public List<CustomDuty> CustomDuties = new();

    public sealed class CustomDuty
    {
        public string Name;
        public int BaseWeight;
        public bool HasValidHours;
        public int ValidStart, ValidEnd;
        public List<DutyAction> Actions = new();
    }

    public sealed class DutyAction
    {
        public string Type;                 // Move | Wait | StartEncounter | AffectItems
        // Move
        public string Destination;          // Env | EnvTag | Player | Home
        public string Env;                  // Destination=Env: CT4 env UID
        public string Tag;                  // Destination=EnvTag: runtime CardTag name
        public string Selection = "Closest"; // Closest | Furthest | Random
        public string MovementType = "Pathfind"; // Pathfind | Teleport
        public bool LeaveTracks;
        public bool AwayFrom;
        // Wait
        public int TicksMin = 1, TicksMax = 1;
        // StartEncounter
        public string Encounter;
        public bool RequirePlayerEnv = true;
        // AffectItems (M4) — the feed/bait action. v1 supports the SimpleCardChange/Destroy shape
        // only, which is precisely what springs a trap: destroying bait routes through
        // GameManager.RemoveCard, which is the RemoveItemFromInventory trigger the traps listen for.
        public List<string> ItemTags = new();
        public List<string> ItemCards = new();
        /// <summary>"Destroy" (v1). Other CardStateChange ModTypes are validated out until a
        /// second consumer justifies them.</summary>
        public string Affect = "Destroy";
        /// <summary>Allow the search to reach inside the vanilla land traps (their container tag
        /// is derived live by TrapIntegrator). This is what makes the animal take BAIT.</summary>
        public bool InTrapContainers = true;
    }

    /// <summary>Recognized-but-not-yet-implemented sections found in the file, with the milestone
    /// that implements them. One Warn per file lists these.</summary>
    public List<string> DeferredSections = new();

    /// <summary>Unrecognized top-level keys (likely typos). One Warn per file lists these.</summary>
    public List<string> UnknownKeys = new();

    // ------------------------------------------------------------------ parsing ---

    private static readonly HashSet<string> TopLevelKeys = new(StringComparer.Ordinal)
    {
        "SchemaVersion", "SpeciesId", "DisplayName", "LocalizationKey", "Sprite", "WeightCategory",
        "Agent", "Spawn", "ActivityWindow", "Movement", "Tracks", "Traps", "Encounter", "Carcass",
        "Interactions", "Companion", "CustomDuties",
    };

    public static AnimalManifest FromDict(Dictionary<string, object> root, string sourceMod, string sourceFile)
    {
        var m = new AnimalManifest { SourceMod = sourceMod, SourceFile = sourceFile };

        foreach (var key in root.Keys)
        {
            if (key.StartsWith("//", StringComparison.Ordinal)) continue;
            if (!TopLevelKeys.Contains(key)) m.UnknownKeys.Add(key);
        }

        if (TryGetNumber(root, "SchemaVersion", out var ver)) m.SchemaVersion = (int)ver;
        m.SpeciesId       = GetString(root, "SpeciesId");
        m.DisplayName     = GetString(root, "DisplayName");
        m.LocalizationKey = GetString(root, "LocalizationKey");
        m.Sprite          = GetString(root, "Sprite");
        m.WeightCategory  = GetString(root, "WeightCategory");

        if (GetDict(root, "Agent") is { } agent)
        {
            m.AgentRef = GetString(agent, "Ref");
            if (GetDict(agent, "Stats") is { } statMap)
            {
                m.AgentStatMap = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var kv in statMap)
                {
                    if (kv.Key.StartsWith("//", StringComparison.Ordinal)) continue;
                    if (kv.Value is string uid && !string.IsNullOrEmpty(uid)) m.AgentStatMap[kv.Key] = uid;
                }
            }
            m.AgentBlood            = ParseStatOverride(GetDict(agent, "Blood"));
            m.AgentSatiation        = ParseStatOverride(GetDict(agent, "Satiation"));
            m.AgentPoisonResistance = ParseStatOverride(GetDict(agent, "PoisonResistance"));
        }

        if (GetDict(root, "Spawn") is { } spawn)
        {
            m.HomeEnv = GetString(spawn, "HomeEnv");
            if (TryGetNumber(spawn, "DeathRespawnTicks", out var drt)) m.DeathRespawnTicks = (int)drt;
            m.SuppressWhileCardOnBoard = GetString(spawn, "SuppressWhileCardOnBoard");
            if (TryGetNumber(spawn, "SuppressedRespawnTicks", out var srt)) m.SuppressedRespawnTicks = (int)srt;
            if (spawn.ContainsKey("Population"))          m.DeferredSections.Add("Spawn.Population (M4)");
            if (spawn.ContainsKey("WinterDespawn"))       m.DeferredSections.Add("Spawn.WinterDespawn (M4)");
            if (spawn.ContainsKey("InitialRespawnTicks")) m.DeferredSections.Add("Spawn.InitialRespawnTicks (M4)");
        }

        if (GetDict(root, "ActivityWindow") is { } window)
        {
            if (GetDict(window, "ActiveHours") is { } hours
                && TryGetNumber(hours, "Start", out var s) && TryGetNumber(hours, "End", out var e))
            {
                m.HasActivityWindow = true;
                m.ActiveStart = (int)s;
                m.ActiveEnd = (int)e;
            }
            m.Roost = GetString(window, "Roost") ?? "SpiritWorld";
            if (window.TryGetValue("HiddenWhileRoosting", out var hwr) && hwr is bool hwrVal)
                m.HiddenWhileRoosting = hwrVal;
        }

        if (GetDict(root, "Movement") is { } movement)
        {
            m.HasMovement = true;
            if (GetList(movement, "WanderEnvs") is { } wanderEnvs)
                foreach (var env in wanderEnvs)
                    if (env is string envUid && !string.IsNullOrEmpty(envUid)) m.WanderEnvs.Add(envUid);
            if (TryGetNumber(movement, "WanderWeight", out var ww)) m.WanderWeight = (int)ww;
            if (movement.TryGetValue("FleeFromPlayer", out var flee) && flee is bool fleeVal)
                m.FleeFromPlayer = fleeVal;
            if (GetDict(movement, "PlayerAttraction") is { } pa)
            {
                if (pa.TryGetValue("Enabled", out var en) && en is bool enVal) m.PlayerAttractionEnabled = enVal;
                if (TryGetNumber(pa, "BaseWeight", out var bw)) m.PlayerAttractionBaseWeight = (int)bw;
                if (pa.TryGetValue("OnlyDuringActiveHours", out var odah) && odah is bool odahVal)
                    m.PAOnlyDuringActiveHours = odahVal;
                m.PAMovementType = GetString(pa, "MovementType") ?? "Pathfind";
                if (GetDict(pa, "DistanceWeight") is { } dw
                    && GetList(dw, "Range") is { Count: 2 } range && range[0] is double r0 && range[1] is double r1
                    && GetList(dw, "Weights") is { Count: 2 } weights && weights[0] is double w0 && weights[1] is double w1)
                {
                    m.HasPlayerAttractionDistanceWeight = true;
                    m.PADistanceMin = r0; m.PADistanceMax = r1;
                    m.PAWeightNear = (int)w0; m.PAWeightFar = (int)w1;
                }
            }
        }

        if (GetDict(root, "Tracks") is { } tracks)
        {
            m.HasTracks = true;
            if (tracks.TryGetValue("Enabled", out var te) && te is bool teVal) m.TracksEnabled = teVal;
            m.TrackCard = GetString(tracks, "TrackCard");

            if (GetDict(tracks, "LifetimeTicks") is { } lifetime)
            {
                if (TryGetNumber(lifetime, "Min", out var lmin)) m.TrackLifetimeMin = (int)lmin;
                if (TryGetNumber(lifetime, "Max", out var lmax)) m.TrackLifetimeMax = (int)lmax;
            }

            // AddedDiscoverChance accepts a bare number (flat) or [min, max] (rolled per track).
            if (TryGetNumber(tracks, "AddedDiscoverChance", out var adc))
            {
                m.TrackAddedDiscoverMin = adc;
                m.TrackAddedDiscoverMax = adc;
            }
            else if (GetList(tracks, "AddedDiscoverChance") is { Count: 2 } adcRange
                     && adcRange[0] is double adc0 && adcRange[1] is double adc1)
            {
                m.TrackAddedDiscoverMin = adc0;
                m.TrackAddedDiscoverMax = adc1;
            }

            m.TrackRevealSkill = GetString(tracks, "RevealSkill");
            if (GetDict(tracks, "RevealSkillCurve") is { } curve)
            {
                var (defMin, defMax) = TrackBuilder.DefaultRevealStatRange;
                m.HasTrackRevealCurve = true;
                m.TrackRevealStatMin = defMin;
                m.TrackRevealStatMax = defMax;
                if (GetList(curve, "StatRange") is { Count: 2 } sr && sr[0] is double s0 && sr[1] is double s1)
                {
                    m.TrackRevealStatMin = s0;
                    m.TrackRevealStatMax = s1;
                }
                if (GetList(curve, "ChanceRange") is { Count: 2 } cr && cr[0] is double c0 && cr[1] is double c1)
                {
                    m.TrackRevealChanceMin = c0;
                    m.TrackRevealChanceMax = c1;
                }
            }

            if (tracks.TryGetValue("BloodTrail", out var bt) && bt is bool btVal) m.TrackBloodTrail = btVal;
        }

        if (GetDict(root, "Carcass") is { } carcass)
        {
            m.CarcassCard = GetString(carcass, "Card");
            if (GetList(carcass, "ExtraDrops") is { } extras)
            {
                foreach (var entry in extras)
                {
                    if (entry is not Dictionary<string, object> drop) continue;
                    var cd = new CarcassDrop { Card = GetString(drop, "Card") };
                    if (GetList(drop, "Amount") is { Count: 2 } amt && amt[0] is double a0 && amt[1] is double a1)
                    {
                        cd.Min = (int)a0;
                        cd.Max = (int)a1;
                    }
                    m.CarcassExtraDrops.Add(cd);
                }
            }
        }

        if (GetDict(root, "Encounter") is { } enc)
        {
            m.EncounterRef = GetString(enc, "Ref");
            if (enc.TryGetValue("ApproachButton", out var ab) && ab is bool abVal) m.ApproachButton = abVal;

            m.EncounterBodyTemplate = GetString(enc, "BodyTemplate");
            if (TryGetNumber(enc, "Blood", out var eBlood)) m.EncounterBlood = eBlood;
            if (TryGetNumber(enc, "Size", out var eSize)) m.EncounterSize = eSize;
            if (GetList(enc, "Awareness") is { Count: 2 } awareness
                && awareness[0] is double awMin && awareness[1] is double awMax)
            { m.EncounterAwarenessMin = awMin; m.EncounterAwarenessMax = awMax; }
            if (GetList(enc, "Cover") is { Count: 2 } cover
                && cover[0] is double coMin && cover[1] is double coMax)
            { m.EncounterCoverMin = coMin; m.EncounterCoverMax = coMax; }
            if (GetList(enc, "Stealth") is { Count: 2 } stealth
                && stealth[0] is double stMin && stealth[1] is double stMax)
            { m.EncounterStealthMin = stMin; m.EncounterStealthMax = stMax; }
            if (enc.TryGetValue("Passive", out var pv) && pv is bool pvVal) m.EncounterPassive = pvVal;
            if (enc.TryGetValue("ForceFight", out var ff) && ff is bool ffVal) m.EncounterForceFight = ffVal;

            if (GetDict(enc, "Aggression") is { } agg)
            {
                if (agg.TryGetValue("Enabled", out var aEn) && aEn is bool aEnVal) m.AggressionEnabled = aEnVal;
                if (GetDict(agg, "Hours") is { } aHours
                    && TryGetNumber(aHours, "Start", out var aStart) && TryGetNumber(aHours, "End", out var aEnd))
                {
                    m.HasAggressionHours = true;
                    m.AggressionStart = (int)aStart;
                    m.AggressionEnd = (int)aEnd;
                }
                if (TryGetNumber(agg, "BaseWeight", out var aWeight)) m.AggressionBaseWeight = (int)aWeight;
                if (agg.TryGetValue("RequirePlayerInEnv", out var rpe) && rpe is bool rpeVal) m.AggressionRequirePlayerInEnv = rpeVal;
                if (TryGetNumber(agg, "MaxPerDay", out var aMaxPerDay)) m.AggressionMaxPerDay = (int)aMaxPerDay;
            }

            // Generation is requested whenever there's no Ref and at least one real key beyond
            // Ref/ApproachButton/Aggression (Aggression alone with no other generation key and no
            // Ref is a validator error, not an implicit generation request -- there'd be nothing
            // to generate an encounter FROM).
            if (m.EncounterRef == null && HasRealKeysBeyond(enc, "Ref", "ApproachButton", "Aggression"))
                m.HasEncounterGeneration = true;
        }

        if (GetList(root, "CustomDuties") is { } customDuties)
        {
            foreach (var entry in customDuties)
            {
                if (entry is not Dictionary<string, object> dutyDict) continue;
                var duty = new CustomDuty { Name = GetString(dutyDict, "Name") };
                if (TryGetNumber(dutyDict, "BaseWeight", out var dbw)) duty.BaseWeight = (int)dbw;
                if (GetDict(dutyDict, "ValidHours") is { } vh
                    && TryGetNumber(vh, "Start", out var vs) && TryGetNumber(vh, "End", out var ve))
                {
                    duty.HasValidHours = true;
                    duty.ValidStart = (int)vs;
                    duty.ValidEnd = (int)ve;
                }
                if (GetList(dutyDict, "Actions") is { } actions)
                {
                    foreach (var actionEntry in actions)
                    {
                        if (actionEntry is not Dictionary<string, object> a) continue;
                        var da = new DutyAction { Type = GetString(a, "Type") };
                        da.Destination = GetString(a, "Destination");
                        da.Env = GetString(a, "Env");
                        da.Tag = GetString(a, "Tag");
                        da.Selection = GetString(a, "Selection") ?? "Closest";
                        da.MovementType = GetString(a, "MovementType") ?? "Pathfind";
                        if (a.TryGetValue("LeaveTracks", out var lt) && lt is bool ltVal) da.LeaveTracks = ltVal;
                        if (a.TryGetValue("AwayFrom", out var af) && af is bool afVal) da.AwayFrom = afVal;
                        if (TryGetNumber(a, "Ticks", out var ticks)) { da.TicksMin = (int)ticks; da.TicksMax = (int)ticks; }
                        if (GetList(a, "TicksRange") is { Count: 2 } tr && tr[0] is double t0 && tr[1] is double t1)
                        {
                            da.TicksMin = (int)t0;
                            da.TicksMax = (int)t1;
                        }
                        da.Encounter = GetString(a, "Encounter");
                        if (a.TryGetValue("RequirePlayerEnv", out var rpe) && rpe is bool rpeVal) da.RequirePlayerEnv = rpeVal;
                        if (GetList(a, "ItemTags") is { } itemTags)
                            foreach (var t in itemTags)
                                if (t is string tg && !string.IsNullOrEmpty(tg)) da.ItemTags.Add(tg);
                        if (GetList(a, "ItemCards") is { } itemCards)
                            foreach (var c in itemCards)
                                if (c is string cd && !string.IsNullOrEmpty(cd)) da.ItemCards.Add(cd);
                        da.Affect = GetString(a, "Affect") ?? "Destroy";
                        if (a.TryGetValue("InTrapContainers", out var itc) && itc is bool itcVal) da.InTrapContainers = itcVal;
                        duty.Actions.Add(da);
                    }
                }
                m.CustomDuties.Add(duty);
            }
        }

        if (GetDict(root, "Traps") is { } traps)
        {
            m.HasTraps = true;
            if (traps.TryGetValue("Enabled", out var tre) && tre is bool treVal) m.TrapsEnabled = treVal;
            if (TryGetNumber(traps, "Wariness", out var war)) m.TrapWariness = war;

            if (GetDict(traps, "PerType") is { } perType)
                foreach (var kv in perType)
                {
                    if (kv.Key.StartsWith("//", StringComparison.Ordinal)) continue;
                    if (kv.Value is double cunning) m.TrapPerType[kv.Key] = cunning;
                }

            if (GetList(traps, "BaitTags") is { } baitTags)
                foreach (var t in baitTags)
                    if (t is string tag && !string.IsNullOrEmpty(tag)) m.BaitTags.Add(tag);
            if (GetList(traps, "BaitCards") is { } baitCards)
                foreach (var c in baitCards)
                    if (c is string card && !string.IsNullOrEmpty(card)) m.BaitCards.Add(card);

            if (GetDict(traps, "Bait") is { } bait)
            {
                if (TryGetNumber(bait, "DutyWeight", out var fdw)) m.FeedDutyWeight = (int)fdw;
                if (TryGetNumber(bait, "WhenSatiationBelow", out var fhb)) m.FeedHungerBelow = fhb;
            }

            if (GetList(traps, "CatchResults") is { } catchResults)
            {
                foreach (var entry in catchResults)
                {
                    if (entry is not Dictionary<string, object> cr) continue;
                    var result = new CatchResult { TrapType = GetString(cr, "TrapType") };
                    if (GetList(cr, "Drops") is { } drops)
                    {
                        foreach (var d in drops)
                        {
                            if (d is not Dictionary<string, object> dropDict) continue;
                            var drop = new CatchDrop { Card = GetString(dropDict, "Card") };
                            if (TryGetNumber(dropDict, "Weight", out var dw)) drop.Weight = (int)dw;
                            result.Drops.Add(drop);
                        }
                    }
                    m.CatchResults.Add(result);
                }
            }
        }

        if (GetList(root, "Interactions") is { } interactions)
        {
            foreach (var entry in interactions)
            {
                if (entry is not Dictionary<string, object> ix) continue;
                var interaction = new Interaction
                {
                    Name = GetString(ix, "Name"),
                    LocalizationKey = GetString(ix, "LocalizationKey"),
                    ActionName = GetString(ix, "ActionName"),
                    ActionDescription = GetString(ix, "ActionDescription"),
                    Skill = GetString(ix, "Skill"),
                };
                if (TryGetNumber(ix, "DaytimeCost", out var dtc)) interaction.DaytimeCost = (int)dtc;
                if (TryGetNumber(ix, "SuccessWeight", out var sw)) interaction.SuccessWeight = (int)sw;
                if (TryGetNumber(ix, "FailWeight", out var fw)) interaction.FailWeight = (int)fw;

                if (GetList(ix, "BaitCards") is { } baitCards)
                    foreach (var c in baitCards)
                        if (c is string cs && !string.IsNullOrEmpty(cs)) interaction.BaitCards.Add(cs);
                if (GetList(ix, "BaitTags") is { } baitTags)
                    foreach (var t in baitTags)
                        if (t is string ts && !string.IsNullOrEmpty(ts)) interaction.BaitTags.Add(ts);

                if (GetDict(ix, "SkillBonus") is { } bonus)
                {
                    interaction.HasSkillBonus = true;
                    if (GetList(bonus, "StatRange") is { Count: 2 } sr && sr[0] is double s0 && sr[1] is double s1)
                    {
                        interaction.SkillStatMin = s0;
                        interaction.SkillStatMax = s1;
                    }
                    if (GetList(bonus, "WeightRange") is { Count: 2 } wr && wr[0] is double w0 && wr[1] is double w1)
                    {
                        interaction.SkillWeightMin = (int)w0;
                        interaction.SkillWeightMax = (int)w1;
                    }
                }

                if (GetDict(ix, "OnSuccess") is { } onSuccess)
                {
                    interaction.GiveCard = GetString(onSuccess, "GiveCard");
                    if (onSuccess.TryGetValue("DespawnAgent", out var da) && da is bool daVal)
                        interaction.DespawnAgent = daVal;
                }
                if (GetDict(ix, "OnFail") is { } onFail)
                {
                    if (onFail.TryGetValue("Flee", out var fl) && fl is bool flVal) interaction.Flee = flVal;
                    if (TryGetNumber(onFail, "AttackChance", out var ac)) interaction.AttackChance = ac;
                }

                m.Interactions.Add(interaction);
            }
        }

        if (GetDict(root, "Companion") is { } companion)
        {
            m.CompanionCard = GetString(companion, "Card");
            if (companion.TryGetValue("InitStatsOnSpawn", out var iss) && iss is bool issVal)
                m.CompanionInitStatsOnSpawn = issVal;
        }

        return m;
    }

    private static StatOverride ParseStatOverride(Dictionary<string, object> d)
    {
        if (d == null) return null;
        var o = new StatOverride();
        if (TryGetNumber(d, "Start", out var s)) o.Start = s;
        if (TryGetNumber(d, "Max", out var x)) o.Max = x;
        if (TryGetNumber(d, "RatePerTick", out var r)) o.RatePerTick = r;
        return o;
    }

    // MiniJson typing: objects are Dictionary<string,object>, arrays List<object>,
    // numbers boxed double, plus string/bool/null.

    private static string GetString(Dictionary<string, object> d, string key)
        => d.TryGetValue(key, out var v) && v is string s && !string.IsNullOrEmpty(s) ? s : null;

    private static bool TryGetNumber(Dictionary<string, object> d, string key, out double value)
    {
        if (d.TryGetValue(key, out var v) && v is double n) { value = n; return true; }
        value = 0;
        return false;
    }

    private static Dictionary<string, object> GetDict(Dictionary<string, object> d, string key)
        => d.TryGetValue(key, out var v) ? v as Dictionary<string, object> : null;

    private static List<object> GetList(Dictionary<string, object> d, string key)
        => d.TryGetValue(key, out var v) ? v as List<object> : null;

    private static bool HasRealKeysBeyond(Dictionary<string, object> d, params string[] known)
        => d.Keys.Any(k => !k.StartsWith("//", StringComparison.Ordinal) && !known.Contains(k, StringComparer.Ordinal));
}
