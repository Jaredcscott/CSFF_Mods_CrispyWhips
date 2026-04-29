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
///   2. Iterate every <c>InGameCardBase</c> in the scene (via the master
///      AllCards list, with <c>FindObjectsOfTypeAll</c> fallback if AllCards
///      is empty for any reason).
///   3. For each blueprint container with stale <c>CardsInInventory</c>,
///      reflect-invoke the game's own <c>SpawnDefaultContainedBlueprints</c>
///      so behavior matches a fresh placement exactly.
///
/// Logs every step at Info so the trace is visible in <c>LogOutput.log</c>
/// without needing VerboseLogging enabled.
///
/// Pattern is generic — applies to any modded blueprint container, not
/// just OilPress.
/// </summary>
internal static class BlueprintContainerSaveLoadFix
{
    private static bool _subscribedToGmInitialized;
    private static Action _gmInitializedHandler;

    public static void Schedule()
    {
        // The boot-time call subscribes to GameManager.OnGMInitialized so the fix
        // re-fires after every save load, when GameManager exists and AllCards is
        // populated. The boot pass itself is a no-op (no save loaded).
        TrySubscribeToGameManagerInitialized();

        // Plugin.Instance is a MonoBehaviour — used to host the deferred coroutine
        // so we can yield one frame after WarpResolver before walking AllCards.
        var host = Plugin.Instance;
        if (host == null)
        {
            Log.Warn("[BlueprintContainerSaveLoadFix] Plugin.Instance unavailable — running synchronously (timing may miss late Init coroutines)");
            try { RunNow(); } catch (Exception ex) { Log.Warn($"[BlueprintContainerSaveLoadFix] sync run failed: {ex}"); }
            return;
        }
        host.StartCoroutine(DeferredRun());
    }

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
            Log.Info("[BlueprintContainerSaveLoadFix] subscribed to GameManager.OnGMInitialized");
        }
        catch (Exception ex)
        {
            Log.Warn($"[BlueprintContainerSaveLoadFix] failed to subscribe to OnGMInitialized: {ex}");
        }
    }

    private static void OnGameManagerInitialized()
    {
        Log.Info("[BlueprintContainerSaveLoadFix] GameManager.OnGMInitialized fired — scheduling save-load refresh");
        var host = Plugin.Instance;
        if (host == null)
        {
            try { RunNow(); } catch (Exception ex) { Log.Warn($"[BlueprintContainerSaveLoadFix] post-init sync run failed: {ex}"); }
            return;
        }
        host.StartCoroutine(DeferredRun());
    }

    private static IEnumerator DeferredRun()
    {
        // Yield once so any Init coroutines started during save load can complete
        // and AllCards is fully populated.
        yield return null;
        try
        {
            RunNow();
        }
        catch (Exception ex)
        {
            Log.Warn($"[BlueprintContainerSaveLoadFix] coroutine run failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }

    private static void RunNow()
    {
        Log.Info("[BlueprintContainerSaveLoadFix] starting refresh pass");

        var gmInstance = GetGameManagerInstance();
        if (gmInstance == null)
        {
            Log.Info("[BlueprintContainerSaveLoadFix] GameManager.Instance == null (likely first launch, no save loaded) → skip");
            return;
        }

        var gmType = gmInstance.GetType();

        var spawnMethod = AccessTools.Method(gmType, "SpawnDefaultContainedBlueprints");
        if (spawnMethod == null)
        {
            Log.Warn("[BlueprintContainerSaveLoadFix] GameManager.SpawnDefaultContainedBlueprints not found — fix unavailable");
            return;
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
            return;
        }

        // Resolve SetBpAvailable once — used to force BlueprintModelStates[card] = Available
        // for every contained blueprint of every placed container. The Recipes tab on a
        // placed container is gated by `BlueprintModelStates[bp] == Available` (igcb.cs
        // ShouldHideContainedBlueprints / GetSelectedContainedBlueprint, line 1861). After
        // a save load, contained blueprints can end up in the dictionary as Locked even
        // when the cards are physically present in CardsInInventory, leaving the visible
        // tab hidden.
        var setBpAvailable = AccessTools.Method(gmType, "SetBpAvailable");
        if (setBpAvailable == null)
            Log.Warn("[BlueprintContainerSaveLoadFix] GameManager.SetBpAvailable not found — Recipes-tab unlock unavailable");

        var cards = CollectCards(gmInstance, gmType);
        Log.Info($"[BlueprintContainerSaveLoadFix] enumerating {cards.Count} card(s)");

        int containers = 0;
        int candidates = 0;
        int refreshed = 0;
        int unlocked = 0;

        foreach (var card in cards)
        {
            if (card == null) continue;

            if (!IsBlueprintContainer(card)) continue;
            containers++;

            // 1) Ensure inventory slots are populated. Identity check: which expected
            //    blueprint cards are actually present? The count-only heuristic missed
            //    the case where Init sized CardsInInventory correctly but each slot's
            //    MainCard is null.
            var missing = GetMissingBlueprintCards(card);
            if (missing != null && missing.Count > 0)
            {
                candidates++;
                Log.Info($"[BlueprintContainerSaveLoadFix] {DescribeCard(card)}: {missing.Count} blueprint card(s) missing from inventory — re-spawning");

                var iter = spawnMethod.Invoke(gmInstance, new[] { card }) as IEnumerator;
                if (iter != null)
                {
                    startCoroutineMethod.Invoke(gmInstance, new object[] { iter });
                    refreshed++;
                    Log.Info($"[BlueprintContainerSaveLoadFix] re-spawned blueprints for {DescribeCard(card)}");
                }
                else
                {
                    Log.Warn($"[BlueprintContainerSaveLoadFix] SpawnDefault returned null IEnumerator for card {DescribeCard(card)}");
                }
            }

            // 2) Ensure each contained blueprint is unlocked (Available state). This is
            //    the critical fix for the Recipes-tab-missing case: cards can be in the
            //    inventory while BlueprintModelStates[card] is still Locked, which
            //    silently hides the tab.
            if (setBpAvailable != null && !ContainerStartsBlueprintsLocked(card))
            {
                unlocked += UnlockContainedBlueprints(card, gmInstance, setBpAvailable);
            }
        }

        Log.Info($"[BlueprintContainerSaveLoadFix] done: {containers} blueprint container(s) seen, {candidates} stale, {refreshed} refresh coroutines started, {unlocked} blueprint(s) marked Available");
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
                    Log.Warn($"[BlueprintContainerSaveLoadFix] SetBpAvailable threw for {DescribeBlueprint(bp)}: {ex.InnerException?.Message ?? ex.Message}");
                }
            }
            return count;
        }
        catch { return 0; }
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
                Log.Info($"[BlueprintContainerSaveLoadFix] GameManager.AllCards: {list.Count} cards");
            }
            else
            {
                Log.Info("[BlueprintContainerSaveLoadFix] GameManager.AllCards unavailable");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[BlueprintContainerSaveLoadFix] reading AllCards failed: {ex.Message}");
        }

        // Fallback: scan all InGameCardBase scene objects in case AllCards is empty
        // or a card hasn't been added to it yet.
        try
        {
            var igcbType = AccessTools.TypeByName("InGameCardBase");
            if (igcbType != null)
            {
                var found = Resources.FindObjectsOfTypeAll(igcbType);
                int added = 0;
                foreach (var obj in found)
                {
                    if (obj == null) continue;
                    // Skip prefabs (no scene) — only refresh placed instances
                    if (obj is Component comp && comp.gameObject.scene.IsValid() == false) continue;
                    if (seen.Add(obj)) { result.Add(obj); added++; }
                }
                if (added > 0)
                    Log.Info($"[BlueprintContainerSaveLoadFix] FindObjectsOfTypeAll added {added} additional card(s) not in AllCards");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[BlueprintContainerSaveLoadFix] scene scan failed: {ex.Message}");
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

    /// <summary>
    /// Returns the list of expected blueprint <c>CardData</c> entries that are NOT
    /// currently present in the container's inventory, using the same identity check
    /// (<c>HasCardInInventory</c>) the game's own <c>SpawnDefaultContainedBlueprints</c>
    /// uses internally. Returns <c>null</c> if the card cannot be inspected (no CardModel,
    /// missing reflection target, etc.). An empty list means the container is fully
    /// populated and no refresh is needed.
    ///
    /// Why this and not <c>CardsInInventory.Count &lt; ContainedBlueprintCards.Length</c>:
    /// after a save load, <c>InGameCardBase.Init</c> sizes <c>CardsInInventory</c> from
    /// the (now-populated) template, so counts match — but each <c>InventorySlot</c>
    /// can have <c>MainCard == null</c>, leaving the visible Recipes tab empty.
    /// Identity-based detection catches that case.
    /// </summary>
    private static List<object> GetMissingBlueprintCards(object card)
    {
        try
        {
            var cardType = card.GetType();

            var cardModelProp = cardType.GetProperty("CardModel", BindingFlags.Instance | BindingFlags.Public);
            var cardModel = cardModelProp?.GetValue(card, null);
            if (cardModel == null) return null;

            var containedField = AccessTools.Field(cardModel.GetType(), "ContainedBlueprintCards");
            var containedArr = containedField?.GetValue(cardModel) as Array;
            if (containedArr == null || containedArr.Length == 0) return new List<object>();

            // Resolve InGameCardBase.HasCardInInventory(CardData) once.
            // Signature: public bool HasCardInInventory(CardData _Card, List<InGameCardBase> _Results = null)
            var hasMethod = cardType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(m => m.Name == "HasCardInInventory");
            if (hasMethod == null) return null;

            var hasParams = hasMethod.GetParameters();
            var missing = new List<object>();

            foreach (var entry in containedArr)
            {
                if (entry == null) { missing.Add(null); continue; }

                object[] args;
                if (hasParams.Length == 1)      args = new[] { entry };
                else if (hasParams.Length == 2) args = new[] { entry, null };
                else return null;

                bool present;
                try { present = (bool)hasMethod.Invoke(card, args); }
                catch { present = false; }

                if (!present) missing.Add(entry);
            }

            return missing;
        }
        catch { return null; }
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
