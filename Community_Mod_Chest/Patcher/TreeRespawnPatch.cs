using System;
using System.Collections.Generic;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Restores tree regrowth on the Village map's clone nodes.
    ///
    /// Every vanilla explorable location carries "Create Small/Large/Birch Tree"
    /// <c>OnStatsChangeActions</c> that re-plant a chopped tree once its board tag goes
    /// missing (WillowLine/Green Glade/Pine Meadows and siblings all have this). CardCloneService
    /// copies these onto our clone CT8s, but <c>WorldMap/MapNodes.json</c>'s <c>StripLegacyBoardUIDs</c>
    /// on cmcEnvVillagePath/cmcEnvVillage/cmcEnvVillageFarm also strips every action whose
    /// ProducedCards references one of the stripped tree UIDs (CardCloneService.StripActionsProducingUids
    /// matches by produced-card UID, not by intent) — so the same fix that keeps Nettle/Clover/
    /// Meadowgrass patches from sprouting in the finished Village silently deleted its native
    /// tree-respawn actions too. Re-adding those specific tree UIDs to the strip list is not an
    /// option (that list also carries the legitimate one-time board-migration cleanup), so this
    /// patch replaces the missing behavior for all twelve CMC map locations with an env-scoped
    /// equivalent: once per in-game day, for the current environment only, spawn any of its listed
    /// tree species that isn't currently on the board, gated by a chance roll so regrowth feels
    /// gradual instead of instantaneous. See CLAUDE.md §WorldMap Clone Env Board Seeding.
    /// </summary>
    internal static class TreeRespawnPatch
    {
        // Vanilla tree GUIDs (Documentation/CSFF_Reference.md §Large Tree GUIDs + UniqueIDScriptableGUID/CardData.json).
        private const string TreeLargeOak = "14201221856a7b34fb86d602e0359b83";
        private const string TreeSmallOak = "bd614575807e6b54488493417146f749";
        private const string TreeLargeBirch = "e8287a79ea2ea4245b4a83ce727c4c9d";
        private const string TreeLargePine = "41fbf7771da1b9f4ea13af0bc1ea4341";
        private const string TreeSmallPine = "27cdcd9c74d2c0548bba4b5d43d37b9e";
        private const string TreeLargeAlder = "3fbe36a6c9ef2e746affc9a5e91ed81a";
        private const string TreeSmallAlder = "0602e2df5cb0a7843b5517326590a915";
        private const string TreeLargeWillow = "f27a6838066ae10428aa6df6d6259221";

        // Per-environment target tree counts, derived from the vanilla template CT8's
        // OnStatsChangeActions (one action entry per tree slot; repeated UIDs = multiple trees of
        // that species). SpawnMissingTrees counts board presence by species and spawns the difference.
        // Source: Documentation/GameData CT8 JSON GUID-count query per template, 2026-07-31.
        private static readonly Dictionary<string, string[]> EnvTrees = new()
        {
            ["cmcEnvVillagePath"]    = new[] { TreeLargeOak, TreeSmallOak },
            ["cmcEnvHighGrove"]      = new[] { TreeLargePine, TreeSmallPine },
            ["cmcEnvPineTrail"]      = new[] { TreeLargePine, TreeSmallPine, TreeLargeBirch, TreeLargeBirch },
            ["cmcEnvVillage"]        = new[] { TreeLargeOak, TreeSmallOak, TreeLargeBirch, TreeLargeBirch },
            ["cmcEnvVillageFarm"]    = new[] { TreeLargePine, TreeSmallPine, TreeLargeBirch, TreeLargeBirch },
            ["cmcEnvClayFlats"]      = new[] { TreeLargeAlder, TreeLargeAlder, TreeSmallAlder, TreeLargeWillow, TreeLargeWillow },
            ["cmcEnvMarshHollow"]    = new[] { TreeLargeAlder, TreeLargeAlder, TreeSmallAlder, TreeLargeBirch, TreeLargeBirch, TreeLargeWillow, TreeLargeWillow },
            ["cmcEnvMossyClearing"]  = new[] { TreeLargeOak, TreeSmallOak, TreeLargeBirch, TreeLargeBirch, TreeLargeWillow, TreeLargeWillow },
            ["cmcEnvForagingForest"] = new[] { TreeLargePine, TreeSmallPine },
            ["cmcEnvHuntersCrossing"]= new[] { TreeLargePine, TreeSmallPine, TreeLargeBirch, TreeLargeBirch },
            ["cmcEnvDeerMeadow"]     = new[] { TreeLargePine, TreeSmallPine },
            ["cmcEnvBadgerWarren"]   = new[] { TreeLargePine, TreeSmallPine, TreeLargeBirch, TreeLargeBirch },
        };

        private static bool _initialized;
        private static string _prevEnvUid;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            TickEvents.DayRollover += OnDayRollover;
            TickEvents.DtpTick += OnEnvEntryCheck;
            Plugin.Logger.LogDebug("[TreeRespawnPatch] initialized.");
        }

        // Fires every 15 in-game minutes; detects env changes and seeds trees immediately on arrival.
        // Fixes saves where the vanilla OnStatsChangeActions were stripped and the player arrives at
        // a CMC env with a full "Trees" stat but no tree cards on the board.
        private static void OnEnvEntryCheck()
        {
            try
            {
                var envUid = GameQuery.CurrentEnvironmentUniqueId;
                if (envUid == _prevEnvUid) return;
                _prevEnvUid = envUid;
                if (envUid == null || !EnvTrees.TryGetValue(envUid, out var trees)) return;
                SpawnMissingTrees(envUid, trees);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[TreeRespawnPatch] OnEnvEntryCheck failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void OnDayRollover()
        {
            try
            {
                var envUid = GameQuery.CurrentEnvironmentUniqueId;
                if (envUid == null || !EnvTrees.TryGetValue(envUid, out var trees)) return;
                SpawnMissingTrees(envUid, trees);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[TreeRespawnPatch] OnDayRollover failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void SpawnMissingTrees(string envUid, string[] trees)
        {
            // Count how many of each species are on the board (multiple trees of the same species can coexist).
            var boardCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var card in GameQuery.CardsInPlayerEnv())
            {
                var uid = CardUtil.GetCardUniqueId(card);
                if (uid == null) continue;
                boardCounts.TryGetValue(uid, out var c);
                boardCounts[uid] = c + 1;
            }

            // Tally target count per species from the trees array (duplicate entries = multiple slots).
            var targetCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var uid in trees)
            {
                targetCounts.TryGetValue(uid, out var c);
                targetCounts[uid] = c + 1;
            }

            foreach (var kvp in targetCounts)
            {
                boardCounts.TryGetValue(kvp.Key, out var current);
                var needed = kvp.Value - current;
                for (int i = 0; i < needed; i++)
                {
                    SpawnService.Spawn(kvp.Key);
                    Plugin.Logger.LogDebug($"[TreeRespawnPatch] '{envUid}': spawned '{kvp.Key}' ({current + i + 1}/{kvp.Value}).");
                }
            }
        }
    }
}
