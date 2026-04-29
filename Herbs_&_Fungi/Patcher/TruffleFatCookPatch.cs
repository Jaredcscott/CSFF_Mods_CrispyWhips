using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Herbs_And_Fungi.Patcher
{
    /// <summary>
    /// Drag-fat-onto-truffle cook flow.
    ///
    /// JSON side:
    ///   - SpecialDurability1 "Char" — MaxValue 3.0. Vanilla `tag_Cookable` recipe
    ///     (ClayBowl / CookingPot / Hearth) advances Special1 by 1 per daytime point
    ///     when the bowl is heated. Therefore: 3 dtp × 15 min/dtp = 45 min to burn.
    ///   - SpecialDurability4 "Fat" — 0–1 marker, touched only by our drag CI.
    ///     Special4 is intentionally chosen because no vanilla cooking recipe writes
    ///     to it on the truffle's tags (Special2 and Special3 are used by vanilla
    ///     bowl/hearth recipes and would auto-fill).
    ///   - A CardInteraction accepts Fat / FatChunk / ButterChunk / MilkButter being
    ///     dragged onto a slice; the butter is destroyed and Special4 is set to 1.
    ///   - Special1 OnFull (burn → CharredRemains) fires at MaxValue=3.0 → 45 min.
    ///
    /// Patch behavior:
    ///   When a slice has Char ≥ 1.0 (1 dtp = 15 min in-game) AND Fat ≥ 0.99,
    ///   swap it in place to a Cooked Truffle BEFORE the OnFull burn at Char=3.0
    ///   fires. The 0.333 normalized threshold = 1.0/3.0 absolute.
    ///   In-place transform uses the WDI/TeaStation CardModel-swap pattern so the
    ///   card stays in its container slot (no OnDestroy relocation).
    /// </summary>
    public static class TruffleFatCookPatch
    {
        private const string CutTruffleID    = "herbs_fungi_dried_truffle_cut";
        private const string CookedTruffleID = "herbs_fungi_cooked_truffle";

        // Char advances 1 per dtp. We fire at 1 dtp absolute (= 15 min in-game).
        // MaxValue=3.0 in JSON, so normalized threshold = 1.0/3.0.
        private const float CharPreemptThreshold = 0.333f;
        private const float FatRequiredThreshold = 0.99f;
        private const float TickInterval = 0.25f;

        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static float _nextCheck;

        // Type lookups
        private static Type _inGameCardBaseType;
        private static Type _cardDataType;
        private static Type _gmType;
        private static Type _uidScriptableType;
        private static MethodInfo _getFromIDMethod;
        private static bool _typeReflectionInit;

        // Per-runtime-type transform reflection (CardModel property / setter / backing field)
        private static Type _transformCardType;
        private static PropertyInfo _cardModelSetProp;
        private static MethodInfo _cardModelSetter;
        private static FieldInfo _cardModelBackingField;
        private static MethodInfo _cardResetMethod;
        private static MethodInfo _cardSetupMethod;

        private static ManualLogSource Logger => Plugin.Logger;

        public static void ApplyPatch(Harmony harmony)
        {
            try
            {
                var gmType = AccessTools.TypeByName("GameManager");
                if (gmType == null)
                {
                    Logger?.LogError("[TruffleFatCook] GameManager type not found");
                    return;
                }
                var updateMethod = AccessTools.Method(gmType, "Update");
                if (updateMethod == null)
                {
                    Logger?.LogError("[TruffleFatCook] GameManager.Update not found");
                    return;
                }
                harmony.Patch(updateMethod,
                    postfix: new HarmonyMethod(typeof(TruffleFatCookPatch), nameof(Update_Postfix)));
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[TruffleFatCook] ApplyPatch failed: {ex.Message}");
            }
        }

        private static void Update_Postfix()
        {
            if (Time.time < _nextCheck) return;
            _nextCheck = Time.time + TickInterval;

            try { ScanTruffles(); }
            catch (Exception ex) { Logger?.LogError($"[TruffleFatCook] tick error: {ex.Message}"); }
        }

        private static void ScanTruffles()
        {
            InitTypeReflection();
            if (_inGameCardBaseType == null) return;

            var allCards = UnityEngine.Object.FindObjectsOfType(_inGameCardBaseType);
            if (allCards == null || allCards.Length == 0) return;

            foreach (var card in allCards)
            {
                if (card == null) continue;
                if (GetUniqueId(card) != CutTruffleID) continue;

                float charStat = GetSpecial(card, "Special1") ?? 0f;
                if (charStat < CharPreemptThreshold) continue;

                float fatStat = GetSpecial(card, "Special4") ?? 0f;
                if (fatStat < FatRequiredThreshold) continue;

                Logger?.Log(LogLevel.Debug,
                    $"[TruffleFatCook] Pre-empting char (Char={charStat:F2}, Fat={fatStat:F2}) → CookedTruffle");
                TransformCardInPlace(card, CookedTruffleID);
            }
        }

        // ============================================================
        //  Reflection helpers
        // ============================================================
        private static void InitTypeReflection()
        {
            if (_typeReflectionInit) return;
            _typeReflectionInit = true;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (_inGameCardBaseType == null) _inGameCardBaseType = asm.GetType("InGameCardBase", false);
                if (_cardDataType       == null) _cardDataType       = asm.GetType("CardData", false);
                if (_gmType             == null) _gmType             = asm.GetType("GameManager", false);
                if (_uidScriptableType  == null) _uidScriptableType  = asm.GetType("UniqueIDScriptable", false);
            }

            if (_uidScriptableType != null && _cardDataType != null)
            {
                var generic = _uidScriptableType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "GetFromID" && m.IsGenericMethodDefinition);
                if (generic != null)
                    _getFromIDMethod = generic.MakeGenericMethod(_cardDataType);
            }
        }

        private static string GetUniqueId(object card)
        {
            if (card == null) return null;
            try
            {
                var cardModel = GetMemberValue(card, "CardModel");
                if (cardModel == null) return null;
                return GetMemberValue(cardModel, "UniqueID") as string;
            }
            catch { return null; }
        }

        /// <summary>
        /// Returns the normalized (0..1) value of the named special-durability stat
        /// on the runtime InGameCardBase. EA 0.62b exposes these directly as
        /// `CurrentSpecial1` / `CurrentSpecial1Percent` (and ...4) on the card —
        /// there is no `DurabilityStats` container. Tries the *Percent property
        /// first, then falls back to Current{N} / template MaxValue.
        /// </summary>
        private static float? GetSpecial(object card, string memberName)
        {
            try
            {
                // memberName is "Special1" or "Special4" — InGameCardBase has
                // CurrentSpecial1Percent / CurrentSpecial4Percent properties (0..1).
                var pct = GetMemberValue(card, "Current" + memberName + "Percent");
                if (pct != null) return ToFloat(pct);

                // Fallback: raw CurrentSpecial{N} divided by template MaxValue.
                var raw = GetMemberValue(card, "Current" + memberName);
                if (raw == null) return null;
                float current = ToFloat(raw);

                var cardModel = GetMemberValue(card, "CardModel");
                var statTemplate = cardModel != null
                    ? GetMemberValue(cardModel, memberName.Replace("Special", "SpecialDurability"))
                    : null;
                float max = statTemplate != null ? ToFloat(GetMemberValue(statTemplate, "MaxValue")) : 0f;
                if (max <= 0f) return current;
                return current / max;
            }
            catch { return null; }
        }

        private static float ToFloat(object o)
        {
            if (o is float f) return f;
            if (o is double d) return (float)d;
            if (o is int i) return i;
            return 0f;
        }

        private static object GetMemberValue(object target, string name)
        {
            if (target == null) return null;
            var t = target.GetType();
            var prop = t.GetProperty(name, Flags);
            if (prop != null && prop.CanRead) { try { return prop.GetValue(target, null); } catch { } }
            var field = t.GetField(name, Flags);
            if (field != null) { try { return field.GetValue(target); } catch { } }
            return null;
        }

        // ============================================================
        //  In-place CardModel swap (WDI / TeaStation pattern)
        // ============================================================
        private static void TransformCardInPlace(object card, string targetUniqueId)
        {
            if (card == null || string.IsNullOrEmpty(targetUniqueId)) return;
            try
            {
                if (_getFromIDMethod == null)
                {
                    Logger?.LogError("[TruffleFatCook] Transform: GetFromID reflection unavailable");
                    return;
                }

                var targetData = _getFromIDMethod.Invoke(null, new object[] { targetUniqueId });
                if (targetData == null)
                {
                    Logger?.LogError($"[TruffleFatCook] Transform: CardData not found for '{targetUniqueId}'");
                    return;
                }

                Type cardType = card.GetType();
                if (_transformCardType != cardType)
                {
                    _transformCardType    = cardType;
                    _cardModelSetProp     = cardType.GetProperty("CardModel", Flags);
                    _cardModelSetter      = _cardModelSetProp?.GetSetMethod(nonPublic: true);
                    _cardModelBackingField = ResolveBackingField(cardType, "CardModel");
                    _cardResetMethod      = cardType.GetMethod("ResetCard", Flags, null, Type.EmptyTypes, null);
                    _cardSetupMethod      = cardType.GetMethods(Flags)
                        .FirstOrDefault(m => m.Name == "SetupCardSource"
                            && m.GetParameters().Length >= 1
                            && _cardDataType != null
                            && m.GetParameters()[0].ParameterType.IsAssignableFrom(_cardDataType));
                    Logger?.Log(LogLevel.Debug,
                        $"[TruffleFatCook] Transform reflection for {cardType.Name}: " +
                        $"CardModelProp={(_cardModelSetProp != null)}, " +
                        $"Setter={(_cardModelSetter != null)}, " +
                        $"BackingField={(_cardModelBackingField != null)}, " +
                        $"SetupCardSource={(_cardSetupMethod != null)}, " +
                        $"ResetCard={(_cardResetMethod != null)}");
                }

                if (!TrySetCardModel(card, targetData))
                {
                    Logger?.LogError($"[TruffleFatCook] Transform: CardModel not settable on {cardType.Name}");
                    return;
                }

                if (_cardSetupMethod != null)
                {
                    var p = _cardSetupMethod.GetParameters();
                    var args = new object[p.Length];
                    args[0] = targetData;
                    for (int i = 1; i < p.Length; i++)
                    {
                        var pt = p[i].ParameterType;
                        args[i] = pt.IsValueType ? Activator.CreateInstance(pt) : null;
                    }
                    _cardSetupMethod.Invoke(card, args);
                }
                else if (_cardResetMethod != null)
                {
                    _cardResetMethod.Invoke(card, null);
                }

                Logger?.LogInfo($"[TruffleFatCook] Slices cooked → {targetUniqueId}");
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[TruffleFatCook] Transform failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static bool TrySetCardModel(object card, object targetData)
        {
            try { if (_cardModelSetProp != null && _cardModelSetProp.CanWrite) { _cardModelSetProp.SetValue(card, targetData); return true; } }
            catch (Exception ex) { Logger?.Log(LogLevel.Debug, $"[TruffleFatCook] prop.SetValue: {ex.Message}"); }

            try { if (_cardModelSetter != null) { _cardModelSetter.Invoke(card, new object[] { targetData }); return true; } }
            catch (Exception ex) { Logger?.Log(LogLevel.Debug, $"[TruffleFatCook] setter.Invoke: {ex.Message}"); }

            try { if (_cardModelBackingField != null) { _cardModelBackingField.SetValue(card, targetData); return true; } }
            catch (Exception ex) { Logger?.Log(LogLevel.Debug, $"[TruffleFatCook] backingField.SetValue: {ex.Message}"); }

            return false;
        }

        private static FieldInfo ResolveBackingField(Type type, string propName)
        {
            string target = $"<{propName}>k__BackingField";
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                var f = t.GetField(target, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (f != null) return f;
            }
            return null;
        }
    }
}
