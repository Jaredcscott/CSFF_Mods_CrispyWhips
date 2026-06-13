using CSFFModFramework.Util;

namespace CSFFModFramework.Loading;

/// <summary>
/// Supersedes duplicate <see cref="UniqueIDScriptable"/> instances created by other
/// mod loaders for UIDs the framework owns.
///
/// <para>Pikachu ModLoader (and ModCore) load every <c>plugins/&lt;dir&gt;/ModInfo.json</c>
/// mod themselves — including framework-native mods — inside a prefix on
/// <c>UniqueIDScriptable.ClearDict</c> that runs BEFORE the game's boot-time
/// <c>AllData[i].Init()</c> registration loop. Their duplicate instances therefore win
/// <c>GetFromID</c>, while the framework's instances (the ones injected into journal
/// tabs, perk groups, smelting recipes, and resolved by WarpResolver) lose.</para>
///
/// <para>Why that matters: blueprint research persistence works by save-restoring
/// blueprint MODEL CARDS through <c>GetFromID</c> (GiveCard's Blueprint case calls
/// <c>SetBpAvailable</c> on the resolved instance). If <c>GetFromID</c> returns loader
/// A's instance while the crafting journal tabs hold loader B's instance, the restored
/// Available state lands on an object the UI never reads — every mod blueprint shows
/// as unresearched after loading a save. The same identity split makes blueprint
/// <c>RequiredCard</c> slots reject crafted items (crafted card's CardModel is one
/// instance, the slot requires the other) while console-spawned items fit.</para>
///
/// <para>Fix: after the framework materializes and registers its instances, walk every
/// UID the framework loaded and make the framework instance canonical in all three
/// game-side registries: <c>AllUniqueObjects</c> (GetFromID), <c>AllUniqueObjectsAsInts</c>
/// (index lookups — swapped in place, index carried over), and
/// <c>GameLoad.Instance.DataBase.AllData</c> (the source InitializeStatsAndActions
/// iterates to seed AllBlueprintModels/BlueprintModelStates and to collect STAs,
/// Objectives, and QuestLogs each run). Foreign duplicates are removed from AllData so
/// those per-run loops see exactly one instance per UID.</para>
///
/// <para>MUST run after <see cref="JsonDataLoader.LoadAll"/> and BEFORE
/// <c>WarpResolver.ResolveAll</c>, so all warp resolution links against the canonical
/// instances.</para>
/// </summary>
internal static class ForeignInstanceReconciler
{
    public static void ReconcileAll()
    {
        var dict = Data.GameRegistry.AllUniqueObjects;
        if (dict == null)
        {
            Log.Warn("[LoaderCoexistence] AllUniqueObjects unavailable — cannot reconcile duplicate instances");
            return;
        }

        // foreign instance → our canonical instance
        var foreignByInstance = new Dictionary<UniqueIDScriptable, UniqueIDScriptable>();
        int rebound = 0, lateRegistered = 0;

        foreach (var kvp in JsonDataLoader.LoadedObjectsByUniqueId)
        {
            var uid = kvp.Key;
            var ours = kvp.Value;
            if (ours == null) continue;

            if (!dict.Contains(uid))
            {
                // TryRegister normally already added it; cover the gap defensively.
                try { dict.Add(uid, ours); lateRegistered++; } catch { }
                continue;
            }

            var existing = dict[uid] as UniqueIDScriptable;
            if (ReferenceEquals(existing, ours)) continue;

            dict[uid] = ours;
            rebound++;
            if (existing != null && !foreignByInstance.ContainsKey(existing))
                foreignByInstance.Add(existing, ours);

            // Our instance lost the earlier Init() race and sits in the game's
            // Duplicates list; it is the registered instance now — drop the marker.
            try { UniqueIDScriptable.Duplicates?.Remove(ours); } catch { }
        }

        if (rebound == 0)
        {
            if (lateRegistered > 0)
                Log.Debug($"[LoaderCoexistence] {lateRegistered} UID(s) registered late; no foreign duplicates found");
            return;
        }

        // Swap the foreign instances out of the index-lookup list in place so
        // GetFromIndex keeps working, carrying the assigned UniqueIDIndex over.
        int swapped = 0;
        try
        {
            var asInts = UniqueIDScriptable.AllUniqueObjectsAsInts;
            if (asInts != null)
            {
                for (int i = 0; i < asInts.Count; i++)
                {
                    var entry = asInts[i];
                    if (entry != null && foreignByInstance.TryGetValue(entry, out var ours))
                    {
                        ours.UniqueIDIndex = entry.UniqueIDIndex;
                        asInts[i] = ours;
                        swapped++;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[LoaderCoexistence] index-list swap failed: {Log.ExceptionText(ex)}");
        }

        // Remove the foreign duplicates from the game database so per-run discovery
        // loops (AllBlueprintModels/BlueprintModelStates seeding, STA/Objective/Quest
        // collection in InitializeStatsAndActions) see one instance per UID.
        int removed = 0;
        try
        {
            var allData = Data.GameRegistry.AllData;
            if (allData != null)
            {
                for (int i = allData.Count - 1; i >= 0; i--)
                {
                    if (allData[i] is UniqueIDScriptable u && foreignByInstance.ContainsKey(u))
                    {
                        allData.RemoveAt(i);
                        removed++;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[LoaderCoexistence] AllData dedup failed: {Log.ExceptionText(ex)}");
        }

        var loader = Discovery.ModDiscovery.PikachuLoaderName ?? "another mod loader";
        Log.Info($"[LoaderCoexistence] {loader} created {rebound} duplicate object(s) for framework-loaded UIDs — "
               + $"framework instances are now canonical (registry rebound: {rebound}, index list swapped: {swapped}, "
               + $"database entries removed: {removed})");
    }
}
