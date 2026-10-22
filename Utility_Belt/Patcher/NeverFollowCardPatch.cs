namespace Utility_Belt.Patcher;

/// <summary>
/// [Display] Never Follow Cards. Vanilla scrolls a card row to a card whenever one is created (GameManager.AddCard
/// with _MoveView), moved (GraphicsManager.MoveCardToSlot, which Take out all items and Quick Transfer use),
/// dropped onto a row (CardSlot.OnDrop), re-slotted when its liquid changes (InGameCardBase) or released from
/// a pile (GraphicsManager.RemovePilingException). Every one of those calls GraphicsManager.MoveViewToSlot with
/// both _ForceCenter and _Pulse false. The two places that move the view because the player ASKED to see a
/// card, a bookmark (BookmarkGraphics) and Find Card (GraphicsManager.FindAndPulseCard), pass both true. So
/// this prefix skips the call only when both are false, and the row stays where the player left it.
/// </summary>
/// <remarks>
/// The idea is Never Follow Card for Card Survival: Tropical Island (Nexus mod 11), which removes the same
/// calls by IL transpilers on AddCard and MoveCardToSlot. A prefix on the one callee covers every caller,
/// including the drop, liquid and pile paths, and cannot break when a game update reshapes a caller's IL.
/// MoveViewToSlot is a large method (eight branches and a coroutine), so it is not inlined into its callers
/// (CLAUDE.md "Harmony Patching Pitfalls"). WaitBeforeMovingView calls it again with the same arguments, so a
/// call that waited for a slot refresh is judged the same way. Re-read against EA 0.69c, 2026-10-20.
/// </remarks>
internal static class NeverFollowCardPatch
{
    internal static void ApplyPatch(Harmony harmony)
    {
        var moveView = AccessTools.Method(typeof(GraphicsManager), nameof(GraphicsManager.MoveViewToSlot));
        if (moveView == null)
        {
            Plugin.Logger.LogWarning("[UB] GraphicsManager.MoveViewToSlot not found; Never Follow Cards stays off.");
            return;
        }

        harmony.Patch(moveView, prefix: new HarmonyMethod(typeof(NeverFollowCardPatch), nameof(MoveViewToSlotPrefix)));
    }

    // Parameter names match vanilla's (_ForceCenter, _Pulse): Harmony binds them by name.
    private static bool MoveViewToSlotPrefix(bool _ForceCenter, bool _Pulse)
    {
        return !Plugin.NeverFollowCards.Value || _ForceCenter || _Pulse;
    }
}
