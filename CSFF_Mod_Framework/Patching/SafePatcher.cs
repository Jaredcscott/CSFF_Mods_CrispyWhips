namespace CSFFModFramework.Patching;

internal static class SafePatcher
{
    public static bool TryPatch(Harmony harmony, string typeName, string methodName,
        HarmonyMethod prefix = null, HarmonyMethod postfix = null, HarmonyMethod finalizer = null)
    {
        try
        {
            var type = Reflection.ReflectionCache.FindType(typeName);
            if (type == null) { Util.Log.Warn($"[SafePatcher] Type not found: {typeName}"); return false; }
            var method = AccessTools.Method(type, methodName);
            if (method == null) { Util.Log.Warn($"[SafePatcher] Method not found: {typeName}.{methodName}"); return false; }
            harmony.Patch(method, prefix, postfix, finalizer: finalizer);
            Util.Log.Debug($"Patched {typeName}.{methodName}");
            return true;
        }
        catch (Exception ex)
        {
            Util.Log.Error($"Failed to patch {typeName}.{methodName}: {ex.Message}");
            return false;
        }
    }

    public static bool TryPatch(Harmony harmony, Type type, string methodName,
        HarmonyMethod prefix = null, HarmonyMethod postfix = null)
    {
        try
        {
            if (type == null) { Util.Log.Warn("[SafePatcher] Type is null"); return false; }
            var method = AccessTools.Method(type, methodName);
            if (method == null) { Util.Log.Warn($"[SafePatcher] Method not found: {type.Name}.{methodName}"); return false; }
            harmony.Patch(method, prefix, postfix);
            Util.Log.Debug($"Patched {type.Name}.{methodName}");
            return true;
        }
        catch (Exception ex)
        {
            Util.Log.Error($"Failed to patch {type.Name}.{methodName}: {ex.Message}");
            return false;
        }
    }
}
