using CSFFModFramework.Util;

namespace CSFFModFramework.Data;

internal static class Database
{
    public static Dictionary<string, Sprite> SpriteDict { get; private set; }
        = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

    public static Dictionary<string, AudioClip> AudioClipDict { get; private set; }
        = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);

    public static Dictionary<string, ScriptableObject> AllScriptableObjectDict { get; private set; }
        = new Dictionary<string, ScriptableObject>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Per-type dictionaries keyed by ScriptableObject.name. Solves name collisions
    /// in the flat AllScriptableObjectDict (e.g. a CardTag and another SO sharing the same name).
    /// </summary>
    private static readonly Dictionary<Type, IDictionary> _typedSODicts
        = new Dictionary<Type, IDictionary>();

    /// <summary>
    /// Look up a ScriptableObject by (type, name). Checks the exact type first,
    /// then scans all registered types for an assignable match.
    /// </summary>
    public static ScriptableObject GetTypedSO(Type soType, string name)
    {
        if (soType == null || string.IsNullOrEmpty(name)) return null;

        // Exact type match first
        if (_typedSODicts.TryGetValue(soType, out var dict))
        {
            if (dict is Dictionary<string, ScriptableObject> typed && typed.TryGetValue(name, out var val))
                return val;
        }

        // Scan all registered types for an assignable match (e.g. field type is a base class)
        foreach (var kvp in _typedSODicts)
        {
            if (!soType.IsAssignableFrom(kvp.Key)) continue;
            if (kvp.Value is Dictionary<string, ScriptableObject> d && d.TryGetValue(name, out var found))
                return found;
        }

        return null;
    }

    /// <summary>
    /// Register a dynamically-created ScriptableObject (e.g. a runtime-created CardTag)
    /// into both the per-type and flat dictionaries so future lookups find it.
    /// </summary>
    public static void RegisterTypedSO(Type soType, string name, ScriptableObject so)
    {
        if (soType == null || string.IsNullOrEmpty(name) || so == null) return;

        if (!_typedSODicts.TryGetValue(soType, out var dict))
        {
            dict = new Dictionary<string, ScriptableObject>(StringComparer.OrdinalIgnoreCase);
            _typedSODicts[soType] = dict;
        }
        var typed = (Dictionary<string, ScriptableObject>)dict;
        typed[name] = so;

        if (!AllScriptableObjectDict.ContainsKey(name))
            AllScriptableObjectDict[name] = so;
    }

    public static void InitFromGame()
    {
        // UniqueID-keyed lookups go directly through GameRegistry (the game's own
        // AllUniqueObjects dict). No parallel registry to populate here.
        Log.Info($"Database: game has {GameRegistry.Count} UniqueIDScriptable objects registered");

        // Populate SpriteDict from all loaded sprites
        SpriteDict.Clear();
        foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
        {
            if (sprite != null && !string.IsNullOrEmpty(sprite.name) && !SpriteDict.ContainsKey(sprite.name))
                SpriteDict[sprite.name] = sprite;
        }
        Log.Debug($"Database: loaded {SpriteDict.Count} sprites");

        // Build per-type dictionaries from a single FindObjectsOfTypeAll<ScriptableObject>() call.
        // Previous approach called FindObjectsOfTypeAll per-subclass (100+ calls) — extremely slow.
        AllScriptableObjectDict.Clear();
        _typedSODicts.Clear();
        int typedCount = 0;
        int typeCount = 0;
        var allSOs = Resources.FindObjectsOfTypeAll<ScriptableObject>();
        foreach (var so in allSOs)
        {
            if (so == null || string.IsNullOrEmpty(so.name)) continue;

            var type = so.GetType();
            if (!_typedSODicts.TryGetValue(type, out var dict))
            {
                dict = new Dictionary<string, ScriptableObject>(StringComparer.OrdinalIgnoreCase);
                _typedSODicts[type] = dict;
                typeCount++;
            }
            var typed = (Dictionary<string, ScriptableObject>)dict;

            // Per-type dict (no cross-type collisions)
            if (!typed.ContainsKey(so.name))
            {
                typed[so.name] = so;
                typedCount++;
            }

            // Flat dict (first-come-first-served, used as last resort)
            if (!AllScriptableObjectDict.ContainsKey(so.name))
                AllScriptableObjectDict[so.name] = so;
        }
        Log.Debug($"Database: loaded {AllScriptableObjectDict.Count} ScriptableObjects by name ({typeCount} types, {typedCount} typed entries)");

        // Populate AudioClipDict
        AudioClipDict.Clear();
        foreach (var clip in Resources.FindObjectsOfTypeAll<AudioClip>())
        {
            if (clip != null && !string.IsNullOrEmpty(clip.name) && !AudioClipDict.ContainsKey(clip.name))
                AudioClipDict[clip.name] = clip;
        }
        Log.Debug($"Database: loaded {AudioClipDict.Count} audio clips");
    }

    public static void Clear()
    {
        SpriteDict.Clear();
        AudioClipDict.Clear();
        AllScriptableObjectDict.Clear();
        _typedSODicts.Clear();
    }
}
