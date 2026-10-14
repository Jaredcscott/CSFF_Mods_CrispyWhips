using System;
using System.Collections.Generic;
using System.Linq;

namespace CSFFModFramework.Loading;

/// <summary>
/// The load phases <see cref="LoadOrchestrator"/> has timed in the current boot, so its closing
/// "Loading Complete" line (and the slow-boot Warn) can name the slowest few instead of leaving a
/// reader to add up the Debug-only per-phase lines. Pure bookkeeping: no Unity or game types.
/// </summary>
internal static class PhaseTimings
{
    private static readonly List<KeyValuePair<string, long>> _phases = new();

    internal static void Clear() => _phases.Clear();

    internal static void Record(string phase, long ms) => _phases.Add(new KeyValuePair<string, long>(phase, ms));

    /// <summary>
    /// The <paramref name="count"/> slowest phases as "Name 1234ms, Name 987ms", slowest first. A phase
    /// recorded twice (a phase name reused in one boot) is summed. Ties keep first-recorded order. Empty
    /// string when nothing was recorded.
    /// </summary>
    internal static string Slowest(int count)
    {
        if (count <= 0 || _phases.Count == 0) return "";
        var order = new List<string>();
        var totals = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var kv in _phases)
        {
            if (!totals.ContainsKey(kv.Key)) { totals[kv.Key] = 0; order.Add(kv.Key); }
            totals[kv.Key] += kv.Value;
        }
        return string.Join(", ", order
            .Select((name, i) => new { name, i, ms = totals[name] })
            .OrderByDescending(p => p.ms).ThenBy(p => p.i)
            .Take(count)
            .Select(p => p.name + " " + p.ms + "ms"));
    }
}
