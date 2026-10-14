using System;
using System.Collections.Generic;
using BepInEx.Logging;
using HarmonyLib;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace WaterDrivenInfrastructure.Patcher
{
    /// <summary>
    /// The plumbed ACT bathtub (plan feature 16b). With ACT installed, an empty placed Copper Bathtub at a location
    /// with a RUNNING Mill Race Outlet (Placed; a Frozen or Closed outlet fills nothing) turns Full in place through
    /// CardUtil.TransformCardInPlace, then its Water Level (SpecialDurability2) is set to the full value, because
    /// SetModel writes no durability and a swapped tub left at 0 refuses Cold Bath. A Warm tub is never touched and
    /// manual filling stays. Polled on a framework real-time interval, subscribed once per process. ACT UniqueIDs
    /// live in ActCompat only.
    /// </summary>
    public static class BathtubPlumbingPatch
    {
        private const float PollSeconds = 5f;
        private const string WaterLevelStat = "SpecialDurability2";

        private static ManualLogSource Logger => Plugin.Logger;
        private static bool _subscribed;
        private static readonly HashSet<string> _warnedCauses = new HashSet<string>(StringComparer.Ordinal);

        public static void ApplyPatch(Harmony harmony)
        {
            if (_subscribed) return;
            TickEvents.Interval(PollSeconds, Poll, "WDI.BathtubPlumbing");
            _subscribed = true;
        }

        private static void Poll()
        {
            if (!ActCompat.IsInstalled) return;
            string envUid = GameQuery.CurrentEnvironmentUniqueId;
            if (string.IsNullOrEmpty(envUid)) return;

            List<object> tubs = null;
            foreach (var card in GameQuery.CardsInPlayerEnv())
            {
                if (card == null) continue;
                if (!string.Equals(CardUtil.GetCardUniqueId(card), ActCompat.BathtubEmptyPlacedUid, StringComparison.Ordinal)) continue;
                (tubs ?? (tubs = new List<object>())).Add(card);
            }
            if (tubs == null) return;
            if (!MillRaceNetwork.HasRunningOutletAt(envUid)) return;

            foreach (var tub in tubs)
                Fill(tub);
        }

        private static void Fill(object tub)
        {
            if (!CardUtil.TransformCardInPlace(tub, ActCompat.BathtubFullPlacedUid))
            {
                WarnOnce("transform-failed", "CardUtil.TransformCardInPlace refused the empty ACT bathtub; it stays empty (is " + ActCompat.BathtubFullPlacedUid + " loaded?)");
                return;
            }
            float max = CardUtil.GetDurabilityMax(tub, WaterLevelStat);
            if (float.IsNaN(max) || max <= 0f)
            {
                WarnOnce("water-level-max-unreadable", "The filled bathtub's Water Level maximum could not be read; using " + ActCompat.BathtubFullWaterLevel);
                max = ActCompat.BathtubFullWaterLevel;
            }
            if (!CardUtil.SetDurability(tub, WaterLevelStat, max))
                WarnOnce("water-level-write-failed", "CardUtil.SetDurability could not write the filled bathtub's Water Level; Cold Bath may refuse it");
            Logger?.LogInfo("[BathtubPlumbing] a running Mill Race Outlet filled an empty Copper Bathtub");
        }

        private static void WarnOnce(string cause, string message)
        {
            if (!_warnedCauses.Add(cause)) return;
            Logger?.LogWarning($"[BathtubPlumbing] {message} [cause '{cause}', logged once per session]");
        }
    }
}
