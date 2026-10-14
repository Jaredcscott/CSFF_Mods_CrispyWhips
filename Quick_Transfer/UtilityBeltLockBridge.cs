using System.Reflection;
using BepInEx.Bootstrap;

namespace Quick_Transfer;

/// <summary>
/// Lets a batch skip a card Utility Belt has locked (Utility Belt plan B1 step 4). Utility Belt publishes
/// <c>Utility_Belt.LockApi.IsLocked(object)</c>; this bridge finds it by reflection through
/// Chainloader.PluginInfos, so Quick Transfer takes no reference to Utility Belt and declares no
/// [BepInDependency] (a dependency cycle between the two would load neither). Resolution is lazy, from the first
/// Update, never Awake: a plugin that loads after this one is absent from PluginInfos during this one's Awake
/// (see RepeatActionBridge). With Utility Belt absent the answer is "absent", cached, and <see cref="IsLocked"/>
/// is always false, so Quick Transfer behaves exactly as before.
/// </summary>
internal static class UtilityBeltLockBridge
{
    internal const string UtilityBeltGuid = "crispywhips.utility_belt";
    internal const string LockApiTypeName = "Utility_Belt.LockApi";
    internal const string IsLockedMethodName = "IsLocked";

    private static bool _resolved;
    private static Func<object, bool> _isLocked;
    private static readonly HashSet<string> _warned = new HashSet<string>();

    internal static bool Present => _resolved && _isLocked != null;

    private static void WarnOnce(string cause, string message)
    {
        if (_warned.Add(cause))
            Plugin.Logger.LogWarning("Utility Belt item lock: " + message);
    }

    /// <summary>Look Utility Belt up once. Called from Plugin.Update; a no-op after the first call.</summary>
    internal static void EnsureResolved()
    {
        if (_resolved) return;
        _resolved = true;
        _isLocked = null;
        try
        {
            if (!Chainloader.PluginInfos.TryGetValue(UtilityBeltGuid, out var info) || info == null)
            {
                // The normal case for a Quick Transfer-only install: nothing is ever locked.
                Plugin.Logger.LogDebug($"Utility Belt ({UtilityBeltGuid}) is absent; locked cards are not skipped.");
                return;
            }
            if (info.Instance == null)
            {
                WarnOnce("instance", $"{UtilityBeltGuid} is listed but has no plugin instance (it failed to load?); locked cards are not skipped.");
                return;
            }
            var type = info.Instance.GetType().Assembly.GetType(LockApiTypeName, false);
            if (type == null)
            {
                WarnOnce("type", $"{LockApiTypeName} was not found in the loaded Utility Belt (an older build?); locked cards are not skipped.");
                return;
            }
            var method = type.GetMethod(IsLockedMethodName, BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(object) }, null);
            if (method == null || method.ReturnType != typeof(bool))
            {
                WarnOnce("method", $"{LockApiTypeName}.{IsLockedMethodName}(object) was not found (renamed?); locked cards are not skipped.");
                return;
            }
            _isLocked = (Func<object, bool>)Delegate.CreateDelegate(typeof(Func<object, bool>), method);
            Plugin.Logger.LogDebug("Utility Belt found; batches skip locked cards.");
        }
        catch (Exception ex)
        {
            _isLocked = null;
            WarnOnce("lookup", $"the lookup threw ({ex.GetType().Name}: {ex.Message}); locked cards are not skipped.");
        }
    }

    /// <summary>True when Utility Belt is loaded and reports <paramref name="card"/> (an InGameCardBase) locked. Never throws.</summary>
    internal static bool IsLocked(object card)
    {
        if (!_resolved) EnsureResolved();
        var call = _isLocked;
        if (call == null || card == null) return false;
        try { return call(card); }
        catch (Exception ex)
        {
            WarnOnce("call:" + ex.GetType().Name, $"IsLocked threw ({ex.GetType().Name}: {ex.Message}); the card is treated as unlocked.");
            return false;
        }
    }
}
