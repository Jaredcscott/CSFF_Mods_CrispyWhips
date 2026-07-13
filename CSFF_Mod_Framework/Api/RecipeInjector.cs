using CSFFModFramework.Util;

namespace CSFFModFramework.Api;

/// <summary>
/// Describes a CookingRecipe to append to a station's <c>CookingRecipes</c> array.
/// Defaults match the "modify the held ingredient in place, leave the station itself
/// alone" shape both ACT's kettle-heating and H&amp;F's tendon-drying recipes use.
/// </summary>
public sealed class RecipeSpec
{
    /// <summary>Cards that trigger this recipe. Leave null/empty to gate on tags only.</summary>
    public IReadOnlyList<object> CompatibleCards { get; set; }

    /// <summary>Tags that trigger this recipe. Leave null/empty to gate on cards only.</summary>
    public IReadOnlyList<object> CompatibleTags { get; set; }

    /// <summary>CookingRecipe.ConditionsCard — 0 for "no extra condition required".</summary>
    public int ConditionsCard { get; set; } = 0;

    /// <summary>CookingRecipe.Duration, in DTP ticks.</summary>
    public int Duration { get; set; } = 1;

    /// <summary>CookerChanges.ModType — 0 leaves the station itself unaffected.</summary>
    public int CookerModType { get; set; } = 0;

    /// <summary>IngredientChanges.ModType — 1 modifies the held ingredient in place.</summary>
    public int IngredientModType { get; set; } = 1;

    public Vector2 UsageChange { get; set; } = Vector2.zero;
    public Vector2 SpoilageChange { get; set; } = Vector2.zero;
    public Vector2 FuelChange { get; set; } = Vector2.zero;
}

/// <summary>
/// Generalized CookingRecipe injection (Centralization Tier 3). Replaces the
/// near-identical clone-template/set-fields/idempotency-scan blocks independently
/// duplicated in AdvancedCopperTools (VanillaFireKettlePatch.InjectHeatingRecipe) and
/// HerbsAndFungi (GameLoadPatch.InjectTendonDryingRecipe) — same field names, same
/// shape, ~120 LOC each.
/// </summary>
public static class RecipeInjector
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>
    /// Clones <paramref name="stationCard"/>'s first CookingRecipe as a structural
    /// template, overwrites it per <paramref name="spec"/>, and appends it to
    /// <c>CookingRecipes</c>. Idempotent: skipped if an existing recipe already has
    /// the same CompatibleCards set. Returns true if a recipe was injected.
    /// </summary>
    public static bool InjectCookingRecipe(object stationCard, RecipeSpec spec, string label = null)
    {
        label ??= CardUtil.GetCardUniqueId(stationCard) ?? "?";
        try
        {
            if (stationCard == null || spec == null) return false;

            var recipesField = stationCard.GetType().GetField("CookingRecipes", Flags);
            if (recipesField == null)
            {
                Log.Warn($"[RecipeInjector] {label}: CookingRecipes field not found.");
                return false;
            }

            var recipeArr = recipesField.GetValue(stationCard) as Array;
            if (recipeArr == null || recipeArr.Length == 0)
            {
                Log.Debug($"[RecipeInjector] {label}: CookingRecipes is null/empty; skipped.");
                return false;
            }

            var recipeType = recipeArr.GetType().GetElementType();
            var compatCardsField = recipeType.GetField("CompatibleCards", Flags);

            if (compatCardsField != null && AlreadyInjected(recipeArr, compatCardsField, spec.CompatibleCards))
            {
                Log.Debug($"[RecipeInjector] {label}: recipe already injected; skipped.");
                return false;
            }

            // Clone the first recipe as a template for correct type/field defaults.
            var template = recipeArr.GetValue(0);
            var newRecipe = Activator.CreateInstance(recipeType);
            foreach (var fi in recipeType.GetFields(Flags))
                fi.SetValue(newRecipe, fi.GetValue(template));

            SetObjectArray(compatCardsField, newRecipe, spec.CompatibleCards);
            SetObjectArray(recipeType.GetField("CompatibleTags", Flags), newRecipe, spec.CompatibleTags);

            recipeType.GetField("ConditionsCard", Flags)?.SetValue(newRecipe, spec.ConditionsCard);
            recipeType.GetField("Duration", Flags)?.SetValue(newRecipe, spec.Duration);

            var cookerField = recipeType.GetField("CookerChanges", Flags);
            if (cookerField != null)
            {
                var cooker = cookerField.GetValue(newRecipe) ?? Activator.CreateInstance(cookerField.FieldType);
                cooker.GetType().GetField("ModType", Flags)?.SetValue(cooker, spec.CookerModType);
                cookerField.SetValue(newRecipe, cooker);
            }

            var ingField = recipeType.GetField("IngredientChanges", Flags);
            if (ingField == null)
            {
                Log.Warn($"[RecipeInjector] {label}: IngredientChanges field not found.");
                return false;
            }

            var ing = ingField.GetValue(newRecipe) ?? Activator.CreateInstance(ingField.FieldType);
            var ingType = ing.GetType();
            var fuelChangeField = ingType.GetField("FuelChange", Flags);
            if (fuelChangeField == null)
            {
                Log.Warn($"[RecipeInjector] {label}: FuelChange field not found on IngredientChanges.");
                return false;
            }

            ingType.GetField("ModType", Flags)?.SetValue(ing, spec.IngredientModType);
            ingType.GetField("UsageChange", Flags)?.SetValue(ing, spec.UsageChange);
            ingType.GetField("SpoilageChange", Flags)?.SetValue(ing, spec.SpoilageChange);
            fuelChangeField.SetValue(ing, spec.FuelChange);
            ingField.SetValue(newRecipe, ing);

            var newArr = Array.CreateInstance(recipeType, recipeArr.Length + 1);
            Array.Copy(recipeArr, newArr, recipeArr.Length);
            newArr.SetValue(newRecipe, recipeArr.Length);
            recipesField.SetValue(stationCard, newArr);

            Log.Debug($"[RecipeInjector] {label}: injected recipe.");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"[RecipeInjector] {label}: {Log.ExceptionText(ex)}");
            return false;
        }
    }

    // True if any existing recipe's CompatibleCards is the same set as targetCards.
    private static bool AlreadyInjected(Array recipeArr, FieldInfo compatCardsField, IReadOnlyList<object> targetCards)
    {
        if (targetCards == null || targetCards.Count == 0) return false;
        foreach (var recipe in recipeArr)
        {
            if (compatCardsField.GetValue(recipe) is not Array existing) continue;
            if (existing.Length != targetCards.Count) continue;

            int matches = 0;
            foreach (var target in targetCards)
                foreach (var c in existing)
                    if (Equals(c, target)) { matches++; break; }

            if (matches == targetCards.Count) return true;
        }
        return false;
    }

    private static void SetObjectArray(FieldInfo field, object target, IReadOnlyList<object> values)
    {
        if (field == null || !field.FieldType.IsArray) return;
        var elementType = field.FieldType.GetElementType() ?? typeof(object);

        values ??= Array.Empty<object>();
        var arr = Array.CreateInstance(elementType, values.Count);
        for (int i = 0; i < values.Count; i++)
            if (values[i] != null && elementType.IsInstanceOfType(values[i]))
                arr.SetValue(values[i], i);

        field.SetValue(target, arr);
    }
}
