using System;
using System.Collections;
using System.Collections.Generic;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using UnityEngine;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Market Stall mechanics:
    ///   - Village gate: "Set Up Stall" DA is canceled outside cmcEnvVillage
    ///   - Selling: one random item consumed from inventory per day; value = ObjectWeight/10 added to SD1
    ///   - "Collect as Copper": drains SD1, spawns MetalNuggets (50 revenue each)
    ///   - "Collect as Salt":   drains SD1, spawns Salt       (30 revenue each)
    ///   - Robbery: 5% chance every 7 days — one item stolen from inventory
    /// </summary>
    internal static class MarketStallPatch
    {
        private const string KitUid        = "cmcmarketstallkit";
        private const string StallUid      = "cmcmarketstall";
        private const string VillageEnvUid = "cmcEnvVillage";

        private const string NuggetGuid = "4b0f4937a5ecb90499428c8c10288afc";
        private const string SaltGuid   = "f91b5676fc26c0e48a4ee6fd9dfc2ffa";

        private const float CostPerNugget      = 50f;
        private const float CostPerSalt        = 30f;
        private const float SD1MaxValue        = 480f;

        private const float RobberyChance       = 0.05f;
        private const int   RobberyIntervalDays = 7;
        private static int  _daysSinceRobbery   = 0;

        /// <summary>Village Hall renown reward — the multiplier applied to sale revenue.
        /// Set by VillageRenownPatch once Village Renown reaches full.</summary>
        public const float FullRenownRevenueMultiplier = 1.25f;
        public static float RevenueMultiplier = 1f;

        private static bool _initialized;
        private static bool _setActiveFailureLogged;

        // ── Registration ─────────────────────────────────────────────────────────

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

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
                CardUid          = StallUid,
                ActionNamePrefix = "Collect as Copper",
                Timing           = ActionTiming.AfterWrapped,
                After            = CollectCopperAfter,
            });

            // Payment: collect as salt
            ActionRouter.Register(new ActionHandler
            {
                Name             = "MarketStallCollectSalt",
                CardUid          = StallUid,
                ActionNamePrefix = "Collect as Salt",
                Timing           = ActionTiming.AfterWrapped,
                After            = CollectSaltAfter,
            });

            TickEvents.DayRollover += OnDayChanged;

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
            CardUtil.SetDurability(ctx.Card, "SpecialDurability1", revenue - count * CostPerNugget);
            for (int i = 0; i < count; i++) SpawnService.Spawn(NuggetGuid);
            Plugin.Logger.LogDebug($"[MarketStallPatch] Collected {count} copper nugget(s) (revenue was {revenue:0}).");
        }

        private static void CollectSaltAfter(ActionContext ctx)
        {
            float revenue = CardUtil.GetDurability(ctx.Card, "SpecialDurability1");
            if (float.IsNaN(revenue) || revenue < CostPerSalt) return;
            int count = (int)(revenue / CostPerSalt);
            CardUtil.SetDurability(ctx.Card, "SpecialDurability1", revenue - count * CostPerSalt);
            for (int i = 0; i < count; i++) SpawnService.Spawn(SaltGuid);
            Plugin.Logger.LogDebug($"[MarketStallPatch] Collected {count} salt unit(s) (revenue was {revenue:0}).");
        }

        // ── Daily tick ────────────────────────────────────────────────────────────

        private static void OnDayChanged()
        {
            try
            {
                var stalls = CardFinder.FindAll(StallUid);
                if (stalls.Count == 0) return;

                foreach (var stall in stalls)
                    SellNextItem(stall);

                _daysSinceRobbery++;
                if (_daysSinceRobbery >= RobberyIntervalDays)
                {
                    _daysSinceRobbery = 0;
                    if (UnityEngine.Random.value < RobberyChance)
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

        private static void SellNextItem(object stall)
        {
            var (list, container, item) = PickRandomItem(stall);
            if (item == null) return;

            float value  = GetItemValue(item);
            ConsumeItem(stall, list, container, item);

            float current = CardUtil.GetDurability(stall, "SpecialDurability1");
            if (float.IsNaN(current)) current = 0f;
            float newRev = Mathf.Min(current + value, SD1MaxValue);
            CardUtil.SetDurability(stall, "SpecialDurability1", newRev);
            Plugin.Logger.LogDebug($"[MarketStallPatch] Item sold for +{value:0} revenue. Total: {newRev:0}");
        }

        private static void TryRobStall(object stall)
        {
            var (list, container, item) = PickRandomItem(stall);
            if (item == null) return;
            ConsumeItem(stall, list, container, item);
            Plugin.Logger.LogDebug("[MarketStallPatch] Market stall robbed! An item was stolen.");
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
            return Mathf.Max(1f, weight / 10f) * RevenueMultiplier;
        }
    }
}
