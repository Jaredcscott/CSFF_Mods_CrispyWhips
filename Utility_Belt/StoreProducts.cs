using UnityEngine;

namespace Utility_Belt;

/// <summary>
/// [RepeatAction] Store Products In (plan C2, section 16). With the option on, hover a container and press the Pick
/// key to make it the destination; every card a repeated action produces is then moved into it as each iteration
/// finishes. Subscribes to Repeat Action's <c>OnIterationStarting</c> / <c>OnIterationDone</c> through
/// <see cref="ModuleSeams"/> only while the option is on, so with it off Repeat Action runs exactly as its standalone
/// release.
/// </summary>
/// <remarks>
/// What counts as a product. Repeat Action never reads products, so the cards the game spawns between a step's
/// <c>OnIterationStarting</c> and its <c>OnIterationDone</c> are collected from vanilla's own
/// <c>GameManager.OnCardSpawned</c> (Repeat Action raises Starting after its refuel branch, so a refuel's spawns fall
/// outside the window). A spawned card is moved only if it is still an Item in the player's place, not already inside the
/// destination, not locked in Utility Belt (LockApi), not CannotBeTransferred (the game's own click does not check it:
/// CLAUDE.md "Programmatic Card Movement"), not pinned, not a liquid and not in an NPC's hands.
///
/// How it moves. Capacity first: the destination's own <c>CanReceiveInInventoryInstance</c> and
/// <c>GetIndexForInventory</c> (weight and pile logic), so a full container is detected before anything is touched; a
/// full destination stops storing for the rest of the run, with one notice. A carried card moves through Quick
/// Transfer's <c>TryClickMoveOne</c> when the destination is the container whose popup is open (the click a player
/// would make); every other card goes through the vanilla call the game's own mini-inventory drop uses,
/// <c>InGameCardBase.DropInInventory</c> (a closed container has no popup for a click to target).
///
/// The destination is forgotten when the player's place changes, when the card is destroyed, and when the option is
/// turned off. Picking again on the destination clears it.
/// </remarks>
internal static class StoreProducts
{
    private const int MaxCollected = 200;

    private static readonly HashSet<string> _warned = new();
    private static readonly List<InGameCardBase> _collected = new();

    private static InGameCardBase _dest;
    private static string _destEnvKey;
    private static float _nextEnvCheck;

    private static bool _collecting;
    private static int _fullRun = -1;

    private static void WarnOnce(string cause, string message)
    {
        if (_warned.Add(cause))
        {
            Plugin.Logger.LogWarning("[UB] " + message);
        }
    }

    /// <summary>The destination, or null (also null once the card is destroyed). For the harness.</summary>
    internal static InGameCardBase Destination => (object)_dest != null && _dest ? _dest : null;

    /// <summary>Plugin.Update: keeps the subscription in step with the option, forgets a stale destination, reads the Pick key.</summary>
    internal static void Tick()
    {
        var on = Plugin.StoreProductsIn.Value;
        ModuleSeams.TickIteration(on);
        if (!on)
        {
            if ((object)_dest != null || _collecting)
            {
                Reset();
            }

            return;
        }

        if ((object)_dest != null)
        {
            if (!_dest || _dest.Destroyed)
            {
                Reset();
            }
            else if (Time.unscaledTime >= _nextEnvCheck)
            {
                _nextEnvCheck = Time.unscaledTime + 0.25f;
                if (CurrentEnvKey() != _destEnvKey)
                {
                    Plugin.Logger.LogDebug($"[UB] Store Products In: the place changed, so the destination ({_dest.CardModel?.name}) is forgotten.");
                    Reset();
                }
            }
        }

        var key = Plugin.StoreContainerKey.Value;
        if (key != KeyCode.None && Input.GetKeyDown(key) && !GraphicsManager.PlayerIsTyping)
        {
            try
            {
                Pick();
            }
            catch (Exception ex)
            {
                WarnOnce("pick:" + ex.GetType().Name, $"[UB] Store Products In: picking a container failed ({ex.GetType().Name}: {ex.Message}).");
            }
        }
    }

    private static void Reset()
    {
        _dest = null;
        _destEnvKey = null;
        StopCollecting();
    }

    private static string CurrentEnvKey()
    {
        var gm = GameManager.Instance;
        if (gm == null)
        {
            return null;
        }

        var env = gm.CurrentEnvironment;
        return env.IsNull ? null : env.StringDictionnaryKey;
    }

    private static void Pick()
    {
        if (GameManager.Instance == null)
        {
            return; // main menu
        }

        var hovered = GameManager.HoveredCard;
        if (hovered == null || !hovered || hovered.CardModel == null)
        {
            Plugin.Logger.LogDebug("[UB] Store Products In: no card is hovered.");
            return;
        }

        if ((object)_dest != null && ReferenceEquals(hovered, _dest))
        {
            Reset();
            Plugin.Logger.LogDebug($"[UB] Store Products In: {hovered.CardModel.name} is no longer the destination.");
            Notice(hovered, OverlayText.Get(OverlayText.StoreClearedKey, OverlayText.StoreClearedDefault));
            return;
        }

        var name = hovered.CardName(_IgnoreLiquid: true);
        if (!Eligible(hovered, out var why))
        {
            Plugin.Logger.LogDebug($"[UB] Store Products In: {hovered.CardModel.name} cannot be the destination ({why}).");
            Notice(hovered, OverlayText.Format(OverlayText.StoreBadTargetKey, OverlayText.StoreBadTargetDefault, name));
            return;
        }

        _dest = hovered;
        _destEnvKey = CurrentEnvKey();
        _nextEnvCheck = Time.unscaledTime + 0.25f;
        _fullRun = -1;
        Plugin.Logger.LogDebug($"[UB] Store Products In: destination is {hovered.CardModel.name}.");
        Notice(hovered, OverlayText.Format(OverlayText.StorePickedKey, OverlayText.StorePickedDefault, name));
    }

    /// <summary>The containers a product may be stored into: a plain inventory in the player's place (never a construction site,
    /// a cooking or other mini inventory, a fixed-slot inventory, or an NPC's).</summary>
    private static bool Eligible(InGameCardBase card, out string why)
    {
        var model = card.CardModel;
        why = null;
        if (card.Destroyed || card.InBackground || !card.CardEnvironment.MatchesPlayerEnv)
        {
            why = "not in this place";
        }
        else if (!card.IsInventoryCard)
        {
            why = "no inventory";
        }
        else if (model.CannotPutItemsIn || model.IsMiniInventory || model.CanCook || model.IsBlueprintCard || card.IsBlueprintContainer || card.IsLegacyInventory || card.IsInNPCInventory)
        {
            why = "not a plain container";
        }
        else if (model.CardType != CardTypes.Item && model.CardType != CardTypes.Base && model.CardType != CardTypes.Location)
        {
            why = "not an item, base or placed card";
        }

        return why == null;
    }

    private static void Notice(InGameCardBase near, string text)
    {
        try
        {
            var grm = MBSingleton<GraphicsManager>.Instance;
            if (grm != null && near != null && near)
            {
                grm.ShowImpossibleToInspect(near, text);
            }
        }
        catch (Exception ex)
        {
            WarnOnce("notice:" + ex.GetType().Name, $"[UB] Store Products In: showing a notice threw ({ex.GetType().Name}: {ex.Message}); the text is {text}.");
        }
    }

    // ----- Repeat Action's iteration window (bound by ModuleSeams; plain parameters, no Repeat Action type) -----

    /// <summary>OnIterationStarting(runSerial, iteration, what).</summary>
    internal static void OnIterationStarting(int runId, int iteration, string what)
    {
        if (!Plugin.StoreProductsIn.Value || (object)_dest == null)
        {
            return;
        }

        if (_fullRun != runId)
        {
            _fullRun = -1; // a new run tries again
        }

        _collected.Clear();
        if (!_collecting)
        {
            GameManager.OnCardSpawned += OnCardSpawned;
            _collecting = true;
        }
    }

    private static void OnCardSpawned(InGameCardBase card)
    {
        if (card != null && _collected.Count < MaxCollected)
        {
            _collected.Add(card);
        }
    }

    private static void StopCollecting()
    {
        if (_collecting)
        {
            GameManager.OnCardSpawned -= OnCardSpawned;
            _collecting = false;
        }

        _collected.Clear();
    }

    /// <summary>OnIterationDone(runSerial, iteration, lastStep, what): the game is idle; store what the step produced.</summary>
    internal static void OnIterationDone(int runId, int iteration, bool lastStep, string what)
    {
        if (!_collecting)
        {
            return;
        }

        var products = new List<InGameCardBase>(_collected);
        StopCollecting();
        if (!Plugin.StoreProductsIn.Value || (object)_dest == null || !_dest || _dest.Destroyed || _fullRun == runId)
        {
            return;
        }

        foreach (var card in products)
        {
            if (!Producible(card))
            {
                continue;
            }

            if (!StoreOne(card, runId, iteration))
            {
                break; // the destination is full for this run, or cannot take what is left
            }
        }
    }

    private static bool Producible(InGameCardBase card)
    {
        if (card == null || !card || card.Destroyed || card.CardModel == null || ReferenceEquals(card, _dest) || card.CurrentContainer == _dest)
        {
            return false;
        }

        var model = card.CardModel;
        if (model.CardType != CardTypes.Item || model.CannotBeTransferred || card.IsPinned || card.IsLiquid || card.LockedByUser || card.IsInNPCInventory)
        {
            return false;
        }

        if (card.InBackground || !card.CardEnvironment.MatchesPlayerEnv || card.CurrentSlotInfo == null || card.IsInsideLockedInventory)
        {
            return false;
        }

        if (LockApi.IsLocked(card))
        {
            return false;
        }

        // Never move a container into itself or into something inside it.
        for (var up = _dest.CurrentContainer; up != null && up; up = up.CurrentContainer)
        {
            if (ReferenceEquals(up, card))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Move one product into the destination. False when storing should stop for this run.</summary>
    private static bool StoreOne(InGameCardBase card, int runId, int iteration)
    {
        var dest = _dest;
        var name = card.CardModel.name;
        // Two different refusals. A container whose filter, liquid or recipe rejects this card never takes it (this card
        // only: it stays where it landed). A container with the right filter but no weight or slot left is FULL, which
        // stops storing for the rest of the run with one notice. CanReceiveInInventoryInstance folds both together, so
        // the weight test (the game's own CanHoldWeight, and GetIndexForInventory's slot answer) is read apart from it.
        var filterOk = dest.CanReceiveInInventory(card.CardModel, card.ContainedLiquidModel);
        if (filterOk && (!dest.CanHoldWeight(card) || dest.GetIndexForInventory(0, card.CardModel, card.ContainedLiquidModel, card.CurrentWeight(false)) < 0))
        {
            _fullRun = runId;
            Plugin.Logger.LogDebug($"[UB] Store Products In: iteration {iteration}: {dest.CardModel.name} is full; storing stops for this run.");
            Notice(dest, OverlayText.Format(OverlayText.StoreFullKey, OverlayText.StoreFullDefault, dest.CardName(_IgnoreLiquid: true)));
            return false;
        }

        if (!filterOk || !dest.CanReceiveInInventoryInstance(card))
        {
            Plugin.Logger.LogDebug($"[UB] Store Products In: iteration {iteration}: {name} is not accepted by {dest.CardModel.name}; left where it landed.");
            return true; // this card only
        }

        string path;
        bool moved;
        var grm = MBSingleton<GraphicsManager>.Instance;
        if (grm != null && grm.InspectedCard == dest && card.CurrentSlotInfo.SlotType == SlotsTypes.Item
            && card.CurrentSlot != null && card.CardVisuals != null && ModuleSeams.QuickTransferPresent)
        {
            path = "Quick Transfer's click replay";
            moved = ModuleSeams.TryClickMoveOne(card, out var refusal);
            if (!moved)
            {
                Plugin.Logger.LogDebug($"[UB] Store Products In: Quick Transfer did not move {name} ({refusal}); trying DropInInventory.");
                path = "DropInInventory";
                moved = DropIn(dest, card);
            }
        }
        else
        {
            path = "DropInInventory";
            moved = DropIn(dest, card);
        }

        if (moved && grm != null && grm.InspectedCard == dest && grm.CurrentInspectionPopup != null)
        {
            grm.CurrentInspectionPopup.RefreshInventory();
        }

        Plugin.Logger.LogDebug($"[UB] Store Products In: iteration {iteration}: {name} {(moved && card.CurrentContainer == dest ? "stored in" : "NOT stored in")} {dest.CardModel.name} ({path}).");
        return true;
    }

    private static bool DropIn(InGameCardBase dest, InGameCardBase card)
    {
        try
        {
            return dest.DropInInventory(card);
        }
        catch (Exception ex)
        {
            WarnOnce("drop:" + ex.GetType().Name, $"[UB] Store Products In: DropInInventory threw ({ex.GetType().Name}: {ex.Message}); the card stays where it is.");
            return false;
        }
    }
}
