namespace Utility_Belt.Patcher;

/// <summary>
/// [Equipment] Quickswap When Equipping (plan B5, R7). Vanilla refuses to equip a card whose equipment tag is
/// already at its MaxEquipped (CharacterScreen.ReasonForNotEquipping returns CannotEquipMore) and offers no swap.
/// This prefix on GraphicsManager.MoveCardToSlot, for a player-started drag or double-click equip only, first
/// moves the one equipped card that holds the full tag back to where the new card is coming from, through the
/// game's own MoveCardToSlot (which unequips it through DynamicLayoutSlot.AssignCard and RemoveSpecificCard), and
/// then lets the original equip run. The equipment lists are never edited.
/// </summary>
/// <remarks>
/// The patch target is MoveCardToSlot, a large caller, never the small ReasonForNotEquipping it consults (a small
/// callee can be inlined into an already-patched caller; CLAUDE.md "Harmony Patching Pitfalls"). Player-started
/// means _Dragged true, _User the player and _Force false: every vanilla equip path (EquipmentButton.OnDrop,
/// CardLine.OnDrop onto the equipment line, the double-click in InGameCardBase) passes exactly that, while NPC
/// and programmatic moves pass _Dragged false or another user. The swap is skipped (vanilla's refusal shows) when
/// either card is CannotBeTransferred (CLAUDE.md "Programmatic Card Movement"), when the holder is mandatory
/// equipment, locked by a user, or not the single card whose removal frees every full tag, and when the old card
/// cannot go back where the new one is leaving: from the item bar and the ground it always can, from an inventory
/// only when that container is the open one and takes the old card by weight and filter.
/// </remarks>
internal static class QuickswapEquipPatch
{
    private static readonly HashSet<string> _warned = new();
    private static bool _inSwap;

    internal static void ApplyPatch(Harmony harmony)
    {
        var move = AccessTools.Method(typeof(GraphicsManager), nameof(GraphicsManager.MoveCardToSlot));
        if (move == null)
        {
            Plugin.Logger.LogWarning("[UB] GraphicsManager.MoveCardToSlot not found; Quickswap When Equipping stays off.");
            return;
        }

        harmony.Patch(move, prefix: new HarmonyMethod(typeof(QuickswapEquipPatch), nameof(MoveCardToSlotPrefix)));
    }

    private static void MoveCardToSlotPrefix(GraphicsManager __instance, InGameCardBase _Card, SlotInfo _ToSlot, bool _Dragged, InGameNPCOrPlayer _User, bool _Force)
    {
        if (_inSwap || _ToSlot == null || _ToSlot.SlotType != SlotsTypes.Equipment || !_Dragged || _Force || !_User.Player || _User.NPC)
        {
            return;
        }

        if (!Plugin.QuickswapEquip.Value)
        {
            return;
        }

        try
        {
            TrySwap(__instance, _Card);
        }
        catch (Exception ex)
        {
            _inSwap = false;
            WarnOnce("swap:" + ex.GetType().Name, $"[UB] Quickswap failed ({ex.GetType().Name}: {ex.Message}); the game's own equip runs.");
        }
    }

    private static void TrySwap(GraphicsManager grm, InGameCardBase card)
    {
        if (grm == null || card == null || card.CardModel == null)
        {
            WarnOnce("null-args", "[UB] Quickswap: GraphicsManager or the card is null; the game's own equip runs.");
            return;
        }

        var model = card.CardModel;
        var window = grm.CharacterWindow;
        if (window == null || window.EquipmentSlotsLine == null || window.EquippedTags == null)
        {
            WarnOnce("window-null", "[UB] Quickswap: CharacterWindow, its equipment line or EquippedTags is null; the game's own equip runs.");
            return;
        }

        if (!model.IsEquipment || card.IsPinned || model.EquipmentTags == null || window.HasCardEquipped(card))
        {
            return; // not an equip that vanilla would answer with CannotEquipMore
        }

        // Which of the new card's tags are full.
        var full = new List<EquipmentTag>();
        foreach (var tag in model.EquipmentTags)
        {
            if (tag != null && tag.MaxEquipped > 0 && window.EquippedTags.TryGetValue(tag, out var count) && count >= tag.MaxEquipped)
            {
                full.Add(tag);
            }
        }

        if (full.Count == 0)
        {
            return; // vanilla allows it
        }

        // The one equipped card that holds a full tag.
        InGameCardBase holder = null;
        foreach (var slot in window.EquipmentSlotsLine.Slots)
        {
            var assigned = slot == null ? null : slot.AssignedCard;
            if (assigned == null || assigned.CardModel == null || assigned.CardModel.EquipmentTags == null || !window.HasCardEquipped(assigned))
            {
                continue;
            }

            if (full.Exists(t => Array.IndexOf(assigned.CardModel.EquipmentTags, t) >= 0))
            {
                holder = assigned;
                break;
            }
        }

        if (holder == null)
        {
            return;
        }

        if (model.CannotBeTransferred || holder.CardModel.CannotBeTransferred || holder.CardModel.IsMandatoryEquipment || holder.LockedByUser)
        {
            return; // vanilla refusal shows
        }

        // Removing the holder must free every full tag, or the equip would still be refused after we unequipped.
        foreach (var tag in full)
        {
            var left = window.EquippedTags[tag] - (Array.IndexOf(holder.CardModel.EquipmentTags, tag) >= 0 ? 1 : 0);
            if (left >= tag.MaxEquipped)
            {
                return;
            }
        }

        var source = card.CurrentSlotInfo;
        if (source == null)
        {
            return;
        }

        SlotInfo destination;
        switch (source.SlotType)
        {
            case SlotsTypes.Item:
                destination = new SlotInfo(SlotsTypes.Item, -1);
                break;
            case SlotsTypes.Base:
                destination = new SlotInfo(SlotsTypes.Base, -2);
                break;
            case SlotsTypes.Inventory:
                destination = InventoryDestination(grm, card, holder);
                if (destination == null)
                {
                    return;
                }

                break;
            default:
                return;
        }

        _inSwap = true;
        try
        {
            // The game's own move: AssignCard puts the holder in the destination and RemoveSpecificCard unequips it.
            grm.MoveCardToSlot(holder, destination, _Dragged: false, InGameNPCOrPlayer.PlayerAgent);
        }
        finally
        {
            _inSwap = false;
        }

        Plugin.Logger.LogDebug($"[UB] Quickswap: {holder.CardModel.name} out of the equipment line for {model.name} (to {destination.SlotType}).");
    }

    private static SlotInfo InventoryDestination(GraphicsManager grm, InGameCardBase card, InGameCardBase holder)
    {
        var container = card.CurrentContainer;
        if (container == null || container.CardModel == null || grm.InspectedCard != container || grm.CurrentInspectionPopup == null)
        {
            return null; // the card did not come from the open container
        }

        var cm = container.CardModel;
        if (cm.IsMiniInventory || cm.CannotPutItemsIn || cm.IsBlueprintCard || container.IsLegacyInventory || container.CurrentSavedRecipe != null)
        {
            return null;
        }

        if (!container.CanReceiveInInventory(holder.CardModel, holder.ContainedLiquidModel))
        {
            return null; // the container's filter
        }

        // Vanilla's CanHoldWeight, with the new card counted as already out.
        var net = container.InventoryWeight(true) - card.CurrentWeight(false) + holder.CurrentWeight(false);
        if (net > container.MaxWeightCapacity)
        {
            return null;
        }

        var index = container.GetIndexForInventory(0, holder.CardModel, holder.ContainedLiquidModel, holder.CurrentWeight(false));
        return index < 0 ? null : new SlotInfo(SlotsTypes.Inventory, index);
    }

    private static void WarnOnce(string cause, string message)
    {
        if (_warned.Add(cause))
        {
            Plugin.Logger.LogWarning(message);
        }
    }
}
