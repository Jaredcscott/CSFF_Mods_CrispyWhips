using CSFFModFramework.Util;

namespace CSFFModFramework.Discovery;

internal static class ModDiscovery
{
    public static List<ModManifest> DiscoverMods()
    {
        var mods = new List<ModManifest>();
        var frameworkDir = PathUtil.FrameworkDir;
        var skippedForLoader = new List<string>();
        var reclaimedFromLoader = new List<string>();

        foreach (var dir in PathUtil.GetModDirectories())
        {
            // Skip the framework's own directory
            if (string.Equals(Path.GetFullPath(dir), Path.GetFullPath(frameworkDir),
                    StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                var jsonPath = Path.Combine(dir, "ModInfo.json");
                var json = File.ReadAllText(jsonPath);
                var manifest = ModManifest.FromJson(json, dir);

                if (string.IsNullOrEmpty(manifest.Name))
                    manifest.Name = Path.GetFileName(dir);

                // Probe features before the coexistence decision below — it needs
                // HasFrameworkOnlyMarkers, and LoadOrchestrator needs the rest regardless.
                ProbeFeatures(manifest);

                // Coexistence rule: Pikachu ModLoader/ModCore load every plugins
                // folder that has a ModInfo.json themselves (inside a
                // UniqueIDScriptable.ClearDict prefix at boot). Mods authored for
                // them carry the ModLoaderVerison manifest field — when one of
                // those loaders is installed it owns such mods, and the framework
                // must not load them a second time: duplicate instances split
                // reference identity (blueprint research resets on save load,
                // RequiredCard slots reject crafted items). Without a Pikachu
                // loader installed, the framework loads them as before.
                //
                // Exception: some mods are exported by the ModEditor tool with
                // ModLoaderVerison stamped in regardless of which loader they
                // actually target. When the mod ships framework-exclusive
                // declarative content (BlueprintTabs.json, InjectImprovementInto.json,
                // a GameSourceModify/ patch using MatchTagWarpData/MatchTypeWarpData,
                // etc. — see ModManifest.HasFrameworkOnlyMarkers), it is framework
                // format content mistagged as ModLoader-native; that content only
                // works if the framework loads and processes it. Load it anyway —
                // ForeignInstanceReconciler already neutralizes the duplicate
                // UniqueIDScriptable instances ModLoader creates for it.
                if (manifest.IsModLoaderNative && PikachuLoaderName != null)
                {
                    if (!manifest.HasFrameworkOnlyMarkers)
                    {
                        skippedForLoader.Add(manifest.Name);
                        continue;
                    }
                    reclaimedFromLoader.Add(manifest.Name);
                }

                mods.Add(manifest);
                Log.Debug($"Discovered mod: {manifest.Name} v{manifest.Version} by {manifest.Author} @ {dir}");
            }
            catch (Exception ex)
            {
                Log.Warn($"Failed to parse ModInfo.json in {dir}: {Log.ExceptionText(ex)}");
            }
        }

        if (skippedForLoader.Count > 0)
            Log.Info($"Skipping {skippedForLoader.Count} ModLoader-native mod(s) owned by installed {PikachuLoaderName}: "
                   + string.Join(", ", skippedForLoader));

        if (reclaimedFromLoader.Count > 0)
            Log.Info($"Loading {reclaimedFromLoader.Count} mod(s) via framework despite a ModLoaderVerison tag "
                   + $"(ships framework-only content such as BlueprintTabs.json): "
                   + string.Join(", ", reclaimedFromLoader));

        // Deduplicate mods with the same Name (e.g. stale debug folder + deploy folder).
        // Picks the folder with MORE content (JSON file count across well-known dirs) as primary.
        // Falls back to newest-mtime only when content counts tie. This prevents silent data loss
        // when a user hand-edits an old stale folder — its mtime is newer, but it's missing newer
        // content (e.g. SpiceTag/ added after the stale copy was made). Picking by content count
        // makes that scenario surface the complete deployment instead of quietly losing SpiceTags.
        var deduped = new List<ModManifest>();
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in mods)
        {
            if (seen.TryGetValue(mod.Name, out int idx))
            {
                var existing = deduped[idx];
                int existingCount = CountContentFiles(existing.DirectoryPath);
                int currentCount  = CountContentFiles(mod.DirectoryPath);

                // Prefer more content; on tie, prefer newer mtime.
                bool currentWins = currentCount > existingCount
                    || (currentCount == existingCount
                        && Directory.GetLastWriteTimeUtc(mod.DirectoryPath)
                           > Directory.GetLastWriteTimeUtc(existing.DirectoryPath));

                string keepDir, discardDir;
                int keepCount, discardCount;
                if (currentWins)
                {
                    keepDir = mod.DirectoryPath;      keepCount = currentCount;
                    discardDir = existing.DirectoryPath; discardCount = existingCount;
                    deduped[idx] = mod;
                }
                else
                {
                    keepDir = existing.DirectoryPath; keepCount = existingCount;
                    discardDir = mod.DirectoryPath;   discardCount = currentCount;
                }

                Log.Warn($"Duplicate mod '{mod.Name}' detected. Using {keepDir} ({keepCount} content files); " +
                         $"ignoring {discardDir} ({discardCount} content files). " +
                         "Remove the stale folder from BepInEx/plugins — runtime merge was intentionally disabled " +
                         "for startup performance.");
            }
            else
            {
                seen[mod.Name] = deduped.Count;
                deduped.Add(mod);
            }
        }

        deduped.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        // Prepend a synthetic manifest for the framework itself so JsonDataLoader picks up
        // framework-owned content (CardData/Hub/, CharacterPerk/, Localization/) from the
        // framework directory. The framework dir is intentionally skipped by the main loop
        // above (it has no ModInfo.json for the user-mod contract), but its JSON must load.
        var fwManifest = new ModManifest { Name = "CSFFModFramework", DirectoryPath = frameworkDir };
        ProbeFeatures(fwManifest);
        deduped.Insert(0, fwManifest);

        Log.Debug($"Discovered {deduped.Count - 1} mod(s) + framework (total slots: {deduped.Count})");
        return deduped;
    }

    private static bool _pikachuProbed;
    private static string _pikachuLoaderName;

    /// <summary>
    /// Display name of the installed Pikachu loader ("ModLoader", "ModCore", or
    /// "ModLoader+ModCore"), or <c>null</c> when neither DLL exists under plugins.
    /// Detected by filename on disk — does not load anything into the AppDomain.
    /// </summary>
    internal static string PikachuLoaderName
    {
        get
        {
            if (_pikachuProbed) return _pikachuLoaderName;
            _pikachuProbed = true;
            try
            {
                bool hasModLoader = false, hasModCore = false;
                foreach (var dll in Directory.EnumerateFiles(PathUtil.PluginsDir, "*.dll", SearchOption.AllDirectories))
                {
                    var name = Path.GetFileName(dll);
                    if (string.Equals(name, "ModLoader.dll", StringComparison.OrdinalIgnoreCase)) hasModLoader = true;
                    else if (string.Equals(name, "ModCore.dll", StringComparison.OrdinalIgnoreCase)) hasModCore = true;
                    if (hasModLoader && hasModCore) break;
                }
                _pikachuLoaderName = hasModLoader && hasModCore ? "ModLoader+ModCore"
                                   : hasModLoader ? "ModLoader"
                                   : hasModCore ? "ModCore"
                                   : null;
            }
            catch (Exception ex)
            {
                Log.Warn($"Pikachu loader probe failed: {Log.ExceptionText(ex)}");
                _pikachuLoaderName = null;
            }
            return _pikachuLoaderName;
        }
    }

    /// <summary>
    /// Inspects a mod's directory tree for content that gates optional LoadOrchestrator phases.
    /// Populates the <c>Has*</c> flags on the manifest. Only filesystem checks — no parsing.
    /// </summary>
    private static void ProbeFeatures(ModManifest mod)
    {
        var dir = mod.DirectoryPath;

        mod.HasSpriteFiles =
            HasDeclaredAssets(mod.Assets?.Sprites) ||
            HasAnyFile(Path.Combine(dir, "Resource", "Picture"), new[] { "*.png", "*.jpg" }) ||
            HasAnyFile(Path.Combine(dir, "Resource", "Texture2D"), new[] { "*.png", "*.jpg" });

        mod.HasAssetBundles =
            HasDeclaredAssets(mod.Assets?.AssetBundles) ||
            HasAnyFile(Path.Combine(dir, "Resource"), new[] { "*.ab" }, SearchOption.TopDirectoryOnly);

        mod.HasLocalization =
            HasAnyFile(Path.Combine(dir, "Localization"), new[] { "*.csv" });

        mod.HasBlueprintTabs = File.Exists(Path.Combine(dir, "BlueprintTabs.json"));
        mod.HasSmeltingRecipes = File.Exists(Path.Combine(dir, "SmeltingRecipes.json"));
        mod.HasDropInjections  = File.Exists(Path.Combine(dir, "DropInjections.json"));
        mod.HasImprovementInjections = File.Exists(Path.Combine(dir, "InjectImprovementInto.json"));
        mod.HasTradingValues = File.Exists(Path.Combine(dir, "TradingValues.json"));

        mod.HasAudio =
            HasDeclaredAssets(mod.Assets?.Audio) ||
            HasAnyFile(Path.Combine(dir, "Resource", "Audio"),
                new[] { "*.wav", "*.mp3", "*.ogg" }, SearchOption.AllDirectories);

        mod.HasMapCaches =
            HasDeclaredAssets(mod.Assets?.MapCaches) ||
            HasAnyFile(Path.Combine(dir, "Data", "MapCaches"), new[] { "*.json" }) ||
            HasAnyFile(Path.Combine(dir, "Data", "MapCache"), new[] { "*.json" }) ||
            HasAnyFile(Path.Combine(dir, "Data"), new[] { "*Map*.json" });

        mod.HasTriggers =
            HasAnyFile(Path.Combine(dir, "CardData", "Trigger"), new[] { "*.json" });

        mod.HasGifContent =
            HasAnyFile(Path.Combine(dir, "CardData", "Gif"), new[] { "*.json" });

        mod.HasGSMTagOrTypeMatch = HasGSMBulkMatch(dir);

        mod.HasFullMap = File.Exists(Path.Combine(dir, "WorldMap", "FullMap.json"));

        mod.HasWorldMapNodes =
            HasAnyFile(Path.Combine(dir, "WorldMap"), new[] { "MapNodes.json" }) ||
            mod.HasFullMap;

        mod.HasEncounterGuards =
            HasAnyFile(Path.Combine(dir, "EncounterGuards"), new[] { "*.json" }, SearchOption.TopDirectoryOnly);

        mod.HasQuestManifest = File.Exists(Path.Combine(dir, "Quests.json"));
        mod.HasCharacterManifest = File.Exists(Path.Combine(dir, "Characters.json"));
        mod.HasHubPortals = File.Exists(Path.Combine(dir, "WorldMap", "HubPortals.json"));
        mod.HasMapMod     = File.Exists(Path.Combine(dir, "MapMod.json"));

        mod.HasAnimals =
            HasAnyFile(Path.Combine(dir, "Animals"), new[] { "*.json" }, SearchOption.TopDirectoryOnly);
    }

    private static bool HasGSMBulkMatch(string modDir)
    {
        var gsmDir = Path.Combine(modDir, "GameSourceModify");
        if (!Directory.Exists(gsmDir))
            gsmDir = Path.Combine(modDir, "Data", "GameSourceModify");
        if (!Directory.Exists(gsmDir)) return false;

        try
        {
            foreach (var file in Directory.EnumerateFiles(gsmDir, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    if (json.IndexOf("MatchTagWarpData", StringComparison.Ordinal) >= 0
                        || json.IndexOf("MatchTypeWarpData", StringComparison.Ordinal) >= 0)
                        return true;
                }
                catch (Exception ex) { Log.Debug($"[ModDiscovery] HasGSMBulkMatch: read/scan failed for {file}: {ex.GetType().Name} {ex.Message}"); }
            }
        }
        catch (Exception ex) { Log.Debug($"[ModDiscovery] HasGSMBulkMatch: enumerating {gsmDir} failed: {ex.GetType().Name} {ex.Message}"); }
        return false;
    }

    private static bool HasDeclaredAssets(List<string> entries)
    {
        if (entries == null) return false;
        foreach (var entry in entries)
            if (!string.IsNullOrWhiteSpace(entry))
                return true;
        return false;
    }

    // Counts JSON content files across the well-known content directories.
    // Used by dedup to pick the more complete of two same-named folders.
    private static readonly string[] ContentDirs =
        { "CardData", "CharacterPerk", "PerkGroup", "GameStat", "SpiceTag", "ScriptableObject" };

    private static int CountContentFiles(string modDir)
    {
        int count = 0;
        foreach (var sub in ContentDirs)
        {
            var path = Path.Combine(modDir, sub);
            if (!Directory.Exists(path)) continue;
            try { count += Directory.GetFiles(path, "*.json", SearchOption.AllDirectories).Length; }
            catch (Exception ex) { Log.Debug($"[ModDiscovery] CountContentFiles: GetFiles failed for {path}: {ex.GetType().Name} {ex.Message}"); }
        }
        return count;
    }

    private static bool HasAnyFile(string dir, string[] patterns,
        SearchOption option = SearchOption.AllDirectories)
    {
        if (!Directory.Exists(dir)) return false;
        try
        {
            foreach (var pattern in patterns)
                if (Directory.EnumerateFiles(dir, pattern, option).Any())
                    return true;
        }
        catch (Exception ex) { Log.Debug($"[ModDiscovery] HasAnyFile: enumerating {dir} failed: {ex.GetType().Name} {ex.Message}"); }
        return false;
    }
}
