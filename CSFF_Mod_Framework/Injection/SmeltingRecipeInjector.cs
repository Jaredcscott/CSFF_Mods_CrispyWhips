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
/// into the vanilla Furnace and every mod smelting station (a card tagged tag_SmeltingContainer
/// that ships its own CookingRecipes) so mod copper items return exact nugget counts. The vanilla
/// Forge is deliberately not a target: its id here was a typo from ede83db72 until 2.26.9, so it never
/// received these recipes, and the owner chose on 2026-09-25 to drop the lookup rather than start.
/// Clones the vanilla "Smelt Small Tool" recipe as a template to inherit all conditions.
///
/// <para>Row schema: <c>{ "Item": "&lt;uid&gt;", "NuggetReturn": 3, "Duration": 32, "MetalType": 200 }</c>.
/// <c>MetalType</c> (2.27.0, optional) is the Special4 value the returned nuggets carry (the metal
/// type that metal-gated recipes read: see AdvancedCopperTools/CLAUDE.md). It is written as the
/// drop's <c>TransferRules.Special4 = SetValue</c> with <c>Special4Value</c> = the number, which the
/// engine turns into an active Special4 on the spawned nugget (<c>TransferedDurabilities</c>). A
/// value that is not a number in (0, 1000] (MetalNugget's Special4 max) is dropped with a warning
/// and the row returns untyped nuggets, as it did before the field existed. Without the field
/// nothing about the drop changes.</para>
///
/// <para><c>InheritMetal</c> (2.28.0, optional, <c>true</c>/<c>false</c>): the nuggets take the smelted
/// item's own Special2 (Metal Quality) and Special4 (Metal Type), through vanilla's melt rule
/// <c>TransferRules.Special2 = Special4 = FromReceivingCard</c>. Use it for an item that carries those
/// stats (ACT's Metal Sheet), so a bronze sheet returns bronze nuggets at the sheet's quality instead
/// of untyped quality-0 ones. An explicit <c>MetalType</c> on the same row still wins for Special4.</para>
/// </summary>
internal static class SmeltingRecipeInjector
{
    private const string FURNACE_UID = "984bad0c8931f3545bef58171f9bf252";
    private const string NUGGET_UID = "4b0f4937a5ecb90499428c8c10288afc";

    private static readonly BindingFlags BF =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static void InjectAll(IEnumerable allData, List<ModManifest> mods)
    {
        // 1. Collect all smelting recipe entries from mod JSON files
        var entries = new List<SmeltEntry>();
        // Rows skipped for an empty Item, a NuggetReturn <= 0 or an Item that does not resolve; one log
        // line each, counted in the Info summary (framework README "Unresolved references").
        int skipped = 0;
        foreach (var mod in mods)
        {
            var path = Path.Combine(mod.DirectoryPath, "SmeltingRecipes.json");
            if (!File.Exists(path)) continue;
            try
            {
                var json = File.ReadAllText(path);
                var parsed = MiniJson.Parse(json);
                if (parsed is not List<object> arr)
                {
                    Log.Warn($"SmeltingRecipeInjector: {mod.Name} SmeltingRecipes.json skipped, its root must be a JSON array");
                    continue;
                }

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
                    if (dict.TryGetValue("MetalType", out var v4))
                        entry.MetalType = ParseMetalType(v4, mod.Name, entry.ItemUid);
                    if (dict.TryGetValue("InheritMetal", out var v5))
                    {
                        if (v5 is bool inherit) entry.InheritMetal = inherit;
                        else Log.Warn($"SmeltingRecipeInjector: {mod.Name} SmeltingRecipes.json row '{entry.ItemUid}': InheritMetal '{v5}' is not true/false; ignored");
                    }
                    if (!string.IsNullOrEmpty(entry.ItemUid) && entry.NuggetReturn > 0)
                    {
                        entries.Add(entry);
                        modCount++;
                    }
                    else
                    {
                        Log.Debug($"SmeltingRecipeInjector: {mod.Name} SmeltingRecipes.json row '{entry.ItemUid}' skipped: it needs an Item and a NuggetReturn above 0");
                        skipped++;
                    }
                }
                Log.Debug($"SmeltingRecipeInjector: loaded {modCount} entries from {mod.Name}");
            }
            catch (Exception ex)
            {
                Log.Warn($"SmeltingRecipeInjector: error reading {path}: {Log.ExceptionText(ex)}");
            }
        }

        if (entries.Count == 0)
        {
            if (skipped > 0) Log.Info($"SmeltingRecipeInjector: 0 recipes, {skipped} row(s) skipped");
            return;
        }

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

        // 4. Find the furnace
        var furnaceCard = UniqueIDScriptable.GetFromID<CardData>(FURNACE_UID);

        // Also find mod smelting stations (any card with tag_SmeltingContainer + CookingRecipes)
        var customForges = new List<CardData>();
        FieldInfo cookingRecipesFieldCached = null;
        FieldInfo cardTagsFieldCached = null;
        foreach (var obj in allData)
        {
            if (obj is CardData cd && cd != furnaceCard
                && !string.IsNullOrEmpty(cd.UniqueID)
                && HasSmeltingContainerTag(cd, ref cardTagsFieldCached))
            {
                cookingRecipesFieldCached ??= CardUtil.GetCachedField(cd.GetType(), "CookingRecipes");
                if (cookingRecipesFieldCached != null && cookingRecipesFieldCached.GetValue(cd) is Array a && a.Length > 0)
                    customForges.Add(cd);
            }
        }

        var targets = new List<CardData>();
        if (furnaceCard != null) targets.Add(furnaceCard);
        targets.AddRange(customForges);

        if (targets.Count == 0)
        {
            Log.Error("SmeltingRecipeInjector: no furnace or mod smelting station found");
            return;
        }

        static bool HasSmeltingContainerTag(CardData cd, ref FieldInfo cachedTagsField)
        {
            cachedTagsField ??= CardUtil.GetCachedField(cd.GetType(), "CardTags");
            if (cachedTagsField == null) return false;
            if (cachedTagsField.GetValue(cd) is not Array tags) return false;
            for (int i = 0; i < tags.Length; i++)
            {
                if (tags.GetValue(i) is UnityEngine.Object tag && tag.name == "tag_SmeltingContainer")
                    return true;
            }
            return false;
        }

        // 5. Get a vanilla smelting recipe as TEMPLATE (clone from it)
        object templateRecipe = null;
        Type cookingRecipeType = null;
        FieldInfo recipesFieldInfo = null;

        CardData fallbackSource = null;
        object fallbackRecipe = null;
        var templateSources = new List<CardData>();
        if (furnaceCard != null) templateSources.Add(furnaceCard);

        foreach (var templateSource in templateSources)
        {
            recipesFieldInfo = CardUtil.GetCachedField(templateSource.GetType(), "CookingRecipes");
            var existing = recipesFieldInfo?.GetValue(templateSource) as Array;
            if (existing != null)
            {
                cookingRecipeType = existing.GetType().GetElementType();
                Log.Debug($"SmeltingRecipeInjector: CookingRecipe type = {cookingRecipeType.FullName}, template has {existing.Length} recipes from {templateSource.name} ({templateSource.UniqueID})");

                // Log available fields on CookingRecipe type
                var ccField = CardUtil.GetCachedField(cookingRecipeType, "CompatibleCards");
                var ctField = CardUtil.GetCachedField(cookingRecipeType, "CompatibleTags");
                Log.Debug($"SmeltingRecipeInjector: CompatibleCards field = {(ccField != null ? ccField.FieldType.FullName : "NOT FOUND")}");
                Log.Debug($"SmeltingRecipeInjector: CompatibleTags field = {(ctField != null ? ctField.FieldType.FullName : "NOT FOUND")}");

                // Find "Smelt Small Tool" or "Smelt Large Tool" (recipes with CompatibleTags containing copper tags)
                var compatTagsField = CardUtil.GetCachedField(cookingRecipeType, "CompatibleTags");
                for (int i = 0; i < existing.Length; i++)
                {
                    var r = existing.GetValue(i);
                    if (r == null) continue;
                    var tags = compatTagsField?.GetValue(r) as Array;
                    var cards = ccField?.GetValue(r) as Array;
                    Log.Debug($"SmeltingRecipeInjector: template recipe[{i}] CompatibleTags={tags?.Length ?? -1}, CompatibleCards={cards?.Length ?? -1}");
                    if (tags != null && tags.Length > 0)
                    {
                        templateRecipe = r;
                        Log.Debug($"SmeltingRecipeInjector: using recipe[{i}] as template (has {tags.Length} tags)");
                        break;
                    }
                }

                if (fallbackRecipe == null && existing.Length > 0)
                {
                    fallbackRecipe = existing.GetValue(0);
                    fallbackSource = templateSource;
                }
            }

            if (templateRecipe != null)
                break;
        }

        if (templateRecipe == null && fallbackRecipe != null)
        {
            templateRecipe = fallbackRecipe;
            Log.Warn($"SmeltingRecipeInjector: using first recipe from {fallbackSource.name} as template (no tagged smelting recipe found)");
        }

        if (templateRecipe == null || cookingRecipeType == null)
        {
            Log.Error("SmeltingRecipeInjector: no template recipe found on the furnace");
            return;
        }

        // 6. Create custom recipes by cloning the template
        var newRecipes = new List<object>();
        int typedRecipes = 0;
        int inheritingRecipes = 0;
        foreach (var entry in entries)
        {
            if (!cardLookup.TryGetValue(entry.ItemUid, out var itemCard))
            {
                // Warn, not Debug: SmeltingRecipes.json names a mod's own items, so a miss is an
                // authoring error rather than an absent partner mod (plan W0-11 finding 1).
                Log.Warn($"SmeltingRecipeInjector: item {entry.ItemUid} not found, skipping");
                skipped++;
                continue;
            }

            var recipe = CloneAndCustomize(cookingRecipeType, templateRecipe, itemCard, nugget,
                                           entry.NuggetReturn, entry.Duration, entry.MetalType,
                                           entry.InheritMetal, out bool typed, out bool inherits);
            if (recipe != null)
            {
                newRecipes.Add(recipe);
                if (typed) typedRecipes++;
                if (inherits) inheritingRecipes++;
            }
        }

        // 6b. Custom forges keep only their own JSON-defined CookingRecipes.
        // Vanilla forge recipes (Smelt Copper, Axe Head, etc.) are NOT copied —
        // they cause phantom cooking timers and unwanted crafting on custom forges.

        if (newRecipes.Count == 0)
        {
            if (skipped > 0) Log.Info($"SmeltingRecipeInjector: 0 recipes, {skipped} row(s) skipped");
            return;
        }

        // 7. Put mod smelting recipes AHEAD of each forge/furnace's own (see PlaceAhead)
        int injected = 0;
        var cardsField = CardUtil.GetCachedField(cookingRecipeType, "CompatibleCards");
        var tagsField = CardUtil.GetCachedField(cookingRecipeType, "CompatibleTags");

        foreach (var target in targets)
        {
            var field = CardUtil.GetCachedField(target.GetType(), "CookingRecipes");
            if (field == null) continue;
            var existing = field.GetValue(target) as Array ?? Array.CreateInstance(cookingRecipeType, 0);

            var existingCards = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var existingTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < existing.Length; i++)
                CollectRecipeIdentifiers(existing.GetValue(i), cardsField, tagsField, existingCards, existingTags);

            var recipesToAdd = new List<object>();
            foreach (var recipe in newRecipes)
            {
                if (RecipeAlreadyExists(recipe, cardsField, tagsField, existingCards, existingTags))
                    continue;

                recipesToAdd.Add(recipe);
                CollectRecipeIdentifiers(recipe, cardsField, tagsField, existingCards, existingTags);
            }

            if (recipesToAdd.Count == 0)
            {
                Log.Debug($"SmeltingRecipeInjector: all {newRecipes.Count} recipes already present on {target.name} ({target.UniqueID})");
                continue;
            }

            field.SetValue(target, PlaceAhead(existing, recipesToAdd, cookingRecipeType));
            injected += recipesToAdd.Count;

            // The furnace is vanilla, so mark every target for NullReferenceCompactor.
            Loading.FrameworkDirtyTracker.MarkDirty(target);

            // Verify the recipes are actually on the target
            var verifyArr = field.GetValue(target) as Array;
            Log.Debug($"SmeltingRecipeInjector: injected {recipesToAdd.Count} recipes into {target.name} ({target.UniqueID}) — verified total: {verifyArr?.Length ?? -1}");
        }

        Log.Debug($"SmeltingRecipeInjector: {injected} total recipes injected across {targets.Count} forge(s)");
        Log.Info($"SmeltingRecipeInjector: {newRecipes.Count} recipes, {typedRecipes} with a metal type, {inheritingRecipes} inheriting the item's metal"
               + (skipped > 0 ? $", {skipped} row(s) skipped" : ""));
    }

    /// <summary>The Special4 the returned nuggets carry, or null (with a warning naming the mod and
    /// item) when the value is not a number in (0, 1000]. MiniJson hands numbers over as double.</summary>
    private static float? ParseMetalType(object raw, string modName, string itemUid)
    {
        if (raw is double d && d > 0 && d <= 1000) return (float)d;
        Log.Warn($"SmeltingRecipeInjector: {modName} SmeltingRecipes.json row '{itemUid}': MetalType '{raw}' is not a number in (0, 1000]; "
               + "the row returns untyped nuggets");
        return null;
    }

    private static readonly HashSet<string> _warned = new(StringComparer.Ordinal);

    private static void WarnOnce(string cause, string message)
    {
        if (_warned.Add(cause)) Log.Warn($"SmeltingRecipeInjector: {message}");
    }

    /// <summary>Writes <c>TransferRules.Special4 = SetValue</c> and <c>Special4Value</c> into the boxed
    /// drop. Every other transfer keeps its default (NoTransfer). False, with one warning per cause,
    /// when a member is missing, which leaves the drop exactly as it was built before 2.27.0.</summary>
    private static bool SetMetalType(object boxedDrop, Type dropType, float metalType)
    {
        var rulesField = CardUtil.GetCachedField(dropType, "TransferRules");
        if (rulesField == null)
        {
            WarnOnce("transferrules", $"{dropType.Name}.TransferRules is missing; MetalType rows return untyped nuggets");
            return false;
        }

        var rulesType = rulesField.FieldType;
        var special4Field = CardUtil.GetCachedField(rulesType, "Special4");
        var special4ValueField = CardUtil.GetCachedField(rulesType, "Special4Value");
        if (special4Field == null || special4ValueField == null || !special4Field.FieldType.IsEnum)
        {
            WarnOnce("special4", $"{rulesType.Name}.Special4 (an enum) or .Special4Value is missing; MetalType rows return untyped nuggets");
            return false;
        }
        if (!Enum.IsDefined(special4Field.FieldType, "SetValue"))
        {
            WarnOnce("setvalue", $"{special4Field.FieldType.Name} has no SetValue member; MetalType rows return untyped nuggets");
            return false;
        }

        // TransferToDrop is a struct inside the CardDrop struct: box it, write, and write the box
        // back, or the writes land on a copy. SetValue is resolved BY NAME, so a reordered game
        // enum fails the check above instead of writing the wrong member.
        object rules = rulesField.GetValue(boxedDrop);
        special4Field.SetValue(rules, Enum.Parse(special4Field.FieldType, "SetValue"));
        special4ValueField.SetValue(rules, metalType);
        rulesField.SetValue(boxedDrop, rules);
        return true;
    }

    /// <summary>Writes <c>TransferRules.Special2 = Special4 = FromReceivingCard</c> into the boxed drop,
    /// vanilla's own melt rule (MetalBarFinished, MoltenMetalCrucible*, Blank_MetalArrowHead all drop
    /// their nuggets with it). In a cooking recipe the receiving card is the cooked ingredient
    /// (<c>CookingRecipe.GetResult</c> passes it to <c>SetDroppedCards</c>), so the nuggets take the
    /// smelted item's Metal Quality and Metal Type; a slot the item does not have active transfers as
    /// inactive and the nugget keeps its default. False, with one warning per cause, when a member is
    /// missing, leaving the drop as it was.</summary>
    private static bool SetInheritMetal(object boxedDrop, Type dropType)
    {
        var rulesField = CardUtil.GetCachedField(dropType, "TransferRules");
        if (rulesField == null)
        {
            WarnOnce("inherit-transferrules", $"{dropType.Name}.TransferRules is missing; InheritMetal rows return untyped nuggets");
            return false;
        }
        var rulesType = rulesField.FieldType;
        var special2Field = CardUtil.GetCachedField(rulesType, "Special2");
        var special4Field = CardUtil.GetCachedField(rulesType, "Special4");
        if (special2Field == null || special4Field == null || !special2Field.FieldType.IsEnum || !special4Field.FieldType.IsEnum
            || !Enum.IsDefined(special4Field.FieldType, "FromReceivingCard"))
        {
            WarnOnce("inherit-members", $"{rulesType.Name}.Special2/Special4 or FromReceivingCard is missing; InheritMetal rows return untyped nuggets");
            return false;
        }

        // Same boxing rule as SetMetalType: the struct is written through a box, then the box back.
        object rules = rulesField.GetValue(boxedDrop);
        var fromReceiving = Enum.Parse(special4Field.FieldType, "FromReceivingCard");
        special2Field.SetValue(rules, fromReceiving);
        special4Field.SetValue(rules, fromReceiving);
        rulesField.SetValue(boxedDrop, rules);
        return true;
    }

    /// <summary>
    /// Deep-clone a vanilla CookingRecipe, then override CompatibleCards, CompatibleTags, Drops, and Duration.
    /// This inherits all Conditions, FeedbackMode, ProgressBar, etc. from the working vanilla recipe.
    /// </summary>
    private static object CloneAndCustomize(Type recipeType, object template, CardData item,
                                             CardData nugget, int nuggetCount, int duration,
                                             float? metalType, bool inheritMetal,
                                             out bool typed, out bool inherits)
    {
        typed = false;
        inherits = false;
        try
        {
            // Deep clone via JSON round-trip (Unity's JsonUtility handles serializable classes)
            var json = JsonUtility.ToJson(template);
            var recipe = Activator.CreateInstance(recipeType);
            JsonUtility.FromJsonOverwrite(json, recipe);

            // Override: CompatibleCards = [item] (match this specific mod item)
            var compatCardsField = CardUtil.GetCachedField(recipeType, "CompatibleCards");
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
            var compatTagsField = CardUtil.GetCachedField(recipeType, "CompatibleTags");
            if (compatTagsField != null)
            {
                var elemType = compatTagsField.FieldType.GetElementType() ?? typeof(object);
                compatTagsField.SetValue(recipe, Array.CreateInstance(elemType, 0));
            }

            // Override: Duration
            var durField = recipeType.GetField("Duration", BF);
            if (durField != null) durField.SetValue(recipe, duration);

            // Override: Drops = [nugget x nuggetCount]
            var dropsField = CardUtil.GetCachedField(recipeType, "Drops");
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

                // Optional InheritMetal, then MetalType: both must land in the box before the box
                // goes into the array, and MetalType runs second so its SetValue wins for Special4.
                if (inheritMetal)
                    inherits = SetInheritMetal(boxedDrop, dropsElemType);
                if (metalType is { } mt)
                    typed = SetMetalType(boxedDrop, dropsElemType, mt);

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
            var nameField = CardUtil.GetCachedField(recipeType, "ActionName");
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

            // Smelting threshold 900 C instead of the template's {1100,3000}. It was added so the
            // vanilla Forge (max 1000 C) could smelt these too, but the Forge was never a target
            // (see the class doc). It stays because players already smelt these items at 900 C
            // in the Furnace and the mod stations; raising it would change that.
            // ReceivingRequiredDurabilityRanges is nested inside the Conditions sub-object.
            var conditionsField = CardUtil.GetCachedField(recipeType, "Conditions");
            if (conditionsField != null)
            {
                object conditions = conditionsField.GetValue(recipe);
                if (conditions != null)
                {
                    var rrdrField = conditions.GetType().GetField("ReceivingRequiredDurabilityRanges", BF);
                    if (rrdrField != null)
                    {
                        object ranges = rrdrField.GetValue(conditions);
                        if (ranges != null)
                        {
                            var spoilField = ranges.GetType().GetField("SpoilageRange", BF);
                            if (spoilField != null)
                            {
                                spoilField.SetValue(ranges, new Vector2(900f, 3000f));
                                rrdrField.SetValue(conditions, ranges);
                                conditionsField.SetValue(recipe, conditions);
                            }
                        }
                    }
                }
            }

            // Clear StatModifications (don't copy vanilla skill XP effects)
            var statModField = CardUtil.GetCachedField(recipeType, "StatModifications");
            if (statModField != null && statModField.FieldType.IsArray)
            {
                var elemType = statModField.FieldType.GetElementType() ?? typeof(object);
                statModField.SetValue(recipe, Array.CreateInstance(elemType, 0));
            }

            // Override: IngredientChanges.ModType = 0 (consume ingredient after smelting).
            // In a CookingRecipe, IngredientChanges.ModType uses a DIFFERENT enum than
            // ReceivingCardChanges: 0=consume, 1=modify-in-place, 2=transform-into.
            // All 25 vanilla consume-and-drop recipes (Make Charcoal, Burn Stone, etc.) use 0.
            // The cloned template "Heats up Nuggets and Lumps" uses 1 (heat only, keeps item) —
            // without this override the item is never consumed and drops nuggets every Duration tick forever.
            var ingredChangesField = CardUtil.GetCachedField(recipeType, "IngredientChanges");
            if (ingredChangesField != null)
            {
                object ingredChanges = ingredChangesField.GetValue(recipe);
                if (ingredChanges != null)
                {
                    var modTypeField = ingredChanges.GetType().GetField("ModType", BF);
                    if (modTypeField != null)
                    {
                        modTypeField.SetValue(ingredChanges, 0);
                        ingredChangesField.SetValue(recipe, ingredChanges);
                    }
                }
            }

            return recipe;
        }
        catch (Exception ex)
        {
            Log.Error($"SmeltingRecipeInjector: failed to create recipe for {item.UniqueID}: {Log.ExceptionText(ex)}");
            return null;
        }
    }

    /// <summary>
    /// The station's new recipe array: the injected recipes first, then the existing ones in their order.
    /// <c>CardData.GetRecipeForCard</c> returns the FIRST recipe that is valid for the ingredient and whose
    /// conditions hold (.decomp/CardData.cs), and each injected recipe names exactly one card, so it must
    /// sit ahead of the station's tag-wide recipes. Appended, it lost to the vanilla Furnace's recipe 0,
    /// "Temperature 1100+" (CompatibleTags tag_SmeltsAt1100, cooker heat 1100 and up, heats the item and
    /// never consumes it): 45 of ACT's 50 rows carry that tag, so above 1100 an iron helmet or a copper
    /// cauldron sat in the furnace for ever and only a furnace held at 900-1099 smelted them (harness
    /// T2.363: 10 ticks at 1200, recipe 0 ran every tick, no nugget; T2.442 the same for Tin Bracers).
    /// </summary>
    internal static Array PlaceAhead(Array existing, IList<object> injected, Type elementType)
    {
        var combined = Array.CreateInstance(elementType, injected.Count + existing.Length);
        for (int i = 0; i < injected.Count; i++)
            combined.SetValue(injected[i], i);
        Array.Copy(existing, 0, combined, injected.Count, existing.Length);
        return combined;
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
        /// <summary>Special4 of the returned nuggets; null = untyped (the pre-2.27.0 behaviour).</summary>
        public float? MetalType;
        /// <summary>2.28.0: the nuggets take the smelted item's own Special2 (Metal Quality) and
        /// Special4 (Metal Type); an explicit <see cref="MetalType"/> still wins for Special4.</summary>
        public bool InheritMetal;
    }
}
