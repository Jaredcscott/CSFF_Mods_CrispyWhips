// GameManager.Awake postfix that re-asserts the two blueprint research flags.
//
// `BlueprintPurchasing = true` keeps the "+" research button visible so players can unlock
// blueprints. `PurchasingWithTime = true` makes those unlocks consume `BlueprintUnlockTicksCost`
// rather than complete instantly. Both flags default to true in vanilla; this patch is here to
// reinforce them in case any other mod or future game change flips them off.
//
// Also patches GameManager.FinishInitializing as a prefix to register all mod blueprints in
// AllBlueprintModels BEFORE the game's save-state restoration loop runs. Without this, mod
// blueprints are absent from AllBlueprintModels (which the game builds from CardTabGroup.IncludedCards
// — not from AllData), so their researched/available states are lost on every load.
//
// Must use compile-time `typeof(GameManager)` (via the framework's Assembly-CSharp reference) —
// `SafePatcher` cannot find `GameManager.Awake` reliably at framework Awake time.

using System.Collections;
using HarmonyLib;

namespace CSFFModFramework.Patching;

internal static class BlueprintFlagFix
{
    public static void ApplyPatch(Harmony harmony)
    {
        try
        {
            var awakeMethod = AccessTools.Method(typeof(GameManager), "Awake");
            if (awakeMethod == null)
            {
                Util.Log.Warn("BlueprintFlagFix: GameManager.Awake not found; blueprint flags will not be reinforced.");
                return;
            }
            var postfix = new HarmonyMethod(typeof(BlueprintFlagFix), nameof(Postfix));
            harmony.Patch(awakeMethod, postfix: postfix);
        }
        catch (System.Exception ex)
        {
            Util.Log.Error($"BlueprintFlagFix: failed to patch GameManager.Awake: {FullException(ex)}");
        }

        // Patch FinishInitializing so mod blueprints are in AllBlueprintModels before save-state
        // restoration. The game builds AllBlueprintModels from CardTabGroup.IncludedCards; since
        // BlueprintInjector runs at journal-open time (not load time), mod blueprints are missing
        // from AllBlueprintModels when FinishInitializing looks up UIDs to restore states.
        try
        {
            // FinishInitializing may have overloads; grab the first one with the most parameters
            // (the one that actually loads save data), falling back to any overload.
            var methods = typeof(GameManager).GetMethods(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic);
            var finishMethod = methods
                .Where(m => m.Name == "FinishInitializing")
                .OrderByDescending(m => m.GetParameters().Length)
                .FirstOrDefault();

            if (finishMethod == null)
            {
                Util.Log.Warn("BlueprintFlagFix: GameManager.FinishInitializing not found; mod blueprint save states may not persist across loads.");
            }
            else
            {
                var prefix = new HarmonyMethod(typeof(BlueprintFlagFix), nameof(FinishInitializing_Prefix));
                harmony.Patch(finishMethod, prefix: prefix);
                Util.Log.Debug("BlueprintFlagFix: patched GameManager.FinishInitializing for mod blueprint state restoration.");
            }
        }
        catch (System.Exception ex)
        {
            Util.Log.Warn($"BlueprintFlagFix: failed to patch FinishInitializing: {FullException(ex)}");
        }
    }

    // Runs before FinishInitializing processes the loaded save's blueprint state lists.
    // Registers all mod blueprints in AllBlueprintModels so the game's normal state-restoration
    // loop finds them by UniqueID and applies their saved Available/Purchased states.
    private static void FinishInitializing_Prefix(GameManager __instance)
    {
        if (__instance == null) return;
        try
        {
            var added = RegisterModBlueprintsInAllBlueprintModels(__instance);
            if (added > 0)
                Util.Log.Info($"[BlueprintStateFix] registered {added} mod blueprint(s) in AllBlueprintModels before save-state restore");
        }
        catch (System.Exception ex)
        {
            Util.Log.Warn($"[BlueprintStateFix] FinishInitializing prefix failed: {FullException(ex)}");
        }
    }

    private static void Postfix(GameManager __instance)
    {
        if (__instance == null) return;
        try
        {
            __instance.BlueprintPurchasing = true;
            __instance.PurchasingWithTime = true;

            // Also add to AllBlueprintModels in Awake postfix as a second registration point —
            // covers the case where AllBlueprintModels is built during Awake rather than per
            // FinishInitializing call.
            RegisterModBlueprintsInAllBlueprintModels(__instance);
        }
        catch (System.Exception ex)
        {
            Util.Log.Warn($"BlueprintFlagFix: failed: {FullException(ex)}");
        }
    }

    // Adds all mod blueprints to GameManager.AllBlueprintModels so the game's blueprint state
    // lookup (which iterates AllBlueprintModels by UniqueID) can find them.
    internal static int RegisterModBlueprintsInAllBlueprintModels(GameManager instance)
    {
        var allBpModelsField = AccessTools.Field(typeof(GameManager), "AllBlueprintModels");
        if (allBpModelsField == null) return 0;
        var allBpModels = allBpModelsField.GetValue(instance) as IList;
        if (allBpModels == null) return 0;

        int added = 0;
        foreach (var uid in Loading.JsonDataLoader.AllModUniqueIds)
        {
            // Filter to blueprints only (CardType == 7) using the cached parsed JSON tree.
            if (!Loading.JsonDataLoader.ParsedJsonByUniqueId.TryGetValue(uid, out var parsed)) continue;
            if (!parsed.TryGetValue("CardType", out var ct)) continue;
            bool isBp = ct is long l && l == 7 || ct is string s && s == "7";
            if (!isBp) continue;

            var card = Data.GameRegistry.GetByUid(uid) as CardData;
            if (card == null) continue;
            if (!allBpModels.Contains(card))
            {
                allBpModels.Add(card);
                added++;
            }
        }
        return added;
    }

    private static string FullException(System.Exception ex)
        => ex.InnerException?.ToString() ?? ex.ToString();
}
