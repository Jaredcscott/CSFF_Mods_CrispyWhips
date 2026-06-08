using CSFFModFramework.Discovery;
using CSFFModFramework.Util;

namespace CSFFModFramework.Triggers;

/// <summary>
/// Loads <c>CardData/Trigger/*.json</c> files from all mods and builds
/// <see cref="TriggerDefinition"/> objects for <see cref="TriggerService"/> to evaluate at runtime.
///
/// <para>Uses MiniJson for parsing — the trigger schema contains array fields that
/// <c>JsonUtility.FromJson</c> silently nulls even on public types (see CLAUDE.md).</para>
/// </summary>
internal static class TriggerLoader
{
    /// <summary>All parsed trigger definitions from this load session.</summary>
    internal static List<TriggerDefinition> LoadedTriggers { get; } = new();

    public static void LoadAll(List<ModManifest> mods)
    {
        LoadedTriggers.Clear();
        int total = 0;

        foreach (var mod in mods)
        {
            if (!mod.HasTriggers) continue;

            var dir = Path.Combine(mod.DirectoryPath, "CardData", "Trigger");
            if (!Directory.Exists(dir)) continue;

            int count = 0;
            foreach (var file in Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    if (MiniJson.Parse(json) is not Dictionary<string, object> parsed) continue;

                    var def = ParseTrigger(parsed, mod.Name);
                    if (def == null) continue;

                    LoadedTriggers.Add(def);
                    count++;
                }
                catch (Exception ex)
                {
                    Log.Warn($"TriggerLoader: failed to parse {file}: {Log.ExceptionText(ex)}");
                }
            }

            if (count > 0)
                Log.Info($"[TriggerLoader] loaded {count} trigger(s) from {mod.Name}");
            total += count;
        }

        Log.Info($"[TriggerLoader] {total} trigger(s) loaded across all mods");
    }

    private static TriggerDefinition ParseTrigger(Dictionary<string, object> d, string modName)
    {
        var uid = GetString(d, "UniqueID");
        if (string.IsNullOrEmpty(uid))
        {
            Log.Warn($"TriggerLoader: [{modName}] trigger missing UniqueID — skipped");
            return null;
        }

        var triggerCardUID = GetString(d, "TriggerCardWarpData");
        if (string.IsNullOrEmpty(triggerCardUID))
        {
            Log.Warn($"TriggerLoader: [{modName}] trigger '{uid}' missing TriggerCardWarpData — skipped");
            return null;
        }

        var displayName = "";
        if (d.TryGetValue("CardName", out var cn) && cn is Dictionary<string, object> cnDict)
            displayName = GetString(cnDict, "DefaultText") ?? "";

        var maxUIDs = new List<string>();
        if (d.TryGetValue("MaxOnBoardWarpData", out var mw) && mw is List<object> mwList)
        {
            foreach (var item in mwList)
            {
                var s = item as string;
                if (!string.IsNullOrEmpty(s)) maxUIDs.Add(s);
            }
        }

        return new TriggerDefinition
        {
            UniqueID           = uid,
            DisplayName        = displayName,
            TriggerCardUID     = triggerCardUID,
            SpawnChancePercent = GetInt(d, "SpawnChance", 0),
            SpawnLocation      = GetInt(d, "SpawnLocation", 1),
            TriggerFrequency   = GetInt(d, "TriggerFrequency", 96),
            MaxOnBoard         = GetInt(d, "MaxOnBoard", 0),
            MaxOnBoardUIDs     = maxUIDs,
            ModName            = modName,
        };
    }

    private static string GetString(Dictionary<string, object> d, string key) =>
        d.TryGetValue(key, out var v) ? v as string : null;

    private static int GetInt(Dictionary<string, object> d, string key, int fallback)
    {
        if (!d.TryGetValue(key, out var v)) return fallback;
        try { return Convert.ToInt32(v); }
        catch { return fallback; }
    }
}
