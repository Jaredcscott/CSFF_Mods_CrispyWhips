using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using BepInEx.Logging;
using UnityEngine;
using CSFFModFramework.Api;

namespace Advanced_Copper_Tools.Patcher
{
    /// <summary>
    /// Lets ACT's copper tea kettle and copper cauldron sit on vanilla fires and heat there.
    ///
    /// Two injections per fire, both keyed on the two ACT cards themselves:
    ///   1. The containers are appended to the fire's InventoryFilter.AcceptedCards.
    ///      CardFilter.SupportsCard returns true for any AcceptedCards match (after the fire's
    ///      own NOT filters), so no tag injection is needed and none is done.
    ///   2. A CookingRecipe whose CompatibleCards are those containers is appended to the fire's
    ///      CookingRecipes, heating the container's contents. GetRecipeForCard returns the FIRST
    ///      match, so appending never overrides a vanilla recipe.
    ///
    /// What this patch must NEVER do to a vanilla fire (1.16.10, player report):
    ///   - write MaxWeightCapacity. Vanilla capacities (Campfire 600, Fireplace 1200, FirePit 2400,
    ///     Oven 1200) are the cooking ladder: a container fits where its full weight fits, exactly
    ///     like vanilla pots. Up to 1.16.9 this raised every listed fire to 2580, and gave the
    ///     SaunaStove (no inventory in vanilla) a 2580 inventory that accepted any card.
    ///   - widen AcceptedTags or match a recipe by a vanilla tag. Those tags are vanilla objects,
    ///     so every vanilla card carrying them would have been let in or heated too.
    /// Fires with no vanilla inventory (SaunaStove) are not in the list at all. The Oven gets the
    /// kettle only, and the copper cauldron goes into its NOTAcceptedCards, as vanilla's Oven names
    /// the ClayCauldron there. The Oven's one accepted tag is tag_Clay (read in-game 2026-09-26,
    /// export name Image_7993), which the copper cauldron does not carry, so today leaving it off
    /// AcceptedCards already keeps it out; the NOT entry states the rule and survives a tag change.
    /// The Hearths are slot inventories (2 slots, LegacyInventory while capacity is 0), which the
    /// old capacity write silently turned into weight inventories.
    /// </summary>
    public static class VanillaFireKettlePatch
    {
        private static ManualLogSource Logger => Plugin.Logger;

        private const string KettleUid = "advanced_copper_tools_copper_tea_kettle";
        private const string CauldronUid = "advanced_copper_tools_copper_cauldron";
        private static bool _loggedReady;

        private static readonly HashSet<string> LitFireGuids = new HashSet<string>
        {
            "63e4efe772bf6f649b30c893a0257ef0", // Campfire
            "7408fa4e89aa405468e5acb994c6f3a6", // CampfireExtinguished
            "f16aa437d91e2434ab7a4b7a8827a826", // FirePit
            "1939016de9415da4897e862cd1fb8410", // FirePitExtinguished
            "e50543ef8a7e7d543a42e199adeee963", // Fireplace
            "58523f8a86c4e0347b93d4a8ff192a13", // FireplaceExtinguished
            "e6cc2a4d002a46745abc87aac39680b6", // Hearth
            "760141d2251da2947b2d537c4b2eeacb", // HearthAwakened
            "eeef909ec09637145a5ff38003b48d8c", // Oven
        };

        // Vanilla's Oven InventoryFilter carries a NOT CardFilter for the ClayCauldron
        // (b1b5f3f02a453df42acaa396d61738f0): cauldrons do not go in the oven. The copper
        // cauldron follows the same rule.
        private static readonly HashSet<string> NoCauldronFireGuids = new HashSet<string>
        {
            "eeef909ec09637145a5ff38003b48d8c", // Oven
        };

        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static void InjectKettleSlots(IEnumerable allData)
        {
            try
            {
                object kettleCard = null;
                object cauldronCard = null;
                var fireCands = new List<(string uid, object card)>();

                foreach (var item in allData)
                {
                    if (item == null) continue;
                    var uid = AccessTools.Field(item.GetType(), "UniqueID")?.GetValue(item) as string;
                    if (string.IsNullOrEmpty(uid)) continue;

                    if (uid == KettleUid) kettleCard = item;
                    else if (uid == CauldronUid) cauldronCard = item;
                    else if (LitFireGuids.Contains(uid)) fireCands.Add((uid, item));
                }

                if (kettleCard == null && cauldronCard == null)
                {
                    Logger?.LogDebug("[KettleFire] ACT copper fire containers not present in AllData; injection skipped for this pass.");
                    return;
                }

                int patched = 0;
                foreach (var (uid, fire) in fireCands)
                {
                    var containers = new List<object>();
                    if (kettleCard != null) containers.Add(kettleCard);
                    if (cauldronCard != null && !NoCauldronFireGuids.Contains(uid)) containers.Add(cauldronCard);
                    if (containers.Count == 0) continue;

                    bool cardOk   = AddCardsToFilter(fire, "InventoryFilter", "AcceptedCards", containers, uid);
                    bool recipeOk = InjectHeatingRecipe(fire, containers, uid);
                    bool rejectOk = cauldronCard != null && NoCauldronFireGuids.Contains(uid)
                        && AddCardsToFilter(fire, "InventoryFilter", "NOTAcceptedCards", new List<object> { cauldronCard }, uid);
                    if (cardOk || recipeOk || rejectOk) patched++;
                }

                if (!_loggedReady)
                {
                    Logger?.LogDebug($"[KettleFire] Fire placement ready for ACT copper containers on {fireCands.Count} vanilla fire card(s); changed {patched} card(s) this pass.");
                    _loggedReady = true;
                }
                else
                {
                    Logger?.LogDebug($"[KettleFire] Patched {patched}/{fireCands.Count} vanilla fire cards.");
                }
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[KettleFire] InjectKettleSlots failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // -------------------------------------------------------------------------
        // Card-list injection into the fire's filter: names exactly the ACT containers,
        // in AcceptedCards (let in) or NOTAcceptedCards (keep out), nothing else
        // -------------------------------------------------------------------------

        static bool AddCardsToFilter(object fireCard, string filterMemberName, string arrayFieldName, IReadOnlyList<object> cards, string label)
        {
            try
            {
                var filterField = fireCard.GetType().GetField(filterMemberName, Flags);
                var filter = filterField != null
                    ? filterField.GetValue(fireCard)
                    : fireCard.GetType().GetProperty(filterMemberName, Flags)?.GetValue(fireCard, null);
                if (filter == null)
                {
                    Logger?.LogWarning($"[KettleFire] {label}: {filterMemberName} not found; cannot set {arrayFieldName} for ACT containers on this fire.");
                    return false;
                }

                var cardsField = filter.GetType().GetField(arrayFieldName, Flags);
                if (cardsField == null || !cardsField.FieldType.IsArray)
                {
                    Logger?.LogWarning($"[KettleFire] {label}: {filterMemberName}.{arrayFieldName} not found; cannot set it for ACT containers on this fire.");
                    return false;
                }

                var elemType = cardsField.FieldType.GetElementType();
                if (elemType == null) return false;

                var existing = cardsField.GetValue(filter) as Array;
                var merged = new List<object>();
                if (existing != null)
                    foreach (var card in existing) if (card != null && !merged.Contains(card)) merged.Add(card);

                bool changed = false;
                foreach (var card in cards)
                {
                    if (card != null && elemType.IsInstanceOfType(card) && !merged.Contains(card))
                    {
                        merged.Add(card);
                        changed = true;
                    }
                }

                if (!changed) return false;

                var newArr = Array.CreateInstance(elemType, merged.Count);
                for (int i = 0; i < merged.Count; i++) newArr.SetValue(merged[i], i);
                cardsField.SetValue(filter, newArr);

                // Write filter back: CardFilter is a struct, so the change lives only in this box until then.
                filterField?.SetValue(fireCard, filter);

                Logger?.LogDebug($"[KettleFire] {label}: added {cards.Count} ACT container(s) to {filterMemberName}.{arrayFieldName}.");
                return true;
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[KettleFire] {label}: AddCardsToFilter({arrayFieldName}) failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
                return false;
            }
        }

        // -------------------------------------------------------------------------
        // CookingRecipe injection: heats the ACT containers, matched by card only
        // -------------------------------------------------------------------------

        static bool InjectHeatingRecipe(object fireCard, IReadOnlyList<object> compatibleCards, string label)
        {
            try
            {
                var spec = new RecipeSpec
                {
                    CompatibleCards = compatibleCards,
                    CompatibleTags = Array.Empty<object>(),
                    ConditionsCard = 0, // no heat condition required (fire is always lit)
                    Duration = 1,
                    CookerModType = 0, // fire itself unchanged
                    IngredientModType = 1, // modify kettle in place
                    UsageChange = Vector2.zero,
                    SpoilageChange = Vector2.zero,
                    // Net heating rate: +200 (recipe) + (-100) (Cool Down passive) = +100/dtp
                    FuelChange = new Vector2(200f, 200f),
                };

                bool injected = RecipeInjector.InjectCookingRecipe(fireCard, spec, label);
                if (injected)
                    Logger?.LogDebug($"[KettleFire] Injected kettle heating recipe on fire {label}.");
                return injected;
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[KettleFire] {label}: InjectHeatingRecipe failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
                return false;
            }
        }
    }
}
