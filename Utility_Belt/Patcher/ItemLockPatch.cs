namespace Utility_Belt.Patcher;

/// <summary>
/// Item lock (plan B1 step 3, and the run-start hook of step 1). A prefix on InspectionPopup.EmptyInventory,
/// vanilla's Take Out All: when the open container holds a locked card it takes out every unlocked card
/// through the same per-card move EmptyInventory itself makes, and skips the original; with no locked card
/// the original runs untouched.
/// </summary>
/// <remarks>
/// The per-card move, read from .decomp/InspectionPopup.cs EmptyInventory (EA 0.68b): for each inventory
/// slot from the last, for each card of the slot from the last,
/// <c>GrM.MoveCardToSlot(card, CurrentCard.CurrentSlotInfo.SlotType == Base ? CurrentCard.CurrentSlotInfo : new SlotInfo(Base, -2), false, PlayerAgent)</c>,
/// with EmptyingInventory set around the loop and Hide(true) after it when _Hide. The replacement copies
/// that exactly and only leaves cards out, so it moves a subset of what vanilla would: vanilla does not test
/// CardModel.CannotBeTransferred here and neither does this (CLAUDE.md "Programmatic Card Movement": no card
/// moves that the game's own button would not move).
///
/// Only the plain InspectionPopup (the card and inventory popups) honours a lock. BlueprintConstructionPopup
/// calls EmptyInventory to clear a blueprint it is switching or deconstructing, and a cooking container's
/// recipe load (LoadCustomRecipe) calls it to clear the pot; a lock must not leave a card behind in either,
/// so both run vanilla's whole loop. EmptyInventory is a loop over every slot, never a small helper, so it is
/// not a JIT inlining risk (CLAUDE.md "Harmony Patching Pitfalls"); it is reached from the button and from
/// those two internal callers.
/// </remarks>
internal static class ItemLockPatch
{
    private static int _recipeLoadDepth;
    private static readonly HashSet<string> _warned = new();

    internal static void ApplyPatch(Harmony harmony)
    {
        var empty = AccessTools.Method(typeof(InspectionPopup), nameof(InspectionPopup.EmptyInventory));
        if (empty == null)
        {
            Plugin.Logger.LogWarning("[UB] InspectionPopup.EmptyInventory not found; Take Out All ignores item locks.");
        }
        else
        {
            harmony.Patch(empty, prefix: new HarmonyMethod(typeof(ItemLockPatch), nameof(EmptyInventoryPrefix)));
        }

        var recipe = AccessTools.Method(typeof(InspectionPopup), "LoadCustomRecipe");
        if (recipe == null)
        {
            // Without the guard a recipe load would leave a locked card in the pot, so locks are not honoured
            // anywhere rather than in a way that could jam a recipe.
            Plugin.Logger.LogWarning("[UB] InspectionPopup.LoadCustomRecipe not found; Take Out All ignores item locks.");
            _recipeGuardMissing = true;
        }
        else
        {
            harmony.Patch(recipe,
                prefix: new HarmonyMethod(typeof(ItemLockPatch), nameof(RecipePrefix)),
                finalizer: new HarmonyMethod(typeof(ItemLockPatch), nameof(RecipeFinalizer)));
        }

        var awake = AccessTools.Method(typeof(GameManager), "Awake");
        if (awake == null)
        {
            Plugin.Logger.LogWarning("[UB] GameManager.Awake not found; item locks cannot tell a new game from a load and are pruned, never cleared.");
        }
        else
        {
            harmony.Patch(awake, postfix: new HarmonyMethod(typeof(ItemLockPatch), nameof(GameManagerAwakePostfix)));
        }
    }

    private static bool _recipeGuardMissing;

    private static void GameManagerAwakePostfix(GameManager __instance)
    {
        Locks.ItemLocks.OnGameManagerAwake(__instance);
    }

    private static void RecipePrefix()
    {
        _recipeLoadDepth++;
    }

    private static Exception RecipeFinalizer(Exception __exception)
    {
        if (_recipeLoadDepth > 0)
        {
            _recipeLoadDepth--;
        }

        return __exception;
    }

    private static bool EmptyInventoryPrefix(InspectionPopup __instance, bool _Hide, GraphicsManager ___GrM, ref bool ___EmptyingInventory)
    {
        try
        {
            if (_recipeGuardMissing || _recipeLoadDepth > 0 || __instance == null || __instance.GetType() != typeof(InspectionPopup))
            {
                return true;
            }

            var container = __instance.CurrentCard;
            if (container == null || container.CardsInInventory == null || container.CardsInInventory.Count == 0)
            {
                return true;
            }

            if (Locks.LockStore.Count == 0 || !HoldsLockedCard(container))
            {
                return true; // vanilla, untouched
            }

            if (___GrM == null)
            {
                WarnOnce("grm-null", "[UB] InspectionPopup.GrM is null at Take Out All; the game's own Take Out All runs and ignores item locks.");
                return true;
            }

            var target = container.CurrentSlotInfo != null && container.CurrentSlotInfo.SlotType == SlotsTypes.Base
                ? container.CurrentSlotInfo
                : null;

            ___EmptyingInventory = true;
            var kept = 0;
            try
            {
                for (var i = container.CardsInInventory.Count - 1; i >= 0; i--)
                {
                    if (i >= container.CardsInInventory.Count)
                    {
                        continue; // a move trimmed the slot list
                    }

                    var slot = container.CardsInInventory[i];
                    if (slot == null || slot.IsFree)
                    {
                        continue;
                    }

                    for (var j = slot.CardAmt - 1; j >= 0; j--)
                    {
                        if (slot.AllCards == null || j >= slot.AllCards.Count)
                        {
                            continue;
                        }

                        var card = slot.AllCards[j];
                        if (card == null || card.CardModel == null)
                        {
                            continue; // vanilla tests the slot's model; an empty entry moves nothing
                        }

                        if (LockApi.IsLocked(card))
                        {
                            kept++;
                            continue;
                        }

                        ___GrM.MoveCardToSlot(card, target ?? new SlotInfo(SlotsTypes.Base, -2), _Dragged: false, InGameNPCOrPlayer.PlayerAgent);
                    }
                }
            }
            finally
            {
                ___EmptyingInventory = false;
            }

            Plugin.Logger.LogDebug($"[UB] Take Out All: {kept} locked card(s) kept in {container.CardModel?.name}.");
            if (_Hide)
            {
                __instance.Hide(_PlaySound: true);
            }

            return false;
        }
        catch (Exception ex)
        {
            WarnOnce("prefix:" + ex.GetType().Name, $"[UB] Take Out All with item locks failed ({ex.GetType().Name}: {ex.Message}); the game's own Take Out All runs instead.");
            ___EmptyingInventory = false;
            return true;
        }
    }

    private static bool HoldsLockedCard(InGameCardBase container)
    {
        foreach (var slot in container.CardsInInventory)
        {
            if (slot == null || slot.IsFree || slot.AllCards == null)
            {
                continue;
            }

            foreach (var card in slot.AllCards)
            {
                if (LockApi.IsLocked(card))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void WarnOnce(string cause, string message)
    {
        if (_warned.Add(cause))
        {
            Plugin.Logger.LogWarning(message);
        }
    }
}
