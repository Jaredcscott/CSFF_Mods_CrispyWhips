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
    /// option (that list also carries the legitimate one-time board-migration cleanup — and,
    /// separately, WorldMapInjector.PreCreateCloneEnvSaveData treats any StripLegacyBoardUIDs card
    /// still present on a saved board as migration contamination and wipes the whole env entry to
    /// force a reseed, which would fire on EVERY load for a species this patch deliberately keeps
    /// on the board forever — so tree UIDs must never be added to that list), so this patch
    /// replaces the missing behavior for all twelve CMC map locations with an env-scoped
    /// equivalent: once per in-game day, for the current environment only, spawn any of its listed
    /// tree species that isn't currently on the board, gated by a chance roll so regrowth feels
    /// gradual instead of instantaneous. See CLAUDE.md §WorldMap Clone Env Board Seeding.
    ///
    /// The nine CMC nodes with NO StripLegacyBoardUIDs coverage at all (cmcEnvPineTrail,
    /// cmcEnvHighGrove, cmcEnvClayFlats, cmcEnvMarshHollow, cmcEnvMossyClearing,
    /// cmcEnvForagingForest, cmcEnvHuntersCrossing, cmcEnvDeerMeadow, cmcEnvBadgerWarren) still
    /// carry that native regrowth action untouched — it can race this patch's own spawn-if-missing
    /// check on the same env-entry/day-rollover tick and place a second, duplicate instance of an
    /// otherwise UniqueOnBoard tree species (confirmed via player report: "Pine Trail" showing two
    /// Small Pine Tree, two Pine Tree, and a Birch Tree x2 stack). Since tree UIDs can't safely go
    /// into StripLegacyBoardUIDs (see above), <see cref="SpawnMissingTrees"/> also trims any excess
    /// down to the declared per-species target — self-correcting both future races and any
    /// already-duplicated save.
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
            // Group live board cards by species so both shortfalls (spawn) and excess (trim) can
            // be corrected. Excess arises on nodes whose clone CT8 still carries the vanilla
            // native "Create X Tree" regrowth action (no StripLegacyBoardUIDs coverage — see the
            // class summary for why tree UIDs can't safely go in that list) racing this same
            // spawn-if-missing check.
            var boardCards = new Dictionary<string, List<object>>(StringComparer.OrdinalIgnoreCase);
            foreach (var card in GameQuery.CardsInPlayerEnv())
            {
                var uid = CardUtil.GetCardUniqueId(card);
                if (uid == null) continue;
                if (!boardCards.TryGetValue(uid, out var list))
                    boardCards[uid] = list = new List<object>();
                list.Add(card);
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
                boardCards.TryGetValue(kvp.Key, out var present);
                var current = present?.Count ?? 0;
                var needed = kvp.Value - current;
                if (needed > 0)
                {
                    for (int i = 0; i < needed; i++)
                    {
                        SpawnService.Spawn(kvp.Key);
                        Plugin.Logger.LogDebug($"[TreeRespawnPatch] '{envUid}': spawned '{kvp.Key}' ({current + i + 1}/{kvp.Value}).");
                    }
                }
                else if (needed < 0 && present != null)
                {
                    var excess = -needed;
                    for (int i = 0; i < excess && i < present.Count; i++)
                    {
                        if (CardUtil.TryRemoveCard(present[i]))
                            Plugin.Logger.LogDebug($"[TreeRespawnPatch] '{envUid}': removed excess duplicate '{kvp.Key}' ({current - i - 1}/{kvp.Value}).");
                    }
                }
            }
        }
    }
}
