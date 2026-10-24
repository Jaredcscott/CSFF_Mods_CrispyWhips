using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using UnityEngine;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Market Stall mechanics:
    ///   - Village gate: "Set Up Stall" DA is canceled outside cmcEnvVillage
    ///   - Selling: one random item consumed from inventory per day; value = ObjectWeight/10 times
    ///     VillagePricing.Multiplier(PriceSide.Earn) (the Village Reputation bonus curve, halved while
    ///     Banished) added to SD1
    ///   - "Collect as Copper": drains SD1, spawns copper MetalNuggets (250 revenue each)
    ///   - "Collect as Salt":   drains SD1, spawns Salt       (15 revenue each)
    ///   - Robbery: 5% chance every 7 days — one item stolen from inventory
    ///   - D6 report: the stolen card is remembered as a 24-bit hash of its UniqueID in a hidden GameStat
    ///     (a GameStat holds a number, not text); any guard's Talk line moves the marker to "reported", a
    ///     few days later a 50% roll decides, and a recovered item is respawned on the Village board
    ///
    /// The 7-day count lives in the hidden cmcStatStallRobberyDays GameStat, not in process memory,
    /// so quitting and relaunching does not restart it.
    /// </summary>
    internal static class MarketStallPatch
    {
        private const string KitUid        = "cmcmarketstallkit";
        private const string StallUid      = "cmcmarketstall";
        /// <summary>"Set Up Awning" transforms the plain stall into this card (TransferInventory +
        /// TransferSpecial1) and "Take Down Awning" transforms it back, so both UIDs are the same
        /// stall and every handler below must answer for both.</summary>
        internal const string DressedStallUid = "cmcmarketstalldressed";
        private const string VillageEnvUid = "cmcEnvVillage";

        internal static bool IsStallUid(string uid) =>
            string.Equals(uid, StallUid, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uid, DressedStallUid, StringComparison.OrdinalIgnoreCase);

        private const string NuggetGuid = "4b0f4937a5ecb90499428c8c10288afc";
        private const string SaltGuid   = "f91b5676fc26c0e48a4ee6fd9dfc2ffa";

        // Vanilla balance pass (1.69.17): priced at vanilla trade value (Metal Nugget 250, Salt 15).
        private const float CostPerNugget      = 250f;
        private const float CostPerSalt        = 15f;
        private const float SD1MaxValue        = 480f;

        private const float RobberyChance       = 0.05f;
        private const int   RobberyIntervalDays = 7;

        /// <summary>The lowest the robbery chance can fall to once Luck (B4) takes its share off: the same 1%
        /// floor the chest search's detection roll keeps. The base is only 5%, so subtracting Luck's full 5
        /// points from it with no floor would make a stall immune to robbery.</summary>
        private const float RobberyFloor        = 0.01f;

        /// <summary>Hidden GameStat (GameStat/CMC_StallRobberyDays.json, 0..7, rate 0) holding the days
        /// a stall has stood since the last robbery roll. Its max must equal
        /// <see cref="RobberyIntervalDays"/>; CMC-Round2.Tests.ps1 asserts that.</summary>
        private const string RobberyDaysStatUid = "cmcStatStallRobberyDays";

        // ── D6: reporting a robbery to the Watch ─────────────────────────────────

        /// <summary>Clamped 0..4 marker (GameStat/CMC_StallTheftState.json): 0 none, 1 a theft waits to be
        /// reported, 2 reported (a guard's Talk line wrote it), 3 the search found the item and it is owed on the
        /// next Village visit, 4 the search found nothing and a guard will say so. Never a counter.</summary>
        internal const string TheftStateUid = "cmcStatStallTheftState";

        /// <summary>24-bit hash of the stolen card's UniqueID (GameStat/CMC_StallTheftHash.json, 0..16777215): a
        /// GameStat holds a number, not text, and a float holds every integer below 2^24 exactly.</summary>
        internal const string TheftHashUid = "cmcStatStallTheftHash";

        /// <summary>The in-game day the search is decided (GameStat/CMC_StallRecoveryDue.json, 0..99999, 0 = unset).</summary>
        internal const string RecoveryDueUid = "cmcStatStallRecoveryDue";

        /// <summary>TUNABLE PLACEHOLDER: the chance a reported item comes back.</summary>
        internal const float RecoveryChance = 0.5f;

        /// <summary>TUNABLE PLACEHOLDER: days between the report being taken and the search being decided.</summary>
        internal const int RecoveryDelayDays = 3;

        /// <summary>Largest value the due-day stat can hold; the due day is clamped to it.</summary>
        internal const int RecoveryDueMax = 99999;

        /// <summary>FNV-1a over the UTF-8 bytes of a UniqueID, folded to 24 bits and never 0 (0 means "none").
        /// Pure, so the Pester suite mirrors it.</summary>
        internal static int IdentityHash(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return 0;
            uint h = 2166136261u;
            foreach (byte b in System.Text.Encoding.UTF8.GetBytes(uid))
            {
                h ^= b;
                h *= 16777619u;
            }
            int folded = (int)((h ^ (h >> 24)) & 0xFFFFFFu);
            return folded == 0 ? 1 : folded;
        }

        private static bool _initialized;
        private static bool _setActiveFailureLogged;

        private static MethodInfo _getFromIdMethod;
        private static object _robberyDaysStat;
        private static readonly HashSet<string> _warned = new(StringComparer.Ordinal);

        // Market Stall blueprint: its gate and its chest slot name the vanilla Storage Chest, so the stall
        // stands without Advanced Copper Tools. With ACT installed, its Copper Chest (advanced_copper_tools_copper_pantry)
        // also fits the slot, through the framework's allow-list form so no other blueprint that needs a
        // Storage Chest (vanilla ones included) starts accepting it. Without ACT the framework call finds no
        // alternate and returns 0 with one Debug line. The research gate stays the Storage Chest alone.
        private const string MarketStallBlueprintUid = "bpcmcmarketstall";
        private const string StorageChestUid = "2daa7f9ceb2e7534a943e22c3cc6b027";
        private const string ActCopperChestUid = "advanced_copper_tools_copper_pantry";

        private static void RegisterChestAlternate()
        {
            try
            {
                var allData = GetAllData();
                if (allData == null)
                {
                    WarnOnce("ChestAlt-AllData-null", "[MarketStallPatch] GameLoad.DataBase.AllData is not reachable, so the Market Stall blueprint does not accept a Copper Chest this load.");
                    return;
                }
                BlueprintAlternates.AddAlternateIngredientIn(allData, StorageChestUid, ActCopperChestUid,
                    new[] { MarketStallBlueprintUid }, "CMC Market Stall: Storage Chest / ACT Copper Chest");
            }
            catch (Exception ex)
            {
                WarnOnce("ChestAlt-threw", $"[MarketStallPatch] registering the Copper Chest alternate threw: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static IEnumerable GetAllData()
        {
            const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.FlattenHierarchy;
            var gameLoadType = CardUtil.FindGameType("GameLoad");
            if (gameLoadType == null) return null;
            var gameLoad = gameLoadType.GetProperty("Instance", Any)?.GetValue(null)
                ?? gameLoadType.GetField("Instance", Any)?.GetValue(null)
                ?? UnityEngine.Object.FindObjectOfType(gameLoadType);
            if (gameLoad == null) return null;
            var dataBase = gameLoadType.GetField("DataBase", Any)?.GetValue(gameLoad);
            if (dataBase == null) return null;
            return dataBase.GetType().GetField("AllData", Any)?.GetValue(dataBase) as IEnumerable;
        }

        // ── Registration ─────────────────────────────────────────────────────────

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            // The blueprint's chest slot takes the vanilla Storage Chest; ACT's Copper Chest is an accepted
            // alternate (Fleet plan feature 1). Registered on every GameDataReady (idempotent, and a no-op
            // when ACT is not installed).
            FrameworkEvents.GameDataReady += RegisterChestAlternate;

            if (CardUtil.FindGameType("InGameCardBase") == null)
            {
                Plugin.Logger.LogWarning("[MarketStallPatch] InGameCardBase type not found — market stall mechanics inactive.");
                return;
            }

            // Village gate: cancel "Set Up Stall" outside the village (not gift actions)
            ActionRouter.Register(new ActionHandler
            {
                Name             = "MarketStallVillageGate",
                CardPredicate    = ctx => ctx.CardUid == KitUid,
                ActionNamePrefix = "Set Up Stall",
                Timing           = ActionTiming.Cancel,
                Before           = VillageGateBefore,
            });

            // Payment: collect as copper nuggets
            ActionRouter.Register(new ActionHandler
            {
                Name             = "MarketStallCollectCopper",
                CardPredicate    = ctx => IsStallUid(ctx.CardUid),
                ActionNamePrefix = "Collect as Copper",
                Timing           = ActionTiming.AfterWrapped,
                After            = CollectCopperAfter,
            });

            // Payment: collect as salt
            ActionRouter.Register(new ActionHandler
            {
                Name             = "MarketStallCollectSalt",
                CardPredicate    = ctx => IsStallUid(ctx.CardUid),
                ActionNamePrefix = "Collect as Salt",
                Timing           = ActionTiming.AfterWrapped,
                After            = CollectSaltAfter,
            });

            // Payment: collect as Duros coins (J5). The button is hidden by cmcStatDurosPresent unless
            // DurosCoinage's coin resolves (QuestChainSchedulePatch.SyncDurosPresence keeps that flag), and this
            // handler refuses on its own when the coin does not resolve. Registered here, once, with the other two.
            ActionRouter.Register(new ActionHandler
            {
                Name             = "MarketStallCollectCoins",
                CardPredicate    = ctx => IsStallUid(ctx.CardUid),
                ActionNamePrefix = "Collect as Coins",
                Timing           = ActionTiming.AfterWrapped,
                After            = CollectCoinsAfter,
            });

            TickEvents.DayRollover += OnDayChanged;
            // D6: the report's due day is decided at a day rollover whether or not a stall still stands, and the
            // recovered item is delivered by a poll that waits for the Village board (the spawn lands on the
            // current board only).
            TickEvents.DayRollover += TheftReportDay;
            TickEvents.DtpTick += TheftDeliveryTick;

            Plugin.Logger.LogDebug("[MarketStallPatch] initialized.");
        }

        // ── Village gate ──────────────────────────────────────────────────────────

        private static bool VillageGateBefore(ActionContext ctx)
        {
            bool inVillage = GameQuery.CurrentEnvironmentUniqueId == VillageEnvUid;
            if (!inVillage)
                Plugin.Logger.LogDebug("[MarketStallPatch] Set Up Stall blocked — not in village.");
            return !inVillage;
        }

        // ── Collection handlers ───────────────────────────────────────────────────

        private static void CollectCopperAfter(ActionContext ctx)
        {
            float revenue = CardUtil.GetDurability(ctx.Card, "SpecialDurability1");
            if (float.IsNaN(revenue) || revenue < CostPerNugget) return;
            int count = (int)(revenue / CostPerNugget);
            // Drain first and check it: a revenue that did not go down must not pay out (G5).
            if (!CardUtil.SetDurability(ctx.Card, "SpecialDurability1", revenue - count * CostPerNugget))
            {
                WarnOnce("collect-drain-failed", "[MarketStallPatch] Sales Revenue (SpecialDurability1) did not take the write - nothing is paid out.");
                return;
            }
            for (int i = 0; i < count; i++) SpawnService.Spawn(NuggetGuid, CurrencyValue.CopperNuggetStats);
            Plugin.Logger.LogDebug($"[MarketStallPatch] Collected {count} copper nugget(s) (revenue was {revenue:0}).");
        }

        private static void CollectSaltAfter(ActionContext ctx)
        {
            float revenue = CardUtil.GetDurability(ctx.Card, "SpecialDurability1");
            if (float.IsNaN(revenue) || revenue < CostPerSalt) return;
            int count = (int)(revenue / CostPerSalt);
            if (!CardUtil.SetDurability(ctx.Card, "SpecialDurability1", revenue - count * CostPerSalt))
            {
                WarnOnce("collect-drain-failed", "[MarketStallPatch] Sales Revenue (SpecialDurability1) did not take the write - nothing is paid out.");
                return;
            }
            for (int i = 0; i < count; i++) SpawnService.Spawn(SaltGuid);
            Plugin.Logger.LogDebug($"[MarketStallPatch] Collected {count} salt unit(s) (revenue was {revenue:0}).");
        }

        /// <summary>J5: pays the takings in DurosCoinage coins at the coin's own trade value
        /// (<see cref="CurrencyValue.CoinPrice"/>), keeping the remainder in the stall like the other two. Without
        /// DurosCoinage the price is 0 and nothing is drained or spawned (the button is hidden then as well).</summary>
        private static void CollectCoinsAfter(ActionContext ctx)
        {
            float price = CurrencyValue.CoinPrice();
            if (price <= 0f)
            {
                Plugin.Logger.LogDebug("[MarketStallPatch] Collect as Coins ignored: the Duros coin does not resolve.");
                return;
            }
            float revenue = CardUtil.GetDurability(ctx.Card, "SpecialDurability1");
            if (float.IsNaN(revenue) || revenue < price) return;
            int count = (int)(revenue / price);
            if (!CardUtil.SetDurability(ctx.Card, "SpecialDurability1", revenue - count * price))
            {
                WarnOnce("collect-drain-failed", "[MarketStallPatch] Sales Revenue (SpecialDurability1) did not take the write - nothing is paid out.");
                return;
            }
            for (int i = 0; i < count; i++) SpawnService.Spawn(CurrencyValue.CoinUid);
            Plugin.Logger.LogDebug($"[MarketStallPatch] Collected {count} Duros coin(s) at {price:0.##} each (revenue was {revenue:0}).");
        }

        // ── Daily tick ────────────────────────────────────────────────────────────

        private static void OnDayChanged()
        {
            try
            {
                var stalls = CardFinder.FindAll(StallUid);
                stalls.AddRange(CardFinder.FindAll(DressedStallUid));
                if (stalls.Count == 0) return;

                foreach (var stall in stalls)
                    SellNextItem(stall);

                if (AdvanceRobberyClock())
                {
                    float roll = UnityEngine.Random.value;
                    // B4: Luck's current value, read once for this roll, takes up to 5 points off the 5%.
                    float luckAdjustment = LuckBonus.Adjustment("stall");
                    float chance = Math.Max(RobberyFloor, RobberyChance - luckAdjustment);
                    bool robbed = roll < chance;
                    Plugin.Logger.LogDebug($"[MarketStallPatch] Robbery roll after {RobberyIntervalDays} days: rolled {roll:0.000} against {chance:0.000} (base {RobberyChance:0.000}, luck -{luckAdjustment:0.000}), {(robbed ? "robbed" : "safe")}.");
                    if (robbed)
                    {
                        foreach (var stall in stalls)
                            TryRobStall(stall);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[MarketStallPatch] OnDayChanged: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // ── Robbery clock (kept in the save) ──────────────────────────────────────

        /// <summary>
        /// Advances the robbery clock by one day and returns true when the interval is up, so the
        /// caller rolls exactly once. The count is the hidden GameStat <see cref="RobberyDaysStatUid"/>
        /// (direct StatsDict write, the CopperChestPatch.AddGameStatTrust idiom), so it survives a
        /// relaunch. The reset to 0 is written BEFORE the roll is reported due: a stat that cannot be
        /// read or written skips the roll with a warn-once line (one per cause) instead of rolling
        /// every day because the count never reset.
        /// </summary>
        private static bool AdvanceRobberyClock()
        {
            try
            {
                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null)
                {
                    WarnOnce("robbery-gm-null", "[MarketStallPatch] GameManager instance not found - the stall robbery clock cannot advance.");
                    return false;
                }

                var entry = RobberyDaysEntry(gm);
                if (entry == null) return false; // RobberyDaysEntry already warned, once per cause

                if (Reflect.GetMember(entry, "SimpleCurrentValue") is not float stored || float.IsNaN(stored))
                {
                    WarnOnce("robbery-read", $"[MarketStallPatch] {RobberyDaysStatUid} is not readable (InGameStat.SimpleCurrentValue) - the stall robbery clock cannot advance.");
                    return false;
                }

                int days = (int)Math.Round(stored) + 1;
                bool due = days >= RobberyIntervalDays;
                if (!Reflect.SetMember(entry, "CurrentBaseValue", due ? 0f : (float)days))
                {
                    WarnOnce("robbery-write", $"[MarketStallPatch] {RobberyDaysStatUid} write failed (InGameStat.CurrentBaseValue) - the stall robbery clock cannot advance.");
                    return false;
                }

                Plugin.Logger.LogDebug($"[MarketStallPatch] Robbery clock {(due ? RobberyIntervalDays : days)}/{RobberyIntervalDays}.");
                return due;
            }
            catch (Exception ex)
            {
                WarnOnce("robbery-exception", $"[MarketStallPatch] AdvanceRobberyClock failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
                return false;
            }
        }

        /// <summary>The live InGameStat for the robbery clock, or null after a warn-once line naming the
        /// cause. Looks the entry up in GameManager.StatsDict by the GameStat DEFINITION: handing the
        /// definition itself to StatAccess reads NaN and writes nothing (memory
        /// reference_gamestat_direct_csharp_write).</summary>
        private static object RobberyDaysEntry(object gm)
        {
            var stat = RobberyDaysStat();
            if (stat == null) return null;

            if (Reflect.GetMember(gm, "StatsDict") is not IDictionary statsDict)
            {
                WarnOnce("robbery-statsdict-null", "[MarketStallPatch] GameManager.StatsDict is not readable - the stall robbery clock cannot advance.");
                return null;
            }
            if (!statsDict.Contains(stat))
            {
                WarnOnce("robbery-statsdict-miss", $"[MarketStallPatch] {RobberyDaysStatUid} is not in GameManager.StatsDict - the stall robbery clock cannot advance.");
                return null;
            }
            var entry = statsDict[stat];
            if (entry == null)
                WarnOnce("robbery-entry-null", $"[MarketStallPatch] {RobberyDaysStatUid} has a null StatsDict entry - the stall robbery clock cannot advance.");
            return entry;
        }

        /// <summary>The GameStat definition for <see cref="RobberyDaysStatUid"/>, resolved once.</summary>
        private static object RobberyDaysStat()
        {
            if (_robberyDaysStat != null) return _robberyDaysStat;
            if (_getFromIdMethod == null)
            {
                var uidType = CardUtil.FindGameType("UniqueIDScriptable");
                _getFromIdMethod = uidType?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "GetFromID" && !m.IsGenericMethodDefinition
                        && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
                if (_getFromIdMethod == null)
                {
                    WarnOnce("robbery-getfromid-null", "[MarketStallPatch] UniqueIDScriptable.GetFromID(string) not found - the stall robbery clock cannot advance.");
                    return null;
                }
            }
            _robberyDaysStat = _getFromIdMethod.Invoke(null, new object[] { RobberyDaysStatUid });
            if (_robberyDaysStat == null)
                WarnOnce("robbery-stat-missing", $"[MarketStallPatch] {RobberyDaysStatUid} did not resolve - the stall robbery clock cannot advance.");
            return _robberyDaysStat;
        }

        private static void WarnOnce(string cause, string message)
        {
            if (_warned.Add(cause)) Plugin.Logger.LogWarning(message);
        }

        private static void SellNextItem(object stall)
        {
            var (list, container, item) = PickRandomItem(stall);
            if (item == null) return;

            float baseValue = GetItemValue(item);
            float multiplier = VillagePricing.Multiplier(VillagePricing.PriceSide.Earn);
            float value = baseValue * multiplier;

            // Credit the revenue first and only then take the item, so a revenue that cannot be
            // written leaves the item on the stall instead of selling it for nothing (G5).
            float current = CardUtil.GetDurability(stall, "SpecialDurability1");
            if (float.IsNaN(current)) current = 0f;
            float newRev = Mathf.Min(current + value, SD1MaxValue);
            if (!CardUtil.SetDurability(stall, "SpecialDurability1", newRev))
            {
                WarnOnce("sell-credit-failed", "[MarketStallPatch] Sales Revenue (SpecialDurability1) did not take the write - the stall sells nothing until it does.");
                return;
            }
            ConsumeItem(stall, list, container, item);
            Plugin.Logger.LogDebug($"[MarketStallPatch] Item sold for +{value:0.##} revenue (base {baseValue:0}, x{multiplier:0.00}). Total: {newRev:0}");
        }

        private static void TryRobStall(object stall)
        {
            var (list, container, item) = PickRandomItem(stall);
            if (item == null) return;
            string stolenUid = CardUtil.GetCardUniqueId(item);
            ConsumeItem(stall, list, container, item);
            Plugin.Logger.LogDebug($"[MarketStallPatch] Market stall robbed! An item was stolen ({stolenUid ?? "unknown uid"}).");
            RecordTheft(stolenUid);
        }

        // ── D6: the Watch's report ────────────────────────────────────────────────

        /// <summary>Remembers what was stolen so a guard can be asked about it. One open report at a time: a
        /// robbery while a report is open (state not 0) is not recorded, and an unreadable stat records nothing.
        /// The hash is written BEFORE the state, so a state of 1 always carries an identity.</summary>
        private static void RecordTheft(string uid)
        {
            if (string.IsNullOrEmpty(uid))
            {
                WarnOnce("theft-uid-null", "[MarketStallPatch] The stolen card's UniqueID could not be read, so this theft cannot be reported.");
                return;
            }
            float state = HiddenStat.Get(TheftStateUid);
            if (state < 0f)
            {
                WarnOnce("theft-state-unreadable", $"[MarketStallPatch] {TheftStateUid} is not readable, so this theft cannot be reported.");
                return;
            }
            if (state >= 0.5f)
            {
                Plugin.Logger.LogDebug("[MarketStallPatch] A theft report is already open, so this theft is not recorded.");
                return;
            }
            if (!HiddenStat.Set(TheftHashUid, IdentityHash(uid)) || !HiddenStat.Set(TheftStateUid, 1f))
            {
                WarnOnce("theft-write-failed", "[MarketStallPatch] The theft record could not be written, so this theft cannot be reported.");
                return;
            }
            Plugin.Logger.LogDebug($"[MarketStallPatch] Theft recorded ({uid}): it can be reported to any guard.");
        }

        /// <summary>Once a day: a report a guard has taken (state 2) gets its due day, and on that day the search is
        /// decided. 3 means the item is owed, 4 that nothing was found. The state is written before the due day is
        /// cleared, so a failed write retries the next day instead of losing the report.</summary>
        private static void TheftReportDay()
        {
            try
            {
                float state = HiddenStat.Get(TheftStateUid);
                if (state < 1.5f || state >= 2.5f) return;

                int today = GameQuery.CurrentDay;
                if (today <= 0)
                {
                    WarnOnce("theft-day-unreadable", "[MarketStallPatch] The current day is not readable, so a theft report cannot be scheduled.");
                    return;
                }
                float due = HiddenStat.Get(RecoveryDueUid);
                if (due < 0f)
                {
                    WarnOnce("theft-due-unreadable", $"[MarketStallPatch] {RecoveryDueUid} is not readable, so a theft report cannot be scheduled.");
                    return;
                }
                if (due < 0.5f)
                {
                    if (!HiddenStat.Set(RecoveryDueUid, Math.Min(RecoveryDueMax, today + RecoveryDelayDays)))
                        WarnOnce("theft-due-write-failed", $"[MarketStallPatch] {RecoveryDueUid} could not be written, so a theft report cannot be scheduled.");
                    else
                        Plugin.Logger.LogDebug($"[MarketStallPatch] Theft report taken: the Watch decides on day {today + RecoveryDelayDays}.");
                    return;
                }
                if (today < due) return;

                float roll = UnityEngine.Random.value;
                bool found = roll < RecoveryChance;
                if (!HiddenStat.Set(TheftStateUid, found ? 3f : 4f))
                {
                    WarnOnce("theft-result-write-failed", $"[MarketStallPatch] {TheftStateUid} could not be written, so the search result was not recorded.");
                    return;
                }
                HiddenStat.Set(RecoveryDueUid, 0f);
                Plugin.Logger.LogDebug($"[MarketStallPatch] Theft search decided: rolled {roll:0.000} against {RecoveryChance:0.000}, {(found ? "found" : "not found")}.");
            }
            catch (Exception ex)
            {
                WarnOnce("theft-report-exception", $"[MarketStallPatch] TheftReportDay failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        /// <summary>The delivery poll: a recovered item (state 3) is spawned once the player stands in the Village
        /// and the board has settled. The item is resolved BEFORE anything is latched, the latch (state back to 0)
        /// is written BEFORE the spawn, and an item that no longer resolves is dropped with a warn-once line, never
        /// retried forever.</summary>
        private static void TheftDeliveryTick()
        {
            try
            {
                float state = HiddenStat.Get(TheftStateUid);
                if (state < 2.5f || state >= 3.5f) return;
                if (GameQuery.IsTransitioning) return;
                if (GameQuery.CurrentEnvironmentUniqueId != VillageEnvUid) return;

                float hash = HiddenStat.Get(TheftHashUid);
                if (hash < 0f) { WarnOnce("theft-hash-unreadable", $"[MarketStallPatch] {TheftHashUid} is not readable, so a recovered item cannot be delivered."); return; }

                string uid = ResolveStolenUid((int)Math.Round(hash));
                if (!HiddenStat.Set(TheftStateUid, 0f))
                {
                    WarnOnce("theft-latch-write-failed", $"[MarketStallPatch] {TheftStateUid} could not be reset, so the recovered item was not delivered.");
                    return;
                }
                HiddenStat.Set(TheftHashUid, 0f);
                if (uid == null) return; // ResolveStolenUid already warned, once per cause

                SpawnService.Spawn(uid);
                Plugin.Logger.LogInfo($"[MarketStallPatch] The Watch recovered the stolen item ({uid}); it is back in the Village.");
            }
            catch (Exception ex)
            {
                WarnOnce("theft-delivery-exception", $"[MarketStallPatch] TheftDeliveryTick failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        /// <summary>The UniqueID of the one loaded card whose <see cref="IdentityHash"/> matches, or null after a
        /// warn-once line naming the cause (nothing matches, or two cards share the hash).</summary>
        private static string ResolveStolenUid(int hash)
        {
            var all = UniqueIDScriptable.AllUniqueObjectsAsInts;
            if (all == null)
            {
                WarnOnce("theft-registry-null", "[MarketStallPatch] The game's card registry is not readable, so the stolen item cannot be found again.");
                return null;
            }
            string match = null;
            foreach (var entry in all)
            {
                if (entry is not CardData card || string.IsNullOrEmpty(card.UniqueID)) continue;
                if (IdentityHash(card.UniqueID) != hash) continue;
                if (match != null && !string.Equals(match, card.UniqueID, StringComparison.Ordinal))
                {
                    WarnOnce("theft-ambiguous", $"[MarketStallPatch] Two cards ({match} and {card.UniqueID}) share the stolen item's identity hash, so it is not returned.");
                    return null;
                }
                match = card.UniqueID;
            }
            if (match == null)
                WarnOnce("theft-unresolved", "[MarketStallPatch] The stolen item no longer resolves to a loaded card, so it is not returned.");
            return match;
        }

        // ── Inventory manipulation ────────────────────────────────────────────────

        private static (IList list, object container, object item) PickRandomItem(object stall)
        {
            var slots = CardUtil.GetInventoryList(stall);
            if (slots == null || slots.Count == 0) return (null, null, null);

            var candidates = new List<(object container, IList innerList, object card)>();
            foreach (var slot in slots)
            {
                if (slot == null) continue;
                var inner = CardUtil.GetInventoryList(slot);
                if (inner != null)
                {
                    foreach (var c in inner)
                        if (c != null) candidates.Add((slot, inner, c));
                }
                else
                {
                    candidates.Add((null, slots, slot));
                }
            }

            if (candidates.Count == 0) return (null, null, null);
            int idx    = UnityEngine.Random.Range(0, candidates.Count);
            var chosen = candidates[idx];
            return (chosen.innerList, chosen.container, chosen.card);
        }

        private static void ConsumeItem(object stall, IList list, object container, object item)
        {
            try
            {
                list.Remove(item);
                TrySetActive(item, false);

                // Remove the slot wrapper if it is now empty
                if (container != null)
                {
                    var remaining = CardUtil.GetInventoryList(container);
                    if (remaining == null || remaining.Count == 0)
                        CardUtil.GetInventoryList(stall)?.Remove(container);
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[MarketStallPatch] ConsumeItem: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void TrySetActive(object card, bool active)
        {
            try
            {
                if (card is UnityEngine.Object uo)
                {
                    var go = (uo as Component)?.gameObject ?? uo as GameObject;
                    go?.SetActive(active);
                }
            }
            catch (Exception ex)
            {
                if (!_setActiveFailureLogged)
                {
                    _setActiveFailureLogged = true;
                    Plugin.Logger.LogDebug($"[MarketStallPatch] TrySetActive failed (item visual may not toggle): {ex.Message}");
                }
            }
        }

        // ── Value calculation ─────────────────────────────────────────────────────

        private static float GetItemValue(object card)
        {
            var cardData = CardUtil.GetCardData(card);
            if (cardData == null) return 1f;
            int weight = Reflect.GetInt(cardData, "ObjectWeight");
            return Mathf.Max(1f, weight / 10f);
        }
    }
}
