using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CSFFModFramework.Data;
using CSFFModFramework.Discovery;
using CSFFModFramework.Util;
using HarmonyLib;
using UnityEngine;

namespace CSFFModFramework.Injection;

/// <summary>
/// Reads SmeltingRecipes.json from each mod and injects custom CookingRecipes
/// into the vanilla Forge and Furnace so mod copper items return exact nugget counts.
/// Clones the vanilla "Smelt Small Tool" recipe as a template to inherit all conditions.
/// </summary>
internal static class SmeltingRecipeInjector
{
    private const string FORGE_UID = "0e4fa8919542c6c44a464c3fba469661";
    private const string FURNACE_UID = "984bad0c8931f3545bef58171f9bf252";
    private const string NUGGET_UID = "4b0f4937a5ecb90499428c8c10288afc";

    private static readonly BindingFlags BF =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static void InjectAll(IEnumerable allData, List<ModManifest> mods)
    {
        // 1. Collect all smelting recipe entries from mod JSON files
        var entries = new List<SmeltEntry>();
        foreach (var mod in mods)
        {
            var path = Path.Combine(mod.DirectoryPath, "SmeltingRecipes.json");
            if (!File.Exists(path)) continue;
            try
            {
                var json = File.ReadAllText(path);
                var parsed = MiniJson.Parse(json);
                if (parsed is not List<object> arr) continue;

                int modCount = 0;
                foreach (var item in arr)
                {
                    if (item is not Dictionary<string, object> dict) continue;
                    var entry = new SmeltEntry
                    {
                        ItemUid = dict.TryGetValue("Item", out var v1) ? v1 as string : null,
                        NuggetReturn = dict.TryGetValue("NuggetReturn", out var v2) ? ToInt(v2) : 0,
                        Duration = dict.TryGetValue("Duration", out var v3) ? ToInt(v3) : 32,
                    };
                    if (!string.IsNullOrEmpty(entry.ItemUid) && entry.NuggetReturn > 0)
                    {
                        entries.Add(entry);
                        modCount++;
                    }
                }
                Log.Info($"SmeltingRecipeInjector: loaded {modCount} entries from {mod.Name}");
            }
            catch (Exception ex)
            {
                Log.Warn($"SmeltingRecipeInjector: error reading {path}: {ex.Message}");
            }
        }

        if (entries.Count == 0) return;

        // 2. Find the copper nugget CardData
        var nugget = UniqueIDScriptable.GetFromID<CardData>(NUGGET_UID);
        if (nugget == null)
        {
            Log.Error("SmeltingRecipeInjector: MetalNugget not found in game data");
            return;
        }

        // 3. Build a lookup of all cards by UniqueID (AllData + game's AllUniqueObjects fallback)
        var cardLookup = new Dictionary<string, CardData>(StringComparer.OrdinalIgnoreCase);
        foreach (var obj in allData)
        {
            if (obj is CardData cd && !string.IsNullOrEmpty(cd.UniqueID))
                cardLookup[cd.UniqueID] = cd;
        }
        // Fallback: some mod objects may register in AllUniqueObjects but not yet in AllData
        var allUniqueObjects = GameRegistry.AllUniqueObjects;
        if (allUniqueObjects != null)
        {
            foreach (DictionaryEntry entry in allUniqueObjects)
            {
                if (entry.Value is CardData cd2
                    && !string.IsNullOrEmpty(cd2.UniqueID)
                    && !cardLookup.ContainsKey(cd2.UniqueID))
                    cardLookup[cd2.UniqueID] = cd2;
            }
        }

        // 4. Find forge and furnace
        var forgeCard = UniqueIDScriptable.GetFromID<CardData>(FORGE_UID);
        var furnaceCard = UniqueIDScriptable.GetFromID<CardData>(FURNACE_UID);

        // Also find WDI custom forges
        var customForges = new List<CardData>();
        foreach (var obj in allData)
        {
            if (obj is CardData cd && cd != forgeCard && cd != furnaceCard
                && !string.IsNullOrEmpty(cd.UniqueID)
                && cd.UniqueID.ToLowerInvariant().Contains("forge"))
            {
                var rf = AccessTools.Field(cd.GetType(), "CookingRecipes");
                if (rf != null && rf.GetValue(cd) is Array a && a.Length > 0)
                    customForges.Add(cd);
            }
        }

        var targets = new List<CardData>();
        if (forgeCard != null) targets.Add(forgeCard);
        if (furnaceCard != null) targets.Add(furnaceCard);
        targets.AddRange(customForges);

        if (targets.Count == 0)
        {
            Log.Error("SmeltingRecipeInjector: no forge/furnace found");
            return;
        }

        // 5. Get a vanilla smelting recipe as TEMPLATE (clone from it)
        object templateRecipe = null;
        Type cookingRecipeType = null;
        FieldInfo recipesFieldInfo = null;

        if (forgeCard != null)
        {
            recipesFieldInfo = AccessTools.Field(forgeCard.GetType(), "CookingRecipes");
            var existing = recipesFieldInfo?.GetValue(forgeCard) as Array;
            if (existing != null)
            {
                cookingRecipeType = existing.GetType().GetElementType();
                Log.Debug($"SmeltingRecipeInjector: CookingRecipe type = {cookingRecipeType.FullName}, Forge has {existing.Length} recipes");

                // Log available fields on CookingRecipe type
                var ccField = AccessTools.Field(cookingRecipeType, "CompatibleCards");
                var ctField = AccessTools.Field(cookingRecipeType, "CompatibleTags");
                Log.Debug($"SmeltingRecipeInjector: CompatibleCards field = {(ccField != null ? ccField.FieldType.FullName : "NOT FOUND")}");
                Log.Debug($"SmeltingRecipeInjector: CompatibleTags field = {(ctField != null ? ctField.FieldType.FullName : "NOT FOUND")}");

                // Find "Smelt Small Tool" or "Smelt Large Tool" (recipes with CompatibleTags containing copper tags)
                var compatTagsField = AccessTools.Field(cookingRecipeType, "CompatibleTags");
                for (int i = 0; i < existing.Length; i++)
                {
                    var r = existing.GetValue(i);
                    if (r == null) continue;
                    var tags = compatTagsField?.GetValue(r) as Array;
                    var cards = ccField?.GetValue(r) as Array;
                    Log.Debug($"SmeltingRecipeInjector: Forge recipe[{i}] CompatibleTags={tags?.Length ?? -1}, CompatibleCards={cards?.Length ?? -1}");
                    if (tags != null && tags.Length > 0)
                    {
                        templateRecipe = r;
                        Log.Debug($"SmeltingRecipeInjector: using recipe[{i}] as template (has {tags.Length} tags)");
                        break;
                    }
                }
                // Fallback: use any recipe
                if (templateRecipe == null && existing.Length > 0)
                {
                    templateRecipe = existing.GetValue(0);
                    Log.Warn("SmeltingRecipeInjector: FALLBACK — using recipe[0] as template (no recipe with tags found)");
                }
            }
        }

        if (templateRecipe == null || cookingRecipeType == null)
        {
            Log.Error("SmeltingRecipeInjector: no template recipe found on forge");
            return;
        }

        // 6. Create custom recipes by cloning the template
        var newRecipes = new List<object>();
        foreach (var entry in entries)
        {
            if (!cardLookup.TryGetValue(entry.ItemUid, out var itemCard))
            {
                Log.Warn($"SmeltingRecipeInjector: item {entry.ItemUid} not found, skipping");
                continue;
            }

            var recipe = CloneAndCustomize(cookingRecipeType, templateRecipe, itemCard, nugget,
                                           entry.NuggetReturn, entry.Duration);
            if (recipe != null)
                newRecipes.Add(recipe);
        }

        // 6b. Custom forges keep only their own JSON-defined CookingRecipes.
        // Vanilla forge recipes (Smelt Copper, Axe Head, etc.) are NOT copied —
        // they cause phantom cooking timers and unwanted crafting on custom forges.

        if (newRecipes.Count == 0) return;

        // 7. Append mod smelting recipes to each forge/furnace
        int injected = 0;
        foreach (var target in targets)
        {
            var field = AccessTools.Field(target.GetType(), "CookingRecipes");
            if (field == null) continue;
            var existing = field.GetValue(target) as Array ?? Array.CreateInstance(cookingRecipeType, 0);

            var combined = Array.CreateInstance(cookingRecipeType, existing.Length + newRecipes.Count);
            Array.Copy(existing, combined, existing.Length);
            for (int i = 0; i < newRecipes.Count; i++)
                combined.SetValue(newRecipes[i], existing.Length + i);

            field.SetValue(target, combined);
            injected += newRecipes.Count;

            // Forge/furnace targets are vanilla — mark for NullReferenceCompactor.
            Loading.FrameworkDirtyTracker.MarkDirty(target);

            // Verify the recipes are actually on the target
            var verifyArr = field.GetValue(target) as Array;
            Log.Debug($"SmeltingRecipeInjector: injected {newRecipes.Count} recipes into {target.name} ({target.UniqueID}) — verified total: {verifyArr?.Length ?? -1}");
        }

        Log.Info($"SmeltingRecipeInjector: {injected} total recipes injected across {targets.Count} forge(s)");
    }

    /// <summary>
    /// Deep-clone a vanilla CookingRecipe, then override CompatibleCards, CompatibleTags, Drops, and Duration.
    /// This inherits all Conditions, FeedbackMode, ProgressBar, etc. from the working vanilla recipe.
    /// </summary>
    private static object CloneAndCustomize(Type recipeType, object template, CardData item,
                                             CardData nugget, int nuggetCount, int duration)
    {
        try
        {
            // Deep clone via JSON round-trip (Unity's JsonUtility handles serializable classes)
            var json = JsonUtility.ToJson(template);
            var recipe = Activator.CreateInstance(recipeType);
            JsonUtility.FromJsonOverwrite(json, recipe);

            // Override: CompatibleCards = [item] (match this specific mod item)
            var compatCardsField = AccessTools.Field(recipeType, "CompatibleCards");
            if (compatCardsField != null)
            {
                var arr = Array.CreateInstance(compatCardsField.FieldType.GetElementType() ?? typeof(CardData), 1);
                arr.SetValue(item, 0);
                compatCardsField.SetValue(recipe, arr);
                // Verify
                var verify = compatCardsField.GetValue(recipe) as Array;
                Log.Debug($"SmeltingRecipeInjector: [{item.UniqueID}] CompatibleCards set: type={compatCardsField.FieldType.FullName}, length={verify?.Length ?? -1}, item[0]={(verify?.Length > 0 ? verify.GetValue(0) : "null")}");
            }
            else
            {
                Log.Error($"SmeltingRecipeInjector: CompatibleCards field NOT FOUND on {recipeType.FullName}");
            }

            // Override: CompatibleTags = [] (don't match by tag — we match by specific card)
            var compatTagsField = AccessTools.Field(recipeType, "CompatibleTags");
            if (compatTagsField != null)
            {
                var elemType = compatTagsField.FieldType.GetElementType() ?? typeof(object);
                compatTagsField.SetValue(recipe, Array.CreateInstance(elemType, 0));
            }

            // Override: Duration
            var durField = recipeType.GetField("Duration", BF);
            if (durField != null) durField.SetValue(recipe, duration);

            // Override: Drops = [nugget x nuggetCount]
            var dropsField = AccessTools.Field(recipeType, "Drops");
            if (dropsField != null)
            {
                // CardDrop is a struct (ValueType). Reflection SetValue on a struct requires
                // boxing the struct into an object first and keeping that boxed reference
                // for all Set/GetValue calls — otherwise modifications go to a thrown-away copy.
                var dropsElemType = dropsField.FieldType.GetElementType() ?? typeof(CardDrop);
                object boxedDrop = Activator.CreateInstance(dropsElemType);

                var dcField = dropsElemType.GetField("DroppedCard", BF);
                if (dcField != null)
                {
                    dcField.SetValue(boxedDrop, nugget);
                    var verifyCard = dcField.GetValue(boxedDrop);
                    Log.Debug($"SmeltingRecipeInjector: [{item.UniqueID}] DroppedCard set to {verifyCard?.ToString() ?? "NULL"}");
                }
                else
                {
                    Log.Error($"SmeltingRecipeInjector: DroppedCard field NOT FOUND on {dropsElemType.FullName}");
                }

                var qtyField = dropsElemType.GetField("Quantity", BF);
                if (qtyField != null)
                    qtyField.SetValue(boxedDrop, new Vector2Int(nuggetCount, nuggetCount));

                var dropsArr = Array.CreateInstance(dropsElemType, 1);
                dropsArr.SetValue(boxedDrop, 0);
                dropsField.SetValue(recipe, dropsArr);

                // Verify the drop was stored correctly
                var verifyArr = dropsField.GetValue(recipe) as Array;
                object verifyDrop = verifyArr?.GetValue(0);
                var verifyDC = dcField?.GetValue(verifyDrop);
                Log.Debug($"SmeltingRecipeInjector: [{item.UniqueID}] Drops array length={verifyArr?.Length ?? -1}, DroppedCard in array={verifyDC?.ToString() ?? "NULL"}, nuggets={nuggetCount}");
            }
            else
            {
                Log.Error($"SmeltingRecipeInjector: Drops field NOT FOUND on {recipeType.FullName}");
            }

            // Override: ActionName text
            var nameField = AccessTools.Field(recipeType, "ActionName");
            if (nameField != null)
            {
                var ls = nameField.GetValue(recipe);
                if (ls != null)
                {
                    var dtField = ls.GetType().GetField("DefaultText", BF);
                    if (dtField != null) dtField.SetValue(ls, $"Smelt {item.name}");
                    nameField.SetValue(recipe, ls);
                }
            }

            // Keep SpoilageRange={1100,3000} from the Forge template — the vanilla Furnace
            // IS externally heated to 1300°C before smelting, and the WDI Forge self-heats
            // via Rate=+40. Both satisfy the {1100,3000} condition. The previous code that
            // cleared SpoilageRange to {0,0} was based on the incorrect assumption that those
            // stations never reach 1100°C; zeroing the range caused the timer to freeze.

            // Clear StatModifications (don't copy vanilla skill XP effects)
            var statModField = AccessTools.Field(recipeType, "StatModifications");
            if (statModField != null && statModField.FieldType.IsArray)
            {
                var elemType = statModField.FieldType.GetElementType() ?? typeof(object);
                statModField.SetValue(recipe, Array.CreateInstance(elemType, 0));
            }

            return recipe;
        }
        catch (Exception ex)
        {
            Log.Error($"SmeltingRecipeInjector: failed to create recipe for {item.UniqueID}: {ex.Message}");
            return null;
        }
    }

    private static int ToInt(object v)
    {
        if (v is double d) return (int)d;
        if (v is long l) return (int)l;
        if (v is int i) return i;
        return 0;
    }

    /// <summary>Collect card/tag identifiers from a recipe for duplicate detection.</summary>
    private static void CollectRecipeIdentifiers(object recipe, FieldInfo cardsField, FieldInfo tagsField,
                                                  HashSet<string> cardNames, HashSet<string> tagNames)
    {
        if (cardsField != null && cardsField.GetValue(recipe) is Array cards)
        {
            for (int i = 0; i < cards.Length; i++)
            {
                var c = cards.GetValue(i);
                if (c is UniqueIDScriptable uid && !string.IsNullOrEmpty(uid.UniqueID))
                    cardNames.Add(uid.UniqueID);
            }
        }
        if (tagsField != null && tagsField.GetValue(recipe) is Array tags)
        {
            for (int i = 0; i < tags.Length; i++)
            {
                var t = tags.GetValue(i);
                if (t != null) tagNames.Add(t.ToString());
            }
        }
    }

    /// <summary>Check if a recipe's cards/tags are already covered by existing recipes.</summary>
    private static bool RecipeAlreadyExists(object recipe, FieldInfo cardsField, FieldInfo tagsField,
                                             HashSet<string> existingCards, HashSet<string> existingTags)
    {
        if (cardsField != null && cardsField.GetValue(recipe) is Array cards && cards.Length > 0)
        {
            for (int i = 0; i < cards.Length; i++)
            {
                var c = cards.GetValue(i);
                if (c is UniqueIDScriptable uid && existingCards.Contains(uid.UniqueID))
                    return true;
            }
        }
        if (tagsField != null && tagsField.GetValue(recipe) is Array tags && tags.Length > 0)
        {
            for (int i = 0; i < tags.Length; i++)
            {
                var t = tags.GetValue(i);
                if (t != null && existingTags.Contains(t.ToString()))
                    return true;
            }
        }
        return false;
    }

    private struct SmeltEntry
    {
        public string ItemUid;
        public int NuggetReturn;
        public int Duration;
    }
}
