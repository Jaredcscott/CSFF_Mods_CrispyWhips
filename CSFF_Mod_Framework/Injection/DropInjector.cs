using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CSFFModFramework.Data;
using CSFFModFramework.Discovery;
using CSFFModFramework.Util;
using HarmonyLib;
using UnityEngine;

namespace CSFFModFramework.Injection;

/// <summary>
/// Reads DropInjections.json from each mod and appends CardDrop entries to matching
/// DismantleAction ProducedCards on location cards. Provides a declarative alternative
/// to per-mod C# forage injection loops (e.g. HerbsAndFungi's ~400 LOC injection).
///
/// JSON schema (DropInjections.json in mod root):
/// <code>
/// [
///   {
///     "Locations": {
///       "Uids": ["exact_uid"],
///       "CardNameKeyContains": ["GroveOak", "River_"],
///       "Tags": ["tag_ForestBiome"]
///     },
///     "Action": "Forage",
///     "ActionMode": "exact",
///     "Drops": [
///       { "Card": "my_mod_item", "Chance": 8.0, "Quantity": [1, 1] }
///     ]
///   }
/// ]
/// </code>
/// Locations: any of Uids (exact match), CardNameKeyContains (substring on CardName.LocalizationKey),
/// or Tags (CardTag runtime name) — the rule fires if ANY criterion matches.
/// Action: the DismantleAction's ActionName.DefaultText.
/// ActionMode: "exact" (default) or "contains" for substring matching.
/// Drops.Chance: 0–100 float base drop chance. Quantity defaults to [1,1].
/// Idempotent — skips a drop if the same CardData is already present in the ProducedCards list.
/// </summary>
internal static class DropInjector
{
    private static readonly BindingFlags BF =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static void InjectAll(IEnumerable allData, List<ModManifest> mods)
    {
        // 1. Collect injection rules from all mods
        var rules = new List<DropRule>();
        foreach (var mod in mods)
        {
            var path = Path.Combine(mod.DirectoryPath, "DropInjections.json");
            if (!File.Exists(path)) continue;
            try
            {
                var json = File.ReadAllText(path);
                var parsed = MiniJson.Parse(json);
                if (parsed is not List<object> arr) continue;

                int modCount = 0;
                foreach (var item in arr)
                {
                    if (item is not Dictionary<string, object> dict) continue;
                    var rule = ParseRule(dict);
                    if (rule != null) { rules.Add(rule); modCount++; }
                }
                Log.Debug($"DropInjector: loaded {modCount} rule(s) from {mod.Name}");
            }
            catch (Exception ex)
            {
                Log.Warn($"DropInjector: error reading {path}: {Log.ExceptionText(ex)}");
            }
        }

        if (rules.Count == 0) return;

        // 2. Build card lookup by UniqueID for resolving "Card" references in drop entries
        var cardLookup = BuildCardLookup(allData);

        // 3. Walk all loaded cards; for each card that matches a rule's location criteria,
        //    find the named DismantleAction and append drops.
        int totalDrops = 0;
        foreach (var obj in allData)
        {
            if (obj is not CardData card) continue;

            // Collect which rules target this card (may be >1 when rules overlap)
            var matchingRules = new List<DropRule>();
            foreach (var rule in rules)
                if (MatchesLocation(card, rule)) matchingRules.Add(rule);
            if (matchingRules.Count == 0) continue;

            var daField = AccessTools.Field(card.GetType(), "DismantleActions");
            var dismantleActions = daField?.GetValue(card) as IList;
            if (dismantleActions == null || dismantleActions.Count == 0) continue;

            bool cardModified = false;
            foreach (var action in dismantleActions)
            {
                if (action == null) continue;
                var actionName = GetActionDefaultText(action);
                if (actionName == null) continue;

                foreach (var rule in matchingRules)
                {
                    bool nameMatch = rule.ActionExact
                        ? string.Equals(actionName, rule.Action, StringComparison.OrdinalIgnoreCase)
                        : actionName.IndexOf(rule.Action, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!nameMatch) continue;

                    var pcField = AccessTools.Field(action.GetType(), "ProducedCards");
                    var producedCards = pcField?.GetValue(action) as IList;
                    if (producedCards == null || producedCards.Count == 0) continue;

                    foreach (var drop in rule.Drops)
                    {
                        if (!cardLookup.TryGetValue(drop.CardUid, out var dropCard))
                        {
                            // Debug, not Warn: a missing card is the expected, common case for a
                            // soft-dependency drop list (e.g. another mod's items, only present
                            // when that mod is installed) — Warn-per-missing-item would spam the
                            // log on every load for players without the optional mod.
                            Log.Debug($"DropInjector: card '{drop.CardUid}' not found — skipping drop entry");
                            continue;
                        }
                        if (AppendDrop(producedCards, dropCard, drop.Chance, drop.QuantityMin, drop.QuantityMax))
                        {
                            totalDrops++;
                            cardModified = true;
                        }
                    }
                }
            }

            // Vanilla location cards must be marked dirty so NullReferenceCompactor
            // re-walks their mutated DroppedCards arrays.
            if (cardModified)
                Loading.FrameworkDirtyTracker.MarkDirty(card);
        }

        Log.Debug($"DropInjector: {totalDrops} drop(s) injected across location cards");
    }

    // ── Location matching ────────────────────────────────────────────────────

    private static bool MatchesLocation(CardData card, DropRule rule)
    {
        // Exact UID
        if (rule.Uids.Count > 0 && rule.Uids.Contains(card.UniqueID))
            return true;

        // CardName.LocalizationKey contains pattern
        if (rule.CardNameKeyContains.Count > 0)
        {
            var cardNameField = card.GetType().GetField("CardName", BF);
            var nameObj = cardNameField?.GetValue(card);
            if (nameObj != null)
            {
                var locKey = GetStringMember(nameObj, "LocalizationKey");
                if (!string.IsNullOrEmpty(locKey))
                    foreach (var pattern in rule.CardNameKeyContains)
                        if (locKey.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                            return true;
            }
        }

        // CardTag runtime name
        if (rule.Tags.Count > 0)
        {
            var tagsField = AccessTools.Field(card.GetType(), "CardTags");
            if (tagsField?.GetValue(card) is Array tags)
                for (int i = 0; i < tags.Length; i++)
                    if (tags.GetValue(i) is UnityEngine.Object tag && rule.Tags.Contains(tag.name))
                        return true;
        }

        return false;
    }

    // ── Drop append ──────────────────────────────────────────────────────────

    private static bool AppendDrop(IList producedCards, object dropCard,
                                   float chance, int qMin, int qMax)
    {
        var collection = producedCards[0];
        if (collection == null) return false;

        var dropsField = AccessTools.Field(collection.GetType(), "DroppedCards");
        if (dropsField?.GetValue(collection) is not Array dropsArray) return false;

        var dropType = dropsArray.GetType().GetElementType();
        if (dropType == null) return false;

        var dcField = AccessTools.Field(dropType, "DroppedCard");

        // Idempotency: skip if this CardData already appears in the drops list.
        for (int i = 0; i < dropsArray.Length; i++)
        {
            var existing = dropsArray.GetValue(i);
            if (existing != null && dcField?.GetValue(existing) == dropCard) return false;
        }

        // Create new drop struct (boxed for mutable field writes)
        object newDrop = Activator.CreateInstance(dropType);
        dcField?.SetValue(newDrop, dropCard);

        AccessTools.Field(dropType, "Quantity")?.SetValue(newDrop, new Vector2Int(qMin, qMax));

        var dcChanceField = AccessTools.Field(dropType, "DropChance");
        if (dcChanceField != null)
        {
            object dcObj = Activator.CreateInstance(dcChanceField.FieldType);
            AccessTools.Field(dcChanceField.FieldType, "Active")?.SetValue(dcObj, true);
            AccessTools.Field(dcChanceField.FieldType, "BaseDropChance")?.SetValue(dcObj, chance);
            dcChanceField.SetValue(newDrop, dcObj);
        }

        var newArray = Array.CreateInstance(dropType, dropsArray.Length + 1);
        Array.Copy(dropsArray, newArray, dropsArray.Length);
        newArray.SetValue(newDrop, dropsArray.Length);
        dropsField.SetValue(collection, newArray);
        return true;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Dictionary<string, object> BuildCardLookup(IEnumerable allData)
    {
        var lookup = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var obj in allData)
            if (obj is CardData cd && !string.IsNullOrEmpty(cd.UniqueID))
                lookup[cd.UniqueID] = cd;

        // Fallback for any cards registered in AllUniqueObjects but not yet in AllData
        var allUnique = GameRegistry.AllUniqueObjects;
        if (allUnique != null)
            foreach (DictionaryEntry entry in allUnique)
                if (entry.Value is CardData cd2 && !string.IsNullOrEmpty(cd2.UniqueID)
                    && !lookup.ContainsKey(cd2.UniqueID))
                    lookup[cd2.UniqueID] = cd2;

        return lookup;
    }

    private static string GetActionDefaultText(object action)
    {
        var nameField = AccessTools.Field(action.GetType(), "ActionName");
        var nameObj = nameField?.GetValue(action);
        return nameObj == null ? null : GetStringMember(nameObj, "DefaultText");
    }

    private static string GetStringMember(object obj, string memberName)
    {
        var fi = obj.GetType().GetField(memberName, BF);
        if (fi != null) return fi.GetValue(obj) as string;
        var pi = obj.GetType().GetProperty(memberName, BF);
        return pi?.GetValue(obj) as string;
    }

    // ── JSON parsing ─────────────────────────────────────────────────────────

    private static DropRule ParseRule(Dictionary<string, object> dict)
    {
        var rule = new DropRule();

        if (dict.TryGetValue("Locations", out var locVal) && locVal is Dictionary<string, object> locDict)
        {
            if (locDict.TryGetValue("Uids", out var uidsVal) && uidsVal is List<object> uidsList)
                foreach (var u in uidsList) if (u is string s) rule.Uids.Add(s);

            if (locDict.TryGetValue("CardNameKeyContains", out var keyVal) && keyVal is List<object> keyList)
                foreach (var k in keyList) if (k is string s) rule.CardNameKeyContains.Add(s);

            if (locDict.TryGetValue("Tags", out var tagsVal) && tagsVal is List<object> tagsList)
                foreach (var t in tagsList) if (t is string s) rule.Tags.Add(s);
        }

        rule.Action = dict.TryGetValue("Action", out var actVal) ? actVal as string : null;
        if (dict.TryGetValue("ActionMode", out var modeVal) && modeVal is string mode)
            rule.ActionExact = !string.Equals(mode, "contains", StringComparison.OrdinalIgnoreCase);

        // Both Action and at least one location criterion are required
        if (string.IsNullOrEmpty(rule.Action)) return null;
        if (rule.Uids.Count == 0 && rule.CardNameKeyContains.Count == 0 && rule.Tags.Count == 0) return null;

        if (dict.TryGetValue("Drops", out var dropsVal) && dropsVal is List<object> dropsList)
        {
            foreach (var d in dropsList)
            {
                if (d is not Dictionary<string, object> dd) continue;
                var cardUid = dd.TryGetValue("Card", out var c) ? c as string : null;
                if (string.IsNullOrEmpty(cardUid)) continue;

                float chance = dd.TryGetValue("Chance", out var ch) ? ToFloat(ch) : 10f;
                int qMin = 1, qMax = 1;
                if (dd.TryGetValue("Quantity", out var qv) && qv is List<object> ql && ql.Count >= 2)
                { qMin = ToInt(ql[0]); qMax = ToInt(ql[1]); }

                rule.Drops.Add(new DropEntry
                {
                    CardUid = cardUid,
                    Chance = chance,
                    QuantityMin = Math.Max(1, qMin),
                    QuantityMax = Math.Max(1, qMax),
                });
            }
        }

        return rule.Drops.Count > 0 ? rule : null;
    }

    private static int ToInt(object v) =>
        v is double d ? (int)d : v is long l ? (int)l : v is int i ? i : 0;

    private static float ToFloat(object v) =>
        v is double d ? (float)d : v is long l ? (float)l : v is float f ? f : v is int i ? i : 0f;

    // ── Data types ───────────────────────────────────────────────────────────

    private sealed class DropRule
    {
        public HashSet<string> Uids = new(StringComparer.OrdinalIgnoreCase);
        public List<string> CardNameKeyContains = new();
        public HashSet<string> Tags = new(StringComparer.OrdinalIgnoreCase);
        public string Action;
        public bool ActionExact = true;
        public List<DropEntry> Drops = new();
    }

    private struct DropEntry
    {
        public string CardUid;
        public float Chance;
        public int QuantityMin;
        public int QuantityMax;
    }
}
