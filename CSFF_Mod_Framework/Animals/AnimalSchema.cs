namespace CSFFModFramework.Animals;

/// <summary>
/// Typed model of one Animals/&lt;Species&gt;.json manifest (schema v1) plus the MiniJson-dict →
/// model mapping. Schema reference: Documentation/Design/Animals_Schema.md.
///
/// <para>Milestone coverage: this model carries the full v1 surface, but only the M1 subset is
/// materialized into fields; sections that land in later milestones are recorded in
/// <see cref="DeferredSections"/> so the loader can warn (never silently ignore) per file.
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

    // Spawn (M1 subset)
    public string HomeEnv;

    // ActivityWindow (M1 subset)
    public bool HasActivityWindow;
    public int ActiveStart;
    public int ActiveEnd;
    public string Roost = "SpiritWorld";

    // Encounter (M1 subset)
    public bool ApproachButton = true;
    public string EncounterRef;

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
            if (HasRealKeysBeyond(agent, "Ref"))
                m.DeferredSections.Add("Agent stat overrides (M2)");
        }

        if (GetDict(root, "Spawn") is { } spawn)
        {
            m.HomeEnv = GetString(spawn, "HomeEnv");
            if (HasRealKeysBeyond(spawn, "HomeEnv"))
                m.DeferredSections.Add("Spawn lifecycle/population (M2)");
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
            if (window.ContainsKey("HiddenWhileRoosting"))
                m.DeferredSections.Add("ActivityWindow.HiddenWhileRoosting (M2)");
        }

        if (GetDict(root, "Encounter") is { } enc)
        {
            m.EncounterRef = GetString(enc, "Ref");
            if (enc.TryGetValue("ApproachButton", out var ab) && ab is bool abVal) m.ApproachButton = abVal;
            if (HasRealKeysBeyond(enc, "Ref", "ApproachButton"))
                m.DeferredSections.Add("Encounter generation/Aggression (M5)");
        }

        if (root.ContainsKey("Movement"))     m.DeferredSections.Add("Movement (M2)");
        if (root.ContainsKey("Carcass"))      m.DeferredSections.Add("Carcass (M2)");
        if (root.ContainsKey("CustomDuties")) m.DeferredSections.Add("CustomDuties (M2)");
        if (root.ContainsKey("Tracks"))       m.DeferredSections.Add("Tracks (M3)");
        if (root.ContainsKey("Traps"))        m.DeferredSections.Add("Traps (M4)");
        if (root.ContainsKey("Interactions")) m.DeferredSections.Add("Interactions/Tame (M6)");

        return m;
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

    private static bool HasRealKeysBeyond(Dictionary<string, object> d, params string[] known)
        => d.Keys.Any(k => !k.StartsWith("//", StringComparison.Ordinal) && !known.Contains(k, StringComparer.Ordinal));
}
