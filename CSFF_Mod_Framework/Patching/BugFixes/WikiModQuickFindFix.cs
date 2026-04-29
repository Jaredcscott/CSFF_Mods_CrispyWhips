using System;
using System.Reflection;

namespace CSFFModFramework.Patching.BugFixes;

/// <summary>
/// Patches WikiMod's QuickFindWindow to suppress two NullReferenceExceptions
/// that prevent the quick-find overlay from opening:
///
///   1. CreateBlocker (called from Awake): canvas lookup via GameObject.Find
///      returns null when the expected object isn't ready. Swallowing leaves
///      the window without a click-outside-to-dismiss blocker overlay, but
///      the search UI itself still functions.
///
///   2. ResolveContextKey (called from ShowWindow): the current card context
///      is null when no card is selected. After swallowing, _contextKey is
///      set to "" so ShowWindow continues without a second NullRef cascade.
/// </summary>
internal static class WikiModQuickFindFix
{
    private static FieldInfo _contextKeyField;
    private static bool _loggedBlocker;
    private static bool _loggedContext;

    public static void ApplyPatch(Harmony harmony)
    {
        // Only patch if WikiMod is installed; avoids Warn spam for users who don't have it.
        var type = Reflection.ReflectionCache.FindType("WikiMod.QuickFindWindow");
        if (type == null) return;

        var blockerFinalizer = new HarmonyMethod(typeof(WikiModQuickFindFix), nameof(CreateBlockerFinalizer));
        SafePatcher.TryPatch(harmony, "WikiMod.QuickFindWindow", "CreateBlocker", finalizer: blockerFinalizer);

        var contextFinalizer = new HarmonyMethod(typeof(WikiModQuickFindFix), nameof(ResolveContextKeyFinalizer));
        SafePatcher.TryPatch(harmony, "WikiMod.QuickFindWindow", "ResolveContextKey", finalizer: contextFinalizer);

        // Cache _contextKey FieldInfo up front while we have the type.
        _contextKeyField = type.GetField("_contextKey",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    }

    static Exception CreateBlockerFinalizer(Exception __exception)
    {
        if (__exception is NullReferenceException && !_loggedBlocker)
        {
            _loggedBlocker = true;
            Util.Log.Debug("WikiModQuickFindFix: NullRef in QuickFindWindow.CreateBlocker (canvas not ready); blocker overlay suppressed.");
        }
        return __exception is NullReferenceException ? null : __exception;
    }

    static Exception ResolveContextKeyFinalizer(object __instance, Exception __exception)
    {
        if (__exception is NullReferenceException)
        {
            if (!_loggedContext)
            {
                _loggedContext = true;
                Util.Log.Debug("WikiModQuickFindFix: NullRef in QuickFindWindow.ResolveContextKey (no card selected); search opens with empty context.");
            }

            // Set _contextKey to "" so ShowWindow doesn't NullRef again trying to use it.
            try
            {
                if (_contextKeyField?.GetValue(__instance) == null)
                    _contextKeyField?.SetValue(__instance, "");
            }
            catch { }

            return null;
        }
        return __exception;
    }
}
