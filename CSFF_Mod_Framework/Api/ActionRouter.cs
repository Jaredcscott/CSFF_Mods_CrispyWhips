using CSFFModFramework.Util;

namespace CSFFModFramework.Api;

/// <summary>When a handler runs relative to the original game action.</summary>
public enum ActionTiming
{
    /// <summary>
    /// Suppress the original action entirely. <see cref="ActionHandler.Before"/> runs
    /// first and decides (return true = cancel; a null Before always cancels). The
    /// framework substitutes the canonical "handled" coroutine that restores game
    /// state (PLAYINGCARD → SELECT, IsPerformingAction toggle — lifted from WDI).
    /// </summary>
    Cancel,

    /// <summary>Run <see cref="ActionHandler.Before"/> in the prefix; the original action proceeds.</summary>
    Before,

    /// <summary>
    /// Let the original action run to completion (preserving its DaytimeCost timing,
    /// popup state, and side effects), then run <see cref="ActionHandler.After"/> —
    /// the single framework-owned IEnumerator wrap point. An optional
    /// <see cref="ActionHandler.Before"/> still runs in the prefix (e.g. requirement
    /// pre-checks, capturing values into <see cref="ActionContext.Tag"/>).
    /// </summary>
    AfterWrapped,
}

/// <summary>Context for one intercepted action dispatch.</summary>
public sealed class ActionContext
{
    /// <summary>The CardAction / CardInteraction being performed.</summary>
    public object Action { get; internal set; }

    /// <summary>The receiving in-game card (drag target / button owner).</summary>
    public object Card { get; internal set; }

    /// <summary>
    /// The dragged card for card-on-card actions (CardOnCardActionRoutine route, or the
    /// ActionRoutine route when the game passes one); null otherwise.
    /// </summary>
    public object GivenCard { get; internal set; }

    /// <summary>UniqueID of the receiving card.</summary>
    public string CardUid { get; internal set; }

    /// <summary>Action display name (ActionName.DefaultText), or null.</summary>
    public string ActionName { get; internal set; }

    /// <summary>Action LocalizationKey (ActionName.LocalizationKey), or null.</summary>
    public string ActionKey { get; internal set; }

    /// <summary>Which game method the action arrived through (e.g. "ActionRoutine").</summary>
    public string Route { get; internal set; }

    /// <summary>Handler scratch slot — set it in Before, read it in After.</summary>
    public object Tag { get; set; }
}

/// <summary>
/// A registered action interception. Card identity: set <see cref="CardUid"/> (exact,
/// case-insensitive) or <see cref="CardPredicate"/> (evaluated per dispatch). Action
/// identity is two-tier (CLAUDE.md §Harmony Patching Pitfalls): <see cref="ActionKeyPrefix"/>
/// matches ActionName.LocalizationKey (ordinal prefix), falling back to
/// <see cref="ActionNamePrefix"/> against ActionName.DefaultText (case-insensitive prefix).
/// Leave both null to match every action on the card.
/// </summary>
public sealed class ActionHandler
{
    /// <summary>Diagnostic label used in log lines.</summary>
    public string Name;

    /// <summary>Exact receiving-card UniqueID to match (case-insensitive).</summary>
    public string CardUid;

    /// <summary>Predicate alternative to <see cref="CardUid"/> (e.g. UID prefix families).</summary>
    public Func<ActionContext, bool> CardPredicate;

    /// <summary>Tier-1 action identity: ordinal prefix match on ActionName.LocalizationKey.</summary>
    public string ActionKeyPrefix;

    /// <summary>Tier-2 action identity: case-insensitive prefix match on ActionName.DefaultText.</summary>
    public string ActionNamePrefix;

    /// <summary>When the handler runs. Default <see cref="ActionTiming.Before"/>.</summary>
    public ActionTiming Timing = ActionTiming.Before;

    /// <summary>
    /// Prefix-time callback. For <see cref="ActionTiming.Cancel"/>: return true to
    /// suppress the original (null Before = always suppress). For Before/AfterWrapped:
    /// optional pre-step; the return value is ignored.
    /// </summary>
    public Func<ActionContext, bool> Before;

    /// <summary>Post-completion callback for <see cref="ActionTiming.AfterWrapped"/>.</summary>
    public Action<ActionContext> After;

    // Frame dedup: the same logical action can fire through multiple game methods in
    // one frame (PerformStackActionRoutine + ActionRoutine) — dispatch each handler once.
    internal int LastDispatchFrame = -1;
}

/// <summary>
/// The framework-owned action dispatch layer (Centralization Tier 2, P3). ONE set of
/// Harmony patches on <c>GameManager.ActionRoutine</c> / <c>CardOnCardActionRoutine</c> /
/// <c>PerformStackActionRoutine</c> / <c>PerformActionAsEnumerator</c>; mods register
/// <see cref="ActionHandler"/>s instead of patching those methods themselves.
///
/// <para>Card-on-card actions (every drag CardInteraction, stack drag, NPC-driven
/// CardOnCardAction and cooking result) reach the game as
/// <c>CardOnCardActionRoutine(_Action, _GivenCard, _ReceivingCard, ...)</c>, which applies the
/// GIVEN card's own changes and then tail-calls
/// <c>ActionRoutine(..., _ModifiersAlreadyCollected: true, _GivenCard)</c> and waits for it.
/// Since 2.25.29 the card-on-card prefix owns that dispatch (both cards resolved by parameter
/// NAME) and the tail-call leg is skipped, so a drag dispatches exactly once, with Before /
/// Cancel running BEFORE the dragged card is consumed. Until 2.25.28 the card-on-card leg
/// resolved its cards positionally on a receiver-first assumption: receiver-keyed handlers
/// silently never matched there and fired on the tail-call leg instead (after the dragged
/// card's changes had already been applied, so a Cancel gate refused a Sell / Deposit / Stock /
/// Cut only after the dragged card was destroyed), while card-unbounded handlers fired twice
/// per drag. A bare index swap was built and reverted on 2026-08-16 because with correct cards
/// on both legs every receiver-keyed handler fired twice. Memory:
/// reference_actionrouter_cardoncardaction_dual_dispatch; tracker T1.56 / T1.81.</para>
///
/// <para>Built in: two-tier action identity, per-handler frame dedup, and the SINGLE
/// IEnumerator wrap point — multiple iterator postfixes on the same coroutine cannot
/// compose (only the first wrapper observes the original), so all AfterWrapped handlers
/// for a dispatch run inside one framework wrapper, in registration order.</para>
///
/// <para>Patches are applied lazily on the first <see cref="Register"/> call — a game
/// with no ActionRouter consumers pays zero dispatch overhead. With handlers registered,
/// non-matching actions cost one snapshot-array scan with early exits.</para>
/// </summary>
public static class ActionRouter
{
    private static readonly List<ActionHandler> _handlers = new();
    private static ActionHandler[] _snapshot = Array.Empty<ActionHandler>();
    private static bool _patchAttempted;
    private static readonly List<MethodInfo> _patchedMethods = new();

    /// <summary>Number of registered handlers.</summary>
    public static int Count => _snapshot.Length;

    /// <summary>
    /// Registers a handler and (on first use) applies the dispatch patches.
    /// Returns the handler for later <see cref="Unregister"/>; null if invalid.
    /// </summary>
    public static ActionHandler Register(ActionHandler handler)
    {
        if (handler == null) return null;
        if (handler.CardUid == null && handler.CardPredicate == null)
        {
            Log.Warn($"[ActionRouter] handler '{handler.Name ?? "?"}' has neither CardUid nor CardPredicate — not registered.");
            return null;
        }
        if (handler.Timing == ActionTiming.AfterWrapped && handler.After == null)
        {
            Log.Warn($"[ActionRouter] handler '{handler.Name ?? "?"}' is AfterWrapped but After is null — not registered.");
            return null;
        }
        handler.Name ??= handler.CardUid ?? "handler";

        lock (_handlers)
        {
            _handlers.Add(handler);
            _snapshot = _handlers.ToArray();
        }
        EnsurePatched();
        Log.Debug($"[ActionRouter] registered '{handler.Name}' ({_snapshot.Length} total)");
        return handler;
    }

    /// <summary>Removes a previously registered handler.</summary>
    public static void Unregister(ActionHandler handler)
    {
        if (handler == null) return;
        lock (_handlers)
        {
            if (_handlers.Remove(handler))
                _snapshot = _handlers.ToArray();
        }
    }

    // ── Patch application ────────────────────────────────────────────────────

    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // Per-route arg-index maps resolved from the live method signatures at patch time
    // (signatures shift between game versions - e.g. ActionRoutine's _GivenCard sits at
    // index 6 in EA 0.64f; never hardcode positions). Where the game names a parameter we
    // resolve by NAME first: positional "first InGameCardBase" resolution is exactly what
    // inverted the card-on-card leg (its signature is GIVEN card first, receiver second).
    private static int _arGivenIdx = -1;     // ActionRoutine: _GivenCard (trailing optional param)
    private static int _arModsIdx = -1;      // ActionRoutine: _ModifiersAlreadyCollected - true ONLY on the tail-call from CardOnCardActionRoutine
    private static int _cocReceiverIdx = -1; // CardOnCardActionRoutine: _ReceivingCard (the SECOND InGameCardBase param)
    private static int _cocGivenIdx = -1;    // CardOnCardActionRoutine: _GivenCard (the FIRST InGameCardBase param)
    private static int _paeActionIdx = -1;   // PerformActionAsEnumerator: CardAction param
    private static int _paeCardIdx = -1;     // PerformActionAsEnumerator: InGameCardBase param

    // True when the card-on-card prefix is installed AND dispatching: the ActionRoutine leg then
    // skips the tail-call (see IsCardOnCardTailCall) so each drag dispatches exactly once. False
    // (the routine is left unpatched) whenever a parameter name failed to resolve, which
    // degrades to one dispatch per drag on the ActionRoutine leg - never to a double dispatch
    // and never to the pre-2.25.29 inverted read.
    private static bool _cocOwnsDispatch;

    private static void EnsurePatched()
    {
        if (_patchAttempted) return;
        _patchAttempted = true;

        var gmType = Reflection.ReflectionCache.FindType("GameManager");
        if (gmType == null)
        {
            Log.Warn("[ActionRouter] GameManager type not found — router inactive.");
            return;
        }

        // ActionRoutine - single-card DismantleActions, plus the tail-call leg of every
        // card-on-card action (which carries _GivenCard and _ModifiersAlreadyCollected = true).
        bool actionRoutinePatched = false;
        var actionRoutine = AccessTools.Method(gmType, "ActionRoutine");
        if (actionRoutine != null)
        {
            _arGivenIdx = ResolveParamIndex(actionRoutine, "_GivenCard", "InGameCardBase", skip: 1);
            _arModsIdx = FindParamIndexByName(actionRoutine, "_ModifiersAlreadyCollected");
            if (_arGivenIdx < 0)
                Log.Warn("[ActionRouter] GameManager.ActionRoutine has no _GivenCard parameter on this game version - handlers see GivenCard = null on that route.");
            actionRoutinePatched = TryPatch(actionRoutine, nameof(ActionRoutine_Prefix), nameof(Shared_Postfix));
        }
        else Log.Warn("[ActionRouter] GameManager.ActionRoutine not found.");

        // PerformStackActionRoutine — DismantleAction buttons on card stacks.
        var stackRoutine = AccessTools.Method(gmType, "PerformStackActionRoutine");
        if (stackRoutine != null)
            TryPatch(stackRoutine, nameof(StackAction_Prefix), nameof(Shared_Postfix));
        else Log.Warn("[ActionRouter] GameManager.PerformStackActionRoutine not found.");

        // CardOnCardActionRoutine - card-on-card interactions (drags, stack drags, NPC-driven
        // CardOnCardActions, cooking results). Its signature is (_Action, _GivenCard,
        // _ReceivingCard, ...) - GIVEN card first - and it ends by tail-calling ActionRoutine
        // with _ModifiersAlreadyCollected: true and waiting for it (the only call site in the
        // game that passes true; .decomp/GameManager.cs, re-verified 2026-09-08 on EA 0.67i).
        // The prefix here owns dispatch for those actions ONLY when both card parameters and
        // the tail-call marker resolve by name; otherwise the routine is left unpatched and the
        // ActionRoutine leg (which also receives _GivenCard) dispatches once with correct cards,
        // at the cost of Before/Cancel running after the dragged card's own changes.
        var cardOnCard = AccessTools.Method(gmType, "CardOnCardActionRoutine");
        if (cardOnCard != null)
        {
            _cocGivenIdx = FindParamIndexByName(cardOnCard, "_GivenCard");
            _cocReceiverIdx = FindParamIndexByName(cardOnCard, "_ReceivingCard");
            bool cardsResolved = _cocGivenIdx >= 0 && _cocReceiverIdx >= 0 && _cocGivenIdx != _cocReceiverIdx;
            if (cardsResolved && _arModsIdx >= 0 && actionRoutinePatched)
                _cocOwnsDispatch = TryPatch(cardOnCard, nameof(CardOnCard_Prefix), nameof(Shared_Postfix));

            if (_cocOwnsDispatch)
                Log.Debug($"[ActionRouter] card-on-card dispatch owned by CardOnCardActionRoutine (given={_cocGivenIdx}, receiver={_cocReceiverIdx}, tail-call marker={_arModsIdx}); its ActionRoutine tail-call is skipped.");
            else
                Log.Warn($"[ActionRouter] CardOnCardActionRoutine left unpatched (given={_cocGivenIdx}, receiver={_cocReceiverIdx}, marker={_arModsIdx}, actionRoutinePatched={actionRoutinePatched}): "
                       + "drags dispatch once on the ActionRoutine leg, so Before/Cancel handlers run after the dragged card's own changes.");
        }
        else Log.Debug("[ActionRouter] GameManager.CardOnCardActionRoutine not found (OK on this game version).");

        // PerformActionAsEnumerator — fallback individual-action execution path.
        var performAction = AccessTools.Method(gmType, "PerformActionAsEnumerator");
        if (performAction != null)
        {
            _paeActionIdx = FindParamIndex(performAction, "CardAction", skip: 0);
            _paeCardIdx = FindParamIndex(performAction, "InGameCardBase", skip: 0);
            TryPatch(performAction, nameof(PerformAction_Prefix), nameof(Shared_Postfix));
        }
        else Log.Debug("[ActionRouter] GameManager.PerformActionAsEnumerator not found (OK on this game version).");

        WarnOnExternalPostfixes();
    }

    /// <summary>Applies one route's prefix + postfix pair; returns false when Harmony rejected it.</summary>
    private static bool TryPatch(MethodInfo method, string prefixName, string postfixName)
    {
        try
        {
            Plugin.Harmony.Patch(method,
                prefix: new HarmonyMethod(typeof(ActionRouter).GetMethod(prefixName,
                    BindingFlags.Static | BindingFlags.NonPublic)) { priority = Priority.High },
                postfix: new HarmonyMethod(typeof(ActionRouter).GetMethod(postfixName,
                    BindingFlags.Static | BindingFlags.NonPublic)));
            _patchedMethods.Add(method);
            Log.Debug($"[ActionRouter] patched {method.Name}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"[ActionRouter] failed to patch {method.Name}: {Log.ExceptionText(ex)}");
            return false;
        }
    }

    /// <summary>Index of the (skip+1)-th parameter assignable from the named game type, or -1.</summary>
    private static int FindParamIndex(MethodInfo method, string typeName, int skip)
    {
        var ps = method.GetParameters();
        int seen = 0;
        for (int i = 0; i < ps.Length; i++)
        {
            var t = ps[i].ParameterType;
            bool match = false;
            for (var cur = t; cur != null; cur = cur.BaseType)
            {
                if (cur.Name == typeName) { match = true; break; }
            }
            if (!match) continue;
            if (seen++ == skip) return i;
        }
        return -1;
    }

    /// <summary>Index of the parameter carrying exactly this name (ordinal), or -1.</summary>
    private static int FindParamIndexByName(MethodInfo method, string name)
    {
        var ps = method.GetParameters();
        for (int i = 0; i < ps.Length; i++)
        {
            if (string.Equals(ps[i].Name, name, StringComparison.Ordinal)) return i;
        }
        return -1;
    }

    /// <summary>By-name resolution first; the positional (skip+1)-th match on the type name only as the fallback.</summary>
    private static int ResolveParamIndex(MethodInfo method, string name, string typeName, int skip)
    {
        int byName = FindParamIndexByName(method, name);
        return byName >= 0 ? byName : FindParamIndex(method, typeName, skip);
    }

    /// <summary>
    /// True when an ActionRoutine argument array is the tail-call from CardOnCardActionRoutine:
    /// that is the only call in the game passing _ModifiersAlreadyCollected = true (the
    /// card-on-card routine collected them itself), and the card-on-card prefix has already
    /// dispatched this action with both cards. Pure; exercised offline by
    /// Development_Tools/Tests/Framework-ActionRouterDispatch.Tests.ps1.
    /// </summary>
    private static bool IsCardOnCardTailCall(object[] args, int modifiersCollectedIdx)
        => modifiersCollectedIdx >= 0
           && args != null
           && args.Length > modifiersCollectedIdx
           && args[modifiersCollectedIdx] is bool collected
           && collected;

    /// <summary>
    /// Part 4 guardrail: ActionRouter's single wrap point only fixes the iterator
    /// composition hazard if every action-intercepting mod migrates. During the
    /// migration window, warn when another plugin still postfixes a routed method.
    /// </summary>
    private static void WarnOnExternalPostfixes()
    {
        foreach (var method in _patchedMethods)
        {
            try
            {
                var info = Harmony.GetPatchInfo(method);
                if (info?.Postfixes == null) continue;
                foreach (var p in info.Postfixes)
                {
                    if (p.owner == Plugin.PluginGuid) continue;
                    Log.Warn($"[ActionRouter] WARNING: external postfix by '{p.owner}' detected on "
                           + $"{method.Name} — possible iterator composition conflict. Migrate that "
                           + "mod onto ActionRouter handlers.");
                }
            }
            catch (Exception ex) { Log.Debug($"[ActionRouter] WarnOnExternalPostfixes: patch-info read failed for {method.Name}: {ex.GetType().Name} {ex.Message}"); }
        }
    }

    // ── Route prefixes ───────────────────────────────────────────────────────

    // ActionRoutine(CardAction, InGameCardBase receiver, InGameNPCOrPlayer, bools..., InGameCardBase given)
    private static bool ActionRoutine_Prefix(object[] __args, ref IEnumerator __result, ref object __state)
    {
        if (_snapshot.Length == 0 || __args == null || __args.Length < 2) return true;
        // The card-on-card prefix already dispatched this action (with both cards, before the
        // dragged card was consumed); dispatching again here is the double fire that sank the
        // 2026-08-16 bare index swap. The per-frame dedup in DispatchPrefix cannot catch it
        // because the card-on-card routine yields between the two legs.
        if (_cocOwnsDispatch && IsCardOnCardTailCall(__args, _arModsIdx)) return true;
        var given = _arGivenIdx >= 0 && __args.Length > _arGivenIdx ? __args[_arGivenIdx] : null;
        return DispatchPrefix("ActionRoutine", __args[0], __args[1], given, ref __result, ref __state);
    }

    // PerformStackActionRoutine(CardAction, List<InGameCardBase>, ...)
    private static bool StackAction_Prefix(object[] __args, ref IEnumerator __result, ref object __state)
    {
        if (_snapshot.Length == 0 || __args == null || __args.Length < 2) return true;
        var list = __args[1] as IList;
        if (list == null || list.Count == 0) return true;
        return DispatchPrefix("PerformStackActionRoutine", __args[0], list[0], null, ref __result, ref __state);
    }

    // CardOnCardActionRoutine(CardOnCardAction, InGameCardBase _GivenCard, InGameCardBase _ReceivingCard,
    // InGameNPCOrPlayer, bools...) - GIVEN card first; both indices are resolved by parameter name.
    // Installed only when _cocOwnsDispatch (see EnsurePatched); its ActionRoutine tail-call is skipped.
    private static bool CardOnCard_Prefix(object[] __args, ref IEnumerator __result, ref object __state)
    {
        if (_snapshot.Length == 0 || __args == null || __args.Length < 2) return true;
        var receiver = _cocReceiverIdx >= 0 && __args.Length > _cocReceiverIdx ? __args[_cocReceiverIdx] : null;
        var given = _cocGivenIdx >= 0 && __args.Length > _cocGivenIdx ? __args[_cocGivenIdx] : null;
        return DispatchPrefix("CardOnCardActionRoutine", __args[0], receiver, given, ref __result, ref __state);
    }

    private static bool PerformAction_Prefix(object[] __args, ref IEnumerator __result, ref object __state)
    {
        if (_snapshot.Length == 0 || __args == null) return true;
        var action = _paeActionIdx >= 0 && __args.Length > _paeActionIdx ? __args[_paeActionIdx] : null;
        var card = _paeCardIdx >= 0 && __args.Length > _paeCardIdx ? __args[_paeCardIdx] : null;
        return DispatchPrefix("PerformActionAsEnumerator", action, card, null, ref __result, ref __state);
    }

    // ── Dispatch core ────────────────────────────────────────────────────────

    private sealed class WrapState
    {
        public List<ActionHandler> Handlers;
        public ActionContext Ctx;
    }

    private static bool DispatchPrefix(string route, object action, object card, object given,
        ref IEnumerator result, ref object state)
    {
        try
        {
            if (action == null || card == null) return true;
            var snapshot = _snapshot;

            string cardUid = CardUtil.GetCardUniqueId(card);
            ActionContext ctx = null;
            List<ActionHandler> afterMatches = null;

            foreach (var h in snapshot)
            {
                if (h.CardUid != null
                    && !string.Equals(h.CardUid, cardUid, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Identity established (or predicate pending) — build the context lazily;
                // action-name extraction is the expensive part of a dispatch.
                ctx ??= BuildContext(route, action, card, given, cardUid);

                if (h.CardUid == null && !SafeBool(h.CardPredicate, ctx, h.Name)) continue;
                if (!ActionMatches(h, ctx)) continue;

                if (h.LastDispatchFrame == Time.frameCount) continue;
                h.LastDispatchFrame = Time.frameCount;

                switch (h.Timing)
                {
                    case ActionTiming.Cancel:
                        if (h.Before == null || SafeBool(h.Before, ctx, h.Name))
                        {
                            Log.Debug($"[ActionRouter] '{h.Name}' canceled '{ctx.ActionName ?? ctx.ActionKey}' on {cardUid} ({route})");
                            result = HandledActionStub(card);
                            return false;
                        }
                        break;

                    case ActionTiming.Before:
                        if (h.Before != null) SafeBool(h.Before, ctx, h.Name);
                        break;

                    case ActionTiming.AfterWrapped:
                        if (h.Before != null) SafeBool(h.Before, ctx, h.Name);
                        (afterMatches ??= new List<ActionHandler>()).Add(h);
                        break;
                }
            }

            if (afterMatches != null)
                state = new WrapState { Handlers = afterMatches, Ctx = ctx };
        }
        catch (Exception ex)
        {
            Log.Warn($"[ActionRouter] {route} dispatch error: {Log.ExceptionText(ex)}");
        }
        return true;
    }

    // Shared postfix for all routes — the single wrap point. Only allocates when the
    // prefix matched at least one AfterWrapped handler.
    private static void Shared_Postfix(object __state, ref IEnumerator __result)
    {
        if (__state is not WrapState ws) return;
        __result = RunWrapped(__result, ws);
    }

    // MoveNext is stepped manually (rather than `while (original.MoveNext()) yield return ...`)
    // so a throw from the WRAPPED GAME'S OWN coroutine can be caught outside the yield — C#
    // iterators cannot yield inside a try block that has a catch clause. Without this, an
    // exception mid-action (e.g. a bad dialog/stat-mod state) propagates out of this wrapper
    // uncaught, the same failure shape already fixed once for GameManager.ChangeEnvironment
    // (see Patching/BugFixes/ChangeEnvironmentCrashGuard.cs): the coroutine never reaches the
    // point where the game clears RootAction, so PerformingAction stays true forever and every
    // later action shows "I can't do two things at once..." with no recovery short of quitting.
    private static IEnumerator RunWrapped(IEnumerator original, WrapState ws)
    {
        bool completedCleanly = true;
        if (original != null)
        {
            while (true)
            {
                bool moved;
                try
                {
                    moved = original.MoveNext();
                }
                catch (Exception ex)
                {
                    completedCleanly = false;
                    Log.Error($"[ActionRouter] wrapped action coroutine threw and was suppressed to prevent a "
                        + $"permanent action-lock (\"I can't do two things at once\"). Route='{ws.Ctx?.Route}' "
                        + $"Action='{ws.Ctx?.ActionName ?? ws.Ctx?.ActionKey}' Card='{ws.Ctx?.CardUid}'. Exception: {ex}");
                    break;
                }
                if (!moved) break;
                yield return original.Current;
            }
        }

        // The action didn't finish normally — skip After handlers, whose contract assumes
        // the wrapped action actually completed.
        if (!completedCleanly) yield break;

        foreach (var h in ws.Handlers)
        {
            try { h.After(ws.Ctx); }
            catch (Exception ex) { Log.Warn($"[ActionRouter] After handler '{h.Name}' threw: {Log.ExceptionText(ex)}"); }
        }
    }

    private static ActionContext BuildContext(string route, object action, object card, object given, string cardUid)
        => new()
        {
            Route = route,
            Action = action,
            Card = card,
            GivenCard = given,
            CardUid = cardUid,
            ActionName = CardUtil.GetActionName(action),
            ActionKey = CardUtil.GetActionLocalizationKey(action),
        };

    private static bool ActionMatches(ActionHandler h, ActionContext ctx)
    {
        if (h.ActionKeyPrefix == null && h.ActionNamePrefix == null) return true;
        if (h.ActionKeyPrefix != null && ctx.ActionKey != null
            && ctx.ActionKey.StartsWith(h.ActionKeyPrefix, StringComparison.Ordinal))
            return true;
        if (h.ActionNamePrefix != null && ctx.ActionName != null
            && ctx.ActionName.StartsWith(h.ActionNamePrefix, StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private static bool SafeBool(Func<ActionContext, bool> fn, ActionContext ctx, string name)
    {
        try { return fn(ctx); }
        catch (Exception ex)
        {
            Log.Warn($"[ActionRouter] handler '{name}' threw: {Log.ExceptionText(ex)}");
            return false;
        }
    }

    // ── Canonical cancel stub ────────────────────────────────────────────────
    // Lifted from WDI's proven FinishHandledAction: restores game state so a canceled
    // action doesn't leave the UI stuck in PLAYINGCARD / IsPerformingAction.

    private static IEnumerator HandledActionStub(object receivingCard)
    {
        SetGameState("PLAYINGCARD");
        SetIsPerformingAction(receivingCard, true);
        yield return null;
        SetIsPerformingAction(receivingCard, false);
        SetGameState("SELECT");
    }

    private static void SetGameState(string stateName)
    {
        try
        {
            var gm = CardUtil.GetGameManagerInstance();
            if (gm == null) return;
            var gmType = gm.GetType();
            var prop = CardUtil.GetCachedProperty(gmType, "CurrentGameState");
            var field = prop == null ? CardUtil.GetCachedField(gmType, "CurrentGameState") : null;
            var valueType = prop?.PropertyType ?? field?.FieldType;
            if (valueType == null || !valueType.IsEnum) return;

            var value = Enum.Parse(valueType, stateName);
            var setter = prop?.GetSetMethod(nonPublic: true);
            if (setter != null) setter.Invoke(gm, new[] { value });
            else field?.SetValue(gm, value);
        }
        catch (Exception ex) { Log.Debug($"[ActionRouter] SetGameState({stateName}) failed: {Log.ExceptionText(ex)}"); }
    }

    private static void SetIsPerformingAction(object card, bool value)
    {
        if (card == null) return;
        Reflect.SetMember(card, "IsPerformingAction", value);
    }
}
