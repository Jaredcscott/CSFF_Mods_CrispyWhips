using System;
using System.Collections.Generic;
using CSFFModFramework.Api;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// One-time Comfort reset (vanilla balance pass, plan Part II row 3.1.5, owner tile 5 "reset once on load").
    ///
    /// <para>Before 1.69.17 several CMC actions (the Clay Ocarina's Play a Tune, eating Herb Paste, Join the Table,
    /// the Snowed-In Supper, every resident's Chat) wrote vanilla Comfort directly. Comfort's base rate is 0, so
    /// each write was PERMANENT and a long save sits pinned near the cap. Removing the writes does not undo them,
    /// so this class sets Comfort's BASE back to 0 (vanilla's own base; no vanilla source writes it) once per save.</para>
    ///
    /// <para><b>Source-agnostic by design.</b> It clears the whole base, whatever wrote it, so it also clears what
    /// Sirus23's Lanolin Salve wrote. Sirus23 may ship its own latched reset for players without CMC; both
    /// writing base 0 on the same first load is harmless (the second finds 0 and does nothing). Worn garments,
    /// placed decor and statuses are modifiers, not base, so they are untouched and keep applying.</para>
    ///
    /// <para><b>Latch.</b> <see cref="LatchStatUid"/> (hidden, 0..1, rate 0) is read on every
    /// <c>TickEvents.DtpTick</c>, so the first tick after a load (when saved stats are certainly in place) does the
    /// work. An unreadable latch or Comfort stat retries on the next tick; the latch is written only AFTER the base
    /// reads 0, never before (a failed write retries rather than latching a save that was not reset). Registered
    /// once per process (root CLAUDE.md "Per-Run Event Registration", pattern b).</para>
    /// </summary>
    internal static class ComfortFixupPatch
    {
        internal const string ComfortStatUid = "be10ed6d196f5d043b7a819f9f1fd1ff";
        internal const string LatchStatUid = "cmcStatComfortFixupDone";

        private static bool _initialized;
        private static readonly HashSet<string> _warned = new(StringComparer.Ordinal);
        private static int _failedTicks;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            TickEvents.DtpTick += OnTick;
            Plugin.Logger.LogDebug("[ComfortFixupPatch] initialized.");
        }

        private static void OnTick()
        {
            try
            {
                float latch = HiddenStat.Get(LatchStatUid);
                if (latch < 0f) return;          // not readable yet (booting run)
                if (latch >= 0.5f) return;       // already done for this save

                float before = HiddenStat.GetBase(ComfortStatUid);
                if (float.IsNaN(before)) { CountFailure("comfort-unreadable", "the Comfort stat is not readable"); return; }

                if (!HiddenStat.SetBase(ComfortStatUid, 0f)) { CountFailure("comfort-write", "the Comfort base could not be set to 0"); return; }

                if (!HiddenStat.Set(LatchStatUid, 1f)) { CountFailure("latch-write", $"{LatchStatUid} could not be written (the reset runs again next tick, which is harmless)"); return; }

                _failedTicks = 0;
                if (Math.Abs(before) < 0.01f) Plugin.Logger.LogDebug("[ComfortFixupPatch] Comfort base already 0; latched.");
                else Plugin.Logger.LogInfo($"[ComfortFixupPatch] One-time Comfort reset: base {before:0.##} -> 0 (comfort written permanently by older CMC actions; garments and decor are not affected).");
            }
            catch (Exception ex)
            {
                CountFailure("exception", "OnTick threw: " + (ex.InnerException?.ToString() ?? ex.ToString()));
            }
        }

        private static void CountFailure(string cause, string what)
        {
            if (++_failedTicks >= 8 && _warned.Add(cause))
                Plugin.Logger.LogWarning($"[ComfortFixupPatch] {what}; the one-time Comfort reset keeps retrying each tick.");
        }
    }
}
