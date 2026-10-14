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

    private static readonly Dictionary<Type, List<ScriptableObject>> _typedSOLists
        = new Dictionary<Type, List<ScriptableObject>>();

    // Membership of each per-type list, by reference, so a registration never lists an object twice
    // and never pays a List.Contains walk (the CardData list holds thousands of entries).
    private static readonly Dictionary<Type, HashSet<ScriptableObject>> _typedSOListMembers
        = new Dictionary<Type, HashSet<ScriptableObject>>();

    /// <summary>
    /// Return all cached ScriptableObjects whose runtime type is assignable to <paramref name="soType"/>.
    /// Prefer this over <c>Resources.FindObjectsOfTypeAll</c> for types already captured by
    /// <see cref="InitFromGame"/>. The cache holds the load-time snapshot plus, since 2.28.0, every
    /// object the framework adds to the game's AllData (<see cref="RegisterAddedToAllData"/>): mod
    /// JSON content, WorldMap clones and generated animal content.
    /// </summary>
    public static IEnumerable<ScriptableObject> GetAllOfType(Type soType)
    {
        if (soType == null) yield break;
        foreach (var kvp in _typedSOLists)
        {
            if (!soType.IsAssignableFrom(kvp.Key)) continue;
            foreach (var so in kvp.Value)
                yield return so;
        }
    }

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
    /// <para><paramref name="replaceExistingName"/> decides a name another object of the same type
    /// already holds: true (the default, what the non-UID loader and WarpResolver's created tags
    /// rely on) makes <paramref name="so"/> the holder; false leaves the earlier holder in place and
    /// only lists <paramref name="so"/> under its type. The flat dictionary is first-come either way.
    /// Returns true when <paramref name="so"/> holds the name afterwards.</para>
    /// </summary>
    public static bool RegisterTypedSO(Type soType, string name, ScriptableObject so, bool replaceExistingName = true)
    {
        if (soType == null || string.IsNullOrEmpty(name) || so == null) return false;

        if (!_typedSODicts.TryGetValue(soType, out var dict))
        {
            dict = new Dictionary<string, ScriptableObject>(StringComparer.OrdinalIgnoreCase);
            _typedSODicts[soType] = dict;
        }
        var typed = (Dictionary<string, ScriptableObject>)dict;
        bool holdsName = true;
        if (typed.TryGetValue(name, out var holder) && !ReferenceEquals(holder, so) && !replaceExistingName)
            holdsName = false;
        else
            typed[name] = so;

        AddToTypedList(soType, so);

        if (!AllScriptableObjectDict.ContainsKey(name))
            AllScriptableObjectDict[name] = so;
        return holdsName;
    }

    /// <summary>
    /// Typed-cache side of <see cref="GameRegistry.TryAddToAllData"/>: every object the framework
    /// adds to the game's AllData (JSON-loaded mod content, WorldMap clones, generated animal
    /// content, <c>Api.ContentRegistry</c> registrations) is registered here at the same moment,
    /// because <see cref="InitFromGame"/> snapshots the cache before any of them exist
    /// (Documentation/Plans/CSFFModFramework/MASTER_PLAN.md section 29; without this,
    /// <c>CardUtil.FindCardsWhere</c> and the trap-tag leak pass saw vanilla only).
    /// <para>A name another loaded object of the same type already holds (a vanilla card, or an
    /// earlier mod's file of the same name) is never taken over: the earlier holder keeps it and
    /// this object is listed under its type, so name lookups resolve exactly as they did before
    /// and <see cref="GetAllOfType"/> returns both. Logged at Debug.</para>
    /// </summary>
    internal static void RegisterAddedToAllData(UniqueIDScriptable obj)
    {
        if (obj == null) return;
        try
        {
            var name = obj.name;
            var type = obj.GetType();
            if (string.IsNullOrEmpty(name))
            {
                AddToTypedList(type, obj);
                return;
            }
            if (!RegisterTypedSO(type, name, obj, replaceExistingName: false))
                Log.Debug($"Database: '{name}' ({type.Name}, UniqueID '{obj.UniqueID}') shares its name with an object loaded earlier; "
                        + "that one keeps the name and this one is listed under its type");
        }
        catch (Exception ex)
        {
            Log.Warn($"Database: could not add UniqueID '{obj.UniqueID}' to the typed cache: {Log.ExceptionText(ex)}");
        }
    }

    /// <summary>
    /// Typed-cache side of <c>ForeignInstanceReconciler</c>: once a foreign loader's duplicate has
    /// been replaced by the framework's instance in the game's registries, drop the duplicate from
    /// the per-type lists and point every name it held at the framework's instance. Returns how
    /// many list and name entries changed.
    /// </summary>
    internal static int SupersedeInstances(IReadOnlyDictionary<UniqueIDScriptable, UniqueIDScriptable> foreignToOurs)
    {
        if (foreignToOurs == null || foreignToOurs.Count == 0) return 0;
        int changed = 0;
        var oursToList = new List<UniqueIDScriptable>();

        foreach (var kvp in _typedSOLists)
        {
            _typedSOListMembers.TryGetValue(kvp.Key, out var members);
            var list = kvp.Value;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i] is UniqueIDScriptable u && foreignToOurs.TryGetValue(u, out var ours) && !ReferenceEquals(u, ours))
                {
                    members?.Remove(list[i]);
                    list.RemoveAt(i);
                    oursToList.Add(ours);
                    changed++;
                }
            }
        }
        foreach (var ours in oursToList)
            if (ours != null) AddToTypedList(ours.GetType(), ours);

        foreach (var dict in _typedSODicts.Values)
        {
            if (dict is not Dictionary<string, ScriptableObject> typed) continue;
            foreach (var key in typed.Keys.ToList())
                if (typed[key] is UniqueIDScriptable u && foreignToOurs.TryGetValue(u, out var ours) && ours != null && !ReferenceEquals(u, ours))
                {
                    typed[key] = ours;
                    changed++;
                }
        }
        foreach (var key in AllScriptableObjectDict.Keys.ToList())
            if (AllScriptableObjectDict[key] is UniqueIDScriptable u && foreignToOurs.TryGetValue(u, out var ours) && ours != null && !ReferenceEquals(u, ours))
            {
                AllScriptableObjectDict[key] = ours;
                changed++;
            }
        return changed;
    }

    /// <summary>
    /// Lists every AllData object the typed cache does not hold yet. <see cref="RegisterAddedToAllData"/>
    /// only sees what goes through <c>GameRegistry.TryAddToAllData</c>; other code appends to the game's
    /// AllData itself (WDI's mill-race injector adds 600 CardData clones that way, and third-party
    /// plugins add more), and <c>CardUtil.FindCardsWhere</c> was blind to all of it (harness T2.1221,
    /// 2026-10-05: FindCardsWhere 4029 against 4666 CardData in AllData, 656 objects missing). AllData is
    /// the authority: <c>ForeignInstanceReconciler</c> has already removed foreign duplicates from it.
    /// Returns how many objects were added.
    /// </summary>
    internal static int RegisterAllDataStragglers()
    {
        var allData = GameRegistry.AllData;
        if (allData == null) return 0;
        int added = 0;
        foreach (var item in allData)
        {
            if (item is not ScriptableObject so || so == null) continue;
            var type = so.GetType();
            if (_typedSOListMembers.TryGetValue(type, out var members) && members.Contains(so)) continue;
            if (so is UniqueIDScriptable uso) RegisterAddedToAllData(uso);
            else AddToTypedList(type, so);
            added++;
        }
        return added;
    }

    /// <summary>
    /// Counts behind the typed-cache invariant: what <c>CardUtil.FindCardsWhere</c> returns against
    /// the CardData in the game's AllData, and how many AllData entries the cache does not list.
    /// Read by the harness (reflect) to verify plan section 29 in a running game.
    /// </summary>
    internal sealed class TypedCacheCoverage
    {
        public int FindCardsWhereCount;
        public int AllDataCardData;
        public int AllDataObjects;
        public int MissingFromTypedCache;
        public string FirstMissing = "";
    }

    internal static TypedCacheCoverage MeasureCoverage()
    {
        var result = new TypedCacheCoverage();
        foreach (var _ in CardUtil.FindCardsWhere(null)) result.FindCardsWhereCount++;

        var allData = GameRegistry.AllData;
        if (allData == null) return result;
        foreach (var item in allData)
        {
            if (item is not ScriptableObject so) continue;
            result.AllDataObjects++;
            if (so is CardData) result.AllDataCardData++;
            if (!_typedSOListMembers.TryGetValue(so.GetType(), out var members) || !members.Contains(so))
            {
                if (result.MissingFromTypedCache == 0)
                    result.FirstMissing = $"{so.name}({so.GetType().Name})";
                result.MissingFromTypedCache++;
            }
        }
        return result;
    }

    // Adds so to its type's list once, by reference. Returns true when it was not listed yet.
    private static bool AddToTypedList(Type soType, ScriptableObject so)
    {
        if (!_typedSOLists.TryGetValue(soType, out var list))
        {
            list = new List<ScriptableObject>();
            _typedSOLists[soType] = list;
        }
        if (!_typedSOListMembers.TryGetValue(soType, out var members))
        {
            members = new HashSet<ScriptableObject>(ReferenceComparer.Instance);
            _typedSOListMembers[soType] = members;
        }
        if (!members.Add(so)) return false;
        list.Add(so);
        return true;
    }

    private sealed class ReferenceComparer : IEqualityComparer<ScriptableObject>
    {
        public static readonly ReferenceComparer Instance = new();
        public bool Equals(ScriptableObject x, ScriptableObject y) => ReferenceEquals(x, y);
        public int GetHashCode(ScriptableObject obj) => RuntimeHelpers.GetHashCode(obj);
    }

    public static void InitFromGame()
    {
        // UniqueID-keyed lookups go directly through GameRegistry (the game's own
        // AllUniqueObjects dict). No parallel registry to populate here.
        Log.Debug($"Database: game has {GameRegistry.Count} UniqueIDScriptable objects registered");

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
        _typedSOLists.Clear();
        _typedSOListMembers.Clear();
        int typedCount = 0;
        int listCount = 0;
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
                _typedSOLists[type] = new List<ScriptableObject>();
                typeCount++;
            }
            var typed = (Dictionary<string, ScriptableObject>)dict;

            if (AddToTypedList(type, so)) listCount++;

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
        Log.Debug($"Database: loaded {AllScriptableObjectDict.Count} ScriptableObjects by name ({typeCount} types, {typedCount} typed names, {listCount} typed objects)");

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
        _typedSOLists.Clear();
        _typedSOListMembers.Clear();
    }
}
