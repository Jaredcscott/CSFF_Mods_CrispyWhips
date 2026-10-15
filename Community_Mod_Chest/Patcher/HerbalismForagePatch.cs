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
    /// Herbalism Graduate effect: herbs and mushrooms from a forage come up a quarter more often.
    ///
    /// Vanilla balance pass (1.69.17, plan Part II row 3.3.1, owner tile 12 "+25% on herbs and mushrooms only"):
    /// until 1.69.16 every Item drop of every forage was duplicated (a flat x2 haul, stacking with every
    /// injected drop). Now each herb or mushroom drop gets ONE extra copy on a 25% roll, so the expected haul of
    /// those cards rises by a quarter and nothing else changes. Vanilla has no herb tag (the export's tags are
    /// obfuscated), so the predicate is <see cref="HerbUids"/> (vanilla herbs and mushrooms plus CMC's two rare
    /// herbs) or a card tagged with one of the mod tags in <see cref="HerbTagNames"/> (Herbs & Fungi's cards).
    ///
    /// Postfixes GameManager.SelectCardCollection — the private method that returns the
    /// rolled CardsDropCollection copy consumed by ProduceCards. It sits ONLY on the
    /// action-execution path (ActionRoutine); the dismantle-hover preview calls
    /// GetCollectionDropsReport directly, so previews are untouched and the CLAUDE.md
    /// ban on prefixing WillProduceCards / GetCollectionDropsReport / CanUseCollection
    /// is respected (this is a postfix on a different method, filtered to Forage).
    ///
    /// When the player holds the Herbalism degree (Academy course, in-run perk), every
    /// non-null herb or mushroom entry in the selected collection's CurrentDrop list is
    /// duplicated on a 25% roll, one roll per entry.
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

        /// <summary>The chance each herb or mushroom drop is duplicated (expected +25% on those cards).</summary>
        internal const float HerbBonusChance = 0.25f;

        /// <summary>Vanilla forage herbs and mushrooms (UIDs read from the EA 0.69c export on 2026-10-14) and CMC's
        /// two rare herbs.</summary>
        internal static readonly HashSet<string> HerbUids = new(StringComparer.Ordinal)
        {
            "283c5cf6e29e618479a4e768acc4a639", // Fresh Appleweed
            "5ad63f32ed767c64190a64d79841d023", // Fireroot
            "ac0e762e7e085914c8b4751a8485cb65", // Frostleaf
            "a1da115fe6aad774683492f7317850aa", // Fresh Fairyweed
            "322e42ea161bf2e4bb94747882d1285a", // Healer's Moss
            "022d83d74a3b5df4e9f50e70d216d4c0", // Fresh Heather Flowers
            "22bcbfab690575846ab4bb3b81da3635", // Bog Myrtle Leaves
            "ecff0e5482448a54e9e54c4f788286ef", // Wild Garlic
            "ffbc6fdc5dc0eec43b100d0feb63b70d", // Fresh Wolfsbane
            "a07b58829bd83d441ab96b4d33197aa6", // Forest Caps
            "c324f662484d721469d7565b7bfb3e65", // Fly Agaric
            "33a8fa532b48ca040a368f63c0cb68ff", // Bracket Fungus
            "e41ab416339b3c9459a37b4942f8f57b", // Spirit Mushrooms
            "cmcSilverwortBlossom",
            "cmcHollowSage",
        };

        /// <summary>Mod tags that mark a herb or a mushroom (Herbs & Fungi); vanilla carries neither.</summary>
        internal static readonly string[] HerbTagNames = { "tag_Herb", "tag_Fungus" };

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
                    if (!IsHerbDrop(entry)) continue;
                    if (UnityEngine.Random.value >= HerbBonusChance) continue;
                    duplicates.Add(CloneEntry(entry));
                }

                if (duplicates.Count == 0) return;
                foreach (var dup in duplicates)
                    drops.Add(dup);

                if (!_announcedThisSession)
                {
                    _announcedThisSession = true;
                    Plugin.Logger.LogInfo($"[HerbalismForagePatch] Herbalism degree active: {duplicates.Count} extra herb or mushroom drop(s) ('{actionName}').");
                }
                else
                {
                    Plugin.Logger.LogDebug($"[HerbalismForagePatch] {duplicates.Count} extra herb or mushroom drop(s) ('{actionName}').");
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[HerbalismForagePatch] postfix failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        /// <summary>True for an Item drop whose card is a herb or a mushroom (<see cref="HerbUids"/> or a
        /// <see cref="HerbTagNames"/> tag).</summary>
        private static bool IsHerbDrop(object endResult)
        {
            if (!IsItemDrop(endResult)) return false;
            if (!Reflect.TryGetMember(endResult, "Card", out var card) || card == null) return false;
            string uid = CardUtil.GetCardUniqueId(card) ?? Reflect.GetMember(card, "UniqueID") as string;
            if (uid != null && HerbUids.Contains(uid)) return true;
            if (Reflect.GetMember(card, "CardTags") is IEnumerable tags)
            {
                foreach (var tag in tags)
                {
                    if (tag is not UnityEngine.Object so || so == null) continue;
                    foreach (var name in HerbTagNames)
                        if (string.Equals(so.name, name, StringComparison.Ordinal)) return true;
                }
            }
            return false;
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
