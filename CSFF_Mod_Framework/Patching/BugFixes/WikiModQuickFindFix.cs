using System;
using System.Reflection;

namespace CSFFModFramework.Patching.BugFixes;

/// <summary>
/// Patches WikiMod to suppress NullReferenceExceptions that would otherwise
/// abort WikiMod's data-processing or prevent its UI from opening.
///
/// QuickFindWindow fixes:
///   1. CreateBlocker (called from Awake): canvas lookup via GameObject.Find
///      returns null when the expected object isn't ready. Swallowing leaves
///      the window without a click-outside-to-dismiss blocker overlay, but
///      the search UI itself still functions.
///   2. ResolveContextKey (called from ShowWindow): the current card context
///      is null when no card is selected. After swallowing, _contextKey is
///      set to "" so ShowWindow continues without a second NullRef cascade.
///
/// DataStore fix:
///   3. ProcessCardData: a card loaded by a third-party mod (e.g. ModCore) may
///      have a null LocalizedString field that WikiMod dereferences immediately.
///      The finalizer logs the offending card UniqueID and skips it so WikiMod
///      can continue processing remaining cards.
/// </summary>
internal static class WikiModQuickFindFix
{
    private static FieldInfo _contextKeyField;
    private static FieldInfo _cardUniqueIdField;
    private static bool _loggedBlocker;
    private static bool _loggedContext;
    private static bool _quickFindPatched;
    private static bool _dataStorePatched;
    private static bool _deferredStarted;
    private static int _processCardNreCount;
    private const int MaxProcessCardNreLogs = 5;
    private const int DeferredAttempts = 120;

    public static void ApplyPatch(Harmony harmony)
    {
        TryApplyPatch(harmony);

        if (!_deferredStarted && (!_quickFindPatched || !_dataStorePatched) && Plugin.Instance != null)
        {
            _deferredStarted = true;
            Plugin.Instance.StartCoroutine(DeferredApplyPatch(harmony));
        }
    }

    private static IEnumerator DeferredApplyPatch(Harmony harmony)
    {
        for (int attempt = 0; attempt < DeferredAttempts && (!_quickFindPatched || !_dataStorePatched); attempt++)
        {
            yield return null;
            TryApplyPatch(harmony);
        }
    }

    private static void TryApplyPatch(Harmony harmony)
    {
        // Only patch if WikiMod is installed; avoids Warn spam for users who don't have it.
        var type = Reflection.ReflectionCache.FindType("WikiMod.QuickFindWindow");
        if (type != null && !_quickFindPatched)
        {
            var blockerFinalizer = new HarmonyMethod(typeof(WikiModQuickFindFix), nameof(CreateBlockerFinalizer));
            var blockerPatched = SafePatcher.TryPatch(harmony, "WikiMod.QuickFindWindow", "CreateBlocker", finalizer: blockerFinalizer);

            var contextFinalizer = new HarmonyMethod(typeof(WikiModQuickFindFix), nameof(ResolveContextKeyFinalizer));
            var contextPatched = SafePatcher.TryPatch(harmony, "WikiMod.QuickFindWindow", "ResolveContextKey", finalizer: contextFinalizer);

            // Cache _contextKey FieldInfo up front while we have the type.
            _contextKeyField = type.GetField("_contextKey",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            _quickFindPatched = blockerPatched || contextPatched;
        }

        // Patch DataStore.ProcessCardData separately — it doesn't require QuickFindWindow.
        var dataStoreType = Reflection.ReflectionCache.FindType("WikiMod.DataStore");
        if (dataStoreType != null && !_dataStorePatched)
        {
            var cardType = Reflection.ReflectionCache.FindType("CardData");
            if (cardType != null)
                _cardUniqueIdField = AccessTools.Field(cardType, "UniqueID")
                                  ?? cardType.GetField("UniqueID",
                                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            var processCardFinalizer = new HarmonyMethod(typeof(WikiModQuickFindFix), nameof(ProcessCardDataFinalizer));
            _dataStorePatched = SafePatcher.TryPatch(harmony, "WikiMod.DataStore", "ProcessCardData", finalizer: processCardFinalizer);
        }
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

    // __0 = the CardData argument passed to ProcessCardData
    static Exception ProcessCardDataFinalizer(object __0, Exception __exception)
    {
        if (__exception is NullReferenceException)
        {
            _processCardNreCount++;
            if (_processCardNreCount <= MaxProcessCardNreLogs)
            {
                string uid = "(unknown)";
                try { uid = _cardUniqueIdField?.GetValue(__0) as string ?? uid; }
                catch { }
                Util.Log.Warn($"WikiModFix: NullRef in DataStore.ProcessCardData for card '{uid}' — null LocalizedString field; WikiMod skipped this card.");
                if (_processCardNreCount == MaxProcessCardNreLogs)
                    Util.Log.Warn("WikiModFix: further ProcessCardData NREs suppressed silently.");
            }
            return null;
        }
        return __exception;
    }
}
