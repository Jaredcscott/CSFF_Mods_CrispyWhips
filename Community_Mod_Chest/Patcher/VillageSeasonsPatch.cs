using System;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Village seasons (CMC round 2, G2): one beat for each of three seasons, each fired once when the
    /// season CHANGES and never again on a reload.
    ///   - Summer: the market day. Writes <see cref="MarketDayStatUid"/> to its full 96, a GameStat
    ///     that drains by 1 a tick (about a day) and that the five residents' TradingModifiers read as
    ///     a small discount (JSON, Agent_*.json; capped by CMC-ResidentFriendship's Wanted invariant).
    ///   - Autumn: provisioning. Offers the Inn Keeper's errand by raising the clamped marker
    ///     <see cref="ProvisionStateUid"/> to 1 (offered) when it rests at 0, and clears the completion
    ///     flag. The Inn Keeper's dialog accepts it (marker 0), the Inn Counter takes the dried food
    ///     (marker 0.5) and the thanks line resets the marker and sets the completion flag.
    ///   - Winter: the snowed-in supper. Sets <see cref="SupperArmedStatUid"/>; the Inn Counter's
    ///     "Join the Snowed-In Supper" is gated on <see cref="SupperOpenStatUid"/>, which this poll
    ///     republishes as armed AND the clock reads evening.
    /// The last season handled lives in <see cref="SeenStatUid"/> and is written only AFTER the beat's
    /// work succeeded, so a write that failed is retried on the next poll instead of being latched as
    /// done (root CLAUDE.md: never latch handled before doing the work). Each beat also has its own
    /// clamped 0..1 latch GameStat, set after its work and cleared when the season changes.
    /// Beats wait for the village intro (VillageClock.PhaseAtLeast), the same gate every other
    /// village beat uses, and the season comes from GameQuery.CurrentSeason, a NAME that is null
    /// until the season resolves (memory: reference_gamequery_currentseason_null), read here as
    /// "unknown this frame": nothing fires and nothing is latched.
    /// Registration runs once per process under an _initialized guard (root CLAUDE.md "Per-Run Event
    /// Registration", pattern b).
    /// </summary>
    internal static class VillageSeasonsPatch
    {
        internal const string SeenStatUid = "cmcStatSeasonSeen";
        internal const string SummerLatchUid = "cmcStatSeasonSummer";
        internal const string AutumnLatchUid = "cmcStatSeasonAutumn";
        internal const string WinterLatchUid = "cmcStatSeasonWinter";
        internal const string MarketDayStatUid = "cmcStatMarketDay";
        internal const string ProvisionStateUid = "cmcStatProvisionState";
        internal const string ProvisionDoneUid = "cmcStatProvisionDone";
        internal const string SupperArmedStatUid = "cmcStatSupperArmed";
        internal const string SupperOpenStatUid = "cmcStatSupperOpen";

        // Tunable placeholders.
        private const float MarketDayFull = 96f;   // one in-game day of 15-minute ticks
        private const float SupperEveningFrom = 17f; // clock hour the table opens; closes at midnight

        private static bool _initialized;

        // One latch per cause (CLAUDE.md Silent Catch Blocks, G5).
        private static readonly System.Collections.Generic.HashSet<string> _warned = new(StringComparer.Ordinal);

        private static void WarnOnce(string cause, string message)
        {
            if (_warned.Add(cause)) Plugin.Logger.LogWarning(message);
        }

        /// <summary>Consecutive 5 s polls the season-seen stat may read unreadable before its one warning
        /// (a run's first polls can miss while it boots).</summary>
        private const int SeenUnreadablePollsBeforeWarning = 6;
        private static int _seenUnreadablePolls;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            TickEvents.Interval(5f, Tick, "VillageSeasons");
            Plugin.Logger.LogDebug("[VillageSeasonsPatch] initialized.");
        }

        private static void Tick()
        {
            try
            {
                if (CardUtil.GetGameManagerInstance() == null) return; // main menu
                if (!VillageClock.PhaseAtLeast(1f)) return;            // village not introduced yet

                int season = SeasonIndex();
                if (season == 0) return; // unknown this frame: never fire or latch on a null

                float seen = HiddenStat.Get(SeenStatUid);
                if (seen < 0f)           // stat not readable yet; warned if it stays that way
                {
                    if (++_seenUnreadablePolls == SeenUnreadablePollsBeforeWarning)
                        WarnOnce("seen-unreadable", $"[VillageSeasonsPatch] {SeenStatUid} is not readable - no season beat fires until it is.");
                    return;
                }
                _seenUnreadablePolls = 0;

                // HiddenStat.Set reports a refused write at Debug only, so each failure here warns once; both are
                // retried on the next poll (an unlatched season re-runs its beat).
                if ((int)Math.Round(seen) != season && EnterSeason(season))
                {
                    if (!HiddenStat.Set(SeenStatUid, season))
                        WarnOnce("seen-write", $"[VillageSeasonsPatch] {SeenStatUid} did not take {season} - the season's beat runs again every poll until it does.");
                }
                else if ((int)Math.Round(seen) != season)
                {
                    WarnOnce("beat-" + season, $"[VillageSeasonsPatch] Season {season}'s beat did not take one of its writes - retried every poll until it does.");
                }

                PublishSupperOpen();
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[VillageSeasonsPatch] Tick failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        /// <summary>Does the season's work, then latches it. False means a write failed and the season
        /// is not marked handled, so the next poll retries.</summary>
        private static bool EnterSeason(int season)
        {
            // Clear every latch first; the entering season's latch is set again only after its work.
            bool ok = HiddenStat.Set(SummerLatchUid, 0f)
                    & HiddenStat.Set(AutumnLatchUid, 0f)
                    & HiddenStat.Set(WinterLatchUid, 0f);
            if (!ok) return false;

            // Winter's supper ends with winter, whatever the new season is.
            if (season != 4 && !HiddenStat.Set(SupperArmedStatUid, 0f)) return false;

            switch (season)
            {
                case 2: // Summer: market day
                    if (!HiddenStat.Set(MarketDayStatUid, MarketDayFull)) return false;
                    if (!HiddenStat.Set(SummerLatchUid, 1f)) return false;
                    Plugin.Logger.LogInfo("[VillageSeasonsPatch] Summer: the village holds its market day.");
                    return true;

                case 3: // Autumn: provisioning
                {
                    float marker = HiddenStat.Get(ProvisionStateUid);
                    if (marker < 0f)
                    {
                        WarnOnce("provision-unreadable", $"[VillageSeasonsPatch] {ProvisionStateUid} is not readable - the autumn provisioning errand is retried every poll until it is.");
                        return false;
                    }
                    // Offer only from rest: a delivery still awaiting thanks (0.5) is not clobbered.
                    if (marker < 0.25f && !HiddenStat.Set(ProvisionStateUid, 1f)) return false;
                    if (!HiddenStat.Set(ProvisionDoneUid, 0f)) return false;
                    if (!HiddenStat.Set(AutumnLatchUid, 1f)) return false;
                    Plugin.Logger.LogInfo("[VillageSeasonsPatch] Autumn: the Inn Keeper asks for dried food to stock up.");
                    return true;
                }

                case 4: // Winter: the snowed-in supper
                    if (!HiddenStat.Set(SupperArmedStatUid, 1f)) return false;
                    if (!HiddenStat.Set(WinterLatchUid, 1f)) return false;
                    Plugin.Logger.LogInfo("[VillageSeasonsPatch] Winter: the drifts close the roads and the supper is called.");
                    return true;

                default: // Spring: nothing to fire, latches cleared above
                    return true;
            }
        }

        private static void PublishSupperOpen()
        {
            float armed = HiddenStat.Get(SupperArmedStatUid);
            float open = HiddenStat.Get(SupperOpenStatUid);
            if (armed < 0f || open < 0f) return;

            float hour = GameQuery.HourOfDay;
            float want = (armed >= 0.5f && hour >= SupperEveningFrom) ? 1f : 0f;
            if (Math.Abs(open - want) > 0.01f && !HiddenStat.Set(SupperOpenStatUid, want))
                WarnOnce("supper-open-write", $"[VillageSeasonsPatch] {SupperOpenStatUid} did not take {want} - the Snowed-In Supper button may show at the wrong hour.");
        }

        // GameQuery.CurrentSeason is a NAME (null until the season resolves); 0 = unknown this frame.
        private static int SeasonIndex()
        {
            string season = GameQuery.CurrentSeason;
            if (string.IsNullOrEmpty(season)) return 0;
            if (season.Equals("Spring", StringComparison.OrdinalIgnoreCase)) return 1;
            if (season.Equals("Summer", StringComparison.OrdinalIgnoreCase)) return 2;
            if (season.Equals("Autumn", StringComparison.OrdinalIgnoreCase)) return 3;
            if (season.Equals("Winter", StringComparison.OrdinalIgnoreCase)) return 4;
            WarnOnce("season-unrecognised", $"[VillageSeasonsPatch] Unrecognized season name '{season}' - no season beat fires while the game reports it.");
            return 0;
        }
    }
}
