using System;
using System.Collections;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// The Hidden Tunnel (Village_Master_Plan.md §10.8.8). Everything player-initiated and
    /// expressible in JSON lives on <c>CMC_JailCellBed.json</c> / <c>CMC_JailCellTunnel.json</c>
    /// (Dig's gated <c>StatModifications</c>, Crawl's <c>MovePlayer</c> + stat writes). This class
    /// owns only the two things JSON cannot express:
    ///
    /// <list type="number">
    /// <item><b>The bed/tunnel concealment toggle.</b> Every other use of
    /// <see cref="CardUtil.TransformCardInPlace"/> in this codebase (AshBoarHuntPatch) is a
    /// one-shot life-cycle transform triggered by game state. This is the first use where the
    /// PLAYER repeatedly toggles the same pair back and forth by clicking a button — R10,
    /// §10.8.9. The primitive itself (<c>SetModel</c> + <c>ReinitCard</c>) has no direction bias,
    /// but this is new enough usage that it needs its own in-game verification (repeated toggle
    /// across a save/reload) before the rest of the escape arc can be trusted — see the build
    /// report for this chunk for whether that verification has actually been run. Progress is kept
    /// entirely off both cards' own durability fields (a hidden GameStat instead) specifically to
    /// avoid needing to reason about durability surviving the swap at all
    /// (reference_ingamecard_cache_divergence).</item>
    /// <item><b>Getting caught (§10.8.8.3).</b> There is no declarative "on stat transition"
    /// primitive, so this polls <c>cmcStatJailUnguarded</c> for the 1-&gt;0 edge (mirroring
    /// JailPatch.UpdateUnguardedFlag's own idiom) and, if the Tunnel card is still on the board at
    /// that instant, resets progress, force-transforms back to Bed, and applies the penalty.</item>
    /// <item><b>The headcount and the deferred hole fill (Community_Mod_Chest MASTER_PLAN §1.3).</b>
    /// Now that the tunnel leads to the Skylit Cave, which the player comes back from, the guard
    /// pressure below is a twice-daily headcount (<see cref="RunHeadcount"/>) with a saved latch,
    /// and the fill it orders is applied only on the cell's own board (<see cref="RunDeferredFill"/>).</item>
    /// </list>
    /// </summary>
    internal static class JailEscapePatch
    {
        private const string BedUid = "cmcJailCellBed";
        private const string TunnelUid = "cmcJailCellTunnel";
        private const string ProgressStatUid = "cmcStatJailTunnelProgress";

        private const string MoveBedActionKey = "CMC_JailCellBed_DA_MoveBed";
        private const string MoveBedBackActionKey = "CMC_JailCellTunnel_DA_MoveBedBack";

        /// <summary>§10.8.8.3 penalty, open question 12 (unconfirmed by the owner) — picked to
        /// read clearly worse than a first-arrest theft detection (+10, CopperChestPatch) without
        /// being large enough to strand a low-crime player at the Banished floor by itself.
        /// Tunable.</summary>
        private const float EscapeAttemptCrimeBump = 5f;

        // ── The headcount (MASTER_PLAN §1.3 steps 3-5) ───────────────────────────
        // Constants first; the logic and its reasoning are on RunHeadcount and RunDeferredFill.

        private const string CaveEnvUid = "cmcEnvJailCave";
        private const string LedgeEnvUid = "cmcEnvJailLedge";

        /// <summary>Hidden latch: the id of the last headcount this class PROCESSED (caught or
        /// not), +1-offset so 0 means never. id = gameDay * HeadcountClockHours.Length + slot, so
        /// it only grows. Saved with the game, so a reload never re-processes a headcount.</summary>
        internal const string HeadcountLastStatUid = "cmcStatJailHeadcountLast";

        /// <summary>Hidden clamped 0/1 marker: a headcount caught the player below and the cell
        /// end of the tunnel is still to be filled. While 1 the cave's "Crawl back to the cell" is
        /// hidden (its DA gates on this stat) and its "bricked up" notice shows instead. Cleared
        /// only by <see cref="RunDeferredFill"/>, after the swap back to the bed succeeded.</summary>
        internal const string FillPendingStatUid = "cmcStatJailFillPending";

        /// <summary>The Watch counts the prisoner at these CLOCK hours (06:00 and 21:00).
        /// Placeholder times, tunable (MASTER_PLAN §1.6 question 2). Compared against
        /// <see cref="GameQuery.HourOfDay"/>, which is the on-screen clock hour since framework
        /// plan D6 (2026-09-17), no longer the dawn-relative count its old memory note
        /// describes.</summary>
        private static readonly int[] HeadcountClockHours = { 6, 21 };

        /// <summary>Used only when <c>GameManager.DaySettings</c> cannot be read: the shipped
        /// <c>DayStartingHour</c> (the same assumption <see cref="GameQuery.HourOfDay"/>'s
        /// fallback makes).</summary>
        private const int AssumedDayStartingHour = 4;

        private static bool _initialized;
        private static bool _lastUnguarded;
        private static bool _unguardedKnown;
        private static int _fillNotReadyPolls;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            ActionRouter.Register(new ActionHandler
            {
                Name          = "JailTunnelMoveBed",
                CardUid       = BedUid,
                ActionKeyPrefix = MoveBedActionKey,
                Timing        = ActionTiming.AfterWrapped,
                After         = ctx =>
                {
                    if (CardUtil.TransformCardInPlace(ctx.Card, TunnelUid))
                        Plugin.Logger.LogInfo("[JailEscapePatch] The bed slides aside -- the tunnel is exposed.");
                    else
                        Plugin.Logger.LogWarning("[JailEscapePatch] Move the bed: transform to Tunnel failed.");
                },
            });

            ActionRouter.Register(new ActionHandler
            {
                Name          = "JailTunnelMoveBedBack",
                CardUid       = TunnelUid,
                ActionKeyPrefix = MoveBedBackActionKey,
                Timing        = ActionTiming.AfterWrapped,
                After         = ctx =>
                {
                    if (CardUtil.TransformCardInPlace(ctx.Card, BedUid))
                        Plugin.Logger.LogInfo("[JailEscapePatch] The bed is back in place -- the tunnel is hidden.");
                    else
                        Plugin.Logger.LogWarning("[JailEscapePatch] Move the bed back: transform to Bed failed.");
                },
            });

            // Registered once per process (the _initialized guard above): TickEvents and
            // ActionRouter are process-wide, so a per-run re-registration would double every
            // handler after a save reload (root CLAUDE.md "Per-Run Event Registration").
            TickEvents.Interval(2f, PollEscape, "JailTunnelCatchCheck");
            // The headcount also runs on every DTP change, so the first poll after a slot passes
            // is the game tick that crossed it, not up to two real seconds later.
            TickEvents.DtpTick += RunHeadcount;
            Plugin.Logger.LogDebug("[JailEscapePatch] initialized.");
        }

        // ── Getting caught (§10.8.8.3) ────────────────────────────────────────────

        /// <summary>
        /// Edge-detects <c>cmcStatJailUnguarded</c> 1-&gt;0 (a guard has returned) and, only on
        /// that transition, checks whether the Tunnel card is still on the board. If the bed was
        /// already moved back in time, the Tunnel card is gone (transformed back to Bed by the
        /// player) and this is a silent no-op — no penalty, progress untouched.
        /// </summary>
        private static void CheckForCatch()
        {
            try
            {
                float unguardedRaw = HiddenStat.Get(JailPatch.UnguardedStatUid);
                if (unguardedRaw < 0f) return; // not readable yet
                bool unguarded = unguardedRaw >= 0.5f;

                if (!_unguardedKnown)
                {
                    _unguardedKnown = true;
                    _lastUnguarded = unguarded;
                    return; // first read after load — establish baseline, not an edge
                }
                if (unguarded == _lastUnguarded) return;
                bool windowJustClosed = _lastUnguarded && !unguarded;
                _lastUnguarded = unguarded;
                if (!windowJustClosed) return;

                // INTENTIONAL: a warden gap that closes while the player is BELOW (in the Skylit
                // Cave or on the Ledge) is NOT a catch. The guard pressure below is the headcount
                // (RunHeadcount), and the Watch does not climb down (MASTER_PLAN §1.3 step 5).
                // FindCardByUid scans GameManager.AllCards, which holds only the CURRENT board
                // plus carried and IndependentFromEnv cards, so the Tunnel card is invisible from
                // below and this has always been a no-op there. This explicit env test says so in
                // code: if the scan is ever "fixed" to see other boards' saved cards, a gap closing
                // over an empty cell still must not become a second, unannounced catch.
                if (GameQuery.CurrentEnvironmentUniqueId != JailPatch.JailCellEnvUid) return;

                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;
                var tunnelCard = FindCardByUid(gm, TunnelUid);
                if (tunnelCard == null) return; // bed already moved back — nothing to catch

                HiddenStat.Set(ProgressStatUid, 0f);
                if (!CardUtil.TransformCardInPlace(tunnelCard, BedUid))
                {
                    Plugin.Logger.LogWarning("[JailEscapePatch] Caught mid-dig but force-transform back to Bed failed.");
                }

                ApplyCatchPenalty("The Watch returns and finds the tunnel exposed");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[JailEscapePatch] CheckForCatch failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        /// <summary>
        /// The one catch penalty, shared by the warden-gap catch and the headcount: half the
        /// ORIGINAL sentence rounded up onto what remains (capped at
        /// <see cref="JailPatch.MaxSentenceDays"/>) and <see cref="EscapeAttemptCrimeBump"/> crime.
        /// </summary>
        private static void ApplyCatchPenalty(string what)
        {
            float original = HiddenStat.Get(JailPatch.SentenceOriginalStatUid);
            if (original < 0f) original = 0f;
            float remaining = HiddenStat.Get(JailPatch.SentenceRemainingStatUid);
            if (remaining < 0f) remaining = 0f;

            // Whole days only, rounded up. The cell door's "Your sentence: N more days" lines
            // gate on exact integer values (StatValueTrigger floors the stat before comparing),
            // so a half-day remainder from an odd original sentence would read one day short.
            float penaltyDays = (float)Math.Ceiling(original * 0.5f);
            float nextRemaining = Math.Min(JailPatch.MaxSentenceDays, remaining + penaltyDays);
            HiddenStat.Set(JailPatch.SentenceRemainingStatUid, nextRemaining);

            VillageCrimePatch.AddCrime(EscapeAttemptCrimeBump);

            Plugin.Logger.LogInfo(
                $"[JailEscapePatch] {what} -- caught. " +
                $"+{penaltyDays:0} day(s) ({remaining:0} -> {nextRemaining:0} of {JailPatch.MaxSentenceDays}), " +
                $"+{EscapeAttemptCrimeBump:0} crime.");
        }

        // ── The headcount and the deferred hole fill (MASTER_PLAN §1.3 steps 3-4) ──

        private static void PollEscape()
        {
            CheckForCatch();
            RunHeadcount();
            RunDeferredFill();
        }

        /// <summary>
        /// Twice a day (<see cref="HeadcountClockHours"/>) the Watch counts the prisoner. A
        /// headcount that finds a serving prisoner (sentence 1 day or more) below, in the Skylit
        /// Cave or on the Ledge, is a catch: <see cref="ApplyCatchPenalty"/> once, and the cell end
        /// of the tunnel is marked for filling (<see cref="FillPendingStatUid"/>), done by
        /// <see cref="RunDeferredFill"/> the next time the cell is the current board.
        ///
        /// <para><b>Once per scheduled headcount, by a saved latch, not by an edge.</b> Every call
        /// works out the most recent headcount at or before "now" and processes it only if its id
        /// is above <see cref="HeadcountLastStatUid"/>. So: a sleep or a long action that jumps
        /// straight over 21:00 is processed by the first call after it (the player is still where
        /// they slept, so that is where the Watch finds them); a sleep that jumps over BOTH slots
        /// processes only the latest one (one count, at most one penalty), and the skipped one is
        /// never replayed; a save reload restores the latch with the save, so a headcount processed
        /// before the save is never processed again, and one processed after it was never saved
        /// (the reloaded game reaches it again and processes it once). The latch is advanced at
        /// every headcount, in jail or not, so a fresh sentence never inherits a stale count.</para>
        ///
        /// <para><b>No double penalty.</b> The latch is written BEFORE the penalty, and a failed
        /// latch write returns without one, so a retry can never charge the same headcount twice.
        /// A headcount while the fill is already pending (the escape was found at an earlier
        /// count and the player is still below) only advances the latch: the Watch already knows.
        /// </para>
        ///
        /// <para>"Below" is the two escape rooms, not "anywhere but the cell": a serving prisoner
        /// is elsewhere only in the moment between sentencing and the arrest's move into the cell,
        /// and a headcount landing in that window must not read as an escape.</para>
        /// </summary>
        private static void RunHeadcount()
        {
            try
            {
                if (GameQuery.IsTransitioning) return;
                if (GameQuery.DayTimePoints < 0) return; // game not running
                string env = GameQuery.CurrentEnvironmentUniqueId;
                if (string.IsNullOrEmpty(env)) return;

                float stored = HiddenStat.Get(HeadcountLastStatUid);
                if (stored < 0f) return; // stat not ready
                float remaining = HiddenStat.Get(JailPatch.SentenceRemainingStatUid);
                if (remaining < 0f) return;
                float pending = HiddenStat.Get(FillPendingStatUid);
                if (pending < 0f) return;

                if (!TryLatestHeadcount(GameQuery.CurrentDay, GameQuery.HourOfDay, ReadDayStartingHour(),
                        out int headcountId, out int clockHour))
                    return;
                int lastId = stored < 0.5f ? -1 : (int)Math.Round(stored) - 1; // +1-offset "0 = never"
                if (headcountId <= lastId) return; // already processed (also after a reload)

                // Latch FIRST: a headcount whose latch cannot be written is not processed at all.
                if (!HiddenStat.Set(HeadcountLastStatUid, headcountId + 1)) return;

                bool below = env == CaveEnvUid || env == LedgeEnvUid;
                // Whole days: Break Rocks can leave a quarter-day remainder, and under one day the
                // cell door already reads 0 and opens the Exit, so that prisoner is not serving.
                bool serving = remaining >= 1f;
                if (!below || !serving) return;

                if (pending >= 0.5f)
                {
                    Plugin.Logger.LogInfo(
                        $"[JailEscapePatch] {clockHour:00}:00 headcount: still missing below; the hole is already being filled (no second penalty).");
                    return;
                }

                if (!HiddenStat.Set(FillPendingStatUid, 1f))
                    Plugin.Logger.LogWarning("[JailEscapePatch] Headcount catch: the fill-pending marker could not be written; the cell end stays open.");
                ApplyCatchPenalty($"{clockHour:00}:00 headcount: the cell is empty and the Watch finds the tunnel");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[JailEscapePatch] RunHeadcount failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        /// <summary>
        /// The most recent scheduled headcount at or before now. A game day runs from
        /// <paramref name="dayStartHour"/> (04:00 shipped) for 24 hours and
        /// <see cref="GameQuery.CurrentDay"/> counts those days, so each slot's offset into the
        /// game day is <c>(slotHour - dayStartHour) mod 24</c> and "now" is
        /// <c>day * 24 + (clockHour - dayStartHour) mod 24</c> hours. The slots are numbered in
        /// that offset order, so <paramref name="headcountId"/> = day * slots + index grows with
        /// time. Only today and yesterday are searched: the latest slot is always within 24 hours.
        /// </summary>
        internal static bool TryLatestHeadcount(int day, float clockHour, int dayStartHour,
            out int headcountId, out int slotClockHour)
        {
            headcountId = -1;
            slotClockHour = -1;
            int slots = HeadcountClockHours.Length;
            var offsets = new float[slots];
            var hours = new int[slots];
            for (int i = 0; i < slots; i++)
            {
                hours[i] = HeadcountClockHours[i];
                offsets[i] = Wrap24(HeadcountClockHours[i] - dayStartHour);
            }
            Array.Sort(offsets, hours); // number the slots in game-day order

            double now = day * 24.0 + Wrap24(clockHour - dayStartHour);
            double best = double.NegativeInfinity;
            for (int d = day; d >= day - 1 && d >= 0; d--)
            {
                for (int i = 0; i < slots; i++)
                {
                    double t = d * 24.0 + offsets[i];
                    if (t > now + 0.001 || t <= best) continue;
                    best = t;
                    headcountId = d * slots + i;
                    slotClockHour = hours[i];
                }
            }
            return headcountId >= 0;
        }

        private static float Wrap24(float hours)
        {
            float r = hours % 24f;
            return r < 0f ? r + 24f : r;
        }

        private static int ReadDayStartingHour()
        {
            try
            {
                var gm = MBSingleton<GameManager>.Instance;
                if (gm && gm.DaySettings.DailyPoints > 0) return gm.DaySettings.DayStartingHour;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogDebug($"[JailEscapePatch] DaySettings read threw: {ex.Message}");
            }
            return AssumedDayStartingHour;
        }

        /// <summary>
        /// Fills the hole after a headcount catch: zeroes <c>cmcStatJailTunnelProgress</c> and
        /// swaps every Tunnel card on the cell board back to the bed. Deferred to a poll that runs
        /// only while the cell is the CURRENT board (a background board cannot be edited reliably),
        /// in the env-gated idiom of <c>JailPatch.GrantDailyRationIfDue</c>. It retries every poll
        /// and clears <see cref="FillPendingStatUid"/> only after the swap succeeded (root CLAUDE.md,
        /// "Environment-entry logic must RETRY"): a cell board that shows neither the Tunnel nor the
        /// bed yet is NOT READY (the arrival window before the env's own cards exist), never "done".
        /// Progress in the cave and on the ledge is kept: the Watch bricks up the cell end only.
        /// </summary>
        private static void RunDeferredFill()
        {
            try
            {
                if (GameQuery.IsTransitioning) return;
                if (GameQuery.CurrentEnvironmentUniqueId != JailPatch.JailCellEnvUid) return;
                float pending = HiddenStat.Get(FillPendingStatUid);
                if (pending < 0.5f) return; // not pending, or not readable yet

                var tunnels = new System.Collections.Generic.List<object>();
                bool bedSeen = false;
                // Env-scoped (AllCards is not "this room"), and EVERY Tunnel instance: a
                // UniqueOnBoard card can still be live twice (root CLAUDE.md cache divergence item 4).
                foreach (var card in GameQuery.CardsInPlayerEnv())
                {
                    string uid = CardUtil.GetCardUniqueId(card);
                    if (uid == TunnelUid) tunnels.Add(card);
                    else if (uid == BedUid) bedSeen = true;
                }
                if (tunnels.Count == 0 && !bedSeen)
                {
                    if (++_fillNotReadyPolls == 30)
                        Plugin.Logger.LogWarning("[JailEscapePatch] Hole fill pending, but the cell board has shown neither the bed nor the tunnel for 30 polls; still retrying.");
                    return; // board not ready: retry
                }

                bool allSwapped = true;
                foreach (var tunnel in tunnels)
                    if (!CardUtil.TransformCardInPlace(tunnel, BedUid)) allSwapped = false;
                if (!allSwapped)
                {
                    Plugin.Logger.LogDebug("[JailEscapePatch] Hole fill: a Tunnel did not swap back to the bed; retrying next poll.");
                    return;
                }

                if (!HiddenStat.Set(ProgressStatUid, 0f)) return;
                if (!HiddenStat.Set(FillPendingStatUid, 0f)) return;
                _fillNotReadyPolls = 0;
                Plugin.Logger.LogInfo(
                    $"[JailEscapePatch] The Watch has filled the hole under the bed ({tunnels.Count} tunnel card(s) swapped back); dig progress reset.");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[JailEscapePatch] RunDeferredFill failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static object FindCardByUid(object gm, string uid)
        {
            if (Reflect.GetMember(gm, "AllCards") is not IEnumerable allCards) return null;
            foreach (var card in allCards)
            {
                if (card == null) continue;
                var model = Reflect.GetMember(card, "CardModel");
                if (model == null) continue;
                if (Reflect.GetMember(model, "UniqueID") as string == uid) return card;
            }
            return null;
        }
    }
}
