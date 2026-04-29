using CSFFModFramework.Util;

namespace CSFFModFramework.Discovery;

/// <summary>
/// Resolves the effective asset file list for a mod. Respects the
/// <see cref="AssetsManifest"/> declared in <c>ModInfo.json</c> when present,
/// and otherwise falls back to the legacy <c>Resource/&lt;Subdir&gt;/</c> scan
/// for backward compatibility.
///
/// <para>
/// Manifest entries are interpreted as paths relative to the mod folder. An
/// entry ending in <c>/</c> or <c>\</c>, or pointing at an existing directory,
/// is scanned recursively for the asset kind's default extensions. Otherwise
/// it is treated as a literal file path.
/// </para>
/// </summary>
internal static class ModAssets
{
    private static readonly string[] LegacySpriteDirs    = { "Resource/Picture", "Resource/Texture2D" };
    private static readonly string[] LegacyAudioDirs     = { "Resource/Audio" };
    private static readonly string[] LegacyGifDirs       = { "Resource/GIF" };
    private const string LegacyAssetBundleDir            = "Resource";

    private static readonly string[] SpriteExts          = { ".png", ".jpg" };
    private static readonly string[] AudioExts           = { ".wav", ".ogg", ".mp3" };
    private static readonly string[] GifExts             = { ".gif" };
    private static readonly string[] AssetBundleExts     = { ".ab" };

    public static IEnumerable<string> ResolveSprites(ModManifest mod)
        => Resolve(mod, mod.Assets?.Sprites, LegacySpriteDirs, SpriteExts, recursive: true);

    public static IEnumerable<string> ResolveAudio(ModManifest mod)
        => Resolve(mod, mod.Assets?.Audio, LegacyAudioDirs, AudioExts, recursive: true);

    public static IEnumerable<string> ResolveGifs(ModManifest mod)
        => Resolve(mod, mod.Assets?.Gifs, LegacyGifDirs, GifExts, recursive: true);

    public static IEnumerable<string> ResolveAssetBundles(ModManifest mod)
    {
        // AssetBundles default to top-level only (legacy behavior — bundles ship
        // at Resource/*.ab, not nested).
        return Resolve(mod, mod.Assets?.AssetBundles,
            new[] { LegacyAssetBundleDir }, AssetBundleExts, recursive: false);
    }

    private static IEnumerable<string> Resolve(
        ModManifest mod,
        List<string> manifestEntries,
        string[] legacyDirs,
        string[] extensions,
        bool recursive)
    {
        // Manifest path: explicit entries win.
        if (manifestEntries != null && manifestEntries.Count > 0)
        {
            foreach (var entry in manifestEntries)
            {
                if (string.IsNullOrEmpty(entry)) continue;
                foreach (var path in ExpandManifestEntry(mod.DirectoryPath, entry, extensions, recursive))
                    yield return path;
            }
            yield break;
        }

        // Legacy fallback: scan each well-known directory.
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        foreach (var subDir in legacyDirs)
        {
            var dir = Path.Combine(mod.DirectoryPath, subDir);
            if (!Directory.Exists(dir)) continue;

            foreach (var file in Directory.GetFiles(dir, "*.*", searchOption))
            {
                if (!HasMatchingExtension(file, extensions)) continue;
                yield return file;
            }
        }
    }

    private static IEnumerable<string> ExpandManifestEntry(
        string modRoot, string entry, string[] extensions, bool recursive)
    {
        var trimmed = entry.Replace('\\', '/').TrimStart('/');
        var full = Path.Combine(modRoot, trimmed);
        var endsWithSeparator = entry.EndsWith("/", StringComparison.Ordinal)
                             || entry.EndsWith("\\", StringComparison.Ordinal);

        // Directory entry (explicit "/" terminator OR an existing directory)
        if (endsWithSeparator || Directory.Exists(full))
        {
            if (!Directory.Exists(full))
            {
                Log.Warn($"ModAssets: declared directory '{entry}' not found under {modRoot}");
                yield break;
            }
            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            foreach (var file in Directory.GetFiles(full, "*.*", searchOption))
            {
                if (!HasMatchingExtension(file, extensions)) continue;
                yield return file;
            }
            yield break;
        }

        // Literal file entry
        if (!File.Exists(full))
        {
            Log.Warn($"ModAssets: declared asset '{entry}' not found at {full}");
            yield break;
        }
        if (!HasMatchingExtension(full, extensions))
        {
            Log.Warn($"ModAssets: declared asset '{entry}' has unsupported extension (expected one of {string.Join(", ", extensions)})");
            yield break;
        }
        yield return full;
    }

    private static bool HasMatchingExtension(string file, string[] extensions)
    {
        var ext = Path.GetExtension(file);
        for (int i = 0; i < extensions.Length; i++)
            if (string.Equals(ext, extensions[i], StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
