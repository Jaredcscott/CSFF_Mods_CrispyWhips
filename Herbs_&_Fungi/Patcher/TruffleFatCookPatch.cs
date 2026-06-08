using System;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using CSFFModFramework.Util;
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
    ///   In-place transform uses CardUtil.TransformCardInPlace so the card stays
    ///   in its container slot (no OnDestroy relocation).
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

        private static float _nextCheck;

        // Type lookups
        private static Type _inGameCardBaseType;
        private static Type _cardDataType;
        private static Type _gmType;
        private static Type _uidScriptableType;
        private static MethodInfo _getFromIDMethod;
        private static bool _typeReflectionInit;

        // GameManager.AllCards access
        private static PropertyInfo _gmInstanceProp;
        private static PropertyInfo _allCardsProp;

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
            catch (System.Exception ex)
            {
                Logger?.LogError($"[TruffleFatCook] ApplyPatch failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void Update_Postfix()
        {
            if (Time.time < _nextCheck) return;
            _nextCheck = Time.time + TickInterval;

            try { ScanTruffles(); }
            catch (System.Exception ex) { Logger?.LogError($"[TruffleFatCook] tick error: {ex.Message}"); }
        }

        private static void ScanTruffles()
        {
            InitTypeReflection();
            _inGameCardBaseType ??= CardUtil.FindGameType("InGameCardBase");
            if (_inGameCardBaseType == null) return;

            var allCards = GetAllCardsFromGM();
            if (allCards == null)
            {
                // Fallback to scene scan only if AllCards is unavailable (e.g. before GM initialized)
                var found = UnityEngine.Object.FindObjectsOfType(_inGameCardBaseType);
                if (found == null || found.Length == 0) return;
                foreach (var c in found) ProcessCard(c);
                return;
            }

            foreach (var card in allCards)
                ProcessCard(card);
        }

        private static void ProcessCard(object card)
        {
            if (card == null) return;
            if (GetUniqueId(card) != CutTruffleID) return;

            float charStat = GetSpecial(card, "Special1") ?? 0f;
            if (charStat < CharPreemptThreshold) return;

            float fatStat = GetSpecial(card, "Special4") ?? 0f;
            if (fatStat < FatRequiredThreshold) return;

            Logger?.Log(LogLevel.Debug,
                $"[TruffleFatCook] Pre-empting char (Char={charStat:F2}, Fat={fatStat:F2}) → CookedTruffle");
            if (!CardUtil.TransformCardInPlace(card, CookedTruffleID))
                Logger?.LogError($"[TruffleFatCook] Transform failed for card {card}");
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

            if (_gmType != null && _gmInstanceProp == null)
            {
                // Instance is on MBSingleton<T> — need FlattenHierarchy or base-type walk
                _gmInstanceProp = _gmType.GetProperty("Instance",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy);
                if (_gmInstanceProp == null)
                {
                    for (var t = _gmType.BaseType; t != null && t != typeof(object); t = t.BaseType)
                    {
                        _gmInstanceProp = t.GetProperty("Instance",
                            BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy);
                        if (_gmInstanceProp != null) break;
                    }
                }
            }
        }

        private static System.Collections.IEnumerable GetAllCardsFromGM()
        {
            try
            {
                if (_gmInstanceProp == null) return null;
                var gm = _gmInstanceProp.GetValue(null, null);
                if (gm == null) return null;

                if (_allCardsProp == null)
                {
                    var gmRuntimeType = gm.GetType();
                    _allCardsProp = gmRuntimeType.GetProperty("AllCards",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
                    if (_allCardsProp == null)
                    {
                        for (var t = gmRuntimeType.BaseType; t != null && t != typeof(object); t = t.BaseType)
                        {
                            _allCardsProp = t.GetProperty("AllCards",
                                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (_allCardsProp != null) break;
                        }
                    }
                }

                if (_allCardsProp == null) return null;
                return _allCardsProp.GetValue(gm, null) as System.Collections.IEnumerable;
            }
            catch { return null; }
        }

        private static string GetUniqueId(object card)
        {
            if (card == null) return null;
            try
            {
                var cardModel = CardUtil.GetMemberValue(card, "CardModel");
                if (cardModel == null) return null;
                return CardUtil.GetMemberValue(cardModel, "UniqueID") as string;
            }
            catch { return null; }
        }


        /// <summary>
        /// Returns the normalized (0..1) value of the named special-durability stat.
        /// Tries the *Percent property first, then falls back to Current{N} / template MaxValue.
        /// </summary>
        private static float? GetSpecial(object card, string memberName)
        {
            try
            {
                var pct = CardUtil.GetMemberValue(card, "Current" + memberName + "Percent");
                if (pct != null) return CardUtil.ToFloat(pct);

                var raw = CardUtil.GetMemberValue(card, "Current" + memberName);
                if (raw == null) return null;
                float current = CardUtil.ToFloat(raw);

                var cardModel = CardUtil.GetMemberValue(card, "CardModel");
                var statTemplate = cardModel != null
                    ? CardUtil.GetMemberValue(cardModel, memberName.Replace("Special", "SpecialDurability"))
                    : null;
                float max = statTemplate != null
                    ? CardUtil.ToFloat(CardUtil.GetMemberValue(statTemplate, "MaxValue"))
                    : 0f;
                return max <= 0f ? current : current / max;
            }
            catch { return null; }
        }
    }
}
