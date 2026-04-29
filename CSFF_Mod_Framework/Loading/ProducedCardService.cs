using CSFFModFramework.Discovery;
using CSFFModFramework.Util;

namespace CSFFModFramework.Loading;

/// <summary>
/// Normalizes ProducedCards arrays on mod cards (initialize defaults) and
/// cleans null/invalid DroppedCard entries.
///
/// Previously every mod (H&amp;F, ACT, WDI) had its own copy of this logic.
/// Combines ProducedCardNormalizer + ProducedCardCleaner into one service.
/// </summary>
internal static class ProducedCardService
{
    private static readonly BindingFlags AllInstance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // Cache field lookups by (type, fieldName) — avoids thousands of repeated reflection calls
    private static readonly Dictionary<(Type, string), FieldInfo> _fieldCache = new();

    private static FieldInfo CachedField(Type type, string name)
    {
        var key = (type, name);
        if (!_fieldCache.TryGetValue(key, out var field))
        {
            field = AccessTools.Field(type, name);
            _fieldCache[key] = field;
        }
        return field;
    }

    private static readonly string[] DurabilityFields =
    {
        "FuelCapacity", "SpecialDurability1", "SpecialDurability2",
        "SpecialDurability3", "SpecialDurability4", "Progress"
    };

    private static readonly string[] ActionArrayFields = { "DismantleActions", "CardInteractions" };

    // Counter for Quantity (0,0)→(1,1) fixes — logged as summary instead of per-drop
    private static int _quantityFixCount;

    /// <summary>
    /// Run both normalization and cleanup on all mod cards.
    /// </summary>
    public static void ProcessAll(IEnumerable allData, List<ModManifest> mods)
    {
        var modUniqueIds = JsonDataLoader.AllModUniqueIds;
        if (modUniqueIds.Count == 0) return;

        _quantityFixCount = 0;
        int normalized = 0;
        int cleaned = 0;

        foreach (var item in allData)
        {
            if (!(item is CardData)) continue;
            if (!(item is UniqueIDScriptable uid) || string.IsNullOrEmpty(uid.UniqueID)
                || !modUniqueIds.Contains(uid.UniqueID))
                continue;

            // Phase 1: Normalize defaults
            normalized += NormalizeCard(item);

            // Phase 2: Clean nulls
            cleaned += CleanCard(item);
        }

        if (normalized > 0 || cleaned > 0 || _quantityFixCount > 0)
            Log.Info($"ProducedCardService: initialized {normalized} fields, removed {cleaned} null drops, fixed {_quantityFixCount} (0,0)→(1,1) quantities");
    }

    // ===== NORMALIZATION =====

    private static int NormalizeCard(object card)
    {
        int total = 0;

        foreach (var fieldName in ActionArrayFields)
            total += NormalizeActionsArray(card, fieldName);

        foreach (var durName in DurabilityFields)
        {
            var durField = CachedField(card.GetType(), durName);
            if (durField == null) continue;
            var durVal = durField.GetValue(card);
            if (durVal == null) continue;

            // Check HasActionOnFull for diagnostic purposes
            var hasOnFullField = CachedField(durVal.GetType(), "HasActionOnFull");
            bool hasOnFull = hasOnFullField != null && (bool)hasOnFullField.GetValue(durVal);
            if (hasOnFull && card is UniqueIDScriptable diagUid)
            {
                var onFullField = CachedField(durVal.GetType(), "OnFull");
                var onFullVal = onFullField?.GetValue(durVal);
                var pcField = onFullVal != null ? CachedField(onFullVal.GetType(), "ProducedCards") : null;
                var pcVal = pcField?.GetValue(onFullVal);
                int pcLen = pcVal is Array pcArr ? pcArr.Length : (pcVal is IList pcList ? pcList.Count : -1);
                Log.Debug($"ProducedCardService: {diagUid.UniqueID} {durName}.OnFull: action={onFullVal != null}, ProducedCards={pcLen}, durIsValueType={durField.FieldType.IsValueType}");
            }

            foreach (var actionName in new[] { "OnZero", "OnFull" })
            {
                var actionField = CachedField(durVal.GetType(), actionName);
                if (actionField == null) continue;
                var actionVal = actionField.GetValue(durVal);
                if (actionVal == null) continue;

                int count = NormalizeProducedCardsOnAction(actionVal);
                if (count > 0 && durField.FieldType.IsValueType)
                {
                    actionField.SetValue(durVal, actionVal);
                    durField.SetValue(card, durVal);
                }
                total += count;
            }
        }

        return total;
    }

    private static int NormalizeActionsArray(object owner, string fieldName)
    {
        var field = CachedField(owner.GetType(), fieldName);
        if (field == null) return 0;
        var actionsValue = field.GetValue(owner);
        if (actionsValue == null) return 0;

        int total = 0;
        if (actionsValue is Array actionsArray)
        {
            for (int i = 0; i < actionsArray.Length; i++)
            {
                var action = actionsArray.GetValue(i);
                if (action == null) continue;
                int count = NormalizeProducedCardsOnAction(action);
                if (count > 0 && action.GetType().IsValueType)
                    actionsArray.SetValue(action, i);
                total += count;
            }
        }
        else if (actionsValue is IList actionsList)
        {
            for (int i = 0; i < actionsList.Count; i++)
            {
                var action = actionsList[i];
                if (action == null) continue;
                int count = NormalizeProducedCardsOnAction(action);
                if (count > 0 && action.GetType().IsValueType)
                    actionsList[i] = action;
                total += count;
            }
        }
        return total;
    }

    private static int NormalizeProducedCardsOnAction(object action)
    {
        var producedField = CachedField(action.GetType(), "ProducedCards");
        if (producedField == null) return 0;
        var produced = producedField.GetValue(action) as Array;
        if (produced == null || produced.Length == 0) return 0;

        int total = 0;
        for (int c = 0; c < produced.Length; c++)
        {
            var collection = produced.GetValue(c);
            if (collection == null) continue;

            int collectionCount = ReflectionHelpers.InitializeSerializableDefaults(collection, 2);
            total += collectionCount;

            var droppedCardsField = CachedField(collection.GetType(), "DroppedCards");
            var drops = droppedCardsField?.GetValue(collection) as Array;

            int compatCount = ApplyCompatibilityDefaults(collection, droppedCardsField, ref drops);
            total += compatCount;

            if (drops != null)
            {
                for (int d = 0; d < drops.Length; d++)
                {
                    var drop = drops.GetValue(d);
                    if (drop == null) continue;

                    int dropCount = ReflectionHelpers.InitializeSerializableDefaults(drop, 2);

                    // Fix Quantity=(0,0) on drops — Vector2Int is a value type that defaults
                    // to (0,0), never null. InitializeSerializableDefaults skips it.
                    // Without this fix, durability drops (smelting, smithing, smoking) produce 0 items.
                    int qtyFixed = FixZeroQuantity(drop);
                    dropCount += qtyFixed;
                    if (qtyFixed > 0) _quantityFixCount++;

                    total += dropCount;
                    if (dropCount > 0 && drop.GetType().IsValueType)
                        drops.SetValue(drop, d);
                }
            }

            if ((collectionCount > 0 || compatCount > 0) && collection.GetType().IsValueType)
                produced.SetValue(collection, c);
        }
        return total;
    }

    private static int ApplyCompatibilityDefaults(object collection, FieldInfo droppedCardsField, ref Array drops)
    {
        if (collection == null) return 0;
        int changes = 0;

        var collectionNameField = CachedField(collection.GetType(), "CollectionName");
        if (collectionNameField != null && collectionNameField.FieldType == typeof(string))
        {
            if (collectionNameField.GetValue(collection) == null)
            {
                collectionNameField.SetValue(collection, string.Empty);
                changes++;
            }
        }

        if ((drops == null || drops.Length == 0) && TryBuildLegacyDroppedCards(collection, droppedCardsField, out var rebuilt))
        {
            // Log diagnostic: this path resets Quantity to (1,1) if the collection's Quantity field is missing
            var legacyQty = ReflectionHelpers.GetFieldValue(collection, "Quantity");
            Log.Warn($"ProducedCardService: legacy DroppedCards rebuild triggered — DroppedCards was {(drops == null ? "null" : $"empty({drops.Length})")}, droppedCardsField={droppedCardsField?.Name ?? "null"}, collection type={collection.GetType().Name}, collection Quantity={legacyQty}");
            drops = rebuilt;
            changes++;
        }

        if (drops == null || drops.Length == 0) return changes;

        var collectionWeightField = CachedField(collection.GetType(), "CollectionWeight");
        if (collectionWeightField != null)
        {
            if (ReflectionHelpers.TryGetIntValue(collectionWeightField.GetValue(collection), out int weight) && weight <= 0)
            {
                collectionWeightField.SetValue(collection, 1);
                changes++;
            }
        }

        return changes;
    }

    private static bool TryBuildLegacyDroppedCards(object collection, FieldInfo droppedCardsField, out Array drops)
    {
        drops = null;
        if (collection == null || droppedCardsField == null || !droppedCardsField.FieldType.IsArray) return false;

        var dropType = droppedCardsField.FieldType.GetElementType();
        if (dropType == null) return false;

        object legacyCard = ReflectionHelpers.GetFieldValue(collection, "DroppedCard")
                         ?? ReflectionHelpers.GetFieldValue(collection, "Card");
        string legacyWarpData = ReflectionHelpers.GetFieldValue(collection, "DroppedCardWarpData") as string
                             ?? ReflectionHelpers.GetFieldValue(collection, "CardWarpData") as string;

        if (legacyCard == null && string.IsNullOrWhiteSpace(legacyWarpData)) return false;

        object drop;
        try { drop = Activator.CreateInstance(dropType); }
        catch { return false; }

        var droppedCardField = CachedField(dropType, "DroppedCard");
        if (legacyCard == null && !string.IsNullOrWhiteSpace(legacyWarpData) && droppedCardField != null)
            legacyCard = UniqueIDScriptable.GetFromID<CardData>(legacyWarpData);

        if (droppedCardField != null && legacyCard != null && droppedCardField.FieldType.IsInstanceOfType(legacyCard))
            droppedCardField.SetValue(drop, legacyCard);

        ReflectionHelpers.SetFieldValueIfPresent(dropType, drop, "DroppedCardWarpData", legacyWarpData);

        int legacyWarpType = 3;
        if (ReflectionHelpers.TryGetIntFieldValue(collection, "DroppedCardWarpType", out int dwt)) legacyWarpType = dwt;
        else if (ReflectionHelpers.TryGetIntFieldValue(collection, "CardWarpType", out int cwt)) legacyWarpType = cwt;
        ReflectionHelpers.SetNumericFieldIfPresent(dropType, drop, "DroppedCardWarpType", legacyWarpType);

        var quantityField = CachedField(dropType, "Quantity");
        var quantityValue = ReflectionHelpers.GetFieldValue(collection, "Quantity");
        if (!ReflectionHelpers.SetFieldValueIfPresent(dropType, drop, "Quantity", quantityValue))
        {
            if (quantityField != null)
            {
                var def = ReflectionHelpers.CreateVectorLike(quantityField.FieldType, 1, 1);
                if (def != null) quantityField.SetValue(drop, def);
            }
        }

        ReflectionHelpers.SetFieldValueIfPresent(dropType, drop, "FinishUnlocking",
            ReflectionHelpers.GetFieldValue(collection, "FinishUnlocking") is bool b ? (object)b : false);

        drops = Array.CreateInstance(dropType, 1);
        drops.SetValue(drop, 0);
        droppedCardsField.SetValue(collection, drops);
        return true;
    }

    /// <summary>
    /// Detects Quantity=(0,0) on a CardDrop and sets it to (1,1).
    /// Vector2Int is a value type — it defaults to (0,0) instead of null,
    /// so InitializeSerializableDefaults never touches it.
    /// </summary>
    private static int FixZeroQuantity(object drop)
    {
        if (drop == null) return 0;
        var quantityField = CachedField(drop.GetType(), "Quantity");
        if (quantityField == null) return 0;

        var val = quantityField.GetValue(drop);
        if (val is Vector2Int v && v.x == 0 && v.y == 0)
        {
            quantityField.SetValue(drop, new Vector2Int(1, 1));
            return 1;
        }
        return 0;
    }

    // ===== CLEANING =====

    private static int CleanCard(object card)
    {
        int total = 0;

        foreach (var fieldName in ActionArrayFields)
            total += CleanActionsArray(card, fieldName);

        foreach (var durName in DurabilityFields)
        {
            var durField = CachedField(card.GetType(), durName);
            if (durField == null) continue;
            var durVal = durField.GetValue(card);
            if (durVal == null) continue;

            foreach (var actionName in new[] { "OnZero", "OnFull" })
            {
                var actionField = CachedField(durVal.GetType(), actionName);
                if (actionField == null) continue;
                var actionVal = actionField.GetValue(durVal);
                if (actionVal == null) continue;
                int c = CleanProducedCardsOnAction(actionVal);
                if (c > 0 && durField.FieldType.IsValueType)
                {
                    actionField.SetValue(durVal, actionVal);
                    durField.SetValue(card, durVal);
                }
                total += c;
            }
        }
        return total;
    }

    private static int CleanActionsArray(object owner, string fieldName)
    {
        var field = CachedField(owner.GetType(), fieldName);
        if (field == null) return 0;
        var actionsValue = field.GetValue(owner);
        if (actionsValue == null) return 0;

        int total = 0;
        if (actionsValue is Array actionsArray)
        {
            for (int i = 0; i < actionsArray.Length; i++)
            {
                var action = actionsArray.GetValue(i);
                if (action == null) continue;
                total += CleanProducedCardsOnAction(action);
            }
        }
        else if (actionsValue is IList actionsList)
        {
            for (int i = 0; i < actionsList.Count; i++)
            {
                var action = actionsList[i];
                if (action == null) continue;
                total += CleanProducedCardsOnAction(action);
            }
        }
        return total;
    }

    private static int CleanProducedCardsOnAction(object action)
    {
        var producedField = CachedField(action.GetType(), "ProducedCards");
        if (producedField == null) return 0;
        var produced = producedField.GetValue(action) as Array;
        if (produced == null || produced.Length == 0) return 0;

        int total = 0;
        for (int c = 0; c < produced.Length; c++)
        {
            var collection = produced.GetValue(c);
            if (collection == null) continue;

            var droppedCardsField = CachedField(collection.GetType(), "DroppedCards");
            if (droppedCardsField == null) continue;
            var drops = droppedCardsField.GetValue(collection) as Array;
            if (drops == null || drops.Length == 0) continue;

            var elemType = drops.GetType().GetElementType();
            var droppedCardFieldInfo = elemType != null ? CachedField(elemType, "DroppedCard") : null;
            if (droppedCardFieldInfo == null) continue;

            var warpFieldInfo = elemType.GetField("DroppedCardWarpData", AllInstance)
                             ?? elemType.GetField("CardWarpData", AllInstance);

            var validDrops = new List<object>();
            bool anyModified = false;

            for (int d = 0; d < drops.Length; d++)
            {
                var drop = drops.GetValue(d);
                if (drop == null) continue;

                var card = droppedCardFieldInfo.GetValue(drop);
                var warpData = warpFieldInfo?.GetValue(drop) as string;

                // Try to resolve unresolved references
                if ((card == null || (card is UnityEngine.Object uo && uo == null))
                    && !string.IsNullOrWhiteSpace(warpData))
                {
                    var resolved = UniqueIDScriptable.GetFromID<CardData>(warpData);
                    if (resolved != null && droppedCardFieldInfo.FieldType.IsInstanceOfType(resolved))
                    {
                        droppedCardFieldInfo.SetValue(drop, resolved);
                        card = resolved;
                        anyModified = true;
                    }
                }

                bool hasCard = card != null && !(card is UnityEngine.Object nullCard && nullCard == null);
                bool hasWarp = !string.IsNullOrWhiteSpace(warpData);
                if (hasCard || hasWarp)
                    validDrops.Add(drop);
                else
                    total++;
            }

            if (validDrops.Count < drops.Length || anyModified)
            {
                var newArr = Array.CreateInstance(elemType, validDrops.Count);
                for (int d = 0; d < validDrops.Count; d++)
                    newArr.SetValue(validDrops[d], d);
                droppedCardsField.SetValue(collection, newArr);

                if (collection.GetType().IsValueType)
                    produced.SetValue(collection, c);
            }
        }
        return total;
    }

}
