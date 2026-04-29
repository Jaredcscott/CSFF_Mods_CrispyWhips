using CSFFModFramework.Util;

namespace CSFFModFramework.Data;

internal static class DataMap
{
    public static Dictionary<string, List<CardData>> CardTagMap { get; private set; }
        = new Dictionary<string, List<CardData>>(StringComparer.OrdinalIgnoreCase);

    public static Dictionary<int, List<CardData>> CardTypeMap { get; private set; }
        = new Dictionary<int, List<CardData>>();

    public static void BuildMaps(IEnumerable allData)
    {
        CardTagMap.Clear();
        CardTypeMap.Clear();

        int cardCount = 0;
        foreach (var item in allData)
        {
            if (item is not CardData card) continue;
            cardCount++;

            // Index by CardType
            int ct = (int)card.CardType;
            if (!CardTypeMap.TryGetValue(ct, out var typeList))
            {
                typeList = new List<CardData>();
                CardTypeMap[ct] = typeList;
            }
            typeList.Add(card);

            // Index by tags (CardTag array on CardData)
            var tags = card.CardTags;
            if (tags == null) continue;
            foreach (var tag in tags)
            {
                if (tag == null) continue;
                var tagName = tag.name;
                if (string.IsNullOrEmpty(tagName)) continue;

                if (!CardTagMap.TryGetValue(tagName, out var tagList))
                {
                    tagList = new List<CardData>();
                    CardTagMap[tagName] = tagList;
                }
                tagList.Add(card);
            }
        }

        Log.Debug($"DataMap: indexed {cardCount} cards across {CardTypeMap.Count} types, {CardTagMap.Count} tags");
    }

    public static List<CardData> GetCardsByTag(string tagName)
    {
        if (CardTagMap.TryGetValue(tagName, out var list))
            return list;
        return new List<CardData>();
    }

    public static List<CardData> GetCardsByType(int cardType)
    {
        if (CardTypeMap.TryGetValue(cardType, out var list))
            return list;
        return new List<CardData>();
    }
}
