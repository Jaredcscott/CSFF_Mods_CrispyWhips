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
///
/// DynamicLayoutSlotStackingMod fixes:
///   4-6. OnCardLoaded / AssignCardPostfix / OnCardSpawned all surface the
///      same MissingMethodException, not because each calls AddSlot directly
///      but because all three are callers of a shared private helper,
///      DynamicLayoutSlotStackingMod.TryRelocate, which contains the one
///      actual stale GraphicsManager.AddSlot(SlotsTypes,CardData,CardData,int)
///      call site. CONFIRMED at the IL level (a peer session's Mono.Cecil
///      scan of the live WikiMod 3.5.1 DLL, cross-checked in this session via
///      an identical MD5 of the same binary, 2026-09-06): each of the three
///      caller methods has ZERO AddSlot references in its own compiled body
///      and a single `call TryRelocate` at IL_0006; the lone stale
///      `callvirt GraphicsManager::AddSlot(...)` sits inside TryRelocate
///      itself at IL_00b3, with no aggressive-inlining hint on any of the
///      four methods. This is a fact about the shipped bytes, not a
///      stack-trace inference — no TryRelocate frame appears in a caller's
///      exception trace, but that alone would have been equally consistent
///      with JIT inlining OR with resolution failing while compiling the
///      callee, so the runtime stack could not have settled it either way.
///      A game update inserted an InGameNPCOrPlayer param and made AddSlot
///      private, so TryRelocate's call site throws MissingMethodException
///      now — the classic "compiled against an old Assembly-CSharp.dll" bug
///      (see root CLAUDE.md's Game-Update Reference Refresh rule), except the
///      stale binary is WikiMod's own and can't be rebuilt from here.
///      Patching all three callers (rather than TryRelocate itself, an
///      un-named implementation detail we would not have known to target
///      without the same IL-level lookup) is the boundary this fix actually
///      uses; the same Cecil scan found exactly these three callers and no
///      fourth entry point on the deployed DLL. Full pattern writeup:
///      `Documentation/ERROR_LOG_SOLUTIONS.md`, "MissingMethodException:
///      DynamicLayoutSlot GraphicsManager.AddSlot(...)" entry.
///      - OnCardLoaded runs once per card during GameManager.AddCard's
///        deserialization coroutine — left unpatched, it aborted that
///        coroutine and broke save load entirely (fixed first, see below).
///      - AssignCardPostfix is a Harmony POSTFIX WikiMod applies to vanilla's
///        own DynamicLayoutSlot.AssignCard(InGameCardBase,bool) — vanilla's
///        own AddSlot()/AssignCard() calls (GraphicsManager.cs, e.g. lines
///        2204/2310/2353/2361/2376) already succeed BEFORE this postfix runs,
///        but its own broken call then throws unhandled, and — being a
///        genuine Harmony postfix on a vanilla method, not one of our own
///        finalizer-wrapped methods — that exception propagates out of
///        AssignCard back into whatever vanilla loop is restoring multiple
///        saved slot assignments, aborting it after the first card. This is
///        the dominant instance of the bug (801 unhandled occurrences in one
///        session vs. a handful for OnCardLoaded) and is why boards/
///        containers render completely empty even after OnCardLoaded alone
///        was fixed — the cards' underlying data may be intact, but the loop
///        that assigns them into visible slots never gets past card #1.
///      - OnCardSpawned fires for newly spawned/created cards during normal
///        play (not just save load), so this isn't a one-time load artifact —
///        it recurs for the rest of the session too.
///      All three get the same treatment: a finalizer that swallows
///      MissingMethodException so the surrounding vanilla loop/coroutine can
///      continue to the next card instead of aborting.
/// </summary>
internal static class WikiModQuickFindFix
{
    private static FieldInfo _contextKeyField;
    private static FieldInfo _cardUniqueIdField;
    private static Type _cardDataType;
    private static bool _loggedBlocker;
    private static bool _loggedContext;
    private static bool _quickFindPatched;
    private static bool _dataStorePatched;
    private static bool _dynamicLayoutSlotPatched;
    private static bool _assignCardPatched;
    private static bool _onCardSpawnedPatched;
    private static bool _deferredStarted;
    private static int _processCardNreCount;
    private static int _onCardLoadedMmeCount;
    private static int _assignCardMmeCount;
    private static int _onCardSpawnedMmeCount;
    private static int _lastAssemblyCount = -1;
    private const int MaxProcessCardNreLogs = 5;
    private const int MaxOnCardLoadedMmeLogs = 5;
    private const int MaxAssignCardMmeLogs = 5;
    private const int MaxOnCardSpawnedMmeLogs = 5;
    private const int DeferredAttempts = 120;

    private static bool AllPatched => _quickFindPatched && _dataStorePatched && _dynamicLayoutSlotPatched && _assignCardPatched && _onCardSpawnedPatched;

    public static void ApplyPatch(Harmony harmony)
    {
        TryApplyPatch(harmony);

        if (!_deferredStarted && !AllPatched && Plugin.Instance != null)
        {
            _deferredStarted = true;
            Plugin.Instance.StartCoroutine(DeferredApplyPatch(harmony));
        }
    }

    private static IEnumerator DeferredApplyPatch(Harmony harmony)
    {
        for (int attempt = 0; attempt < DeferredAttempts && !AllPatched; attempt++)
        {
            yield return null;
            TryApplyPatch(harmony);
        }
    }

    private static void TryApplyPatch(Harmony harmony)
    {
        // FindType results can only change when a new assembly loads (types never appear inside
        // an already-loaded assembly). Without this gate, the WikiMod-absent case burned the full
        // 120-frame deferred retry on all-assembly type scans (~240 scans + 240 log lines/load).
        var assemblyCount = AppDomain.CurrentDomain.GetAssemblies().Length;
        if (assemblyCount == _lastAssemblyCount) return;
        _lastAssemblyCount = assemblyCount;

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
            {
                _cardDataType = cardType;
                _cardUniqueIdField = AccessTools.Field(cardType, "UniqueID")
                                  ?? cardType.GetField("UniqueID",
                                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }

            var processCardFinalizer = new HarmonyMethod(typeof(WikiModQuickFindFix), nameof(ProcessCardDataFinalizer));
            _dataStorePatched = SafePatcher.TryPatch(harmony, "WikiMod.DataStore", "ProcessCardData", finalizer: processCardFinalizer);
        }

        // Patch DynamicLayoutSlotStackingMod's three broken AddSlot call sites separately — none require QuickFindWindow.
        var dynamicLayoutSlotType = Reflection.ReflectionCache.FindType("WikiMod.DynamicLayoutSlotStackingMod");
        if (dynamicLayoutSlotType != null)
        {
            if (!_dynamicLayoutSlotPatched)
            {
                var onCardLoadedFinalizer = new HarmonyMethod(typeof(WikiModQuickFindFix), nameof(OnCardLoadedFinalizer));
                _dynamicLayoutSlotPatched = SafePatcher.TryPatch(harmony, "WikiMod.DynamicLayoutSlotStackingMod", "OnCardLoaded", finalizer: onCardLoadedFinalizer);
            }

            // The dominant instance (801 unhandled occurrences/session): a Harmony postfix WikiMod applies to
            // vanilla's own DynamicLayoutSlot.AssignCard. Vanilla's own AssignCard body already succeeded by the
            // time this postfix runs, but its unhandled throw aborts the vanilla loop restoring OTHER cards'
            // slot assignments — this, not OnCardLoaded, is why boards/containers render empty after load.
            if (!_assignCardPatched)
            {
                var assignCardFinalizer = new HarmonyMethod(typeof(WikiModQuickFindFix), nameof(AssignCardPostfixFinalizer));
                _assignCardPatched = SafePatcher.TryPatch(harmony, "WikiMod.DynamicLayoutSlotStackingMod", "AssignCardPostfix", finalizer: assignCardFinalizer);
            }

            // Fires for newly spawned/created cards during normal play, not just save load — an ongoing hazard
            // for the rest of the session, not a one-time load artifact.
            if (!_onCardSpawnedPatched)
            {
                var onCardSpawnedFinalizer = new HarmonyMethod(typeof(WikiModQuickFindFix), nameof(OnCardSpawnedFinalizer));
                _onCardSpawnedPatched = SafePatcher.TryPatch(harmony, "WikiMod.DynamicLayoutSlotStackingMod", "OnCardSpawned", finalizer: onCardSpawnedFinalizer);
            }
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
            catch (Exception ex) { Util.Log.Debug($"WikiModQuickFindFix: _contextKey reset failed: {ex.GetType().Name} {ex.Message}"); }

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
                catch (Exception ex) { Util.Log.Debug($"WikiModQuickFindFix: UniqueID read failed on card for NRE diagnostic: {ex.GetType().Name} {ex.Message}"); }
                bool isCardData = _cardDataType == null || _cardDataType.IsInstanceOfType(__0);
                if (isCardData)
                {
                    Util.Log.Warn($"WikiModFix: NullRef in DataStore.ProcessCardData for card '{uid}' — null LocalizedString field; WikiMod skipped this card.");
                    if (_processCardNreCount == MaxProcessCardNreLogs)
                        Util.Log.Warn("WikiModFix: further ProcessCardData NREs suppressed silently.");
                }
                else
                {
                    Util.Log.Debug($"WikiModFix: DataStore.ProcessCardData skipped non-CardData '{uid}' ({__0?.GetType().Name}).");
                }
            }
            return null;
        }
        return __exception;
    }

    static Exception OnCardLoadedFinalizer(Exception __exception)
    {
        if (__exception is MissingMethodException)
        {
            _onCardLoadedMmeCount++;
            if (_onCardLoadedMmeCount <= MaxOnCardLoadedMmeLogs)
            {
                Util.Log.Warn($"WikiModFix: MissingMethodException in DynamicLayoutSlotStackingMod.OnCardLoaded ({__exception.Message}) — WikiMod's compiled call site is stale against this game version's GraphicsManager.AddSlot; card-loaded hook skipped for this card so load continues.");
                if (_onCardLoadedMmeCount == MaxOnCardLoadedMmeLogs)
                    Util.Log.Warn("WikiModFix: further OnCardLoaded MissingMethodExceptions suppressed silently.");
            }
            return null;
        }
        return __exception;
    }

    static Exception AssignCardPostfixFinalizer(Exception __exception)
    {
        if (__exception is MissingMethodException)
        {
            _assignCardMmeCount++;
            if (_assignCardMmeCount <= MaxAssignCardMmeLogs)
            {
                Util.Log.Warn($"WikiModFix: MissingMethodException in DynamicLayoutSlotStackingMod.AssignCardPostfix ({__exception.Message}) — WikiMod's compiled call site is stale against this game version's GraphicsManager.AddSlot; suppressed so the vanilla slot-assignment loop can continue to the next card instead of aborting.");
                if (_assignCardMmeCount == MaxAssignCardMmeLogs)
                    Util.Log.Warn("WikiModFix: further AssignCardPostfix MissingMethodExceptions suppressed silently.");
            }
            return null;
        }
        return __exception;
    }

    static Exception OnCardSpawnedFinalizer(Exception __exception)
    {
        if (__exception is MissingMethodException)
        {
            _onCardSpawnedMmeCount++;
            if (_onCardSpawnedMmeCount <= MaxOnCardSpawnedMmeLogs)
            {
                Util.Log.Warn($"WikiModFix: MissingMethodException in DynamicLayoutSlotStackingMod.OnCardSpawned ({__exception.Message}) — WikiMod's compiled call site is stale against this game version's GraphicsManager.AddSlot; card-spawned hook skipped so the newly spawned card isn't lost.");
                if (_onCardSpawnedMmeCount == MaxOnCardSpawnedMmeLogs)
                    Util.Log.Warn("WikiModFix: further OnCardSpawned MissingMethodExceptions suppressed silently.");
            }
            return null;
        }
        return __exception;
    }
}
