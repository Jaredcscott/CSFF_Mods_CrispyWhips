using CSFFModFramework.Discovery;
using CSFFModFramework.Util;

namespace CSFFModFramework.Data;

/// <summary>
/// Resolves WarpData references across ALL mods. Adapted from the proven
/// HerbsAndFungi WarpDataResolver but without mod-prefix filtering.
/// </summary>
internal static class WarpResolver
{
    private static readonly BindingFlags AllInstance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // Cache FieldInfo lookups by (Type, fieldName). Walk() calls GetField repeatedly
    // on the same runtime types (CardData, CharacterPerk, etc.) across hundreds of JSON
    // files — caching eliminates thousands of redundant reflection lookups per load.
    // Stores nulls so absent-field lookups don't keep paying either.
    private static readonly Dictionary<(Type, string), FieldInfo> _fieldCache = new();

    private static FieldInfo CachedField(Type type, string name)
    {
        var key = (type, name);
        if (!_fieldCache.TryGetValue(key, out var field))
        {
            field = type.GetField(name, AllInstance);
            _fieldCache[key] = field;
        }
        return field;
    }

    // Tracking counters for summary logging (reset per ResolveAll)
    private static int _runtimeCreatedCount;
    private static int _triggerResolveCount;
    private static readonly Dictionary<string, HashSet<string>> _unresolvedByMod = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> _unresolvedAudioClips = new(StringComparer.OrdinalIgnoreCase);
    private static string _currentUid = "";

    /// <summary>
    /// Resolve all WarpData references for all mod JSON files.
    /// </summary>
    public static void ResolveAll(IEnumerable allData, List<ModManifest> mods)
    {
        _runtimeCreatedCount = 0;
        _triggerResolveCount = 0;
        _unresolvedByMod.Clear();
        _unresolvedAudioClips.Clear();
        _currentUid = "";

        // Reuse JSON content cached by JsonDataLoader (avoids redundant filesystem reads)
        var jsonByUid = Loading.JsonDataLoader.JsonByUniqueId;

        if (jsonByUid.Count == 0)
        {
            Log.Info("WarpResolver: no JSON files found across mods");
            return;
        }

        // Build runtime object map — ALL objects, not filtered by prefix
        var runtimeMap = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in allData)
        {
            if (item is UniqueIDScriptable s && !string.IsNullOrEmpty(s.UniqueID))
                runtimeMap[s.UniqueID] = item;
        }

        int totalResolved = 0;
        int filesProcessed = 0;

        foreach (var kvp in jsonByUid)
        {
            if (!runtimeMap.TryGetValue(kvp.Key, out var runtimeObj))
                continue;

            _currentUid = kvp.Key;
            try
            {
                var tree = MiniJson.Parse(kvp.Value);
                if (tree is Dictionary<string, object> root)
                {
                    int n = Walk(runtimeObj, root);
                    if (n > 0) totalResolved += n;
                    filesProcessed++;
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"WarpResolver: error processing {kvp.Key}: {ex.Message}");
            }
        }
        _currentUid = "";

        if (_unresolvedAudioClips.Count > 0)
            Log.Debug($"WarpResolver: {_unresolvedAudioClips.Count} unique AudioClip refs deferred (bundle-loaded): {string.Join(", ", _unresolvedAudioClips)}");

        int totalUnresolved = 0;
        foreach (var kv in _unresolvedByMod.OrderBy(x => x.Key))
        {
            totalUnresolved += kv.Value.Count;
            Log.Debug($"WarpResolver: [{kv.Key}] unresolved: {string.Join(", ", kv.Value)}");
        }

        Log.Info($"WarpResolver: processed {filesProcessed} files, resolved {totalResolved} references, created {_runtimeCreatedCount} runtime tags, {_triggerResolveCount} trigger refs, {totalUnresolved} unresolved");
        _runtimeCreatedCount = 0;
        _triggerResolveCount = 0;
        _unresolvedByMod.Clear();
        _unresolvedAudioClips.Clear();
        _currentUid = "";
    }

    /// <summary>
    /// Walk a single runtime object against its JSON tree, resolving WarpData references.
    /// Can be called standalone (e.g. after GameSourceModify patches).
    /// </summary>
    public static int Walk(object rtObj, Dictionary<string, object> json)
    {
        if (rtObj == null || json == null) return 0;

        int resolved = 0;
        var rtType = rtObj.GetType();

        // --- Pass 1: resolve *WarpData fields at this level ---
        foreach (var key in json.Keys.ToArray())
        {
            if (!key.EndsWith("WarpData", StringComparison.Ordinal)) continue;

            var baseName = key.Substring(0, key.Length - 8); // strip "WarpData"
            var typeKey = baseName + "WarpType";

            // Read WarpType
            int warpType = 0;
            if (json.TryGetValue(typeKey, out var tv))
            {
                if (tv is double d) warpType = (int)d;
                else if (tv is long l) warpType = (int)l;
            }

            // Support WarpType 3 (Reference), 4 (Add), 5 (Modify), 6 (AddReference)
            if (warpType != 3 && warpType != 4 && warpType != 5 && warpType != 6) continue;

            var field = CachedField(rtType, baseName);
            if (field == null)
            {
                if (baseName == "TriggerCards" || baseName == "TriggerTags")
                    Log.Debug($"WarpResolver: field '{baseName}' not found on {rtType.Name}");
                continue;
            }

            // Count TriggerCards/TriggerTags resolutions for summary (verbose logs per-item)
            if (baseName == "TriggerCards" || baseName == "TriggerTags")
            {
                _triggerResolveCount++;
                Log.Debug($"WarpResolver: resolving {key} on {rtType.Name}, warpType={warpType}, value={json[key]}");
            }

            var warpVal = json[key];

            // Single string -> single reference
            if (warpVal is string singleId)
            {
                if (warpType == 3 && !NeedsResolve(field.GetValue(rtObj))) continue;

                var obj = Lookup(singleId, field.FieldType);
                if (obj != null)
                {
                    field.SetValue(rtObj, obj);
                    resolved++;
                }
            }
            // Array of strings -> array/list of references
            else if (warpVal is List<object> idList && idList.Count > 0)
            {
                var cur = field.GetValue(rtObj);

                // For WarpType 3, skip if already resolved
                if (warpType == 3 && cur is Array arr && arr.Length > 0 && !AllNull(arr))
                    continue;

                var elemType = field.FieldType.IsArray
                    ? field.FieldType.GetElementType()
                    : field.FieldType.IsGenericType
                        ? field.FieldType.GetGenericArguments()[0]
                        : typeof(UniqueIDScriptable);

                var items = new List<object>();
                foreach (var item in idList)
                {
                    if (item is string id)
                    {
                        var obj = Lookup(id, elemType);
                        if (obj != null) items.Add(obj);
                    }
                }

                if (items.Count > 0)
                {
                    if (warpType == 4 || warpType == 6)
                    {
                        // Add/AddReference: append to existing array
                        resolved += AppendToField(rtObj, field, elemType, items);
                    }
                    else
                    {
                        // Replace — handle both Array and List<T> field types
                        var fieldElem = GetFieldElementType(field.FieldType) ?? elemType;
                        if (field.FieldType.IsArray)
                        {
                            var newArr = Array.CreateInstance(fieldElem, items.Count);
                            for (int i = 0; i < items.Count; i++)
                                newArr.SetValue(items[i], i);
                            field.SetValue(rtObj, newArr);
                        }
                        else
                        {
                            var listType = typeof(List<>).MakeGenericType(fieldElem);
                            var list = (IList)Activator.CreateInstance(listType);
                            foreach (var it in items) list.Add(it);
                            field.SetValue(rtObj, list);
                        }
                        resolved += items.Count;
                    }
                }

                // WarpType 5 (Modify) with dict elements: walk each non-empty dict against
                // the corresponding existing array/list element in-place.
                // Handles GameSourceModify patches like Greenstone's DismantleActionsWarpData,
                // where nested objects are modified rather than replaced by reference.
                if (warpType == 5)
                {
                    var existingVal = field.GetValue(rtObj);
                    if (existingVal is Array modArr)
                    {
                        var eType = modArr.GetType().GetElementType();
                        int n = Math.Min(idList.Count, modArr.Length);
                        for (int i = 0; i < n; i++)
                        {
                            if (idList[i] is Dictionary<string, object> elemDict && elemDict.Count > 0)
                            {
                                var elem = modArr.GetValue(i);
                                if (elem == null) continue;
                                resolved += Walk(elem, elemDict);
                                if (eType?.IsValueType == true)
                                    modArr.SetValue(elem, i);
                            }
                        }
                    }
                    else if (existingVal is IList modList)
                    {
                        int n = Math.Min(idList.Count, modList.Count);
                        for (int i = 0; i < n; i++)
                        {
                            if (idList[i] is Dictionary<string, object> elemDict && elemDict.Count > 0)
                            {
                                var elem = modList[i];
                                if (elem == null) continue;
                                resolved += Walk(elem, elemDict);
                                if (elem?.GetType().IsValueType == true)
                                    modList[i] = elem;
                            }
                        }
                    }
                }

                // WarpType 4/6 (Add) with dict elements: create new instances, walk each dict
                // to populate fields (including nested WarpData), then append to existing array.
                // Handles cases like DroppedCardsWarpData with WarpType 4 containing new
                // CardDrop object definitions rather than string ID references.
                if ((warpType == 4 || warpType == 6) && elemType != null && IsSerializableType(elemType))
                {
                    var newElems = new List<object>();
                    foreach (var item in idList)
                    {
                        if (item is not Dictionary<string, object> elemDict || elemDict.Count == 0) continue;
                        try
                        {
                            var newElem = Activator.CreateInstance(elemType);
                            Walk(newElem, elemDict);
                            newElems.Add(newElem);
                        }
                        catch { }
                    }
                    if (newElems.Count > 0)
                        resolved += AppendToField(rtObj, field, elemType, newElems);
                }
            }
        }

        // --- Pass 1.5: apply simple scalar/vector values from JSON ---
        // JsonUtility.FromJsonOverwrite may fail to populate fields in deeply nested
        // struct arrays (e.g., CardDrop.Quantity inside ProducedCards). This pass
        // explicitly applies int, float, bool, string, and Vector2Int values from JSON.
        foreach (var kvp in json)
        {
            if (kvp.Key.EndsWith("WarpData", StringComparison.Ordinal)
                || kvp.Key.EndsWith("WarpType", StringComparison.Ordinal))
                continue;

            var field = CachedField(rtType, kvp.Key);
            if (field == null) continue;

            // Vector2Int from JSON dict {"x": N, "y": M}
            if (field.FieldType == typeof(Vector2Int) && kvp.Value is Dictionary<string, object> vecDict)
            {
                int vx = 0, vy = 0;
                if (vecDict.TryGetValue("x", out var xv)) { if (xv is double dx) vx = (int)dx; else if (xv is long lx) vx = (int)lx; }
                if (vecDict.TryGetValue("y", out var yv)) { if (yv is double dy) vy = (int)dy; else if (yv is long ly) vy = (int)ly; }
                var cur = (Vector2Int)field.GetValue(rtObj);
                if (cur.x != vx || cur.y != vy)
                {
                    field.SetValue(rtObj, new Vector2Int(vx, vy));
                    resolved++;
                }
                continue;
            }

            // Vector2 from JSON dict {"x": N, "y": M}
            if (field.FieldType == typeof(Vector2) && kvp.Value is Dictionary<string, object> vec2Dict)
            {
                float fx = 0f, fy = 0f;
                if (vec2Dict.TryGetValue("x", out var xv2)) { if (xv2 is double dx2) fx = (float)dx2; }
                if (vec2Dict.TryGetValue("y", out var yv2)) { if (yv2 is double dy2) fy = (float)dy2; }
                var cur2 = (Vector2)field.GetValue(rtObj);
                if (cur2.x != fx || cur2.y != fy)
                {
                    field.SetValue(rtObj, new Vector2(fx, fy));
                    resolved++;
                }
                continue;
            }

            // int
            if (field.FieldType == typeof(int) && (kvp.Value is double dv || kvp.Value is long))
            {
                int iv = kvp.Value is double d2 ? (int)d2 : (int)(long)kvp.Value;
                if ((int)field.GetValue(rtObj) != iv)
                {
                    field.SetValue(rtObj, iv);
                    resolved++;
                }
                continue;
            }

            // float
            if (field.FieldType == typeof(float) && kvp.Value is double fd)
            {
                float fv = (float)fd;
                if ((float)field.GetValue(rtObj) != fv)
                {
                    field.SetValue(rtObj, fv);
                    resolved++;
                }
                continue;
            }

            // bool
            if (field.FieldType == typeof(bool) && kvp.Value is bool bv)
            {
                if ((bool)field.GetValue(rtObj) != bv)
                {
                    field.SetValue(rtObj, bv);
                    resolved++;
                }
                continue;
            }

            // string
            if (field.FieldType == typeof(string) && kvp.Value is string sv)
            {
                if ((string)field.GetValue(rtObj) != sv)
                {
                    field.SetValue(rtObj, sv);
                    resolved++;
                }
                continue;
            }
        }

        // --- Pass 2: recurse into nested arrays and serializable objects ---
        foreach (var kvp in json)
        {
            if (kvp.Key.EndsWith("WarpData", StringComparison.Ordinal)
                || kvp.Key.EndsWith("WarpType", StringComparison.Ordinal))
                continue;

            var field = CachedField(rtType, kvp.Key);
            if (field == null) continue;

            // Array field -> walk elements in parallel
            if (kvp.Value is List<object> jsonArr)
            {
                var rtVal = field.GetValue(rtObj);

                if (rtVal is Array rtArr)
                {
                    var eType = rtArr.GetType().GetElementType();
                    if (eType == null) continue;
                    bool isValueType = eType.IsValueType;

                    // Expand array if JSON has more elements than runtime
                    if (jsonArr.Count > rtArr.Length && IsSerializableType(eType))
                    {
                        rtArr = ExpandArray(rtArr, eType, jsonArr.Count);
                        field.SetValue(rtObj, rtArr);
                        resolved++;
                    }

                    int n = Math.Min(jsonArr.Count, rtArr.Length);
                    for (int i = 0; i < n; i++)
                    {
                        if (jsonArr[i] is Dictionary<string, object> elemDict)
                        {
                            var elem = rtArr.GetValue(i);
                            // Create null elements if JSON has data for them
                            if (elem == null && IsSerializableType(eType))
                            {
                                try { elem = Activator.CreateInstance(eType); rtArr.SetValue(elem, i); resolved++; }
                                catch { continue; }
                            }
                            if (elem == null) continue;
                            int r = Walk(elem, elemDict);
                            resolved += r;
                            if (isValueType)
                                rtArr.SetValue(elem, i);
                        }
                    }
                }
                else if (rtVal is IList rtList)
                {
                    var listElemType = GetFieldElementType(field.FieldType);

                    // Expand list if JSON has more elements than runtime
                    if (listElemType != null && jsonArr.Count > rtList.Count && IsSerializableType(listElemType))
                    {
                        for (int i = rtList.Count; i < jsonArr.Count; i++)
                        {
                            try { rtList.Add(Activator.CreateInstance(listElemType)); resolved++; }
                            catch { break; }
                        }
                    }

                    int n = Math.Min(jsonArr.Count, rtList.Count);
                    for (int i = 0; i < n; i++)
                    {
                        if (jsonArr[i] is Dictionary<string, object> elemDict)
                        {
                            var elem = rtList[i];
                            if (elem == null && listElemType != null && IsSerializableType(listElemType))
                            {
                                try { elem = Activator.CreateInstance(listElemType); rtList[i] = elem; resolved++; }
                                catch { continue; }
                            }
                            if (elem == null) continue;
                            int r = Walk(elem, elemDict);
                            resolved += r;
                            if (elem != null && elem.GetType().IsValueType)
                                rtList[i] = elem;
                        }
                    }
                }
                // Null/missing runtime value — create the array from scratch if JSON has data
                else if (rtVal == null && field.FieldType.IsArray)
                {
                    var eType = field.FieldType.GetElementType();
                    if (eType != null && IsSerializableType(eType) && jsonArr.Count > 0)
                    {
                        var newArr = Array.CreateInstance(eType, jsonArr.Count);
                        for (int i = 0; i < jsonArr.Count; i++)
                        {
                            try { newArr.SetValue(Activator.CreateInstance(eType), i); }
                            catch { }
                        }
                        field.SetValue(rtObj, newArr);
                        resolved++;

                        // Walk the newly created array
                        for (int i = 0; i < jsonArr.Count; i++)
                        {
                            if (jsonArr[i] is Dictionary<string, object> elemDict)
                            {
                                var elem = newArr.GetValue(i);
                                if (elem == null) continue;
                                int r = Walk(elem, elemDict);
                                resolved += r;
                                if (eType.IsValueType)
                                    newArr.SetValue(elem, i);
                            }
                        }
                    }
                }
            }
            // Nested object -> recurse into serializable (non-Unity.Object) fields
            else if (kvp.Value is Dictionary<string, object> nested)
            {
                if (typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType))
                    continue;

                var rtVal = field.GetValue(rtObj);

                // Create null serializable objects if JSON has data for them
                if (rtVal == null && IsSerializableType(field.FieldType))
                {
                    try { rtVal = Activator.CreateInstance(field.FieldType); field.SetValue(rtObj, rtVal); resolved++; }
                    catch { continue; }
                }

                if (rtVal != null)
                {
                    int r = Walk(rtVal, nested);
                    resolved += r;
                    if (field.FieldType.IsValueType)
                        field.SetValue(rtObj, rtVal);
                }
            }
        }

        return resolved;
    }

    private static int AppendToField(object rtObj, FieldInfo field, Type elemType, List<object> newItems)
    {
        var cur = field.GetValue(rtObj);
        var existing = new List<object>();

        if (cur is Array curArr)
        {
            for (int i = 0; i < curArr.Length; i++)
            {
                var e = curArr.GetValue(i);
                if (e != null && !(e is UnityEngine.Object uo && uo == null))
                    existing.Add(e);
            }
        }
        else if (cur is IList curList)
        {
            foreach (var e in curList)
            {
                if (e != null && !(e is UnityEngine.Object uo && uo == null))
                    existing.Add(e);
            }
        }

        existing.AddRange(newItems);

        // Derive element type from field to handle List<T> vs array correctly,
        // even if the passed-in elemType is a base type (e.g., UniqueIDScriptable)
        var fieldElemType = GetFieldElementType(field.FieldType) ?? elemType;

        if (field.FieldType.IsArray)
        {
            var result = Array.CreateInstance(fieldElemType, existing.Count);
            for (int i = 0; i < existing.Count; i++)
                result.SetValue(existing[i], i);
            field.SetValue(rtObj, result);
        }
        else if (cur is IList)
        {
            // Field is a List<T> — append directly to the existing list to preserve the reference
            var curListRef = (IList)cur;
            foreach (var item in newItems) curListRef.Add(item);
        }
        else
        {
            var listType = typeof(List<>).MakeGenericType(fieldElemType);
            var list = (IList)Activator.CreateInstance(listType);
            foreach (var e in existing) list.Add(e);
            field.SetValue(rtObj, list);
        }
        return newItems.Count;
    }

    /// <summary>
    /// Extract the element type from an array or generic collection field type.
    /// Returns null if the type is not recognized as a collection.
    /// </summary>
    private static Type GetFieldElementType(Type fieldType)
    {
        if (fieldType.IsArray)
            return fieldType.GetElementType();
        if (fieldType.IsGenericType)
            return fieldType.GetGenericArguments()[0];
        // Fallback: check for IList<T> interfaces (covers cases where IsGenericType is unreliable)
        foreach (var iface in fieldType.GetInterfaces())
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IList<>))
                return iface.GetGenericArguments()[0];
        }
        return null;
    }

    private static bool NeedsResolve(object val)
    {
        if (val == null) return true;
        if (val is UnityEngine.Object uo && uo == null) return true;
        return false;
    }

    private static bool AllNull(Array arr)
    {
        for (int i = 0; i < arr.Length; i++)
        {
            var e = arr.GetValue(i);
            if (e != null && !(e is UnityEngine.Object uo && uo == null))
                return false;
        }
        return true;
    }

    // Lazy-built caches by asset type
    private static Dictionary<string, ScriptableObject> _soNameCache;
    private static Dictionary<string, Sprite> _spriteCache;
    private static Dictionary<string, AudioClip> _audioClipCache;

    private static Dictionary<string, ScriptableObject> GetSONameCache()
    {
        if (_soNameCache == null)
        {
            // Reuse Database's pre-built cache instead of re-scanning all resources
            _soNameCache = Database.AllScriptableObjectDict;
            Log.Debug($"WarpResolver: reused Database SO cache with {_soNameCache.Count} entries");
        }
        return _soNameCache;
    }

    private static Dictionary<string, Sprite> GetSpriteCache()
    {
        if (_spriteCache == null)
        {
            // Reuse Database's pre-built sprite cache
            _spriteCache = Database.SpriteDict;
            Log.Debug($"WarpResolver: reused Database sprite cache with {_spriteCache.Count} entries");
        }
        return _spriteCache;
    }

    private static Dictionary<string, AudioClip> GetAudioClipCache()
    {
        if (_audioClipCache == null)
        {
            // Reuse Database's pre-built audio clip cache
            _audioClipCache = Database.AudioClipDict;
            Log.Debug($"WarpResolver: reused Database audio clip cache with {_audioClipCache.Count} entries");
        }
        return _audioClipCache;
    }

    // Non-UniqueIDScriptable types that can safely be created at runtime when not found.
    // These are simple tag/category objects used for classification, not complex data holders.
    private static readonly HashSet<string> _creatableTypeNames = new(StringComparer.Ordinal)
    {
        "CardTag", "ActionTag", "EquipmentTag", "CardTabGroup"
    };

    private static bool IsCreatableTagType(Type t)
    {
        // Check the type and its base types — the actual runtime type name may differ
        var current = t;
        while (current != null && current != typeof(object))
        {
            if (_creatableTypeNames.Contains(current.Name))
                return true;
            current = current.BaseType;
        }
        return false;
    }

    /// <summary>
    /// Look up a runtime object by UniqueID or name.
    ///   UniqueIDScriptable → GetFromID(key)
    ///   Other ScriptableObject types → Database per-type dict lookup by name
    /// </summary>
    public static object Lookup(string id, Type fieldType)
    {
        if (string.IsNullOrEmpty(id)) return null;

        // Determine the actual element type for assignment checking
        var targetType = fieldType.IsArray ? fieldType.GetElementType() : fieldType;
        if (targetType == null) targetType = fieldType;

        // --- Sprite: check Database.SpriteDict (includes mod sprites) then Resources cache ---
        if (typeof(Sprite).IsAssignableFrom(targetType))
        {
            if (Database.SpriteDict.TryGetValue(id, out var dbSprite))
                return dbSprite;
            var sprites = GetSpriteCache();
            if (sprites.TryGetValue(id, out var sprite))
                return sprite;

            AddUnresolved($"{id}(Sprite)");
            return null;
        }

        // --- AudioClip: check Database.AudioClipDict then Resources cache ---
        // AudioClip references are typically not registered for explicit lookup;
        // we try our best and downgrade to debug if not found.
        if (typeof(AudioClip).IsAssignableFrom(targetType))
        {
            if (Database.AudioClipDict.TryGetValue(id, out var dbClip))
                return dbClip;
            var clips = GetAudioClipCache();
            if (clips.TryGetValue(id, out var clip))
                return clip;

            _unresolvedAudioClips.Add(id);
            return null;
        }

        // Skip types that aren't ScriptableObject or UnityEngine.Object
        if (!typeof(ScriptableObject).IsAssignableFrom(targetType)
            && !typeof(UnityEngine.Object).IsAssignableFrom(targetType))
            return null;

        // === UniqueIDScriptable types: delegate to the game's own resolver. ===
        // Mod content has been registered into UniqueIDScriptable.AllUniqueObjects
        // by JsonDataLoader, so the game's GetFromID finds vanilla and mod content
        // through the same path.
        if (typeof(UniqueIDScriptable).IsAssignableFrom(targetType))
        {
            var resolved = GameRegistry.GetByUid(id);
            if (resolved != null && targetType.IsInstanceOfType(resolved))
                return resolved;
        }

        // === For non-UniqueIDScriptable types (CardTag, ActionTag, etc.): resolve by name ===
        if (typeof(ScriptableObject).IsAssignableFrom(targetType))
        {
            // 3. Per-type lookup against Database's typed dictionaries.
            var typed = Database.GetTypedSO(targetType, id);
            if (typed != null) return typed;

            // 4. Fallback: flat AllScriptableObjectDict by name
            if (Database.AllScriptableObjectDict.TryGetValue(id, out var byName) && targetType.IsInstanceOfType(byName))
                return byName;

            // 5. Fallback: full Resources SO name cache
            var cache = GetSONameCache();
            if (cache.TryGetValue(id, out var fromCache) && targetType.IsInstanceOfType(fromCache))
                return fromCache;

            // 6. Create custom non-UniqueIDScriptable tags at runtime (CardTag, ActionTag, etc.)
            //    These are mod-defined tags that don't exist in vanilla. Creating them allows
            //    mod items to have custom tag categories without shipping ScriptableObject JSON.
            if (IsCreatableTagType(targetType))
            {
                var created = ScriptableObject.CreateInstance(targetType);
                if (created != null)
                {
                    created.name = id;
                    InitializeTagFields(created, id);
                    Database.RegisterTypedSO(targetType, id, created);
                    _runtimeCreatedCount++;
                    return created;
                }
            }
        }

        AddUnresolved($"{id}({targetType.Name})");
        return null;
    }

    private static void AddUnresolved(string entry)
    {
        var modName = Loading.JsonDataLoader.UniqueIdToModName.TryGetValue(_currentUid, out var m) ? m : "unknown";
        if (!_unresolvedByMod.TryGetValue(modName, out var set))
        {
            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _unresolvedByMod[modName] = set;
        }
        set.Add(entry);
    }

    /// <summary>
    /// Initialize LocalizedString fields (InGameName, etc.) on runtime-created tag ScriptableObjects.
    /// Without this, third-party mods like WikiMod crash when accessing null LocalizedString fields.
    /// </summary>
    private static void InitializeTagFields(ScriptableObject obj, string displayName)
    {
        var type = obj.GetType();
        var dtFieldInfo = (FieldInfo)null;
        foreach (var field in type.GetFields(AllInstance))
        {
            if (field.FieldType.Name != "LocalizedString") continue;

            try
            {
                // Cache the DefaultText FieldInfo for LocalizedString
                if (dtFieldInfo == null)
                    dtFieldInfo = field.FieldType.GetField("DefaultText", AllInstance);

                var existing = field.GetValue(obj);
                // Check if the field is null OR if it's a struct/class with null DefaultText
                // (default structs box to non-null but have null string fields)
                bool needsInit = existing == null;
                if (!needsInit && dtFieldInfo != null)
                {
                    var existingText = dtFieldInfo.GetValue(existing) as string;
                    needsInit = string.IsNullOrEmpty(existingText);
                }
                if (!needsInit) continue;

                var ls = existing ?? Activator.CreateInstance(field.FieldType);
                // Set DefaultText to a human-readable version of the tag name
                if (dtFieldInfo != null)
                {
                    // "tag_Structure" -> "Structure"
                    var text = displayName;
                    if (text.StartsWith("tag_", StringComparison.OrdinalIgnoreCase))
                        text = text.Substring(4);
                    dtFieldInfo.SetValue(ls, text);
                }
                // Also set LocalizationKey so third-party mods that use it won't crash
                var lkField = field.FieldType.GetField("LocalizationKey", AllInstance);
                if (lkField != null && string.IsNullOrEmpty(lkField.GetValue(ls) as string))
                    lkField.SetValue(ls, displayName);
                field.SetValue(obj, ls);
            }
            catch { }
        }
    }

    /// <summary>
    /// Returns true if the type is a non-abstract, non-UnityObject serializable type
    /// that can be created with Activator.CreateInstance.
    /// </summary>
    private static bool IsSerializableType(Type t)
    {
        if (t == null || t.IsAbstract || t.IsInterface) return false;
        if (t == typeof(string) || t.IsPrimitive) return false;
        if (typeof(UnityEngine.Object).IsAssignableFrom(t)) return false;
        return true;
    }

    /// <summary>
    /// Expand an array to a new size, preserving existing elements and creating
    /// new elements via Activator.CreateInstance for new slots.
    /// </summary>
    private static Array ExpandArray(Array existing, Type elemType, int newSize)
    {
        var newArr = Array.CreateInstance(elemType, newSize);
        for (int i = 0; i < existing.Length; i++)
            newArr.SetValue(existing.GetValue(i), i);
        for (int i = existing.Length; i < newSize; i++)
        {
            try { newArr.SetValue(Activator.CreateInstance(elemType), i); }
            catch { }
        }
        return newArr;
    }
}
