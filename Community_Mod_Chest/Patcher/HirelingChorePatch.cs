using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Hireling chores (CMC Part II row N1; session-inferred design, every price and duration a tunable
    /// placeholder): pay a resident in Salt to do one vanilla chore you would do yourself. Built so far: ONE
    /// chore at ONE resident, the Miller splitting four Log Pieces into twelve Firewood. The scutch-flax and the
    /// dry-a-rack chores are NOT built.
    ///
    /// The chore is vanilla's own: TreeLogPieceItem's "Cut into Firewood" turns one Log Piece into 3 Firewood
    /// (<see cref="FirewoodPerLog"/>) and copies the log's Quality (Special2) and wood ID (Special4) onto each.
    /// The player supplies the inputs, so no goods are minted: the Miller gives back exactly
    /// <see cref="LogsPerChore"/> x <see cref="FirewoodPerLog"/> Firewood, at the lowest Quality among the logs
    /// handed over and the wood ID of the first (a hireling holds no stack of mixed wood). The
    /// <c>CMC-HirelingChores</c> Pester check compares those constants with the vanilla recipe.
    ///
    /// The flow is three clamped hidden stats and one day stamp, never an unclamped counter (the root CLAUDE.md
    /// "NPC Errand Stat Design" rule):
    ///   - the LOGS stat, 0..4: each Log Piece dragged onto the Miller adds 1 in JSON (the card is taken); the
    ///     drag hides at 4. The Cancel gate below only records the log's Quality and ID, it never refuses;
    ///   - the FEE stat, 0..2: each Salt dragged on once the logs are in adds 1 (the Salt is taken);
    ///   - the ORDER marker, 0..1, and the DUE stat. When the fee reaches <see cref="FeeSalt"/> this poll opens
    ///     the order (due = today + <see cref="DueDays"/>, absolute: JSON cannot compute it), then clears the
    ///     logs and the fee. While an order is open every drag is hidden, so one chore is open at a time.
    /// The fee is the dragged Salt cards themselves (pocket currency cannot be read or spent from an action,
    /// memory reference_no_player_inventory_access).
    ///
    /// Delivery follows CommissionPatch: SpawnService.Spawn places on the CURRENT board only, so the Firewood
    /// is handed back when the player stands in the Miller's cottage on or after the due day. The order marker
    /// and due stat are cleared BEFORE any spawn so a delivered chore never delivers twice.
    ///
    /// Winter: a chore is NOT a commission for the N2 closure (CMC-WinterScarcity gates the three commission
    /// order actions only). It mints nothing, it only moves the player's own logs through a day of someone
    /// else's labour, so it stays open in winter. Registration runs once per process from Plugin.Awake under an
    /// _initialized guard (CLAUDE.md "Per-Run Event Registration", pattern b).
    /// </summary>
    internal static class HirelingChorePatch
    {
        // Vanilla recipe (TreeLogPieceItem.json, interaction "Cut into Firewood"): 1 Log Piece -> 3 Firewood.
        internal const string LogPieceUid = "53e411d8fd0939648a3b42b177392b91";
        internal const string FirewoodUid = "758be01db25eb4649a288d64b7600b9b";
        internal const int FirewoodPerLog = 3;

        // Tunable placeholders. 4 logs at about 4 DTP each is ~16 DTP of the player's time, and the odd-jobs
        // board pays 1 Salt per 8 DTP, so the chore costs 2 Salt.
        internal const int LogsPerChore = 4;
        internal const int FeeSalt = 2;
        internal const int DueDays = 1;

        internal const string ActionKeyPrefix = "CMC_Miller_Chore_GiveLogs";
        internal const string OrderStatUid = "cmcStatMillerChore";
        internal const string DueStatUid = "cmcStatMillerChoreDue";
        internal const string LogsStatUid = "cmcStatMillerChoreLogs";
        internal const string FeeStatUid = "cmcStatMillerChoreFee";
        internal const string QualityStatUid = "cmcStatMillerChoreQuality";
        internal const string WoodIdStatUid = "cmcStatMillerChoreWoodId";
        internal const string DeliveryEnvUid = "cmcMillerCottageInterior";
        internal const string ReadyTitleKey = "CMC_Commission_Title";
        internal const string ReadyLineKey = "CMC_Chore_Ready_Firewood";
        private const float PollSeconds = 3f;

        private static bool _initialized;
        private static readonly HashSet<string> _warned = new(StringComparer.Ordinal);

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            ActionRouter.Register(new ActionHandler
            {
                Name = "HirelingChoreLogGate",
                CardPredicate = ctx => !string.IsNullOrEmpty(ctx.ActionKey)
                    && ctx.ActionKey.StartsWith(ActionKeyPrefix, StringComparison.Ordinal),
                Timing = ActionTiming.Cancel,
                Before = RecordLog,
            });
            TickEvents.Interval(PollSeconds, Poll, "HirelingChore");
            Plugin.Logger.LogDebug("[HirelingChorePatch] initialized.");
        }

        /// <summary>Cancel gate: notes the dragged Log Piece's Quality and wood ID for the delivery. Returns
        /// true (refuses the drag, the log stays with the player) only when a stat cannot be written.</summary>
        private static bool RecordLog(ActionContext ctx)
        {
            float logs = HiddenStat.Get(LogsStatUid);
            if (logs < 0f)
            {
                WarnOnce("logs-read", $"[HirelingChorePatch] {LogsStatUid} is not readable - the Miller takes no logs until it is.");
                return true;
            }
            float quality = CardUtil.GetDurability(ctx.GivenCard, "SpecialDurability2");
            float woodId = CardUtil.GetDurability(ctx.GivenCard, "SpecialDurability4");
            if (float.IsNaN(quality) || quality < 0f) quality = 0f;
            if (float.IsNaN(woodId) || woodId < 0f) woodId = 0f;

            float stored = HiddenStat.Get(QualityStatUid);
            float newQuality = logs < 0.5f || stored < 0f ? quality : Math.Min(stored, quality);
            if (!HiddenStat.Set(QualityStatUid, newQuality))
            {
                WarnOnce("quality-write", $"[HirelingChorePatch] {QualityStatUid} could not be written - the log was not taken.");
                return true;
            }
            if (logs < 0.5f && !HiddenStat.Set(WoodIdStatUid, woodId))
            {
                WarnOnce("woodid-write", $"[HirelingChorePatch] {WoodIdStatUid} could not be written - the log was not taken.");
                return true;
            }
            Plugin.Logger.LogDebug($"[HirelingChorePatch] log {logs + 1f:0}/{LogsPerChore}: quality {quality:0}, wood ID {woodId:0}; lowest quality now {newQuality:0}.");
            return false;
        }

        private static void Poll()
        {
            try
            {
                string playerEnv = GameQuery.CurrentEnvironmentUniqueId;
                if (string.IsNullOrEmpty(playerEnv)) return; // main menu or mid-travel

                float order = HiddenStat.Get(OrderStatUid);
                if (order < 0f)
                {
                    WarnOnce("order-read", $"[HirelingChorePatch] {OrderStatUid} is not readable - the Miller cannot do a chore.");
                    return;
                }
                float fee = HiddenStat.Get(FeeStatUid);
                float logs = HiddenStat.Get(LogsStatUid);
                if (fee < 0f || logs < 0f)
                {
                    WarnOnce("inputs-read", $"[HirelingChorePatch] {FeeStatUid} or {LogsStatUid} is not readable - the Miller cannot do a chore.");
                    return;
                }

                if (order < 0.5f)
                {
                    if (fee >= FeeSalt - 0.5f && logs >= LogsPerChore - 0.5f) Open();
                    return;
                }

                // An order is open. A write that failed after the order was set leaves the inputs behind: clear them.
                if (fee > 0.5f || logs > 0.5f)
                {
                    HiddenStat.Set(FeeStatUid, 0f);
                    HiddenStat.Set(LogsStatUid, 0f);
                }

                float due = HiddenStat.Get(DueStatUid);
                if (due < 1f) return; // not armed yet (or unreadable)
                if (GameQuery.CurrentDay < (int)due) return; // the Miller still has the logs
                if (!string.Equals(playerEnv, DeliveryEnvUid, StringComparison.OrdinalIgnoreCase)) return; // holds until collected
                Deliver();
            }
            catch (Exception ex)
            {
                WarnOnce("poll-exception", $"[HirelingChorePatch] Poll failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        /// <summary>Logs and Salt are in: stamp the due day, mark the order open, then clear the inputs.</summary>
        private static void Open()
        {
            float day = GameQuery.CurrentDay + DueDays;
            float max = HiddenStat.GetMax(DueStatUid);
            if (max > 0f) day = Math.Min(day, max);
            if (!HiddenStat.Set(DueStatUid, day))
            {
                WarnOnce("due-write", $"[HirelingChorePatch] {DueStatUid} could not be written - the chore is not started.");
                return;
            }
            if (!HiddenStat.Set(OrderStatUid, 1f))
            {
                HiddenStat.Set(DueStatUid, 0f);
                WarnOnce("order-write", $"[HirelingChorePatch] {OrderStatUid} could not be written - the chore is not started.");
                return;
            }
            if (!HiddenStat.Set(FeeStatUid, 0f) || !HiddenStat.Set(LogsStatUid, 0f))
                WarnOnce("inputs-clear", $"[HirelingChorePatch] {FeeStatUid} or {LogsStatUid} could not be cleared; the next poll clears them.");
            Plugin.Logger.LogInfo($"[HirelingChorePatch] The Miller took {LogsPerChore} logs and {FeeSalt} Salt on day {GameQuery.CurrentDay}; the Firewood is ready on day {day:0}.");
        }

        private static void Deliver()
        {
            float quality = HiddenStat.Get(QualityStatUid);
            float woodId = HiddenStat.Get(WoodIdStatUid);
            if (quality < 0f || woodId < 0f)
            {
                WarnOnce("stamp-read", $"[HirelingChorePatch] {QualityStatUid} or {WoodIdStatUid} is not readable - the Firewood is not handed back yet.");
                return;
            }

            // Clear BEFORE spawning: a delivered chore can never deliver twice; due first, then the marker.
            if (!HiddenStat.Set(DueStatUid, 0f))
            {
                WarnOnce("due-clear", $"[HirelingChorePatch] {DueStatUid} could not be cleared - the Firewood is not handed back, so it cannot be handed back twice.");
                return;
            }
            if (!HiddenStat.Set(OrderStatUid, 0f))
            {
                WarnOnce("order-clear", $"[HirelingChorePatch] {OrderStatUid} could not be cleared - the Firewood is not handed back, so it cannot be handed back twice.");
                return;
            }

            var stats = new Dictionary<string, float>
            {
                { "SpecialDurability2", quality },
                { "SpecialDurability4", woodId },
            };
            int count = LogsPerChore * FirewoodPerLog;
            int spawned = 0;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    // SpawnService.Spawn returns null on success (GiveCard is void this game version).
                    SpawnService.Spawn(FirewoodUid, stats);
                    spawned++;
                }
            }
            catch (Exception ex)
            {
                if (spawned == 0)
                {
                    // Nothing handed back yet: put the chore back as due today so the next poll in this room tries again.
                    HiddenStat.Set(OrderStatUid, 1f);
                    HiddenStat.Set(DueStatUid, Math.Max(1f, GameQuery.CurrentDay));
                    WarnOnce("spawn", $"[HirelingChorePatch] Handing back the Firewood failed before any card, so the chore is back open: {ex.InnerException?.ToString() ?? ex.ToString()}");
                    return;
                }
                // Some cards already landed: re-opening would hand back the whole batch again (minting), so the chore stays closed.
                Plugin.Logger.LogError($"[HirelingChorePatch] Handing back the Firewood failed after {spawned} of {count} cards; the chore is closed so it cannot hand back twice: {ex.InnerException?.ToString() ?? ex.ToString()}");
                return;
            }
            Plugin.Logger.LogInfo($"[HirelingChorePatch] Handed back the Miller's Firewood into {DeliveryEnvUid}: {spawned} cards at quality {quality:0}, wood ID {woodId:0}.");
            ShowMessage();
        }

        private static void ShowMessage()
        {
            string title = RoadMeetingPatch.LocalizedText(ReadyTitleKey);
            string line = RoadMeetingPatch.LocalizedText(ReadyLineKey);
            if (string.IsNullOrWhiteSpace(line))
            {
                WarnOnce("line", $"[HirelingChorePatch] No localization row for '{ReadyLineKey}', so that message shows no line.");
                return;
            }
            var gmType = CardUtil.FindGameType("GraphicsManager");
            var instance = gmType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)?.GetValue(null);
            var popup = instance == null ? null : Reflect.GetMember(instance, "MessagePopup");
            var setup = popup?.GetType().GetMethod("Setup", BindingFlags.Instance | BindingFlags.Public, null,
                new[] { typeof(string), typeof(string), typeof(UnityEngine.Sprite) }, null);
            if (popup == null || setup == null)
            {
                WarnOnce("popup-null", "[HirelingChorePatch] GraphicsManager.Instance.MessagePopup.Setup is unavailable - the chore message shows no line.");
                return;
            }
            setup.Invoke(popup, new object[] { string.IsNullOrWhiteSpace(title) ? "" : title.Trim(), line.Trim(), null });
        }

        private static void WarnOnce(string cause, string message)
        {
            if (_warned.Add(cause)) Plugin.Logger.LogWarning(message);
        }
    }
}
