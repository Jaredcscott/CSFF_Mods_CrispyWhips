using CSFFModFramework.Util;

namespace CSFFModFramework.Loading;

/// <summary>
/// Defensive post-resolve pass: walks every loaded UniqueIDScriptable and removes
/// null entries from any array/list whose element type is a UnityEngine.Object
/// subclass (CardTag, CardData, GameStat, Sprite, etc.).
///
/// Third-party mods (and our own) can ship JSON whose WarpData refs don't resolve
/// because the target asset is missing or renamed. WarpResolver logs them as
/// "unresolved" but the on-card array still contains a null slot. At runtime that
/// null slot causes NullReferenceException in:
///   - CardOnCardAction.DurabilitiesAreCorrect (drag-start) — null tag/durability
///   - GameManager.TagIsOnBoard (day-tick / ApplyExtraDurabilitiesChanges) — null CardTag
///
/// Removing null slots from collections of Unity object refs is always safe: the
/// game iterates them with foreach and dereferences each element, so a missing
/// reference is functionally identical to the entry not existing in the first
/// place.
/// </summary>
internal static class NullReferenceCompactor
{
    private static readonly BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // Cache reflection metadata per type — the same struct/class types are walked
    // hundreds of times across thousands of cards.
    private static readonly Dictionary<Type, FieldInfo[]> _fieldsByType = new();
    // Per-type classification of which fields are worth recursing/compacting.
    // Empty array = no walkable fields, saves the per-card revisit cost.
    private static readonly Dictionary<Type, FieldInfo[]> _walkableFieldsByType = new();

    // Fields whose null entries are INTENTIONAL and must not be compacted.
    // InventorySlots: null CardTabGroup = "unfiltered slot" — removing nulls leaves no slots at all.
    private static readonly HashSet<string> _preserveNullFields = new(StringComparer.Ordinal)
    {
        "InventorySlots",
    };
    // Per-type transitive closure: does walking this type ever reach a UnityEngine.Object
    // collection? Pruning fields whose type tree contains no Unity-collection eliminates
    // millions of useless recursive visits on simple value types (LocalizedString,
    // SpoilageRange, durability scalars, etc.) that have nothing to compact.
    private static readonly Dictionary<Type, bool> _typeHasUnityCollection = new();

    private static int _nullsRemoved;
    private static int _arraysCompacted;
    private static int _objectsVisited;

    /// <summary>
    /// Compact null entries on every object that any framework service has marked
    /// dirty during this load pass (see <see cref="FrameworkDirtyTracker"/>).
    /// When <paramref name="allowFullSweep"/> is true (the default) and no objects
    /// have been marked, falls back to a full sweep across <paramref name="allData"/>.
    /// Pass <c>false</c> for a follow-up pass that should only run when something
    /// new has actually been touched.
    /// </summary>
    public static void CompactAll(IEnumerable allData, bool allowFullSweep = true)
    {
        _nullsRemoved = 0;
        _arraysCompacted = 0;
        _objectsVisited = 0;

        // Reference-identity visited set prevents infinite recursion through cycles.
        var visited = new HashSet<object>(ReferenceEqualityComparer.Default);

        IEnumerable targets;
        bool guided;
        int dirtyCount = 0;
        if (FrameworkDirtyTracker.DirtyObjects.Count > 0)
        {
            // Drain the dirty set so a later CompactAll call picks up only NEW
            // mutations from services that run after this pass (GameSourceModifier,
            // PerkInjector, etc.).
            var drained = FrameworkDirtyTracker.Drain();
            targets = drained;
            guided = true;
            dirtyCount = drained.Count;
        }
        else
        {
            if (!allowFullSweep) return; // nothing dirty and caller doesn't want a full sweep
            if (allData == null) return;
            targets = allData;
            guided = false;
        }

        foreach (var item in targets)
        {
            if (item == null) continue;
            try { Walk(item, visited); }
            catch (Exception ex) { Log.Debug($"NullReferenceCompactor: walk failed on {item.GetType().Name}: {ex.Message}"); }
        }

        var mode = guided ? $"guided ({dirtyCount} dirty)" : "full sweep";
        if (_arraysCompacted > 0 || _nullsRemoved > 0)
            Log.Info($"NullReferenceCompactor: removed {_nullsRemoved} null entries from {_arraysCompacted} collection(s) across {_objectsVisited} objects [{mode}]");
        else
            Log.Debug($"NullReferenceCompactor: no null entries found across {_objectsVisited} objects [{mode}]");

        _fieldsByType.Clear();
        _walkableFieldsByType.Clear();
        _typeHasUnityCollection.Clear();
    }

    private static void Walk(object obj, HashSet<object> visited)
    {
        if (obj == null) return;
        var type = obj.GetType();

        // Note: we DO walk UniqueIDScriptable / ScriptableObject entry points (they inherit
        // from UnityEngine.Object). We don't recurse INTO arbitrary Unity objects via fields
        // — IsRecursable() already excludes UnityEngine.Object subclasses, so any recursive
        // Walk() call is on a serializable struct/class, never a Unity managed object.

        // Reference cycles: only track class instances. Structs are copied, can't cycle.
        if (!type.IsValueType)
        {
            if (!visited.Add(obj)) return;
        }

        _objectsVisited++;

        var fields = GetWalkableFields(type);
        if (fields == null) return;

        foreach (var field in fields)
        {
            object value;
            try { value = field.GetValue(obj); }
            catch { continue; }
            if (value == null) continue;

            var fieldType = field.FieldType;

            // Array of T
            if (value is Array arr && arr.Rank == 1)
            {
                var elemType = fieldType.GetElementType();
                if (elemType == null) continue;

                if (typeof(UnityEngine.Object).IsAssignableFrom(elemType))
                {
                    var compacted = CompactUnityObjectArray(arr, elemType);
                    if (compacted != null) field.SetValue(obj, compacted);
                }
                else if (IsRecursable(elemType))
                {
                    bool isValueType = elemType.IsValueType;
                    for (int i = 0; i < arr.Length; i++)
                    {
                        var elem = arr.GetValue(i);
                        if (elem == null) continue;
                        Walk(elem, visited);
                        if (isValueType) arr.SetValue(elem, i);
                    }
                }
                continue;
            }

            // List<T>
            if (value is IList list && fieldType.IsGenericType
                && fieldType.GetGenericTypeDefinition() == typeof(List<>))
            {
                var elemType = fieldType.GetGenericArguments()[0];
                if (typeof(UnityEngine.Object).IsAssignableFrom(elemType))
                {
                    CompactUnityObjectList(list);
                }
                else if (IsRecursable(elemType))
                {
                    bool isValueType = elemType.IsValueType;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var elem = list[i];
                        if (elem == null) continue;
                        Walk(elem, visited);
                        if (isValueType) list[i] = elem;
                    }
                }
                continue;
            }

            // Nested serializable struct/class — recurse
            if (IsRecursable(fieldType))
            {
                Walk(value, visited);
                if (fieldType.IsValueType)
                    field.SetValue(obj, value);
            }
        }
    }

    /// <summary>
    /// Compact a UnityEngine.Object[]: remove entries that are C# null OR
    /// "Unity-null" (UnityEngine.Object overloads == to compare against destroyed
    /// native objects). Returns a new array if any entries were removed; null otherwise.
    /// </summary>
    private static Array CompactUnityObjectArray(Array arr, Type elemType)
    {
        int nulls = 0;
        for (int i = 0; i < arr.Length; i++)
        {
            var e = arr.GetValue(i);
            if (e == null || (e is UnityEngine.Object uo && uo == null)) nulls++;
        }
        if (nulls == 0) return null;

        var result = Array.CreateInstance(elemType, arr.Length - nulls);
        int w = 0;
        for (int i = 0; i < arr.Length; i++)
        {
            var e = arr.GetValue(i);
            if (e == null || (e is UnityEngine.Object uo && uo == null)) continue;
            result.SetValue(e, w++);
        }

        _arraysCompacted++;
        _nullsRemoved += nulls;
        return result;
    }

    /// <summary>
    /// Compact a List&lt;T&gt; where T is a UnityEngine.Object subclass — remove null
    /// entries in place. Walks back-to-front so RemoveAt indices stay valid.
    /// </summary>
    private static void CompactUnityObjectList(IList list)
    {
        int nulls = 0;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var e = list[i];
            if (e == null || (e is UnityEngine.Object uo && uo == null))
            {
                list.RemoveAt(i);
                nulls++;
            }
        }
        if (nulls > 0)
        {
            _arraysCompacted++;
            _nullsRemoved += nulls;
        }
    }

    /// <summary>
    /// True for serializable struct/class types worth recursing into. Excludes
    /// primitives, strings, UnityEngine.Object subclasses, abstracts, interfaces,
    /// and known leaf value types (Vector2/3/Int, Color, etc.) for speed.
    /// </summary>
    private static bool IsRecursable(Type t)
    {
        if (t == null || t.IsAbstract || t.IsInterface) return false;
        if (t.IsPrimitive || t == typeof(string) || t == typeof(decimal)) return false;
        if (t.IsEnum) return false;
        if (typeof(UnityEngine.Object).IsAssignableFrom(t)) return false;
        if (t == typeof(Vector2) || t == typeof(Vector3) || t == typeof(Vector4)) return false;
        if (t == typeof(Vector2Int) || t == typeof(Vector3Int)) return false;
        if (t == typeof(Color) || t == typeof(Color32) || t == typeof(Quaternion)) return false;
        if (t == typeof(Rect) || t == typeof(Bounds)) return false;
        if (t.Namespace != null && t.Namespace.StartsWith("System", StringComparison.Ordinal)) return false;
        return true;
    }

    private static FieldInfo[] GetWalkableFields(Type type)
    {
        if (_walkableFieldsByType.TryGetValue(type, out var cached)) return cached;

        var all = GetFields(type);
        var walkable = new List<FieldInfo>();
        foreach (var f in all)
        {
            // Skip fields whose null entries are intentional (e.g. InventorySlots).
            if (_preserveNullFields.Contains(f.Name)) continue;

            var ft = f.FieldType;
            if (ft.IsArray)
            {
                var elem = ft.GetElementType();
                if (elem == null) continue;
                // Direct hit: array of Unity objects → always walk, we compact it.
                if (typeof(UnityEngine.Object).IsAssignableFrom(elem)) { walkable.Add(f); continue; }
                // Recursable element: only walk if the element type's tree reaches a Unity collection.
                if (IsRecursable(elem) && TypeHasUnityCollection(elem)) walkable.Add(f);
            }
            else if (ft.IsGenericType && ft.GetGenericTypeDefinition() == typeof(List<>))
            {
                var elem = ft.GetGenericArguments()[0];
                if (typeof(UnityEngine.Object).IsAssignableFrom(elem)) { walkable.Add(f); continue; }
                if (IsRecursable(elem) && TypeHasUnityCollection(elem)) walkable.Add(f);
            }
            else if (IsRecursable(ft) && TypeHasUnityCollection(ft))
            {
                walkable.Add(f);
            }
        }

        var arr = walkable.Count == 0 ? Array.Empty<FieldInfo>() : walkable.ToArray();
        _walkableFieldsByType[type] = arr;
        return arr;
    }

    /// <summary>
    /// Static analysis: does the field-graph rooted at <paramref name="type"/> contain
    /// any array or List&lt;T&gt; whose element is a UnityEngine.Object subclass?
    /// Cycle-safe: types currently being analyzed return false on the back-edge,
    /// then the real value is computed from any other path.
    /// </summary>
    private static bool TypeHasUnityCollection(Type type)
    {
        if (_typeHasUnityCollection.TryGetValue(type, out var cached)) return cached;
        // Mark in-progress as false so cycles short-circuit (any true path will overwrite).
        _typeHasUnityCollection[type] = false;

        bool result = false;
        foreach (var f in GetFields(type))
        {
            var ft = f.FieldType;
            if (ft.IsArray)
            {
                var elem = ft.GetElementType();
                if (elem == null) continue;
                if (typeof(UnityEngine.Object).IsAssignableFrom(elem)) { result = true; break; }
                if (IsRecursable(elem) && TypeHasUnityCollection(elem)) { result = true; break; }
            }
            else if (ft.IsGenericType && ft.GetGenericTypeDefinition() == typeof(List<>))
            {
                var elem = ft.GetGenericArguments()[0];
                if (typeof(UnityEngine.Object).IsAssignableFrom(elem)) { result = true; break; }
                if (IsRecursable(elem) && TypeHasUnityCollection(elem)) { result = true; break; }
            }
            else if (IsRecursable(ft) && TypeHasUnityCollection(ft))
            {
                result = true;
                break;
            }
        }

        _typeHasUnityCollection[type] = result;
        return result;
    }

    private static FieldInfo[] GetFields(Type type)
    {
        if (_fieldsByType.TryGetValue(type, out var cached)) return cached;

        var fields = new List<FieldInfo>();
        var current = type;
        while (current != null && current != typeof(object) && current != typeof(UnityEngine.Object)
            && current != typeof(ScriptableObject) && current != typeof(MonoBehaviour))
        {
            fields.AddRange(current.GetFields(InstanceFlags | BindingFlags.DeclaredOnly));
            current = current.BaseType;
        }

        var result = fields.ToArray();
        _fieldsByType[type] = result;
        return result;
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Default = new();
        public new bool Equals(object x, object y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
