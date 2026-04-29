using CSFFModFramework.Data;
using CSFFModFramework.Discovery;
using CSFFModFramework.Util;

namespace CSFFModFramework.Loading;

/// <summary>
/// Loads Unity AssetBundles (.ab files) from mod Resource/ directories.
/// Extracts Sprites and AudioClips into Database dictionaries.
/// </summary>
internal static class AssetBundleLoader
{
    public static void LoadAll(List<ModManifest> mods)
    {
        int totalSprites = 0;
        int totalClips = 0;

        foreach (var mod in mods)
        {
            foreach (var file in ModAssets.ResolveAssetBundles(mod))
            {
                try
                {
                    var bundle = AssetBundle.LoadFromFile(file);
                    if (bundle == null)
                    {
                        Log.Warn($"AssetBundleLoader: failed to load bundle {file}");
                        continue;
                    }

                    // Extract sprites
                    var sprites = bundle.LoadAllAssets<Sprite>();
                    foreach (var sprite in sprites)
                    {
                        if (sprite != null && !string.IsNullOrEmpty(sprite.name)
                            && !Database.SpriteDict.ContainsKey(sprite.name))
                        {
                            Database.SpriteDict[sprite.name] = sprite;
                            totalSprites++;
                        }
                    }

                    // Extract audio clips
                    var clips = bundle.LoadAllAssets<AudioClip>();
                    foreach (var clip in clips)
                    {
                        if (clip != null && !string.IsNullOrEmpty(clip.name)
                            && !Database.AudioClipDict.ContainsKey(clip.name))
                        {
                            Database.AudioClipDict[clip.name] = clip;
                            totalClips++;
                        }
                    }

                    bundle.Unload(false);
                }
                catch (Exception ex)
                {
                    Log.Warn($"AssetBundleLoader: error loading {file}: {ex.Message}");
                }
            }
        }

        if (totalSprites > 0 || totalClips > 0)
            Log.Info($"AssetBundleLoader: loaded {totalSprites} sprites, {totalClips} audio clips from bundles");
    }
}
