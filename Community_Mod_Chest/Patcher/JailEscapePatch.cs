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

        private static bool _initialized;
        private static bool _lastUnguarded;
        private static bool _unguardedKnown;

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

            TickEvents.Interval(2f, CheckForCatch, "JailTunnelCatchCheck");
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

                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;
                var tunnelCard = FindCardByUid(gm, TunnelUid);
                if (tunnelCard == null) return; // bed already moved back — nothing to catch

                HiddenStat.Set(ProgressStatUid, 0f);
                if (!CardUtil.TransformCardInPlace(tunnelCard, BedUid))
                {
                    Plugin.Logger.LogWarning("[JailEscapePatch] Caught mid-dig but force-transform back to Bed failed.");
                }

                float original = HiddenStat.Get(JailPatch.SentenceOriginalStatUid);
                if (original < 0f) original = 0f;
                float remaining = HiddenStat.Get(JailPatch.SentenceRemainingStatUid);
                if (remaining < 0f) remaining = 0f;

                float penaltyDays = original * 0.5f;
                float nextRemaining = Math.Min(JailPatch.MaxSentenceDays, remaining + penaltyDays);
                HiddenStat.Set(JailPatch.SentenceRemainingStatUid, nextRemaining);

                VillageCrimePatch.AddCrime(EscapeAttemptCrimeBump);

                Plugin.Logger.LogInfo(
                    $"[JailEscapePatch] The Watch returns and finds the tunnel exposed -- caught. " +
                    $"+{penaltyDays:0} day(s) ({remaining:0} -> {nextRemaining:0} of {JailPatch.MaxSentenceDays}), " +
                    $"+{EscapeAttemptCrimeBump:0} crime.");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[JailEscapePatch] CheckForCatch failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
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
