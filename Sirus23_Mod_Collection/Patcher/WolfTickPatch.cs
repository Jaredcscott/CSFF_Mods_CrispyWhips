using System;
using System.Collections.Generic;
using BepInEx.Logging;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using UnityEngine;

namespace Sirus23ModCollection.Patcher
{
    /// <summary>
    /// All companion upkeep: auto-feed, auto-drink, starvation health drain, and
    /// morale-departure check, evaluated once per in-game DTP tick.
    ///
    /// Handles wolf, fox, and owl companions. Auto-drink fires when a companion's
    /// thirst drops below 20% and a water container (ClayBasin, WateringTrough,
    /// RainCistern, ClayBowl, ClayJar, CopperBottle, CopperJar, WaterskinWaterProofed_)
    /// is on the board — drains 100 liquid from the nearest container and restores
    /// the companion's thirst to full. Auto-feed works identically for hunger using
    /// the nearest available food item.
    ///
    /// Health management and morale-departure apply to all three companions; health
    /// no-ops for fox/owl because SpecialDurability1 is inactive on those cards.
    /// </summary>
    public static class WolfTickPatch
    {
        public const string WolfId = "fc_wolf_companion";
        public const string FoxId  = "fc_fox_companion";
        public const string OwlId  = "fc_owl_companion";

        private const string WolfCarcassUid = "aad9634a9b5273844bbe2e7a119c5b23";

        private const float Threshold = 0.20f;
        private static ManualLogSource Logger => Plugin.Logger;

        private static readonly string[] FallbackFoodIds =
        {
            "fe07d4d800bcc8646a0ff2513c78d5df",
            "e292e8faac1041f4ab1da9bcf900e751",
            "91168c5978471d54aaad816581f63ffd",
            "f4be79e87ba98db41a9c9e31bb76c33d",
            "5dc9560f81be57c4b9e22780f8a5ad78",
            "692c88d91a4fdf0498749339ae6d53f8",
            "4093dde8982306b4d8a5a9108ac5f0bb",
            "1e3f5517c9203e74b9c76a7bae1a3920",
            "ec0f203ea77c79741a70170a2c42e202",
            "f6cc4f98e0ab4ae47b4b2c8f5d48da02",
            "b25f1f9bfc2a0bc44a93079ed02cb43f",
            "699942038046235428420895876b83e2",
            "674b640f46671f1418af4559b259b442",
            "3dda5e481d296c5409b05ac6e66a9221"
        };

        // Mod-added water containers (always merged with VanillaIds.WaterSources).
        private static readonly string[] ModWaterIds =
        {
            // AdvancedCopperTools
            "advanced_copper_tools_bucket",              // Wheelbarrow Bucket
            "advanced_copper_tools_wearable_metal_pan",  // Wearable Metal Pan
            "advanced_copper_tools_copper_tea_kettle",   // Copper Tea Kettle
            "advanced_copper_tools_copper_cauldron",     // Copper Cauldron
            "advanced_copper_tools_copper_bathtub_empty",// Copper Bathtub Kit
            // Community_Mod_Chest
            "potterysculpturepaintedvase",               // Painted Clay Vase
            "potterysculpturepaintedcup",                // Painted Clay Cup
            "potterysculpturepaintedbowl",               // Painted Clay Bowl
            // HerbsAndFungi
            "herbs_fungi_pickling_vat",                  // Pickle Vat
        };

        // Vanilla backstop: used when VanillaIds registry is empty (e.g. framework not installed).
        private static readonly string[] FallbackWaterIds =
        {
            "2ae9292d2b80ccb41a0840ca736e0d12",  // ClayBasin
            "1cecbd7141fa89a4c90686dcd2d92155",  // WateringTrough
            "536f722edbb5e9e4b959b1f3ad25f648",  // RainCistern
            "a968f3eaffc6b9743b82982b5af2ab8c",  // ClayBowl
            "db78f1724c2fffe4d9302c72457ca8bf",  // ClayJar
            "57ede9efd6e81ec41a065f3f7a2e3d8e",  // CopperBottle
            "dff317def52c6294a8ee7cfc3ce23b63",  // CopperJar
            "2674de0b486a266499d2edfc38f5148d",  // WaterskinWaterProofed_
        };

        private static HashSet<string> _foodIds;
        private static HashSet<string> _waterIds;

        private static List<object> _cachedCompanions;
        private static List<object> _cachedFoods;
        private static List<object> _cachedWaters;
        private static int _lastAllCardsCount = int.MinValue;
        private static Type _cardBaseType;

        public static void Register()
        {
            _foodIds  = BuildIdSet(VanillaIds.AnimalFoods,  FallbackFoodIds);
            _waterIds = BuildWaterIdSet();
            TickEvents.DtpTick += OnTick;
        }

        private static HashSet<string> BuildWaterIdSet()
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            var registry = VanillaIds.WaterSources;
            if (registry != null && registry.Count > 0)
                foreach (var uid in registry) set.Add(uid);
            else
                foreach (var uid in FallbackWaterIds) set.Add(uid);
            // Mod-added containers always merged regardless of registry state.
            foreach (var uid in ModWaterIds) set.Add(uid);
            return set;
        }

        private static HashSet<string> BuildIdSet(IReadOnlyList<string> registry, string[] fallback)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (registry != null && registry.Count > 0)
                foreach (var uid in registry) set.Add(uid);
            else
                foreach (var uid in fallback) set.Add(uid);
            return set;
        }

        private static void OnTick()
        {
            try
            {
                _cardBaseType ??= CardUtil.FindGameType("InGameCardBase");
                if (_cardBaseType == null) return;

                RefreshCachesIfBoardChanged();
                if (_cachedCompanions == null || _cachedCompanions.Count == 0) return;

                foreach (var companion in _cachedCompanions)
                {
                    if (TryInitFreshSpawn(companion)) continue;
                    TryAutoFeed(companion, _cachedFoods);
                    TryAutoDrink(companion, _cachedWaters);
                    TryManageHealth(companion);
                    TryMoraleCheck(companion);
                }
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[CompanionTick] tick error: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void RefreshCachesIfBoardChanged()
        {
            int count = GetAllCardsCount();
            if (_cachedCompanions != null && count == _lastAllCardsCount) return;
            _lastAllCardsCount = count;

            _cachedCompanions = new List<object>();
            _cachedFoods      = new List<object>();
            _cachedWaters     = new List<object>();

            var all = UnityEngine.Object.FindObjectsOfType(_cardBaseType);
            if (all == null) return;

            foreach (var obj in all)
            {
                string uid = CardUtil.GetCardUniqueId(obj);
                if (uid == null) continue;
                if (uid == WolfId || uid == FoxId || uid == OwlId) _cachedCompanions.Add(obj);
                else if (_foodIds.Contains(uid))  _cachedFoods.Add(obj);
                else if (_waterIds.Contains(uid)) _cachedWaters.Add(obj);
            }
        }

        /// <summary>
        /// Whether an owl companion is currently on the board, per this tick's cached
        /// companion scan (used by WildOwlLifecyclePatch to gate the companion-death
        /// respawn timer without a second FindObjectsOfType scan per tick).
        /// </summary>
        internal static bool HasOwlCompanion()
        {
            if (_cachedCompanions == null) return false;
            foreach (var c in _cachedCompanions)
                if (CardUtil.GetCardUniqueId(c) == OwlId) return true;
            return false;
        }

        private static int GetAllCardsCount()
        {
            var gm = CardUtil.GetGameManagerInstance();
            if (gm == null) return -1;
            return Reflect.GetMember(gm, "AllCards") is System.Collections.ICollection c ? c.Count : -1;
        }

        // ── Upkeep rules ─────────────────────────────────────────────────────

        /// <summary>
        /// Companions spawned via ProducedCards/GiveCard (tame success) start with their
        /// durability fields at the struct default (0), not the CardData's designed
        /// FloatValue (SpawnStatDefaults doesn't apply to this spawn path — memory
        /// reference_givecard_postfix_stat_init). Left alone, TryMoraleCheck removes the
        /// companion on the very next tick because Progress (Morale) reads 0. Hunger and
        /// Thirst decay gradually and independently, so a companion legitimately reading
        /// exactly 0 across ALL THREE at once — while under normal play TryMoraleCheck would
        /// already have removed it the instant Morale alone hit 0 — only happens in the tick
        /// right after spawn. Treat that as "never initialized" and fill to full instead of
        /// treating it as starvation/death.
        /// </summary>
        internal static bool TryInitFreshSpawn(object companion)
        {
            if (!TryGetStat(companion, "SpoilageTime", out float hunger, out float hungerMax)) return false;
            if (!TryGetStat(companion, "UsageDurability", out float thirst, out float thirstMax)) return false;
            if (!TryGetStat(companion, "Progress", out float morale, out float moraleMax)) return false;
            if (hungerMax <= 0f || thirstMax <= 0f || moraleMax <= 0f) return false;
            if (hunger > 0f || thirst > 0f || morale > 0f) return false;

            CardUtil.SetDurability(companion, "SpoilageTime", hungerMax);
            CardUtil.SetDurability(companion, "UsageDurability", thirstMax);
            CardUtil.SetDurability(companion, "Progress", moraleMax);
            Logger?.LogDebug($"[CompanionTick] {CardUtil.GetCardUniqueId(companion) ?? "companion"} spawned with zeroed stats — initialized to full (hunger {hungerMax:F0}, thirst {thirstMax:F0}, morale {moraleMax:F0}).");
            return true;
        }

        private static void TryManageHealth(object companion)
        {
            if (!TryGetStat(companion, "SpecialDurability1", out float healthCur, out float healthMax)) return;
            if (healthMax <= 0f) return;  // no-op for fox/owl which have SpecialDurability1 inactive

            if (!TryGetStat(companion, "SpoilageTime", out float hungerCur, out float hungerMax)) return;
            float hungerRatio = hungerMax > 0f ? hungerCur / hungerMax : 0f;

            if (hungerRatio < 0.10f)
            {
                float newHealth = (float)Math.Max(0.0, healthCur - 2.0);
                if (newHealth == healthCur) return;
                CardUtil.SetDurability(companion, "SpecialDurability1", newHealth);
                if (newHealth <= 0f)
                {
                    Logger?.LogDebug("[CompanionTick] wolf died of starvation — spawning wolf carcass.");
                    SpawnService.Spawn(WolfCarcassUid);
                    CardUtil.TryRemoveCard(companion);
                    _lastAllCardsCount = int.MinValue;
                }
            }
            else if (hungerRatio > 0.60f && healthCur < healthMax)
            {
                CardUtil.SetDurability(companion, "SpecialDurability1",
                    (float)Math.Min(healthMax, healthCur + 0.5));
            }
        }

        private static void TryMoraleCheck(object companion)
        {
            if (!TryGetStat(companion, "Progress", out float cur, out float max)) return;
            if (max <= 0f || cur > 0f) return;

            string uid = CardUtil.GetCardUniqueId(companion) ?? "companion";
            Logger?.LogDebug($"[CompanionTick] {uid} morale depleted — companion has left.");
            CardUtil.TryRemoveCard(companion);
            _lastAllCardsCount = int.MinValue;
        }

        private static void TryAutoFeed(object companion, List<object> foods)
        {
            if (foods == null || foods.Count == 0) return;

            if (!TryGetStat(companion, "SpoilageTime", out float cur, out float max)) return;
            if (max <= 0f || (cur / max) >= Threshold) return;

            object food = PickNearest(companion, foods);
            if (food == null) return;

            if (!CardUtil.SetDurability(companion, "SpoilageTime", max)) return;
            string foodUid = CardUtil.GetCardUniqueId(food);
            if (CardUtil.TryRemoveCard(food))
            {
                foods.Remove(food);
                _lastAllCardsCount = int.MinValue;
                // Mirror the Feed CI: auto-feeding also grants a small morale boost so the
                // companion doesn't immediately depart on the same tick it eats.
                if (TryGetStat(companion, "Progress", out float mor, out float morMax) && morMax > 0f)
                    CardUtil.SetDurability(companion, "Progress", (float)Math.Min(morMax, mor + 20.0));
                Logger?.LogDebug($"[CompanionTick] {CardUtil.GetCardUniqueId(companion)} auto-ate {foodUid} (hunger {cur:F0}->{max:F0})");
            }
        }

        private static void TryAutoDrink(object companion, List<object> waters)
        {
            if (waters == null || waters.Count == 0) return;

            if (!TryGetStat(companion, "UsageDurability", out float cur, out float max)) return;
            if (max <= 0f || (cur / max) >= Threshold) return;

            object source = null;
            float bestDist = float.MaxValue;
            foreach (var w in waters)
            {
                if (GetLiquidQuantity(w) <= 0f) continue;
                float d = DistanceSquared(companion, w);
                if (d < bestDist) { bestDist = d; source = w; }
            }
            if (source == null) return;

            if (!CardUtil.SetDurability(companion, "UsageDurability", max)) return;
            AdjustLiquidQuantity(source, -100f);
            Logger?.LogDebug($"[CompanionTick] {CardUtil.GetCardUniqueId(companion)} auto-drank from {CardUtil.GetCardUniqueId(source)} (thirst {cur:F0}->{max:F0})");
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static bool TryGetStat(object card, string statName, out float cur, out float max)
        {
            cur = CardUtil.GetDurability(card, statName);
            max = CardUtil.GetDurabilityMax(card, statName);
            if (float.IsNaN(cur) || float.IsNaN(max)) { cur = 0f; max = 0f; return false; }
            return true;
        }

        private static float DistanceSquared(object a, object b)
        {
            if (a is not UnityEngine.Component ca || b is not UnityEngine.Component cb) return 0f;
            return (ca.transform.position - cb.transform.position).sqrMagnitude;
        }

        private static object PickNearest(object companion, List<object> candidates)
        {
            object best = null;
            float bestDist = float.MaxValue;
            foreach (var c in candidates)
            {
                float d = DistanceSquared(companion, c);
                if (d < bestDist) { bestDist = d; best = c; }
            }
            return best;
        }

        private static readonly string[] LiquidMemberNames =
            { "CurrentLiquidQuantity", "LiquidQuantity", "CurrentLiquid" };

        // Containers (Rain Cistern, ClayBowl, WateringTrough, ...) hold their water in the
        // ContainedLiquid child card, not their own CurrentLiquidQuantity — that field stays 0
        // unless the card itself is a Liquid. Mirrors InGameCardBase.GetCurrentDurability(Liquid).
        private static object ResolveLiquidHolder(object card)
        {
            var contained = Reflect.GetMember(card, "ContainedLiquid");
            if (contained is UnityEngine.Object uo && uo != null) return contained;
            return card;
        }

        private static float GetLiquidQuantity(object card)
        {
            var v = Reflect.GetMember(ResolveLiquidHolder(card), LiquidMemberNames);
            return v != null ? CardUtil.ToFloat(v) : 0f;
        }

        private static void AdjustLiquidQuantity(object card, float delta)
        {
            var holder = ResolveLiquidHolder(card);
            var v = Reflect.GetMember(holder, LiquidMemberNames);
            if (v == null) return;
            float next = Math.Max(0f, CardUtil.ToFloat(v) + delta);
            Reflect.SetMemberAny(holder, next, LiquidMemberNames);
        }
    }
}
