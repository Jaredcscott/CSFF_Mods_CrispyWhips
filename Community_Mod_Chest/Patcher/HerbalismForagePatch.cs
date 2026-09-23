using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CSFFModFramework.Api;
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
    ///
    /// CurrentDrop is an auto-property ({ get; private set; }, .decomp/CardsDropCollection.cs),
    /// never a field. Until 1.68.41 this patch looked it up with GetField, which returns null
    /// for a property, so it returned silently on every forage and never doubled anything
    /// (walkthrough T2.259). Members are now read through Reflect.TryGetMember (property, then
    /// field), and every exit that means "the game moved" warns once per cause.
    /// </summary>
    internal static class HerbalismForagePatch
    {
        private static bool _applied;
        private static MethodInfo _memberwiseClone;
        private static bool _announcedThisSession;
        private static bool _cloneErrorLogged;
        private static readonly HashSet<string> _warned = new HashSet<string>();

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

        // __result = CardsDropCollection (rolled copy), __0 = CardAction,
        // __3 = InGameNPCOrPlayer _User (a struct: Player flag + InGameNPC NPC)
        private static void SelectCardCollection_Postfix(object __result, object __0, object __3)
        {
            try
            {
                if (__result == null || __0 == null) return;

                string actionName = CardUtil.GetActionName(__0);
                if (actionName == null || actionName.IndexOf("Forage", StringComparison.OrdinalIgnoreCase) < 0) return;

                // The degree is the player's: an NPC's forage (the Professor fills his satchel
                // this way) must not double because the player graduated.
                if (IsNpcUser(__3)) return;

                if (!AcademyCourseService.HasCourse(AcademyCourseService.GradHerbalism)) return;

                if (!Reflect.TryGetMember(__result, "CurrentDrop", out var currentDrop))
                {
                    WarnOnce("CurrentDrop-missing", $"{__result.GetType().Name}.CurrentDrop not found - forage doubling inactive.");
                    return;
                }
                if (currentDrop is not IList drops)
                {
                    WarnOnce("CurrentDrop-not-list", $"{__result.GetType().Name}.CurrentDrop is {currentDrop?.GetType().Name ?? "null"}, not a list - forage doubling inactive.");
                    return;
                }
                if (drops.Count == 0) return;

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
            if (!Reflect.TryGetMember(endResult, "Card", out var card))
            {
                WarnOnce("Card-missing", $"{endResult.GetType().Name}.Card not found - forage doubling inactive.");
                return false;
            }
            if (card == null) return false; // a drop entry with no card: vanilla skips it too

            if (!Reflect.TryGetMember(card, "CardType", out var cardType) || cardType == null)
            {
                WarnOnce("CardType-missing", $"{card.GetType().Name}.CardType not found - forage doubling inactive.");
                return false;
            }
            return Convert.ToInt32(cardType) == CardTypeItem;
        }

        private static bool IsNpcUser(object user)
        {
            if (user == null) return false;
            if (Reflect.GetMember(user, "Player") is bool isPlayer && isPlayer) return false;
            return Reflect.GetMember(user, "NPC") is UnityEngine.Object npc && npc != null;
        }

        private static void WarnOnce(string cause, string message)
        {
            if (!_warned.Add(cause)) return;
            Plugin.Logger.LogWarning($"[HerbalismForagePatch] {message}");
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
