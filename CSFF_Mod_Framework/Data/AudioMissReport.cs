namespace CSFFModFramework.Data;

/// <summary>
/// Decides which sound names each mod is told about at boot (2.29.0, fleet plan 4.1, credit Chiwei).
/// A name in a *SoundsWarpData field that matches no AudioClip is dropped, so the action plays nothing.
/// WarpResolver records the owning mod at each miss; ResolveAll passes those records and the clip names
/// it resolved against through <see cref="MissesByMod"/> and prints one <see cref="FormatLine"/> per mod.
/// Pure (BCL types only), so Development_Tools/Tests/Framework-AudioMissReport.Tests.ps1 runs it outside the game.
/// </summary>
internal static class AudioMissReport
{
    internal const string UnknownMod = "unknown mod";

    /// <summary>
    /// For each mod, the names it used that match no clip (case-insensitive, as Database.AudioClipDict
    /// matches), once each and sorted; a mod with no such name is left out. An empty owner is "unknown mod".
    /// </summary>
    internal static SortedDictionary<string, List<string>> MissesByMod(
        Dictionary<string, HashSet<string>> usedByMod, IEnumerable<string> clipNames)
    {
        var result = new SortedDictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (usedByMod == null || usedByMod.Count == 0) return result;

        var clips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (clipNames != null)
            foreach (var c in clipNames)
                if (!string.IsNullOrEmpty(c)) clips.Add(c);

        foreach (var kv in usedByMod)
        {
            if (kv.Value == null) continue;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> missed = null;
            foreach (var name in kv.Value)
            {
                if (string.IsNullOrEmpty(name) || clips.Contains(name) || !seen.Add(name)) continue;
                (missed ??= new List<string>()).Add(name);
            }
            if (missed == null) continue;

            var mod = string.IsNullOrEmpty(kv.Key) ? UnknownMod : kv.Key;
            if (result.TryGetValue(mod, out var existing))
            {
                foreach (var n in missed)
                    if (!existing.Contains(n, StringComparer.OrdinalIgnoreCase)) existing.Add(n);
                existing.Sort(StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                missed.Sort(StringComparer.OrdinalIgnoreCase);
                result[mod] = missed;
            }
        }
        return result;
    }

    /// <summary>The Info line for one mod's misses.</summary>
    internal static string FormatLine(string mod, List<string> names)
        => $"WarpResolver: [{mod}] {names.Count} sound name(s) match no audio clip, so they play nothing: {string.Join(", ", names)}";
}
