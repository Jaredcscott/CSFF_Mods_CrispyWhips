using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using CSFFModFramework.Data;
using CSFFModFramework.Discovery;
using CSFFModFramework.Util;

namespace CSFFModFramework.Injection;

/// <summary>
/// Reads <c>TradingValues.json</c> from each mod root and writes the listed values onto
/// <c>CardData.TradingValue</c> — a declarative bulk-repricing mechanism so a mod can make
/// NPC trading meaningful without shipping one <c>GameSourceModify/</c> file per card
/// (the vanilla game leaves ~65% of items/liquids at 0, which trades as "free").
///
/// JSON schema (<c>TradingValues.json</c> in mod root) — a flat object map:
/// <code>
/// {
///   "_comment": "keys starting with _ are ignored",
///   "&lt;vanilla or mod CardData UniqueID&gt;": 25,
///   "Blueberries": 8.5
/// }
/// </code>
///
/// Values are applied unconditionally (a listed card's existing TradingValue is replaced,
/// zero or not) so the same file can retune already-priced cards. When two mods price the
/// same UID the later mod in load order wins and a Warn is logged. Targeted
/// <c>GameSourceModify/</c> patches still override this (GameSourceModifier runs later).
/// Idempotent — re-applying the same map on every LoadMainGameData is a no-op by value.
/// </summary>
internal static class TradingValueInjector
{
    public static void InjectAll(IEnumerable allData, List<ModManifest> mods)
    {
        // uid -> (value, owning mod) — later mods overwrite earlier ones, with a Warn.
        var priceMap = new Dictionary<string, (float value, string modName)>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in mods)
        {
            var path = Path.Combine(mod.DirectoryPath, "TradingValues.json");
            if (!File.Exists(path)) continue;
            try
            {
                var parsed = MiniJson.Parse(File.ReadAllText(path));
                if (parsed is not Dictionary<string, object> map)
                {
                    Log.Warn($"TradingValueInjector: {mod.Name} TradingValues.json root must be a JSON object map of UniqueID -> value");
                    continue;
                }

                int modCount = 0;
                foreach (var kv in map)
                {
                    if (string.IsNullOrEmpty(kv.Key) || kv.Key[0] == '_') continue;
                    if (kv.Value is not double d) // MiniJson parses every JSON number as double
                    {
                        Log.Warn($"TradingValueInjector: {mod.Name} '{kv.Key}' value is not a number — skipped");
                        continue;
                    }
                    var value = (float)d;
                    if (value < 0f)
                    {
                        Log.Warn($"TradingValueInjector: {mod.Name} '{kv.Key}' is negative ({value}) — skipped");
                        continue;
                    }
                    if (priceMap.TryGetValue(kv.Key, out var prior) && prior.modName != mod.Name)
                        Log.Warn($"TradingValueInjector: '{kv.Key}' priced by both {prior.modName} ({prior.value}) and {mod.Name} ({value}) — {mod.Name} wins (load order)");
                    priceMap[kv.Key] = (value, mod.Name);
                    modCount++;
                }
                Log.Debug($"TradingValueInjector: loaded {modCount} entries from {mod.Name}");
            }
            catch (Exception ex)
            {
                Log.Warn($"TradingValueInjector: error reading {path}: {Log.ExceptionText(ex)}");
            }
        }

        if (priceMap.Count == 0) return;

        int applied = 0, missing = 0;
        var cardLookup = BuildCardLookup(allData);
        foreach (var kv in priceMap)
        {
            if (!cardLookup.TryGetValue(kv.Key, out var card))
            {
                // Missing UIDs are Debug, not Warn: a fleet price table may cover content
                // from an optional sibling mod that isn't installed.
                Log.Debug($"TradingValueInjector: {kv.Value.modName}: '{kv.Key}' not found in registry — skipped");
                missing++;
                continue;
            }
            // No FrameworkDirtyTracker.MarkDirty: a float write creates no null refs for
            // NullReferenceCompactor to walk; marking would only add compaction cost.
            card.TradingValue = kv.Value.value;
            applied++;
        }

        Log.Info($"TradingValueInjector: {applied} trading value(s) applied" +
                 (missing > 0 ? $", {missing} UID(s) not found (Debug-logged)" : ""));
    }

    private static Dictionary<string, CardData> BuildCardLookup(IEnumerable allData)
    {
        var lookup = new Dictionary<string, CardData>(StringComparer.OrdinalIgnoreCase);
        foreach (var obj in allData)
            if (obj is CardData cd && !string.IsNullOrEmpty(cd.UniqueID))
                lookup[cd.UniqueID] = cd;

        var allUnique = GameRegistry.AllUniqueObjects;
        if (allUnique != null)
            foreach (DictionaryEntry entry in allUnique)
                if (entry.Value is CardData cd2 && !string.IsNullOrEmpty(cd2.UniqueID)
                    && !lookup.ContainsKey(cd2.UniqueID))
                    lookup[cd2.UniqueID] = cd2;

        return lookup;
    }
}
