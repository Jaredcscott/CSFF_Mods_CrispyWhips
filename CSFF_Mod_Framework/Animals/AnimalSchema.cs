namespace CSFFModFramework.Animals;

/// <summary>
/// Typed model of one Animals/&lt;Species&gt;.json manifest (schema v1) plus the MiniJson-dict →
/// model mapping. Schema reference: Documentation/Design/Animals_Schema.md.
///
/// <para>Milestone coverage: M1 (species card, HomeEnv spawn, activity window, Encounter Ref,
/// Approach) + M2 (Movement, CustomDuties, full ActivityWindow, Spawn lifecycle timers, Carcass,
/// Agent stat overrides / Ref-path stat map). Sections landing in later milestones are recorded
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

    // Carcass (M2) — consumed by the generated lifecycle's death action (generated agents only;
    // Ref-path agents author their own death action's drops).
    public string CarcassCard;
    public List<CarcassDrop> CarcassExtraDrops = new();

    public sealed class CarcassDrop
    {
        public string Card;
        public int Min = 1, Max = 1;
    }

    // Encounter (M1 subset)
    public bool ApproachButton = true;
    public string EncounterRef;

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
        public string Type;                 // Move | Wait | StartEncounter (AffectItems lands M4)
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
        "Interactions", "CustomDuties",
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
            if (HasRealKeysBeyond(enc, "Ref", "ApproachButton"))
                m.DeferredSections.Add("Encounter generation/Aggression (M5)");
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
                        duty.Actions.Add(da);
                    }
                }
                m.CustomDuties.Add(duty);
            }
        }

        if (root.ContainsKey("Tracks"))       m.DeferredSections.Add("Tracks (M3)");
        if (root.ContainsKey("Traps"))        m.DeferredSections.Add("Traps (M4)");
        if (root.ContainsKey("Interactions")) m.DeferredSections.Add("Interactions/Tame (M6)");

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
