using System;
using System.Collections.Generic;

namespace Repeat_Action.Patcher
{
    /// <summary>
    /// What a run hands an <see cref="ActionPatch.OnRequirementsUnmet"/> subscriber when an iteration cannot be dispatched for
    /// want of a card. Plain fields, so a subscriber in another assembly (Utility Belt's Auto-Pull Materials) can read and
    /// set them by reflection without a compile-time reference to Repeat Action.
    /// </summary>
    public sealed class RequirementsUnmetArgs
    {
        /// <summary>"source used up" (a drag-drop's dragged card has no live card of its kind left in this place) or
        /// "requirements" (the game's own availability check refused the action).</summary>
        public string Site;
        /// <summary>The iteration being dispatched, counting from 1 (a refuel shares its iteration's number).</summary>
        public int Iteration;
        /// <summary>The run's own label for this dispatch, as its log lines open ("iteration 3", "iteration 3: refuel 1").</summary>
        public string What;
        /// <summary>The action's name in the player's language.</summary>
        public string ActionName;
        /// <summary>The refusal as the run would report it.</summary>
        public string Reason;
        /// <summary>The action the run was about to dispatch (a CardAction, or a CardOnCardAction for a drag-drop).</summary>
        public CardAction Action;
        /// <summary>The card the action was resolved onto, or null for a card-less action.</summary>
        public InGameCardBase ReceivingCard;
        /// <summary>A drag-drop's dragged card when one was resolved, else null.</summary>
        public InGameCardBase GivenCard;
        /// <summary>The card kinds the action is short of: for "source used up" the dragged card's kind; for "requirements" the
        /// cards the game's own CardAction.CardsAndTagsAreCorrect reports failed (RequiredCardsOnBoard). Never null.</summary>
        public List<CardData> MissingCards = new List<CardData>();
        /// <summary>The tags the game's own check reports failed (RequiredTagsOnBoard). Never null.</summary>
        public List<CardTag> MissingTags = new List<CardTag>();
        /// <summary>Set by a subscriber that changed the board (moved a card into reach) and wants the dispatch tried ONE more
        /// time. The run waits, frame by frame, for the game to settle, then retries once; a second refusal stops the run as
        /// before and raises no second event.</summary>
        public bool RetryRequested;
    }

    public static partial class ActionPatch
    {
        // =====================================================================
        // RUN HOOKS (Utility Belt plan section 2.2 R9). Public, additive, and inert with nothing subscribed: every hook is
        // a static event raised only when it has a subscriber, so a Repeat Action-only install runs exactly as before.
        // A subscriber in another assembly binds by reflection (Type.GetEvent + EventInfo.AddEventHandler) through
        // Chainloader.PluginInfos["crispywhips.repeat_action"], so neither side declares a [BepInDependency].
        // OnIterationStarting and OnIterationDone (below) sit beside it. No tool-break hook exists: a worn tool that
        // breaks reaches OnRequirementsUnmet (Utility Belt's Equip Spare Tool On Break answers it there), and a dragged
        // tool's break is Use Spare Tool On Break's own case (Utility Belt plan 2.2, measured 2026-10-12).
        // =====================================================================

        /// <summary>
        /// Raised when an iteration is refused for want of a card: a drag-drop whose dragged card is used up, or an action the
        /// game's availability check refuses (most often a card that must be in the hand). Raised at most once per dispatch, and
        /// never for the retry it can ask for (<see cref="RequirementsUnmetArgs.RetryRequested"/>), so a handler that moves a card
        /// cannot set off another round of itself. Handlers run on the main thread inside the run's coroutine; one that throws
        /// is logged once per exception type and counts as asking for nothing.
        /// </summary>
        public static event Action<RequirementsUnmetArgs> OnRequirementsUnmet;

        // Filled by a refusal site (DispatchCardOnCard, ActionAvailable) only while a subscriber exists and the dispatch is
        // not itself the retry; read and cleared by DispatchAndWait after TryDispatch refuses.
        private static RequirementsUnmetArgs pendingUnmet;
        private static bool unmetSuppressed;
        private static readonly HashSet<string> unmetHandlerFailuresWarned = new HashSet<string>();

        // ---------------------------------------------------------------------
        // Iteration window (Utility Belt plan C2, section 16). Two plain-parameter events around each step's own dispatch, so a
        // subscriber can collect what that dispatch produced. Raised only with a subscriber; a handler that throws is logged
        // once per exception type and ignored. Neither carries a Repeat Action type, so a subscriber binds by reflection.
        // ---------------------------------------------------------------------

        /// <summary>
        /// Raised just before a step's own dispatch (never before a refuel, so a refuel's spawns fall outside the window).
        /// Arguments: the run's serial number (1 for the first run of the session, +1 per run; one run is one Shift+R),
        /// the iteration being started counting from 1, and the run's label for this dispatch ("iteration 3"). A dispatch the
        /// game refuses raises no matching <see cref="OnIterationDone"/>; the next start (or the next run's) closes that window.
        /// </summary>
        public static event Action<int, int, string> OnIterationStarting;

        /// <summary>
        /// Raised when a step's dispatch finished and the game is idle again, before any Stop At Quantity or tool check.
        /// Arguments: the run's serial number, the iteration this step belongs to, whether it was the iteration's last step
        /// (always true for a one-action run), and the run's label. A subscriber may move cards here: the game is idle.
        /// </summary>
        public static event Action<int, int, bool, string> OnIterationDone;

        private static int runSerial;
        private static readonly HashSet<string> iterationHandlerFailuresWarned = new HashSet<string>();

        private static void RaiseIterationStarting(int iteration, string what)
        {
            var handlers = OnIterationStarting;
            if (handlers == null) return;
            foreach (var d in handlers.GetInvocationList())
            {
                try { ((Action<int, int, string>)d)(runSerial, iteration, what); }
                catch (Exception ex) { NoteIterationHandlerFailure("iteration-starting", ex); }
            }
        }

        private static void RaiseIterationDone(int iteration, bool lastStep, string what)
        {
            var handlers = OnIterationDone;
            if (handlers == null) return;
            foreach (var d in handlers.GetInvocationList())
            {
                try { ((Action<int, int, bool, string>)d)(runSerial, iteration, lastStep, what); }
                catch (Exception ex) { NoteIterationHandlerFailure("iteration-done", ex); }
            }
        }

        private static void NoteIterationHandlerFailure(string which, Exception ex)
        {
            string type = ex.GetType().Name;
            if (iterationHandlerFailuresWarned.Add(which + ":" + type))
                Logger.LogWarning($"[Repeat] An {which} handler threw ({type}); it is ignored and the run goes on. Later {type}s go to Verbose Run Diagnostics only. {ex}");
            else
                RunLog($"{which} handler threw ({type}); ignored");
        }

        private static bool UnmetWanted => OnRequirementsUnmet != null && !unmetSuppressed;

        /// <summary>A drag-drop's dragged card is used up (DispatchCardOnCard, given == null).</summary>
        private static void NoteSourceUsedUp(Captured cap, InGameCardBase receiving)
        {
            if (!UnmetWanted) return;
            var args = new RequirementsUnmetArgs { Site = "source used up", Action = cap.Action, ReceivingCard = receiving };
            try
            {
                var model = cap.GivenCard != null && cap.GivenCard ? cap.GivenCard.CardModel : null;
                if (model == null && !string.IsNullOrEmpty(cap.GivenUid)) model = UniqueIDScriptable.GetFromID<CardData>(cap.GivenUid);
                if (model != null) args.MissingCards.Add(model);
                else RunLog($"requirements-unmet hook: the dragged card '{cap.GivenUid}' has no CardData to name");
            }
            catch (Exception ex)
            {
                RunLog($"requirements-unmet hook: reading the dragged card's kind threw ({ex.GetType().Name}: {ex.Message})");
            }
            pendingUnmet = args;
        }

        /// <summary>The game's availability check refused the action (ActionAvailable). Names what it is short of through the
        /// game's own CardsAndTagsAreCorrect, called only when a subscriber exists.</summary>
        private static void NoteRequirementsUnmet(CardAction action, InGameCardBase card, InGameCardBase given, string reason)
        {
            if (!UnmetWanted) return;
            var args = new RequirementsUnmetArgs { Site = "requirements", Action = action, ReceivingCard = card, GivenCard = given, Reason = reason };
            try
            {
                action.CardsAndTagsAreCorrect(card, args.MissingCards, args.MissingTags);
                args.MissingCards.RemoveAll(c => c == null);
                args.MissingTags.RemoveAll(t => t == null);
            }
            catch (Exception ex)
            {
                RunLog($"requirements-unmet hook: CardsAndTagsAreCorrect threw ({ex.GetType().Name}: {ex.Message}); the event carries no missing cards");
            }
            pendingUnmet = args;
        }

        /// <summary>Raise <see cref="OnRequirementsUnmet"/> for <paramref name="args"/>; true when a handler asked for the retry.</summary>
        private static bool RaiseRequirementsUnmet(RequirementsUnmetArgs args)
        {
            var handlers = OnRequirementsUnmet;
            if (handlers == null || args == null) return false;
            foreach (var d in handlers.GetInvocationList())
            {
                try { ((Action<RequirementsUnmetArgs>)d)(args); }
                catch (Exception ex)
                {
                    string type = ex.GetType().Name;
                    if (unmetHandlerFailuresWarned.Add(type))
                        Logger.LogWarning($"[Repeat] A requirements-unmet handler threw ({type}); it is ignored and the run stops as it would without it. Later {type}s go to Verbose Run Diagnostics only. {ex}");
                    else
                        RunLog($"requirements-unmet handler threw ({type}); ignored");
                }
            }
            return args.RetryRequested;
        }
    }
}
