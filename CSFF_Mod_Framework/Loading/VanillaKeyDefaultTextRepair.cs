using System.Collections;
using CSFFModFramework.Util;

namespace CSFFModFramework.Loading;

/// <summary>
/// Gives English players the vanilla English text when a mod card reuses a VANILLA card's
/// <c>LocalizationKey</c> but wrote its own <c>DefaultText</c> in Chinese (2.28.2).
///
/// <para>The game ships no English CSV (<c>StreamingAssets/Localization/</c> holds Cn/Jp/Kr/Ru only), so in
/// English a vanilla key is never in <c>LocalizationManager.CurrentTexts</c> and every
/// <see cref="LocalizedString"/> renders its own <c>DefaultText</c>. Vanilla's DefaultText is English, so
/// vanilla reads fine; a mod object carrying the same key with a Chinese DefaultText shows Chinese.
/// Chiwei's Additional Processing Equipment 0.0.25 does this on 10 blueprints (the cheese press and
/// cutting-board recipes are named <c>CheeseFirm_CardName</c>, <c>Fat_CardName</c> ... like the products
/// they make) and on their descriptions.</para>
///
/// <para>The repair copies the vanilla DefaultText for that same key when the mod's text holds CJK and the
/// vanilla text does not. It is language-independent on purpose: in Chinese (and Jp/Kr/Ru) the key
/// resolves from the game's own CSV, so DefaultText is never read there, and a player who switches to
/// English mid-session still gets English. A mod CSV row for the key wins over both, as before. Only mod
/// objects (<see cref="JsonDataLoader.UniqueIdToModName"/>) are written; vanilla is read-only here.</para>
/// </summary>
internal static class VanillaKeyDefaultTextRepair
{
    // Every LocalizedString field on CardData (.decomp/CardData.cs).
    private static readonly string[] Fields =
        { "CardName", "CardDescription", "CardHelpSection", "InventorySlotsText", "UnlockConditionsDesc" };

    internal static int RepairAll(IEnumerable allData) =>
        Repair(allData, uid => JsonDataLoader.UniqueIdToModName.ContainsKey(uid));

    /// <summary>Returns how many fields were rewritten. <paramref name="isModUid"/> separates mod from vanilla.</summary>
    internal static int Repair(IEnumerable allData, Func<string, bool> isModUid)
    {
        var fields = new List<System.Reflection.FieldInfo>();
        foreach (var name in Fields)
        {
            var f = typeof(CardData).GetField(name);
            if (f != null && f.FieldType == typeof(LocalizedString)) fields.Add(f);
            else Log.Warn($"VanillaKeyDefaultTextRepair: CardData.{name} is missing or no longer a LocalizedString - skipped");
        }
        if (fields.Count == 0) return 0;

        var mods = new List<CardData>();
        var vanillaText = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in allData)
        {
            if (item is not CardData card || card.UniqueID == null) continue;
            if (isModUid(card.UniqueID)) { mods.Add(card); continue; }
            foreach (var f in fields)
            {
                var ls = (LocalizedString)f.GetValue(card);
                if (!UsableKey(ls.LocalizationKey) || string.IsNullOrEmpty(ls.DefaultText) || ContainsCjk(ls.DefaultText)) continue;
                if (!vanillaText.ContainsKey(ls.LocalizationKey)) vanillaText[ls.LocalizationKey] = ls.DefaultText;
            }
        }

        int repaired = 0;
        var perMod = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var card in mods)
        {
            foreach (var f in fields)
            {
                var ls = (LocalizedString)f.GetValue(card);
                if (!UsableKey(ls.LocalizationKey) || !ContainsCjk(ls.DefaultText)) continue;
                if (!vanillaText.TryGetValue(ls.LocalizationKey, out var english)) continue;

                // A fresh struct, so the private LocalizedText cache cannot carry the Chinese string over.
                f.SetValue(card, new LocalizedString
                {
                    ParentObjectID = ls.ParentObjectID,
                    LocalizationKey = ls.LocalizationKey,
                    DefaultText = english,
                });
                repaired++;
                var mod = JsonDataLoader.UniqueIdToModName.TryGetValue(card.UniqueID, out var m) ? m : card.UniqueID;
                perMod[mod] = perMod.TryGetValue(mod, out var n) ? n + 1 : 1;
                Log.Debug($"VanillaKeyDefaultTextRepair: {card.UniqueID}.{f.Name} key '{ls.LocalizationKey}' -> vanilla text '{english}'");
            }
        }

        if (repaired > 0)
            Log.Info($"VanillaKeyDefaultTextRepair: {repaired} mod text field(s) reused a vanilla key with Chinese DefaultText; using the vanilla English text ({string.Join(", ", perMod.Select(kv => $"{kv.Key}: {kv.Value}"))})");
        return repaired;
    }

    private static bool UsableKey(string key) =>
        !string.IsNullOrEmpty(key) && key != LocalizedString.IgnoreKey;

    // CJK Unified Ideographs plus Extension A; a Chinese DefaultText always carries some.
    internal static bool ContainsCjk(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (var c in s)
            if ((c >= '一' && c <= '鿿') || (c >= '㐀' && c <= '䶿')) return true;
        return false;
    }
}
