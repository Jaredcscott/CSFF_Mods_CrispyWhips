using CSFFModFramework.Data;

namespace CSFFModFramework.Api;

/// <summary>
/// Look up runtime game objects by UniqueID or name. Wraps the game's own
/// <c>UniqueIDScriptable.GetFromID</c> for <see cref="UniqueIDScriptable"/>
/// types and the framework's per-type ScriptableObject indexes for non-UID
/// types (<c>CardTag</c>, <c>ActionTag</c>, etc.).
///
/// Use after <see cref="FrameworkEvents.Loaded"/> has fired — earlier calls
/// may return null because indexes are not yet built.
/// </summary>
public static class GameContent
{
    /// <summary>
    /// Resolve <paramref name="idOrName"/> to a <typeparamref name="T"/>.
    /// For <see cref="UniqueIDScriptable"/> subtypes, <paramref name="idOrName"/>
    /// is treated as a UniqueID. For other ScriptableObject subtypes, it's
    /// treated as <c>ScriptableObject.name</c>. Returns null if not found.
    /// </summary>
    public static T Find<T>(string idOrName) where T : UnityEngine.Object
    {
        if (string.IsNullOrEmpty(idOrName)) return null;

        // UniqueIDScriptable: delegate to the game's own resolver
        if (typeof(UniqueIDScriptable).IsAssignableFrom(typeof(T)))
        {
            var uid = GameRegistry.GetByUid(idOrName);
            if (uid is T tUid) return tUid;
        }

        // ScriptableObject (any): per-type then flat name lookup
        if (typeof(ScriptableObject).IsAssignableFrom(typeof(T)))
        {
            var typed = Database.GetTypedSO(typeof(T), idOrName);
            if (typed is T tTyped) return tTyped;

            if (Database.AllScriptableObjectDict.TryGetValue(idOrName, out var so) && so is T tSO)
                return tSO;
        }

        return null;
    }

    /// <summary>
    /// Type-erased variant of <see cref="Find{T}"/>. Useful when the target type
    /// is only known at runtime (e.g. resolved from JSON metadata).
    /// </summary>
    public static UnityEngine.Object Find(Type type, string idOrName)
    {
        if (type == null || string.IsNullOrEmpty(idOrName)) return null;

        if (typeof(UniqueIDScriptable).IsAssignableFrom(type))
        {
            var uid = GameRegistry.GetByUid(idOrName);
            if (uid != null && type.IsInstanceOfType(uid)) return uid;
        }

        if (typeof(ScriptableObject).IsAssignableFrom(type))
        {
            var typed = Database.GetTypedSO(type, idOrName);
            if (typed != null && type.IsInstanceOfType(typed)) return typed;

            if (Database.AllScriptableObjectDict.TryGetValue(idOrName, out var so)
                && type.IsInstanceOfType(so))
                return so;
        }

        return null;
    }

    /// <summary>
    /// All loaded objects of the given type. Snapshot — safe to enumerate without
    /// holding a framework lock.
    /// </summary>
    public static IEnumerable<T> All<T>() where T : UnityEngine.Object
    {
        if (typeof(UniqueIDScriptable).IsAssignableFrom(typeof(T)))
        {
            var dict = GameRegistry.AllUniqueObjects;
            if (dict != null)
            {
                foreach (DictionaryEntry entry in dict)
                    if (entry.Value is T t) yield return t;
            }
            yield break;
        }

        if (typeof(ScriptableObject).IsAssignableFrom(typeof(T)))
        {
            foreach (var kv in Database.AllScriptableObjectDict)
                if (kv.Value is T t) yield return t;
        }
    }
}
