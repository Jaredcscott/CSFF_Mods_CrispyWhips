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
///       { "Card": "my_mod_item", "Chance": 8.0, "Quantity": [1, 1] },
///       { "Card": "my_mod_mushroom", "Chance": 5.0, "Seasons": ["Summer", "Autumn"] },
///       { "Card": "my_mod_chanterelle", "Chance": 4.0, "Seasons": ["Spring", "Summer", "Autumn"], "SeasonChance": { "Autumn": 12.0 } }
///     ]
///   }
/// ]
/// </code>
/// Locations: any of Uids (exact match), CardNameKeyContains (substring on CardName.LocalizationKey),
/// or Tags (CardTag runtime name) — the rule fires if ANY criterion matches.
/// Action: the DismantleAction's ActionName.DefaultText.
/// ActionMode: "exact" (default) or "contains" for substring matching.
/// Drops.Chance: 0–100 float base drop chance. Quantity defaults to [1,1].
/// Drops.Seasons (2.27.0): the seasons the drop can happen in, from Spring, Summer, Autumn (or Fall)
/// and Winter, case-insensitive. Absent, empty or all four = year-round. The drop gets one
/// StatsModifier per disallowed season, keyed on that season's SeasonCounter GameStat, that takes
/// 1000 off the chance while that season is active and 0 otherwise: the shape HerbsAndFungi's
/// BuildSeasonSuppressor shipped, and the same stats 1,329 vanilla drop modifiers key on. An unknown
/// name leaves that drop year-round with a warning; if a SeasonCounter stat does not resolve, every
/// seasoned drop stays year-round (never half-gated).
/// Drops.SeasonChance (2.28.0): an optional object of season name to that season's own absolute chance
/// (0-100, same scale as Chance), e.g. <c>{ "Autumn": 12.0 }</c>, for a seasonal flush or a lean season.
/// Names go through the same normalisation as Seasons (Fall is Autumn). Each valid entry becomes one
/// StatsModifier on that season's SeasonCounter stat, appended after the Seasons suppressors, that adds
/// (value - Chance) while that season is active and 0 otherwise, so the drop's chance is exactly the
/// value in that season. A value below Chance is a legal lean season. An entry is ignored with a warning
/// naming the mod, rule and card when its season is unknown or outside the row's Seasons (a suppressed
/// season stays suppressed), when its value is not a number or is negative, or when the season is named
/// twice; a SeasonChance that is not an object is ignored whole. A row without the key builds exactly as
/// before, and a year-round row (no Seasons) may carry it.
/// Idempotent — skips a drop if the same CardData is already present in the ProducedCards list, and
/// never applies Seasons or SeasonChance to that existing drop, which may be vanilla's or another
/// mod's.
/// </summary>
internal static class DropInjector
{
    private static readonly BindingFlags BF =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>The four vanilla SeasonCounter GameStats (UniqueIDScriptableGUID/GameStat.json), in
    /// the order <see cref="SeasonNames"/> lists them.</summary>
    private static readonly string[] SeasonCounterUids =
    {
        "6f3f0faaf50a7974e915ae0915f7e241",   // SeasonCounter_Spring
        "4a9284146ffc28242b1f591ac7f7541f",   // SeasonCounter_Summer
        "f6f436140ffd58644a6438c0838e05a1",   // SeasonCounter_Autumn
        "20042dca062d34a4b8e2968f9dc11f9f",   // SeasonCounter_Winter
    };

    private static readonly string[] SeasonNames = { "Spring", "Summer", "Autumn", "Winter" };

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
                if (parsed is not List<object> arr)
                {
                    Log.Warn($"DropInjector: {mod.Name} DropInjections.json skipped, its root must be a JSON array");
                    continue;
                }

                int modCount = 0;
                for (int ruleIndex = 0; ruleIndex < arr.Count; ruleIndex++)
                {
                    if (arr[ruleIndex] is not Dictionary<string, object> dict) continue;
                    var rule = ParseRule(dict, mod.Name, ruleIndex);
                    if (rule != null) { rule.ModName = mod.Name; rules.Add(rule); modCount++; }
                }
                Log.Debug($"DropInjector: loaded {modCount} rule(s) from {mod.Name}");
            }
            catch (Exception ex)
            {
                Log.Warn($"DropInjector: error reading {path}: {Log.ExceptionText(ex)}");
            }
        }

        if (rules.Count == 0) return;

        // 1b. Season gating and flushes key on the four SeasonCounter stats, resolved once per load. If
        // one is missing, every seasoned drop stays year-round and every flush stays at its base chance:
        // a half-gated drop reads as a working gate.
        object[] seasonStats = rules.Any(r => r.Drops.Any(d => d.Seasons != null || d.SeasonChance != null)) ? ResolveSeasonStats() : null;

        // 2. Build card lookup by UniqueID for resolving "Card" references in drop entries
        var cardLookup = BuildCardLookup(allData);

        // 2b. Unresolved references (framework README "Unresolved references"): a location UID or a
        // drop Card naming a UID that does not resolve is skipped and logged at Debug, once per row
        // rather than once per matched location, and counted in the Info summary. Debug, not Warn:
        // the common cause is a soft-dependency row naming another mod's card that is not installed.
        int unresolved = SkipUnresolved(rules, cardLookup);

        // 3. Walk all loaded cards; for each card that matches a rule's location criteria,
        //    find the named DismantleAction and append drops.
        int totalDrops = 0, gatedDrops = 0, flushedDrops = 0;
        foreach (var obj in allData)
        {
            if (obj is not CardData card) continue;

            // Collect which rules target this card (may be >1 when rules overlap)
            var matchingRules = new List<DropRule>();
            foreach (var rule in rules)
                if (MatchesLocation(card, rule)) matchingRules.Add(rule);
            if (matchingRules.Count == 0) continue;

            var daField = CardUtil.GetCachedField(card.GetType(), "DismantleActions");
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

                    var pcField = CardUtil.GetCachedField(action.GetType(), "ProducedCards");
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

                        var result = AppendDrop(producedCards, dropCard, drop, seasonStats, out var seasonEffect);
                        if (result == AppendResult.AlreadyPresent)
                        {
                            Log.Debug($"DropInjector: '{drop.CardUid}' is already in '{actionName}' on {card.name}; not appended"
                                    + SkippedSeasonKeys(drop));
                            continue;
                        }
                        if (result == AppendResult.Failed) continue;

                        totalDrops++;
                        cardModified = true;
                        if ((seasonEffect & SeasonEffect.Gated) != 0) gatedDrops++;
                        if ((seasonEffect & SeasonEffect.Flushed) != 0) flushedDrops++;
                    }
                }
            }

            // Vanilla location cards must be marked dirty so NullReferenceCompactor
            // re-walks their mutated DroppedCards arrays.
            if (cardModified)
                Loading.FrameworkDirtyTracker.MarkDirty(card);
        }

        // ", N season-gated" is a contract: HerbsAndFungi's T2.296 scenario matches ", 0 season-gated".
        Log.Info($"DropInjector: {totalDrops} drop(s) appended, {gatedDrops} season-gated, {flushedDrops} season-flushed"
               + (unresolved > 0 ? $", {unresolved} unresolved reference(s) skipped (Debug-logged)" : ""));
    }

    /// <summary>Removes every location UID and drop Card that names no loaded card, with one Debug line
    /// each, and returns how many were removed. A rule whose only location UIDs all miss then matches
    /// nothing (MatchesLocation needs a non-empty set), so no drop is appended anywhere for it.</summary>
    private static int SkipUnresolved(List<DropRule> rules, Dictionary<string, object> cardLookup)
    {
        int unresolved = 0;
        foreach (var rule in rules)
        {
            foreach (var uid in rule.Uids.ToList())
            {
                if (cardLookup.ContainsKey(uid)) continue;
                Log.Debug($"DropInjector: {rule.ModName}: location UID '{uid}' does not resolve, skipped");
                rule.Uids.Remove(uid);
                unresolved++;
            }
            for (int i = rule.Drops.Count - 1; i >= 0; i--)
            {
                var uid = rule.Drops[i].CardUid;
                if (!string.IsNullOrEmpty(uid) && cardLookup.ContainsKey(uid)) continue;
                Log.Debug($"DropInjector: {rule.ModName}: drop Card '{uid}' does not resolve, skipped");
                rule.Drops.RemoveAt(i);
                unresolved++;
            }
        }
        return unresolved;
    }

    /// <summary>The tail of the AlreadyPresent Debug line: which of the drop's season keys were not
    /// applied because the existing drop (vanilla's or another mod's) was left alone.</summary>
    private static string SkippedSeasonKeys(DropEntry drop)
    {
        if (drop.Seasons != null && drop.SeasonChance != null)
            return ", and its Seasons and SeasonChance were not applied to the existing drop";
        if (drop.Seasons != null) return ", and its Seasons were not applied to the existing drop";
        if (drop.SeasonChance != null) return ", and its SeasonChance was not applied to the existing drop";
        return "";
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
            var tagsField = CardUtil.GetCachedField(card.GetType(), "CardTags");
            if (tagsField?.GetValue(card) is Array tags)
                for (int i = 0; i < tags.Length; i++)
                    if (tags.GetValue(i) is UnityEngine.Object tag && rule.Tags.Contains(tag.name))
                        return true;
        }

        return false;
    }

    // ── Drop append ──────────────────────────────────────────────────────────

    private enum AppendResult { Failed, AlreadyPresent, Appended }

    /// <summary>What the season keys did to an appended drop; a drop can be both.</summary>
    [Flags]
    private enum SeasonEffect { None = 0, Gated = 1, Flushed = 2 }

    private static AppendResult AppendDrop(IList producedCards, object dropCard, DropEntry drop, object[] seasonStats, out SeasonEffect seasonEffect)
    {
        seasonEffect = SeasonEffect.None;
        var collection = producedCards[0];
        if (collection == null) return AppendResult.Failed;

        var dropsField = CardUtil.GetCachedField(collection.GetType(), "DroppedCards");
        if (dropsField?.GetValue(collection) is not Array dropsArray)
        {
            WarnOnce("droppedcards", $"{collection.GetType().Name}.DroppedCards is missing or not an array; no drop can be appended");
            return AppendResult.Failed;
        }

        var dropType = dropsArray.GetType().GetElementType();
        if (dropType == null) return AppendResult.Failed;

        var dcField = CardUtil.GetCachedField(dropType, "DroppedCard");
        if (dcField == null)
        {
            WarnOnce("droppedcard", $"{dropType.Name}.DroppedCard is missing; no drop can be appended");
            return AppendResult.Failed;
        }

        // Idempotency: skip if this CardData already appears in the drops list.
        for (int i = 0; i < dropsArray.Length; i++)
        {
            var existing = dropsArray.GetValue(i);
            if (existing != null && dcField.GetValue(existing) == dropCard) return AppendResult.AlreadyPresent;
        }

        // A CardDropChance left at its default is inactive, and CurrentDropChance returns 100 for
        // an inactive chance, so a drop without one would drop every time. Refuse instead.
        var dcChanceField = CardUtil.GetCachedField(dropType, "DropChance");
        if (dcChanceField == null)
        {
            WarnOnce("dropchance", $"{dropType.Name}.DropChance is missing; drops are not appended (an unset chance drops every time)");
            return AppendResult.Failed;
        }

        // Create new drop struct (boxed for mutable field writes)
        object newDrop = Activator.CreateInstance(dropType);
        dcField.SetValue(newDrop, dropCard);

        CardUtil.GetCachedField(dropType, "Quantity")?.SetValue(newDrop, new Vector2Int(drop.QuantityMin, drop.QuantityMax));

        object dcObj = Activator.CreateInstance(dcChanceField.FieldType);
        CardUtil.GetCachedField(dcChanceField.FieldType, "Active")?.SetValue(dcObj, true);
        CardUtil.GetCachedField(dcChanceField.FieldType, "BaseDropChance")?.SetValue(dcObj, drop.Chance);
        if ((drop.Seasons != null || drop.SeasonChance != null) && seasonStats != null)
            seasonEffect = ApplySeasons(dcObj, drop, seasonStats);
        dcChanceField.SetValue(newDrop, dcObj);

        var newArray = Array.CreateInstance(dropType, dropsArray.Length + 1);
        Array.Copy(dropsArray, newArray, dropsArray.Length);
        newArray.SetValue(newDrop, dropsArray.Length);
        dropsField.SetValue(collection, newArray);
        return AppendResult.Appended;
    }

    // ── Season gating ────────────────────────────────────────────────────────

    /// <summary>The tail of every warning that season gating or a flush cannot be built. Its first half
    /// is a contract: HerbsAndFungi's T2.296 and T2.1003 harness scenarios assert that exact phrase is
    /// ABSENT from the boot log, so rewording it would leave both checks passing on a real failure.</summary>
    private const string SeasonsOff = "every drop with Seasons stays year-round and every SeasonChance stays at its base chance";

    /// <summary>The output of a season suppressor while its season is active.</summary>
    private const float SuppressorOutput = -1000f;

    /// <summary>Resolves the four SeasonCounter GameStats in <see cref="SeasonNames"/> order, or null
    /// (with one warning naming what is missing) when any does not resolve.</summary>
    private static object[] ResolveSeasonStats()
    {
        var stats = new object[SeasonCounterUids.Length];
        var missing = new List<string>();
        for (int i = 0; i < SeasonCounterUids.Length; i++)
        {
            stats[i] = GameRegistry.GetByUid<GameStat>(SeasonCounterUids[i]);
            if (stats[i] == null) missing.Add($"SeasonCounter_{SeasonNames[i]} ({SeasonCounterUids[i]})");
        }
        if (missing.Count == 0) return stats;
        Log.Warn($"DropInjector: {string.Join(", ", missing)} not found; {SeasonsOff}");
        return null;
    }

    /// <summary>Writes the drop's season modifiers into the boxed CardDropChance: one suppressor per
    /// season the row disallows, in season order, then one flush per SeasonChance entry, in season order.
    /// Returns what it wrote. Writes nothing and returns None, leaving the chance untouched, if any piece
    /// is missing, so a drop is never half-gated.</summary>
    private static SeasonEffect ApplySeasons(object dropChanceBox, DropEntry drop, object[] seasonStats)
    {
        var suppressed = new List<int>(3);
        if (drop.Seasons != null)
            for (int i = 0; i < SeasonNames.Length; i++)
                if (Array.IndexOf(drop.Seasons, SeasonNames[i]) < 0) suppressed.Add(i);
        var flushes = drop.SeasonChance ?? Array.Empty<SeasonFlush>();
        if (suppressed.Count == 0 && flushes.Length == 0) return SeasonEffect.None;

        var modifiersField = CardUtil.GetCachedField(dropChanceBox.GetType(), "StatsModifiers");
        var elemType = modifiersField?.FieldType.GetElementType();
        if (elemType == null)
        {
            WarnOnce("statsmodifiers", $"CardDropChance.StatsModifiers is missing; {SeasonsOff}");
            return SeasonEffect.None;
        }

        var arr = Array.CreateInstance(elemType, suppressed.Count + flushes.Length);
        int n = 0;
        foreach (var season in suppressed)
        {
            var suppressor = BuildSeasonModifier(elemType, seasonStats[season], SuppressorOutput);
            if (suppressor == null) return SeasonEffect.None;
            arr.SetValue(suppressor, n++);
        }
        foreach (var flush in flushes)
        {
            // CurrentDropChance sums BaseDropChance and every modifier, so adding (value - Chance) while
            // the season is active makes the chance exactly the value; below Chance it is a lean season.
            var modifier = BuildSeasonModifier(elemType, seasonStats[flush.Season], flush.Chance - drop.Chance);
            if (modifier == null) return SeasonEffect.None;
            arr.SetValue(modifier, n++);
        }
        modifiersField.SetValue(dropChanceBox, arr);
        return (suppressed.Count > 0 ? SeasonEffect.Gated : SeasonEffect.None)
             | (flushes.Length > 0 ? SeasonEffect.Flushed : SeasonEffect.None);
    }

    /// <summary>One boxed StatInterpolatedValue that returns <paramref name="output"/> while the season's
    /// counter is in [1, 999999] (its season is active) and 0 otherwise. A suppressor passes -1000, the
    /// values ported unchanged from HerbsAndFungi's BuildSeasonSuppressor; a flush passes the difference
    /// from the row's Chance. Null, with one warning per cause, when a member is missing.</summary>
    private static object BuildSeasonModifier(Type statInterpType, object seasonStat, float output)
    {
        try
        {
            var inputStatField = CardUtil.GetCachedField(statInterpType, "InputStat");
            var valueField = CardUtil.GetCachedField(statInterpType, "Value");   // InterpolatedValue
            if (inputStatField == null || valueField == null)
            {
                WarnOnce("statinterp", $"{statInterpType.Name}.InputStat or .Value is missing; {SeasonsOff}");
                return null;
            }

            var ivType = valueField.FieldType;
            var activeField = CardUtil.GetCachedField(ivType, "Active");
            var inputRangeField = CardUtil.GetCachedField(ivType, "InputValueRange");
            var outputRangeField = CardUtil.GetCachedField(ivType, "OutputValueRange");
            var outOfRangeField = CardUtil.GetCachedField(ivType, "WhenOutOfRange");
            if (activeField == null || inputRangeField == null || outputRangeField == null || outOfRangeField == null)
            {
                WarnOnce("interp", $"{ivType.Name} is missing Active, InputValueRange, OutputValueRange or WhenOutOfRange; {SeasonsOff}");
                return null;
            }

            var sm = Activator.CreateInstance(statInterpType);
            inputStatField.SetValue(sm, seasonStat);
            CardUtil.GetCachedField(statInterpType, "UseStatPercentage")?.SetValue(sm, false);

            var iv = Activator.CreateInstance(ivType);
            activeField.SetValue(iv, true);
            inputRangeField.SetValue(iv, new Vector2(1f, 999999f));
            // x == y, so GetInterpolatedValue returns x exactly while the counter is in range.
            outputRangeField.SetValue(iv, new Vector2(output, output));
            // By name, so a reordered game enum throws here instead of writing the wrong member.
            outOfRangeField.SetValue(iv, Enum.Parse(outOfRangeField.FieldType, "Return0Value"));

            valueField.SetValue(sm, iv);
            return sm;
        }
        catch (Exception ex)
        {
            WarnOnce("suppressor", $"building a season modifier failed; {SeasonsOff}: {Log.ExceptionText(ex)}");
            return null;
        }
    }

    private static readonly HashSet<string> _warned = new(StringComparer.Ordinal);

    private static void WarnOnce(string cause, string message)
    {
        if (_warned.Add(cause)) Log.Warn($"DropInjector: {message}");
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
        var nameField = CardUtil.GetCachedField(action.GetType(), "ActionName");
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

    private static DropRule ParseRule(Dictionary<string, object> dict, string modName, int ruleIndex)
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

                var seasons = dd.TryGetValue("Seasons", out var sv) ? ParseSeasons(sv, modName, ruleIndex, cardUid) : null;
                rule.Drops.Add(new DropEntry
                {
                    CardUid = cardUid,
                    Chance = chance,
                    QuantityMin = Math.Max(1, qMin),
                    QuantityMax = Math.Max(1, qMax),
                    Seasons = seasons,
                    SeasonChance = dd.TryGetValue("SeasonChance", out var scv)
                        ? ParseSeasonChance(scv, seasons, modName, ruleIndex, cardUid) : null,
                });
            }
        }

        return rule.Drops.Count > 0 ? rule : null;
    }

    /// <summary>The allowed seasons in canonical spelling, or null for year-round (absent, empty,
    /// all four, or invalid). Invalid is year-round because that is what the drop did before it
    /// carried the key, so a typo never makes a drop rarer than its author shipped it.</summary>
    private static string[] ParseSeasons(object raw, string modName, int ruleIndex, string cardUid)
    {
        if (raw == null) return null;
        if (raw is not List<object> list)
        {
            Log.Warn($"DropInjector: {modName} DropInjections.json rule [{ruleIndex}], drop '{cardUid}': Seasons must be an array of season names; the drop stays year-round");
            return null;
        }

        var allowed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in list)
        {
            var name = NormalizeSeason(entry as string);
            if (name == null)
            {
                Log.Warn($"DropInjector: {modName} DropInjections.json rule [{ruleIndex}], drop '{cardUid}': unknown season '{entry}' in Seasons "
                       + "(expected Spring, Summer, Autumn or Fall, Winter); the drop stays year-round");
                return null;
            }
            allowed.Add(name);
        }
        return allowed.Count is 0 or 4 ? null : allowed.ToArray();
    }

    /// <summary>The valid SeasonChance entries as one flush per season, in season order, or null when
    /// there are none (absent, null, not an object, or every entry ignored). An invalid entry is ignored
    /// with a warning that names the mod, rule, card and season, and never takes a valid sibling with
    /// it: an author who typed one season wrong still gets the others. A value below the row's Chance is
    /// legal (a lean season), so only a negative CHANCE warns, never a negative difference.
    /// <paramref name="allowedSeasons"/> is the row's parsed Seasons (null = year-round, all four allowed).</summary>
    private static SeasonFlush[] ParseSeasonChance(object raw, string[] allowedSeasons, string modName, int ruleIndex, string cardUid)
    {
        if (raw == null) return null;
        var where = $"{modName} DropInjections.json rule [{ruleIndex}], drop '{cardUid}'";
        if (raw is not Dictionary<string, object> map)
        {
            Log.Warn($"DropInjector: {where}: SeasonChance must be an object of season name to chance, such as {{\"Autumn\": 12.0}}; ignored");
            return null;
        }

        var bySeason = new float?[SeasonNames.Length];
        foreach (var entry in map)
        {
            var name = NormalizeSeason(entry.Key);
            if (name == null)
            {
                Log.Warn($"DropInjector: {where}: SeasonChance entry '{entry.Key}' ignored: unknown season (expected Spring, Summer, Autumn or Fall, Winter)");
                continue;
            }
            if (!TryGetChance(entry.Value, out var chance))
            {
                var got = entry.Value switch
                {
                    null => "null",
                    string text => $"\"{text}\"",
                    bool flag => flag ? "true" : "false",
                    List<object> => "an array",
                    Dictionary<string, object> => "an object",
                    _ => entry.Value.ToString(),
                };
                Log.Warn($"DropInjector: {where}: SeasonChance entry '{entry.Key}' ignored: the chance must be a finite number, got {got}");
                continue;
            }
            if (chance < 0f)
            {
                Log.Warn($"DropInjector: {where}: SeasonChance entry '{entry.Key}' ignored: the chance cannot be negative, got {chance}");
                continue;
            }
            if (allowedSeasons != null && Array.IndexOf(allowedSeasons, name) < 0)
            {
                Log.Warn($"DropInjector: {where}: SeasonChance entry '{entry.Key}' ignored: {name} is outside this drop's Seasons, so it stays suppressed");
                continue;
            }
            var index = Array.IndexOf(SeasonNames, name);
            if (bySeason[index].HasValue)
            {
                Log.Warn($"DropInjector: {where}: SeasonChance entry '{entry.Key}' ignored: {name} is already set by another entry (Fall and Autumn are one season)");
                continue;
            }
            bySeason[index] = chance;
        }

        var flushes = new List<SeasonFlush>(SeasonNames.Length);
        for (int i = 0; i < bySeason.Length; i++)
            if (bySeason[i].HasValue) flushes.Add(new SeasonFlush { Season = i, Chance = bySeason[i].Value });
        return flushes.Count == 0 ? null : flushes.ToArray();
    }

    private static string NormalizeSeason(string s) => s?.Trim().ToLowerInvariant() switch
    {
        "spring" => "Spring",
        "summer" => "Summer",
        "autumn" or "fall" => "Autumn",
        "winter" => "Winter",
        _ => null,
    };

    private static int ToInt(object v) =>
        v is double d ? (int)d : v is long l ? (int)l : v is int i ? i : 0;

    private static float ToFloat(object v) =>
        v is double d ? (float)d : v is long l ? (float)l : v is float f ? f : v is int i ? i : 0f;

    /// <summary>A JSON number as a float, or false for anything else (a string, a bool, null, an array,
    /// and a number too large for a float). Unlike <see cref="ToFloat"/> it cannot read a bad value as 0,
    /// which for a chance would be a legal, silent "never".</summary>
    private static bool TryGetChance(object v, out float chance)
    {
        switch (v)
        {
            case double d: chance = (float)d; break;
            case long l: chance = l; break;
            case int i: chance = i; break;
            case float f: chance = f; break;
            default: chance = 0f; return false;
        }
        return !float.IsNaN(chance) && !float.IsInfinity(chance);
    }

    // ── Data types ───────────────────────────────────────────────────────────

    private sealed class DropRule
    {
        public string ModName;
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
        /// <summary>Allowed seasons in canonical spelling; null = year-round.</summary>
        public string[] Seasons;
        /// <summary>One flush per valid SeasonChance entry, in season order; null = none.</summary>
        public SeasonFlush[] SeasonChance;
    }

    /// <summary>One SeasonChance entry: the season (an index into <see cref="SeasonNames"/>) and that
    /// season's absolute chance, on the same 0-100 scale as the drop's Chance.</summary>
    private struct SeasonFlush
    {
        public int Season;
        public float Chance;
    }
}
