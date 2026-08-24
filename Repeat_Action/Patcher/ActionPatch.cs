using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using BepInEx.Logging;
using UnityEngine;

namespace Repeat_Action.Patcher
{
    /// <summary>
    /// v2 native-dispatch architecture (design: Documentation/Design/RepeatAction_v2_Replay_Architecture.md).
    ///
    /// Capture: Harmony prefixes on the PUBLIC STATIC GameManager dispatch funnels the game UI
    /// itself calls — PerformAction, PerformStackAction (both overloads), PerformCardOnCardAction,
    /// PerformGroupInventoryAction. Player clicks are isolated by _User.Player + !_FastMode:
    /// system dispatches (durability triggers, evaporation, trading, objectives) use
    /// InGameNPCOrPlayer.Null or _FastMode:true. Runtime-built actions that are not card buttons
    /// (dialog answers, discard) are excluded by requiring DismantleCardAction.
    ///
    /// Replay: re-resolve the receiving card by UniqueID, re-resolve the action from the LIVE
    /// card's DismantleActions (reference identity, then ActionName.LocalizationKey, then
    /// DefaultText), validate the same way the UI enables buttons (CollectActionModifiers →
    /// SimpleConditionsCheck), then call the same public funnel and yield on the returned
    /// Coroutine until the action fully completes. No popup simulation, no button indices,
    /// no post-hoc success heuristics.
    /// </summary>
    public static class ActionPatch
    {
        private static ManualLogSource Logger => Plugin.Logger;

        private enum FunnelKind { Action, StackAction, CardOnCard, GroupAction }

        private sealed class Captured
        {
            public FunnelKind Kind;
            public CardAction Action;                    // model-owned object captured at click time
            public string ActionKey;                     // ActionName.LocalizationKey
            public string ActionName;                    // ActionName.DefaultText
            public InGameCardBase ReceivingCard;
            public string ReceivingUid;
            public InGameCardBase GivenCard;             // CardOnCard only
            public string GivenUid;
            public bool IsLiquidTransfer;                // CardOnCard only
            public bool StackLiquids;                    // StackAction only
            public List<InGameCardBase> GroupCards;      // GroupAction only
            public List<DismantleCardAction> GroupActions;
            public List<string> GroupUids;
        }

        // Actions never captured: auto-advancing story/event popups is unrecoverable.
        private static readonly string[] BlockedActionKeywords = { "continue" };

        private static Captured last;
        private static string lastRejectedName;          // for the "'X' is not supported" toast
        private static bool isRepeating;
        private static bool cancelRequested;
        private static int groupCaptureFrame = -1;       // PerformGroupInventoryAction loops PerformAction internally

        public static bool HasLastAction => last != null;
        public static string LastActionName => last?.ActionName ?? lastRejectedName ?? "Unknown";
        public static bool IsRepeating => isRepeating;

        public static void CancelRepeat() => cancelRequested = true;

        // =====================================================================
        // PATCH APPLICATION
        // =====================================================================
        public static void ApplyPatch(Harmony harmony)
        {
            try
            {
                var gm = typeof(GameManager);
                harmony.Patch(
                    AccessTools.Method(gm, nameof(GameManager.PerformAction)),
                    prefix: new HarmonyMethod(typeof(ActionPatch), nameof(PerformAction_Prefix)));
                harmony.Patch(
                    AccessTools.Method(gm, nameof(GameManager.PerformStackAction),
                        new[] { typeof(CardAction), typeof(DynamicLayoutSlot), typeof(bool), typeof(InGameNPCOrPlayer) }),
                    prefix: new HarmonyMethod(typeof(ActionPatch), nameof(PerformStackActionLayout_Prefix)));
                harmony.Patch(
                    AccessTools.Method(gm, nameof(GameManager.PerformStackAction),
                        new[] { typeof(CardAction), typeof(InventorySlot), typeof(bool), typeof(InGameNPCOrPlayer) }),
                    prefix: new HarmonyMethod(typeof(ActionPatch), nameof(PerformStackActionInventory_Prefix)));
                // NOTE: patch the STACK variant — every drag (including the single-card
                // PerformCardOnCardAction wrapper) funnels through PerformCardOnCardActionStack.
                harmony.Patch(
                    AccessTools.Method(gm, nameof(GameManager.PerformCardOnCardActionStack)),
                    prefix: new HarmonyMethod(typeof(ActionPatch), nameof(PerformCardOnCardActionStack_Prefix)));
                harmony.Patch(
                    AccessTools.Method(gm, nameof(GameManager.PerformGroupInventoryAction)),
                    prefix: new HarmonyMethod(typeof(ActionPatch), nameof(PerformGroupInventoryAction_Prefix)));

                Logger.LogDebug("ActionPatch v2 applied — native-dispatch capture on 5 GameManager funnels");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to apply patches: {ex}");
            }
        }

        // =====================================================================
        // CAPTURE — prefixes on the public static dispatch funnels
        // =====================================================================

        private static bool ShouldCapture(InGameNPCOrPlayer user, bool fastMode)
        {
            if (isRepeating) return false;
            if (fastMode) return false;     // system dispatches (evaporation, trading, blueprint auto-return)
            if (!user.Player) return false; // excludes NPCs and InGameNPCOrPlayer.Null (durability triggers)
            // Encounter and dialog flows dispatch real DismantleCardActions as the player
            // (Encounter_Finished, chase travel, dialog answers) — capturing those would
            // overwrite the player's real last action with an unreplayable one-off.
            return !InEncounterOrDialog();
        }

        private static bool InEncounterOrDialog()
        {
            try
            {
                var enc = MBSingleton<EncounterPopup>.Instance;
                if (enc != null && enc && enc.gameObject.activeInHierarchy) return true;
                var dlg = MBSingleton<DialogsPopup>.Instance;
                if (dlg != null && dlg && dlg.gameObject.activeInHierarchy) return true;
            }
            catch (Exception ex) { Logger.LogDebug($"[Capture] InEncounterOrDialog check failed: {ex}"); }
            return false;
        }

        static void PerformAction_Prefix(CardAction _Action, InGameCardBase _ReceivingCard, bool _FastMode, InGameNPCOrPlayer _User)
        {
            try
            {
                if (!ShouldCapture(_User, _FastMode)) return;
                if (Time.frameCount == groupCaptureFrame) return; // group funnel already captured this click
                if (!(_Action is DismantleCardAction))
                {
                    // Runtime-built one-offs (dialog answers, discard, finish-game) — not replayable.
                    if (last == null) lastRejectedName = _Action != null ? _Action.ActionName.DefaultText : null;
                    return;
                }
                CaptureAction(FunnelKind.Action, _Action, _ReceivingCard, null, false);
            }
            catch (Exception ex) { Logger.LogError($"[Capture] PerformAction error: {ex}"); }
        }

        static void PerformStackActionLayout_Prefix(CardAction _Action, DynamicLayoutSlot _Slot, bool _Liquids, InGameNPCOrPlayer _User)
        {
            try
            {
                if (!ShouldCapture(_User, false)) return;
                if (!(_Action is DismantleCardAction)) return;
                var pile = _Slot != null ? _Slot.GetCardPile(_Liquids, _IncludeContainers: false, _ForceBaseRow: true) : null;
                CaptureAction(FunnelKind.StackAction, _Action, FirstLive(pile), null, _Liquids);
            }
            catch (Exception ex) { Logger.LogError($"[Capture] PerformStackAction(layout) error: {ex}"); }
        }

        static void PerformStackActionInventory_Prefix(CardAction _Action, InventorySlot _Slot, bool _Liquids, InGameNPCOrPlayer _User)
        {
            try
            {
                if (!ShouldCapture(_User, false)) return;
                if (!(_Action is DismantleCardAction)) return;
                var cards = _Slot != null ? _Slot.GetCards(_Liquids) : null;
                CaptureAction(FunnelKind.StackAction, _Action, FirstLive(cards), null, _Liquids);
            }
            catch (Exception ex) { Logger.LogError($"[Capture] PerformStackAction(inventory) error: {ex}"); }
        }

        static void PerformCardOnCardActionStack_Prefix(CardOnCardAction _Action, List<InGameCardBase> _GivenCards, InGameCardBase _ReceivingCard, bool _IsLiquidTransferAction, InGameNPCOrPlayer _User)
        {
            try
            {
                if (!ShouldCapture(_User, false)) return;
                if (_Action == null) return;
                CaptureAction(FunnelKind.CardOnCard, _Action, _ReceivingCard, FirstLive(_GivenCards), false);
                if (last != null && last.Kind == FunnelKind.CardOnCard && ReferenceEquals(last.Action, _Action))
                    last.IsLiquidTransfer = _IsLiquidTransferAction;
            }
            catch (Exception ex) { Logger.LogError($"[Capture] PerformCardOnCardActionStack error: {ex}"); }
        }

        static void PerformGroupInventoryAction_Prefix(List<InGameCardBase> _Cards, List<DismantleCardAction> _Actions, bool _FastMode, InGameNPCOrPlayer _User)
        {
            try
            {
                if (!ShouldCapture(_User, _FastMode)) return;
                if (_Actions == null || _Actions.Count == 0 || _Cards == null || _Cards.Count == 0) return;

                groupCaptureFrame = Time.frameCount;

                string name = _Actions[0].ActionName.DefaultText;
                if (IsBlockedAction(name)) { if (last == null) lastRejectedName = name; return; }
                if (IsRestLike(name) && last != null && !IsRestLike(last.ActionName)) return;

                var uids = new List<string>(_Cards.Count);
                for (int i = 0; i < _Cards.Count; i++) uids.Add(UidOf(_Cards[i]));

                last = new Captured
                {
                    Kind = FunnelKind.GroupAction,
                    Action = _Actions[0],
                    ActionKey = _Actions[0].ActionName.LocalizationKey,
                    ActionName = name,
                    ReceivingCard = _Cards[0],
                    ReceivingUid = uids[0],
                    GroupCards = new List<InGameCardBase>(_Cards),
                    GroupActions = new List<DismantleCardAction>(_Actions),
                    GroupUids = uids,
                };
                lastRejectedName = null;
                Logger.LogDebug($"[Capture] GroupAction: '{name}' on {_Cards.Count} card(s)");
            }
            catch (Exception ex) { Logger.LogError($"[Capture] PerformGroupInventoryAction error: {ex}"); }
        }

        private static void CaptureAction(FunnelKind kind, CardAction action, InGameCardBase receiving, InGameCardBase given, bool stackLiquids)
        {
            string name = action.ActionName.DefaultText;
            if (IsBlockedAction(name)) { if (last == null) lastRejectedName = name; return; }

            // A rest/relax between iterations must not overwrite the primary action (chop, mine, ...)
            // so Shift+R resumes the real work, not the rest.
            if (IsRestLike(name) && last != null && !IsRestLike(last.ActionName))
            {
                Logger.LogDebug($"[Capture] Skipping rest-like '{name}' — preserving '{last.ActionName}'");
                return;
            }

            last = new Captured
            {
                Kind = kind,
                Action = action,
                ActionKey = action.ActionName.LocalizationKey,
                ActionName = name,
                ReceivingCard = receiving,
                ReceivingUid = UidOf(receiving),
                GivenCard = given,
                GivenUid = UidOf(given),
                StackLiquids = stackLiquids,
            };
            lastRejectedName = null;
            Logger.LogDebug($"[Capture] {kind}: '{name}' on '{CardName(receiving)}'");
        }

        // =====================================================================
        // REPLAY
        // =====================================================================

        public static IEnumerator RepeatLastAction(int count)
        {
            var cap = last;
            if (cap == null)
            {
                Plugin.ShowNotification(string.IsNullOrEmpty(lastRejectedName)
                    ? "No action to repeat"
                    : $"'{lastRejectedName}' is not supported");
                yield break;
            }
            if (GameManager.Instance == null)
            {
                Plugin.ShowNotification("Game not ready");
                yield break;
            }

            isRepeating = true;
            cancelRequested = false;
            int completed = 0;
            string display = string.IsNullOrEmpty(cap.ActionName) ? "action" : cap.ActionName;
            string stopReason = null;

            try
            {
                Plugin.ShowNotification($"Repeating: {display} x{count}");
                Logger.LogInfo($"[Repeat] Starting: '{display}' x{count} ({cap.Kind})");

                for (int i = 0; i < count; i++)
                {
                    if (cancelRequested) { stopReason = "cancelled"; break; }

                    // Wait for the game to be idle. Frame yields + unscaled time — never
                    // WaitForSeconds (stalls at timeScale=0, CLAUDE.md §Coroutine Timing).
                    float waited = 0f;
                    while (!IsGameIdle() && waited < Plugin.ActionCompletionTimeout.Value && !cancelRequested)
                    {
                        yield return null;
                        waited += Time.unscaledDeltaTime;
                    }
                    if (cancelRequested) { stopReason = "cancelled"; break; }
                    if (!IsGameIdle()) { stopReason = "timed out waiting for the game to settle"; break; }
                    yield return null;

                    if (Plugin.StopOnLowStats.Value && GameManager.Instance.AnyActionBlockers)
                    {
                        stopReason = "event triggered";
                        break;
                    }
                    string statStop = CheckStatThresholds();
                    if (statStop != null) { stopReason = statStop; break; }

                    if (!TryDispatch(cap, completed, out Coroutine running, out string failReason))
                    {
                        stopReason = failReason;
                        break;
                    }

                    // Wait for the dispatched action to fully complete. Poll game state instead
                    // of yielding on the Coroutine handle: if the run ends mid-action (quit to
                    // menu destroys GameManager), a yielded handle never resumes and isRepeating
                    // would stay stuck for the rest of the app session.
                    // Phase 1: stack/drag routines flip to PLAYINGCARD one frame late, so give the
                    // routine a grace window to leave idle before trusting "idle = done".
                    bool gameEnded = false;
                    float grace = 0f;
                    while (IsGameIdle() && grace < 1f && !cancelRequested)
                    {
                        yield return null;
                        if (GameManager.Instance == null) { gameEnded = true; break; }
                        grace += Time.unscaledDeltaTime;
                    }
                    // Phase 2: wait for the action (and any confirm dialog) to finish.
                    while (!gameEnded && !IsGameIdle() && !cancelRequested)
                    {
                        yield return null;
                        if (GameManager.Instance == null) gameEnded = true;
                    }
                    if (gameEnded) { stopReason = "game ended"; break; }
                    if (cancelRequested) { stopReason = "cancelled"; break; }

                    completed++;
                    if (count > 1) Plugin.ShowNotification($"{display}: {completed}/{count}");

                    if (cap.Kind == FunnelKind.CardOnCard && Plugin.StopOnToolBreak.Value && GivenCardTransformed(cap))
                    {
                        stopReason = "tool changed";
                        break;
                    }
                }
            }
            finally
            {
                isRepeating = false;
            }

            if (stopReason == null)
            {
                Plugin.ShowNotification($"Complete: {completed}/{count}");
                Logger.LogInfo($"[Repeat] Complete: {completed}/{count}");
            }
            else
            {
                Plugin.ShowNotification($"Stopped - {stopReason} ({completed}/{count})");
                Logger.LogInfo($"[Repeat] Stopped after {completed}/{count}: {stopReason}");
            }
        }

        private static bool IsGameIdle()
        {
            if (GameManager.Instance == null) return false;
            if (GameManager.CurrentState != GameStates.SELECT || GameManager.PerformingAction) return false;
            // An open confirm dialog (ConfirmPopup actions) holds SELECT while awaiting input.
            try
            {
                var g = MBSingleton<GraphicsManager>.Instance;
                if (g != null && g && g.ConfirmActionPopup != null && g.ConfirmActionPopup.activeInHierarchy) return false;
            }
            catch (Exception ex) { Logger.LogDebug($"[Repeat] IsGameIdle confirm-popup check failed: {ex}"); }
            return true;
        }

        // =====================================================================
        // DISPATCH — re-resolve, validate, call the same public funnel
        // =====================================================================

        private static bool TryDispatch(Captured cap, int completed, out Coroutine running, out string failReason)
        {
            running = null;
            failReason = null;
            try
            {
                switch (cap.Kind)
                {
                    case FunnelKind.Action: return DispatchAction(cap, completed, ref running, ref failReason);
                    case FunnelKind.StackAction: return DispatchStack(cap, completed, ref running, ref failReason);
                    case FunnelKind.CardOnCard: return DispatchCardOnCard(cap, completed, ref running, ref failReason);
                    case FunnelKind.GroupAction: return DispatchGroup(cap, completed, ref running, ref failReason);
                }
                failReason = "unknown action kind";
                return false;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[Repeat] Dispatch error: {ex.InnerException?.ToString() ?? ex.ToString()}");
                failReason = "internal error (see log)";
                return false;
            }
        }

        private static bool DispatchAction(Captured cap, int completed, ref Coroutine running, ref string failReason)
        {
            bool cardless = cap.ReceivingCard == null && string.IsNullOrEmpty(cap.ReceivingUid);
            InGameCardBase card = null;
            CardAction action;

            if (cardless)
            {
                // Action-set / stat-popup actions (Rest, time skip) dispatch with no receiving card.
                action = cap.Action;
            }
            else
            {
                card = ResolveCard(cap.ReceivingCard, cap.ReceivingUid);
                if (card != null)
                {
                    action = ResolveActionOnCard(card, cap);
                    if (action == null && cap.Action != null)
                    {
                        // Popup-owned runtime action (e.g. blueprint Build) — not in DismantleActions
                        // but still dispatchable against the live card.
                        action = cap.Action;
                        Logger.LogDebug($"[Repeat] Using captured action object directly for '{cap.ActionName}'");
                    }
                }
                else
                {
                    // Receiving card left the board (travel, consumed target): find the same action
                    // on any live card — e.g. the direction action on the NEW location card.
                    if (FindActionAnywhere(cap, out card, out action))
                        Logger.LogDebug($"[Repeat] Target changed — found '{cap.ActionName}' on '{CardName(card)}'");
                }

                if (card == null || action == null)
                {
                    failReason = completed > 0 ? $"no more '{cap.ActionName}' targets" : "target card not found";
                    return false;
                }
                cap.ReceivingCard = card; // keep instance affinity for the next iteration
            }

            if (!ActionAvailable(action, card, null, ref failReason)) return false;

            running = GameManager.PerformAction(action, card, _FastMode: false, InGameNPCOrPlayer.PlayerAgent);
            if (running == null) { failReason = "the game rejected the action"; return false; }
            return true;
        }

        private static bool DispatchStack(Captured cap, int completed, ref Coroutine running, ref string failReason)
        {
            var card = ResolveCard(cap.ReceivingCard, cap.ReceivingUid);
            if (card == null)
            {
                failReason = completed > 0 ? "stack used up" : "target card not found";
                return false;
            }
            cap.ReceivingCard = card;
            var action = ResolveActionOnCard(card, cap) ?? cap.Action;
            if (action == null) { failReason = "action no longer available"; return false; }

            if (!ActionAvailable(action, card, null, ref failReason)) return false;

            // True stack semantics when the card still sits in a board slot; otherwise degrade
            // to one card per iteration.
            var slot = card.CurrentSlot;
            running = slot != null
                ? GameManager.PerformStackAction(action, slot, cap.StackLiquids, InGameNPCOrPlayer.PlayerAgent)
                : GameManager.PerformAction(action, card, _FastMode: false, InGameNPCOrPlayer.PlayerAgent);
            if (running == null) { failReason = "the game rejected the action"; return false; }
            return true;
        }

        private static bool DispatchCardOnCard(Captured cap, int completed, ref Coroutine running, ref string failReason)
        {
            var receiving = ResolveCard(cap.ReceivingCard, cap.ReceivingUid);
            if (receiving == null)
            {
                failReason = completed > 0 ? "no more targets" : "target card not found";
                return false;
            }
            var given = ResolveCard(cap.GivenCard, cap.GivenUid, receiving);
            if (given == null)
            {
                failReason = completed > 0 ? "source used up" : "source card not found";
                return false;
            }
            var action = cap.Action as CardOnCardAction;
            if (action == null) { failReason = "action no longer available"; return false; }

            if (!ActionAvailable(action, receiving, given, ref failReason)) return false;

            // Track the instances actually used so tool-transform detection and the next
            // iteration's resolution follow the live cards, not the frame-0 originals.
            cap.ReceivingCard = receiving;
            cap.GivenCard = given;

            running = GameManager.PerformCardOnCardActionStack(action, new List<InGameCardBase> { given },
                receiving, cap.IsLiquidTransfer, InGameNPCOrPlayer.PlayerAgent);
            if (running == null) { failReason = "the game rejected the action"; return false; }
            return true;
        }

        private static bool DispatchGroup(Captured cap, int completed, ref Coroutine running, ref string failReason)
        {
            var cards = new List<InGameCardBase>();
            var actions = new List<DismantleCardAction>();
            for (int i = 0; i < cap.GroupCards.Count; i++)
            {
                var c = ResolveCard(cap.GroupCards[i], i < cap.GroupUids.Count ? cap.GroupUids[i] : null, null, cards);
                if (c == null || cards.Contains(c)) continue;
                cards.Add(c);
                actions.Add(cap.GroupActions[i]);
            }
            if (cards.Count == 0)
            {
                failReason = completed > 0 ? "no more targets" : "target cards not found";
                return false;
            }

            if (!ActionAvailable(actions[0], cards[0], null, ref failReason)) return false;

            running = GameManager.PerformGroupInventoryAction(cards, actions, _FastMode: false, InGameNPCOrPlayer.PlayerAgent);
            if (running == null) { failReason = "the game rejected the action"; return false; }
            return true;
        }

        /// <summary>
        /// The same availability test the UI runs to enable a button: refresh the action's
        /// modifier state against the live cards FIRST (SimpleConditionsCheck reads fields
        /// only CollectActionModifiers maintains — stale state was the 1.x "Action
        /// unavailable" bug), then check. Fails open: the game re-validates in ActionRoutine.
        /// </summary>
        private static bool ActionAvailable(CardAction action, InGameCardBase card, InGameCardBase given, ref string failReason)
        {
            try
            {
                action.CollectActionModifiers(card, given, null);
                if (!action.SimpleConditionsCheck(card, InGameNPCOrPlayer.PlayerAgent))
                {
                    string msg = action.ActionBlockedMessage;
                    failReason = string.IsNullOrEmpty(msg) ? "requirements no longer met" : msg;
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"[Repeat] Availability check threw ({ex.GetType().Name}) — dispatching anyway");
                return true;
            }
        }

        // =====================================================================
        // CARD / ACTION RESOLUTION
        // =====================================================================

        private static bool IsLive(InGameCardBase c) => c != null && c && !c.Destroyed && c.CardModel != null;

        private static string UidOf(InGameCardBase c)
        {
            if (c == null || !c) return null;
            var m = c.CardModel;
            return m != null ? m.UniqueID : null;
        }

        private static InGameCardBase FirstLive(List<InGameCardBase> cards)
        {
            if (cards == null) return null;
            for (int i = 0; i < cards.Count; i++)
                if (IsLive(cards[i])) return cards[i];
            return null;
        }

        /// <summary>
        /// Resolve a replay target from the CURRENT environment's card list. Always scans
        /// AllCards (env-scoped; cleared on environment change) rather than trusting the
        /// captured reference — a stale ref can stay Unity-alive after travel and would
        /// otherwise replay onto a card that is no longer in play. Prefers the exact
        /// instance the player used; falls back to another live card of the same UniqueID.
        /// </summary>
        private static InGameCardBase ResolveCard(InGameCardBase original, string uid, InGameCardBase exclude = null, List<InGameCardBase> excludeList = null)
        {
            var gm = GameManager.Instance;
            if (gm == null || gm.AllCards == null) return null;
            InGameCardBase fallback = null;
            for (int i = 0; i < gm.AllCards.Count; i++)
            {
                var c = gm.AllCards[i];
                var hit = MatchCard(c, original, uid, exclude, excludeList);
                if (hit == null && c != null && c)
                    hit = MatchCard(c.ContainedLiquid, original, uid, exclude, excludeList);
                if (hit == null) continue;
                if (ReferenceEquals(hit, original)) return hit; // exact instance the player used
                if (fallback == null) fallback = hit;
            }
            return fallback;
        }

        private static InGameCardBase MatchCard(InGameCardBase c, InGameCardBase original, string uid, InGameCardBase exclude, List<InGameCardBase> excludeList)
        {
            if (!IsLive(c) || ReferenceEquals(c, exclude)) return null;
            if (excludeList != null && excludeList.Contains(c)) return null;
            if (!string.IsNullOrEmpty(uid))
                return string.Equals(c.CardModel.UniqueID, uid, StringComparison.Ordinal) ? c : null;
            return ReferenceEquals(c, original) ? c : null;
        }

        private static CardAction ResolveActionOnCard(InGameCardBase card, Captured cap)
            => FindInActions(card.DismantleActions, cap);

        private static CardAction FindInActions(DismantleCardAction[] actions, Captured cap)
        {
            if (actions == null) return null;

            // Same-UID cards share the same CardData model, so reference identity is exact.
            for (int i = 0; i < actions.Length; i++)
                if (ReferenceEquals(actions[i], cap.Action)) return actions[i];

            if (!string.IsNullOrEmpty(cap.ActionKey) && cap.ActionKey != LocalizedString.IgnoreKey)
            {
                for (int i = 0; i < actions.Length; i++)
                    if (actions[i] != null && string.Equals(actions[i].ActionName.LocalizationKey, cap.ActionKey, StringComparison.Ordinal))
                        return actions[i];
            }

            if (!string.IsNullOrEmpty(cap.ActionName))
            {
                for (int i = 0; i < actions.Length; i++)
                    if (actions[i] != null && string.Equals(actions[i].ActionName.DefaultText, cap.ActionName, StringComparison.OrdinalIgnoreCase))
                        return actions[i];
            }
            return null;
        }

        /// <summary>
        /// Fallback when the captured receiving card no longer exists anywhere: find the same
        /// action (by identity/key/name) on any live card. Covers travel (direction action on
        /// the new location card) and same-kind respawned targets.
        /// </summary>
        private static bool FindActionAnywhere(Captured cap, out InGameCardBase card, out CardAction action)
        {
            card = null;
            action = null;
            if (string.IsNullOrEmpty(cap.ActionKey) && string.IsNullOrEmpty(cap.ActionName) && cap.Action == null)
                return false;
            var gm = GameManager.Instance;
            if (gm == null || gm.AllCards == null) return false;
            for (int i = 0; i < gm.AllCards.Count; i++)
            {
                var c = gm.AllCards[i];
                if (!IsLive(c)) continue;
                var a = FindInActions(c.DismantleActions, cap);
                if (a != null)
                {
                    card = c;
                    action = a;
                    return true;
                }
            }
            return false;
        }

        // =====================================================================
        // SAFETY STOPS
        // =====================================================================

        // Vanilla stat GUIDs (Documentation/CSFF_Reference.md).
        private const string SatiationGuid = "930cf914322e9f145af1315d96f85a28";
        private const string HydrationGuid = "95ca7c21ffad5e647acc3d9cb5bfcde6";
        private const string StaminaGuid = "1cfd30cf13b69b949a0ac521f55a59a2";

        private static string CheckStatThresholds()
        {
            return CheckThreshold("Satiation", SatiationGuid, Plugin.SatiationStopThreshold.Value)
                ?? CheckThreshold("Hydration", HydrationGuid, Plugin.HydrationStopThreshold.Value)
                ?? CheckThreshold("Stamina", StaminaGuid, Plugin.StaminaStopThreshold.Value);
        }

        private static string CheckThreshold(string label, string guid, int threshold)
        {
            if (threshold <= 0) return null;
            try
            {
                var model = UniqueIDScriptable.GetFromID<GameStat>(guid);
                var gm = GameManager.Instance;
                if (model == null || gm == null || gm.StatsDict == null) return null;
                if (!gm.StatsDict.TryGetValue(model, out var stat) || stat == null) return null;
                float max = stat.CurrentMinMaxValue.y;
                if (max <= 0f) return null;
                if (stat.SimpleCurrentValue / max * 100f < threshold)
                    return $"{label} below {threshold}%";
            }
            catch (Exception ex) { Logger.LogDebug($"[Repeat] CheckThreshold('{label}') lookup failed: {ex}"); }
            return null;
        }

        private static bool GivenCardTransformed(Captured cap)
        {
            var c = cap.GivenCard;
            if (c == null || !c || string.IsNullOrEmpty(cap.GivenUid)) return false;
            string uid = UidOf(c);
            return !string.IsNullOrEmpty(uid) && !string.Equals(uid, cap.GivenUid, StringComparison.Ordinal);
        }

        // =====================================================================
        // NAME HELPERS
        // =====================================================================

        private static string CardName(InGameCardBase c)
        {
            if (c == null || !c) return "?";
            var m = c.CardModel;
            return m != null ? m.CardName.DefaultText : "?";
        }

        private static bool IsBlockedAction(string actionName)
        {
            if (string.IsNullOrEmpty(actionName)) return false;
            foreach (var keyword in BlockedActionKeywords)
                if (ContainsWord(actionName, keyword)) return true;
            return false;
        }

        private static bool IsRestLike(string actionName)
        {
            if (string.IsNullOrEmpty(actionName)) return false;
            return ContainsWord(actionName, "rest") || ContainsWord(actionName, "relax");
        }

        private static bool ContainsWord(string text, string word)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(word)) return false;
            int idx = 0;
            while ((idx = text.IndexOf(word, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                bool startOk = idx == 0 || !char.IsLetterOrDigit(text[idx - 1]);
                int end = idx + word.Length;
                bool endOk = end >= text.Length || !char.IsLetterOrDigit(text[end]);
                if (startOk && endOk) return true;
                idx = end;
            }
            return false;
        }
    }
}
