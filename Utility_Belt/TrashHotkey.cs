using UnityEngine;

namespace Utility_Belt;

/// <summary>
/// [Hotkeys] Trash Card (plan B8). Hover a card and press the key to trash ONE card, exactly as the
/// inspection popup's trash button does for a single card (plan Q1: one card per press).
/// </summary>
/// <remarks>
/// <para>Gate: the button's own show conditions (<c>InspectionPopup.ShowTrashButton</c>): not pinned; a Base or
/// Item card unless <c>CardData.CannotBeTrashed</c>; a Location card only if <c>CardData.CanBeTrashed</c>;
/// any other card type refused. The cheat-mode "delete anything" branch is not copied. On top of the
/// button's checks: a card <see cref="LockApi.IsLocked"/> reports locked is refused (a lock is protection),
/// and the key is ignored while typing, while an action runs and while a card is being dragged.</para>
/// <para>Action: the button's single-card call, <c>GameManager.PerformAction(card.CardModel.DefaultDiscardAction,
/// card, false, InGameNPCOrPlayer.PlayerAgent)</c>. It returns a <c>Coroutine</c> that StartCoroutine has
/// already started, so nothing more needs driving (CLAUDE.md "Runtime Card Removal"). The discard action is
/// vanilla's own (a Destroy whose DropOnDestroyList is the card's DropFromTrashButton), so trash drops pay
/// out exactly as from the button. Nothing is logged per press beyond a Debug breadcrumb.</para>
/// </remarks>
internal static class TrashHotkey
{
    private static readonly HashSet<string> _warned = new();

    internal static void Tick()
    {
        var key = Plugin.TrashCardKey.Value;
        if (key == KeyCode.None || !Input.GetKeyDown(key))
        {
            return;
        }

        if (GraphicsManager.PlayerIsTyping || GameManager.PerformingAction || GameManager.DraggedCard != null)
        {
            return;
        }

        try
        {
            Press();
        }
        catch (Exception ex)
        {
            WarnOnce("exception:" + ex.GetType().Name, $"[UB] Trash Card failed ({ex.GetType().Name}: {ex.Message}).");
        }
    }

    private static void Press()
    {
        var card = GameManager.HoveredCard;
        if (card == null || card.CardModel == null || card.Destroyed)
        {
            Plugin.Logger.LogDebug("[UB] Trash Card: no card is hovered.");
            return;
        }

        if (!CanTrash(card, out var why))
        {
            Plugin.Logger.LogDebug($"[UB] Trash Card: {card.CardModel.name} is not trashed ({why}).");
            return;
        }

        GameManager.PerformAction(card.CardModel.DefaultDiscardAction, card, false, InGameNPCOrPlayer.PlayerAgent);
        Plugin.Logger.LogDebug($"[UB] Trash Card: {card.CardModel.name} trashed.");
    }

    /// <summary>The trash button's show conditions, plus the lock.</summary>
    internal static bool CanTrash(InGameCardBase card, out string why)
    {
        if (card.IsPinned)
        {
            why = "pinned";
            return false;
        }

        if (LockApi.IsLocked(card))
        {
            why = "locked";
            return false;
        }

        var model = card.CardModel;
        switch (model.CardType)
        {
            case CardTypes.Base:
            case CardTypes.Item:
                why = model.CannotBeTrashed ? "cannot be trashed" : null;
                return !model.CannotBeTrashed;
            case CardTypes.Location:
                why = model.CanBeTrashed ? null : "a structure that cannot be trashed";
                return model.CanBeTrashed;
            default:
                why = "a " + model.CardType + " card";
                return false;
        }
    }

    private static void WarnOnce(string cause, string message)
    {
        if (_warned.Add(cause))
        {
            Plugin.Logger.LogWarning(message);
        }
    }
}
