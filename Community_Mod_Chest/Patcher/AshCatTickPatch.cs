using System;
using System.Collections.Generic;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using UnityEngine;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Auto-drink for Ash the Cat and Shadow.
    ///
    /// Fires every DTP tick. When a cat's thirst (UsageDurability) falls below 20% and a
    /// water source on the board actually holds water (vanilla tag_WaterAny on its contained
    /// liquid - a brine vat, a cauldron of medicine or a kettle of tea is never drunk from,
    /// same as Sirus23's wolf), drains 100 liquid from the nearest one through
    /// CardUtil.GetLiquidQuantity/ModifyLiquid and restores thirst to full - at the cost of a
    /// Care (SpecialDurability3) chunk, per CHANGELOG's "at the cost of your bond" design:
    /// self-serve is a convenience, not free.
    ///
    /// Mirrors Sirus23's WolfTickPatch auto-drink pattern. Uses FindObjectsOfType with an
    /// AllCards-count cache to avoid calling the scan every tick when nothing has changed.
    /// </summary>
    internal static class AshCatTickPatch
    {
        private const string InnCatUid       = "cmcInnCat";
        private const string ShadowCatUid    = "cmcShadowCatTamed";
        private const float  ThirstThreshold = 0.20f;
        private const string WaterLiquidTag  = "tag_WaterAny";

        // Care (SpecialDurability3, 0-100) lost per auto-drink. At the 20% thirst threshold,
        // full-reliance drink interval is 3*(1-0.20) = 2.4 in-game days (Thirst MaxValue 288,
        // RatePerDaytimePoint -1, 96 DTP/day); 25 Care/drink zeroes a fresh 100 Care cat in 4
        // drinks (~9.6 days) — matching CHANGELOG's "runs dry after roughly 9 in-game days."
        internal const float CareLossPerDrink = 25f;
        private const string CareStatName     = "SpecialDurability3";

        // Containers not reliably tagged tag_WaterContainer (all three are vanilla).
        private static readonly string[] ExplicitWaterIds =
        {
            "2ae9292d2b80ccb41a0840ca736e0d12",  // ClayBasin (vanilla)
            "1cecbd7141fa89a4c90686dcd2d92155",  // WateringTrough (vanilla)
            "536f722edbb5e9e4b959b1f3ad25f648",  // RainCistern (vanilla)
        };

        private static readonly Dictionary<UnityEngine.Object, bool> _waterLiquidModelCache = new();
        private static readonly HashSet<string> _warnedCauses = new(StringComparer.Ordinal);

        private static HashSet<string> _waterIds;
        private static Type   _cardBaseType;

        // Board-change cache — mirrors WolfTickPatch to avoid redundant FindObjectsOfType calls.
        private static List<object> _cachedCats;
        private static List<object> _cachedWaters;
        private static int _lastAllCardsCount = int.MinValue;

        public static void Initialize()
        {
            _waterIds = BuildWaterIdSet();
            TickEvents.DtpTick += OnTick;
        }

        private static HashSet<string> BuildWaterIdSet()
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            var registry = VanillaIds.WaterSources;
            if (registry != null && registry.Count > 0)
                foreach (var uid in registry) set.Add(uid);
            foreach (var uid in ExplicitWaterIds) set.Add(uid);
            return set;
        }

        private static void OnTick()
        {
            try
            {
                _cardBaseType ??= CardUtil.FindGameType("InGameCardBase");
                if (_cardBaseType == null) return;

                RefreshCaches();
                if (_cachedCats == null || _cachedCats.Count == 0) return;

                foreach (var cat in _cachedCats)
                    TryAutoDrink(cat, _cachedWaters);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"[AshCatTickPatch] {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void RefreshCaches()
        {
            int count = GetAllCardsCount();
            if (_cachedCats != null && count == _lastAllCardsCount) return;
            _lastAllCardsCount = count;

            _cachedCats   = new List<object>();
            _cachedWaters = new List<object>();

            var all = UnityEngine.Object.FindObjectsOfType(_cardBaseType);
            if (all == null) return;

            foreach (var obj in all)
            {
                string uid = CardUtil.GetCardUniqueId(obj);
                if (uid == null) continue;
                if (uid == InnCatUid || uid == ShadowCatUid) _cachedCats.Add(obj);
                else if (_waterIds.Contains(uid)) _cachedWaters.Add(obj);
            }
        }

        private static int GetAllCardsCount()
        {
            var gm = CardUtil.GetGameManagerInstance();
            if (gm == null) return -1;
            return Reflect.GetMember(gm, "AllCards") is System.Collections.ICollection c ? c.Count : -1;
        }

        private static void TryAutoDrink(object cat, List<object> waters)
        {
            if (waters == null || waters.Count == 0) return;

            float cur = CardUtil.GetDurability(cat, "UsageDurability");
            float max = CardUtil.GetDurabilityMax(cat, "UsageDurability");
            if (float.IsNaN(cur) || float.IsNaN(max) || max <= 0f) return;
            if ((cur / max) >= ThirstThreshold) return;

            object source = null;
            float bestDist = float.MaxValue;
            foreach (var w in waters)
            {
                if (!HoldsWater(w)) continue;
                float qty = CardUtil.GetLiquidQuantity((InGameCardBase)w);
                if (float.IsNaN(qty) || qty <= 0f) continue;
                float d = DistSq(cat, w);
                if (d < bestDist) { bestDist = d; source = w; }
            }
            if (source == null) return;

            if (!CardUtil.SetDurability(cat, "UsageDurability", max)) return;
            if (!CardUtil.ModifyLiquid((InGameCardBase)source, -100f))
                WarnOnce("drink-liquid-write", $"[AshCatTickPatch] {CardUtil.GetCardUniqueId(source)}'s water could not be reduced after a cat drank from it, so the drink was free.");
            ApplyCareLoss(cat);
            Plugin.Logger.LogDebug($"[AshCatTickPatch] {CardUtil.GetCardUniqueId(cat)} auto-drank (thirst {cur:F0}->{max:F0})");
        }

        // Self-serve drinking costs a chunk of Care (SpecialDurability3) — the "cost of your
        // bond" the auto-drink convenience trades against. Left entirely to the cistern, Care
        // reaches 0 and the cat's own HasActionOnZero wander-off fires, same as vanilla-style
        // relationship stats elsewhere in this mod.
        private static void ApplyCareLoss(object cat)
        {
            float careCur = CardUtil.GetDurability(cat, CareStatName);
            if (float.IsNaN(careCur)) return;
            float next = Math.Max(0f, careCur - CareLossPerDrink);
            CardUtil.SetDurability(cat, CareStatName, next);
        }

        private static float DistSq(object a, object b)
        {
            if (a is not Component ca || b is not Component cb) return 0f;
            return (ca.transform.position - cb.transform.position).sqrMagnitude;
        }

        /// <summary>
        /// True when the container's contained liquid card carries tag_WaterAny. Mirrors
        /// Sirus23's WolfTickPatch.HoldsWater: a container with no contained liquid holds
        /// nothing drinkable, and a liquid that is not water (brine, medicine, tea) is never
        /// auto-drunk even though it sits in a water-tagged container.
        /// </summary>
        private static bool HoldsWater(object container)
        {
            var contained = Reflect.GetMember(container, "ContainedLiquid");
            if (contained is not UnityEngine.Object uo || uo == null) return false;
            return ModelHasTag(contained, WaterLiquidTag, _waterLiquidModelCache);
        }

        private static bool ModelHasTag(object card, string tagName, Dictionary<UnityEngine.Object, bool> cache)
        {
            if (CardUtil.GetCardData(card) is not UnityEngine.Object model || model == null)
            {
                WarnOnce("model-unreadable", $"[AshCatTickPatch] a card's CardModel could not be read; it is not treated as {tagName}.");
                return false;
            }
            if (cache.TryGetValue(model, out bool hit)) return hit;

            hit = false;
            if (Reflect.GetMember(model, "CardTags") is System.Collections.IEnumerable tags)
            {
                foreach (var tag in tags)
                    if (tag is UnityEngine.Object t && t != null
                        && string.Equals(t.name, tagName, StringComparison.Ordinal))
                    { hit = true; break; }
            }
            else
            {
                WarnOnce("cardtags-unreadable", $"[AshCatTickPatch] CardData.CardTags could not be read on '{model.name}'; the water check falls back to false.");
            }
            cache[model] = hit;
            return hit;
        }

        private static void WarnOnce(string cause, string message)
        {
            if (_warnedCauses.Add(cause)) Plugin.Logger.LogWarning(message);
        }
    }
}
