using System;
using System.Collections;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Old-save backfill for the Academy Bookshelf (CMC round 2, F1), the same shape as
    /// AchievementBoardSeedPatch. Fresh saves get <c>cmcAcademyBookshelf</c> from
    /// CMC_AcademyInterior.json's own DefaultEnvCardDrops (evaluated only the first time the
    /// environment's board is generated); a save that had already visited the Academy never
    /// re-evaluates that list, so this patch spawns the shelf the next time the player steps in.
    /// Never ExtraDropUIDs (CMC CLAUDE.md "Env watch must spawn CT8 ONLY").
    ///   - Gated on the player's CURRENT environment, because SpawnService.Spawn places on the
    ///     current board only and GameManager.AllCards (the duplicate guard) is current-env-scoped.
    ///   - Latched by cmcStatAcademyShelfPlaced, set only after a scan finds the shelf on the board.
    ///   - <see cref="CountShelves"/> scans every card rather than stopping at the first match, so a
    ///     genuine duplicate is logged rather than masked; the shelf is UniqueOnBoard and keeps no
    ///     per-instance state (its Study Alone latch is a global GameStat), so the only corrective
    ///     action is to stop spawning and say so.
    /// Registration runs once per process under an _initialized guard (pattern b).
    /// </summary>
    internal static class AcademyShelfSeedPatch
    {
        internal const string ShelfUid = "cmcAcademyBookshelf";
        internal const string PlacedStatUid = "cmcStatAcademyShelfPlaced";

        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            TickEvents.Interval(5f, CheckAndSpawn, "AcademyShelfSeedCheck");
            Plugin.Logger.LogDebug("[AcademyShelfSeedPatch] initialized.");
        }

        private static void CheckAndSpawn()
        {
            try
            {
                if (HiddenStat.Get(PlacedStatUid) >= 0.5f) return; // already placed

                if (!string.Equals(GameQuery.CurrentEnvironmentUniqueId, AcademyGuestPatch.AcademyInteriorUid, StringComparison.OrdinalIgnoreCase))
                    return;

                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;

                int existing = CountShelves(gm);
                if (existing > 0)
                {
                    if (existing > 1)
                        Plugin.Logger.LogWarning($"[AcademyShelfSeedPatch] Found {existing} copies of '{ShelfUid}' on the board - latching {PlacedStatUid} without spawning another. No per-instance state to reconcile.");
                    HiddenStat.Set(PlacedStatUid, 1f);
                    return;
                }

                // SpawnService.Spawn returns null on success (GiveCard is void this game
                // version): verify by re-querying the board, not the return value.
                SpawnService.Spawn(ShelfUid);
                if (CountShelves(gm) > 0)
                {
                    HiddenStat.Set(PlacedStatUid, 1f);
                    Plugin.Logger.LogInfo("[AcademyShelfSeedPatch] Academy Bookshelf backfilled into the Village Academy.");
                }
                else
                {
                    Plugin.Logger.LogWarning($"[AcademyShelfSeedPatch] Academy Bookshelf did not appear after spawn - is {ShelfUid} loaded?");
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[AcademyShelfSeedPatch] CheckAndSpawn failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        /// <summary>Counts every current-board card matching <see cref="ShelfUid"/>, never stopping at the first.</summary>
        private static int CountShelves(object gm)
        {
            if (Reflect.GetMember(gm, "AllCards") is not IEnumerable allCards) return 0;
            int count = 0;
            foreach (var card in allCards)
            {
                if (card == null) continue;
                if (CardUtil.GetCardUniqueId(card) == ShelfUid) count++;
            }
            return count;
        }
    }
}
