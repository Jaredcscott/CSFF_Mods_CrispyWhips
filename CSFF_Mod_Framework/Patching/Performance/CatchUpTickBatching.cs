using BepInEx.Configuration;
using HarmonyLib;

namespace CSFFModFramework.Patching.Performance;

// Phase 3 of the catch-up performance plan (Documentation/Plans/CSFFModFramework/
// CatchUp_Performance_Plan.md) — "K-chunked catch-up", the big win. Composes with
// CatchUpTickCap: instead of replaying every elapsed tick individually, it clamps
// EnvironmentsData[env].LastUpdatedTick to `now − numChunks` so vanilla's OWN
// ChangeEnvironment loop (.decomp/GameManager.cs:10238-10245) runs `numChunks`
// times instead of `effectiveW` times, and scales/repeats the per-call work so
// each chunk represents K real ticks instead of 1.
//
// Vanilla's per-tick catch-up work is durability decay (ModifyDurability =
// value += amount; Clamp — arithmetically exact to batch, including OnZero/OnFull
// edge-triggered flags: .decomp/InGameCardBase.cs:8120-8373) plus a handful of
// companion systems the game already exposes batch parameters for (cooking's
// _TimePoints loop) or that are cheap to repeat because their early-out guards
// make repeats free for the ~99% of cards without flavours/transfers/produced
// liquids. The only inexactness is mid-window rate recomputation (a fire dying
// mid-chunk, a cooker running past fuel exhaustion) becoming piecewise-constant
// at K-tick granularity — bounded to ≤K−1 ticks (4 in-game hours at K=16) per
// transition, strictly smaller than the 34–405 days CatchUpTickCap already
// discards wholesale, and not player-detectable. Full mechanism, call-site
// discriminator census, and empirical exactness checks (RepeatActionOnZero/
// OnFull usage, RestrictToSpecificValues × rate overlap) are in
// Documentation/Plans/Fleet/Game_Performance_Options_2026-08-25.md, `tick-batching`
// explorer — this file is the build, not the spec; cite that doc, don't
// re-derive it here.
//
// ArmForHop is called once per travel from CatchUpTickCap.ChangeEnvironment_Prefix,
// right after that class's own cap/chain-discount decision — see the hand-off
// note there. Eight SafePatcher hooks below (the plan's original five plus a
// UpdatePassiveEffectStacks guard added to fix a produced-liquids over-count —
// see that field's remarks), all soft-failing (a failed patch just leaves
// `Armed` permanently false, i.e. today's unbatched behavior) and all
// double-gated on `Armed && GameManager.IsCatchingUp`, which — per the survey's
// call-site census — makes it impossible for scaled/repeated durability changes
// to leak into ordinary (non-catch-up) gameplay ticks.
internal static class CatchUpTickBatching
{
    private static int _batchK;
    private static int _batchMinTicks;

    internal static bool Armed { get; private set; }

    // Set by ApplyRates_Prefix at the start of each chunk's call; consulted by
    // every other hook below for the remainder of that same synchronous call.
    private static int CurrentBatchTicksInt = 1;
    private static float CurrentBatchScale = 1f;

    // Real-tick-equivalent size of the CURRENT chunk (1 when batching is off/
    // unarmed) — read by CatchUpBudgetClamp so its tick-floor counts real ticks
    // instead of raw ApplyRates call counts once batching compresses many ticks
    // into one call.
    internal static int CurrentChunkTicks => CurrentBatchTicksInt;

    private static int[] _chunkSizes;
    private static int _chunkCursor;

    // Independent re-entrancy guards: each repeat-loop below calls straight back
    // into its own patched method, which would otherwise re-trigger the same
    // repeat logic recursively.
    private static bool _transferReentrant;
    private static bool _fadeFlavoursReentrant;
    private static bool _fadeSpicesReentrant;
    private static bool _producedLiquidsReentrant;

    // UpdateProducedLiquids has 3 reachable call sites per card within a single
    // catch-up pass — unlike FadeFlavours/FadeSpices, which each have exactly
    // one: (1) the direct end-of-block call in ApplyRates (GameManager.cs:5814,
    // always LAST for a given card), (2) a nested call from ApplyPassiveEffect
    // reachable through ChangeCardDurabilities' internal UpdatePassiveEffects
    // call (a newly-applying passive effect), and (3) a second nested path via
    // UpdatePassiveEffectStacks' EffectScalesWithDurabilities rescale branch
    // (Cancel-then-reapply when a durability-scaled effect's magnitude changes).
    // Only the direct call represents "this happens every one of the K real
    // ticks this chunk stands in for" and should get the repeat-multiplier;
    // the two nested calls are one-time transition events within the chunk's
    // window and must fire exactly once, unmultiplied, or a card with both
    // CurrentProducedLiquids and a rescaling passive effect over-produces by
    // up to K× on the tick that transition happens to land in. This flag is
    // held true for the full duration of EITHER nested source's execution
    // (both wrapped below); it is false again by the time the direct call
    // fires, since ChangeCardDurabilities and UpdatePassiveEffectStacks both
    // run to completion (synchronously, in the catch-up no-real-yield case)
    // strictly before ApplyRates reaches its own direct UpdateProducedLiquids
    // call for that card.
    private static bool _producedLiquidsNestedSourceActive;

    public static void Configure(ConfigFile config, Harmony harmony)
    {
        var batchCfg = config.Bind(
            "Performance", "CatchUpBatchTicks", 16,
            "Chunk size (K) for catch-up tick batching: instead of replaying every "
            + "elapsed tick individually, groups them into chunks of up to K ticks "
            + "each and scales/repeats the per-tick work accordingly. This is the "
            + "biggest lever in the catch-up performance plan — a capped 1344-tick "
            + "replay measured at 4.4-6.2s drops to an estimated ~0.8-1.1s at the "
            + "default K=16. Fidelity cost: mid-window rate changes (a fire dying, a "
            + "cooker running out of fuel) become piecewise-constant at K-tick "
            + "granularity — up to K-1 ticks (4 in-game hours at K=16) of timing "
            + "slop per transition, strictly smaller than what CatchUpTickCap already "
            + "discards wholesale on any capped hop. 0 disables batching entirely "
            + "(exact today's per-tick behavior).");
        _batchK = batchCfg.Value;

        var minCfg = config.Bind(
            "Performance", "CatchUpBatchMinTicks", 96,
            "Don't bother batching a replay shorter than this many ticks — the "
            + "overhead isn't worth it for a handful of ticks. Below this, catch-up "
            + "runs today's unbatched per-tick behavior even with batching enabled.");
        _batchMinTicks = minCfg.Value;

        if (_batchK <= 0)
        {
            Util.Log.Debug("CatchUpTickBatching: disabled via config (CatchUpBatchTicks=0).");
            return;
        }

        bool ok = true;
        ok &= PatchApplyRates(harmony);
        ok &= PatchChangeCardDurabilities(harmony);
        ok &= PatchUpdatePassiveEffectStacks(harmony);
        ok &= PatchUpdateCookingRecipes(harmony);
        ok &= PatchUpdateTransferEffects(harmony);
        ok &= PatchFadeAndProducedLiquids(harmony);

        if (ok)
            Util.Log.Debug($"CatchUpTickBatching: enabled (K={_batchK}, min {_batchMinTicks} ticks to arm).");
        else
            Util.Log.Warn("CatchUpTickBatching: one or more hooks failed to patch; "
                          + "falling back to unbatched per-tick catch-up (SafePatcher soft-fail).");
    }

    private static bool PatchApplyRates(Harmony harmony)
    {
        var prefix = new HarmonyMethod(AccessTools.Method(typeof(CatchUpTickBatching), nameof(ApplyRates_Prefix)));
        var postfix = new HarmonyMethod(AccessTools.Method(typeof(CatchUpTickBatching), nameof(ApplyRates_Postfix)));
        return SafePatcher.TryPatch(harmony, typeof(GameManager), "ApplyRates", prefix: prefix, postfix: postfix);
    }

    private static bool PatchChangeCardDurabilities(Harmony harmony)
    {
        var prefix = new HarmonyMethod(AccessTools.Method(typeof(CatchUpTickBatching), nameof(ChangeCardDurabilities_Prefix)));
        var postfix = new HarmonyMethod(AccessTools.Method(typeof(CatchUpTickBatching), nameof(ChangeCardDurabilities_Postfix)));
        return SafePatcher.TryPatch(harmony, typeof(GameManager), "ChangeCardDurabilities", prefix: prefix, postfix: postfix);
    }

    private static bool PatchUpdatePassiveEffectStacks(Harmony harmony)
    {
        var prefix = new HarmonyMethod(AccessTools.Method(typeof(CatchUpTickBatching), nameof(UpdatePassiveEffectStacks_Prefix)));
        var postfix = new HarmonyMethod(AccessTools.Method(typeof(CatchUpTickBatching), nameof(UpdatePassiveEffectStacks_Postfix)));
        return SafePatcher.TryPatch(harmony, typeof(InGameCardBase), "UpdatePassiveEffectStacks", prefix: prefix, postfix: postfix);
    }

    private static bool PatchUpdateCookingRecipes(Harmony harmony)
    {
        var prefix = new HarmonyMethod(AccessTools.Method(typeof(CatchUpTickBatching), nameof(UpdateCookingRecipes_Prefix)));
        return SafePatcher.TryPatch(harmony, typeof(GameManager), "UpdateCookingRecipes", prefix: prefix);
    }

    private static bool PatchUpdateTransferEffects(Harmony harmony)
    {
        var prefix = new HarmonyMethod(AccessTools.Method(typeof(CatchUpTickBatching), nameof(UpdateTransferEffects_Prefix)));
        return SafePatcher.TryPatch(harmony, typeof(InGameCardBase), "UpdateTransferEffects", prefix: prefix);
    }

    private static bool PatchFadeAndProducedLiquids(Harmony harmony)
    {
        bool ok = true;
        ok &= SafePatcher.TryPatch(harmony, typeof(InGameCardBase), "FadeFlavours",
            postfix: new HarmonyMethod(AccessTools.Method(typeof(CatchUpTickBatching), nameof(FadeFlavours_Postfix))));
        ok &= SafePatcher.TryPatch(harmony, typeof(InGameCardBase), "FadeSpices",
            postfix: new HarmonyMethod(AccessTools.Method(typeof(CatchUpTickBatching), nameof(FadeSpices_Postfix))));
        ok &= SafePatcher.TryPatch(harmony, typeof(InGameCardBase), "UpdateProducedLiquids",
            postfix: new HarmonyMethod(AccessTools.Method(typeof(CatchUpTickBatching), nameof(UpdateProducedLiquids_Postfix))));
        return ok;
    }

    // Called from CatchUpTickCap.ChangeEnvironment_Prefix, once per travel, right
    // after that class's own cap/chain-discount clamp has already written
    // envData.LastUpdatedTick — so `now - envData.LastUpdatedTick` here already
    // reflects effectiveW = min(gap, effectiveCap), matching the survey's design.
    internal static void ArmForHop(EnvironmentSaveDataByReference envData, int now)
    {
        Armed = false;
        CurrentBatchTicksInt = 1;
        CurrentBatchScale = 1f;
        _chunkSizes = null;
        _chunkCursor = 0;

        if (_batchK <= 0 || envData == null) return; // 0 = disabled

        int gap = now - envData.LastUpdatedTick;
        if (gap <= _batchMinTicks) return; // don't bother chunking a trivial replay

        int numChunks = (gap + _batchK - 1) / _batchK; // ceil(gap / K)
        if (numChunks <= 0 || numChunks >= gap) return; // nothing to compress

        // Remainder distribution so chunk sizes sum to exactly `gap`.
        int baseSize = gap / numChunks;
        int remainder = gap % numChunks;
        _chunkSizes = new int[numChunks];
        for (int i = 0; i < numChunks; i++)
            _chunkSizes[i] = baseSize + (i < remainder ? 1 : 0);

        envData.LastUpdatedTick = now - numChunks;
        Armed = true;
        Util.Log.Debug($"CatchUpTickBatching: armed {numChunks} chunk(s) for {gap} ticks (K={_batchK}).");
    }

    private static bool IsCatchingUp(GameManager gm) => gm != null && gm.IsCatchingUp;

    // Fires once per ApplyRates call, before the original body runs — advances
    // the chunk cursor so every other hook below sees this call's chunk size for
    // the remainder of this same synchronous call (ApplyRates internally starts
    // ChangeCardDurabilities/UpdateCookingRecipes/etc. via StartCoroutineEx,
    // which run their own prefixes before this call returns).
    private static void ApplyRates_Prefix(GameManager __instance)
    {
        if (!Armed || !IsCatchingUp(__instance)) { CurrentBatchTicksInt = 1; CurrentBatchScale = 1f; return; }
        if (_chunkSizes == null || _chunkCursor >= _chunkSizes.Length)
        {
            CurrentBatchTicksInt = 1;
            CurrentBatchScale = 1f;
            return;
        }
        CurrentBatchTicksInt = _chunkSizes[_chunkCursor];
        CurrentBatchScale = CurrentBatchTicksInt;
        _chunkCursor++;
    }

    // Counter sync top-up: vanilla's own ApplyRates tail loop
    // (.decomp/GameManager.cs:5836-5850, runs as part of the enumerator this
    // wraps) already advances non-Updated env counters by +1 per call — correct
    // for an unbatched 1-tick call, an under-crawl of (K-1) per chunk here. Only
    // counters NOT attached to a live card this chunk need the top-up; Updated
    // counters already jumped straight to their live value in vanilla's own pass.
    private static IEnumerator ApplyRates_Postfix(IEnumerator result, GameManager __instance)
    {
        while (result.MoveNext()) yield return result.Current;

        if (!Armed || !IsCatchingUp(__instance)) yield break;
        int extra = CurrentBatchTicksInt - 1;
        if (extra <= 0) yield break;

        var envData = __instance.LocalCountersEnv;
        if (envData?.CountersDict == null) yield break;
        var allCounters = __instance.AllCounters;
        for (int i = 0; i < allCounters.Count; i++)
        {
            var counter = allCounters[i];
            if (counter == null || counter.Updated || counter.Model == null) continue;
            if (!envData.CountersDict.TryGetValue(counter.Model, out var envCounter) || envCounter == null) continue;
            if (envCounter.Value < counter.Value)
                envCounter.Value = Mathf.Min(envCounter.Value + extra, counter.Value);
        }
    }

    // The catch-up call site is uniquely discriminated by its flag combination
    // (_Feedback:false, _SortSlot:false, _LiquidConcentrationPreservation:true —
    // every OTHER ChangeCardDurabilities call site in GameManager passes
    // _Feedback:true,_SortSlot:true) plus GameManager.IsCatchingUp, so scaling
    // here cannot leak into an action-driven or ordinary-tick durability change.
    private static bool ChangeCardDurabilities_Prefix(GameManager __instance,
        ref float _Spoilage, ref float _Usage, ref float _Fuel, ref float _Consumables, ref float _Liquid,
        ref float _Special1, ref float _Special2, ref float _Special3, ref float _Special4,
        bool _Feedback, bool _SortSlot, bool _LiquidConcentrationPreservation)
    {
        if (!Armed || !IsCatchingUp(__instance)) return true;
        if (_Feedback || _SortSlot || !_LiquidConcentrationPreservation) return true;
        float scale = CurrentBatchScale;
        if (scale <= 1f) return true;

        _Spoilage *= scale;
        _Usage *= scale;
        _Fuel *= scale;
        _Consumables *= scale;
        _Liquid *= scale;
        _Special1 *= scale;
        _Special2 *= scale;
        _Special3 *= scale;
        _Special4 *= scale;
        return true;
    }

    // Brackets the ACTUAL execution of ChangeCardDurabilities' body (not just
    // the trivial stub call the prefix above sees) with the shared nested-
    // source flag — this is a coroutine method, so its body only runs when the
    // returned enumerator is driven, which happens inside this wrap, not
    // before it. See _producedLiquidsNestedSourceActive's remarks for why.
    private static IEnumerator ChangeCardDurabilities_Postfix(IEnumerator result, GameManager __instance)
    {
        if (!Armed || !IsCatchingUp(__instance))
        {
            while (result.MoveNext()) yield return result.Current;
            yield break;
        }

        bool wasActive = _producedLiquidsNestedSourceActive;
        _producedLiquidsNestedSourceActive = true;
        try
        {
            while (result.MoveNext()) yield return result.Current;
        }
        finally
        {
            _producedLiquidsNestedSourceActive = wasActive;
        }
    }

    // UpdatePassiveEffectStacks is a plain synchronous method (not a coroutine
    // stub), so a simple prefix/postfix pair correctly brackets its entire
    // execution, including any GM.StartCoroutine(ApplyPassiveEffect(...)) calls
    // it makes internally (those also resolve synchronously in catch-up mode,
    // same as everywhere else in this file).
    private static void UpdatePassiveEffectStacks_Prefix(out bool __state)
    {
        __state = _producedLiquidsNestedSourceActive;
        if (Armed && IsCatchingUp(MBSingleton<GameManager>.Instance))
            _producedLiquidsNestedSourceActive = true;
    }

    private static void UpdatePassiveEffectStacks_Postfix(bool __state)
    {
        _producedLiquidsNestedSourceActive = __state;
    }

    // Cooking is natively batchable — UpdateCardCooking's own _TimePoints loop
    // preserves exact completion counts and per-tick condition rechecks; this
    // just forwards the chunk size instead of the hardcoded 1 vanilla always
    // passes (.decomp/GameManager.cs:5749).
    private static void UpdateCookingRecipes_Prefix(GameManager __instance, ref int _TimePoints, int _CatchupTick)
    {
        if (!Armed || !IsCatchingUp(__instance) || _CatchupTick <= 0) return;
        if (CurrentBatchTicksInt <= 1) return;
        _TimePoints = CurrentBatchTicksInt;
    }

    // UpdateTransferEffects' _TimePoints parameter does NOT batch named transfer
    // effects (alreadyPerformedTransfers is scoped per-invocation, so one call
    // with K would yield only ONE transfer instead of K) — must be repeated, not
    // scaled. Prefix-skip + yield-through wrapper, re-entrancy guarded so the
    // wrapper's own inner calls pass through instead of re-arming.
    private static bool UpdateTransferEffects_Prefix(InGameCardBase __instance, int _TimePoints, ref IEnumerator __result)
    {
        if (_transferReentrant || !Armed || _TimePoints != 1) return true;
        if (!IsCatchingUp(MBSingleton<GameManager>.Instance)) return true;
        int reps = CurrentBatchTicksInt;
        if (reps <= 1) return true;

        __result = BatchedTransferWrapper(__instance, reps);
        return false;
    }

    private static IEnumerator BatchedTransferWrapper(InGameCardBase card, int reps)
    {
        _transferReentrant = true;
        try
        {
            for (int i = 0; i < reps; i++)
            {
                var inner = card.UpdateTransferEffects(1);
                // Yield-through, never bare-drain (memory: reference_synchronous_
                // coroutine_drain_freeze) — the inner call can start real
                // Unity-scheduled coroutines whose completion needs the pump to
                // advance.
                while (inner.MoveNext()) yield return inner.Current;
            }
        }
        finally
        {
            _transferReentrant = false;
        }
    }

    // FadeFlavours/FadeSpices/UpdateProducedLiquids have per-call accumulation
    // semantics with no batch parameter, but each early-outs immediately for the
    // ~99% of cards with no flavours/spices/produced liquids — repeating the
    // body K-1 extra times is exact and free for those cards; only genuinely
    // flavoured/spiced/producing cards pay the extra work, which is correct
    // since vanilla would have paid it too across K real ticks.
    private static void FadeFlavours_Postfix(InGameCardBase __instance)
    {
        if (_fadeFlavoursReentrant || !Armed || !IsCatchingUp(MBSingleton<GameManager>.Instance)) return;
        int extra = CurrentBatchTicksInt - 1;
        if (extra <= 0) return;
        _fadeFlavoursReentrant = true;
        try { for (int i = 0; i < extra; i++) __instance.FadeFlavours(); }
        finally { _fadeFlavoursReentrant = false; }
    }

    private static void FadeSpices_Postfix(InGameCardBase __instance, bool _FromCombatHit)
    {
        if (_fadeSpicesReentrant || !Armed || !IsCatchingUp(MBSingleton<GameManager>.Instance)) return;
        int extra = CurrentBatchTicksInt - 1;
        if (extra <= 0) return;
        _fadeSpicesReentrant = true;
        try { for (int i = 0; i < extra; i++) __instance.FadeSpices(_FromCombatHit); }
        finally { _fadeSpicesReentrant = false; }
    }

    private static void UpdateProducedLiquids_Postfix(InGameCardBase __instance)
    {
        if (_producedLiquidsReentrant || !Armed || !IsCatchingUp(MBSingleton<GameManager>.Instance)) return;
        // Only the direct end-of-block call (fired with neither nested source's
        // window active) should be multiplied — see _producedLiquidsNestedSourceActive.
        if (_producedLiquidsNestedSourceActive) return;
        int extra = CurrentBatchTicksInt - 1;
        if (extra <= 0) return;
        _producedLiquidsReentrant = true;
        try { for (int i = 0; i < extra; i++) __instance.UpdateProducedLiquids(); }
        finally { _producedLiquidsReentrant = false; }
    }
}
