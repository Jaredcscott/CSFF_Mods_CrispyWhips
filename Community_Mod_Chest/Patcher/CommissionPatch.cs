using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Commissions (CMC round 2, plan feature C7): pay the Miller, the Weaver or the Inn Keeper and the
    /// goods are waiting for you a day or two later.
    ///
    /// The order is ONE drag per resident (a Salt, a metal Nugget or a Duros Coin dragged onto them, the
    /// NPCAgent JSON's "Commission" action). The JSON writes two hidden GameStats and nothing else:
    ///   - the ORDER marker, clamped 0..1 (pending = 1, delivered = 0: the root CLAUDE.md "NPC Errand
    ///     Stat Design" pattern, never an unclamped counter), which also hides the drag while an order is
    ///     open, so each resident holds ONE open order;
    ///   - the DUE stat, which the drag raises by the sentinel 0.5. JSON cannot compute "today + N", so
    ///     the poll below arms the sentinel to an absolute day (the ApothecarySchedulePatch potion-brew
    ///     pattern).
    /// The fee is the dragged card, priced by <see cref="CurrencyValue.ValueOf"/>: a Cancel gate refuses a
    /// card worth less than the spec's fee before it is consumed. Vanilla balance pass (1.69.17, plan Part II
    /// rows CMC-V-B3 and CMC-V-B10): each fee is 60% of its goods' TradingValues.json value (Miller 10 Wheat
    /// Flour 150, Inn hunter Doe Carcass 150, Weaver Large Cloth 360); a resident takes a new order only
    /// <see cref="Spec.CooldownDays"/> after the last one was collected (a per-resident day stamp, day plus one,
    /// written in Deliver); and no resident takes one in the village's first week. The Weaver's 360 is more
    /// than any single card is worth, so her fee is a DEPOSIT: each card dragged onto her adds its value to the
    /// clamped hidden stat <see cref="WeaverDepositStatUid"/> (0..360, the root CLAUDE.md "NPC Errand Stat
    /// Design" rule: never an unclamped counter), the card is taken, and the order opens on the drag that
    /// reaches 360 (the deposit resets to 0 then; value past 360 is the price of rounding). Pocket currency cannot be read
    /// or spent from an action (memory reference_no_player_inventory_access), which is why the fee is
    /// the card itself and not an amount taken from the player.
    ///
    /// Delivery: SpawnService.Spawn places a card on the CURRENT board only (memory
    /// reference_spawn_targets_current_board), so a due order is delivered only while the player stands
    /// in the target interior (the resident's cottage, or the Inn) on or after the due day, and an order
    /// the player never collects simply holds until they walk in. The due stat is cleared FIRST and the
    /// marker second, both before any spawn, so a failed write leaves the order open (retry) and a
    /// delivered order can never deliver twice.
    ///
    /// The cottages' own grinding and weaving blueprints are untouched and stay player-usable: a
    /// commission sells time, never access. Registration runs once per process from Plugin.Awake under an
    /// _initialized guard (CLAUDE.md "Per-Run Event Registration", pattern b).
    /// </summary>
    internal static class CommissionPatch
    {
        // Vanilla balance pass (1.69.17): fee = 60% of the goods' TradingValues.json value.
        internal const float MillerFee = 150f;
        internal const float WeaverFee = 360f;
        internal const float InnHunterFee = 150f;
        internal const int MillerCooldownDays = 3;
        internal const int WeaverCooldownDays = 4;
        internal const int InnHunterCooldownDays = 7;
        internal const string WeaverDepositStatUid = "cmcStatWeaverCommissionDeposit";
        internal const string NotYetKey = "CMC_Commission_NotYet";
        internal const string FirstWeekKey = "CMC_Commission_FirstWeek";
        internal const string DepositKey = "CMC_Commission_Deposit";
        internal const int MillerDueDays = 1;
        internal const int WeaverDueDays = 2;
        internal const int InnHunterDueDays = 2;

        internal const string TitleKey = "CMC_Commission_Title";
        internal const string TooLittleKey = "CMC_Commission_TooLittle";
        private const float PollSeconds = 3f;

        // Vanilla goods, with the stats that make each usable (root CLAUDE.md "Perks" starting-item
        // stats: a bare spawn is quality 0). Wheat Flour 48 is the Quality a Wheat Grain card starts at,
        // which is what the Miller's own grinding turns it into; the Large Cloth's 50 is the Quality the
        // Weaver's own Large Cloth blueprint sets (Special2Value 50, SetValue); a Doe Carcass's 8 is
        // vanilla's own default, written out so the order never depends on it.
        private static readonly Dictionary<string, float> FlourStats = new() { { "SpecialDurability2", 48f } };
        private static readonly Dictionary<string, float> ClothStats = new() { { "SpecialDurability2", 50f } };
        private static readonly Dictionary<string, float> CarcassStats = new() { { "SpecialDurability2", 8f } };

        private sealed class Good
        {
            public string Uid;
            public int Quantity;
            public Dictionary<string, float> Stats;
        }

        private sealed class Spec
        {
            public string Name;
            public string ActionKeyPrefix;
            public string OrderStatUid;
            public string DueStatUid;
            public string DeliveryEnvUid;
            public int DueDays;
            public float Fee;
            public int CooldownDays;
            public string ReadyStatUid;
            public string DepositStatUid;
            public string ReadyLineKey;
            public Good[] Goods;
        }

        private static readonly Spec[] Specs =
        {
            new Spec
            {
                Name = "the Miller", ActionKeyPrefix = "CMC_Miller_Commission",
                OrderStatUid = "cmcStatMillerOrder", DueStatUid = "cmcStatMillerOrderDue",
                DeliveryEnvUid = "cmcMillerCottageInterior", DueDays = MillerDueDays,
                Fee = MillerFee, CooldownDays = MillerCooldownDays, ReadyStatUid = "cmcStatCommissionReadyMiller",
                ReadyLineKey = "CMC_Commission_Ready_Miller",
                Goods = new[] { new Good { Uid = "7900a60c205568640a887f30cc6ff8f9", Quantity = 10, Stats = FlourStats } }, // Wheat Flour
            },
            new Spec
            {
                Name = "the Weaver", ActionKeyPrefix = "CMC_Weaver_Commission",
                OrderStatUid = "cmcStatWeaverOrder", DueStatUid = "cmcStatWeaverOrderDue",
                DeliveryEnvUid = "cmcWeaverCottageInterior", DueDays = WeaverDueDays,
                Fee = WeaverFee, CooldownDays = WeaverCooldownDays, ReadyStatUid = "cmcStatCommissionReadyWeaver",
                DepositStatUid = WeaverDepositStatUid,
                ReadyLineKey = "CMC_Commission_Ready_Weaver",
                Goods = new[] { new Good { Uid = "3e94716876eae304787efa2ae0b43711", Quantity = 1, Stats = ClothStats } }, // Large Cloth
            },
            new Spec
            {
                Name = "the Inn Keeper's hunter", ActionKeyPrefix = "CMC_InnKeeper_Commission",
                OrderStatUid = "cmcStatInnHunterOrder", DueStatUid = "cmcStatInnHunterOrderDue",
                DeliveryEnvUid = "cmcInnInterior", DueDays = InnHunterDueDays,
                Fee = InnHunterFee, CooldownDays = InnHunterCooldownDays, ReadyStatUid = "cmcStatCommissionReadyInnKeeper",
                ReadyLineKey = "CMC_Commission_Ready_Inn",
                Goods = new[] { new Good { Uid = "460b7e5a5579f554db61f9e38e59fcf7", Quantity = 1, Stats = CarcassStats } }, // Doe Carcass
            },
        };

        private static bool _initialized;
        private static readonly HashSet<string> _warned = new(StringComparer.Ordinal);

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            ActionRouter.Register(new ActionHandler
            {
                Name = "CommissionFeeGate",
                CardPredicate = ctx => SpecForAction(ctx.ActionKey) != null,
                Timing = ActionTiming.Cancel,
                Before = FeeGate,
            });
            TickEvents.Interval(PollSeconds, Poll, "Commission");
            Plugin.Logger.LogDebug("[CommissionPatch] initialized.");
        }

        private static Spec SpecForAction(string actionKey)
        {
            if (string.IsNullOrEmpty(actionKey)) return null;
            return Specs.FirstOrDefault(s => actionKey.StartsWith(s.ActionKeyPrefix, StringComparison.Ordinal));
        }

        /// <summary>Cancel gate: true refuses the drag before the dragged card is consumed.</summary>
        private static bool FeeGate(ActionContext ctx)
        {
            var spec = SpecForAction(ctx.ActionKey);
            if (spec == null) return false;

            // CMC-V-B10: no commissions in the village's first week (week 1 = the intro week; 0 = before the
            // intro or unreadable, which never blocks).
            var gm = CardUtil.GetGameManagerInstance();
            if (gm != null && VillageClock.WeeksSinceIntro(gm) == 1)
            {
                Plugin.Logger.LogDebug($"[CommissionPatch] {spec.Name}: refused, the village is in its first week.");
                ShowMessage(FirstWeekKey, null);
                return true;
            }

            // Cooldown since the last collected order (stamp = first open day plus one; 0 = never).
            float ready = HiddenStat.Get(spec.ReadyStatUid);
            if (!CooldownOver(ready, GameQuery.CurrentDay))
            {
                Plugin.Logger.LogDebug($"[CommissionPatch] {spec.Name}: refused, next order from day {ready - 1f:0} (today {GameQuery.CurrentDay}).");
                ShowMessage(NotYetKey, null);
                return true;
            }

            float value = CurrencyValue.ValueOf(ctx.GivenCard);
            if (spec.DepositStatUid == null)
            {
                if (value >= spec.Fee) return false;
                Plugin.Logger.LogDebug($"[CommissionPatch] {spec.Name}: the dragged card is worth {value:0}, below the fee of {spec.Fee:0}; refused.");
                ShowMessage(TooLittleKey, null);
                return true;
            }
            return DepositGate(ctx, spec, value);
        }

        /// <summary>True when a resident whose ready stamp reads <paramref name="stamp"/> takes an order on
        /// <paramref name="today"/>. An unreadable stamp (below 0) never blocks; 0 means never ordered.</summary>
        internal static bool CooldownOver(float stamp, int today) => stamp < 1f || today + 1 >= (int)Math.Round(stamp);

        /// <summary>The Weaver's deposit: false lets the drag open the order (the deposit reached the fee and is
        /// reset first); true cancels it, after the card's value is banked and the card taken. A write that fails
        /// refuses the drag with nothing taken.</summary>
        private static bool DepositGate(ActionContext ctx, Spec spec, float value)
        {
            if (value <= 0f) { ShowMessage(TooLittleKey, null); return true; }
            float banked = HiddenStat.Get(spec.DepositStatUid);
            if (banked < 0f)
            {
                WarnOnce("deposit-read", $"[CommissionPatch] {spec.DepositStatUid} is not readable - {spec.Name} takes no deposit until it is.");
                return true;
            }
            if (banked + value >= spec.Fee - 0.01f)
            {
                if (!HiddenStat.Set(spec.DepositStatUid, 0f))
                {
                    WarnOnce("deposit-reset", $"[CommissionPatch] {spec.DepositStatUid} could not be reset - the order with {spec.Name} is refused so the deposit cannot be spent twice.");
                    return true;
                }
                Plugin.Logger.LogDebug($"[CommissionPatch] {spec.Name}: deposit {banked:0} + {value:0} reaches the fee of {spec.Fee:0}; order opens.");
                return false;
            }
            float after = Math.Min(spec.Fee, banked + value);
            if (!HiddenStat.Set(spec.DepositStatUid, after))
            {
                WarnOnce("deposit-write", $"[CommissionPatch] {spec.DepositStatUid} could not be written - the card was not taken.");
                return true;
            }
            if (!CardUtil.TryRemoveCard(ctx.GivenCard))
            {
                HiddenStat.Set(spec.DepositStatUid, banked); // roll back: the card stays with the player
                WarnOnce("deposit-remove", $"[CommissionPatch] The card dragged onto {spec.Name} could not be taken, so nothing was banked.");
                return true;
            }
            string line = RoadMeetingPatch.LocalizedText(DepositKey);
            if (!string.IsNullOrWhiteSpace(line))
            {
                try { line = string.Format(line, after.ToString("0"), spec.Fee.ToString("0")); }
                catch (FormatException) { WarnOnce("deposit-format", $"[CommissionPatch] The '{DepositKey}' row is not a valid format string."); }
                ShowLine("", line);
            }
            Plugin.Logger.LogDebug($"[CommissionPatch] {spec.Name}: banked {value:0}, deposit now {after:0}/{spec.Fee:0}.");
            return true;
        }

        private static void Poll()
        {
            try
            {
                string playerEnv = GameQuery.CurrentEnvironmentUniqueId;
                if (string.IsNullOrEmpty(playerEnv)) return; // main menu or mid-travel: nothing to read

                foreach (var spec in Specs)
                {
                    float order = HiddenStat.Get(spec.OrderStatUid);
                    if (order < 0f)
                    {
                        WarnOnce("order-read-" + spec.OrderStatUid, $"[CommissionPatch] {spec.OrderStatUid} is not readable - {spec.Name} cannot deliver a commission.");
                        continue;
                    }
                    if (order < 0.5f) continue; // no open order

                    float due = HiddenStat.Get(spec.DueStatUid);
                    if (due < 0f)
                    {
                        WarnOnce("due-read-" + spec.DueStatUid, $"[CommissionPatch] {spec.DueStatUid} is not readable - {spec.Name} cannot deliver a commission.");
                        continue;
                    }
                    if (due < 1f)
                    {
                        // The drag left the 0.5 sentinel: turn it into an absolute due day.
                        Arm(spec);
                        continue;
                    }
                    if (GameQuery.CurrentDay < (int)due) continue; // not ready yet
                    if (!string.Equals(playerEnv, spec.DeliveryEnvUid, StringComparison.OrdinalIgnoreCase)) continue; // holds until collected

                    Deliver(spec);
                }
            }
            catch (Exception ex)
            {
                WarnOnce("poll-exception", $"[CommissionPatch] Poll failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void Arm(Spec spec)
        {
            float day = GameQuery.CurrentDay + spec.DueDays;
            float max = HiddenStat.GetMax(spec.DueStatUid);
            if (max > 0f) day = Math.Min(day, max);
            if (!HiddenStat.Set(spec.DueStatUid, day))
            {
                WarnOnce("due-write-" + spec.DueStatUid, $"[CommissionPatch] {spec.DueStatUid} could not be written - the order with {spec.Name} cannot be given a due day.");
                return;
            }
            Plugin.Logger.LogInfo($"[CommissionPatch] Order with {spec.Name} taken on day {GameQuery.CurrentDay}; it will be ready on day {day:0}.");
        }

        private static void Deliver(Spec spec)
        {
            // Clear BEFORE spawning: a failed write leaves the order open for the next poll, and a
            // delivered order can never deliver twice. The due stat goes first so a failure on the marker
            // only re-arms the delay rather than leaving a stale due day behind.
            if (!HiddenStat.Set(spec.DueStatUid, 0f))
            {
                WarnOnce("due-clear-" + spec.DueStatUid, $"[CommissionPatch] {spec.DueStatUid} could not be cleared - the order with {spec.Name} is not delivered, so it cannot deliver twice.");
                return;
            }
            if (!HiddenStat.Set(spec.OrderStatUid, 0f))
            {
                WarnOnce("order-clear-" + spec.OrderStatUid, $"[CommissionPatch] {spec.OrderStatUid} could not be cleared - the order with {spec.Name} is not delivered, so it cannot deliver twice.");
                return;
            }

            int spawned = 0;
            try
            {
                foreach (var good in spec.Goods)
                {
                    for (int i = 0; i < good.Quantity; i++)
                    {
                        // SpawnService.Spawn returns null on success (GiveCard is void this game version).
                        SpawnService.Spawn(good.Uid, good.Stats);
                        spawned++;
                    }
                }
            }
            catch (Exception ex)
            {
                // Put the order back as due today so the next poll in this room tries again.
                HiddenStat.Set(spec.OrderStatUid, 1f);
                HiddenStat.Set(spec.DueStatUid, Math.Max(1f, GameQuery.CurrentDay));
                WarnOnce("spawn-" + spec.OrderStatUid, $"[CommissionPatch] Delivering the order from {spec.Name} failed after {spawned} cards, so the order is back open: {ex.InnerException?.ToString() ?? ex.ToString()}");
                return;
            }

            // Cooldown from collection: the stamp is the first day a new order is taken, plus one.
            if (!HiddenStat.Set(spec.ReadyStatUid, GameQuery.CurrentDay + spec.CooldownDays + 1))
                WarnOnce("ready-write-" + spec.ReadyStatUid, $"[CommissionPatch] {spec.ReadyStatUid} could not be written - {spec.Name} takes the next order with no wait.");
            Plugin.Logger.LogInfo($"[CommissionPatch] Delivered the order from {spec.Name} into {spec.DeliveryEnvUid}: {spawned} cards.");
            ShowMessage(TitleKey, spec.ReadyLineKey);
        }

        /// <summary>One line in the game's own message popup. A null line key shows the title key's own
        /// row as the line (used by the refusal, which has only one text).</summary>
        private static void ShowMessage(string titleOrLineKey, string lineKey)
        {
            string title = lineKey == null ? "" : RoadMeetingPatch.LocalizedText(titleOrLineKey);
            string line = RoadMeetingPatch.LocalizedText(lineKey ?? titleOrLineKey);
            if (string.IsNullOrWhiteSpace(line))
            {
                WarnOnce("line-" + (lineKey ?? titleOrLineKey), $"[CommissionPatch] No localization row for '{lineKey ?? titleOrLineKey}', so that message shows no line.");
                return;
            }
            // Reflection, not compile-time: SimpleMessagePopup is not in every reference copy of Assembly-CSharp.
            var gmType = CardUtil.FindGameType("GraphicsManager");
            var instance = gmType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)?.GetValue(null);
            var popup = instance == null ? null : Reflect.GetMember(instance, "MessagePopup");
            var setup = popup?.GetType().GetMethod("Setup", BindingFlags.Instance | BindingFlags.Public, null,
                new[] { typeof(string), typeof(string), typeof(UnityEngine.Sprite) }, null);
            if (popup == null || setup == null)
            {
                WarnOnce("popup-null", "[CommissionPatch] GraphicsManager.Instance.MessagePopup.Setup is unavailable - commission messages show no line.");
                return;
            }
            setup.Invoke(popup, new object[] { string.IsNullOrWhiteSpace(title) ? "" : title.Trim(), line.Trim(), null });
        }

        /// <summary>One already-localized line in the game's message popup.</summary>
        private static void ShowLine(string title, string line)
        {
            var gmType = CardUtil.FindGameType("GraphicsManager");
            var instance = gmType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)?.GetValue(null);
            var popup = instance == null ? null : Reflect.GetMember(instance, "MessagePopup");
            var setup = popup?.GetType().GetMethod("Setup", BindingFlags.Instance | BindingFlags.Public, null,
                new[] { typeof(string), typeof(string), typeof(UnityEngine.Sprite) }, null);
            if (popup == null || setup == null) { WarnOnce("popup-null", "[CommissionPatch] GraphicsManager.Instance.MessagePopup.Setup is unavailable - commission messages show no line."); return; }
            setup.Invoke(popup, new object[] { title ?? "", line.Trim(), null });
        }

        private static void WarnOnce(string cause, string message)
        {
            if (_warned.Add(cause)) Plugin.Logger.LogWarning(message);
        }
    }
}
