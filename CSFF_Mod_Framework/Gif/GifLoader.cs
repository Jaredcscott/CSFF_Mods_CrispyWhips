using CSFFModFramework.Discovery;
using CSFFModFramework.Util;
using ThreeDISevenZeroR.UnityGifDecoder;

namespace CSFFModFramework.Gif;

/// <summary>
/// Loads GIF animations and card definitions for all discovered mods.
///
/// GIF files:     <mod>/Resource/GIF/*.gif
/// Definitions:   <mod>/CardData/Gif/*.json
///
/// JSON schema example (CardData/Gif/MyCard.json):
/// {
///   "CardUniqueId": "my_mod_my_card",
///   "CardGif":            { "GifName": "MyCardIdle",    "Loop": true },
///   "CardBackgroundGif":  { "GifName": "MyCardBg",      "Loop": true },
///   "CookingGif":         { "GifName": "MyCardCooking", "Loop": true },
///   "DefaultLiquidGif":   { "GifName": "",              "Loop": false },
///   "LiquidGifs": [ { "Index": 0, "GifPlayDef": { "GifName": "MyLiq0", "Loop": true } } ],
///   "ConditionSets": [
///     {
///       "Gif": { "GifName": "MyCardLow", "Loop": true },
///       "DurabilityConditions": [ { "DurabilityType": 2, "MinNormalized": 0.0, "MaxNormalized": 0.25 } ]
///     }
///   ]
/// }
/// </summary>
internal static class GifLoader
{
    // Decoded GIFs keyed by file name (no extension), shared across all mods.
    public static Dictionary<string, GifFrameSet> GifFrameSets { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    // Card definitions keyed by CardUniqueId.
    public static Dictionary<string, GifCardDefinition> CardDefinitions { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public static void LoadAll(List<ModManifest> mods)
    {
        foreach (var mod in mods)
        {
            LoadGifFiles(mod);
            LoadDefinitions(mod);
        }

        int activeCards = CardDefinitions.Values.Count(d =>
            !string.IsNullOrEmpty(d.CardUniqueId) &&
            (d.CardGif != null || d.CookingGif != null || d.CardBackgroundGif != null || d.ConditionSets.Count > 0));

        if (GifFrameSets.Count > 0 || CardDefinitions.Count > 0)
            Log.Info($"GifLoader: {GifFrameSets.Count} GIFs decoded, {CardDefinitions.Count} card definitions ({activeCards} with GIF references)");
    }

    // -------------------------------------------------------------------------
    // GIF file decoding
    // -------------------------------------------------------------------------

    private static void LoadGifFiles(ModManifest mod)
    {
        foreach (var file in ModAssets.ResolveGifs(mod))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (string.IsNullOrEmpty(name) || GifFrameSets.ContainsKey(name)) continue;

            try
            {
                var frameSet = DecodeGif(file, name);
                if (frameSet != null)
                {
                    GifFrameSets[name] = frameSet;
                    Log.Debug($"GifLoader: decoded '{name}' ({frameSet.Frames.Length} frames) from {mod.Name}");
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"GifLoader: failed to decode '{name}': {ex.Message}");
            }
        }
    }

    private static GifFrameSet DecodeGif(string path, string name)
    {
        var frames = new List<Sprite>();
        var delays = new List<float>();

        using var stream = File.OpenRead(path);
        var gifStream = new GifStream(stream);

        // FlipVertically=true corrects Unity's bottom-up texture convention for GIF top-down data
        gifStream.FlipVertically = true;

        while (gifStream.HasMoreData)
        {
            switch (gifStream.CurrentToken)
            {
                case GifStream.Token.Image:
                    var image = gifStream.ReadImage();
                    if (image?.colors == null || image.colors.Length == 0) break;

                    var tex = new Texture2D(gifStream.Header.width, gifStream.Header.height, TextureFormat.RGBA32, false);
                    tex.SetPixels32(image.colors);
                    // makeNoLongerReadable=true frees CPU-side pixel buffer after GPU upload
                    tex.Apply(false, true);
                    tex.name = name;

                    var sprite = Sprite.Create(
                        tex,
                        new Rect(0f, 0f, tex.width, tex.height),
                        new Vector2(0.5f, 0.5f),
                        100f);
                    sprite.name = name;

                    frames.Add(sprite);
                    delays.Add(image.SafeDelaySeconds);
                    break;

                default:
                    gifStream.SkipToken();
                    break;
            }
        }

        if (frames.Count == 0) return null;

        return new GifFrameSet
        {
            Name = name,
            Frames = frames.ToArray(),
            Delays = delays.ToArray(),
            Loop = true,
        };
    }

    // -------------------------------------------------------------------------
    // Card definition JSON loading
    // -------------------------------------------------------------------------

    private static void LoadDefinitions(ModManifest mod)
    {
        var dir = Path.Combine(mod.DirectoryPath, "CardData/Gif");
        if (!Directory.Exists(dir)) return;

        foreach (var file in Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories))
        {
            try
            {
                var json = File.ReadAllText(file);
                var def = ParseDefinition(json);
                if (def == null || string.IsNullOrEmpty(def.CardUniqueId))
                {
                    Log.Warn($"GifLoader: skipping {file} — missing or empty CardUniqueId");
                    continue;
                }
                CardDefinitions[def.CardUniqueId] = def;
            }
            catch (Exception ex)
            {
                Log.Warn($"GifLoader: failed to parse {file}: {ex.Message}");
            }
        }
    }

    private static GifCardDefinition ParseDefinition(string json)
    {
        if (MiniJson.Parse(json) is not Dictionary<string, object> dict) return null;

        var def = new GifCardDefinition();

        if (dict.TryGetValue("CardUniqueId", out var uid)) def.CardUniqueId = uid as string ?? "";
        if (dict.TryGetValue("CardGif", out var cg)) def.CardGif = ParsePlayDef(cg);
        if (dict.TryGetValue("CardBackgroundGif", out var bg)) def.CardBackgroundGif = ParsePlayDef(bg);
        if (dict.TryGetValue("CookingGif", out var cook)) def.CookingGif = ParsePlayDef(cook);
        if (dict.TryGetValue("DefaultLiquidGif", out var dl)) def.DefaultLiquidGif = ParsePlayDef(dl);

        if (dict.TryGetValue("LiquidGifs", out var liqRaw) && liqRaw is List<object> liqList)
        {
            foreach (var entry in liqList)
            {
                if (entry is not Dictionary<string, object> le) continue;
                le.TryGetValue("Index", out var idxRaw);
                le.TryGetValue("GifPlayDef", out var defRaw);
                if (defRaw == null) le.TryGetValue("Gif", out defRaw);
                int index = idxRaw is double d ? (int)d : 0;
                var pd = ParsePlayDef(defRaw);
                if (pd != null) def.LiquidGifs[index] = pd;
            }
        }

        if (dict.TryGetValue("ConditionSets", out var csRaw) && csRaw is List<object> csList)
        {
            foreach (var entry in csList)
            {
                var cs = ParseConditionSet(entry);
                if (cs != null) def.ConditionSets.Add(cs);
            }
        }

        return def;
    }

    private static GifPlayDef ParsePlayDef(object raw)
    {
        if (raw is not Dictionary<string, object> d) return null;
        d.TryGetValue("GifName", out var nameRaw);
        d.TryGetValue("Loop", out var loopRaw);
        var name = nameRaw as string ?? "";
        if (string.IsNullOrEmpty(name)) return null;
        return new GifPlayDef
        {
            GifName = name,
            Loop = loopRaw is bool b ? b : true,
        };
    }

    private static ConditionSetDef ParseConditionSet(object raw)
    {
        if (raw is not Dictionary<string, object> d) return null;

        d.TryGetValue("Gif", out var gifRaw);
        var gif = ParsePlayDef(gifRaw);
        if (gif == null) return null;

        var cs = new ConditionSetDef { Gif = gif };

        if (d.TryGetValue("DurabilityConditions", out var dcRaw) && dcRaw is List<object> dcList)
        {
            foreach (var entry in dcList)
            {
                if (entry is not Dictionary<string, object> de) continue;
                de.TryGetValue("DurabilityType", out var dtRaw);
                de.TryGetValue("MinNormalized", out var minRaw);
                de.TryGetValue("MaxNormalized", out var maxRaw);
                cs.DurabilityConditions.Add(new DurabilityConditionDef
                {
                    DurabilityType = dtRaw is double dt ? (int)dt : 0,
                    MinNormalized  = minRaw is double mn ? (float)mn : 0f,
                    MaxNormalized  = maxRaw is double mx ? (float)mx : 1f,
                });
            }
        }

        return cs;
    }
}
