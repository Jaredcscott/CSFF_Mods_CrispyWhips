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
#pragma warning restore 0649

    public bool HasAny =>
        (Sprites != null && Sprites.Count > 0) ||
        (Audio != null && Audio.Count > 0) ||
        (AssetBundles != null && AssetBundles.Count > 0) ||
        (Gifs != null && Gifs.Count > 0);
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
#pragma warning restore 0649
    public string DirectoryPath; // Set at runtime, not from JSON
    public bool Enabled = true;

    // ── Feature flags (populated by ModDiscovery after JSON parse; never deserialized) ──
    // These let LoadOrchestrator skip phases whose content isn't present in any mod.
    [NonSerialized] public bool HasSpriteFiles;
    [NonSerialized] public bool HasAssetBundles;       // Resource/*.ab
    [NonSerialized] public bool HasLocalization;       // Localization/*.csv
    [NonSerialized] public bool HasBlueprintTabs;      // BlueprintTabs.json
    [NonSerialized] public bool HasSmeltingRecipes;    // SmeltingRecipes.json
    [NonSerialized] public bool HasAudio;              // Resource/Audio/ (any file)

    public static ModManifest FromJson(string json, string directoryPath)
    {
        var manifest = new ModManifest();
        JsonUtility.FromJsonOverwrite(json, manifest);
        manifest.DirectoryPath = directoryPath;
        return manifest;
    }
}
