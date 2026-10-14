using System;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// The Inn Counter's odd-jobs board (round 2 feature J2). Besides the always-on "Work Odd Jobs" shift, the
    /// counter posts ONE small job each morning: bring a Stone, Rope, Ash or a Clay Bowl and the Inn pays a copper
    /// nugget. Every number here is a tunable placeholder.
    ///
    /// <para><b>State is in the save, never in a static</b> (the stall robbery clock's lesson). Two hidden clamped
    /// GameStats: <see cref="JobStatUid"/> (0 = none or done, 1 to <see cref="JobCount"/> = today's job) and
    /// <see cref="DayStatUid"/> (the in-game day plus one, 0 = never posted). <see cref="RollIfNewDay"/> runs on
    /// every <c>TickEvents.DtpTick</c> and posts a job only when the stamp is not today, writing the STAMP FIRST: a
    /// stat that cannot be written then posts nothing (a warn-once line) instead of re-rolling, and so re-opening
    /// a finished job, every quarter hour.</para>
    ///
    /// <para><b>The buttons.</b> One CardInteraction per job on the Inn Counter (CMC_InnCounter.json), keyed
    /// <see cref="KeyPrefix"/> plus the job's suffix, shown only while <see cref="JobStatUid"/> holds that job's
    /// number (a JSON RequiredStatValues gate with HideIfConditionsNotMet). JSON gates fall OPEN when their stat did
    /// not load, so <c>InnOddJobGate</c> re-checks in C# and refuses unless the stat reads exactly this job: an
    /// unreadable stat blocks the hand-over (nothing consumed) rather than paying four jobs.</para>
    ///
    /// <para><b>Pay and never-priced.</b> The nugget comes from the button's own ProducedCards (TransferRules set
    /// so it spawns typed and not at quality 0); the job closes through the button's own stat change and again in
    /// <c>InnOddJobDelivered</c>. Nothing here reaches VillagePricing or InnPatch.BalanceGate: the board is ungated
    /// by crime, so a Banished player keeps a way to raise restitution money, and both handlers are on
    /// VillagePricing.RestitutionHandlers (CMC-Round2.Tests.ps1 keeps it that way). The key prefix does not start
    /// with any other counter prefix, and no action name contains one of InnPatch's four words (Night, Meal,
    /// Deposit, Firewood).</para>
    ///
    /// <para>Registration runs once per process from Plugin.Awake under an _initialized guard (CLAUDE.md
    /// "Per-Run Event Registration", pattern b).</para>
    /// </summary>
    internal static class InnOddJobPatch
    {
        internal const string CounterUid = "cmcInnCounter";
        internal const string KeyPrefix = "CMC_InnCounter_OddJob";
        internal const string JobStatUid = "cmcStatInnOddJob";
        internal const string DayStatUid = "cmcStatInnOddJobDay";

        /// <summary>One entry per job, in job-number order (job 1 is index 0): the CardInteraction's key stem.
        /// Each starts with <see cref="KeyPrefix"/>; the button's ActionName key is the stem plus "_ActionName".</summary>
        internal static readonly string[] JobKeys =
        {
            "CMC_InnCounter_OddJob_Stone",
            "CMC_InnCounter_OddJob_Rope",
            "CMC_InnCounter_OddJob_Ash",
            "CMC_InnCounter_OddJob_Bowl",
        };

        internal static int JobCount => JobKeys.Length;

        internal const string DoneKey = "CMC_OddJob_Done";
        internal const string NotTodayKey = "CMC_OddJob_NotToday";

        /// <summary>Consecutive DtpTicks a stat may read unreadable before the one warn-once line (a stat is
        /// legitimately unreadable for a moment while a run boots).</summary>
        private const int UnreadableTicksBeforeWarning = 8;

        private static bool _initialized;
        private static int _unreadableTicks;
        private static readonly System.Collections.Generic.HashSet<string> _warned = new(StringComparer.Ordinal);

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            ActionRouter.Register(new ActionHandler
            {
                Name            = "InnOddJobGate",
                CardUid         = CounterUid,
                ActionKeyPrefix = KeyPrefix,
                Timing          = ActionTiming.Cancel,
                Before          = ctx => JobGate(ctx),
            });

            ActionRouter.Register(new ActionHandler
            {
                Name            = "InnOddJobDelivered",
                CardUid         = CounterUid,
                ActionKeyPrefix = KeyPrefix,
                Timing          = ActionTiming.AfterWrapped,
                Before          = ctx => false,
                After           = ctx => JobDelivered(ctx),
            });

            TickEvents.DtpTick += RollIfNewDay;

            Plugin.Logger.LogDebug("[InnOddJobPatch] initialized.");
        }

        // ── Pure helpers (CMC-Round2.Tests.ps1 and harness probes read these) ────────────────

        /// <summary>The job number a draw in [0, 1) posts: 1 to <see cref="JobCount"/>, each equally likely.</summary>
        internal static int PickJob(double roll)
        {
            double r = Math.Min(0.999999, Math.Max(0.0, roll));
            return Math.Min(JobCount, 1 + (int)Math.Floor(r * JobCount));
        }

        /// <summary>True when the day stamp (day plus one, 0 = never) names today.</summary>
        internal static bool StampIsToday(float stamp, int today) => (int)Math.Round(stamp) == today + 1;

        /// <summary>The job number an action key belongs to, or 0 when it is not one of the board's buttons.</summary>
        internal static int JobIdOf(string actionKey)
        {
            if (string.IsNullOrEmpty(actionKey)) return 0;
            for (int i = 0; i < JobKeys.Length; i++)
                if (actionKey.StartsWith(JobKeys[i], StringComparison.Ordinal)) return i + 1;
            return 0;
        }

        // ── The morning roll ─────────────────────────────────────────────────────────────────

        private static void RollIfNewDay()
        {
            try
            {
                int today = GameQuery.CurrentDay;
                if (today < 0) return;

                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;

                float stamp = InnEveningPatch.ReadStat(gm, DayStatUid);
                if (stamp < 0f)
                {
                    if (++_unreadableTicks == UnreadableTicksBeforeWarning)
                        WarnOnce("stamp-unreadable", $"[InnOddJobPatch] {DayStatUid} is not readable - the Inn Counter posts no odd job until it is.");
                    return;
                }
                _unreadableTicks = 0;
                if (StampIsToday(stamp, today)) return;

                // Stamp first: if it cannot be written, post nothing rather than re-rolling every tick.
                if (!InnEveningPatch.WriteStat(gm, DayStatUid, today + 1))
                {
                    WarnOnce("stamp-write", $"[InnOddJobPatch] {DayStatUid} write failed - the Inn Counter posts no odd job.");
                    return;
                }

                int job = PickJob(UnityEngine.Random.value);
                if (!InnEveningPatch.WriteStat(gm, JobStatUid, job))
                {
                    WarnOnce("job-write", $"[InnOddJobPatch] {JobStatUid} write failed - today's odd job is not posted.");
                    return;
                }
                Plugin.Logger.LogDebug($"[InnOddJobPatch] day {today}: posted odd job {job} ({JobKeys[job - 1]}).");
            }
            catch (Exception ex)
            {
                WarnOnce("roll-exception", $"[InnOddJobPatch] RollIfNewDay failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // ── The hand-over ────────────────────────────────────────────────────────────────────

        /// <summary>Cancel unless the job stat reads exactly this button's job. An unreadable stat cancels.</summary>
        private static bool JobGate(ActionContext ctx)
        {
            int wanted = JobIdOf(ctx.ActionKey);
            var gm = CardUtil.GetGameManagerInstance();
            float posted = gm == null ? -1f : InnEveningPatch.ReadStat(gm, JobStatUid);
            if (wanted > 0 && posted >= 0f && (int)Math.Round(posted) == wanted) return false; // proceed

            Plugin.Logger.LogDebug($"[InnOddJobPatch] '{ctx.ActionKey}' refused: posted job reads {posted:0.#}, this button is job {wanted}.");
            if (posted < 0f)
                WarnOnce("gate-unreadable", $"[InnOddJobPatch] {JobStatUid} is not readable - odd-job hand-overs are refused until it is.");
            else
                WorkBoardNotify(ctx.Card, NotTodayKey);
            return true; // cancel
        }

        private static void JobDelivered(ActionContext ctx)
        {
            try
            {
                var gm = CardUtil.GetGameManagerInstance();
                if (gm != null && !InnEveningPatch.WriteStat(gm, JobStatUid, 0f))
                    WarnOnce("close-write", $"[InnOddJobPatch] {JobStatUid} could not be closed after a hand-over - the button's own stat change is the only close.");
                WorkBoardNotify(ctx.Card, DoneKey);
                Plugin.Logger.LogDebug($"[InnOddJobPatch] '{ctx.ActionKey}' delivered: job closed for the day.");
            }
            catch (Exception ex)
            {
                WarnOnce("delivered-exception", $"[InnOddJobPatch] JobDelivered failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void WorkBoardNotify(object card, string key) => InnWagerPatch.Notify(card, key, 0f);

        private static void WarnOnce(string cause, string message)
        {
            if (_warned.Add(cause)) Plugin.Logger.LogWarning(message);
        }
    }
}
