using CSFFModFramework.Util;

namespace CSFFModFramework.Api;

/// <summary>
/// Array/List mutation on game objects (Centralization Tier 1, P2). Replaces the
/// per-mod <c>Array.CreateInstance + Array.Copy + SetValue</c> reconstruction loops
/// (8 sites in ACT VanillaFireKettlePatch alone) and Sirus's CreateCollectionLike.
///
/// <para>All writes that land on a <see cref="UniqueIDScriptable"/> mark the object
/// dirty for the framework's NullReferenceCompactor second pass — mods do not need
/// to (and cannot) notify it manually.</para>
/// </summary>
public static class Collections
{
    /// <summary>
    /// Appends <paramref name="element"/> to the Array or List member
    /// <paramref name="memberName"/> on <paramref name="owner"/>.
    /// Handles: existing <c>List&lt;T&gt;</c> (Add), existing array (rebuild + write back),
    /// and null member (creates an array or list matching the declared member type).
    /// Skips silently if the element is already present (by reference). Returns true on success.
    /// </summary>
    public static bool Append(object owner, string memberName, object element)
        => AppendAll(owner, memberName, new[] { element }) > 0;

    /// <summary>
    /// Appends every element to the Array or List member, skipping elements already
    /// present by reference. Returns the number of elements actually added.
    /// </summary>
    public static int AppendAll(object owner, string memberName, IEnumerable elements)
    {
        if (owner == null || string.IsNullOrEmpty(memberName) || elements == null) return 0;

        var ownerType = owner.GetType();
        var field = ReflectionHelpers.FindField(ownerType, memberName);
        var prop = field == null ? ReflectionHelpers.FindProperty(ownerType, memberName) : null;
        var memberType = field?.FieldType ?? prop?.PropertyType;
        if (memberType == null) return 0;

        object current;
        try { current = field != null ? field.GetValue(owner) : prop.GetValue(owner, null); }
        catch (Exception ex) { Log.Debug($"Collections.AppendAll: read of '{memberName}' on {ownerType.Name} threw: {Log.ExceptionText(ex)}"); return 0; }

        // Existing-by-reference dedup set.
        var existing = new HashSet<object>(ReferenceEqualityComparer.Instance);
        if (current is IEnumerable curEnum && current is not string)
            foreach (var e in curEnum)
                if (e != null) existing.Add(e);

        var toAdd = new List<object>();
        foreach (var e in elements)
            if (e != null && !existing.Contains(e))
                toAdd.Add(e);
        if (toAdd.Count == 0) return 0;

        bool written;
        if (current is IList list && !list.IsFixedSize && !list.IsReadOnly)
        {
            foreach (var e in toAdd) list.Add(e);
            written = true;
        }
        else
        {
            var elemType = GetElementType(memberType) ?? toAdd[0].GetType();
            int oldLen = current is Array oldArr ? oldArr.Length
                       : current is ICollection col ? col.Count : 0;

            if (memberType.IsArray || current is Array)
            {
                var newArr = Array.CreateInstance(elemType, oldLen + toAdd.Count);
                if (current is Array src) Array.Copy(src, newArr, src.Length);
                for (int i = 0; i < toAdd.Count; i++) newArr.SetValue(toAdd[i], oldLen + i);
                written = WriteBack(owner, field, prop, newArr);
            }
            else
            {
                // Null List<T> member — instantiate the declared type (or List<elemType>).
                var listType = memberType.IsInterface || memberType.IsAbstract
                    ? typeof(List<>).MakeGenericType(elemType) : memberType;
                IList newList;
                try { newList = (IList)Activator.CreateInstance(listType); }
                catch (Exception ex) { Log.Debug($"Collections.AppendAll: Activator.CreateInstance({listType.Name}) for '{memberName}' threw: {Log.ExceptionText(ex)}"); return 0; }
                if (current is IEnumerable oldItems && current is not string)
                    foreach (var e in oldItems) newList.Add(e);
                foreach (var e in toAdd) newList.Add(e);
                written = WriteBack(owner, field, prop, newList);
            }
        }

        if (!written) return 0;
        if (owner is UniqueIDScriptable uid)
            Loading.FrameworkDirtyTracker.MarkDirty(uid);
        return toAdd.Count;
    }

    /// <summary>
    /// Alias for <see cref="AppendAll"/> emphasizing the by-reference dedup contract:
    /// elements already present in the target collection are skipped.
    /// </summary>
    public static int Merge(object owner, string memberName, IEnumerable additions)
        => AppendAll(owner, memberName, additions);

    /// <summary>
    /// Builds a new collection of the SAME runtime type as <paramref name="existingCollection"/>
    /// (array → same-element-type array; List&lt;T&gt; → same List&lt;T&gt;) containing
    /// <paramref name="values"/>. Falls back to <c>object[]</c>-style array with the first
    /// value's type when the existing collection is null. Lifted from Sirus CreateCollectionLike.
    /// </summary>
    public static object CreateLike(object existingCollection, IList values)
    {
        values ??= Array.Empty<object>();
        var existingType = existingCollection?.GetType();

        if (existingType != null && existingType.IsArray)
        {
            var elemType = existingType.GetElementType()
                ?? (values.Count > 0 ? values[0].GetType() : typeof(object));
            var array = Array.CreateInstance(elemType, values.Count);
            for (var i = 0; i < values.Count; i++) array.SetValue(values[i], i);
            return array;
        }

        if (existingType != null && typeof(IList).IsAssignableFrom(existingType))
        {
            IList list;
            try { list = (IList)Activator.CreateInstance(existingType); }
            catch (Exception ex) { Log.Debug($"Collections.CreateLike: Activator.CreateInstance({existingType.Name}) threw: {Log.ExceptionText(ex)}"); return null; }
            foreach (var v in values) list.Add(v);
            return list;
        }

        var fallbackElemType = values.Count > 0 ? values[0].GetType() : typeof(object);
        var fallback = Array.CreateInstance(fallbackElemType, values.Count);
        for (var i = 0; i < values.Count; i++) fallback.SetValue(values[i], i);
        return fallback;
    }

    /// <summary>
    /// Replaces the collection member with <paramref name="values"/>, preserving the
    /// member's current runtime collection type via <see cref="CreateLike"/>.
    /// Returns true on success.
    /// </summary>
    public static bool SetCollection(object owner, string memberName, IList values)
    {
        if (owner == null || string.IsNullOrEmpty(memberName)) return false;
        var existing = Reflect.GetMember(owner, memberName);
        var replacement = CreateLike(existing, values);
        if (replacement == null) return false;
        if (!Reflect.SetMember(owner, memberName, replacement)) return false;
        if (owner is UniqueIDScriptable uid)
            Loading.FrameworkDirtyTracker.MarkDirty(uid);
        return true;
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private static bool WriteBack(object owner, FieldInfo field, PropertyInfo prop, object value)
    {
        try
        {
            if (field != null) { field.SetValue(owner, value); return true; }
            var setter = prop?.GetSetMethod(nonPublic: true);
            if (setter == null) return false;
            setter.Invoke(owner, new[] { value });
            return true;
        }
        catch (Exception ex) { Log.Debug($"Collections.WriteBack: write to {owner?.GetType().Name} threw: {Log.ExceptionText(ex)}"); return false; }
    }

    private static Type GetElementType(Type collectionType)
    {
        if (collectionType == null) return null;
        if (collectionType.IsArray) return collectionType.GetElementType();
        if (collectionType.IsGenericType)
        {
            var args = collectionType.GetGenericArguments();
            if (args.Length == 1) return args[0];
        }
        return null;
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new();
        public new bool Equals(object left, object right) => ReferenceEquals(left, right);
        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
