using System.Collections;
using System.Reflection;
using CSFFModFramework.Util;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CSFFModFramework.Loading;

/// <summary>
/// Fixes a vanilla load-order bug where placed blueprint containers
/// (CT2 with <c>ContainedBlueprintCards</c>, e.g. H&amp;F's Oil Press) lose
/// their Recipes tab after save/exit/reload.
///
/// Root cause: <c>GameLoad.LoadMainGameData</c> restores placed cards
/// (calling <c>InGameCardBase.Init</c>) BEFORE our framework postfix runs
/// <c>WarpResolver</c>. At restore time, the template's
/// <c>ContainedBlueprintCards</c> array is still empty (only the WarpData
/// strings are populated). The card's <c>Init</c> branch at line 4144 of
/// <c>InGameCardBase</c> reads <c>ContainedBlueprintCards.Length</c>, sees
/// 0, and never adds inventory slots. <c>SpawnDefaultContainedBlueprints</c>
/// is also dispatched but its first guard (length == 0) returns immediately.
/// The placed instance ends up with an empty <c>CardsInInventory</c>.
///
/// Fix: schedule a coroutine after WarpResolver to:
///   1. Yield one frame to let any in-flight Init coroutines complete and
///      ensure <c>GameManager.AllCards</c> is fully populated.
///   2. Iterate every <c>InGameCardBase</c> tracked by the master
///      <c>GameManager.AllCards</c> list.
///   3. For each blueprint container with stale <c>CardsInInventory</c>,
///      reflect-invoke the game's own <c>SpawnDefaultContainedBlueprints</c>
///      so behavior matches a fresh placement exactly.
///
/// Logs normal no-op details at Debug and only surfaces actual repairs or
/// exceptional fallback paths at Info/Warning.
///
/// Pattern is generic — applies to any modded blueprint container, not
/// just OilPress.
/// </summary>
internal static class BlueprintContainerSaveLoadFix
{
    private static bool _subscribedToGmInitialized;
    private static Action _gmInitializedHandler;

    // True once OnGMInitialized has fired at least once this session.
    // The fresh-placement patch must NOT run during save loading — the synchronous
    // coroutine drain was causing the game to freeze before FinishInitializing
    // completed when ModCore is present. DeferredRun handles the save-load path.
    private static bool _isInGameplay;

    // --- Fresh-placement patch state ---
    // Prefix captures the container card; postfix uses it to wrap the IEnumerator.
    // Single-threaded Unity game loop — no concurrent placements possible.
    private static object _pendingCard;
    private static MethodInfo _freshSetBpAvailable;

    public static void Schedule()
    {
        // Reset gameplay flag — save loading is starting and we are not yet in gameplay.
        // SpawnDefault_Postfix must not run its fresh-placement logic until after
        // OnGMInitialized fires (which sets _isInGameplay = true).
        _isInGameplay = false;

        // Subscribe to GameManager.OnGMInitialized so the refresh pass fires after every
        // save load, when GameManager exists and AllCards is populated. No boot-time pass —
        // it would always be a no-op (no save loaded yet) and just adds log noise.
        TrySubscribeToGameManagerInitialized();
    }

    /// <summary>
    /// Patches <c>GameManager.SpawnDefaultContainedBlueprints</c> so that freshly placed
    /// blueprint containers (e.g. Water-Driven Workshop placed via blueprint during gameplay)
    /// have their contained blueprints marked Available immediately after spawning.
    ///
    /// Without this, <c>SetBpAvailable</c> is only called by the save/load fix on
    /// <c>OnGMInitialized</c> — a freshly placed station must be saved and reloaded before
    /// its Recipes tab becomes functional.
    /// </summary>
    public static void ApplyFreshPlacementPatch(Harmony harmony)
    {
        try
        {
            var gmType = AccessTools.TypeByName("GameManager");
            if (gmType == null)
            {
                Log.Warn("[BlueprintContainerSaveLoadFix] GameManager type not found — fresh-placement patch unavailable");
                return;
            }

            var method = AccessTools.Method(gmType, "SpawnDefaultContainedBlueprints");
            if (method == null)
            {
                Log.Warn("[BlueprintContainerSaveLoadFix] SpawnDefaultContainedBlueprints not found — fresh-placement patch unavailable");
                return;
            }

            _freshSetBpAvailable = AccessTools.Method(gmType, "SetBpAvailable");
            if (_freshSetBpAvailable == null)
                Log.Warn("[BlueprintContainerSaveLoadFix] SetBpAvailable not found — fresh-placement unlock degraded");

            harmony.Patch(method,
                prefix:  new HarmonyMethod(typeof(BlueprintContainerSaveLoadFix), nameof(SpawnDefault_Prefix)),
                postfix: new HarmonyMethod(typeof(BlueprintContainerSaveLoadFix), nameof(SpawnDefault_Postfix)));

            Log.Debug("[BlueprintContainerSaveLoadFix] SpawnDefaultContainedBlueprints patched for fresh-placement unlock");
        }
        catch (Exception ex)
        {
            Log.Warn($"[BlueprintContainerSaveLoadFix] fresh-placement patch setup failed: {Log.ExceptionText(ex)}");
        }
    }

    // Captures the container card before SpawnDefaultContainedBlueprints runs.
    // __0 is the first parameter (the blueprint container InGameCardBase).
    private static bool SpawnDefault_Prefix(object __0)
    {
        _pendingCard = __0;
        ResetStaleContainedBlueprintSlots(__0);
        return true;
    }

    // Fresh-placement postfix: wraps the spawned IEnumerator so blueprint unlock
    // fires asynchronously after the original coroutine completes.
    //
    // IMPORTANT: only active during gameplay (_isInGameplay == true). During save
    // loading, SpawnDefaultContainedBlueprints fires for every placed card before
    // FinishInitializing completes. A synchronous coroutine drain in that path
    // causes a reproducible freeze when ModCore is installed alongside the framework.
    // The save-load path is handled by DeferredRun (via OnGMInitialized).
    private static void SpawnDefault_Postfix(object __instance, ref IEnumerator __result)
    {
        var card = _pendingCard;
        _pendingCard = null;

        if (!_isInGameplay) return;
        if (card == null || _freshSetBpAvailable == null) return;
        if (ContainerStartsBlueprintsLocked(card)) return;

        __result = FreshPlacementWrapper(__result, card, __instance, _freshSetBpAvailable);
    }

    // Yields through the original spawn coroutine frame-by-frame, then unlocks
    // contained blueprints. Async so it never blocks the main thread.
    private static IEnumerator FreshPlacementWrapper(
        IEnumerator original, object card, object gmInstance, MethodInfo setBpAvailable)
    {
        while (true)
        {
            bool hasNext;
            try { hasNext = original.MoveNext(); }
            catch { yield break; }
            if (!hasNext) break;
            yield return original.Current;
        }

        try
        {
            EnsureContainedBlueprintStates(card, gmInstance);
            int count = UnlockContainedBlueprints(card, gmInstance, setBpAvailable);
            if (count > 0)
                Log.Debug($"[BlueprintContainerSaveLoadFix] fresh placement: marked {count} blueprint(s) Available for {DescribeCard(card)}");
        }
        catch (Exception ex)
        {
            Log.Warn($"[BlueprintContainerSaveLoadFix] fresh-placement unlock failed: {Log.ExceptionText(ex)}");
        }
    }

    private static IEnumerator EmptyCoroutine() { yield break; }

    /// <summary>
    /// Subscribes to <c>GameManager.OnGMInitialized</c> (a public static Action field
    /// invoked at the end of <c>FinishInitializing</c>) so our refresh pass re-runs
    /// every time a save finishes loading. Idempotent.
    /// </summary>
    private static void TrySubscribeToGameManagerInitialized()
    {
        if (_subscribedToGmInitialized) return;

        try
        {
            var gmType = AccessTools.TypeByName("GameManager");
            if (gmType == null)
            {
                Log.Warn("[BlueprintContainerSaveLoadFix] GameManager type not found — cannot subscribe to OnGMInitialized");
                return;
            }

            var field = gmType.GetField("OnGMInitialized", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null || field.FieldType != typeof(Action))
            {
                Log.Warn($"[BlueprintContainerSaveLoadFix] OnGMInitialized field not found or wrong type (expected Action, got {field?.FieldType?.Name ?? "<null>"})");
                return;
            }

            _gmInitializedHandler = OnGameManagerInitialized;
            var current = (Action)field.GetValue(null);
            var combined = (Action)Delegate.Combine(current, _gmInitializedHandler);
            field.SetValue(null, combined);

            _subscribedToGmInitialized = true;
            Log.Debug("[BlueprintContainerSaveLoadFix] subscribed to GameManager.OnGMInitialized");
        }
        catch (Exception ex)
        {
            Log.Warn($"[BlueprintContainerSaveLoadFix] failed to subscribe to OnGMInitialized: {ex}");
        }
    }

    private static void OnGameManagerInitialized()
    {
        _isInGameplay = true;
        Log.Debug("[BlueprintContainerSaveLoadFix] GameManager.OnGMInitialized fired — scheduling save-load refresh");
        var host = Plugin.Instance;
        if (host == null)
        {
            try { RunSync(); } catch (Exception ex) { Log.Warn($"[BlueprintContainerSaveLoadFix] post-init sync run failed: {ex}"); }
            return;
        }
        host.StartCoroutine(DeferredRun());
    }

    private static IEnumerator DeferredRun()
    {
        // Yield once so any Init coroutines started during save load can complete
        // and AllCards is fully populated.
        yield return null;

        // Setup is wrapped in try/catch (yield not allowed inside try). The heavy
        // per-card loop runs in a separate coroutine that yields every 100 cards
        // so the loading screen stays responsive on saves with many cards.
        RunContext ctx;
        try
        {
            ctx = PrepareRunContext();
        }
        catch (Exception ex)
        {
            Log.Warn($"[BlueprintContainerSaveLoadFix] setup failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            yield break;
        }
        if (ctx == null) yield break;

        // Yielded loop: the main thread can paint frames between batches.
        var enumerator = RunNowCoroutine(ctx);
        while (true)
        {
            object next;
            try
            {
                if (!enumerator.MoveNext()) break;
                next = enumerator.Current;
            }
            catch (Exception ex)
            {
                Log.Warn($"[BlueprintContainerSaveLoadFix] loop failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
                yield break;
            }
            yield return next;
        }
    }

    private sealed class RunContext
    {
        public object GmInstance;
        public Type GmType;
        public MethodInfo SpawnMethod;
        public MethodInfo StartCoroutineMethod;
        public MethodInfo SetBpAvailable;
        public List<object> Cards;
    }

    private static RunContext PrepareRunContext()
    {
        Log.Debug("[BlueprintContainerSaveLoadFix] starting refresh pass");

        var gmInstance = GetGameManagerInstance();
        if (gmInstance == null)
        {
            // Should not happen — we only run via OnGMInitialized, which fires after the
            // singleton is set. If we hit this, there's a load-order regression to investigate.
            Log.Warn("[BlueprintContainerSaveLoadFix] GameManager.Instance == null after OnGMInitialized — skipping");
            return null;
        }

        var gmType = gmInstance.GetType();

        var spawnMethod = AccessTools.Method(gmType, "SpawnDefaultContainedBlueprints");
        if (spawnMethod == null)
        {
            Log.Warn("[BlueprintContainerSaveLoadFix] GameManager.SpawnDefaultContainedBlueprints not found — fix unavailable");
            return null;
        }

        var startCoroutineMethod = gmType.GetMethod(
            "StartCoroutine",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new[] { typeof(IEnumerator) },
            null);
        if (startCoroutineMethod == null)
        {
            Log.Warn("[BlueprintContainerSaveLoadFix] StartCoroutine(IEnumerator) not found on GameManager");
            return null;
        }

        // Resolve SetBpAvailable once — used to force BlueprintModelStates[card] = Available
        // for every contained blueprint of every placed container.
        var setBpAvailable = AccessTools.Method(gmType, "SetBpAvailable");
        if (setBpAvailable == null)
            Log.Warn("[BlueprintContainerSaveLoadFix] GameManager.SetBpAvailable not found — Recipes-tab unlock unavailable");

        var cards = CollectCards(gmInstance, gmType);
        Log.Debug($"[BlueprintContainerSaveLoadFix] enumerating {cards.Count} card(s)");

        return new RunContext
        {
            GmInstance = gmInstance,
            GmType = gmType,
            SpawnMethod = spawnMethod,
            StartCoroutineMethod = startCoroutineMethod,
            SetBpAvailable = setBpAvailable,
            Cards = cards,
        };
    }

    /// <summary>
    /// Called when <c>Plugin.Instance</c> is unavailable at GM-init time. Logs an error
    /// and skips rather than draining synchronously — synchronous drain causes a reproducible
    /// freeze when ModCore is installed.
    /// </summary>
    private static void RunSync()
    {
        Log.Error("[BlueprintContainerSaveLoadFix] Plugin.Instance is null at GM init — skipping blueprint slot repair to avoid freeze.");
    }

    private static IEnumerator RunNowCoroutine(RunContext ctx)
    {
        const int YieldEvery = 100;
        int processed = 0;
        int containers = 0;
        int refreshed = 0;
        int unlocked = 0;

        foreach (var card in ctx.Cards)
        {
            processed++;

            if (card != null && IsBlueprintContainer(card))
            {
                containers++;
                ProcessOneCard(ctx, card, ref refreshed, ref unlocked);
            }

            if (processed % YieldEvery == 0)
                yield return null;
        }

        var summary = $"[BlueprintContainerSaveLoadFix] done: {containers} blueprint container(s) seen, {refreshed} re-spawned, {unlocked} blueprint(s) marked Available";
        Log.Info(summary);

        // Retroactive fallback: ensure all regular mod blueprints are registered in
        // AllBlueprintModels and have correct BlueprintModelStates.
        //
        // Primary path: BlueprintFlagFix.FinishInitializing_Prefix adds them BEFORE
        // FinishInitializing's state-restoration loop, so states are normally restored
        // from PurchasableBlueprintCards / ResearchedBlueprintCards automatically.
        //
        // This pass handles the case where FinishInitializing rebuilt AllBlueprintModels
        // from IncludedCards (overwriting the prefix's additions) or where the prefix
        // patch was unavailable.
        RestoreModBlueprintStates(ctx.GmInstance, ctx.GmType);
    }

    // Ensures all mod blueprints are in AllBlueprintModels and have a BlueprintModelState.
    // If a mod blueprint is missing from BlueprintModelStates (state was not restored by
    // FinishInitializing), its state is derived from the game's in-memory save-data fields.
    private static void RestoreModBlueprintStates(object gmInstance, Type gmType)
    {
        try
        {
            // Resolve AllBlueprintModels
            var allBpModelsField = AccessTools.Field(gmType, "AllBlueprintModels");
            var allBpModels = allBpModelsField?.GetValue(gmInstance) as IList;
            if (allBpModels == null) return;

            // Resolve BlueprintModelStates
            var statesField = AccessTools.Field(gmType, "BlueprintModelStates");
            var states = statesField?.GetValue(gmInstance) as IDictionary;
            if (states == null) return;

            var stateType = states.GetType().GetGenericArguments().Length >= 2
                ? states.GetType().GetGenericArguments()[1]
                : null;
            if (stateType == null || !stateType.IsEnum) return;

            // Parse Available and Purchased enum values (fallback to int if name not found).
            object available, purchased;
            try { available = Enum.Parse(stateType, "Available"); }
            catch { available = Enum.ToObject(stateType, 1); }
            try { purchased = Enum.Parse(stateType, "Purchased"); }
            catch
            {
                try { purchased = Enum.Parse(stateType, "Researched"); }
                catch { purchased = Enum.ToObject(stateType, 2); }
            }

            // Build sets of UIDs from the game's in-memory save state lists so we can restore
            // the correct state for blueprints that FinishInitializing missed.
            var purchasedUids = BuildUidSet(gmInstance, gmType, "ResearchedBlueprintCards", isPurchased: true);
            var availableUids = BuildUidSet(gmInstance, gmType, "PurchasableBlueprintCards", isPurchased: false);

            int added = 0;
            int restored = 0;
            foreach (var uid in Loading.JsonDataLoader.AllModUniqueIds)
            {
                if (!Loading.JsonDataLoader.ParsedJsonByUniqueId.TryGetValue(uid, out var parsed)) continue;
                if (!parsed.TryGetValue("CardType", out var ct)) continue;
                bool isBp = ct is long l && l == 7 || ct is string s && s == "7";
                if (!isBp) continue;

                var card = Data.GameRegistry.GetByUid(uid) as CardData;
                if (card == null) continue;

                // Ensure blueprint is in AllBlueprintModels (needed for future save cycles).
                if (!allBpModels.Contains(card))
                {
                    allBpModels.Add(card);
                    added++;
                }

                // If state is already present (restored by FinishInitializing), leave it.
                if (states.Contains(card)) continue;

                // Retroactively restore state from the game's in-memory save data.
                object targetState;
                if (purchasedUids.Contains(uid))
                    targetState = purchased;
                else if (availableUids.Contains(uid))
                    targetState = available;
                else
                    continue; // blueprint was NotAvailable/undiscovered — don't seed

                states[card] = targetState;
                restored++;
            }

            if (added > 0 || restored > 0)
                Log.Info($"[BlueprintStateFix] retroactive: {added} blueprint(s) added to AllBlueprintModels, {restored} state(s) restored");
        }
        catch (Exception ex)
        {
            Log.Warn($"[BlueprintStateFix] RestoreModBlueprintStates failed: {Log.ExceptionText(ex)}");
        }
    }

    // Reads a string list/array field on GameManager (PurchasableBlueprintCards or
    // ResearchedBlueprintCards) and extracts the plain UniqueIDs from entries that
    // use the "GUID(CardName)" save format.
    private static HashSet<string> BuildUidSet(object gmInstance, Type gmType, string fieldName, bool isPurchased)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var field = AccessTools.Field(gmType, fieldName);
            if (field == null) return result;

            var value = field.GetValue(gmInstance);
            if (value == null) return result;

            // Field may be List<string>, string[], List<BlueprintResearchData>, etc.
            // For strings: extract UID from "GUID(CardName)" format.
            // For BlueprintResearchData objects: read BlueprintID field.
            if (value is IEnumerable enumerable)
            {
                foreach (var entry in enumerable)
                {
                    if (entry == null) continue;

                    string uid;
                    if (entry is string str)
                    {
                        uid = ExtractUidFromSaveEntry(str);
                    }
                    else
                    {
                        // Likely BlueprintResearchData — read its BlueprintID field.
                        var idField = entry.GetType().GetField("BlueprintID",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        uid = idField?.GetValue(entry) as string;
                        if (uid != null) uid = ExtractUidFromSaveEntry(uid);
                    }

                    if (!string.IsNullOrEmpty(uid))
                        result.Add(uid);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[BlueprintStateFix] BuildUidSet({fieldName}) failed: {Log.ExceptionText(ex)}");
        }
        return result;
    }

    // Save entries use "UniqueID(CardName)" format (e.g. "advanced_copper_tools_bp_oil(Bp_Oil)").
    // Extract the UniqueID portion.
    private static string ExtractUidFromSaveEntry(string entry)
    {
        if (string.IsNullOrEmpty(entry)) return entry;
        var paren = entry.IndexOf('(');
        return paren > 0 ? entry.Substring(0, paren) : entry;
    }

    private static void ProcessOneCard(RunContext ctx, object card,
        ref int refreshed, ref int unlocked)
    {
        try
        {
            // 1) Re-invoke SpawnDefaultContainedBlueprints for every blueprint container.
            //
            // HasCardInInventory has a false-positive: after Init re-runs (post-WarpResolver),
            // CardsInInventory gets N slots sized from the template but each slot has
            // MainCard == null. HasCardInInventory checks slot existence, not whether
            // MainCard is populated, so it returns true for empty slots.
            //
            // SpawnDefaultContainedBlueprints is idempotent — it calls HasCardInInventory
            // internally and only spawns cards not already present. We drain the returned
            // coroutine synchronously here because we are inside a coroutine (DeferredRun)
            // and the drain is bounded (at most a few blueprint cards per container).
            // Note: _isInGameplay is true at this point (set in OnGameManagerInitialized
            // before DeferredRun is scheduled), so the Harmony postfix wraps the coroutine
            // in FreshPlacementWrapper. Draining the wrapper is equivalent to draining
            // the original — small bounded work, safe inside a yielding coroutine.
            ResetStaleContainedBlueprintSlots(card);
            EnsureContainedBlueprintStates(card, ctx.GmInstance);
            var iter = ctx.SpawnMethod.Invoke(ctx.GmInstance, new[] { card }) as IEnumerator;
            if (iter != null)
            {
                try
                {
                    while (iter.MoveNext()) { }
                    refreshed++;
                    Log.Debug($"[BlueprintContainerSaveLoadFix] re-spawned blueprints for {DescribeCard(card)}");
                }
                catch (Exception ex)
                {
                    Log.Warn($"[BlueprintContainerSaveLoadFix] SpawnDefault threw for {DescribeCard(card)}: {Log.ExceptionText(ex)}");
                }
            }
            else
            {
                Log.Warn($"[BlueprintContainerSaveLoadFix] SpawnDefault returned null IEnumerator for card {DescribeCard(card)}");
            }

            // 2) Ensure each contained blueprint is unlocked (Available state).
            if (ctx.SetBpAvailable != null && !ContainerStartsBlueprintsLocked(card))
            {
                unlocked += UnlockContainedBlueprints(card, ctx.GmInstance, ctx.SetBpAvailable);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[BlueprintContainerSaveLoadFix] processing card failed: {Log.ExceptionText(ex)}");
        }
    }

    /// <summary>
    /// Mirrors the gate used by <c>GameManager.SpawnDefaultContainedBlueprints</c> —
    /// only auto-unlock contained blueprints when the container's
    /// <c>ContainedBlueprintsDontStartUnlocked</c> flag is false (the vanilla default).
    /// </summary>
    private static bool ContainerStartsBlueprintsLocked(object card)
    {
        try
        {
            var cardModelProp = card.GetType().GetProperty("CardModel", BindingFlags.Instance | BindingFlags.Public);
            var cardModel = cardModelProp?.GetValue(card, null);
            if (cardModel == null) return true; // safe default
            var flagField = AccessTools.Field(cardModel.GetType(), "ContainedBlueprintsDontStartUnlocked");
            if (flagField == null) return false;
            return (bool)flagField.GetValue(cardModel);
        }
        catch { return false; }
    }

    /// <summary>
    /// For every entry in the container's <c>ContainedBlueprintCards</c>, invoke
    /// <c>GameManager.SetBpAvailable</c>. The game function is idempotent — it returns
    /// immediately if the blueprint is already Available or not in the dictionary.
    /// Returns the count of cards we attempted to unlock (regardless of whether they
    /// were already Available).
    /// </summary>
    private static int UnlockContainedBlueprints(object card, object gmInstance, MethodInfo setBpAvailable)
    {
        try
        {
            var cardModelProp = card.GetType().GetProperty("CardModel", BindingFlags.Instance | BindingFlags.Public);
            var cardModel = cardModelProp?.GetValue(card, null);
            if (cardModel == null) return 0;

            var containedField = AccessTools.Field(cardModel.GetType(), "ContainedBlueprintCards");
            var containedArr = containedField?.GetValue(cardModel) as Array;
            if (containedArr == null || containedArr.Length == 0) return 0;

            int count = 0;
            foreach (var bp in containedArr)
            {
                if (bp == null) continue;
                try
                {
                    setBpAvailable.Invoke(gmInstance, new[] { bp });
                    count++;
                }
                catch (Exception ex)
                {
                    Log.Warn($"[BlueprintContainerSaveLoadFix] SetBpAvailable threw for {DescribeBlueprint(bp)}: {Log.ExceptionText(ex)}");
                }
            }
            return count;
        }
        catch { return 0; }
    }

    /// <summary>
    /// The inspector reads GM.BlueprintModelStates[containedBlueprint] directly when
    /// selecting a recipe. If a modded contained blueprint was loaded after the game's
    /// initial blueprint-state pass, SetBpAvailable is a no-op and the direct indexer
    /// can keep the Recipes tab unusable. Seed missing states as Available for the
    /// container's own recipes; the container itself remains the visibility gate.
    /// </summary>
    private static int EnsureContainedBlueprintStates(object card, object gmInstance)
    {
        try
        {
            if (card == null || gmInstance == null) return 0;

            var contained = GetContainedBlueprints(card);
            if (contained == null || contained.Length == 0) return 0;

            var gmType = gmInstance.GetType();
            var states = AccessTools.Field(gmType, "BlueprintModelStates")?.GetValue(gmInstance) as IDictionary;
            if (states == null) return 0;

            var stateType = states.GetType().GetGenericArguments().Length >= 2
                ? states.GetType().GetGenericArguments()[1]
                : null;
            if (stateType == null || !stateType.IsEnum) return 0;

            var available = Enum.Parse(stateType, "Available");
            var allBlueprintModels = AccessTools.Field(gmType, "AllBlueprintModels")?.GetValue(gmInstance) as IList;

            int added = 0;
            foreach (var bp in contained)
            {
                if (bp == null) continue;

                if (!states.Contains(bp))
                {
                    states.Add(bp, available);
                    added++;
                }

                if (allBlueprintModels != null && !allBlueprintModels.Contains(bp))
                    allBlueprintModels.Add(bp);
            }

            if (added > 0)
                Log.Debug($"[BlueprintContainerSaveLoadFix] seeded {added} missing blueprint state(s) for {DescribeCard(card)}");

            return added;
        }
        catch (Exception ex)
        {
            Log.Warn($"[BlueprintContainerSaveLoadFix] blueprint-state seeding failed for {DescribeCard(card)}: {Log.ExceptionText(ex)}");
            return 0;
        }
    }

    /// <summary>
    /// Repairs the stale-slot shape created when a blueprint container is initialized
    /// before WarpResolver has populated ContainedBlueprintCards. The game allocates
    /// placeholder InventorySlot entries, and HasCardInInventory can then report that
    /// contained blueprints already exist even when MainCard is null. Hybrid stations
    /// can also declare normal InventorySlots; those must live after the hidden
    /// contained-blueprint slots or the station has recipes but no usable storage.
    /// </summary>
    private static int ResetStaleContainedBlueprintSlots(object card)
    {
        try
        {
            if (card == null || !IsBlueprintContainer(card)) return 0;

            var contained = GetContainedBlueprints(card);
            if (contained == null || contained.Length == 0) return 0;

            var slots = GetInventorySlots(card);
            if (slots == null) return 0;

            var liveSlotsByBlueprint = new Dictionary<object, object>();
            var storageSlots = new List<object>();
            var slotType = default(Type);
            var containedCount = contained.Length;

            for (var i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot == null) continue;
                slotType ??= slot.GetType();

                var mainCard = GetMemberValue(slot, "MainCard");
                if (mainCard == null)
                {
                    if (i >= containedCount)
                        storageSlots.Add(slot);
                    continue;
                }

                var model = GetMemberValue(mainCard, "CardModel");
                if (model != null && ContainsReference(contained, model) && !liveSlotsByBlueprint.ContainsKey(model))
                {
                    liveSlotsByBlueprint.Add(model, slot);
                    continue;
                }

                storageSlots.Add(slot);
            }

            var missing = 0;
            foreach (var bp in contained)
            {
                if (bp != null && !liveSlotsByBlueprint.ContainsKey(bp))
                    missing++;
            }

            var desiredStorageSlots = GetHybridStorageSlotCount(card, containedCount);
            var neededStorageSlots = Math.Max(0, desiredStorageSlots - storageSlots.Count);

            if (missing == 0 && neededStorageSlots == 0 && slots.Count == containedCount + storageSlots.Count) return 0;

            slotType ??= AccessTools.TypeByName("InventorySlot");
            if (slotType == null)
            {
                Log.Warn($"[BlueprintContainerSaveLoadFix] InventorySlot type not found while repairing {DescribeCard(card)}");
                return 0;
            }

            slots.Clear();
            foreach (var bp in contained)
            {
                if (bp != null && liveSlotsByBlueprint.TryGetValue(bp, out var liveSlot))
                    slots.Add(liveSlot);
                else
                    slots.Add(Activator.CreateInstance(slotType));
            }

            foreach (var storageSlot in storageSlots)
                slots.Add(storageSlot);

            for (var i = 0; i < neededStorageSlots; i++)
                slots.Add(Activator.CreateInstance(slotType));

            RefreshInventoryInfo(card);

            if (missing > 0)
                Log.Debug($"[BlueprintContainerSaveLoadFix] reset {missing} stale contained-blueprint slot(s) for {DescribeCard(card)}");

            if (neededStorageSlots > 0)
                Log.Debug($"[BlueprintContainerSaveLoadFix] added {neededStorageSlots} hybrid storage slot(s) for {DescribeCard(card)}");

            return missing + neededStorageSlots;
        }
        catch (Exception ex)
        {
            Log.Warn($"[BlueprintContainerSaveLoadFix] contained-blueprint slot repair failed for {DescribeCard(card)}: {Log.ExceptionText(ex)}");
            return 0;
        }
    }

    private static int GetHybridStorageSlotCount(object card, int containedBlueprintCount)
    {
        try
        {
            var cardModel = GetMemberValue(card, "CardModel");
            if (cardModel == null) return 0;

            var inventorySlots = AccessTools.Field(cardModel.GetType(), "InventorySlots")?.GetValue(cardModel) as Array;
            if (inventorySlots == null || inventorySlots.Length == 0) return 0;

            // InGameCardBase.UpdateInventorySlots compares CardsInInventory.Count
            // against CardModel.InventorySlots.Length for legacy inventories. For
            // blueprint-container hybrids, the template count therefore represents
            // total runtime slots: hidden recipe slots + normal storage slots.
            return Math.Max(0, inventorySlots.Length - containedBlueprintCount);
        }
        catch { return 0; }
    }

    private static Array GetContainedBlueprints(object card)
    {
        var cardModelProp = card.GetType().GetProperty("CardModel", BindingFlags.Instance | BindingFlags.Public);
        var cardModel = cardModelProp?.GetValue(card, null);
        if (cardModel == null) return null;

        var containedField = AccessTools.Field(cardModel.GetType(), "ContainedBlueprintCards");
        return containedField?.GetValue(cardModel) as Array;
    }

    private static IList GetInventorySlots(object card)
    {
        var slotsField = AccessTools.Field(card.GetType(), "CardsInInventory");
        return slotsField?.GetValue(card) as IList;
    }

    private static object GetMemberValue(object owner, string name)
    {
        if (owner == null) return null;

        var type = owner.GetType();
        var prop = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop != null)
            return prop.GetValue(owner, null);

        var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return field?.GetValue(owner);
    }

    private static bool ContainsReference(Array values, object target)
    {
        if (values == null || target == null) return false;

        foreach (var value in values)
        {
            if (ReferenceEquals(value, target))
                return true;
        }

        return false;
    }

    private static void RefreshInventoryInfo(object card)
    {
        try
        {
            var visuals = GetMemberValue(card, "CardVisuals");
            var update = visuals?.GetType().GetMethod(
                "UpdateInventoryInfo",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);
            update?.Invoke(visuals, null);
        }
        catch { }
    }

    private static string DescribeBlueprint(object bp)
    {
        try
        {
            var uidField = AccessTools.Field(bp.GetType(), "UniqueID");
            return uidField?.GetValue(bp) as string ?? bp.ToString();
        }
        catch { return "<unknown>"; }
    }

    private static List<object> CollectCards(object gmInstance, Type gmType)
    {
        var result = new List<object>();
        var seen = new HashSet<object>();

        // Primary source: GameManager.AllCards
        try
        {
            var allCardsField = AccessTools.Field(gmType, "AllCards");
            if (allCardsField != null && allCardsField.GetValue(gmInstance) is IList list)
            {
                foreach (var c in list)
                {
                    if (c == null) continue;
                    if (seen.Add(c)) result.Add(c);
                }
                Log.Debug($"[BlueprintContainerSaveLoadFix] GameManager.AllCards: {list.Count} cards");
            }
            else
            {
                Log.Debug("[BlueprintContainerSaveLoadFix] GameManager.AllCards unavailable");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[BlueprintContainerSaveLoadFix] reading AllCards failed: {Log.ExceptionText(ex)}");
        }

        if (result.Count == 0)
        {
            Log.Warn("[BlueprintContainerSaveLoadFix] GameManager.AllCards returned no cards; skipped refresh to avoid a broad Resources.FindObjectsOfTypeAll scene scan");
        }

        return result;
    }

    private static object GetGameManagerInstance()
    {
        var gmType = AccessTools.TypeByName("GameManager");
        if (gmType == null) return null;

        // GameManager : MBSingleton<GameManager>. The "Instance" static property
        // lives on the base class, so we walk the inheritance chain to find it.
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;

        for (var t = gmType; t != null && t != typeof(object); t = t.BaseType)
        {
            var prop = t.GetProperty("Instance", flags);
            if (prop != null)
            {
                try
                {
                    var val = prop.GetValue(null, null);
                    // MBSingleton.Instance returns a Unity Object — `null` if no instance,
                    // and Unity's overloaded `==` treats destroyed objects as null too.
                    if (val is UnityEngine.Object uo && uo == null) continue;
                    if (val != null) return val;
                }
                catch { }
            }

            var field = t.GetField("Instance", flags);
            if (field != null)
            {
                try
                {
                    var val = field.GetValue(null);
                    if (val is UnityEngine.Object uo && uo == null) continue;
                    if (val != null) return val;
                }
                catch { }
            }
        }

        // Last-resort fallback: scan scene for the GameManager component.
        try
        {
            var found = Object.FindObjectOfType(gmType);
            if (found != null) return found;
        }
        catch { }

        return null;
    }

    private static bool IsBlueprintContainer(object card)
    {
        try
        {
            var prop = card.GetType().GetProperty("IsBlueprintContainer", BindingFlags.Instance | BindingFlags.Public);
            if (prop == null) return false;
            return (bool)prop.GetValue(card, null);
        }
        catch { return false; }
    }

    private static string DescribeCard(object card)
    {
        try
        {
            var cardModelProp = card.GetType().GetProperty("CardModel", BindingFlags.Instance | BindingFlags.Public);
            var cardModel = cardModelProp?.GetValue(card, null);
            if (cardModel == null) return "<no CardModel>";
            var uidField = AccessTools.Field(cardModel.GetType(), "UniqueID");
            var uid = uidField?.GetValue(cardModel) as string;
            return uid ?? cardModel.ToString();
        }
        catch { return "<unknown>"; }
    }
}
