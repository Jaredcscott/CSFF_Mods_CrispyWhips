using CSFFModFramework.Data;
using CSFFModFramework.Discovery;
using CSFFModFramework.Util;

namespace CSFFModFramework.Loading;

internal static class SpriteLoader
{
    public static void LoadAll(List<ModManifest> mods)
    {
        SpriteTextureCache.Initialize();
        int totalLoaded = 0;
        int skippedVanilla = 0;

        foreach (var mod in mods)
        {
            int modCount = 0;

            foreach (var file in ModAssets.ResolveSprites(mod))
            {
                try
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    if (string.IsNullOrEmpty(name)) continue;
                    if (Database.SpriteDict.ContainsKey(name)) { skippedVanilla++; continue; }

                    Texture2D texture;
                    if (!SpriteTextureCache.TryLoad(file, out texture))
                    {
                        // Cache miss: decode PNG, schedule a background cache write,
                        // then free CPU memory. markNonReadable=false keeps the CPU
                        // copy so SaveAsync can call GetRawTextureData().
                        var bytes = File.ReadAllBytes(file);
                        texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        if (!texture.LoadImage(bytes, false))
                        {
                            UnityEngine.Object.Destroy(texture);
                            continue;
                        }
                        SpriteTextureCache.SaveAsync(file, bytes, texture);
                        // Apply frees CPU memory after we've handed the bytes off to
                        // the background writer.
                        texture.Apply(false, true);
                    }

                    texture.name = name;
                    var sprite = Sprite.Create(
                        texture,
                        new Rect(0f, 0f, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f),
                        100f);
                    sprite.name = name;

                    Database.SpriteDict[name] = sprite;
                    modCount++;
                }
                catch (Exception ex)
                {
                    Log.Warn($"Failed to load sprite {file}: {Log.ExceptionText(ex)}");
                }
            }

            if (modCount > 0)
                Log.Debug($"SpriteLoader: loaded {modCount} sprites from {mod.Name}");
            totalLoaded += modCount;
        }

        var rescues = SpriteTextureCache.HashRescues;
        var rescueSuffix = rescues > 0 ? $", {rescues} rescued via hash" : string.Empty;
        var bundleSuffix = SpriteTextureCache.BundleHits > 0
            ? $" [{SpriteTextureCache.BundleHits} via bundle, {SpriteTextureCache.CacheHits - SpriteTextureCache.BundleHits} via per-file]"
            : string.Empty;
        Log.Debug($"SpriteLoader: {totalLoaded} sprites loaded " +
                 $"({SpriteTextureCache.CacheHits} from cache, {SpriteTextureCache.CacheMisses} decoded, {skippedVanilla} skipped vanilla{rescueSuffix}){bundleSuffix}");

        // Warm-cache phase breakdown — surfaces where the per-sprite cost lands so we
        // can target the actual bottleneck (bundle read vs per-file vs GPU upload).
        if (SpriteTextureCache.CacheHits > 0)
        {
            var freq = (double)System.Diagnostics.Stopwatch.Frequency;
            long ToMs(long ticks) => (long)(ticks * 1000.0 / freq);
            Log.Debug($"SpriteLoader: warm-cache breakdown - " +
                     $"bundle load {ToMs(SpriteTextureCache.BundleLoadTicks)}ms, " +
                     $"PNG stat {ToMs(SpriteTextureCache.PngStatTicks)}ms, " +
                     $"per-file read {ToMs(SpriteTextureCache.CacheReadTicks)}ms, " +
                     $"Texture2D create+upload {ToMs(SpriteTextureCache.TextureCreateTicks)}ms, " +
                     $"GPU Apply {ToMs(SpriteTextureCache.GpuApplyTicks)}ms");
        }
    }
}
