using System;
using System.Collections.Generic;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// E2, small gifts from a Content cat. While a tamed cat (Ash or Shadow) stands on the board the player is on
    /// with its Care (SpecialDurability3) at <see cref="ContentCareFloor"/> or more, the cat rolls once a day for a
    /// <see cref="GiftChance"/> chance to leave a small gift at camp: a Fish Scraps or a Feathers card, both vanilla,
    /// spawned onto the CURRENT board with their own vanilla starting stats (root CLAUDE.md "GiveCard / SpawnService.Spawn
    /// place the card on the CURRENT board only"). The roll is latched by the clamped 0..1 stat
    /// <see cref="GiftRolledStatUid"/>, which is set when a qualified cat is present and a roll is made (win or lose)
    /// and put back to 0 at every day rollover, so a day spent away from the cats does not use the day's roll.
    ///
    /// <para>The Content tier's description line and its Loneliness aura are JSON (the cats' AlternateNames and a
    /// PassiveEffects entry conditioned on the card's own Care); this class owns only the gift. Registered once per
    /// process (the early return on <c>_initialized</c> is load-bearing, root CLAUDE.md "Per-Run Event
    /// Registration"). Every miss leaves a warn-once line keyed on its cause. The numbers are tunable placeholders.</para>
    /// </summary>
    internal static class CatGiftPatch
    {
        internal const string InnCatUid = "cmcInnCat";
        internal const string ShadowCatUid = "cmcShadowCatTamed";

        /// <summary>0..1: today's gift roll has been made. Set back to 0 at every day rollover.</summary>
        internal const string GiftRolledStatUid = "cmcStatCatGiftRolled";

        /// <summary>The cat's own Care stat on both cats (SpecialDurability3, 0 to 100).</summary>
        internal const string CareStatName = "SpecialDurability3";

        /// <summary>TUNABLE PLACEHOLDER: the Care at which a cat is Content (the fourth description tier).</summary>
        internal const float ContentCareFloor = 90f;

        /// <summary>TUNABLE PLACEHOLDER: the chance of a gift on the one roll a day.</summary>
        internal const float GiftChance = 0.10f;

        /// <summary>Fish Scraps (vanilla FishScraps) and Feathers (vanilla Feathers).</summary>
        internal const string FishScrapsUid = "b25f1f9bfc2a0bc44a93079ed02cb43f";
        internal const string FeathersUid = "ac7fa8419b608d54fa2002aa589d3f94";

        /// <summary>RNG hook: -1 = unseeded, 0 or above = a replayable sequence. Written by harness scenarios.</summary>
        public static int RngSeed = -1;

        private static int _seededWith = int.MinValue;
        private static Random _rng;
        private static bool _initialized;
        private static readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);

        private static void WarnOnce(string cause, string message)
        {
            if (_warned.Add(cause)) Plugin.Logger.LogWarning("[CatGiftPatch] " + message);
        }

        /// <summary>True when a cat at this Care is Content. NaN (an unreadable stat) is never Content.</summary>
        internal static bool IsContent(float care) => care >= ContentCareFloor;

        /// <summary>True when a draw in [0, 1) lands inside <see cref="GiftChance"/>.</summary>
        internal static bool GiftWins(double roll) => roll < GiftChance;

        /// <summary>Which gift a second draw in [0, 1) picks: the first half Fish Scraps, the rest Feathers.</summary>
        internal static string PickGift(double roll) => roll < 0.5 ? FishScrapsUid : FeathersUid;

        /// <summary>The next draw in [0, 1) from the seedable generator (see <see cref="RngSeed"/>).</summary>
        internal static double NextRoll()
        {
            if (_rng == null || _seededWith != RngSeed)
            {
                _rng = RngSeed >= 0 ? new Random(RngSeed) : new Random();
                _seededWith = RngSeed;
            }
            return _rng.NextDouble();
        }

        /// <summary>The gift's own vanilla starting stats (read from the vanilla card JSON: Fish Scraps start fresh at
        /// Spoilage 0, Feathers at Mill 0), so a spawned gift matches what a drop of the same card carries.</summary>
        private static Dictionary<string, float> StatsFor(string uid)
        {
            return uid == FishScrapsUid
                ? new Dictionary<string, float> { { "SpoilageTime", 0f } }
                : new Dictionary<string, float> { { "SpecialDurability3", 0f } };
        }

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            TickEvents.Interval(5f, TryGift, "CatGiftRoll");
            TickEvents.DayRollover += OnDayRollover;
            Plugin.Logger.LogDebug("[CatGiftPatch] initialized.");
        }

        // CardFinder is scoped to the player's current environment (reference_allcards_env_scoped), so a cat found
        // here is on the board the gift will land on. A poll that finds no Content cat makes no roll and leaves the
        // latch alone, so the day's roll is not used up while the player is elsewhere.
        private static void TryGift()
        {
            // No run yet (main menu) or between runs: every stat read fails there, which is not broken.
            if (CardUtil.GetGameManagerInstance() == null) return;
            try
            {
                float rolled = HiddenStat.Get(GiftRolledStatUid);
                if (rolled < 0f)
                {
                    WarnOnce("latch-unreadable", "The gift latch stat is unreadable, so no gift was rolled.");
                    return;
                }
                if (rolled >= 0.5f) return; // today's roll is made

                if (!HasContentCatHere(InnCatUid) && !HasContentCatHere(ShadowCatUid)) return;

                // Latch first: a roll is never made twice, whatever happens to the spawn.
                if (!HiddenStat.Set(GiftRolledStatUid, 1f))
                {
                    WarnOnce("latch-write-failed", "The gift latch stat could not be written, so no gift was rolled.");
                    return;
                }

                if (!GiftWins(NextRoll())) return;

                string uid = PickGift(NextRoll());
                SpawnService.Spawn(uid, StatsFor(uid));
                Plugin.Logger.LogInfo($"[CatGiftPatch] A Content cat left a gift at camp: {(uid == FishScrapsUid ? "Fish Scraps" : "Feathers")}.");
            }
            catch (Exception ex)
            {
                WarnOnce("gift-exception", "TryGift failed: " + (ex.InnerException?.ToString() ?? ex.ToString()));
            }
        }

        private static bool HasContentCatHere(string catUid)
        {
            var cat = CardFinder.Find(catUid);
            if (cat == null) return false;
            float care = CardUtil.GetDurability(cat, CareStatName);
            if (float.IsNaN(care))
            {
                WarnOnce("care-unreadable-" + catUid, catUid + ": the Care stat is unreadable, so no gift was considered.");
                return false;
            }
            return IsContent(care);
        }

        private static void OnDayRollover()
        {
            try
            {
                float rolled = HiddenStat.Get(GiftRolledStatUid);
                if (rolled < 0f)
                {
                    WarnOnce("latch-unreadable-reset", "The gift latch stat is unreadable at the day rollover, so it was not reset.");
                    return;
                }
                if (rolled > 0f && !HiddenStat.Set(GiftRolledStatUid, 0f))
                    WarnOnce("latch-reset-failed", "The gift latch stat could not be reset at the day rollover.");
            }
            catch (Exception ex)
            {
                WarnOnce("reset-exception", "OnDayRollover failed: " + (ex.InnerException?.ToString() ?? ex.ToString()));
            }
        }
    }
}
