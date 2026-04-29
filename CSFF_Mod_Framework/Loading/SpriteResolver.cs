using CSFFModFramework.Data;
using CSFFModFramework.Discovery;
using CSFFModFramework.Util;

namespace CSFFModFramework.Loading;

internal static class SpriteResolver
{
    private static readonly Dictionary<string, Sprite> _diskSpriteCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] DurabilityIconFields =
    {
        "SpoilageTime", "UsageTime", "Fuel", "Progress",
        "SpecialDurability1", "SpecialDurability2", "SpecialDurability3", "SpecialDurability4"
    };

    public static void ResolveAll(IEnumerable allData, List<ModManifest> mods)
    {
        var perkIconMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var cardImageMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var durabilityIconMap = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        // Build maps from cached JSON (loaded by JsonDataLoader — no filesystem reads needed)
        foreach (var kvp in JsonDataLoader.JsonByUniqueId)
        {
            var uid = kvp.Key;
            var json = kvp.Value;

            var perkWarp = PathUtil.QuickExtractString(json, "PerkIconWarpData");
            if (!string.IsNullOrEmpty(perkWarp))
                perkIconMap[uid] = perkWarp;

            var cardWarp = PathUtil.QuickExtractString(json, "CardImageWarpData");
            if (!string.IsNullOrEmpty(cardWarp))
                cardImageMap[uid] = cardWarp;

            // Durability icons
            BuildDurabilityMap(json, uid, durabilityIconMap);
        }

        Log.Debug($"SpriteResolver: maps built — {perkIconMap.Count} perk icons, {cardImageMap.Count} card images, {durabilityIconMap.Count} durability icon cards");

        // Resolve sprites on loaded objects
        var perkIconField = AccessTools.Field(typeof(CharacterPerk), "PerkIcon");
        var cardImageField = AccessTools.Field(typeof(CardData), "CardImage");
        int perksFixed = 0, cardsFixed = 0, durabilityFixed = 0;

        foreach (var item in allData)
        {
            if (item is CharacterPerk perk && perkIconField != null)
            {
                var icon = perkIconField.GetValue(perk) as Sprite;
                if (icon == null && perkIconMap.TryGetValue(perk.UniqueID, out var spriteName))
                {
                    var sprite = LookupSprite(spriteName);
                    if (sprite != null)
                    {
                        perkIconField.SetValue(perk, sprite);
                        perksFixed++;
                    }
                    else
                    {
                        Log.Warn($"SpriteResolver: missing perk icon '{spriteName}' for {perk.UniqueID}");
                    }
                }
            }

            if (item is CardData card && cardImageField != null && card.UniqueID != null)
            {
                var img = cardImageField.GetValue(card) as Sprite;
                if (img == null && cardImageMap.TryGetValue(card.UniqueID, out var imgName))
                {
                    var imgSprite = LookupSprite(imgName);
                    if (imgSprite != null)
                    {
                        cardImageField.SetValue(card, imgSprite);
                        cardsFixed++;
                    }
                }

                // Durability override icons
                if (durabilityIconMap.TryGetValue(card.UniqueID, out var perCard))
                {
                    foreach (var kvp in perCard)
                    {
                        var durField = AccessTools.Field(card.GetType(), kvp.Key);
                        if (durField == null) continue;

                        var durObj = durField.GetValue(card);
                        if (durObj == null) continue;

                        var overrideIconField = AccessTools.Field(durObj.GetType(), "OverrideIcon");
                        if (overrideIconField == null || !typeof(Sprite).IsAssignableFrom(overrideIconField.FieldType))
                            continue;

                        var current = overrideIconField.GetValue(durObj) as Sprite;
                        if (current != null) continue;

                        var sprite = LookupSprite(kvp.Value);
                        if (sprite == null) continue;

                        overrideIconField.SetValue(durObj, sprite);
                        if (durObj.GetType().IsValueType)
                            durField.SetValue(card, durObj);
                        durabilityFixed++;
                    }
                }
            }
        }

        Log.Debug($"SpriteResolver: fixed {perksFixed} perk icons, {cardsFixed} card images, {durabilityFixed} durability icons");
    }

    private static Sprite LookupSprite(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;

        // Tier 1: Database.SpriteDict (includes mod-loaded sprites)
        if (Database.SpriteDict.TryGetValue(name, out var s)) return s;

        // Tier 2: Database already has all sprites from FindObjectsOfTypeAll
        // (no separate cache needed — Database.SpriteDict IS the comprehensive cache)

        // Tier 3: Disk load from Resource/Picture/
        return LoadFromDisk(name);
    }

    private static Sprite LoadFromDisk(string name)
    {
        if (_diskSpriteCache.TryGetValue(name, out var cached)) return cached;

        var pluginsDir = PathUtil.PluginsDir;
        if (pluginsDir == null) return null;

        foreach (var modDir in Directory.GetDirectories(pluginsDir))
        {
            var pictureDir = Path.Combine(modDir, "Resource", "Picture");
            if (!Directory.Exists(pictureDir)) continue;

            var path = Path.Combine(pictureDir, name + ".png");
            if (!File.Exists(path))
            {
                path = Path.Combine(pictureDir, name + ".jpg");
                if (!File.Exists(path)) continue;
            }

            try
            {
                var bytes = File.ReadAllBytes(path);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!texture.LoadImage(bytes))
                {
                    UnityEngine.Object.Destroy(texture);
                    continue;
                }

                texture.name = name;
                var sprite = Sprite.Create(texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f), 100f);
                sprite.name = name;

                _diskSpriteCache[name] = sprite;
                Database.SpriteDict[name] = sprite;
                return sprite;
            }
            catch { }
        }

        return null;
    }

    private static void BuildDurabilityMap(string json, string uid,
        Dictionary<string, Dictionary<string, string>> map)
    {
        if (string.IsNullOrEmpty(uid)) return;

        try
        {
            var parsed = MiniJson.Parse(json) as Dictionary<string, object>;
            if (parsed == null) return;

            var perCard = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var fieldName in DurabilityIconFields)
            {
                if (!parsed.TryGetValue(fieldName, out var durObj) ||
                    durObj is not Dictionary<string, object> durDict)
                    continue;

                if (durDict.TryGetValue("OverrideIconWarpData", out var warpObj) &&
                    warpObj is string warpStr && !string.IsNullOrWhiteSpace(warpStr))
                {
                    perCard[fieldName] = warpStr;
                }
            }

            if (perCard.Count > 0)
                map[uid] = perCard;
        }
        catch { }
    }
}
