using System;
using System.Collections.Generic;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// D5, community service: the lawful road back. Captain Sterling's dialog (JSON) offers a job, sets the clamped
    /// marker <see cref="ArmedStatUid"/> from 1 to 0 and the job type <see cref="JobStatUid"/> to 1 or 2, and his thanks
    /// line pays the crime down, advances the completion counter and puts the marker back to 1. This class owns the one
    /// thing JSON cannot see: whether the work was actually done. When it was, it moves the marker from 0 to 0.5 and
    /// nothing else; it never touches the counter, which advances only in the thanks line.
    ///
    /// <para>Two jobs have a completion signal the code can observe, so two are built. <b>Firewood</b>
    /// (<see cref="JobFirewood"/>): <see cref="TownWoodPilePatch"/> calls <see cref="OnWoodStocked"/> after it has
    /// credited a stocking, and a stocking worth at least <see cref="MinFuelForJob"/> fuel counts. <b>Clear a drift</b>
    /// (<see cref="JobDrift"/>): a handler on each snow drift card, on the same action-key prefix the sealable gate
    /// uses, reads the drift's CurrentUsageDurability after every dig and counts the hit that takes it to zero (the
    /// gate's own epsilon). The third job in the plan, a night round with Iris Vane, is not built: a guard's patrol
    /// is a random pick of a node each cycle and nothing records the player walking one with her, so there is no
    /// signal to observe and none is invented here.</para>
    ///
    /// <para>Both amounts, the fuel floor and the crime paid (-6, in the thanks line's JSON) are tunable placeholders.
    /// Registered once per process (the early return on <c>_initialized</c> is load-bearing, root CLAUDE.md
    /// "Per-Run Event Registration"). Every miss leaves a warn-once line keyed on its cause.</para>
    /// </summary>
    internal static class CommunityServicePatch
    {
        internal const string ArmedStatUid = "cmcStatWatchServiceArmed";
        internal const string JobStatUid = "cmcStatWatchServiceJob";

        internal const int JobFirewood = 1;
        internal const int JobDrift = 2;

        /// <summary>TUNABLE PLACEHOLDER: the least fuel one stocking must credit to count as the firewood job (a log of
        /// wood, not a twig).</summary>
        internal const float MinFuelForJob = 20f;

        /// <summary>The sealable gate's own tolerance for "this MultiHit card is cleared" (repeated -1 hits leave a
        /// residue near 1e-6, never an exact zero).</summary>
        internal const float ClearedDurabilityEpsilon = 0.01f;

        /// <summary>Each drift card and the action-key prefix of its dig actions (the same prefix its sealable gate uses).</summary>
        private static readonly string[][] Drifts =
        {
            new[] { "cmcSnowDriftNorth", "CMC_SnowDriftNorth_CI_Dig" },
            new[] { "cmcSnowDriftSouth", "CMC_SnowDriftSouth_CI_Dig" },
            new[] { "cmcSnowDriftEast", "CMC_SnowDriftEast_CI_Dig" },
        };

        private static bool _initialized;
        private static readonly HashSet<string> _warned = new HashSet<string>();

        private static void WarnOnce(string cause, string message)
        {
            if (_warned.Add(cause)) Plugin.Logger.LogWarning("[CommunityServicePatch] " + message);
        }

        /// <summary>True when a job of type <paramref name="wanted"/> is accepted and under way: the job stat names it and
        /// the marker is at the accepted value 0 (not offered, not already done). Unreadable stats read -1 and never
        /// complete anything.</summary>
        internal static bool JobIsUnderWay(float job, float armed, int wanted)
        {
            return job >= 0f && armed >= 0f && Math.Abs(job - wanted) < 0.5f && armed < 0.25f;
        }

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            foreach (var drift in Drifts)
            {
                ActionRouter.Register(new ActionHandler
                {
                    Name = "WatchServiceDrift_" + drift[0],
                    CardUid = drift[0],
                    ActionKeyPrefix = drift[1],
                    Timing = ActionTiming.AfterWrapped,
                    After = OnDriftDug,
                });
            }
            Plugin.Logger.LogDebug("[CommunityServicePatch] initialized.");
        }

        /// <summary>Called by <see cref="TownWoodPilePatch"/> once a stocking has been credited to the pool.</summary>
        internal static void OnWoodStocked(float fuelCredited)
        {
            try
            {
                if (fuelCredited < MinFuelForJob) return;
                TryComplete(JobFirewood, "firewood carried to the Town Wood Pile");
            }
            catch (Exception ex)
            {
                WarnOnce("wood-exception", "OnWoodStocked failed: " + (ex.InnerException?.ToString() ?? ex.ToString()));
            }
        }

        private static void OnDriftDug(ActionContext ctx)
        {
            try
            {
                // Cheap exit first: nothing to do unless the drift job is the one under way.
                if (!JobIsUnderWay(HiddenStat.Get(JobStatUid), HiddenStat.Get(ArmedStatUid), JobDrift)) return;

                var card = ctx?.Card;
                if (card == null)
                {
                    WarnOnce("drift-card-null", "A drift dig reported no card, so the clearing could not be checked.");
                    return;
                }
                object raw = CardUtil.GetMemberValue(card, "CurrentUsageDurability");
                if (raw == null)
                {
                    WarnOnce("drift-durability-unreadable", "A snow drift's CurrentUsageDurability could not be read, so the clearing could not be checked.");
                    return;
                }
                float left = Convert.ToSingle(raw);
                if (left > ClearedDurabilityEpsilon) return; // still standing: a later hit finishes it
                TryComplete(JobDrift, "a snow drift cleared");
            }
            catch (Exception ex)
            {
                WarnOnce("drift-exception", "OnDriftDug failed: " + (ex.InnerException?.ToString() ?? ex.ToString()));
            }
        }

        private static void TryComplete(int job, string what)
        {
            float jobNow = HiddenStat.Get(JobStatUid);
            float armed = HiddenStat.Get(ArmedStatUid);
            if (jobNow < 0f || armed < 0f)
            {
                WarnOnce("stat-unreadable", "The service stats are unreadable, so finished work was not recorded.");
                return;
            }
            if (!JobIsUnderWay(jobNow, armed, job)) return;

            if (!HiddenStat.Set(ArmedStatUid, 0.5f))
            {
                WarnOnce("marker-write-failed", "The service marker could not be written, so finished work was not recorded.");
                return;
            }
            Plugin.Logger.LogInfo($"[CommunityServicePatch] Job done ({what}): the Captain's thanks is waiting.");
        }
    }
}
