using System.Runtime.Serialization;
using CSFFModFramework.Util;

namespace CSFFModFramework.Api;

/// <summary>
/// Public reflection utility layer for content mods (Centralization Tier 1, P1).
/// Replaces the per-mod GetMember/SetMember copies, BindingFlags constants,
/// multi-name fallback chains, and DeepClone implementations duplicated across
/// ACT, WDI, H&amp;F, CMC, and Sirus.
///
/// <para>All lookups are cached per (Type, memberName) — including negative results —
/// so repeated access in per-card or per-frame loops costs one dictionary hit.</para>
///
/// <para>Member resolution order: readable property → field, walking the inheritance
/// chain (matches <c>CardUtil.GetMemberValue</c>). Writes additionally fall back to the
/// non-public property setter and the <c>&lt;Name&gt;k__BackingField</c> auto-property
/// backing field — the documented fix for <c>AccessTools.Field</c> returning null on
/// auto-properties (CLAUDE.md §Harmony Patching Pitfalls).</para>
/// </summary>
public static class Reflect
{
    /// <summary>Instance | Public | NonPublic — the standard flags for game-object member access.</summary>
    public const BindingFlags AllFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>Static | Public | NonPublic | FlattenHierarchy — for singleton/static member access (e.g. MBSingleton Instance).</summary>
    public const BindingFlags AllStaticFlags =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;

    // ── Member resolution cache ──────────────────────────────────────────────
    // Caches nulls too: an absent member is looked up once per type, then free.
    private static readonly Dictionary<(Type, string), MemberInfo> _getCache = new();
    private static readonly Dictionary<(Type, string), MemberInfo> _setCache = new();

    // ── Read ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Gets a member value, trying each candidate name in order until one resolves
    /// (e.g. <c>GetMember(gm, "DayTimePoints", "CurrentDayTimePoints", "DaytimePoints")</c>).
    /// Property first, then field, walking the inheritance chain. Returns null on failure.
    /// </summary>
    public static object GetMember(object instance, params string[] names)
    {
        if (instance == null || names == null) return null;
        var t = instance.GetType();
        foreach (var name in names)
        {
            if (string.IsNullOrEmpty(name)) continue;
            var member = ResolveGetMember(t, name);
            if (member == null) continue;
            try
            {
                if (member is PropertyInfo pi) return pi.GetValue(instance, null);
                if (member is FieldInfo fi) return fi.GetValue(instance);
            }
            catch { }
        }
        return null;
    }

    /// <summary>Gets a member value by a single name. True if the member exists and the read succeeded.</summary>
    public static bool TryGetMember(object instance, string name, out object value)
    {
        value = null;
        if (instance == null || string.IsNullOrEmpty(name)) return false;
        var member = ResolveGetMember(instance.GetType(), name);
        if (member == null) return false;
        try
        {
            value = member is PropertyInfo pi ? pi.GetValue(instance, null)
                  : ((FieldInfo)member).GetValue(instance);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Gets a member value converted to float. Returns <paramref name="fallback"/> on failure.</summary>
    public static float GetFloat(object instance, string name, float fallback = 0f)
        => TryGetMember(instance, name, out var v) && v != null ? CardUtil.ToFloat(v) : fallback;

    /// <summary>Gets a member value converted to int. Returns <paramref name="fallback"/> on failure.</summary>
    public static int GetInt(object instance, string name, int fallback = 0)
        => TryGetMember(instance, name, out var v) && v != null ? CardUtil.ToInt(v) : fallback;

    /// <summary>Gets a member value converted to bool. Returns <paramref name="fallback"/> on failure.</summary>
    public static bool GetBool(object instance, string name, bool fallback = false)
        => TryGetMember(instance, name, out var v) && v != null ? CardUtil.ToBool(v) : fallback;

    // ── Write ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sets a member value. Resolution: writable property → non-public property setter
    /// → field → <c>&lt;name&gt;k__BackingField</c> (base-type walk). Returns true on success.
    /// </summary>
    public static bool SetMember(object instance, string name, object value)
    {
        if (instance == null || string.IsNullOrEmpty(name)) return false;
        var member = ResolveSetMember(instance.GetType(), name);
        if (member == null) return false;
        try
        {
            if (member is PropertyInfo pi)
            {
                var setter = pi.GetSetMethod(nonPublic: true);
                if (setter == null) return false;
                setter.Invoke(instance, new[] { value });
                return true;
            }
            ((FieldInfo)member).SetValue(instance, value);
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Sets the first member that resolves among <paramref name="names"/>.
    /// Returns the name that was written, or null if none resolved.
    /// </summary>
    public static string SetMemberAny(object instance, object value, params string[] names)
    {
        if (instance == null || names == null) return null;
        foreach (var name in names)
            if (SetMember(instance, name, value))
                return name;
        return null;
    }

    // ── Type lookup ──────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves a game type by trying each candidate name in order
    /// (e.g. <c>TryGetType("UCheatsManager", "CheatsManager")</c>). Scans all loaded
    /// assemblies with caching. Returns null if none resolve.
    /// </summary>
    public static Type TryGetType(params string[] names)
    {
        if (names == null) return null;
        foreach (var name in names)
        {
            if (string.IsNullOrEmpty(name)) continue;
            var t = Reflection.ReflectionCache.FindType(name);
            if (t != null) return t;
        }
        return null;
    }

    // ── Deep clone ───────────────────────────────────────────────────────────

    /// <summary>
    /// Deep-clones a plain serializable object graph (PassiveEffects, CardDrops,
    /// DismantleActions, etc. — anything cloned from a vanilla template before mutation).
    /// UnityEngine.Object references are preserved by reference, NOT cloned — sprites,
    /// CardData, and tags in the graph still point at the shared assets. Guards against
    /// cycles. Skips readonly fields. Returns null for null input.
    /// </summary>
    public static object DeepClone(object source)
        => DeepClone(source, new Dictionary<object, object>(ReferenceEqualityComparer.Instance));

    private static object DeepClone(object source, IDictionary<object, object> clones)
    {
        if (source == null) return null;

        var type = source.GetType();
        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal))
            return source;

        // Unity assets are shared references, never duplicated.
        if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            return source;

        if (clones.TryGetValue(source, out var existingClone))
            return existingClone;

        if (type.IsArray)
        {
            var sourceArray = (Array)source;
            var elementType = type.GetElementType() ?? typeof(object);
            var arrayClone = Array.CreateInstance(elementType, sourceArray.Length);
            clones[source] = arrayClone;
            for (var i = 0; i < sourceArray.Length; i++)
                arrayClone.SetValue(DeepClone(sourceArray.GetValue(i), clones), i);
            return arrayClone;
        }

        if (typeof(IList).IsAssignableFrom(type))
        {
            var sourceList = (IList)source;
            var listClone = (IList)Activator.CreateInstance(type);
            clones[source] = listClone;
            foreach (var item in sourceList)
                listClone.Add(DeepClone(item, clones));
            return listClone;
        }

        var clone = type.IsValueType
            ? Activator.CreateInstance(type)
            : FormatterServices.GetUninitializedObject(type);
        clones[source] = clone;

        foreach (var field in type.GetFields(AllFlags))
        {
            if (field.IsInitOnly) continue;
            field.SetValue(clone, DeepClone(field.GetValue(source), clones));
        }

        return clone;
    }

    // ── Internal resolution ──────────────────────────────────────────────────

    private static MemberInfo ResolveGetMember(Type t, string name)
    {
        var key = (t, name);
        if (_getCache.TryGetValue(key, out var member)) return member;

        var prop = ReflectionHelpers.FindProperty(t, name);
        member = prop?.CanRead == true ? prop : (MemberInfo)ReflectionHelpers.FindField(t, name);
        return _getCache[key] = member;
    }

    private static MemberInfo ResolveSetMember(Type t, string name)
    {
        var key = (t, name);
        if (_setCache.TryGetValue(key, out var member)) return member;

        var prop = ReflectionHelpers.FindProperty(t, name);
        if (prop != null && prop.GetSetMethod(nonPublic: true) != null)
            return _setCache[key] = prop;

        var field = ReflectionHelpers.FindField(t, name);
        if (field != null)
            return _setCache[key] = field;

        // Auto-property without an accessible setter — write the backing field directly.
        var backing = ReflectionHelpers.FindField(t, $"<{name}>k__BackingField");
        return _setCache[key] = backing;
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new();
        public new bool Equals(object left, object right) => ReferenceEquals(left, right);
        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
