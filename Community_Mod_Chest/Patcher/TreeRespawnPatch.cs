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
    ///
    /// CORRECTION 2026-09-26 (retro worldmap-clone-duplicate-terrain): the Pine Trail report above
    /// was NOT a race with the native action. Pine Trail's native tree actions are tag-gated and
    /// see the tile's env-local trees; its doubles came from this patch counting only the vanilla
    /// UniqueID (fixed in 1.68.43) and from EnvTrees targeting two Birch. The native actions that
    /// DID double are the card-gated ones (Birch, Large Alder, Willow, Pond, River), which tested the
    /// vanilla card by reference; the framework now guards those on every clone location card
    /// (CardCloneService.GuardCreatorsAgainstEnvLocalDrops).
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

        // Per-environment target tree counts. ONE of each species: all eight vanilla tree cards are
        // UniqueOnBoard, and every template below seeds exactly one of each species it has
        // (DefaultEnvCardDrops Quantity 1..1). SpawnMissingTrees counts board presence by species
        // and spawns the difference, so a repeated UID here means this patch plants and keeps two
        // identical trees. Development_Tools/Tests/WorldMap-CloneCreatorEnvLocalGuard.Tests.ps1
        // holds every row to that.
        //
        // History, so the doubles are not re-derived: the 2026-07-31 table counted each tree GUID
        // in the template's location-card JSON, and a "Create Birch/Alder/Willow Tree" action names
        // its card twice (inverted RequiredCardsOnBoard + ProducedCards), so every card-gated
        // species came out as two. Pine Trail's Birch and Clay Shoal's Willow are not in their
        // templates (GrovePine_PineGrove, River_ClearingAlder_BoggyMeadows) at all: they are CMC
        // additions and stay, at one tree each.
        private static readonly Dictionary<string, string[]> EnvTrees = new()
        {
            ["cmcEnvVillagePath"]    = new[] { TreeLargeOak, TreeSmallOak },
            ["cmcEnvHighGrove"]      = new[] { TreeLargePine, TreeSmallPine },
            ["cmcEnvPineTrail"]      = new[] { TreeLargePine, TreeSmallPine, TreeLargeBirch },
            ["cmcEnvVillage"]        = new[] { TreeLargeOak, TreeSmallOak, TreeLargeBirch },
            ["cmcEnvVillageFarm"]    = new[] { TreeLargePine, TreeSmallPine, TreeLargeBirch },
            ["cmcEnvClayFlats"]      = new[] { TreeLargeAlder, TreeSmallAlder, TreeLargeWillow },
            ["cmcEnvMarshHollow"]    = new[] { TreeLargeAlder, TreeSmallAlder, TreeLargeBirch, TreeLargeWillow },
            ["cmcEnvMossyClearing"]  = new[] { TreeLargeOak, TreeSmallOak, TreeLargeBirch, TreeLargeWillow },
            ["cmcEnvForagingForest"] = new[] { TreeLargePine, TreeSmallPine },
            ["cmcEnvHuntersCrossing"]= new[] { TreeLargePine, TreeSmallPine, TreeLargeBirch },
            ["cmcEnvDeerMeadow"]     = new[] { TreeLargePine, TreeSmallPine },
            ["cmcEnvBadgerWarren"]   = new[] { TreeLargePine, TreeSmallPine, TreeLargeBirch },
        };

        private const string EnvLocalSuffix = "__envlocal";

        private static bool _initialized;
        private static string _prevEnvUid;

        // The UniqueID this env's own DefaultEnvCardDrops seeds for a tree species: the env-local
        // variant when the clone substituted one, else the vanilla UniqueID (also the fallback when
        // the env card cannot be read, which is what this patch always spawned before).
        private static string SeededFormOf(string envUid, string treeUid)
        {
            if (CardUtil.GetCardDataById(envUid) is CardData env && env.DefaultEnvCardDrops != null)
            {
                var envLocalUid = treeUid + EnvLocalSuffix;
                foreach (var drop in env.DefaultEnvCardDrops)
                    if (drop.DroppedCard != null &&
                        string.Equals(drop.DroppedCard.UniqueID, envLocalUid, StringComparison.OrdinalIgnoreCase))
                        return drop.DroppedCard.UniqueID;
            }
            return treeUid;
        }

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
                // A clone env seeds the framework's env-local variant of an AlwaysUpdate tree
                // ("<uid>__envlocal", CardCloneService.GetEnvLocalVariant), not the vanilla card, so a
                // species is on the board under either UniqueID. Counting only the vanilla one read a
                // seeded Pine Tree as missing and planted a vanilla copy beside it on arrival: two Pine
                // Trees and two Small Pine Trees at Highland Pines, surviving save and reload (walkthrough
                // T2.128/T2.147, r39). Vanilla copies go first, so an excess trims the stray and keeps the
                // env's own.
                var present = new List<object>();
                if (boardCards.TryGetValue(kvp.Key, out var vanillaCopies)) present.AddRange(vanillaCopies);
                if (boardCards.TryGetValue(kvp.Key + EnvLocalSuffix, out var envLocalCopies)) present.AddRange(envLocalCopies);
                var current = present.Count;
                var needed = kvp.Value - current;
                if (needed > 0)
                {
                    var spawnUid = SeededFormOf(envUid, kvp.Key);
                    for (int i = 0; i < needed; i++)
                    {
                        SpawnService.Spawn(spawnUid);
                        Plugin.Logger.LogDebug($"[TreeRespawnPatch] '{envUid}': spawned '{spawnUid}' ({current + i + 1}/{kvp.Value}).");
                    }
                }
                else if (needed < 0)
                {
                    var excess = -needed;
                    for (int i = 0; i < excess && i < present.Count; i++)
                    {
                        // Info: this only runs when a tile holds more of a species than its target,
                        // i.e. once per affected tile while an old save heals, so it is the line an
                        // in-game check reads to confirm a doubled tree was removed.
                        var removedUid = CardUtil.GetCardUniqueId(present[i]);
                        if (CardUtil.TryRemoveCard(present[i]))
                            Plugin.Logger.LogInfo($"[TreeRespawnPatch] '{envUid}': removed excess duplicate '{removedUid}' ({current - i - 1}/{kvp.Value}).");
                    }
                }
            }
        }
    }
}
