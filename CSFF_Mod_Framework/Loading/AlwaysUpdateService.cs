using CSFFModFramework.Discovery;
using CSFFModFramework.Util;

namespace CSFFModFramework.Loading;

/// <summary>
/// Enables AlwaysUpdate ticking on all mod cards so they process
/// durability decay, spoilage, stat changes, etc.
///
/// Previously only H&amp;F had this; now the framework does it for all mods.
/// </summary>
internal static class AlwaysUpdateService
{
    public static void EnableAll(IEnumerable allData, List<ModManifest> mods)
    {
        var modUniqueIds = JsonDataLoader.AllModUniqueIds;
        if (modUniqueIds.Count == 0) return;

        var alwaysUpdateField = AccessTools.Field(typeof(CardData), "AlwaysUpdate");
        if (alwaysUpdateField == null || alwaysUpdateField.FieldType != typeof(bool))
            return;

        int updated = 0;
        foreach (var item in allData)
        {
            if (!(item is CardData card)) continue;
            if (string.IsNullOrEmpty(card.UniqueID) || !modUniqueIds.Contains(card.UniqueID))
                continue;

            if (!(bool)alwaysUpdateField.GetValue(card))
            {
                alwaysUpdateField.SetValue(card, true);
                updated++;
            }
        }

        if (updated > 0)
            Log.Debug($"AlwaysUpdateService: enabled ticking on {updated} mod cards");
    }
}
