using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using CSFFModFramework.Data;
using CSFFModFramework.Discovery;
using CSFFModFramework.Util;
using HarmonyLib;

namespace CSFFModFramework.Injection;

/// <summary>
/// Reads <c>InjectImprovementInto.json</c> from each mod and appends a CT10 improvement to a
/// target CT8 location card's <c>EnvironmentImprovements</c> array — a declarative version of
/// the append-to-vanilla-CT8 pattern every improvement mod previously hand-rolled in C#
/// (e.g. Community_Mod_Chest's <c>RiverBridgeImprovementPatch</c>).
///
/// <para>Target MUST be the CT8 location card, not the CT4 environment card — the exploration
/// UI builds its buildable-improvement slots from the CT8's <c>EnvironmentImprovements</c>
/// (<c>ExplorationPopup.SetupImprovements</c>); the CT4 has an empty list and
/// <c>HasImprovements</c> is false off the Explorable type. See CLAUDE.md §Environment
/// Improvements.</para>
///
/// JSON schema (<c>InjectImprovementInto.json</c> in mod root):
/// <code>
/// [
///   { "TargetEnvUID": "&lt;vanilla or mod CT8 UniqueID&gt;", "ImprovementUID": "&lt;mod CT10 UniqueID&gt;" }
/// ]
/// </code>
/// Idempotent — skips an entry if the improvement is already present on the target's list
/// (the SO mutation persists for the process and <c>LoadMainGameData</c> fires on every load).
/// </summary>
internal static class ImprovementInjector
{
    public static void InjectAll(IEnumerable allData, List<ModManifest> mods)
    {
        var entries = new List<(string modName, string targetUid, string improvementUid)>();
        foreach (var mod in mods)
        {
            var path = Path.Combine(mod.DirectoryPath, "InjectImprovementInto.json");
            if (!File.Exists(path)) continue;
            try
            {
                var json = File.ReadAllText(path);
                var parsed = MiniJson.Parse(json);
                if (parsed is not List<object> arr)
                {
                    Log.Warn($"ImprovementInjector: {mod.Name} InjectImprovementInto.json root must be a JSON array");
                    continue;
                }

                int modCount = 0;
                foreach (var item in arr)
                {
                    if (item is not Dictionary<string, object> dict) continue;
                    var targetUid = dict.TryGetValue("TargetEnvUID", out var t) ? t as string : null;
                    var impUid    = dict.TryGetValue("ImprovementUID", out var i) ? i as string : null;
                    if (string.IsNullOrEmpty(targetUid) || string.IsNullOrEmpty(impUid))
                    {
                        Log.Warn($"ImprovementInjector: {mod.Name} entry missing TargetEnvUID or ImprovementUID — skipped");
                        continue;
                    }
                    entries.Add((mod.Name, targetUid, impUid));
                    modCount++;
                }
                Log.Debug($"ImprovementInjector: loaded {modCount} entries from {mod.Name}");
            }
            catch (Exception ex)
            {
                Log.Warn($"ImprovementInjector: error reading {path}: {Log.ExceptionText(ex)}");
            }
        }

        if (entries.Count == 0) return;

        var cardLookup = BuildCardLookup(allData);
        int injected = 0;
        foreach (var (modName, targetUid, impUid) in entries)
        {
            if (!cardLookup.TryGetValue(targetUid, out var targetObj) || targetObj is not CardData target)
            {
                Log.Warn($"ImprovementInjector: {modName}: TargetEnvUID '{targetUid}' not found in registry — skipped");
                continue;
            }
            if (!cardLookup.TryGetValue(impUid, out var impObj) || impObj is not CardData improvement)
            {
                Log.Warn($"ImprovementInjector: {modName}: ImprovementUID '{impUid}' not found in registry — skipped");
                continue;
            }

            var arr = target.EnvironmentImprovements ?? Array.Empty<CardDataRef>();

            bool alreadyPresent = false;
            foreach (var r in arr)
                if (r?.Card != null && impUid.Equals(r.Card.UniqueID, StringComparison.Ordinal)) { alreadyPresent = true; break; }
            if (alreadyPresent) continue;

            var next = new CardDataRef[arr.Length + 1];
            Array.Copy(arr, next, arr.Length);
            next[arr.Length] = new CardDataRef { Card = improvement };
            target.EnvironmentImprovements = next;

            Loading.FrameworkDirtyTracker.MarkDirty(target);
            injected++;
            Log.Debug($"ImprovementInjector: {modName}: '{impUid}' added to '{targetUid}' ({arr.Length} -> {next.Length} improvements)");
        }

        if (injected > 0)
            Log.Info($"ImprovementInjector: {injected} improvement(s) injected across target location card(s)");
    }

    private static Dictionary<string, object> BuildCardLookup(IEnumerable allData)
    {
        var lookup = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
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
