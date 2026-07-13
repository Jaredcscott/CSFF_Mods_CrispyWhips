using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace WaterDrivenInfrastructure.Patcher
{
    /// <summary>
    /// Manages fishpond population breeding gate and Filled ↔ Stocked threshold transforms.
    ///
    /// Two behaviours:
    ///   1. Breeding gate  — a species only grows when its count is ≥ 2; fractional
    ///      growth accumulated while count < 2 is floored away each poll.
    ///   2. Threshold swap — when Pike+Perch+Minnow total exceeds 10 the pond transforms
    ///      to Stocked; when it falls below 10 it reverts to Filled. Populations are
    ///      preserved across the swap via CardUtil.TransformInPlacePreservingStats.
    ///
    /// Uses the same EnsureCardReflection / cache pattern as ActionInterceptPatch to avoid
    /// the silent-null failures that plagued the prior direct-GetProperty approach.
    /// Polling and stat get/set are driven by the framework's TickEvents/CardUtil
    /// (Centralization Tier 1/2) instead of a local StartCoroutine poll loop.
    /// </summary>
    public static class FishpondPopulationPatch
    {
        private const string FilledID   = "water_sawmill_fishpond_filled";
        private const string StockedID  = "water_sawmill_fishpond_stocked";
        private const string WinterID   = "water_sawmill_fishpond_winter";
        private const float  StockedThreshold   = 10f;
        private const float  PollIntervalSeconds = 2f;

        private static ManualLogSource Logger => Plugin.Logger;
        private static readonly BindingFlags Flags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // ── reflection caches: mirror ActionInterceptPatch.EnsureReflection pattern ──
        private static Type _cardBaseType;
        private static readonly Dictionary<Type, PropertyInfo> _cardModelCache = new Dictionary<Type, PropertyInfo>();
        private static FieldInfo _uidField;

        // ── pond cache — repopulated every 60 s; iterated every 2 s ──
        private static readonly List<UnityEngine.Object> _pondCache = new List<UnityEngine.Object>();
        private const float FullScanIntervalSeconds = 60f;

        public static void ApplyPatch(Harmony harmony)
        {
            try
            {
                // Framework-owned real-time interval ticks (CSFFModFramework.Api.TickEvents)
                // replace the local StartCoroutine(WaitForSecondsRealtime) dual-interval loop —
                // one registration per cadence instead of a single loop checking Time.time.
                TickEvents.Interval(FullScanIntervalSeconds, RefreshPondCache, "FishpondPop.RefreshPondCache");
                TickEvents.Interval(PollIntervalSeconds, CheckCachedPonds, "FishpondPop.CheckCachedPonds");
                Logger?.LogDebug("[FishpondPop] tick intervals registered");
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[FishpondPop] ApplyPatch: {ex}");
            }
        }

        // Full scene scan — runs once every 60 s instead of every 2 s.
        private static void RefreshPondCache()
        {
            _pondCache.Clear();

            if (_cardBaseType == null)
                _cardBaseType = AccessTools.TypeByName("InGameCardBase");
            if (_cardBaseType == null) return;

            var cards = UnityEngine.Object.FindObjectsOfType(_cardBaseType);
            if (cards == null || cards.Length == 0) return;

            foreach (UnityEngine.Object card in cards)
            {
                if (card == null) continue;
                EnsureCardReflection(card);
                string uid = GetUID(card);
                if (uid == FilledID || uid == StockedID || uid == WinterID)
                    _pondCache.Add(card);
            }
        }

        // Per-2s tick — iterates only cached ponds, evicts destroyed entries.
        private static void CheckCachedPonds()
        {
            for (int i = _pondCache.Count - 1; i >= 0; i--)
            {
                var card = _pondCache[i];
                if (card == null) // Unity destroyed-object check
                {
                    _pondCache.RemoveAt(i);
                    continue;
                }

                string uid = GetUID(card);
                if (uid == null) continue;

                bool isFilled  = uid == FilledID;
                bool isStocked = uid == StockedID;
                bool isWinter  = uid == WinterID;
                if (!isFilled && !isStocked && !isWinter) continue;

                // ── breeding gate (all pond variants including winter) ──
                GateBreedingGrowth(card, "SpecialDurability1"); // Pike
                GateBreedingGrowth(card, "SpecialDurability2"); // Perch
                GateBreedingGrowth(card, "SpecialDurability3"); // Minnow

                if (isWinter) continue; // winter: no Filled↔Stocked swap

                // ── read populations (decides whether to swap; TryTransform re-reads
                //    fresh values itself via CardUtil.TransformInPlacePreservingStats) ──
                float s1 = CardUtil.GetDurability(card, "SpecialDurability1");
                float s2 = CardUtil.GetDurability(card, "SpecialDurability2");
                float s3 = CardUtil.GetDurability(card, "SpecialDurability3");
                if (float.IsNaN(s1) || float.IsNaN(s2) || float.IsNaN(s3)) continue;

                float total = s1 + s2 + s3;

                // ── threshold swap ──
                if (isFilled && total > StockedThreshold)
                {
                    Logger?.LogDebug($"[FishpondPop] Filled→Stocked (pike={s1:F2} perch={s2:F2} minnow={s3:F2} total={total:F1})");
                    TryTransform(card, StockedID);
                }
                else if (isStocked && total < StockedThreshold)
                {
                    Logger?.LogDebug($"[FishpondPop] Stocked→Filled (pike={s1:F2} perch={s2:F2} minnow={s3:F2} total={total:F1})");
                    TryTransform(card, FilledID);
                }
            }
        }

        // ── breeding gate ─────────────────────────────────────────────────────────────

        /// <summary>
        /// A species can only breed if it has ≥ 2 individuals.  The JSON passive rate
        /// runs unconditionally; this method floors any fractional growth that accumulated
        /// during a poll cycle back to the integer baseline, effectively zeroing the rate
        /// for species below the breeding threshold.
        /// </summary>
        private static void GateBreedingGrowth(object card, string statName)
        {
            float cur = CardUtil.GetDurability(card, statName);
            if (float.IsNaN(cur) || cur >= 2f) return;          // ≥ 2 → breeding allowed
            float floored = (float)Math.Floor(cur);
            if (Math.Abs(cur - floored) < 0.00001f) return;     // already an integer, nothing to do
            CardUtil.SetDurability(card, statName, floored);
        }

        // ── transform ─────────────────────────────────────────────────────────────────

        // CardUtil.TransformInPlacePreservingStats formalizes exactly this sequence
        // (capture durability stats → CardModel swap + SetupCardSource/ResetCard reinit →
        // restore captured stats) — it replaces the hand-rolled GetFromID + CardModel
        // cache + SetupCardSource reflection that used to live here.
        private static void TryTransform(object card, string targetUID)
        {
            try
            {
                if (!CardUtil.TransformInPlacePreservingStats(card, targetUID))
                {
                    Logger?.LogError($"[FishpondPop] Transform to '{targetUID}' failed");
                    return;
                }

                Logger?.LogDebug($"[FishpondPop] transform complete → {targetUID}");
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[FishpondPop] TryTransform failed: {ex.InnerException?.Message ?? ex.Message}");
            }
        }

        // ── reflection helpers ────────────────────────────────────────────────────────

        /// <summary>
        /// Populates the CardModel property cache for this card's concrete type.
        /// Must be called before GetUID so the cache is warm.
        /// Mirrors the first half of ActionInterceptPatch.EnsureReflection.
        /// </summary>
        private static void EnsureCardReflection(object card)
        {
            if (card == null) return;
            Type cardType = card.GetType();
            if (_cardModelCache.ContainsKey(cardType)) return;

            try
            {
                var cmProp = cardType.GetProperty("CardModel", Flags);
                _cardModelCache[cardType] = cmProp; // cache even if null

                // Resolve the UniqueID field from the first card model we encounter
                if (_uidField == null && cmProp != null)
                {
                    var model = cmProp.GetValue(card);
                    if (model != null)
                        _uidField = model.GetType().GetField("UniqueID", Flags);
                }
            }
            catch (Exception ex)
            {
                Logger?.Log(LogLevel.Debug, $"[FishpondPop] EnsureCardReflection({card.GetType().Name}): {ex.Message}");
                _cardModelCache[cardType] = null;
            }
        }

        private static string GetUID(object card)
        {
            try
            {
                if (card == null) return null;

                // Direct path: card IS a CardData/UniqueIDScriptable
                if (card is UniqueIDScriptable s) return s.UniqueID;

                // Indirect path: read CardModel property (InGameCardBase descendants)
                _cardModelCache.TryGetValue(card.GetType(), out var cmProp);
                if (cmProp == null) return null;

                var model = cmProp.GetValue(card);
                if (model == null) return null;

                if (model is UniqueIDScriptable s2) return s2.UniqueID;

                // Fallback: field access (handles any UniqueIDScriptable subclass)
                return _uidField?.GetValue(model) as string;
            }
            catch { return null; }
        }

    }
}
