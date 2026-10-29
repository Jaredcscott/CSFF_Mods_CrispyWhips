using System;
using System.Collections;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// The Founder's Monument (CMC round 2, G1). VillageReputationPatch latches
    /// <see cref="EarnedStatUid"/> the first time Village Reputation reads its full mark; this
    /// patch places one <see cref="MonumentUid"/> on the Village board the next time the player
    /// stands in the Village, the same deferred-spawn shape as AcademyShelfSeedPatch.
    ///   - Gated on the player's CURRENT environment, because SpawnService.Spawn places on the
    ///     current board only and GameManager.AllCards (the duplicate guard) is current-env-scoped.
    ///     Never ExtraDropUIDs (CMC CLAUDE.md "Env watch must spawn CT8 ONLY").
    ///   - Latched by <see cref="PlacedStatUid"/>, set only AFTER a re-scan finds the monument on the
    ///     board, so a spawn that did not land is retried on the next tick.
    ///   - <see cref="CountMonuments"/> scans every card rather than stopping at the first match, so a
    ///     genuine duplicate is logged rather than masked; the monument is UniqueOnBoard and keeps no
    ///     per-instance state, so the only corrective action is to stop spawning and say so.
    /// Registration runs once per process under an _initialized guard (root CLAUDE.md "Per-Run
    /// Event Registration", pattern b).
    /// </summary>
    internal static class MonumentSeedPatch
    {
        internal const string MonumentUid = "cmcfoundersmonument";
        internal const string EarnedStatUid = "cmcStatMonumentEarned";
        internal const string PlacedStatUid = "cmcStatMonumentPlaced";
        private const string VillageEnvUid = "cmcEnvVillage";

        private static bool _initialized;

        // One latch per cause (CLAUDE.md Silent Catch Blocks, G5).
        private static readonly System.Collections.Generic.HashSet<string> _warned =
            new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

        private static void WarnOnce(string cause, string message)
        {
            if (_warned.Add(cause)) Plugin.Logger.LogWarning(message);
        }

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            TickEvents.Interval(5f, CheckAndSpawn, "MonumentSeedCheck");
            Plugin.Logger.LogDebug("[MonumentSeedPatch] initialized.");
        }

        private static void CheckAndSpawn()
        {
            try
            {
                if (HiddenStat.Get(PlacedStatUid) >= 0.5f) return; // already placed
                if (HiddenStat.Get(EarnedStatUid) < 0.5f) return;  // Reputation has not yet reached its full mark

                if (!string.Equals(GameQuery.CurrentEnvironmentUniqueId, VillageEnvUid, StringComparison.OrdinalIgnoreCase))
                    return;

                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;

                int existing = CountMonuments(gm);
                if (existing > 0)
                {
                    if (existing > 1)
                        Plugin.Logger.LogWarning($"[MonumentSeedPatch] Found {existing} copies of '{MonumentUid}' on the board - latching {PlacedStatUid} without spawning another. No per-instance state to reconcile.");
                    if (!HiddenStat.Set(PlacedStatUid, 1f))
                        WarnOnce("placed-latch", $"[MonumentSeedPatch] {PlacedStatUid} did not take the write - retried while the monument stands; one the player takes down is placed again.");
                    return;
                }

                // SpawnService.Spawn returns null on success (GiveCard is void this game
                // version): verify by re-querying the board, not the return value.
                SpawnService.Spawn(MonumentUid);
                if (CountMonuments(gm) > 0)
                {
                    if (!HiddenStat.Set(PlacedStatUid, 1f))
                        WarnOnce("placed-latch", $"[MonumentSeedPatch] {PlacedStatUid} did not take the write - retried while the monument stands; one the player takes down is placed again.");
                    Plugin.Logger.LogInfo("[MonumentSeedPatch] Founder's Monument placed in the Village.");
                }
                else
                {
                    Plugin.Logger.LogWarning($"[MonumentSeedPatch] Founder's Monument did not appear after spawn - is {MonumentUid} loaded?");
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[MonumentSeedPatch] CheckAndSpawn failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        /// <summary>Counts every current-board card matching <see cref="MonumentUid"/>, never stopping at the first.</summary>
        private static int CountMonuments(object gm)
        {
            if (Reflect.GetMember(gm, "AllCards") is not IEnumerable allCards) return 0;
            int count = 0;
            foreach (var card in allCards)
            {
                if (card == null) continue;
                if (CardUtil.GetCardUniqueId(card) == MonumentUid) count++;
            }
            return count;
        }
    }
}
