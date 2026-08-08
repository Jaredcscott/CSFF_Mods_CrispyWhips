using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CSFFModFramework.Util;
using HarmonyLib;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Herbalism Graduate effect: forage actions yield double.
    ///
    /// Postfixes GameManager.SelectCardCollection — the private method that returns the
    /// rolled CardsDropCollection copy consumed by ProduceCards. It sits ONLY on the
    /// action-execution path (ActionRoutine); the dismantle-hover preview calls
    /// GetCollectionDropsReport directly, so previews are untouched and the CLAUDE.md
    /// ban on prefixing WillProduceCards / GetCollectionDropsReport / CanUseCollection
    /// is respected (this is a postfix on a different method, filtered to Forage).
    ///
    /// When the player holds the Herbalism degree (Academy course, in-run perk), every
    /// non-null Item-type entry in the selected collection's CurrentDrop list is
    /// duplicated — one spawned card per entry, so the haul doubles exactly.
    /// </summary>
    internal static class HerbalismForagePatch
    {
        private static bool _applied;
        private static FieldInfo _currentDropField;   // CardsDropCollection.CurrentDrop (List<CardDropEndResult>)
        private static FieldInfo _endResultCardField; // CardDropEndResult.Card (CardData)
        private static FieldInfo _cardTypeField;      // CardData.CardType (CardTypes enum)
        private static MethodInfo _memberwiseClone;
        private static bool _announcedThisSession;
        private static bool _cloneErrorLogged;

        private const int CardTypeItem = 0;

        public static void Apply(Harmony harmony)
        {
            if (_applied) return;
            _applied = true;

            var gmType = CardUtil.FindGameType("GameManager");
            if (gmType == null)
            {
                Plugin.Logger.LogWarning("[HerbalismForagePatch] GameManager type not found — forage doubling inactive.");
                return;
            }

            var method = AccessTools.Method(gmType, "SelectCardCollection");
            if (method == null)
            {
                Plugin.Logger.LogWarning("[HerbalismForagePatch] GameManager.SelectCardCollection not found — forage doubling inactive.");
                return;
            }

            harmony.Patch(method, postfix: new HarmonyMethod(typeof(HerbalismForagePatch), nameof(SelectCardCollection_Postfix)));
            Plugin.Logger.LogDebug("[HerbalismForagePatch] applied.");
        }

        // __result = CardsDropCollection (rolled copy), __0 = CardAction
        private static void SelectCardCollection_Postfix(object __result, object __0)
        {
            try
            {
                if (__result == null || __0 == null) return;

                string actionName = CardUtil.GetActionName(__0);
                if (actionName == null || actionName.IndexOf("Forage", StringComparison.OrdinalIgnoreCase) < 0) return;

                if (!AcademyCourseService.HasCourse(AcademyCourseService.GradHerbalism)) return;

                _currentDropField ??= __result.GetType().GetField("CurrentDrop", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (_currentDropField?.GetValue(__result) is not IList drops || drops.Count == 0) return;

                var duplicates = new List<object>(drops.Count);
                int originalCount = drops.Count;
                for (int i = 0; i < originalCount; i++)
                {
                    var entry = drops[i];
                    if (entry == null) continue;
                    if (!IsItemDrop(entry)) continue;
                    duplicates.Add(CloneEntry(entry));
                }

                if (duplicates.Count == 0) return;
                foreach (var dup in duplicates)
                    drops.Add(dup);

                if (!_announcedThisSession)
                {
                    _announcedThisSession = true;
                    Plugin.Logger.LogInfo($"[HerbalismForagePatch] Herbalism degree active — doubled {duplicates.Count} foraged drop(s) ('{actionName}').");
                }
                else
                {
                    Plugin.Logger.LogDebug($"[HerbalismForagePatch] doubled {duplicates.Count} foraged drop(s) ('{actionName}').");
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[HerbalismForagePatch] postfix failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static bool IsItemDrop(object endResult)
        {
            _endResultCardField ??= endResult.GetType().GetField("Card", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var card = _endResultCardField?.GetValue(endResult);
            if (card == null) return false;

            _cardTypeField ??= card.GetType().GetField("CardType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var cardType = _cardTypeField?.GetValue(card);
            return cardType != null && Convert.ToInt32(cardType) == CardTypeItem;
        }

        // Shallow clone keeps the entry independent of its sibling in the list while
        // sharing the immutable CardData/rule references — same data a second vanilla
        // roll of the identical drop would carry.
        private static object CloneEntry(object entry)
        {
            _memberwiseClone ??= typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
            try
            {
                return _memberwiseClone != null ? _memberwiseClone.Invoke(entry, null) : entry;
            }
            catch (Exception ex)
            {
                if (!_cloneErrorLogged)
                {
                    _cloneErrorLogged = true;
                    Plugin.Logger.LogDebug($"[HerbalismForagePatch] CloneEntry fell back to shared descriptor: {ex.InnerException?.ToString() ?? ex.ToString()}");
                }
                return entry; // fallback: reuse the same descriptor — ProduceCards only reads it
            }
        }
    }
}
