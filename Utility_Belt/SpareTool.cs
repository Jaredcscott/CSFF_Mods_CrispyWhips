namespace Utility_Belt;

/// <summary>
/// [RepeatAction] Equip Spare Tool On Break (plan C3, section 17). Subscribes to Repeat Action's
/// <c>OnRequirementsUnmet</c> through <see cref="ModuleSeams"/> while the option is on. When a repeated action is refused
/// because a card it needs EQUIPPED is no longer worn (the worn tool broke and the game destroyed it), it equips a spare
/// that answers the same requirement through the game's own equip move and asks Repeat Action to try that iteration once
/// more. Nothing found, or the equip refused, and Repeat Action stops exactly as it does on its own.
/// </summary>
/// <remarks>
/// Why this seam. Measured 2026-10-12 (rig 2, report research-ub-c3-...-20261012-114832): Repeat Action notices a broken
/// DRAGGED tool only (Stop On Tool Break, and its own Use Spare Tool On Break carries on with another card of the dragged
/// kind). A worn tool that breaks is not noticed at all: Leather Gloves at Usage 1 broke during the first repeat of a
/// gloved Nettle Patch Harvest ("Leather Gloves broke", card destroyed), and the second repeat was refused by the game's
/// own check, "Stopped after 1/3: requirements no longer met", with Stop On Tool Break and Use Spare Tool On Break both on.
/// That refusal is the site Repeat Action already raises OnRequirementsUnmet at, with one retry per dispatch after the
/// game settles, so no new Repeat Action hook is needed.
///
/// What is a spare. The broken tool is gone by the time the next repeat is refused (the game removed it), so the spare is
/// matched by what the tool was worn FOR: a non-inverted RequiredCardsOnBoard or RequiredTagsOnBoard condition marked
/// OnlyEquipped (and not OnlyInHand, which the game reads first) whose card or tag the game reports missing. The spare is
/// that card, or an equipment card carrying that tag, so it takes the broken tool's equipment slot. Where several fit, the
/// one with the most usage left is worn.
///
/// Where from. Every match is a live card in GameManager.AllCards that is not InBackground and whose CardEnvironment
/// MatchesPlayerEnv (CLAUDE.md "Runtime Card Spawning"): loose in your hands or on the ground, or inside a container you
/// carry or that stands here (never a construction site, a cooking or other mini inventory, an inventory you cannot take
/// items out of, or an NPC's). Skipped: cards Utility Belt has locked (LockApi), cards whose model is CannotBeTransferred
/// (the game's move path does not check it: CLAUDE.md "Programmatic Card Movement"), pinned placeholders, cards held by a
/// user, cards already worn, and cards inside a locked inventory.
///
/// How it equips. GraphicsManager.MoveCardToSlot(card, Equipment, _Dragged: true, player), the call the game's own equip
/// drop makes (EquipmentButton.OnDrop) and the one Quickswap When Equipping (B5) hooks. The game's own
/// ReasonForNotEquipping is read first: when the slot is full with something else, the spare is tried only while
/// Quickswap When Equipping is on, which then swaps the worn card out as it does for a player's equip.
/// </remarks>
internal static class SpareTool
{
    private static readonly HashSet<string> _warned = new();

    private static void WarnOnce(string cause, string message)
    {
        if (_warned.Add(cause))
        {
            Plugin.Logger.LogWarning("[UB] " + message);
        }
    }

    /// <summary>Repeat Action's requirements-unmet handler for this feature (bound by <see cref="ModuleSeams"/>).</summary>
    internal static void OnRequirementsUnmet(object args)
    {
        if (args == null || !Plugin.EquipSpareToolOnBreak.Value)
        {
            return;
        }

        var site = ModuleSeams.UnmetSite.GetValue(args) as string;
        if (site != "requirements")
        {
            return; // a used-up dragged card is Repeat Action's own Use Spare Tool On Break, not this
        }

        var iteration = (int)ModuleSeams.UnmetIteration.GetValue(args);
        var missingCards = ModuleSeams.UnmetMissingCards.GetValue(args) as List<CardData>;
        var missingTags = ModuleSeams.UnmetMissingTags.GetValue(args) as List<CardTag>;
        var action = ModuleSeams.UnmetAction.GetValue(args) as CardAction;
        var receiving = ModuleSeams.UnmetReceiving.GetValue(args) as InGameCardBase;
        var given = ModuleSeams.UnmetGiven.GetValue(args) as InGameCardBase;

        // Only what wearing a card can answer: a non-inverted OnlyEquipped condition that is not also OnlyInHand.
        var cards = new List<CardData>();
        var tags = new List<CardTag>();
        if (missingCards != null && action?.RequiredCardsOnBoard != null)
        {
            foreach (var c in missingCards)
            {
                if (c != null && !cards.Contains(c) && Array.Exists(action.RequiredCardsOnBoard, r => r.TriggerCard == c && r.OnlyEquipped && !r.OnlyInHand && !r.Inverted))
                {
                    cards.Add(c);
                }
            }
        }

        if (missingTags != null && action?.RequiredTagsOnBoard != null)
        {
            foreach (var t in missingTags)
            {
                if (t != null && !tags.Contains(t) && Array.Exists(action.RequiredTagsOnBoard, r => r.TriggerTag == t && r.OnlyEquipped && !r.OnlyInHand && !r.Inverted))
                {
                    tags.Add(t);
                }
            }
        }

        if (cards.Count == 0 && tags.Count == 0)
        {
            Plugin.Logger.LogDebug($"[UB] Spare Tool: iteration {iteration} is short of nothing a worn card can supply; Repeat Action stops as usual.");
            return;
        }

        var equipped = 0;
        foreach (var model in cards)
        {
            if (Equip(c => c.CardModel == model, model.name, receiving, given, iteration))
            {
                equipped++;
            }
        }

        foreach (var tag in tags)
        {
            if (Equip(c => c.CardModel.HasTag(tag, c), tag.name, receiving, given, iteration))
            {
                equipped++;
            }
        }

        // Every worn requirement must be answered, or the retry would only be refused again.
        if (equipped == cards.Count + tags.Count)
        {
            ModuleSeams.UnmetRetry.SetValue(args, true);
        }
    }

    /// <summary>Find the best spare matching <paramref name="match"/> here and wear it. True when it is worn.</summary>
    private static bool Equip(Predicate<InGameCardBase> match, string need, InGameCardBase receiving, InGameCardBase given, int iteration)
    {
        var gm = GameManager.Instance;
        var grm = MBSingleton<GraphicsManager>.Instance;
        if (gm == null || gm.AllCards == null || grm == null)
        {
            WarnOnce("gm", "Spare Tool: GameManager, its card list or GraphicsManager is not available; nothing is equipped.");
            return false;
        }

        var window = grm.CharacterWindow;
        if (window == null)
        {
            WarnOnce("window", "Spare Tool: GraphicsManager.CharacterWindow is null; nothing is equipped.");
            return false;
        }

        var lockedSkipped = 0;
        var otherSkipped = 0;
        var fullSkipped = 0;
        InGameCardBase best = null;
        foreach (var c in gm.AllCards)
        {
            if (c == null || c.Destroyed || c.CardModel == null || ReferenceEquals(c, receiving) || ReferenceEquals(c, given))
            {
                continue;
            }

            // The environment test, on every match: AllCards also lists cards the game keeps from other places.
            if (c.InBackground || !c.CardEnvironment.MatchesPlayerEnv || !c.CardModel.IsEquipment || !match(c))
            {
                continue;
            }

            if (window.HasCardEquipped(c))
            {
                continue; // already worn: then the requirement would not be missing
            }

            if (LockApi.IsLocked(c))
            {
                lockedSkipped++;
                continue;
            }

            if (!Wearable(c))
            {
                otherSkipped++;
                continue;
            }

            if (!string.IsNullOrEmpty(window.ReasonForNotEquipping(c.CardModel, c)) && !Plugin.QuickswapEquip.Value)
            {
                fullSkipped++;
                continue;
            }

            if (best == null || c.CurrentUsagePercent > best.CurrentUsagePercent)
            {
                best = c;
            }
        }

        if (best == null)
        {
            Plugin.Logger.LogDebug($"[UB] Spare Tool: iteration {iteration} needs {need} worn; no spare here ({lockedSkipped} locked, {otherSkipped} not movable, {fullSkipped} with the slot full skipped).");
            return false;
        }

        var from = best.CurrentContainer != null && best.CurrentContainer.CardModel != null ? best.CurrentContainer.CardModel.name : (best.CurrentSlotInfo != null ? best.CurrentSlotInfo.SlotType.ToString() : "?");
        try
        {
            // The game's own equip drop (EquipmentButton.OnDrop); Quickswap When Equipping hooks this same call.
            grm.MoveCardToSlot(best, new SlotInfo(SlotsTypes.Equipment, 1000), _Dragged: true, InGameNPCOrPlayer.PlayerAgent);
        }
        catch (Exception ex)
        {
            WarnOnce("move:" + ex.GetType().Name, $"Spare Tool: MoveCardToSlot threw ({ex.GetType().Name}: {ex.Message}); the spare stays where it is.");
            return false;
        }

        if (!window.HasCardEquipped(best))
        {
            Plugin.Logger.LogDebug($"[UB] Spare Tool: iteration {iteration}: the game did not equip {best.CardModel.name} from {from}; no retry asked.");
            return false;
        }

        Plugin.Logger.LogDebug($"[UB] Spare Tool: iteration {iteration}: equipped {best.CardModel.name} from {from} for {need}; Repeat Action retries.");
        return true;
    }

    /// <summary>The checks a move by hand would pass, and the containers a spare is never taken from.</summary>
    private static bool Wearable(InGameCardBase card)
    {
        var model = card.CardModel;
        if (model.CannotBeTransferred || card.IsPinned || card.LockedByUser || card.IsInNPCInventory || card.IsInsideLockedInventory || card.CurrentSlotInfo == null)
        {
            return false;
        }

        var container = card.CurrentContainer;
        if (container == null)
        {
            // Loose: in your hands or on the ground here.
            var slot = card.CurrentSlotInfo.SlotType;
            return slot == SlotsTypes.Item || slot == SlotsTypes.Base;
        }

        var cm = container.CardModel;
        if (cm == null || container.Destroyed || cm.IsMiniInventory || cm.IsBlueprintCard || cm.CannotTakeItemsOut || container.IsInNPCInventory)
        {
            return false;
        }

        return cm.CardType == CardTypes.Item || cm.CardType == CardTypes.Base || cm.CardType == CardTypes.Location;
    }

    /// <summary>Plugin.Update: keeps the subscription in step with the option.</summary>
    internal static void Tick()
    {
        ModuleSeams.TickSpareTool(Plugin.EquipSpareToolOnBreak.Value);
    }
}
