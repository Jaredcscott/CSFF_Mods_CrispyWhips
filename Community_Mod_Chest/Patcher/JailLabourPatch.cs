using System;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// D4, jail labour. The Jail Cell's "Break Rocks" DismantleAction (JSON: four hours, a Stamina cost, and a
    /// +1 on the clamped 0..2 counter <see cref="LabourTodayStatUid"/>) is the player's way to work time off a
    /// sentence. This class owns the effect: an AfterWrapped handler takes <see cref="LabourSentenceDays"/> of a
    /// day off <c>cmcStatJailSentenceRemaining</c> and pays crime down at the rate days served do
    /// (<see cref="LabourCrimePaydown"/>, a quarter of <c>JailPatch.DailyCrimePaydown</c>). Two uses a day: the
    /// JSON hides the button once the counter reaches 2 (RequiredStatValues with HideIfConditionsNotMet, the
    /// gate the game itself checks before it lists the action); the counter is set back to 0 at every day rollover.
    ///
    /// <para>The sentence stat is no longer always a whole number: <see cref="JailPatch"/> now takes a plain day
    /// off at each rollover and sets a remainder under one day to 0 there, and the cell door's "N more days"
    /// buttons already floor the stat, so a quarter-day value reads as its whole day.</para>
    ///
    /// <para>Labour never releases anyone by itself. The button needs at least two whole days left (the JSON's
    /// floored RequiredStatValues range) and <see cref="SentenceAfterLabour"/> never goes below 1, so the sentence
    /// still runs out through the rollover that clears the crime record; labour shortens time and does not skip
    /// that step. The amounts are tunable placeholders.</para>
    ///
    /// <para>Registered once per process (the early return on <c>_initialized</c> is load-bearing, root
    /// CLAUDE.md "Per-Run Event Registration"). Every miss leaves a warn-once line keyed on its cause.</para>
    /// </summary>
    internal static class JailLabourPatch
    {
        /// <summary>The Jail Cell location card that owns the Break Rocks DismantleAction.</summary>
        internal const string JailCellLocationUid = "cmcJailCellLocation";
        internal const string BreakRocksKeyPrefix = "CMC_JailCellLocation_DA_BreakRocks";

        /// <summary>0..2: uses of Break Rocks today. Set back to 0 at every day rollover.</summary>
        internal const string LabourTodayStatUid = "cmcStatJailLabourToday";

        /// <summary>TUNABLE PLACEHOLDER: the largest number of uses in one day.</summary>
        internal const int MaxUsesPerDay = 2;

        /// <summary>TUNABLE PLACEHOLDER: the part of a day one use takes off the sentence.</summary>
        internal const float LabourSentenceDays = 0.25f;

        /// <summary>TUNABLE PLACEHOLDER: crime paid down per use, the same rate per day served as a whole day.</summary>
        internal const float LabourCrimePaydown = 8f * LabourSentenceDays;

        private static bool _initialized;
        private static readonly System.Collections.Generic.HashSet<string> _warned = new System.Collections.Generic.HashSet<string>();

        private static void WarnOnce(string cause, string message)
        {
            if (_warned.Add(cause)) Plugin.Logger.LogWarning("[JailLabourPatch] " + message);
        }

        /// <summary>The sentence left after one use: a quarter day off, never below one whole day. The cell door
        /// floors the stat before it compares, so anything under 1 would open the Exit before the rollover that
        /// clears the crime record.</summary>
        internal static float SentenceAfterLabour(float remaining)
        {
            return Math.Max(1f, remaining - LabourSentenceDays);
        }

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            ActionRouter.Register(new ActionHandler
            {
                Name = "JailBreakRocksApply",
                CardUid = JailCellLocationUid,
                ActionKeyPrefix = BreakRocksKeyPrefix,
                Timing = ActionTiming.AfterWrapped,
                After = ApplyLabour,
            });

            TickEvents.DayRollover += OnDayRollover;
            Plugin.Logger.LogDebug("[JailLabourPatch] initialized.");
        }

        private static void ApplyLabour(ActionContext ctx)
        {
            try
            {
                float remaining = HiddenStat.Get(JailPatch.SentenceRemainingStatUid);
                if (remaining < 0f)
                {
                    WarnOnce("sentence-unreadable", "Break Rocks: the sentence stat is unreadable, so no time was taken off.");
                    return;
                }
                if (remaining <= 0f) return; // not serving: nothing to shorten

                float next = SentenceAfterLabour(remaining);
                bool wrote = HiddenStat.Set(JailPatch.SentenceRemainingStatUid, next);
                if (!wrote)
                {
                    WarnOnce("sentence-write-failed", "Break Rocks: the sentence stat could not be written, so no time or crime was taken off.");
                    return;
                }
                float crime = VillageCrimePatch.ReduceCrime(LabourCrimePaydown, "labour in the Village Jail");
                Plugin.Logger.LogInfo($"[JailLabourPatch] Broke rocks: sentence {remaining:0.##} -> {next:0.##} day(s), crime now {crime:0.##}.");
            }
            catch (Exception ex)
            {
                WarnOnce("apply-exception", "ApplyLabour failed: " + (ex.InnerException?.ToString() ?? ex.ToString()));
            }
        }

        private static void OnDayRollover()
        {
            try
            {
                float used = HiddenStat.Get(LabourTodayStatUid);
                if (used < 0f)
                {
                    WarnOnce("counter-unreadable-reset", "The daily labour counter is unreadable at the day rollover, so it was not reset.");
                    return;
                }
                if (used > 0f && !HiddenStat.Set(LabourTodayStatUid, 0f))
                    WarnOnce("counter-reset-failed", "The daily labour counter could not be reset at the day rollover.");
            }
            catch (Exception ex)
            {
                WarnOnce("reset-exception", "OnDayRollover failed: " + (ex.InnerException?.ToString() ?? ex.ToString()));
            }
        }
    }
}
