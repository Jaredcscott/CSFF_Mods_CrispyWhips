using UnityEngine.Serialization;

namespace CSFFModFramework.Reflection;

internal static class ReflectionCache
{
    private static readonly Dictionary<string, Type> _typeCache = new();
    private static readonly Dictionary<(Type, string), FieldInfo> _fieldCache = new();
    private static readonly Dictionary<(Type, string), MethodInfo> _methodCache = new();

    private const BindingFlags FieldFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static Type FindType(string name)
    {
        if (_typeCache.TryGetValue(name, out var cached)) return cached;

        // Fast path: direct lookup by full name (works when type has no namespace or caller knows full name)
        Type type = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = asm.GetType(name, false);
            if (type != null) break;
        }

        // Slow path: enumerate all types and match by simple name or full name.
        // Needed when Assembly-CSharp isn't yet indexed by the fast path (e.g., early Awake calls)
        // or when the type lives in a namespace but the caller passes the simple name.
        if (type == null)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = FindInAssembly(asm, name);
                if (type != null) break;
            }
        }

        // Explicit Assembly-CSharp fallback: if an assembly's GetTypes() threw a non-
        // ReflectionTypeLoadException (e.g., TypeLoadException), the outer catch above
        // silently skips it. Unity's Assembly-CSharp is the usual culprit. Find it by name
        // and retry the type search — exposes the real exception via the warning log.
        if (type == null)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var asmName = asm.GetName().Name;
                if (asmName == "Assembly-CSharp" || asmName == "Assembly-CSharp-firstpass")
                {
                    type = FindInAssembly(asm, name, logOnException: true);
                    if (type != null) break;
                }
            }
        }

        if (type == null)
            Util.Log.Debug($"[ReflectionCache] Type not found: {name}");
        else
            _typeCache[name] = type; // only cache successful lookups; null stays uncached so later calls can retry
        return type;
    }

    static Type FindInAssembly(Assembly asm, string name, bool logOnException = false)
    {
        Type[] types;
        try { types = asm.GetTypes(); }
        catch (ReflectionTypeLoadException rtle) { types = rtle.Types ?? Array.Empty<Type>(); }
        catch (Exception ex)
        {
            // Some assemblies throw non-RTLE on GetTypes (TypeLoadException, FileNotFoundException, etc.).
            // Fall back to GetExportedTypes + DefinedTypes as last-resort partial enumerations.
            if (logOnException)
                Util.Log.Warn($"[ReflectionCache] {asm.GetName().Name}.GetTypes() threw {ex.GetType().Name}: {ex.Message}");
            types = TryGetTypesFallback(asm);
        }

        foreach (var t in types)
        {
            if (t == null) continue;
            try
            {
                if (t.Name == name || t.FullName == name) return t;
            }
            catch { /* skip types that throw on name property access */ }
        }
        return null;
    }

    static Type[] TryGetTypesFallback(Assembly asm)
    {
        // Prefer DefinedTypes (lazy, per-type load) over GetExportedTypes (public only).
        try
        {
            var list = new List<Type>();
            foreach (var ti in asm.DefinedTypes)
            {
                try { list.Add(ti.AsType()); } catch { }
            }
            return list.ToArray();
        }
        catch { }
        try { return asm.GetExportedTypes(); } catch { }
        return Array.Empty<Type>();
    }

    public static FieldInfo GetField(Type type, string name)
    {
        if (type == null) return null;
        var key = (type, name);
        if (_fieldCache.TryGetValue(key, out var cached)) return cached;

        var field = AccessTools.Field(type, name);
        if (field == null)
        {
            // Fallback: scan for FormerlySerializedAs attribute
            foreach (var f in type.GetFields(FieldFlags))
            {
                var attrs = f.GetCustomAttributes(typeof(FormerlySerializedAsAttribute), true);
                foreach (FormerlySerializedAsAttribute attr in attrs)
                {
                    if (attr.oldName == name)
                    {
                        field = f;
                        break;
                    }
                }
                if (field != null) break;
            }
        }

        if (field == null)
            Util.Log.Warn($"[ReflectionCache] Field not found: {type.Name}.{name}");
        else
            _fieldCache[key] = field; // only cache successful lookups; null stays uncached so retries can succeed
        return field;
    }

    public static MethodInfo GetMethod(Type type, string name)
    {
        if (type == null) return null;
        var key = (type, name);
        if (_methodCache.TryGetValue(key, out var cached)) return cached;

        var method = AccessTools.Method(type, name);
        if (method == null)
            Util.Log.Warn($"[ReflectionCache] Method not found: {type.Name}.{name}");
        else
            _methodCache[key] = method; // only cache successful lookups; null stays uncached so retries can succeed
        return method;
    }
}
