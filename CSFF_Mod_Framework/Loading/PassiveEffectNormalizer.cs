using CSFFModFramework.Discovery;
using CSFFModFramework.Util;

namespace CSFFModFramework.Loading;

/// <summary>
/// Initializes null sub-fields inside PassiveEffects on all cards AND resolves
/// StatWarpData → Stat references that WarpResolver cannot reach (because they
/// are nested inside struct arrays).
///
/// Without stat resolution, UpdatePassiveEffectStacks crashes with NullRef when
/// it tries to read StatModifier.Stat or Condition stat values.
/// </summary>
internal static class PassiveEffectNormalizer
{
    private static readonly BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static void NormalizeAll(IEnumerable allData, List<ModManifest> mods)
    {
        // Build a reference map for stat resolution (GameStats + all UniqueIDScriptable)
        var referenceMap = BuildReferenceMap(allData);

        // Reuse mod UniqueIDs cached by JsonDataLoader (avoids redundant filesystem scan)
        var modUniqueIds = JsonDataLoader.AllModUniqueIds;

        int cardsFixed = 0;
        int fieldsFixed = 0;
        int refsResolved = 0;

        // Both field names hold arrays of PassiveEffect-like objects and need the same treatment.
        // EffectsToInventoryContent was previously skipped, causing NullRef in PassiveEffect.Instantiate
        // when items were dropped into mod containers.
        string[] effectArrayFieldNames = { "PassiveEffects", "EffectsToInventoryContent" };

        foreach (var item in allData)
        {
            if (item == null) continue;

            // Only normalize mod items — never touch vanilla data
            var uid = item.GetType().GetField("UniqueID", InstanceFlags)?.GetValue(item) as string;
            if (string.IsNullOrEmpty(uid) || !modUniqueIds.Contains(uid)) continue;

            try
            {
                bool cardFixed = false;

                foreach (var fieldName in effectArrayFieldNames)
                {
                    var effectsField = item.GetType().GetField(fieldName, InstanceFlags);
                    if (effectsField == null) continue;

                    var arr = effectsField.GetValue(item) as Array;
                    if (arr == null || arr.Length == 0) continue;

                    var elemType = arr.GetType().GetElementType();
                    bool isValueType = elemType != null && elemType.IsValueType;

                    for (int i = 0; i < arr.Length; i++)
                    {
                        var effect = arr.GetValue(i);
                        if (effect == null) continue;

                        int nullsFixed = InitializeNullFields(effect);
                        fieldsFixed += nullsFixed;

                        // Resolve stat references in StatModifiers
                        int refs = ResolveStatRefsInArray(effect, "StatModifiers", referenceMap);
                        refsResolved += refs;

                        // Handle Conditions sub-object (may be a value type)
                        var condField = effect.GetType().GetField("Conditions", InstanceFlags);
                        if (condField != null)
                        {
                            var cond = condField.GetValue(effect);
                            if (cond != null)
                            {
                                int condNulls = InitializeNullFields(cond);
                                fieldsFixed += condNulls;

                                refsResolved += ResolveStatRefsInArray(cond, "RequiredStatValues", referenceMap);
                                refsResolved += ResolveStatRefsInArray(cond, "RequiredNPCStatValues", referenceMap);

                                if (condField.FieldType.IsValueType)
                                    condField.SetValue(effect, cond);

                                if (condNulls > 0) cardFixed = true;
                            }
                        }

                        if (nullsFixed > 0 || refs > 0) cardFixed = true;
                        if (isValueType)
                            arr.SetValue(effect, i);
                    }
                }

                if (cardFixed) cardsFixed++;
            }
            catch { }
        }

        if (cardsFixed > 0 || refsResolved > 0)
            Log.Debug($"PassiveEffectNormalizer: fixed {fieldsFixed} null fields across {cardsFixed} cards, resolved {refsResolved} stat references");
        else if (fieldsFixed == 0)
            Log.Debug("PassiveEffectNormalizer: no fixes needed");
    }

    /// <summary>
    /// Build a lookup map: name/UniqueID → ScriptableObject.
    /// Covers GameStats (which are ScriptableObjects but not UniqueIDScriptable)
    /// and all cards/perks in AllData.
    /// </summary>
    private static Dictionary<string, object> BuildReferenceMap(IEnumerable allData)
    {
        var map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        // All UniqueIDScriptable from AllData (keyed by UniqueID)
        foreach (var item in allData)
        {
            if (item == null) continue;
            var uid = item.GetType().GetField("UniqueID", InstanceFlags)?.GetValue(item) as string;
            if (!string.IsNullOrEmpty(uid) && !map.ContainsKey(uid))
                map[uid] = item;
        }

        // Reuse Database's pre-built SO cache (covers GameStats, SpiceTags, etc.)
        // instead of calling FindObjectsOfTypeAll again
        foreach (var kvp in Data.Database.AllScriptableObjectDict)
        {
            if (!map.ContainsKey(kvp.Key))
                map[kvp.Key] = kvp.Value;
        }

        return map;
    }

    /// <summary>
    /// Resolve StatWarpData → Stat on each element of a named array field.
    /// </summary>
    private static int ResolveStatRefsInArray(object owner, string arrayFieldName, Dictionary<string, object> referenceMap)
    {
        if (owner == null) return 0;

        var arrayField = owner.GetType().GetField(arrayFieldName, InstanceFlags);
        if (arrayField == null) return 0;

        var arr = arrayField.GetValue(owner) as Array;
        if (arr == null || arr.Length == 0) return 0;

        var elemType = arr.GetType().GetElementType();
        bool isValueType = elemType != null && elemType.IsValueType;
        int resolved = 0;

        for (int i = 0; i < arr.Length; i++)
        {
            var entry = arr.GetValue(i);
            if (entry == null) continue;

            InitializeNullFields(entry);

            if (ResolveReference(entry, "StatWarpData", "Stat", referenceMap))
                resolved++;

            if (isValueType)
                arr.SetValue(entry, i);
        }

        if (isValueType && resolved > 0)
            arrayField.SetValue(owner, arr);

        return resolved;
    }

    /// <summary>
    /// If referenceField is null but warpField has a key, resolve it from the map.
    /// </summary>
    private static bool ResolveReference(object target, string warpFieldName, string referenceFieldName, Dictionary<string, object> referenceMap)
    {
        var referenceField = target.GetType().GetField(referenceFieldName, InstanceFlags);
        var warpField = target.GetType().GetField(warpFieldName, InstanceFlags);
        if (referenceField == null || warpField == null) return false;

        // Check if already resolved (handle Unity fake-null)
        var current = referenceField.GetValue(target);
        if (current != null)
        {
            if (current is not UnityEngine.Object unityObj || unityObj != null)
                return false;
        }

        var warpId = warpField.GetValue(target) as string;
        if (string.IsNullOrWhiteSpace(warpId)) return false;

        if (!referenceMap.TryGetValue(warpId, out var resolved)) return false;
        if (!referenceField.FieldType.IsAssignableFrom(resolved.GetType())) return false;

        referenceField.SetValue(target, resolved);
        return true;
    }

    /// <summary>
    /// For each non-value, non-string, non-UnityObject field on target that is null,
    /// create an empty instance (empty array for array types, default ctor for classes).
    /// </summary>
    private static int InitializeNullFields(object target)
    {
        if (target == null) return 0;
        int count = 0;

        foreach (var field in target.GetType().GetFields(InstanceFlags))
        {
            if (field.IsInitOnly || field.IsLiteral) continue;

            var ft = field.FieldType;
            if (ft.IsValueType || ft == typeof(string) || typeof(UnityEngine.Object).IsAssignableFrom(ft))
                continue;

            if (field.GetValue(target) != null) continue;

            if (ft.IsArray)
            {
                field.SetValue(target, Array.CreateInstance(ft.GetElementType() ?? typeof(object), 0));
                count++;
            }
            else if (!ft.IsAbstract && !ft.IsInterface)
            {
                try
                {
                    field.SetValue(target, Activator.CreateInstance(ft, true));
                    count++;
                }
                catch { }
            }
        }
        return count;
    }
}
