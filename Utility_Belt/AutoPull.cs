using System.Reflection;

namespace Utility_Belt;

/// <summary>
/// [RepeatAction] Auto-Pull Materials (plan C1, section 15). Subscribes to Repeat Action's
/// <c>OnRequirementsUnmet</c> through <see cref="ModuleSeams"/> while the option is on. When a repeated action is refused
/// because a card it needs IN THE HAND sits in a container instead (a chest on the ground, a bag you carry), it moves one
/// such card into the carried row and asks Repeat Action to try that iteration once more. Nothing found, or the move
/// refused, and Repeat Action stops exactly as it does on its own.
/// </summary>
/// <remarks>
/// What is pulled. Measured 2026-10-12 (rig 2, report research-ub-c1-...-20261012-025253): Repeat Action on its own
/// already takes a used-up drag-drop ingredient or a consumed target from a closed chest on the board (Fibers thrown into
/// a Compost Bin, three of them from a Storage Chest: Complete 3/3), because it re-resolves each card from every card in
/// the current environment, containers included. Vanilla's own availability check counts a card in a container for every
/// RequiredCardsOnBoard condition except one marked OnlyInHand (CardAction.CardsAndTagsAreCorrect: the plain branch calls
/// CardIsOnBoard with inventories always counted; OnlyInHand calls CardIsInHand(card, false)), so an OnlyInHand card or
/// tag is the one kind of missing material a pull can supply. Vanilla's Composite Bow "Train" (a Simple Arrow in hand) is
/// that case; OnlyEquipped needs are SpareTool.cs's (C3) and are left alone here.
///
/// Where from. Every match is a live card in GameManager.AllCards that is not InBackground and whose CardEnvironment
/// MatchesPlayerEnv (CLAUDE.md "Runtime Card Spawning": AllCards also holds cards left in other places), sitting in a
/// container that is a placed structure, a base card or an item (never a construction site, a cooking or other mini inventory, an
/// inventory you cannot take items out of, or an NPC's). Skipped: cards Utility Belt has locked (LockApi), cards whose
/// model is CannotBeTransferred (the game's click path does not check it: CLAUDE.md "Programmatic Card Movement"), pinned
/// placeholders, cards held by a user, liquids, and the action's own receiving and dragged cards.
///
/// How it moves. The carried row must have room first (the game's own GraphicsManager.GetItemsListFreeIndex, which also
/// applies the carry-weight limit); otherwise nothing moves, so a full hand never swaps a carried card into the chest.
/// A card with CardVisuals (an open container's top card) goes through Quick Transfer's TryClickMoveOne, the click a
/// player would make. A card without them (inside a closed container) goes through the call that click ends in,
/// GraphicsManager.MoveCardToSlot(card, carried row, _Dragged: true, player), after the same checks SwapCard makes
/// (an Item, not pinned, not destroyed) plus the locked-inventory check (plan 2.2 R9 correction F-D).
/// </remarks>
internal static class AutoPull
{
    private static readonly HashSet<string> _warned = new();
    private static MethodInfo _itemsFreeIndex;
    private static bool _itemsFreeIndexResolved;

    private static void WarnOnce(string cause, string message)
    {
        if (_warned.Add(cause))
        {
            Plugin.Logger.LogWarning("[UB] " + message);
        }
    }

    /// <summary>Repeat Action's requirements-unmet handler (bound by <see cref="ModuleSeams"/>; the argument is Repeat Action's
    /// RequirementsUnmetArgs, read through the cached FieldInfos).</summary>
    internal static void OnRequirementsUnmet(object args)
    {
        if (args == null || !Plugin.AutoPullMaterials.Value)
        {
            return;
        }

        var site = ModuleSeams.UnmetSite.GetValue(args) as string;
        var iteration = (int)ModuleSeams.UnmetIteration.GetValue(args);
        var missingCards = ModuleSeams.UnmetMissingCards.GetValue(args) as List<CardData>;
        var missingTags = ModuleSeams.UnmetMissingTags.GetValue(args) as List<CardTag>;
        var action = ModuleSeams.UnmetAction.GetValue(args) as CardAction;
        var receiving = ModuleSeams.UnmetReceiving.GetValue(args) as InGameCardBase;
        var given = ModuleSeams.UnmetGiven.GetValue(args) as InGameCardBase;

        var cards = new List<CardData>();
        var tags = new List<CardTag>();
        if (site == "requirements")
        {
            // Only what a pull into the hand can answer: a non-inverted OnlyInHand condition.
            if (missingCards != null && action?.RequiredCardsOnBoard != null)
            {
                foreach (var c in missingCards)
                {
                    if (c != null && !cards.Contains(c) && Array.Exists(action.RequiredCardsOnBoard, r => r.TriggerCard == c && r.OnlyInHand && !r.Inverted))
                    {
                        cards.Add(c);
                    }
                }
            }

            if (missingTags != null && action?.RequiredTagsOnBoard != null)
            {
                foreach (var t in missingTags)
                {
                    if (t != null && !tags.Contains(t) && Array.Exists(action.RequiredTagsOnBoard, r => r.TriggerTag == t && r.OnlyInHand && !r.Inverted))
                    {
                        tags.Add(t);
                    }
                }
            }
        }
        else if (site == "source used up" && missingCards != null)
        {
            // Repeat Action has already looked through every container here; a match below would be a card it could not
            // reach, so this is a backstop that normally finds nothing.
            foreach (var c in missingCards)
            {
                if (c != null && !cards.Contains(c))
                {
                    cards.Add(c);
                }
            }
        }

        if (cards.Count == 0 && tags.Count == 0)
        {
            Plugin.Logger.LogDebug($"[UB] Auto-Pull: iteration {iteration} ({site}) is short of nothing a pull into the hand can supply; Repeat Action stops as usual.");
            return;
        }

        var pulled = 0;
        foreach (var model in cards)
        {
            if (Pull(c => c.CardModel == model, model.name, receiving, given, iteration))
            {
                pulled++;
            }
        }

        foreach (var tag in tags)
        {
            if (Pull(c => c.CardModel.HasTag(tag, c), tag.name, receiving, given, iteration))
            {
                pulled++;
            }
        }

        if (pulled > 0)
        {
            ModuleSeams.UnmetRetry.SetValue(args, true);
        }
    }

    /// <summary>Find one card matching <paramref name="match"/> in a container in the current environment and move it into the
    /// carried row. True when it moved.</summary>
    private static bool Pull(Predicate<InGameCardBase> match, string need, InGameCardBase receiving, InGameCardBase given, int iteration)
    {
        var gm = GameManager.Instance;
        if (gm == null || gm.AllCards == null)
        {
            WarnOnce("gm", "Auto-Pull: GameManager or its card list is not available; nothing is pulled.");
            return false;
        }

        var lockedSkipped = 0;
        var otherSkipped = 0;
        InGameCardBase found = null;
        foreach (var c in gm.AllCards)
        {
            if (c == null || c.Destroyed || c.CardModel == null || ReferenceEquals(c, receiving) || ReferenceEquals(c, given))
            {
                continue;
            }

            // The environment test, on every match: AllCards also lists cards the game keeps from other places.
            if (c.InBackground || !c.CardEnvironment.MatchesPlayerEnv || !match(c))
            {
                continue;
            }

            var container = c.CurrentContainer;
            if (container == null || container.CardModel == null)
            {
                continue; // already loose: not something a pull answers
            }

            if (LockApi.IsLocked(c))
            {
                lockedSkipped++;
                continue;
            }

            if (!Pullable(c, container))
            {
                otherSkipped++;
                continue;
            }

            found = c;
            break;
        }

        if (found == null)
        {
            Plugin.Logger.LogDebug($"[UB] Auto-Pull: iteration {iteration} needs {need} in hand; none in a container here ({lockedSkipped} locked, {otherSkipped} not movable skipped).");
            return false;
        }

        var from = found.CurrentContainer.CardModel.name;
        if (!CarriedRowHasRoom(found))
        {
            Plugin.Logger.LogDebug($"[UB] Auto-Pull: iteration {iteration} needs {need}; the carried row has no room for it, so nothing is moved.");
            return false;
        }

        string path;
        bool moved;
        // A click needs a card a player could click: one in a visible slot with its own CardVisuals. A card in a closed
        // container has no visible slot (CurrentSlot is null), whatever its pooled CardVisuals field holds.
        if (found.CurrentSlot != null && found.CardVisuals != null && ModuleSeams.QuickTransferPresent)
        {
            path = "Quick Transfer's click replay";
            moved = ModuleSeams.TryClickMoveOne(found, out var refusal);
            if (!moved)
            {
                Plugin.Logger.LogDebug($"[UB] Auto-Pull: Quick Transfer did not move {need} ({refusal}).");
            }
        }
        else
        {
            path = "MoveCardToSlot";
            moved = MoveToCarriedRow(found);
        }

        var landed = moved && found != null && found.CurrentContainer == null && found.CurrentSlotInfo != null && found.CurrentSlotInfo.SlotType == SlotsTypes.Item;
        if (landed)
        {
            Plugin.Logger.LogDebug($"[UB] Auto-Pull: iteration {iteration}: pulled {need} out of {from} into the hand ({path}); Repeat Action retries.");
        }
        else if (moved)
        {
            Plugin.Logger.LogDebug($"[UB] Auto-Pull: iteration {iteration}: {need} moved by {path} but is not in the carried row; no retry asked.");
        }

        return landed;
    }

    /// <summary>The checks SwapCard and Utility Belt make before any move, and the containers a pull never takes from.</summary>
    private static bool Pullable(InGameCardBase card, InGameCardBase container)
    {
        var model = card.CardModel;
        if (model.CardType != CardTypes.Item || model.CannotBeTransferred || card.IsPinned || card.IsLiquid || card.LockedByUser || card.IsInNPCInventory)
        {
            return false;
        }

        if (card.IsInsideLockedInventory || card.CurrentSlotInfo == null)
        {
            return false;
        }

        var cm = container.CardModel;
        if (container.Destroyed || cm.IsMiniInventory || cm.IsBlueprintCard || cm.CannotTakeItemsOut || container.IsInNPCInventory)
        {
            return false;
        }

        return cm.CardType == CardTypes.Item || cm.CardType == CardTypes.Base || cm.CardType == CardTypes.Location;
    }

    /// <summary>The game's own test for a free carried-row slot (and the carry-weight limit). False when it cannot be read.</summary>
    private static bool CarriedRowHasRoom(InGameCardBase card)
    {
        var grm = MBSingleton<GraphicsManager>.Instance;
        if (grm == null)
        {
            WarnOnce("grm", "Auto-Pull: GraphicsManager is not available; nothing is pulled.");
            return false;
        }

        if (!_itemsFreeIndexResolved)
        {
            _itemsFreeIndexResolved = true;
            _itemsFreeIndex = AccessTools.Method(typeof(GraphicsManager), "GetItemsListFreeIndex", new[] { typeof(int), typeof(CardData), typeof(CardData), typeof(float) });
        }

        if (_itemsFreeIndex == null)
        {
            WarnOnce("free-index", "Auto-Pull: GraphicsManager.GetItemsListFreeIndex(int, CardData, CardData, float) was not found (renamed?); nothing is pulled.");
            return false;
        }

        try
        {
            var index = (int)_itemsFreeIndex.Invoke(grm, new object[] { 1000, card.CardModel, card.ContainedLiquidModel, card.CurrentWeight(false) });
            return index >= 0;
        }
        catch (Exception ex)
        {
            var inner = ex.InnerException ?? ex;
            WarnOnce("free-index:" + inner.GetType().Name, $"Auto-Pull: reading the carried row's free slot threw ({inner.GetType().Name}: {inner.Message}); nothing is pulled.");
            return false;
        }
    }

    /// <summary>The call a player's right-click ends in (SwapCard), for a card the click cannot reach because its container is
    /// closed. Index 1000 is SwapCard's own.</summary>
    private static bool MoveToCarriedRow(InGameCardBase card)
    {
        var grm = MBSingleton<GraphicsManager>.Instance;
        try
        {
            grm.MoveCardToSlot(card, new SlotInfo(SlotsTypes.Item, 1000), _Dragged: true, InGameNPCOrPlayer.PlayerAgent);
            return true;
        }
        catch (Exception ex)
        {
            WarnOnce("move:" + ex.GetType().Name, $"Auto-Pull: MoveCardToSlot threw ({ex.GetType().Name}: {ex.Message}); the card stays where it is.");
            return false;
        }
    }

    /// <summary>Plugin.Update: resolves the module seams once and keeps the subscription in step with the option.</summary>
    internal static void Tick()
    {
        ModuleSeams.Tick(Plugin.AutoPullMaterials.Value);
    }
}
