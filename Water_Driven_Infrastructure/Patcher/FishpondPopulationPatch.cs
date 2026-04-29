using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

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
    ///      preserved across the swap via SetupCardSource + manual stat restore.
    ///
    /// Uses the same EnsureCardReflection / cache pattern as ActionInterceptPatch to avoid
    /// the silent-null failures that plagued the prior direct-GetProperty approach.
    /// </summary>
    public static class FishpondPopulationPatch
    {
        private const string FilledID   = "water_sawmill_fishpond_filled";
        private const string StockedID  = "water_sawmill_fishpond_stocked";
        private const string WinterID   = "water_sawmill_fishpond_winter";
        private const float  StockedThreshold   = 10f;
        private const float  PollIntervalSeconds = 2f;
        private const float  StartupDelaySeconds = 5f;

        private static ManualLogSource Logger => Plugin.Logger;
        private static readonly BindingFlags Flags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // ── reflection caches: mirror ActionInterceptPatch.EnsureReflection pattern ──
        private static Type _cardBaseType;
        private static readonly Dictionary<Type, PropertyInfo> _cardModelCache = new Dictionary<Type, PropertyInfo>();
        private static FieldInfo _uidField;

        // ── spawn reflection (GetFromID + CardData type) ──
        private static bool   _spawnInit;
        private static MethodInfo _getFromIDMethod;
        private static Type   _cardDataType;

        // ── setup (SetupCardSource) reflection cache ──
        private static readonly Dictionary<Type, MethodInfo> _setupCache = new Dictionary<Type, MethodInfo>();

        public static void ApplyPatch(Harmony harmony)
        {
            try
            {
                if (Plugin.Instance == null)
                {
                    Logger?.LogError("[FishpondPop] Plugin.Instance null — coroutine not started");
                    return;
                }
                Plugin.Instance.StartCoroutine(PollCoroutine());
                Logger?.LogInfo("[FishpondPop] poll coroutine started");
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[FishpondPop] ApplyPatch: {ex}");
            }
        }

        // ── poll loop ────────────────────────────────────────────────────────────────

        private static IEnumerator PollCoroutine()
        {
            yield return new WaitForSeconds(StartupDelaySeconds);
            var wait = new WaitForSeconds(PollIntervalSeconds);
            while (true)
            {
                try { CheckAllPonds(); }
                catch (Exception ex) { Logger?.LogError($"[FishpondPop] poll error: {ex.Message}"); }
                yield return wait;
            }
        }

        private static void CheckAllPonds()
        {
            if (_cardBaseType == null)
                _cardBaseType = AccessTools.TypeByName("InGameCardBase");
            if (_cardBaseType == null) return;

            var cards = UnityEngine.Object.FindObjectsOfType(_cardBaseType);
            if (cards == null || cards.Length == 0) return;

            foreach (var card in cards)
            {
                if (card == null) continue;

                // Warm the per-type cache (same first step as ActionInterceptPatch.EnsureReflection)
                EnsureCardReflection(card);

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

                // ── read populations ──
                float s1   = GetStat(card, "SpecialDurability1");
                float s2   = GetStat(card, "SpecialDurability2");
                float s3   = GetStat(card, "SpecialDurability3");
                float spo  = GetStat(card, "SpoilageTime");      // Sturgeon
                float use  = GetStat(card, "UsageDurability");   // Trout
                float fuel = GetStat(card, "FuelCapacity");      // Char
                float prog = GetStat(card, "Progress");

                if (float.IsNaN(s1) || float.IsNaN(s2) || float.IsNaN(s3)) continue;
                if (float.IsNaN(spo))  spo  = 0f;
                if (float.IsNaN(use))  use  = 0f;
                if (float.IsNaN(fuel)) fuel = 0f;
                if (float.IsNaN(prog)) prog = 0f;

                float total = s1 + s2 + s3;

                // ── threshold swap ──
                if (isFilled && total > StockedThreshold)
                {
                    Logger?.LogInfo($"[FishpondPop] Filled→Stocked (pike={s1:F2} perch={s2:F2} minnow={s3:F2} total={total:F1})");
                    TryTransform(card, StockedID, s1, s2, s3, spo, use, fuel, prog);
                }
                else if (isStocked && total < StockedThreshold)
                {
                    Logger?.LogInfo($"[FishpondPop] Stocked→Filled (pike={s1:F2} perch={s2:F2} minnow={s3:F2} total={total:F1})");
                    TryTransform(card, FilledID, s1, s2, s3, spo, use, fuel, prog);
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
            float cur = GetStat(card, statName);
            if (float.IsNaN(cur) || cur >= 2f) return;          // ≥ 2 → breeding allowed
            float floored = (float)Math.Floor(cur);
            if (Math.Abs(cur - floored) < 0.00001f) return;     // already an integer, nothing to do
            SetStat(card, statName, floored);
        }

        // ── transform ─────────────────────────────────────────────────────────────────

        private static void TryTransform(object card, string targetUID,
                                         float s1, float s2, float s3,
                                         float spo, float use, float fuel, float prog)
        {
            try
            {
                EnsureSpawnReflection();
                if (_getFromIDMethod == null)
                {
                    Logger?.LogError("[FishpondPop] GetFromID unavailable — cannot transform");
                    return;
                }

                var targetData = _getFromIDMethod.Invoke(null, new object[] { targetUID });
                if (targetData == null)
                {
                    Logger?.LogError($"[FishpondPop] CardData '{targetUID}' not found");
                    return;
                }

                Type cardType = card.GetType();

                // Swap CardModel — use cached property (populated by EnsureCardReflection)
                _cardModelCache.TryGetValue(cardType, out var cmProp);
                if (cmProp == null)
                {
                    Logger?.LogError($"[FishpondPop] CardModel prop not cached for {cardType.Name}");
                    return;
                }

                if (!TrySetCardModel(card, cardType, cmProp, targetData))
                {
                    Logger?.LogError($"[FishpondPop] CardModel not writable on {cardType.Name}");
                    return;
                }

                // Re-initialise derived state (sprite, tags, durability defaults) from new model
                if (!_setupCache.TryGetValue(cardType, out var setupMethod))
                {
                    setupMethod = cardType.GetMethods(Flags)
                        .FirstOrDefault(m => m.Name == "SetupCardSource"
                            && m.GetParameters().Length >= 1
                            && _cardDataType != null
                            && m.GetParameters()[0].ParameterType.IsAssignableFrom(_cardDataType));
                    _setupCache[cardType] = setupMethod; // cache null too — avoids repeated scan
                }

                if (setupMethod != null)
                {
                    var parms = setupMethod.GetParameters();
                    var args  = new object[parms.Length];
                    args[0]   = targetData;
                    for (int i = 1; i < parms.Length; i++)
                    {
                        var pt = parms[i].ParameterType;
                        args[i] = pt.IsValueType ? Activator.CreateInstance(pt) : null;
                    }
                    setupMethod.Invoke(card, args);
                }
                else
                {
                    cardType.GetMethod("ResetCard", Flags, null, Type.EmptyTypes, null)?.Invoke(card, null);
                }

                // Restore per-species populations (SetupCardSource reset them to JSON FloatValue defaults)
                SetStat(card, "SpecialDurability1", s1);
                SetStat(card, "SpecialDurability2", s2);
                SetStat(card, "SpecialDurability3", s3);
                SetStat(card, "SpoilageTime",       spo);
                SetStat(card, "UsageDurability",    use);
                SetStat(card, "FuelCapacity",       fuel);
                SetStat(card, "Progress",           prog);

                Logger?.LogInfo($"[FishpondPop] transform complete → {targetUID}");
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[FishpondPop] TryTransform failed: {ex.InnerException?.Message ?? ex.Message}");
            }
        }

        // ── reflection helpers ────────────────────────────────────────────────────────

        /// <summary>
        /// Populates the CardModel property cache for this card's concrete type.
        /// Must be called before GetUID/GetStat/SetStat so the cache is warm.
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

        private static float GetStat(object card, string statName)
        {
            try
            {
                var cardType = card.GetType();

                // EA 0.62b: InGameCardBase exposes raw current values as flat properties.
                var directName = MapStatToRuntimeMember(statName);
                if (directName != null)
                {
                    var dp = cardType.GetProperty(directName, Flags);
                    if (dp != null && dp.CanRead) return Convert.ToSingle(dp.GetValue(card));
                    var df = cardType.GetField(directName, Flags);
                    if (df != null) return Convert.ToSingle(df.GetValue(card));
                }

                var sf = cardType.GetField("DurabilityStats", Flags)
                      ?? cardType.GetField("CardDurabilities", Flags);
                if (sf == null) return float.NaN;
                var stats = sf.GetValue(card);
                if (stats == null) return float.NaN;
                var durProp = stats.GetType().GetProperty(statName, Flags);
                if (durProp == null) return float.NaN;
                var dur = durProp.GetValue(stats);
                if (dur == null) return float.NaN;
                var durType = dur.GetType();
                var cvProp = durType.GetProperty("CurrentValue", Flags)
                          ?? durType.GetProperty("FloatValue",   Flags);
                if (cvProp != null) return Convert.ToSingle(cvProp.GetValue(dur));
                var fv = durType.GetField("FloatValue", Flags)
                      ?? durType.GetField("CurrentValue", Flags);
                return fv != null ? Convert.ToSingle(fv.GetValue(dur)) : float.NaN;
            }
            catch { return float.NaN; }
        }

        private static bool SetStat(object card, string statName, float val)
        {
            try
            {
                var cardType = card.GetType();

                // EA 0.62b: InGameCardBase exposes raw current values as flat properties.
                var directName = MapStatToRuntimeMember(statName);
                if (directName != null)
                {
                    var dp = cardType.GetProperty(directName, Flags);
                    if (dp != null && dp.CanWrite) { dp.SetValue(card, val); return true; }
                    if (dp != null)
                    {
                        var setter = dp.GetSetMethod(nonPublic: true);
                        if (setter != null) { setter.Invoke(card, new object[] { val }); return true; }
                    }
                    var df = cardType.GetField(directName, Flags);
                    if (df != null) { df.SetValue(card, val); return true; }
                }

                var sf = cardType.GetField("DurabilityStats", Flags)
                      ?? cardType.GetField("CardDurabilities", Flags);
                if (sf == null) return false;
                var stats = sf.GetValue(card);
                if (stats == null) return false;
                var durProp = stats.GetType().GetProperty(statName, Flags);
                if (durProp == null) return false;
                var dur = durProp.GetValue(stats);
                if (dur == null) return false;
                var durType = dur.GetType();

                var cvProp = durType.GetProperty("CurrentValue", Flags)
                          ?? durType.GetProperty("FloatValue",   Flags);
                if (cvProp != null)
                {
                    if (cvProp.CanWrite)
                    {
                        cvProp.SetValue(dur, val);
                    }
                    else
                    {
                        var setter = cvProp.GetSetMethod(nonPublic: true);
                        if (setter != null)
                        {
                            setter.Invoke(dur, new object[] { val });
                        }
                        else
                        {
                            var t  = durType;
                            FieldInfo bf = null;
                            while (t != null && bf == null)
                            {
                                bf = t.GetField($"<{cvProp.Name}>k__BackingField",
                                    BindingFlags.Instance | BindingFlags.NonPublic);
                                t = t.BaseType;
                            }
                            if (bf == null) return false;
                            bf.SetValue(dur, val);
                        }
                    }
                }
                else
                {
                    var fv = durType.GetField("FloatValue", Flags)
                          ?? durType.GetField("CurrentValue", Flags);
                    if (fv == null) return false;
                    fv.SetValue(dur, val);
                }

                if (dur.GetType().IsValueType)   durProp.SetValue(stats, dur);
                if (stats.GetType().IsValueType) sf.SetValue(card, stats);
                return true;
            }
            catch { return false; }
        }

        // EA 0.62b: InGameCardBase exposes raw current durability values as flat
        // properties/fields, not nested under a DurabilityStats container.
        private static string MapStatToRuntimeMember(string statName)
        {
            switch (statName)
            {
                case "Progress":           return "CurrentProgress";
                case "SpecialDurability1": return "CurrentSpecial1";
                case "SpecialDurability2": return "CurrentSpecial2";
                case "SpecialDurability3": return "CurrentSpecial3";
                case "SpecialDurability4": return "CurrentSpecial4";
                case "FuelCapacity":       return "CurrentFuel";
                case "UsageDurability":    return "CurrentUsage";
                case "SpoilageTime":       return "CurrentSpoilage";
                default:                   return null;
            }
        }

        private static void EnsureSpawnReflection()
        {
            if (_spawnInit && _getFromIDMethod != null) return;
            _spawnInit = true;
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (_cardDataType == null) _cardDataType = asm.GetType("CardData", false);
                }

                Type uidType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    uidType = asm.GetType("UniqueIDScriptable", false);
                    if (uidType != null) break;
                }

                if (uidType != null && _cardDataType != null)
                {
                    var generic = uidType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(m => m.Name == "GetFromID" && m.IsGenericMethodDefinition);
                    if (generic != null)
                        _getFromIDMethod = generic.MakeGenericMethod(_cardDataType);
                    else
                        Logger?.LogError("[FishpondPop] GetFromID generic method not found on UniqueIDScriptable");
                }
                else
                {
                    Logger?.LogError($"[FishpondPop] EnsureSpawnReflection: uidType={uidType != null} cdType={_cardDataType != null}");
                }

                if (_getFromIDMethod == null)
                    Logger?.LogError("[FishpondPop] GetFromID resolution failed");
                else
                    Logger?.LogInfo("[FishpondPop] spawn reflection ready");
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[FishpondPop] EnsureSpawnReflection: {ex.Message}");
            }
        }

        // ── CardModel writer with backing-field fallback (matches TeaStationPatch) ──
        private static readonly Dictionary<Type, FieldInfo> _cardModelBackingCache = new Dictionary<Type, FieldInfo>();

        private static bool TrySetCardModel(object card, Type cardType, PropertyInfo cmProp, object targetData)
        {
            // Path 1 — public/writable property
            try
            {
                if (cmProp != null && cmProp.CanWrite)
                {
                    cmProp.SetValue(card, targetData);
                    return true;
                }
            }
            catch (Exception ex) { Logger?.Log(LogLevel.Debug, $"[FishpondPop] prop.SetValue failed: {ex.Message}"); }

            // Path 2 — non-public setter
            try
            {
                var setter = cmProp?.GetSetMethod(nonPublic: true);
                if (setter != null)
                {
                    setter.Invoke(card, new[] { targetData });
                    return true;
                }
            }
            catch (Exception ex) { Logger?.Log(LogLevel.Debug, $"[FishpondPop] setter.Invoke failed: {ex.Message}"); }

            // Path 3 — auto-property backing field <CardModel>k__BackingField (walk base types)
            try
            {
                if (!_cardModelBackingCache.TryGetValue(cardType, out var bf))
                {
                    const string target = "<CardModel>k__BackingField";
                    for (var t = cardType; t != null && t != typeof(object); t = t.BaseType)
                    {
                        bf = t.GetField(target, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                        if (bf != null) break;
                    }
                    _cardModelBackingCache[cardType] = bf;
                }
                if (bf != null)
                {
                    bf.SetValue(card, targetData);
                    return true;
                }
            }
            catch (Exception ex) { Logger?.Log(LogLevel.Debug, $"[FishpondPop] backingField.SetValue failed: {ex.Message}"); }

            return false;
        }
    }
}
