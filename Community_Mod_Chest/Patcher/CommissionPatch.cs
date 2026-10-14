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
    /// card worth less than <see cref="FeeValue"/> before it is consumed. Pocket currency cannot be read
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
        // TUNABLE placeholders (plan open question 5; the closeout may retune them).
        /// <summary>The least a dragged card must be worth: one Salt (CurrencyValue's SaltValue, 15).</summary>
        internal const float FeeValue = 15f;
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
                ReadyLineKey = "CMC_Commission_Ready_Miller",
                Goods = new[] { new Good { Uid = "7900a60c205568640a887f30cc6ff8f9", Quantity = 10, Stats = FlourStats } }, // Wheat Flour
            },
            new Spec
            {
                Name = "the Weaver", ActionKeyPrefix = "CMC_Weaver_Commission",
                OrderStatUid = "cmcStatWeaverOrder", DueStatUid = "cmcStatWeaverOrderDue",
                DeliveryEnvUid = "cmcWeaverCottageInterior", DueDays = WeaverDueDays,
                ReadyLineKey = "CMC_Commission_Ready_Weaver",
                Goods = new[] { new Good { Uid = "3e94716876eae304787efa2ae0b43711", Quantity = 1, Stats = ClothStats } }, // Large Cloth
            },
            new Spec
            {
                Name = "the Inn Keeper's hunter", ActionKeyPrefix = "CMC_InnKeeper_Commission",
                OrderStatUid = "cmcStatInnHunterOrder", DueStatUid = "cmcStatInnHunterOrderDue",
                DeliveryEnvUid = "cmcInnInterior", DueDays = InnHunterDueDays,
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

            float value = CurrencyValue.ValueOf(ctx.GivenCard);
            if (value >= FeeValue) return false;

            Plugin.Logger.LogDebug($"[CommissionPatch] {spec.Name}: the dragged card is worth {value:0}, below the fee of {FeeValue:0}; refused.");
            ShowMessage(TooLittleKey, null);
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

        private static void WarnOnce(string cause, string message)
        {
            if (_warned.Add(cause)) Plugin.Logger.LogWarning(message);
        }
    }
}
