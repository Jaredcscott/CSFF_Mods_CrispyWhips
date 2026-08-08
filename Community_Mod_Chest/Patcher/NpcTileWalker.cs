using System;
using System.Collections.Generic;
using CSFFModFramework.Api;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Shared tile-by-tile outdoor movement for CMC's scheduled NPCs (Professor, Apothecary,
    /// Miller, Weaver). Every one of those schedulers used to call GameManager.MoveNPC straight
    /// to its final destination, which relocates the NPC across however many map tiles separate
    /// them from it in a single call — a "teleport" regardless of the distance. This walks the
    /// WorldMap graph (built from Api.WorldMap.InjectedEdges — the same edge set CMC's own
    /// WorldMap/MapNodes.json produced) one tile per call, paced by a caller-supplied tick
    /// budget, so a multi-tile trip is modeled as a sequence of hops through each intermediate
    /// tile instead of one long-distance jump.
    ///
    /// Building interiors (Academy/Inn/cabins/cottages) are NOT part of this graph — they sit on
    /// a single outdoor "front door" tile and are entered/exited in one instantaneous hop, same
    /// as the player's own Enter/Exit DAs. Callers resolve interior-to-front-door mapping
    /// themselves (they already track which interior EnvID they own) and only ask this class to
    /// walk the OUTDOOR leg.
    /// </summary>
    internal static class NpcTileWalker
    {
        /// <summary>Default pacing shared by every CMC commute — 45 in-game minutes/tile (1 DTP = 15 min).</summary>
        public const int DefaultTicksPerTile = 3;

        private sealed class WalkState
        {
            public string DestUid;
            public List<string> Path;
            public int NextIndex;
            public int NextStepTick = int.MinValue;
        }

        private static readonly Dictionary<object, WalkState> _states = new();
        private static Dictionary<string, List<string>> _adjacency;

        private static void EnsureGraph()
        {
            if (_adjacency != null) return;
            _adjacency = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var edge in WorldMap.InjectedEdges)
            {
                Link(edge.SourceEnvUid, edge.DestinationEnvUid);
                Link(edge.DestinationEnvUid, edge.SourceEnvUid);
            }
        }

        private static void Link(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return;
            if (!_adjacency.TryGetValue(a, out var list))
                _adjacency[a] = list = new List<string>();
            foreach (var existing in list)
                if (string.Equals(existing, b, StringComparison.OrdinalIgnoreCase)) return;
            list.Add(b);
        }

        // Plain BFS — every injected edge uses the vanilla-standard cost of 10, so shortest hop
        // count is always the shortest-cost path too; no need to reflect into the game's own
        // weighted A* (WorldMapData.Pathfinding) for this.
        private static List<string> FindPath(string fromUid, string toUid)
        {
            EnsureGraph();
            if (string.IsNullOrEmpty(fromUid) || string.IsNullOrEmpty(toUid)) return null;
            if (string.Equals(fromUid, toUid, StringComparison.OrdinalIgnoreCase)) return new List<string> { fromUid };
            if (!_adjacency.ContainsKey(fromUid) || !_adjacency.ContainsKey(toUid)) return null;

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { fromUid };
            var prev = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>();
            queue.Enqueue(fromUid);

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                if (string.Equals(cur, toUid, StringComparison.OrdinalIgnoreCase)) break;
                if (!_adjacency.TryGetValue(cur, out var neighbors)) continue;
                foreach (var next in neighbors)
                {
                    if (!visited.Add(next)) continue;
                    prev[next] = cur;
                    queue.Enqueue(next);
                }
            }

            if (!visited.Contains(toUid)) return null;

            var path = new List<string> { toUid };
            var walk = toUid;
            while (!string.Equals(walk, fromUid, StringComparison.OrdinalIgnoreCase))
            {
                walk = prev[walk];
                path.Add(walk);
            }
            path.Reverse();
            return path;
        }

        /// <summary>
        /// Advances (at most) one outdoor tile toward <paramref name="destUid"/>. Call this every
        /// scheduler tick with the NPC's CURRENT outdoor env uid; returns the uid to MoveNPC to
        /// this call, or null when no step is due yet, the NPC has already arrived, or no path
        /// exists (the last case means the caller should fall back to a direct hop rather than
        /// stall forever — e.g. the two tiles aren't connected in the injected-edge graph).
        /// <paramref name="npcKey"/> is any object stable for the NPC's lifetime this session
        /// (the InGameNPC instance itself works — state is session-scoped, cleared on reload
        /// same as every other cache these schedulers keep).
        /// </summary>
        public static string AdvanceStep(object npcKey, string currentUid, string destUid, int ticksPerTile, int currentTotalTick)
        {
            if (string.IsNullOrEmpty(currentUid) || string.IsNullOrEmpty(destUid)) return null;
            if (string.Equals(currentUid, destUid, StringComparison.OrdinalIgnoreCase))
            {
                _states.Remove(npcKey);
                return null;
            }

            int pace = Math.Max(1, ticksPerTile);

            if (!_states.TryGetValue(npcKey, out var state)
                || !string.Equals(state.DestUid, destUid, StringComparison.OrdinalIgnoreCase)
                || state.Path == null || state.NextIndex <= 0 || state.NextIndex > state.Path.Count
                || !string.Equals(state.Path[state.NextIndex - 1], currentUid, StringComparison.OrdinalIgnoreCase))
            {
                // New destination, or the NPC isn't where our in-flight plan expected (moved by
                // other means — e.g. a night override sent it straight home) — (re)plan from here.
                var path = FindPath(currentUid, destUid);
                if (path == null || path.Count < 2)
                {
                    _states.Remove(npcKey);
                    return null; // unreachable in the injected-edge graph — caller falls back to a direct hop
                }
                state = new WalkState { DestUid = destUid, Path = path, NextIndex = 1, NextStepTick = currentTotalTick + pace };
                _states[npcKey] = state;
            }

            if (currentTotalTick < state.NextStepTick) return null; // step not due yet

            string nextUid = state.Path[state.NextIndex];
            state.NextIndex++;
            state.NextStepTick = currentTotalTick + pace;
            if (state.NextIndex >= state.Path.Count) _states.Remove(npcKey);
            return nextUid;
        }

        /// <summary>Drops any in-flight walk for one NPC (e.g. it was moved by other means).</summary>
        public static void ResetState(object npcKey) => _states.Remove(npcKey);

        /// <summary>Session boundary (reload) — every InGameNPC instance is recreated, so any
        /// in-flight plan keyed on the old instances is stale. Callers wire this into the same
        /// InitializeStatsAndActions postfix they already use to drop their other session caches.</summary>
        public static void ResetAll() => _states.Clear();
    }
}
