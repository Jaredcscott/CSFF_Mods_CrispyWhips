using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CSFFModFramework.Loading;

/// <summary>
/// Reads optional <c>SpawnStatDefaults</c> blocks from mod card JSON and registers
/// them with <see cref="SpawnService"/> so they are applied to every matching spawn
/// (ProducedCards, OnFull, perk kits — any GiveCard path) without per-mod postfixes.
///
/// Add a <c>SpawnStatDefaults</c> block to any <c>CardData/*.json</c>:
/// <code>
/// {
///   "UniqueID": "my_mod_item",
///   "CardType": 0,
///   ...
///   "SpawnStatDefaults": {
///     "SpecialDurability4": 200.0,
///     "UsageDurability": 100.0
///   }
/// }
/// </code>
/// Stat names accept JSON-side names ("SpecialDurability4", "SpoilageTime") or
/// runtime names ("CurrentSpoilage") — resolved via <see cref="Api.CardUtil.SetDurability"/>.
/// Explicit <see cref="SpawnService.OnNextSpawn"/> overrides take priority over defaults.
/// </summary>
internal static class SpawnStatDefaultsLoader
{
    public static void LoadAll()
    {
        int count = 0;
        foreach (var kvp in JsonDataLoader.ParsedJsonByUniqueId)
        {
            var uid    = kvp.Key;
            var parsed = kvp.Value;

            if (!parsed.TryGetValue("SpawnStatDefaults", out var ssdVal)) continue;
            if (ssdVal is not Dictionary<string, object> ssdDict || ssdDict.Count == 0) continue;

            var defaults = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (var entry in ssdDict)
            {
                var val = entry.Value;
                float f = val is double d ? (float)d
                        : val is long l   ? (float)l
                        : val is int  i   ? (float)i
                        : val is float fv ? fv
                        : 0f;
                defaults[entry.Key] = f;
            }

            if (defaults.Count > 0)
            {
                SpawnService.RegisterStatDefault(uid, defaults);
                count++;
            }
        }

        if (count > 0)
            Log.Debug($"SpawnStatDefaultsLoader: registered defaults for {count} card(s)");
    }
}
