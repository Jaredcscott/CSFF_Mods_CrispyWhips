namespace CSFFModFramework.Discovery;

/// <summary>
/// Optional explicit asset declarations in <c>ModInfo.json</c>. Each list holds
/// paths relative to the mod folder. A path ending with <c>/</c> is treated as
/// a directory and scanned recursively for the asset kind's default extensions;
/// otherwise it is a literal file. When a list is null/empty the loader falls
/// back to the legacy <c>Resource/*</c> directory scan for that asset kind.
/// </summary>
[Serializable]
internal class AssetsManifest
{
    // CS0649: fields populated by JsonUtility.FromJsonOverwrite via reflection
#pragma warning disable 0649
    public List<string> Sprites;
    public List<string> Audio;
    public List<string> AssetBundles;
    public List<string> Gifs;
    public List<string> MapCaches;
#pragma warning restore 0649

    public bool HasAny =>
        (Sprites != null && Sprites.Count > 0) ||
        (Audio != null && Audio.Count > 0) ||
        (AssetBundles != null && AssetBundles.Count > 0) ||
        (Gifs != null && Gifs.Count > 0) ||
        (MapCaches != null && MapCaches.Count > 0);
}

internal class ModManifest
{
    // CS0649: fields are populated by JsonUtility.FromJsonOverwrite via reflection
#pragma warning disable 0649
    public string Name;
    public string Author;
    public string Version;
    public string Description;
    public AssetsManifest Assets;
    // Pikachu ModLoader manifest field (typo is the game ecosystem's own spelling).
    // Framework-native mods must NOT declare it; its presence marks a mod as
    // authored for ModLoader/ModCore. See ModDiscovery's coexistence skip.
    public string ModLoaderVerison;
#pragma warning restore 0649
    public string DirectoryPath; // Set at runtime, not from JSON
    public bool Enabled = true;

    /// <summary>True when the manifest declares the ModLoader/ModCore <c>ModLoaderVerison</c> field.</summary>
    public bool IsModLoaderNative => !string.IsNullOrWhiteSpace(ModLoaderVerison);

    /// <summary>
    /// True when the mod ships at least one declarative manifest that only the CSFF
    /// framework's LoadOrchestrator reads — Pikachu ModLoader/ModCore's own loader never
    /// parses <c>BlueprintTabs.json</c>, <c>SmeltingRecipes.json</c>, <c>DropInjections.json</c>,
    /// <c>InjectImprovementInto.json</c>, <c>WorldMap/MapNodes.json</c>/<c>FullMap.json</c>,
    /// <c>EncounterGuards/*.json</c>, <c>Quests.json</c>, <c>Characters.json</c>,
    /// <c>WorldMap/HubPortals.json</c>, <c>MapMod.json</c>, <c>Animals/*.json</c>,
    /// <c>TradingValues.json</c>, or a <c>GameSourceModify/</c> patch using the framework's
    /// bulk-match extension (<c>MatchTagWarpData</c>/<c>MatchTypeWarpData</c> — Pikachu's own
    /// GameSourceModify only supports single-UID targeting). Some mods are exported by the
    /// ModEditor tool with <see cref="ModLoaderVerison"/> stamped in regardless of which loader
    /// they actually target; when one of these framework-exclusive files is present, the mod is
    /// framework-format content mistagged as ModLoader-native. See ModDiscovery.DiscoverMods,
    /// which uses this to override the ModLoaderVerison coexistence skip.
    /// </summary>
    public bool HasFrameworkOnlyMarkers =>
        HasBlueprintTabs || HasSmeltingRecipes || HasDropInjections || HasImprovementInjections ||
        HasWorldMapNodes || HasEncounterGuards || HasQuestManifest || HasCharacterManifest ||
        HasHubPortals || HasMapMod || HasAnimals || HasTradingValues || HasGSMTagOrTypeMatch;

    // ── Feature flags (populated by ModDiscovery after JSON parse; never deserialized) ──
    // These let LoadOrchestrator skip phases whose content isn't present in any mod.
    [NonSerialized] public bool HasSpriteFiles;
    [NonSerialized] public bool HasAssetBundles;       // Resource/*.ab
    [NonSerialized] public bool HasLocalization;       // Localization/*.csv
    [NonSerialized] public bool HasBlueprintTabs;      // BlueprintTabs.json
    [NonSerialized] public bool HasSmeltingRecipes;    // SmeltingRecipes.json
    [NonSerialized] public bool HasAudio;              // Resource/Audio/ (any file)
    [NonSerialized] public bool HasMapCaches;          // Data/MapCaches/*.json, Data/*Map*.json, or Assets.MapCaches
    [NonSerialized] public bool HasTriggers;           // CardData/Trigger/*.json
    [NonSerialized] public bool HasGifContent;         // CardData/Gif/*.json
    [NonSerialized] public bool HasGSMTagOrTypeMatch;  // GameSourceModify/ uses MatchTagWarpData/MatchTypeWarpData
    [NonSerialized] public bool HasDropInjections;      // DropInjections.json
    [NonSerialized] public bool HasImprovementInjections; // InjectImprovementInto.json
    [NonSerialized] public bool HasWorldMapNodes;       // WorldMap/MapNodes.json or WorldMap/FullMap.json
    [NonSerialized] public bool HasFullMap;             // WorldMap/FullMap.json (full map replacement)
    [NonSerialized] public bool HasEncounterGuards;     // EncounterGuards/*.json
    [NonSerialized] public bool HasQuestManifest;       // Quests.json
    [NonSerialized] public bool HasCharacterManifest;   // Characters.json
    [NonSerialized] public bool HasHubPortals;          // WorldMap/HubPortals.json
    [NonSerialized] public bool HasMapMod;              // MapMod.json (portal world registration)
    [NonSerialized] public bool HasAnimals;             // Animals/*.json (declarative animal species)
    [NonSerialized] public bool HasTradingValues;       // TradingValues.json (bulk CardData.TradingValue repricing)

    public static ModManifest FromJson(string json, string directoryPath)
    {
        var manifest = new ModManifest();
        JsonUtility.FromJsonOverwrite(json, manifest);
        manifest.DirectoryPath = directoryPath;
        return manifest;
    }
}
