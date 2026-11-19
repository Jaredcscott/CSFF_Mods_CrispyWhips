// The one decision the fast candidate scan makes per pile card, kept free of Unity, BepInEx and the
// framework so it can be compiled and run outside the game.
//
// Development_Tools/Tests/QuickTransfer-FastScanNullVisuals.Tests.ps1 compiles THIS FILE with Windows
// PowerShell's Add-Type, whose compiler stops at C# 5. Keep it to C# 5 syntax (no "out var", no
// pattern matching, no expression-bodied members, no "?." and no string interpolation) and to the
// three using lines below, or that gate goes red on a compile error.
using System;
using System.Collections;
using System.Collections.Generic;

namespace Quick_Transfer.Patcher
{
    internal static class PileVisuals
    {
        // Reads one card's CardVisuals. False means the MEMBER could not be read at all (renamed or
        // removed by a game update); true with a null value means the member is there and holds nothing.
        internal delegate bool MemberReader(object card, out object value);

        // The usable graphics of one slot's pile, in pile order, or null when the CardVisuals member
        // itself cannot be read.
        //
        // The two "nothing came back" cases are different and must stay apart:
        //
        //   * The member reads and its VALUE is null. That is the game's ordinary state for a pile card
        //     with no graphic on screen: InGameCardBase.LateUpdate hands the card's CardGraphics back
        //     to the pool (CardVisualsManager.FreeCard, then CardVisuals = null) whenever
        //     CheckIfVisibleOnScreen is false, which it is for every pile card past index 1 and for
        //     every card of a slot that is not visible (.decomp/InGameCardBase.cs). Such a card is
        //     skipped, nothing is logged and the scan stays on: the card gets a graphic, and is picked
        //     up by a later read, once the cards above it have moved.
        //
        //   * The member cannot be read. Nothing in the pile can be answered then, so
        //     memberUnreadable runs (the caller's once-per-session warning) and null is returned, which
        //     sends the caller to the scene scan instead. An empty list here would have read as "this
        //     slot holds no candidate" and the batch would have stopped early.
        //
        // Until 2026-11-13 both cases warned "could not read InGameCardBase.CardVisuals ... a member
        // was renamed" and both skipped the card, so an ordinary pile of three printed a warning that
        // blamed a rename (harness row T2.214) and a real rename never reached the scene scan.
        //
        // isDead: true for a destroyed card, which is never read. isActiveGraphic: true when the
        // graphic is one a click can land on (the scene scan this replaces saw only active graphics).
        internal static List<object> Collect(IList pile, MemberReader readVisuals, Predicate<object> isDead,
                                             Predicate<object> isActiveGraphic, Action memberUnreadable)
        {
            var graphics = new List<object>(pile.Count);
            foreach (object card in pile)
            {
                if (card == null || isDead(card)) continue;
                object visuals;
                if (!readVisuals(card, out visuals))
                {
                    memberUnreadable();
                    return null;
                }
                if (visuals == null) continue;
                if (isActiveGraphic(visuals)) graphics.Add(visuals);
            }
            return graphics;
        }
    }
}
