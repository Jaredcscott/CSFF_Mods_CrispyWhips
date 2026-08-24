using System;
using System.Collections;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Old-save backfill for the Town Achievement Board (Village_Master_Plan.md §10.9.2.5).
    ///
    /// Fresh saves get <c>cmcBoardAchievements</c> for free from CMC_InnInterior.json's own
    /// <c>DefaultEnvCardDrops</c> (evaluated only the first time an environment's board is ever
    /// generated). A save that had already visited the Inn before this feature shipped never
    /// re-evaluates that list, so this patch spawns the board the next time the player steps
    /// back into the Inn — the same deferred-spawn shape CopperChestPatch/LostCatPatch/
    /// VillageFounderPerkPatch all use: gate on the player's CURRENT environment, because
    /// <c>SpawnService.Spawn</c> places on the current board only and <c>GameManager.AllCards</c>
    /// (the duplicate guard) is likewise current-env-scoped (root CLAUDE.md § Runtime Card
    /// Spawning, "spawn-targets-current-board").
    ///
    /// Latched by <c>cmcStatAchBoardPlaced</c> so a save that already has the board (either from
    /// the fresh-save drop or a prior run of this same check) never gets a second one. The board
    /// is <c>UniqueOnBoard</c>; <see cref="CountBoards"/> deliberately scans every card on the
    /// board rather than returning at the first match (root CLAUDE.md's reconcile-ALL-instances
    /// rule) so a genuine duplicate is logged rather than silently ignored — there is nothing
    /// instance-local to "fix" on a second copy (its DA visibility reads global GameStats, not
    /// anything stored on the card itself), so the only corrective action needed is "stop
    /// spawning and say so".
    /// </summary>
    internal static class AchievementBoardSeedPatch
    {
        private const string BoardUid = "cmcBoardAchievements";
        private const string InnInteriorEnvUid = "cmcInnInterior";
        private const string PlacedStatUid = "cmcStatAchBoardPlaced";

        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            TickEvents.Interval(5f, CheckAndSpawn, "AchievementBoardSeedCheck");
            Plugin.Logger.LogDebug("[AchievementBoardSeedPatch] initialized.");
        }

        private static void CheckAndSpawn()
        {
            try
            {
                if (HiddenStat.Get(PlacedStatUid) >= 0.5f) return; // already placed

                if (!string.Equals(GameQuery.CurrentEnvironmentUniqueId, InnInteriorEnvUid, StringComparison.OrdinalIgnoreCase))
                    return;

                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;

                int existing = CountBoards(gm);
                if (existing > 0)
                {
                    if (existing > 1)
                        Plugin.Logger.LogWarning($"[AchievementBoardSeedPatch] Found {existing} copies of '{BoardUid}' on the board — latching cmcStatAchBoardPlaced without spawning another. No per-instance state to reconcile (DA visibility reads global GameStats only).");
                    HiddenStat.Set(PlacedStatUid, 1f);
                    return;
                }

                // SpawnService.Spawn returns null on success (GiveCard is void this game
                // version) — verify by re-querying the board, not the return value.
                SpawnService.Spawn(BoardUid);
                if (CountBoards(gm) > 0)
                {
                    HiddenStat.Set(PlacedStatUid, 1f);
                    Plugin.Logger.LogInfo("[AchievementBoardSeedPatch] Town Achievement Board backfilled into the Village Inn.");
                }
                else
                {
                    Plugin.Logger.LogWarning("[AchievementBoardSeedPatch] Town Achievement Board did not appear after spawn — is cmcBoardAchievements loaded?");
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[AchievementBoardSeedPatch] CheckAndSpawn failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        /// <summary>Counts every current-board card matching <see cref="BoardUid"/> — never stops
        /// at the first match, so a genuine duplicate is detected and logged rather than masked.</summary>
        private static int CountBoards(object gm)
        {
            if (Reflect.GetMember(gm, "AllCards") is not IEnumerable allCards) return 0;
            int count = 0;
            foreach (var card in allCards)
            {
                if (card == null) continue;
                if (CardUtil.GetCardUniqueId(card) == BoardUid) count++;
            }
            return count;
        }
    }
}
