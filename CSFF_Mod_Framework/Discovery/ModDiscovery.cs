using CSFFModFramework.Util;

namespace CSFFModFramework.Discovery;

internal static class ModDiscovery
{
    public static List<ModManifest> DiscoverMods()
    {
        var mods = new List<ModManifest>();
        var frameworkDir = PathUtil.FrameworkDir;

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

                ProbeFeatures(manifest);
                mods.Add(manifest);
                Log.Debug($"Discovered mod: {manifest.Name} v{manifest.Version} by {manifest.Author} @ {dir}");
            }
            catch (Exception ex)
            {
                Log.Warn($"Failed to parse ModInfo.json in {dir}: {ex.Message}");
            }
        }

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
        Log.Info($"Discovered {deduped.Count} mod(s) total");
        return deduped;
    }

    /// <summary>
    /// Inspects a mod's directory tree for content that gates optional LoadOrchestrator phases.
    /// Populates the <c>Has*</c> flags on the manifest. Only filesystem checks — no parsing.
    /// </summary>
    private static void ProbeFeatures(ModManifest mod)
    {
        var dir = mod.DirectoryPath;

        mod.HasSpriteFiles =
            HasAnyFile(Path.Combine(dir, "Resource", "Picture"), new[] { "*.png", "*.jpg" }) ||
            HasAnyFile(Path.Combine(dir, "Resource", "Texture2D"), new[] { "*.png", "*.jpg" });

        mod.HasAssetBundles =
            HasAnyFile(Path.Combine(dir, "Resource"), new[] { "*.ab" }, SearchOption.TopDirectoryOnly);

        mod.HasLocalization =
            HasAnyFile(Path.Combine(dir, "Localization"), new[] { "*.csv" });

        mod.HasBlueprintTabs = File.Exists(Path.Combine(dir, "BlueprintTabs.json"));
        mod.HasSmeltingRecipes = File.Exists(Path.Combine(dir, "SmeltingRecipes.json"));

        mod.HasAudio =
            HasAnyFile(Path.Combine(dir, "Resource", "Audio"), new[] { "*.*" }, SearchOption.AllDirectories);
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
            catch { /* permissions / transient IO */ }
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
        catch { /* permissions / transient IO */ }
        return false;
    }
}
