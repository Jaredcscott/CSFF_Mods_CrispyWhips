using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using BepInEx.Logging;
using UnityEngine;

namespace Advanced_Copper_Tools.Patcher
{
    /// <summary>
    /// Adds copper cooking containers to vanilla fire cards so they can be dragged onto
    /// campfires, fireplaces, fire pits, etc., and get heated once inside.
    ///
    /// Root cause of placement failure: vanilla fire InventoryFilter.AcceptedTags contains
    /// the game's compiled (obfuscated) CardTag SO objects (e.g. TextMeshProUGUI_6680).
    /// Our items use human-readable tag names (e.g. tag_HeatAbleAndBoilableLiquid) which
    /// the framework resolves to NEW runtime-created CardTag SOs — different objects from
    /// the vanilla ones.  The tag-match check therefore fails.
    ///
    /// Fix: after the framework resolves our items' CardTags, inject those same SO references
    /// into each fire's InventoryFilter.AcceptedTags, so the game's inclusion check succeeds.
    /// AcceptedCards is also populated as a belt-and-suspenders fallback.
    ///
    /// NOTE: Do NOT add InventorySlots to fires that have 0 slots — those use weight-based
    /// inventory mode (MaxWeightCapacity) and a null slot breaks placement.
    /// </summary>
    public static class VanillaFireKettlePatch
    {
        private static ManualLogSource Logger => Plugin.Logger;

        private const string KettleUid = "advanced_copper_tools_copper_tea_kettle";
        private const string CauldronUid = "advanced_copper_tools_copper_cauldron";
        private static bool _loggedReady;

        // Whitelist of tag names relevant to fire slot acceptance. Injecting all container
        // tags (tag_Metal, tag_Craftable, etc.) would let any matching vanilla item onto fires.
        private static readonly HashSet<string> FireSlotTagWhitelist = new HashSet<string>(StringComparer.Ordinal)
        {
            "tag_Cookable",
            "tag_WaterContainer",
            "tag_WaterContainerLarge",
            "tag_CookingContainer",
            "tag_CookingPot",
            "tag_StewContainer",
            "tag_ContainerOpenLarge",
            "tag_HeatAbleAndBoilableLiquid",
            "tag_Heatable",
        };

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
            "4ea0fc8cb98942e498ec7fa941a69211", // SaunaStove
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

                var containers = new List<object>();
                if (kettleCard != null) containers.Add(kettleCard);
                if (cauldronCard != null) containers.Add(cauldronCard);

                if (containers.Count == 0)
                {
                    Logger?.LogDebug("[KettleFire] ACT copper fire containers not present in AllData; injection skipped for this pass.");
                    return;
                }

                // Collect the resolved CardTag SOs from our container items.
                // These are the runtime objects the framework assigned when resolving our
                // CardTagsWarpData.  We inject them into each fire's AcceptedTags so the
                // game's tag-match check (item.CardTags ∩ fire.AcceptedTags) succeeds.
                var containerTags = CollectResolvedTags(containers);
                var containerTagIds = containerTags.ConvertAll(t => {
                    var uo = t as UnityEngine.Object;
                    return uo != null ? $"{uo.name}(id={uo.GetInstanceID()})" : "null";
                });
                Logger?.LogDebug($"[KettleFire] Diag: Collected {containerTags.Count} tag SO(s): [{string.Join(", ", containerTagIds)}]");

                // Log first fire's AcceptedTags with instance IDs for comparison
                if (fireCands.Count > 0)
                {
                    var (fuid, fcard) = fireCands[0];
                    var ff = fcard.GetType().GetField("InventoryFilter", Flags);
                    var filt = ff?.GetValue(fcard);
                    if (filt != null)
                    {
                        var atf = filt.GetType().GetField("AcceptedTags", Flags);
                        var at = atf?.GetValue(filt) as Array;
                        var atNames = new System.Text.StringBuilder();
                        if (at != null) foreach (var t in at)
                        {
                            var uo = t as UnityEngine.Object;
                            atNames.Append(uo != null ? $"{uo.name}(id={uo.GetInstanceID()})" : "null").Append(", ");
                        }
                        Logger?.LogDebug($"[KettleFire] Diag: {fuid} AcceptedTags ({at?.Length ?? 0}): [{atNames}]");
                        Logger?.LogDebug($"[KettleFire] Diag: InventoryFilter field={ff != null}, filter type={filt.GetType().Name}");

                        // Check TagFilters (NOT entries that could block our items)
                        var tff = filt.GetType().GetField("TagFilters", Flags);
                        var tf = tff?.GetValue(filt) as Array;
                        if (tf != null && tf.Length > 0)
                        {
                            var tfLog = new System.Text.StringBuilder();
                            foreach (var tfEntry in tf)
                            {
                                var tagField = tfEntry?.GetType().GetField("Tag", Flags);
                                var notField = tfEntry?.GetType().GetField("NOT", Flags);
                                var tagSO = tagField?.GetValue(tfEntry) as UnityEngine.Object;
                                bool isNot = notField != null && (bool)notField.GetValue(tfEntry);
                                tfLog.Append($"NOT={isNot} tag={tagSO?.name ?? "null"}(id={tagSO?.GetInstanceID()}), ");
                            }
                            Logger?.LogDebug($"[KettleFire] Diag: {fuid} TagFilters ({tf.Length}): [{tfLog}]");
                        }
                    }
                    else
                    {
                        var fp = fcard.GetType().GetProperty("InventoryFilter", Flags);
                        Logger?.LogDebug($"[KettleFire] Diag: {fuid} InventoryFilter field=null, property={fp?.Name ?? "null"}");
                    }
                }

                // Locate tag_HeatAbleAndBoilableLiquid for the CookingRecipe CompatibleTags
                var cardTagType = AccessTools.TypeByName("CardTag");
                UnityEngine.Object heatableTag = null;
                if (cardTagType != null)
                {
                    foreach (var t in Resources.FindObjectsOfTypeAll(cardTagType))
                    {
                        if (t != null && t.name == "tag_HeatAbleAndBoilableLiquid") { heatableTag = t; break; }
                    }
                }
                if (heatableTag == null)
                    Logger?.LogDebug("[KettleFire] tag_HeatAbleAndBoilableLiquid not found; CookingRecipe will use CompatibleCards only.");

                int patched = 0;
                foreach (var (uid, fire) in fireCands)
                {
                    bool tagOk    = AddAcceptedTagsToFilter(fire, "InventoryFilter", containerTags, uid);
                    bool cardOk   = AddAcceptedCardsToFilter(fire, "InventoryFilter", containers, uid);
                    bool weightOk = EnsureFireWeightCapacity(fire, containers, uid);
                    bool recipeOk = InjectHeatingRecipe(fire, containers, heatableTag, uid);
                    if (tagOk || cardOk || weightOk || recipeOk) patched++;
                }

                if (!_loggedReady)
                {
                    Logger?.LogDebug($"[KettleFire] Fire placement ready for {containers.Count} ACT copper container(s) on {fireCands.Count} vanilla fire card(s); changed {patched} card(s) this pass.");
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
        // Collect resolved CardTag SOs from container items
        // -------------------------------------------------------------------------

        static List<object> CollectResolvedTags(IReadOnlyList<object> containers)
        {
            var result = new List<object>();
            foreach (var card in containers)
            {
                if (card == null) continue;
                var tagsField = card.GetType().GetField("CardTags", Flags);
                var tags = tagsField?.GetValue(card) as Array;
                if (tags == null) continue;
                foreach (var t in tags)
                {
                    if (t == null || result.Contains(t)) continue;
                    var name = (t as UnityEngine.Object)?.name;
                    if (name != null && FireSlotTagWhitelist.Contains(name)) result.Add(t);
                }
            }
            return result;
        }

        // -------------------------------------------------------------------------
        // AcceptedTags injection — PRIMARY fix for the obfuscated-vs-human-readable mismatch
        // -------------------------------------------------------------------------

        static bool AddAcceptedTagsToFilter(object fireCard, string filterMemberName, IReadOnlyList<object> tagsToAdd, string label)
        {
            try
            {
                if (tagsToAdd.Count == 0) return false;

                var filterField = fireCard.GetType().GetField(filterMemberName, Flags);
                var filter = filterField != null
                    ? filterField.GetValue(fireCard)
                    : fireCard.GetType().GetProperty(filterMemberName, Flags)?.GetValue(fireCard, null);
                if (filter == null) return false;

                var acceptedTagsField = filter.GetType().GetField("AcceptedTags", Flags);
                if (acceptedTagsField == null || !acceptedTagsField.FieldType.IsArray) return false;

                var elemType = acceptedTagsField.FieldType.GetElementType();
                if (elemType == null) return false;

                var existing = acceptedTagsField.GetValue(filter) as Array;
                var merged = new List<object>();
                if (existing != null)
                    foreach (var t in existing) if (t != null && !merged.Contains(t)) merged.Add(t);

                bool changed = false;
                foreach (var tag in tagsToAdd)
                {
                    if (tag != null && elemType.IsInstanceOfType(tag) && !merged.Contains(tag))
                    {
                        merged.Add(tag);
                        changed = true;
                    }
                }

                if (!changed) return false;

                var newArr = Array.CreateInstance(elemType, merged.Count);
                for (int i = 0; i < merged.Count; i++) newArr.SetValue(merged[i], i);
                acceptedTagsField.SetValue(filter, newArr);

                // Write filter back to owner — required if InventoryFilter is a value type (struct).
                filterField?.SetValue(fireCard, filter);

                Logger?.LogDebug($"[KettleFire] {label}: added {changed} ACT tag(s) to {filterMemberName}.AcceptedTags.");
                return true;
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[KettleFire] {label}: AddAcceptedTagsToFilter failed: {ex.Message}");
                return false;
            }
        }

        // -------------------------------------------------------------------------
        // AcceptedCards injection — belt-and-suspenders fallback
        // -------------------------------------------------------------------------

        static bool AddAcceptedCardsToFilter(object fireCard, string filterMemberName, IReadOnlyList<object> acceptedCards, string label)
        {
            try
            {
                var filterField = fireCard.GetType().GetField(filterMemberName, Flags);
                var filter = filterField != null
                    ? filterField.GetValue(fireCard)
                    : fireCard.GetType().GetProperty(filterMemberName, Flags)?.GetValue(fireCard, null);
                if (filter == null) return false;

                var acceptedCardsField = filter.GetType().GetField("AcceptedCards", Flags);
                if (acceptedCardsField == null || !acceptedCardsField.FieldType.IsArray) return false;

                var elemType = acceptedCardsField.FieldType.GetElementType();
                if (elemType == null) return false;

                var existing = acceptedCardsField.GetValue(filter) as Array;
                var merged = new List<object>();
                if (existing != null)
                    foreach (var card in existing) if (card != null && !merged.Contains(card)) merged.Add(card);

                bool changed = false;
                foreach (var card in acceptedCards)
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
                acceptedCardsField.SetValue(filter, newArr);

                // Write filter back — handles struct InventoryFilter.
                filterField?.SetValue(fireCard, filter);

                Logger?.LogDebug($"[KettleFire] {label}: added ACT containers to {filterMemberName}.AcceptedCards.");
                return true;
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[KettleFire] {label}: AddAcceptedCardsToFilter failed: {ex.Message}");
                return false;
            }
        }

        static object GetMemberValue(object owner, string memberName)
        {
            if (owner == null) return null;
            var type = owner.GetType();
            var field = type.GetField(memberName, Flags);
            if (field != null) return field.GetValue(owner);
            var property = type.GetProperty(memberName, Flags);
            return property?.GetValue(owner, null);
        }

        static bool SetMemberValue(object owner, string memberName, object value)
        {
            if (owner == null) return false;
            var type = owner.GetType();
            var field = type.GetField(memberName, Flags);
            if (field != null)
            {
                field.SetValue(owner, value);
                return true;
            }

            var property = type.GetProperty(memberName, Flags);
            if (property != null && property.CanWrite)
            {
                property.SetValue(owner, value, null);
                return true;
            }

            return false;
        }

        static float GetFloatMember(object owner, string memberName)
        {
            try
            {
                var value = GetMemberValue(owner, memberName);
                return value == null ? 0f : Convert.ToSingle(value);
            }
            catch
            {
                return 0f;
            }
        }

        static bool EnsureFireWeightCapacity(object fireCard, IReadOnlyList<object> containers, string label)
        {
            try
            {
                float requiredCapacity = 0f;
                foreach (var container in containers)
                {
                    float objectWeight = GetFloatMember(container, "ObjectWeight");
                    float liquidCapacity = GetFloatMember(container, "MaxLiquidCapacity");
                    float contentWeightReduction = GetFloatMember(container, "ContentWeightReduction");
                    float effectiveFullWeight = objectWeight + Math.Max(0f, liquidCapacity + contentWeightReduction);
                    if (effectiveFullWeight > requiredCapacity) requiredCapacity = effectiveFullWeight;
                }

                if (requiredCapacity <= 0f) return false;

                float currentCapacity = GetFloatMember(fireCard, "MaxWeightCapacity");
                if (currentCapacity >= requiredCapacity) return false;

                if (!SetMemberValue(fireCard, "MaxWeightCapacity", requiredCapacity)) return false;

                Logger?.LogDebug($"[KettleFire] {label}: raised MaxWeightCapacity from {currentCapacity:0.#} to {requiredCapacity:0.#} for ACT copper containers.");
                return true;
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[KettleFire] {label}: EnsureFireWeightCapacity failed: {ex.Message}");
                return false;
            }
        }

        // -------------------------------------------------------------------------
        // CookingRecipe injection — heats items with tag_HeatAbleAndBoilableLiquid
        // -------------------------------------------------------------------------

        static bool InjectHeatingRecipe(object fireCard, IReadOnlyList<object> compatibleCards, UnityEngine.Object heatableTag, string label)
        {
            try
            {
                var recipesField = fireCard.GetType().GetField("CookingRecipes", Flags);
                if (recipesField == null)
                {
                    Logger?.LogError($"[KettleFire] {label}: CookingRecipes field not found.");
                    return false;
                }

                var recipeArr = recipesField.GetValue(fireCard) as Array;
                if (recipeArr == null || recipeArr.Length == 0)
                {
                    Logger?.LogDebug($"[KettleFire] {label}: CookingRecipes is null/empty; heating recipe skipped.");
                    return false;
                }

                var recipeType = recipeArr.GetType().GetElementType();

                // Idempotency: skip if our recipe already contains all target cards.
                var compatCardsField = recipeType.GetField("CompatibleCards", Flags);
                if (compatCardsField != null)
                {
                    foreach (var r in recipeArr)
                    {
                        var cc = compatCardsField.GetValue(r) as Array;
                        if (cc != null)
                        {
                            int matches = 0;
                            foreach (var target in compatibleCards)
                            {
                                foreach (var c in cc)
                                {
                                    if (c == target) { matches++; break; }
                                }
                            }
                            if (matches == compatibleCards.Count) return false;
                        }
                    }
                }

                // Clone first recipe as structural template
                var template = recipeArr.GetValue(0);
                var newRecipe = Activator.CreateInstance(recipeType);
                foreach (var fi in recipeType.GetFields(Flags))
                    fi.SetValue(newRecipe, fi.GetValue(template));

                // CompatibleCards = [ACT containers]
                if (compatCardsField != null)
                {
                    var elemType2 = compatCardsField.FieldType.GetElementType() ?? typeof(object);
                    var cards = new List<object>();
                    foreach (var card in compatibleCards)
                        if (card != null && elemType2.IsInstanceOfType(card)) cards.Add(card);

                    var arr = Array.CreateInstance(elemType2, cards.Count);
                    for (int i = 0; i < cards.Count; i++) arr.SetValue(cards[i], i);
                    compatCardsField.SetValue(newRecipe, arr);
                }

                // CompatibleTags = [tag_HeatAbleAndBoilableLiquid] if found
                var compatTagsField = recipeType.GetField("CompatibleTags", Flags);
                if (compatTagsField != null)
                {
                    if (heatableTag != null)
                    {
                        var tagElemType = compatTagsField.FieldType.GetElementType() ?? typeof(UnityEngine.Object);
                        var tagArr = Array.CreateInstance(tagElemType, 1);
                        tagArr.SetValue(heatableTag, 0);
                        compatTagsField.SetValue(newRecipe, tagArr);
                    }
                    else if (compatTagsField.FieldType.IsArray)
                    {
                        compatTagsField.SetValue(newRecipe,
                            Array.CreateInstance(compatTagsField.FieldType.GetElementType(), 0));
                    }
                }

                // No heat condition required (fire is always lit)
                recipeType.GetField("ConditionsCard", Flags)?.SetValue(newRecipe, 0);
                recipeType.GetField("Duration", Flags)?.SetValue(newRecipe, 1);

                // CookerChanges: ModType=0 (fire unchanged)
                var cookerField = recipeType.GetField("CookerChanges", Flags);
                if (cookerField != null)
                {
                    var cooker = cookerField.GetValue(newRecipe) ?? Activator.CreateInstance(cookerField.FieldType);
                    cooker.GetType().GetField("ModType", Flags)?.SetValue(cooker, 0);
                    cookerField.SetValue(newRecipe, cooker);
                }

                // IngredientChanges: ModType=1 (modify kettle), FuelChange=+200/dtp
                // Net heating rate: +200 (recipe) + (-100) (Cool Down passive) = +100/dtp
                var ingField = recipeType.GetField("IngredientChanges", Flags);
                if (ingField == null)
                {
                    Logger?.LogError($"[KettleFire] {label}: IngredientChanges field not found.");
                    return false;
                }

                var ing = ingField.GetValue(newRecipe) ?? Activator.CreateInstance(ingField.FieldType);
                var ingType = ing.GetType();
                var fuelChangeField = ingType.GetField("FuelChange", Flags);
                if (fuelChangeField == null)
                {
                    Logger?.LogError($"[KettleFire] {label}: FuelChange not found on IngredientChanges.");
                    return false;
                }

                var zero = new Vector2(0f, 0f);
                ingType.GetField("ModType",        Flags)?.SetValue(ing, 1);
                ingType.GetField("UsageChange",    Flags)?.SetValue(ing, zero);
                ingType.GetField("SpoilageChange", Flags)?.SetValue(ing, zero);
                fuelChangeField.SetValue(ing, new Vector2(200f, 200f));
                ingField.SetValue(newRecipe, ing);

                // Append recipe
                var newArr = Array.CreateInstance(recipeType, recipeArr.Length + 1);
                Array.Copy(recipeArr, newArr, recipeArr.Length);
                newArr.SetValue(newRecipe, recipeArr.Length);
                recipesField.SetValue(fireCard, newArr);

                Logger?.LogDebug($"[KettleFire] Injected kettle heating recipe on fire {label}.");
                return true;
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[KettleFire] {label}: InjectHeatingRecipe failed: {ex.Message}");
                return false;
            }
        }
    }
}
