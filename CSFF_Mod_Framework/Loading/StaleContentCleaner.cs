using CSFFModFramework.Util;

namespace CSFFModFramework.Loading;

/// <summary>
/// Makes "copy the new version over the old one" safe. Copy-paste leaves behind every file the
/// new release no longer ships (a renamed or deleted card JSON, a retired CSV, an old PNG), and
/// each leftover JSON still loads: a stale card keeps its old UniqueID, a stale blueprint keeps
/// pointing at items that are gone.
///
/// <para>Release zips carry a <c>ContentManifest.json</c> (written by
/// <c>Development_Tools/New-ReleaseZip.ps1</c>) listing every file the release ships. At discovery,
/// before the mod's content is probed or loaded, any content file that is on disk but not in that
/// list is MOVED to <c>BepInEx/CSFF_Retired/&lt;mod&gt;/&lt;version&gt;/</c>, never deleted, so a
/// wrong call is recoverable. Retired folders older than <see cref="RetiredKeepDays"/> are pruned.</para>
///
/// <para>Guards, because this touches the player's disk: it only acts when the manifest's version
/// equals <c>ModInfo.json</c>'s version (a hand-edited or half-updated folder is left alone), only
/// under known content roots and extensions (never DLLs, configs or <c>SpriteCache/</c>), and it
/// aborts and warns when it would retire an implausible share of the folder.</para>
/// </summary>
internal static class StaleContentCleaner
{
    internal const string ManifestFileName = "ContentManifest.json";
    internal const string RetiredFolderName = "CSFF_Retired";
    internal const int RetiredKeepDays = 30;

    // Folders whose contents the loaders read. Anything else in a mod folder is not ours to move.
    private static readonly string[] ContentRoots =
    {
        "CardData", "CharacterPerk", "ScriptableObject", "Localization", "Resource", "GameSourceModify",
        "WorldMap", "Animals", "EncounterGuards", "FlavourMatrix", "Data", "Triggers",
    };

    private static readonly string[] ContentExtensions =
        { ".json", ".csv", ".png", ".jpg", ".gif", ".ab", ".wav", ".ogg", ".mp3" };

    // Resource/ holds runtime caches the game or framework writes; not part of any release.
    private static readonly string[] NeverTouch = { "SpriteCache" };

    [Serializable]
    private class ContentManifest
    {
#pragma warning disable 0649
        public string Version;
        public string[] Files;
#pragma warning restore 0649
    }

    /// <summary>Retire files not listed by the mod's content manifest. Never throws.</summary>
    internal static void Run(string modDir, string modName, string modVersion)
    {
        try
        {
            var manifestPath = Path.Combine(modDir, ManifestFileName);
            if (!File.Exists(manifestPath)) return;

            var manifest = JsonUtility.FromJson<ContentManifest>(File.ReadAllText(manifestPath));
            if (manifest?.Files == null || manifest.Files.Length == 0)
            {
                Log.Warn($"StaleContentCleaner: {modName}: {ManifestFileName} is empty or unreadable, skipping cleanup");
                return;
            }
            if (!string.Equals(manifest.Version, modVersion, StringComparison.OrdinalIgnoreCase))
            {
                Log.Info($"StaleContentCleaner: {modName}: manifest is for v{manifest.Version} but ModInfo says "
                       + $"v{modVersion} (folder was edited by hand or is half-updated), skipping cleanup");
                return;
            }

            var listed = new HashSet<string>(manifest.Files.Select(Normalize), StringComparer.OrdinalIgnoreCase);
            var orphans = FindOrphans(modDir, listed);
            if (orphans.Count == 0) return;

            // A real update retires a handful of files. A huge share means the manifest or the folder
            // is not what we think it is (partial extract, wrong zip), so touch nothing.
            int cap = Math.Max(25, listed.Count / 2);
            if (orphans.Count > cap)
            {
                Log.Warn($"StaleContentCleaner: {modName}: {orphans.Count} unlisted files exceeds the safety cap "
                       + $"({cap}); nothing retired. Reinstall {modName} cleanly if it misbehaves.");
                return;
            }

            var retiredRoot = Path.Combine(Directory.GetParent(PathUtil.PluginsDir).FullName,
                RetiredFolderName, SafeName(modName), SafeName(modVersion));
            int moved = 0, failed = 0;
            foreach (var rel in orphans)
            {
                var src = Path.Combine(modDir, rel.Replace('/', Path.DirectorySeparatorChar));
                try
                {
                    var dst = Path.Combine(retiredRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    if (File.Exists(dst)) File.Delete(dst);
                    File.Move(src, dst);
                    moved++;
                }
                catch (Exception ex)
                {
                    failed++;
                    Log.Warn($"StaleContentCleaner: {modName}: could not retire {rel}: {ex.GetType().Name} {ex.Message}");
                }
            }

            PruneEmptyDirs(modDir);
            Log.Info($"StaleContentCleaner: {modName} v{modVersion}: retired {moved} file(s) left over from an older "
                   + $"version to {RetiredFolderName}/{SafeName(modName)}/{SafeName(modVersion)}"
                   + (failed > 0 ? $" ({failed} could not be moved)" : ""));
        }
        catch (Exception ex)
        {
            Log.Warn($"StaleContentCleaner: {modName}: cleanup skipped: {Log.ExceptionText(ex)}");
        }
    }

    /// <summary>Delete retired folders older than <see cref="RetiredKeepDays"/>. Never throws.</summary>
    internal static void PruneRetired()
    {
        try
        {
            var root = Path.Combine(Directory.GetParent(PathUtil.PluginsDir).FullName, RetiredFolderName);
            if (!Directory.Exists(root)) return;
            var cutoff = DateTime.UtcNow.AddDays(-RetiredKeepDays);
            foreach (var modFolder in Directory.GetDirectories(root))
            {
                foreach (var versionFolder in Directory.GetDirectories(modFolder))
                    if (Directory.GetLastWriteTimeUtc(versionFolder) < cutoff)
                        Directory.Delete(versionFolder, true);
                if (Directory.GetFileSystemEntries(modFolder).Length == 0) Directory.Delete(modFolder);
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"StaleContentCleaner: prune of old retired folders skipped: {ex.GetType().Name} {ex.Message}");
        }
    }

    /// <summary>Relative paths (forward slashes) of content files on disk that the release does not list.</summary>
    internal static List<string> FindOrphans(string modDir, HashSet<string> listed)
    {
        var orphans = new List<string>();
        foreach (var root in ContentRoots)
        {
            var dir = Path.Combine(modDir, root);
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                Consider(modDir, file, listed, orphans);
        }
        // Top-level declarative files (BlueprintTabs.json, DropInjections.json, ...). ModInfo.json and
        // the manifest itself are never candidates.
        foreach (var file in Directory.EnumerateFiles(modDir, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(file);
            if (name.Equals("ModInfo.json", StringComparison.OrdinalIgnoreCase)
                || name.Equals(ManifestFileName, StringComparison.OrdinalIgnoreCase)) continue;
            Consider(modDir, file, listed, orphans);
        }
        return orphans;
    }

    private static void Consider(string modDir, string file, HashSet<string> listed, List<string> orphans)
    {
        var rel = Normalize(file.Substring(modDir.Length));
        if (NeverTouch.Any(n => rel.StartsWith(n + "/", StringComparison.OrdinalIgnoreCase)
                             || rel.IndexOf("/" + n + "/", StringComparison.OrdinalIgnoreCase) >= 0)) return;
        if (!ContentExtensions.Contains(Path.GetExtension(file).ToLowerInvariant())) return;
        if (!listed.Contains(rel)) orphans.Add(rel);
    }

    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');

    private static string SafeName(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return string.IsNullOrWhiteSpace(s) ? "_" : s;
    }

    private static void PruneEmptyDirs(string dir)
    {
        foreach (var sub in Directory.GetDirectories(dir))
        {
            PruneEmptyDirs(sub);
            if (Directory.GetFileSystemEntries(sub).Length == 0)
            {
                try { Directory.Delete(sub); }
                catch (Exception ex) { Log.Debug($"StaleContentCleaner: empty dir {sub} not removed: {ex.GetType().Name}"); }
            }
        }
    }
}
