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

    // Caches for InitializeNullFields — same pattern as NullReferenceCompactor.
    // Called per-effect per-card; caching eliminates repeated GetFields on the same types.
    private static readonly Dictionary<Type, FieldInfo[]> _initFieldsCache = new();

    // Caches for ResolveStatRefsInArray / ResolveReference field lookups.
    // Keyed (type, fieldName) to match the WarpResolver pattern.
    private static readonly Dictionary<(Type, string), FieldInfo> _fieldLookupCache = new();

    private static FieldInfo CachedField(Type type, string name)
    {
        var key = (type, name);
        if (!_fieldLookupCache.TryGetValue(key, out var fi))
            _fieldLookupCache[key] = fi = type.GetField(name, InstanceFlags);
        return fi;
    }

    public static void NormalizeAll(IEnumerable allData, List<ModManifest> mods)
    {
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

            // UniqueIDScriptable is the common base for all allData entries.
            // Direct cast avoids per-item uncached GetField reflection.
            if (!(item is UniqueIDScriptable uidItem)
                || string.IsNullOrEmpty(uidItem.UniqueID)
                || !modUniqueIds.Contains(uidItem.UniqueID)) continue;

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
                        int refs = ResolveStatRefsInArray(effect, "StatModifiers");
                        refsResolved += refs;

                        // Handle Conditions sub-object (may be a value type)
                        var condField = CachedField(effect.GetType(), "Conditions");
                        if (condField != null)
                        {
                            var cond = condField.GetValue(effect);
                            if (cond != null)
                            {
                                int condNulls = InitializeNullFields(cond);
                                fieldsFixed += condNulls;

                                refsResolved += ResolveStatRefsInArray(cond, "RequiredStatValues");
                                refsResolved += ResolveStatRefsInArray(cond, "RequiredNPCStatValues");

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
    /// Resolve StatWarpData → Stat on each element of a named array field.
    /// </summary>
    private static int ResolveStatRefsInArray(object owner, string arrayFieldName)
    {
        if (owner == null) return 0;

        var arrayField = CachedField(owner.GetType(), arrayFieldName);
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

            if (ResolveReference(entry, "StatWarpData", "Stat"))
                resolved++;

            if (isValueType)
                arr.SetValue(entry, i);
        }

        if (isValueType && resolved > 0)
            arrayField.SetValue(owner, arr);

        return resolved;
    }

    /// <summary>
    /// If referenceField is null but warpField has a key, resolve it.
    /// Uses GameRegistry + Database directly instead of a per-call reference map —
    /// eliminates the BuildReferenceMap allData sweep that previously iterated all
    /// ~4000 objects just to build a dict that duplicated GameRegistry.
    /// </summary>
    private static bool ResolveReference(object target, string warpFieldName, string referenceFieldName)
    {
        var referenceField = CachedField(target.GetType(), referenceFieldName);
        var warpField      = CachedField(target.GetType(), warpFieldName);
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

        // Try game's UID registry first (covers all UniqueIDScriptable: cards, perks, stats).
        object resolved = Data.GameRegistry.GetByUid(warpId);

        // Fallback: SO name lookup (covers GameStats, SpiceTags, and any SO keyed by .name).
        if (resolved == null && Data.Database.AllScriptableObjectDict.TryGetValue(warpId, out var byName))
            resolved = byName;

        if (resolved == null) return false;
        if (!referenceField.FieldType.IsAssignableFrom(resolved.GetType())) return false;

        referenceField.SetValue(target, resolved);
        return true;
    }

    /// <summary>
    /// For each non-value, non-string, non-UnityObject field on target that is null,
    /// create an empty instance (empty array for array types, default ctor for classes).
    /// Field list is cached per type to avoid repeated GetFields calls.
    /// </summary>
    private static int InitializeNullFields(object target)
    {
        if (target == null) return 0;
        int count = 0;

        var type = target.GetType();
        if (!_initFieldsCache.TryGetValue(type, out var fields))
        {
            // Filter once and cache — keeps only fields that can ever be null-initialized.
            fields = type.GetFields(InstanceFlags)
                         .Where(f => !f.IsInitOnly && !f.IsLiteral
                                     && !f.FieldType.IsValueType
                                     && f.FieldType != typeof(string)
                                     && !typeof(UnityEngine.Object).IsAssignableFrom(f.FieldType))
                         .ToArray();
            _initFieldsCache[type] = fields;
        }

        foreach (var field in fields)
        {
            if (field.GetValue(target) != null) continue;

            var ft = field.FieldType;
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
