using UnityEngine.Serialization;

namespace CSFFModFramework.Reflection;

internal static class ReflectionCache
{
    // ── GetFromID builder ─────────────────────────────────────────────────────

    private static MethodInfo _getCardDataFromIDMethod;
    private static bool _getFromIDInit;

    /// <summary>
    /// Returns a bound MethodInfo for <c>UniqueIDScriptable.GetFromID&lt;CardData&gt;(string)</c>.
    /// Scans all assemblies once, then caches. Returns null if types not found.
    /// </summary>
    public static MethodInfo GetCardDataFromIDMethod()
    {
        if (_getFromIDInit) return _getCardDataFromIDMethod;
        _getFromIDInit = true;

        Type cardDataType = null;
        Type uidType = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                cardDataType ??= asm.GetType("CardData", false);
                uidType ??= asm.GetType("UniqueIDScriptable", false);
                if (cardDataType != null && uidType != null) break;
            }
            catch { }
        }

        if (uidType == null || cardDataType == null)
        {
            Util.Log.Warn("[ReflectionCache] GetFromID: CardData or UniqueIDScriptable type not found");
            return null;
        }

        var generic = uidType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == "GetFromID" && m.IsGenericMethodDefinition);
        if (generic == null)
        {
            Util.Log.Warn("[ReflectionCache] GetFromID: generic method definition not found on UniqueIDScriptable");
            return null;
        }

        _getCardDataFromIDMethod = generic.MakeGenericMethod(cardDataType);
        return _getCardDataFromIDMethod;
    }

    // ── Signature-based method resolver ──────────────────────────────────────

    /// <summary>
    /// Finds a method on <paramref name="type"/> by name where the first N parameters have type
    /// names matching <paramref name="paramTypeNames"/> (simple name or full name or suffix match).
    /// Searches all instance and static methods including non-public.
    /// </summary>
    public static MethodInfo FindMethodBySignature(Type type, string methodName, params string[] paramTypeNames)
    {
        if (type == null || string.IsNullOrEmpty(methodName)) return null;
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        return type.GetMethods(All).FirstOrDefault(m =>
        {
            if (m.Name != methodName) return false;
            var p = m.GetParameters();
            if (p.Length < paramTypeNames.Length) return false;
            for (int i = 0; i < paramTypeNames.Length; i++)
            {
                var expected = paramTypeNames[i];
                var pt = p[i].ParameterType;
                if (pt.Name == expected || pt.FullName == expected) continue;
                if (pt.FullName != null && pt.FullName.EndsWith("." + expected, StringComparison.Ordinal)) continue;
                return false;
            }
            return true;
        });
    }

    /// <summary>
    /// Finds a method whose parameter names or type names contain the given hints (case-insensitive).
    /// Used for methods with obfuscated/version-varying signatures (e.g. ChangeStatValue).
    /// Returns the method and fills <paramref name="argIndices"/> with the index of each matched hint.
    /// </summary>
    public static MethodInfo FindMethodByParamHints(Type type, string methodName, Type returnType,
        string[] paramHints, out int[] argIndices)
    {
        argIndices = new int[paramHints.Length];
        if (type == null) return null;

        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var m in type.GetMethods(All))
        {
            if (m.Name != methodName) continue;
            if (returnType != null && !returnType.IsAssignableFrom(m.ReturnType)) continue;

            var p = m.GetParameters();
            bool matched = true;
            for (int h = 0; h < paramHints.Length; h++)
            {
                argIndices[h] = -1;
                for (int i = 0; i < p.Length; i++)
                {
                    var pname = p[i].Name ?? string.Empty;
                    var tname = p[i].ParameterType.Name ?? string.Empty;
                    if (pname.IndexOf(paramHints[h], StringComparison.OrdinalIgnoreCase) >= 0 ||
                        tname.IndexOf(paramHints[h], StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        argIndices[h] = i;
                        break;
                    }
                }
                if (argIndices[h] == -1) { matched = false; break; }
            }
            if (matched) return m;
        }
        argIndices = Array.Empty<int>();
        return null;
    }

    private static readonly Dictionary<string, Type> _typeCache = new();
    private static readonly Dictionary<(Type, string), FieldInfo> _fieldCache = new();
    private static readonly Dictionary<(Type, string), MethodInfo> _methodCache = new();

    private const BindingFlags FieldFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static Type FindType(string name)
    {
        if (_typeCache.TryGetValue(name, out var cached)) return cached;

        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        Type type = null;

        // Fast path: direct lookup by full name (works when type has no namespace or caller knows full name)
        foreach (var asm in assemblies)
        {
            type = asm.GetType(name, false);
            if (type != null) break;
        }

        // Slow path: enumerate all types and match by simple name or full name.
        // Needed when Assembly-CSharp isn't yet indexed by the fast path (e.g., early Awake calls)
        // or when the type lives in a namespace but the caller passes the simple name.
        if (type == null)
        {
            foreach (var asm in assemblies)
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
            foreach (var asm in assemblies)
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
                Util.Log.Warn($"[ReflectionCache] {asm.GetName().Name}.GetTypes() threw {ex.GetType().Name}: {Util.Log.ExceptionText(ex)}");
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
            // Only cache successful lookups. Nulls stay uncached so callers can retry after a
            // game update that adds the field — intentional asymmetry with WarpResolver.CachedField,
            // which caches nulls because its field set is stable per-type and retrying is wasteful.
            _fieldCache[key] = field;
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
