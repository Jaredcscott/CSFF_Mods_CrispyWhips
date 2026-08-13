using CSFFModFramework.Api;
using CSFFModFramework.Loading;
using CSFFModFramework.Util;
using static CSFFModFramework.Loading.WorldMapLoader;

namespace CSFFModFramework.Injection;

/// <summary>
/// Evaluates declarative <c>SealableGates</c> from <c>WorldMap/MapNodes.json</c> — negative/
/// challenge gates that are open by default, seal once a trigger (usually taking a perk) is
/// met, and reopen when the player clears a challenge card. Retires ACT's
/// <c>ACTCaveGatePatch.cs</c> and H&amp;F's <c>HFForestGatePatch.cs</c> hand-rolled Harmony
/// patches (~1,100 combined lines doing near-identical things independently).
///
/// <para>Two state models, selected per gate via <see cref="SealableGateDefinition.StateModel"/>:</para>
/// <list type="bullet">
///   <item><b>Marker</b> (ACT's cave-passage model) — a persistent boolean in the target env's
///     <c>CurrentlyBuiltImprovements</c> records "cleared." Symmetric strip/restore of the
///     touching travel DA(s), delegated to <see cref="ConnectionGateService"/>'s existing
///     Env/Edge gate machinery (<see cref="ConnectionGateService.RegisterGate"/> /
///     <see cref="ConnectionGateService.RegisterEdgeGate"/>) — this construct's job is purely
///     to translate JSON into the right calls and manage the challenge-card lifecycle
///     (seeding, clear-action detection, marker persistence).</item>
///   <item><b>CardPresence</b> (H&amp;F's forest-trail model) — no marker. Sealed/open state is
///     derived from which of <see cref="SealableGateDefinition.ChallengeCardUID"/> /
///     <see cref="SealableGateDefinition.ClearedTransformInto"/> is currently present on each
///     independently-evaluated <see cref="DirectionalGateSide"/>'s board/env-save. A native
///     card-JSON regrowth timer (not this service) handles reseal by transforming the cleared
///     card back — the service simply re-syncs DA/map-line state to match on every poll.</item>
/// </list>
///
/// <para>Graceful degradation (CLAUDE.md): every read failure leaves the gate's affected
/// connection OPEN, matching the source patches' documented behavior.</para>
/// </summary>
internal static class SealableGateService
{
    private sealed class GateState
    {
        public SealableGateDefinition Def;
        public string OwnerEnvUID;
        public string OwnerLocUID;
        public bool ClearedThisSession;   // Marker model: bridges the gap before WriteMarker's env entry exists
        public object[] ResolvedSideCt8;  // CardPresence model: cache for dynamically-resolved neighbor CT8s

        // GiveCard starts an AddCard coroutine (decompile-confirmed) — the challenge card does
        // NOT land in GameQuery.CardsInPlayerEnv() the same tick Spawn() is called. OnPoll runs
        // every 1 real second regardless of timeScale (Time.unscaledDeltaTime), so relying on
        // IsCardOnPlayerBoard alone as the "don't spawn twice" guard races the coroutine: a
        // second poll tick can fire before the first spawn is visible, requesting a second copy.
        // This set makes "already requested for this env" a sticky, synchronous fact instead of
        // an eventually-consistent board read. Root cause of the "collapsed wall keeps coming
        // back" report — the second copy sits on the board at full durability after the first is
        // cleared and the passage is already open, reading to the player as an infinite respawn.
        public HashSet<string> SeedRequestedEnvUIDs = new();

        // Tracks the last-known result of EvalTrigger(Def.SealTrigger) so OnPoll can detect a
        // true→false transition (e.g. a Season SealTrigger ending) even though the rest of this
        // gate's per-tick body is intentionally skipped while ungated. Default false is safe: a
        // gate that starts ungated produces no spurious transition until it has actually gone
        // true→false at least once.
        public bool TriggerWasActive;
    }

    private static readonly List<GateState> _gates = new();
    private static bool _initialized;

    // ── setup ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses all <c>SealableGates</c> from prepared nodes, registers clear-action handlers,
    /// wires Marker-model gates into <see cref="ConnectionGateService"/>, and starts the
    /// shared poll. Called from <see cref="WorldMapInjector.OnGameManagerInitialized"/>
    /// alongside <see cref="ConnectionGateService.Initialize"/>.
    /// </summary>
    internal static void Initialize(IEnumerable<MapNodeDefinition> defs)
    {
        // OnGMInitialized fires on EVERY run start (GameManager.FinishInitializing), not once
        // per process. Registration (ActionRouter handlers, marker gates, the poll interval)
        // must happen exactly once; per-run flags are reset instead, so state from a previous
        // save can't leak into the one being loaded — a stale ClearedThisSession=true would
        // report a sealed wall as cleared, and a stale SeedRequestedEnvUIDs would suppress
        // the new run's one legitimate seed.
        if (_initialized)
        {
            foreach (var state in _gates)
            {
                state.ClearedThisSession = false;
                state.SeedRequestedEnvUIDs.Clear();
                state.TriggerWasActive = false;
            }
            if (_gates.Count > 0) ConnectionGateService.EvaluateAll();
            return;
        }
        _initialized = true;

        foreach (var def in defs)
        {
            if (def?.SealableGates == null) continue;
            foreach (var g in def.SealableGates)
            {
                var state = new GateState
                {
                    Def = g,
                    OwnerEnvUID = def.EnvironmentUID,
                    OwnerLocUID = def.LocationUID,
                    ResolvedSideCt8 = g.DirectionalGates != null ? new object[g.DirectionalGates.Count] : null,
                };
                _gates.Add(state);
                RegisterClearHandler(state);
            }
        }
        if (_gates.Count == 0) return;

        foreach (var state in _gates)
        {
            if (IsCardPresence(state.Def)) continue;
            RegisterMarkerGate(state);
        }

        try { TickEvents.Interval(1f, OnPoll, "SealableGateService"); }
        catch (Exception ex) { Log.Warn($"SealableGateService: Interval subscribe failed: {ex.Message}"); }

        Log.Debug($"SealableGateService: initialized {_gates.Count} gate(s)");
    }

    private static bool IsCardPresence(SealableGateDefinition g)
        => "CardPresence".Equals(g.StateModel, StringComparison.OrdinalIgnoreCase);

    // ── shared: seal-trigger evaluation ─────────────────────────────────────

    private static bool EvalTrigger(GateConditionDefinition trigger)
        => trigger != null && ConnectionGateService.EvalSingleGateCond(trigger);

    // ── shared: clear-action handler ────────────────────────────────────────

    private static void RegisterClearHandler(GateState state)
    {
        var g = state.Def;
        if (string.IsNullOrEmpty(g.ChallengeCardUID) || string.IsNullOrEmpty(g.ClearActionKeyPrefix)) return;
        try
        {
            ActionRouter.Register(new ActionHandler
            {
                Name = $"SealableGate_{g.ChallengeCardUID}",
                CardUid = g.ChallengeCardUID,
                ActionKeyPrefix = g.ClearActionKeyPrefix,
                Timing = ActionTiming.AfterWrapped,
                After = ctx => OnClearActionFired(state, ctx),
            });
        }
        catch (Exception ex)
        {
            Log.Warn($"SealableGateService: register clear handler for '{g.ChallengeCardUID}' failed: {ex.Message}");
        }
    }

    private static void OnClearActionFired(GateState state, ActionContext ctx)
    {
        var g = state.Def;
        if (!EvalTrigger(g.SealTrigger)) return;   // never gated for this player — nothing to clear

        if (IsCardPresence(g))
        {
            // The action's own ReceivingCardChanges/transform already changed the card on the
            // board natively — just re-sync DA/map-line state to match the new presence.
            Log.Info($"SealableGateService: '{g.ChallengeCardUID}' clear action fired (CardPresence) — re-syncing '{state.OwnerEnvUID}' "
                + $"[DIAG route={ctx?.Route} actionKey={ctx?.ActionKey} actionName={ctx?.ActionName} receivingCardUid={ctx?.CardUid}]");
            SyncCardPresenceGate(state);
            return;
        }

        // Multi-hit walls (durability-based) fire this action on every hit, not just the last —
        // verify the challenge card actually reached zero/left the board before treating it as
        // cleared (mirrors ACT's IsPassageCleared check).
        if (g.MultiHit && !IsChallengeCardCleared(ctx, g.ChallengeCardUID)) return;

        Log.Info($"SealableGateService: '{g.ChallengeCardUID}' cleared — opening '{g.ConnectionUID}' (marker '{g.MarkerUID}' on '{g.MarkerEnvUID}')");
        state.ClearedThisSession = true;
        WriteMarker(g);
        ConnectionGateService.EvaluateAll();
    }

    private static bool IsChallengeCardCleared(ActionContext ctx, string wallUid)
    {
        // CardUtil.GetDurability maps "UsageDurability" to the wrong runtime member for this
        // card shape — read CurrentUsageDurability directly (mirrors ACT's own workaround).
        var card = ctx?.Card;
        if (card != null)
        {
            var val = CardUtil.GetMemberValue(card, "CurrentUsageDurability");
            if (val != null)
            {
                try { return Convert.ToSingle(val) <= 0f; }
                catch (Exception ex) { Log.Debug($"SealableGateService: CurrentUsageDurability conversion failed for '{wallUid}' — falling through: {ex.GetType().Name} {ex.Message}"); }
            }
        }
        // Card is gone (destroyed by its own OnZero mechanic) ⟹ cleared.
        return !IsCardOnPlayerBoard(wallUid);
    }

    // ── Marker state model (ACT-style) ──────────────────────────────────────

    private static void RegisterMarkerGate(GateState state)
    {
        var g = state.Def;
        bool unlocked() => !EvalTrigger(g.SealTrigger) || state.ClearedThisSession || IsMarkerSet(g);

        if ("Edge".Equals(g.Granularity, StringComparison.OrdinalIgnoreCase))
            ConnectionGateService.RegisterEdgeGate(state.OwnerEnvUID, g.ConnectionUID, unlocked,
                restoreOnUnlock: true, ownCt8UID: g.NeighborCt8UID);
        else
            ConnectionGateService.RegisterGate(g.ConnectionUID, unlocked,
                restoreOnUnlock: true, neighborCt8UID: g.NeighborCt8UID);
    }

    private static void SeedMarkerChallengeCard(GateState state)
    {
        var g = state.Def;
        if (state.ClearedThisSession || IsMarkerSet(g)) return;
        if (!EvalTrigger(g.SealTrigger)) return;
        var curEnv = GameQuery.CurrentEnvironmentUniqueId;
        if (curEnv == null || g.SeedOnEnvUIDs == null || !g.SeedOnEnvUIDs.Contains(curEnv)) return;
        if (GameQuery.IsTransitioning) return;
        if (state.SeedRequestedEnvUIDs.Contains(curEnv)) return;
        if (IsCardOnPlayerBoard(g.ChallengeCardUID)) { state.SeedRequestedEnvUIDs.Add(curEnv); return; }
        state.SeedRequestedEnvUIDs.Add(curEnv);
        SpawnService.Spawn(g.ChallengeCardUID);
        Log.Info($"SealableGateService: seeded '{g.ChallengeCardUID}' on '{curEnv}'");
    }

    private static void CheckResealTimer(GateState state)
    {
        var g = state.Def;
        if (g.ResealCondition == null) return;
        if (!"TimerRegrowth".Equals(g.ResealCondition.Type, StringComparison.OrdinalIgnoreCase)) return;

        // NOTE: deliberately no early-return on state.ClearedThisSession here. That flag is only
        // ever reset to false on a full game restart (Initialize's _initialized branch) — for a
        // non-monotonic (e.g. Season) SealTrigger it stays true for the rest of a continuous
        // session after the FIRST clear, which made RemoveMarker below permanently unreachable
        // and this gate unable to ever reseal without the player reloading a save. The
        // elapsed-day check right below already fully covers "not enough time has passed yet"
        // on its own.
        int clearedDay = ReadMarkerDay(g);
        if (clearedDay < 0) return;   // not cleared, or unreadable

        int elapsed = GameQuery.CurrentDay - clearedDay;
        if (elapsed < g.ResealCondition.Days) return;

        RemoveMarker(g);
        state.ClearedThisSession = false;
        if (g.SeedOnEnvUIDs != null)
            foreach (var env in g.SeedOnEnvUIDs) state.SeedRequestedEnvUIDs.Remove(env);
        Log.Info($"SealableGateService: '{g.ConnectionUID}' reseal timer elapsed ({elapsed}d) — resealing '{g.ResealCondition.TransformInto}'");
        ConnectionGateService.EvaluateAll();
    }

    // ── CardPresence state model (H&F-style) ────────────────────────────────

    private static void SyncCardPresenceGate(GateState state)
    {
        var g = state.Def;
        if (g.DirectionalGates == null || g.DirectionalGates.Count == 0) return;

        try { WorldMap.EnsureInjected(); }
        catch (Exception ex) { Log.Warn($"SealableGateService: EnsureInjected failed: {ex.Message}"); }

        bool gated = EvalTrigger(g.SealTrigger);
        var worldMapSo = WorldMapInjector.GetWorldMapSo();
        Log.Debug($"SealableGateService: [DIAG] SyncCardPresenceGate '{g.ChallengeCardUID}' gated={gated} sides={g.DirectionalGates.Count}");

        for (int i = 0; i < g.DirectionalGates.Count; i++)
        {
            var side = g.DirectionalGates[i];
            bool sealedHere = gated && IsSealedAt(side.WatchEnvUID, g.ChallengeCardUID, g.ClearedTransformInto);

            var ct8 = ResolveSideCt8(state, i, side);
            string ct8Uid = ct8 != null ? CardUtil.GetCardUniqueId(ct8) : null;
            bool mutated = false;
            if (ct8 is CardData ct8Card)
                mutated = SetDaPresence(ct8Card, state, side, g, present: !sealedHere);
            Log.Debug($"SealableGateService: [DIAG] side[{i}] WatchEnvUID={side.WatchEnvUID} sealedHere={sealedHere} "
                + $"resolvedCt8={ct8Uid ?? "NULL"} present={!sealedHere} mutated={mutated} ControlsMapLine={side.ControlsMapLine}");

            if (side.ControlsMapLine && worldMapSo != null)
                ConnectionGateService.ToggleEdgeBidirectional(state.OwnerEnvUID, g.ConnectionUID, sealedHere, worldMapSo);
        }
    }

    private static void SeedCardPresenceChallengeCard(GateState state)
    {
        var g = state.Def;
        if (!EvalTrigger(g.SealTrigger)) return;
        var curEnv = GameQuery.CurrentEnvironmentUniqueId;
        if (curEnv == null || g.SeedOnEnvUIDs == null || !g.SeedOnEnvUIDs.Contains(curEnv)) return;
        if (GameQuery.IsTransitioning) return;
        if (state.SeedRequestedEnvUIDs.Contains(curEnv)) return;
        if (IsCardOnPlayerBoard(g.ChallengeCardUID) || IsCardOnPlayerBoard(g.ClearedTransformInto))
        { state.SeedRequestedEnvUIDs.Add(curEnv); return; }
        state.SeedRequestedEnvUIDs.Add(curEnv);
        SpawnService.Spawn(g.ChallengeCardUID);
        Log.Info($"SealableGateService: seeded '{g.ChallengeCardUID}' on '{curEnv}' (CardPresence)");
    }

    /// <summary>
    /// True when the challenge card should be treated as sealed for env <paramref name="envUID"/>:
    /// the cleared-state card is absent there (whether the challenge card is present, or neither
    /// card has been seeded yet — unseeded defaults to sealed for a gated player, matching
    /// H&amp;F's documented "never let a perk player slip through before clearing" rule).
    /// </summary>
    private static bool IsSealedAt(string envUID, string challengeUID, string clearedUID)
    {
        bool atEnv = envUID.Equals(GameQuery.CurrentEnvironmentUniqueId, StringComparison.Ordinal);
        bool cleared = atEnv ? IsCardOnPlayerBoard(clearedUID) : EnvSaveContainsCard(envUID, clearedUID);
        return !cleared;
    }

    private static object ResolveSideCt8(GateState state, int sideIndex, DirectionalGateSide side)
    {
        if (!string.IsNullOrEmpty(side.Ct8Uid))
            return CardUtil.GetCardDataById(side.Ct8Uid);

        if (state.ResolvedSideCt8 == null) return null;
        if (state.ResolvedSideCt8[sideIndex] != null) return state.ResolvedSideCt8[sideIndex];

        // Dynamic resolution: find the CT8 (not one of ours) whose own DA travels to THIS
        // node — used for a vanilla neighbor whose CT8 UID this mod does not own/know.
        var found = FindCt8TravelingTo(state.OwnerEnvUID, state.OwnerLocUID, side.ExcludeCt8Uids);
        if (found != null) state.ResolvedSideCt8[sideIndex] = found;
        return found;
    }

    private static readonly Dictionary<Type, FieldInfo> _daListFieldCache = new();

    // One-time reflection scan: find a CT8 (not in excludeUids) carrying a DA that travels to
    // the given target env/loc — resolves a vanilla neighbor's CT8 whose UID this mod doesn't
    // own. Mirrors HFForestGatePatch.FindPrimevalWoodsCt8, generalized.
    private static object FindCt8TravelingTo(string targetEnvUid, string targetLocUid, List<string> excludeUids)
    {
        try
        {
            var cardDataType = CardUtil.FindGameType("CardData");
            if (cardDataType == null) return null;
            var all = UnityEngine.Resources.FindObjectsOfTypeAll(cardDataType);
            if (all == null) return null;
            foreach (var obj in all)
            {
                if (obj == null) continue;
                var uid = CardUtil.GetCardUniqueId(obj);
                if (uid == null || (excludeUids != null && excludeUids.Contains(uid))) continue;
                if (obj is not CardData ct8) continue;
                var daList = ConnectionGateService.GetDaList(ct8);
                if (daList == null) continue;
                foreach (var da in daList)
                {
                    if (da != null && DaTravelsTo(da, targetEnvUid, targetLocUid))
                    {
                        Log.Debug($"SealableGateService: [DIAG] resolved neighbor CT8 '{uid}' traveling to '{targetEnvUid}'");
                        return obj;
                    }
                }
            }
            Log.Warn($"SealableGateService: could not resolve a CT8 traveling to '{targetEnvUid}' — that side's gate is inactive");
        }
        catch (Exception ex) { Log.Warn($"SealableGateService: FindCt8TravelingTo failed: {ex.Message}"); }
        return null;
    }

    // Returns true if this side's DA list was mutated (added/removed a DA).
    private static bool SetDaPresence(CardData ct8, GateState state, DirectionalGateSide side, SealableGateDefinition g, bool present)
    {
        try
        {
            var daList = ConnectionGateService.GetDaList(ct8);
            if (daList == null)
            { Log.Debug($"SealableGateService: [DIAG] SetDaPresence on '{CardUtil.GetCardUniqueId(ct8)}' — GetDaList returned NULL, silent no-op"); return false; }

            Func<object, bool> match = side.Direction.HasValue
                ? da => DaIsDirection(da, side.Direction.Value)
                : da => DaTravelsTo(da, g.ConnectionUID, null) || DaTravelsTo(da, state.OwnerEnvUID, state.OwnerLocUID);

            string cacheKey = $"{state.OwnerEnvUID}=>{g.ConnectionUID}|{side.WatchEnvUID}";
            var ct8Uid = CardUtil.GetCardUniqueId(ct8);
            if (present)
            {
                if (FindDaIndex(daList, match) >= 0)
                { Log.Debug($"SealableGateService: [DIAG] SetDaPresence present=true on '{ct8Uid}' — already present, no-op"); return false; }
                if (!_strippedDaCache.TryGetValue(cacheKey, out var stored) || stored == null)
                { Log.Debug($"SealableGateService: [DIAG] SetDaPresence present=true on '{ct8Uid}' — NO cached DA for key '{cacheKey}' (cache has {_strippedDaCache.Count} entries: [{string.Join(", ", _strippedDaCache.Keys)}]) — cannot restore, silent no-op"); return false; }
                daList.Add(stored);
                _strippedDaCache.Remove(cacheKey);
                Log.Debug($"SealableGateService: [DIAG] restored DA on '{ct8Uid}' (key '{cacheKey}')");
                ConnectionGateService.ResyncInGameDaCacheIfPresent(ct8Uid);
                return true;
            }

            int idx = FindDaIndex(daList, match);
            if (idx < 0)
            { Log.Debug($"SealableGateService: [DIAG] SetDaPresence present=false on '{ct8Uid}' — no matching DA found in daList (count={daList.Count}) — already stripped or never present, silent no-op"); return false; }
            _strippedDaCache[cacheKey] = daList[idx];
            daList.RemoveAt(idx);
            Log.Debug($"SealableGateService: [DIAG] stripped DA on '{ct8Uid}' (key '{cacheKey}')");
            ConnectionGateService.ResyncInGameDaCacheIfPresent(ct8Uid);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"SealableGateService: SetDaPresence failed: {ex.Message}");
            return false;
        }
    }

    private static readonly Dictionary<string, object> _strippedDaCache = new();

    private static int FindDaIndex(IList daList, Func<object, bool> match)
    {
        for (int i = 0; i < daList.Count; i++)
        {
            var da = daList[i];
            if (da != null && match(da)) return i;
        }
        return -1;
    }

    // ── shared: DA reflection (direction / destination matching) ────────────

    private static readonly Dictionary<Type, FieldInfo> _hasExpDirCache = new();
    private static readonly Dictionary<Type, FieldInfo> _expDirCache = new();
    private static readonly Dictionary<Type, FieldInfo> _producedCache = new();

    private static bool DaIsDirection(object da, int dir)
    {
        var t = da.GetType();
        if (!_hasExpDirCache.TryGetValue(t, out var hf))
            _hasExpDirCache[t] = hf = CardUtil.FindField(t, "HasExplorationDirection");
        if (hf?.GetValue(da) is not true) return false;
        if (!_expDirCache.TryGetValue(t, out var ef))
            _expDirCache[t] = ef = CardUtil.FindField(t, "ExplorationDirection");
        var d = ef?.GetValue(da);
        return d != null && Convert.ToInt32(d) == dir;
    }

    // Travel DA destination = ProducedCards[0].DroppedCards[0].DroppedCard.UniqueID (the CT4
    // card given to the player). Matches either the target env or target loc UID (defensive —
    // some DAs are authored pointing at the CT8 instead of the CT4).
    private static bool DaTravelsTo(object da, string targetEnvUid, string targetLocUid)
    {
        try
        {
            var t = da.GetType();
            if (!_hasExpDirCache.TryGetValue(t, out var hf))
                _hasExpDirCache[t] = hf = CardUtil.FindField(t, "HasExplorationDirection");
            if (hf?.GetValue(da) is not true) return false;

            if (!_producedCache.TryGetValue(t, out var pf))
                _producedCache[t] = pf = CardUtil.FindField(t, "ProducedCards");
            if (pf?.GetValue(da) is not IList produced || produced.Count == 0) return false;
            var coll = produced[0];
            if (coll == null) return false;
            var drops = CardUtil.FindField(coll.GetType(), "DroppedCards")?.GetValue(coll) as IList;
            if (drops == null || drops.Count == 0) return false;
            var drop = drops[0];
            if (drop == null) return false;
            var card = CardUtil.FindField(drop.GetType(), "DroppedCard")?.GetValue(drop);
            var destUid = CardUtil.GetCardUniqueId(card);
            return destUid != null &&
                (destUid.Equals(targetEnvUid, StringComparison.Ordinal) ||
                 (targetLocUid != null && destUid.Equals(targetLocUid, StringComparison.Ordinal)));
        }
        catch (Exception ex) { Log.Debug($"SealableGateService: drop-target check threw: {Log.ExceptionText(ex)}"); return false; }
    }

    // ── poll ─────────────────────────────────────────────────────────────────

    private static void OnPoll()
    {
        try
        {
            bool anyMarkerActive = false;
            bool anyTriggerJustDeactivated = false;
            foreach (var state in _gates)
            {
                var g = state.Def;
                bool triggerActive = EvalTrigger(g.SealTrigger);
                if (!triggerActive)
                {
                    // True→false transition (e.g. a Season SealTrigger ending, such as winter
                    // giving way to spring): force one more EvaluateAll() below so the map/DA
                    // state catches up to the closure's already-correct "unlocked" answer.
                    // Without this, a gate whose trigger doesn't reactivate soon never gets
                    // another EvaluateAll() call and stays showing LOCKED indefinitely — only a
                    // full game restart's Initialize()-time EvaluateAll() would ever fix it.
                    // No-op for monotonic triggers (PerkEquipped/ImprovementBuilt): they never
                    // flip true→false in normal play, so this branch never fires for existing
                    // gates.
                    if (state.TriggerWasActive) anyTriggerJustDeactivated = true;
                    state.TriggerWasActive = false;
                    continue;   // never gated (or no longer gated) for this player — nothing to do
                }
                state.TriggerWasActive = true;

                if (IsCardPresence(g))
                {
                    SeedCardPresenceChallengeCard(state);
                    SyncCardPresenceGate(state);
                }
                else
                {
                    anyMarkerActive = true;
                    SeedMarkerChallengeCard(state);
                    CheckResealTimer(state);
                }
            }
            // Marker-model DA strip/restore goes through ConnectionGateService.RegisterGate/
            // RegisterEdgeGate, which already resyncs the InGameCardBase cache on every mutation
            // (see ConnectionGateService.ResyncInGameDaCacheIfPresent) — re-evaluating here is
            // enough to pick up any trigger/marker state change since the last tick, including a
            // trigger that just went false (anyTriggerJustDeactivated) even though no gate is
            // "active" this tick.
            if (anyMarkerActive || anyTriggerJustDeactivated) ConnectionGateService.EvaluateAll();
        }
        catch (Exception ex) { Log.Warn($"SealableGateService: poll error: {ex.Message}"); }
    }

    // ── board / env-save reads (shared by both state models) ───────────────

    private static bool IsCardOnPlayerBoard(string uid)
    {
        if (string.IsNullOrEmpty(uid)) return false;
        foreach (var card in GameQuery.CardsInPlayerEnv())
            if (uid.Equals(CardUtil.GetCardUniqueId(card), StringComparison.Ordinal))
                return true;
        return false;
    }

    private static bool EnvSaveContainsCard(string envUid, string cardUid)
    {
        if (string.IsNullOrEmpty(envUid) || string.IsNullOrEmpty(cardUid)) return false;
        try
        {
            var gm = CardUtil.GetGameManagerInstance();
            if (gm == null) return false;
            var envDataField = CardUtil.GetCachedField(gm.GetType(), "EnvironmentsData");
            if (envDataField?.GetValue(gm) is not IDictionary envData) return false;

            var cardData = CardUtil.GetCardDataById(cardUid);
            if (cardData == null) return false;

            foreach (DictionaryEntry entry in envData)
            {
                var value = entry.Value;
                if (value == null) continue;
                var vt = value.GetType();
                if (!IsTargetEnvEntry(vt, value, entry.Key, envUid)) continue;

                var contains = vt.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == "ContainsCard" && m.GetParameters().Length == 1
                        && m.GetParameters()[0].ParameterType.Name == "CardData");
                if (contains == null) return false;
                return contains.Invoke(value, new[] { cardData }) is true;
            }
        }
        catch (Exception ex) { Log.Debug($"SealableGateService: EnvSaveContainsCard error: {ex.Message}"); }
        return false;
    }

    // ── Marker persistence (Marker state model only) ────────────────────────

    private static bool IsMarkerSet(SealableGateDefinition g) => ReadMarkerDay(g) >= 0 || HasBareMarker(g);

    private static bool HasBareMarker(SealableGateDefinition g)
        => FindMarkerEntry(g.MarkerEnvUID, e => e == g.MarkerUID) != null;

    // Resealable gates encode the clear day as "MarkerUID@day" so CheckResealTimer can read it
    // back. Returns the day, or -1 if the marker isn't set (or isn't day-encoded).
    private static int ReadMarkerDay(SealableGateDefinition g)
    {
        var prefix = g.MarkerUID + "@";
        var entry = FindMarkerEntry(g.MarkerEnvUID, e => e != null && e.StartsWith(prefix, StringComparison.Ordinal));
        if (entry == null) return -1;
        var dayPart = entry.Substring(prefix.Length);
        return int.TryParse(dayPart, out var day) ? day : -1;
    }

    private static string FindMarkerEntry(string envUid, Func<string, bool> match)
    {
        if (string.IsNullOrEmpty(envUid)) return null;
        try
        {
            var gm = CardUtil.GetGameManagerInstance();
            if (gm == null) return null;
            var envDataField = CardUtil.GetCachedField(gm.GetType(), "EnvironmentsData");
            if (envDataField?.GetValue(gm) is not IDictionary envData) return null;

            foreach (DictionaryEntry entry in envData)
            {
                var value = entry.Value;
                if (value == null) continue;
                var vt = value.GetType();
                if (!IsTargetEnvEntry(vt, value, entry.Key, envUid)) continue;

                if (CardUtil.GetCachedField(vt, "CurrentlyBuiltImprovements")?.GetValue(value) is IEnumerable built)
                    foreach (var uid in built)
                        if (match(uid as string)) return uid as string;
            }
        }
        catch (System.Exception ex) { Log.Debug($"SealableGateService: improvement lookup in '{envUid}' threw: {ex}"); }
        return null;
    }

    private static void WriteMarker(SealableGateDefinition g)
    {
        var markerValue = g.ResealCondition != null
            ? $"{g.MarkerUID}@{GameQuery.CurrentDay}"
            : g.MarkerUID;
        WriteMarkerValue(g.MarkerEnvUID, markerValue);
    }

    private static void RemoveMarker(SealableGateDefinition g)
    {
        var prefix = g.MarkerUID + "@";
        MutateMarkerList(g.MarkerEnvUID, built =>
        {
            for (int i = built.Count - 1; i >= 0; i--)
            {
                var s = built[i] as string;
                if (s == g.MarkerUID || (s != null && s.StartsWith(prefix, StringComparison.Ordinal)))
                    built.RemoveAt(i);
            }
        });
    }

    private static void WriteMarkerValue(string envUid, string markerValue)
    {
        MutateMarkerList(envUid, built =>
        {
            if (!built.Contains(markerValue)) built.Add(markerValue);
        });
    }

    // Finds (or creates) the EnvironmentsData entry for envUid and applies `mutate` to its
    // CurrentlyBuiltImprovements list, reinitializing the list if it deserialized as null.
    // Mirrors ACTCaveGatePatch.WriteMarker, generalized.
    private static void MutateMarkerList(string envUid, Action<IList> mutate)
    {
        if (string.IsNullOrEmpty(envUid)) return;
        try
        {
            var gm = CardUtil.GetGameManagerInstance();
            if (gm == null) { Log.Warn("SealableGateService: MutateMarkerList: GameManager null."); return; }
            var envDataField = CardUtil.GetCachedField(gm.GetType(), "EnvironmentsData");
            if (envDataField?.GetValue(gm) is not IDictionary envData)
            { Log.Warn("SealableGateService: MutateMarkerList: EnvironmentsData not accessible."); return; }

            foreach (DictionaryEntry entry in envData)
            {
                var value = entry.Value;
                if (value == null) continue;
                var vt = value.GetType();
                if (!IsTargetEnvEntry(vt, value, entry.Key, envUid)) continue;

                var cbiField = CardUtil.GetCachedField(vt, "CurrentlyBuiltImprovements");
                if (cbiField == null) continue;
                if (cbiField.GetValue(value) is not IList built)
                {
                    built = new List<string>();
                    cbiField.SetValue(value, built);
                }
                mutate(built);
                return;
            }

            // Env not yet visited (no EnvironmentsData entry) — create it so the marker persists.
            var getEnvSaveData = gm.GetType().GetMethod("GetEnvSaveData",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (getEnvSaveData == null)
            { Log.Warn("SealableGateService: MutateMarkerList: GetEnvSaveData not found."); return; }

            var envSaveObj = getEnvSaveData.Invoke(gm, new object[] { new EnvID(envUid), true });
            if (envSaveObj == null)
            { Log.Warn($"SealableGateService: MutateMarkerList: GetEnvSaveData returned null for '{envUid}'."); return; }

            var svt = envSaveObj.GetType();
            var svtCbi = CardUtil.GetCachedField(svt, "CurrentlyBuiltImprovements");
            if (svtCbi == null) { Log.Warn("SealableGateService: MutateMarkerList: CurrentlyBuiltImprovements field not found on new entry."); return; }
            if (svtCbi.GetValue(envSaveObj) is not IList newBuilt)
            {
                newBuilt = new List<string>();
                svtCbi.SetValue(envSaveObj, newBuilt);
            }
            mutate(newBuilt);
        }
        catch (Exception ex)
        {
            Log.Warn($"SealableGateService: MutateMarkerList failed: {ex.InnerException?.Message ?? ex.Message}");
        }
    }

    private static bool IsTargetEnvEntry(Type vt, object value, object entryKey, string envUid)
    {
        var envId   = CardUtil.GetCachedField(vt, "EnvironmentID")?.GetValue(value) as string;
        var dictKey = CardUtil.GetCachedField(vt, "DictionaryKey")?.GetValue(value) as string;
        return envUid.Equals(envId, StringComparison.Ordinal)
            || CardUtil.EnvKeyMatchesUid(envUid, dictKey)
            || envUid.Equals(entryKey as string, StringComparison.Ordinal);
    }
}
