namespace CSFFModFramework.Patching.BugFixes;

/// <summary>
/// Keeps one card's failing drag check from freezing a drag: a card that throws while the game asks
/// "what would this dragged card do to you?" now just shows no action, and the drag goes on.
///
/// <para><strong>The vanilla flaw</strong> (EA 0.69c, Nexus report 2026-10-21: Flint Knife onto a
/// fish). <c>GameManager.BeginDragItem</c> raises <c>OnBeginDragItem</c>, a multicast delegate holding
/// <c>InGameCardBase.GetPossibleActionFromPlayerDrag</c> for every card on the board. If any one card
/// throws (a null entry in its <c>CardInteractions</c>, a condition reading a null reference, any
/// mod's bad data), the invoke stops at that card and the exception leaves
/// <c>InGameDraggableCard.OnBeginDrag</c> before it sets <c>Dragged = true</c> or reparents the card.
/// The cards already checked stay highlighted or greyed out, the dragged card never moves, and
/// <c>OnEndDrag</c> does nothing (it needs <c>Dragged</c>), so <c>GameManager.DraggedCard</c> stays
/// set and the board stays dimmed until a reload. Unity logs the exception to Player.log only, not
/// BepInEx's LogOutput.log, so players report it as "nothing in the logs". Reproduced on a harness
/// rig by planting a null in a Trout's CardInteractions and dragging a Flint Knife with the real
/// pointer.</para>
///
/// <para><strong>The fix.</strong> A finalizer on <c>GetPossibleActionFromPlayerDrag</c> (no subclass
/// overrides it) swallows the exception, so the invoke reaches every other card and
/// <c>OnBeginDrag</c> finishes. The failing card is left as vanilla left it mid-check (greyed out, no
/// action), and the game's own <c>CancelPossibleAction</c> resets it when the drag ends. It logs one
/// warning per card model, naming the card and the exception, so the next report names the card
/// whose data is bad. A run with no exception pays only the finalizer's try/catch.</para>
/// </summary>
internal static class DragStartCrashGuard
{
    private const int MaxWarnedModels = 32;
    private static readonly HashSet<string> _warnedModels = new HashSet<string>();

    public static void ApplyPatch(Harmony harmony)
    {
        try
        {
            var original = AccessTools.Method(typeof(InGameCardBase), "GetPossibleActionFromPlayerDrag");
            if (original == null)
            {
                Util.Log.Warn("DragStartCrashGuard: InGameCardBase.GetPossibleActionFromPlayerDrag not found; one card with bad data can still freeze a drag with the board dimmed.");
                return;
            }
            harmony.Patch(original, finalizer: new HarmonyMethod(AccessTools.Method(typeof(DragStartCrashGuard), nameof(Finalizer))));
            Util.Log.Debug("DragStartCrashGuard: patched InGameCardBase.GetPossibleActionFromPlayerDrag.");
        }
        catch (Exception ex)
        {
            Util.Log.Warn($"DragStartCrashGuard: patch failed; one card with bad data can still freeze a drag with the board dimmed: {Util.Log.ExceptionText(ex)}");
        }
    }

    private static Exception Finalizer(Exception __exception, InGameCardBase __instance, InGameCardBase _WithCard)
    {
        if (__exception == null) return null;
        try
        {
            var model = __instance ? __instance.CardModel : null;
            string key = model ? model.UniqueID : "(no model)";
            if (_warnedModels.Count < MaxWarnedModels && _warnedModels.Add(key))
            {
                string name = model ? model.name : "(no model)";
                string dragged = _WithCard && _WithCard.CardModel ? _WithCard.CardModel.name : "(none)";
                Util.Log.Warn($"DragStartCrashGuard: card '{name}' ({key}) threw while checking what dragged '{dragged}' could do to it; it shows no action for this drag and the drag goes on. Its data is the likely fault. Logged once per card. {Util.Log.ExceptionText(__exception)}");
            }
        }
        catch
        {
            // The log line is a courtesy; the swallow below is the fix.
        }
        return null;
    }
}
