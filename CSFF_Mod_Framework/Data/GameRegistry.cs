using CSFFModFramework.Util;

namespace CSFFModFramework.Data;

/// <summary>
/// Thin wrapper over the game's own <see cref="UniqueIDScriptable"/> static
/// dictionary and <c>GetFromID</c> lookup. The framework registers mod content
/// directly into the game's <c>AllUniqueObjects</c> table — so the game's own
/// resolver finds mod content the same way it finds vanilla content. This file
/// is the single point of contact between framework code and the game's static
/// UID registry.
///
/// <para>Coexistence note: writes are additive (first-wins). If another plugin
/// has already registered a UID, we log Debug and leave their
/// entry intact rather than overwriting.</para>
/// </summary>
internal static class GameRegistry
{
    private static FieldInfo _allUniqueObjectsField;
    private static IDictionary _cachedDict;
    private static MethodInfo _getFromIdNonGeneric;
    private static MethodInfo _getFromIdGeneric;
    private static readonly Dictionary<Type, MethodInfo> _typedGenericCache = new();
    private static bool _initAttempted;

    private static void EnsureInit()
    {
        if (_initAttempted) return;
        _initAttempted = true;

        var uidType = typeof(UniqueIDScriptable);

        _allUniqueObjectsField =
              AccessTools.Field(uidType, "AllUniqueObjects")
           ?? AccessTools.Field(uidType, "allUniqueObjects")
           ?? AccessTools.Field(uidType, "_allUniqueObjects");

        if (_allUniqueObjectsField == null)
            Log.Warn("GameRegistry: UniqueIDScriptable.AllUniqueObjects field not found");

        _getFromIdNonGeneric = uidType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == "GetFromID" && !m.IsGenericMethod
                && m.GetParameters().Length == 1
                && m.GetParameters()[0].ParameterType == typeof(string));

        _getFromIdGeneric = uidType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == "GetFromID" && m.IsGenericMethod);
    }

    /// <summary>
    /// The game's static <c>AllUniqueObjects</c> dictionary, by reference.
    /// Returns <c>null</c> if the field could not be reflected.
    /// Enumerable as <c>DictionaryEntry</c>; safe to read but mutate only via
    /// <see cref="TryRegister"/> to preserve the additive-write contract.
    /// </summary>
    public static IDictionary AllUniqueObjects
    {
        get
        {
            EnsureInit();
            if (_cachedDict != null) return _cachedDict;
            if (_allUniqueObjectsField == null) return null;
            try { _cachedDict = _allUniqueObjectsField.GetValue(null) as IDictionary; }
            catch (Exception ex) { Log.Warn($"GameRegistry: failed to read AllUniqueObjects: {ex.Message}"); }
            return _cachedDict;
        }
    }

    /// <summary>Number of entries in the game's UID registry. 0 if the field could not be reflected.</summary>
    public static int Count => AllUniqueObjects?.Count ?? 0;

    /// <summary>
    /// Resolve a UID through the game's own <c>GetFromID</c> static method.
    /// Returns null if the game's resolver returns null.
    /// </summary>
    public static UniqueIDScriptable GetByUid(string uid)
    {
        if (string.IsNullOrEmpty(uid)) return null;
        EnsureInit();

        if (_getFromIdNonGeneric != null)
        {
            try
            {
                return _getFromIdNonGeneric.Invoke(null, new object[] { uid }) as UniqueIDScriptable;
            }
            catch (Exception ex)
            {
                Log.Debug($"GameRegistry.GetByUid({uid}) non-generic threw: {ex.InnerException?.Message ?? ex.Message}");
            }
        }

        // Fallback: read the dict directly.
        var dict = AllUniqueObjects;
        if (dict != null && dict.Contains(uid))
            return dict[uid] as UniqueIDScriptable;

        return null;
    }

    /// <summary>
    /// Resolve a UID through the game's <c>GetFromID&lt;T&gt;</c> generic method,
    /// caching the typed MethodInfo per <typeparamref name="T"/>.
    /// Returns null on miss or wrong type.
    /// </summary>
    public static T GetByUid<T>(string uid) where T : UniqueIDScriptable
    {
        if (string.IsNullOrEmpty(uid)) return null;
        EnsureInit();

        if (_getFromIdGeneric != null)
        {
            if (!_typedGenericCache.TryGetValue(typeof(T), out var method))
            {
                try { method = _getFromIdGeneric.MakeGenericMethod(typeof(T)); }
                catch { method = null; }
                _typedGenericCache[typeof(T)] = method;
            }

            if (method != null)
            {
                try { return method.Invoke(null, new object[] { uid }) as T; }
                catch (Exception ex)
                {
                    Log.Debug($"GameRegistry.GetByUid<{typeof(T).Name}>({uid}) generic threw: {ex.InnerException?.Message ?? ex.Message}");
                }
            }
        }

        return GetByUid(uid) as T;
    }

    // Cached AllData list reference (resolved on first call).
    private static IList _allDataCached;
    private static bool _allDataInitAttempted;

    /// <summary>
    /// The game's <c>GameLoad.Instance.DataBase.AllData</c> list, by reference.
    /// Returns <c>null</c> if <c>GameLoad.Instance</c> isn't constructed yet.
    /// </summary>
    public static IList AllData
    {
        get
        {
            if (_allDataCached != null) return _allDataCached;
            if (_allDataInitAttempted) return null; // earlier resolution failed
            _allDataInitAttempted = true;

            try
            {
                var gameLoadType = Reflection.ReflectionCache.FindType("GameLoad");
                if (gameLoadType == null) return null;

                var instanceProp = gameLoadType.GetProperty("Instance",
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                    ?? gameLoadType.GetProperty("Instance", BindingFlags.NonPublic | BindingFlags.Static);

                var instance = instanceProp?.GetValue(null);
                if (instance == null)
                {
                    var instanceField = AccessTools.Field(gameLoadType, "Instance");
                    instance = instanceField?.GetValue(null);
                }
                if (instance == null) return null;

                var dbField = AccessTools.Field(gameLoadType, "DataBase");
                var db = dbField?.GetValue(instance);
                if (db == null) return null;

                var allDataField = AccessTools.Field(db.GetType(), "AllData");
                _allDataCached = allDataField?.GetValue(db) as IList;
                return _allDataCached;
            }
            catch (Exception ex)
            {
                Log.Warn($"GameRegistry: failed to resolve GameLoad.Instance.DataBase.AllData: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Additive write: append <paramref name="obj"/> to <c>GameLoad.Instance.DataBase.AllData</c>
    /// if not already present. No-op if AllData isn't reachable yet.
    /// </summary>
    public static bool TryAddToAllData(UniqueIDScriptable obj)
    {
        if (obj == null) return false;
        var allData = AllData;
        if (allData == null) return false;

        try
        {
            if (!allData.Contains(obj))
            {
                allData.Add(obj);
                return true;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"GameRegistry: failed to register {obj.name} in AllData: {ex.Message}");
        }
        return false;
    }

    /// <summary>
    /// Additive write: register <paramref name="obj"/> under its <c>UniqueID</c>
    /// in the game's static dict. Returns false if a different value is already
    /// registered (first-wins coexistence with other plugins).
    /// </summary>
    public static bool TryRegister(UniqueIDScriptable obj)
    {
        if (obj == null) return false;
        var uid = obj.UniqueID;
        if (string.IsNullOrEmpty(uid)) return false;

        var dict = AllUniqueObjects;
        if (dict == null) return false;

        if (dict.Contains(uid))
        {
            var existing = dict[uid];
            if (!ReferenceEquals(existing, obj))
                Log.Debug($"GameRegistry: UID '{uid}' already registered (existing={existing?.GetType().Name}); leaving existing entry in place");
            return false;
        }

        try
        {
            dict.Add(uid, obj);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"GameRegistry: failed to register UID '{uid}': {ex.Message}");
            return false;
        }
    }
}
