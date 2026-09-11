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

        /// <summary>
        /// Verbose Run Diagnostics: a stop/abort/resolution breadcrumb. Info while a run is active
        /// AND the config is on, so a player can read why a run stopped without enabling BepInEx
        /// Debug output (CLAUDE.md §BepInEx Logging: LogDebug is invisible by default); Debug
        /// otherwise. Gated on isRepeating, so it never touches the one-Info-line startup budget.
        /// </summary>
        private static void RunLog(string message)
        {
            if (isRepeating && Plugin.VerboseRunDiagnostics != null && Plugin.VerboseRunDiagnostics.Value)
                Logger.LogInfo($"[Repeat] {message}");
            else
                Logger.LogDebug($"[Repeat] {message}");
        }

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
        private static readonly string[] BuiltInBlockedKeywords = { "continue" };

        // Built-ins + the player's "Extra Blocked Actions" config, re-parsed only when that
        // config string changes (BepInEx lets the player edit it mid-session).
        private static string[] blockedKeywordsCache = BuiltInBlockedKeywords;
        private static string blockedKeywordsSource;

        private static string[] BlockedActionKeywords
        {
            get
            {
                string raw = Plugin.ExtraBlockedActions != null ? Plugin.ExtraBlockedActions.Value : null;
                if (string.Equals(raw, blockedKeywordsSource, StringComparison.Ordinal)) return blockedKeywordsCache;
                blockedKeywordsSource = raw;
                if (string.IsNullOrEmpty(raw) || raw.Trim().Length == 0)
                {
                    blockedKeywordsCache = BuiltInBlockedKeywords;
                }
                else
                {
                    var merged = new List<string>(BuiltInBlockedKeywords);
                    foreach (var part in raw.Split(','))
                    {
                        string k = part.Trim();
                        if (k.Length > 0 && !merged.Contains(k)) merged.Add(k);
                    }
                    blockedKeywordsCache = merged.ToArray();
                }
                Logger.LogDebug($"[Repeat] Blocklist: {string.Join(", ", blockedKeywordsCache)}");
                return blockedKeywordsCache;
            }
        }

        // Extra Stat Thresholds: "guid:percent, guid:percent" - stop floors on any GameStat, checked
        // after the three fixed vanilla ones. Re-parsed only when the config string changes (BepInEx
        // lets the player edit it mid-session). A malformed entry is skipped with a breadcrumb so one
        // typo cannot silently disable the others.
        private struct ExtraThreshold { public string Guid; public int Percent; }
        private static readonly List<ExtraThreshold> noExtraThresholds = new List<ExtraThreshold>();
        private static List<ExtraThreshold> extraThresholdsCache = noExtraThresholds;
        private static string extraThresholdsSource;

        private static List<ExtraThreshold> ExtraStatThresholds
        {
            get
            {
                string raw = Plugin.ExtraStatThresholds != null ? Plugin.ExtraStatThresholds.Value : null;
                if (string.Equals(raw, extraThresholdsSource, StringComparison.Ordinal)) return extraThresholdsCache;
                extraThresholdsSource = raw;
                var parsed = new List<ExtraThreshold>();
                if (!string.IsNullOrEmpty(raw) && raw.Trim().Length > 0)
                {
                    foreach (var part in raw.Split(','))
                    {
                        string entry = part.Trim();
                        if (entry.Length == 0) continue;
                        int sep = entry.LastIndexOf(':');
                        string guid = sep > 0 ? entry.Substring(0, sep).Trim() : "";
                        string pctText = sep > 0 ? entry.Substring(sep + 1).Trim() : "";
                        if (guid.Length == 0 || !int.TryParse(pctText, out int pct) || pct < 1 || pct > 100)
                        {
                            RunLog($"Extra Stat Thresholds: skipping malformed entry '{entry}' (expected guid:percent, percent 1-100)");
                            continue;
                        }
                        parsed.Add(new ExtraThreshold { Guid = guid, Percent = pct });
                    }
                }
                extraThresholdsCache = parsed;
                RunLog($"Extra Stat Thresholds: {parsed.Count} active entr{(parsed.Count == 1 ? "y" : "ies")}");
                return extraThresholdsCache;
            }
        }

        private static Captured last;
        private static string lastRejectedName;          // for the "'X' is not supported" toast
        private static bool isRepeating;
        private static bool cancelRequested;
        private static int groupCaptureFrame = -1;       // PerformGroupInventoryAction loops PerformAction internally

        // Per-Card Group Repeat: the live cards already dispatched during the CURRENT run, so each
        // iteration lands on the next still-unprocessed member of the captured group. Reset per run.
        private static readonly List<InGameCardBase> perCardDispatched = new List<InGameCardBase>();

        private static bool PerCardGroupMode => Plugin.PerCardGroupRepeat != null && Plugin.PerCardGroupRepeat.Value;

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
                Patch(harmony,
                    AccessTools.Method(gm, nameof(GameManager.PerformAction)),
                    nameof(GameManager.PerformAction), nameof(PerformAction_Prefix));
                Patch(harmony,
                    AccessTools.Method(gm, nameof(GameManager.PerformStackAction),
                        new[] { typeof(CardAction), typeof(DynamicLayoutSlot), typeof(bool), typeof(InGameNPCOrPlayer) }),
                    "PerformStackAction(DynamicLayoutSlot)", nameof(PerformStackActionLayout_Prefix));
                Patch(harmony,
                    AccessTools.Method(gm, nameof(GameManager.PerformStackAction),
                        new[] { typeof(CardAction), typeof(InventorySlot), typeof(bool), typeof(InGameNPCOrPlayer) }),
                    "PerformStackAction(InventorySlot)", nameof(PerformStackActionInventory_Prefix));
                // NOTE: patch the STACK variant — every drag (including the single-card
                // PerformCardOnCardAction wrapper) funnels through PerformCardOnCardActionStack.
                Patch(harmony,
                    AccessTools.Method(gm, nameof(GameManager.PerformCardOnCardActionStack)),
                    nameof(GameManager.PerformCardOnCardActionStack), nameof(PerformCardOnCardActionStack_Prefix));
                Patch(harmony,
                    AccessTools.Method(gm, nameof(GameManager.PerformGroupInventoryAction)),
                    nameof(GameManager.PerformGroupInventoryAction), nameof(PerformGroupInventoryAction_Prefix));

                Logger.LogDebug("ActionPatch v2 applied — native-dispatch capture on 5 GameManager funnels");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to apply patches: {ex}");
            }
        }

        /// <summary>
        /// Patch one dispatch funnel, recording its resolved signature.
        ///
        /// This mod calls the game's dispatch funnels directly with compile-time typed
        /// arguments, so a parameter-list change in a game update breaks replay at runtime
        /// (EA 0.66bb added an InGameNPC param to CollectActionModifiers and every dispatch
        /// threw MissingMethodException). An unresolvable funnel is therefore an ERROR the
        /// player's log always shows; the healthy-case signature dump stays at Debug so the
        /// mod keeps to its one-Info-line-at-startup budget.
        /// </summary>
        private static void Patch(Harmony harmony, System.Reflection.MethodInfo target, string label, string prefixName)
        {
            if (target == null)
            {
                Logger.LogError($"Dispatch funnel '{label}' not found on GameManager - the game's signature likely changed in a game update. Repeat will not capture this action kind.");
                return;
            }
            var ps = target.GetParameters();
            var sig = new string[ps.Length];
            for (int i = 0; i < ps.Length; i++) sig[i] = $"{ps[i].ParameterType.Name} {ps[i].Name}";
            Logger.LogDebug($"[Funnel] {label}({string.Join(", ", sig)})");

            harmony.Patch(target, prefix: new HarmonyMethod(typeof(ActionPatch), prefixName));
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
            perCardDispatched.Clear();
            int completed = 0;
            string display = string.IsNullOrEmpty(cap.ActionName) ? "action" : cap.ActionName;
            string kind = KindLabel(cap.Kind);
            if (cap.Kind == FunnelKind.GroupAction && PerCardGroupMode) kind = "group, per card";
            string stopReason = null;

            // count <= 0 is the "unlimited" sentinel: run until a stop condition fires, with
            // MaxUnboundedIterations as a hard backstop against a never-failing action.
            bool unlimited = count <= Plugin.UnlimitedCount;
            int limit = unlimited ? Mathf.Max(1, Plugin.MaxUnboundedIterations.Value) : count;
            string target = unlimited ? "unlimited" : $"x{count}";

            try
            {
                Plugin.ReportRunProgress(0, count, unlimited);
                Plugin.ShowNotification($"Repeating {kind}: {display} {target}");
                Logger.LogInfo($"[Repeat] Starting: '{display}' {target} ({cap.Kind})");

                for (int i = 0; i < limit; i++)
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
                    if (!IsGameIdle())
                    {
                        stopReason = "timed out waiting for the game to settle";
                        RunLog($"iteration {completed + 1}: gave up after {waited:0.0}s waiting for idle (state={GameManager.CurrentState}, performingAction={GameManager.PerformingAction})");
                        break;
                    }
                    yield return null;

                    if (Plugin.StopOnLowStats.Value && GameManager.Instance.AnyActionBlockers)
                    {
                        // Surface the game's own reason for the block (starving, exhausted, ...)
                        // rather than guessing at one.
                        stopReason = ActionBlockerMessage() ?? "blocked by your condition";
                        RunLog($"iteration {completed + 1}: a status blocker is active (Stop On Low Stats) - '{stopReason}'");
                        break;
                    }
                    string statStop = CheckStatThresholds();
                    if (statStop != null) { stopReason = statStop; break; }

                    if (Plugin.StopOnInventoryFull.Value && CarriedInventoryFull())
                    {
                        stopReason = "inventory full";
                        RunLog($"iteration {completed + 1}: every carried container is full (Stop On Inventory Full)");
                        break;
                    }

                    if (!TryDispatch(cap, completed, out Coroutine running, out string failReason))
                    {
                        stopReason = failReason;
                        RunLog($"iteration {completed + 1}: dispatch refused - {failReason}");
                        break;
                    }
                    RunLog($"iteration {completed + 1}: dispatched '{display}' ({kind})");

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
                    if (gameEnded)
                    {
                        stopReason = "game ended";
                        RunLog($"iteration {completed + 1}: GameManager went away mid-action (quit to menu or load?)");
                        break;
                    }
                    if (cancelRequested) { stopReason = "cancelled"; break; }

                    completed++;
                    Plugin.ReportRunProgress(completed, count, unlimited);
                    if (unlimited) Plugin.ShowNotification($"{display}: {completed}");
                    else if (count > 1) Plugin.ShowNotification($"{display}: {completed}/{count}");

                    if (cap.Kind == FunnelKind.CardOnCard && Plugin.StopOnToolBreak.Value && GivenCardTransformed(cap))
                    {
                        stopReason = "tool changed";
                        RunLog($"iteration {completed}: drag-drop tool '{cap.GivenUid}' is now '{UidOf(cap.GivenCard)}' (Stop On Tool Break)");
                        break;
                    }
                }
            }
            finally
            {
                isRepeating = false;
                Plugin.EndRunProgress();
            }

            string progress = unlimited ? completed.ToString() : $"{completed}/{count}";
            if (stopReason == null)
            {
                // In unlimited mode, running out of iterations is the cap, not a clean finish.
                if (unlimited)
                {
                    Plugin.ShowNotification($"Stopped - reached the {limit}-iteration limit ({progress})");
                    Logger.LogInfo($"[Repeat] Stopped after {progress}: hit MaxUnboundedIterations ({limit})");
                }
                else
                {
                    Plugin.ShowNotification($"Complete: {progress}");
                    Logger.LogInfo($"[Repeat] Complete: {progress}");
                }
            }
            else
            {
                Plugin.ShowNotification($"Stopped - {stopReason} ({progress})");
                Logger.LogInfo($"[Repeat] Stopped after {progress}: {stopReason}");
            }
        }

        // OnGUI polls this several times a frame; a failing lookup must not flood the log.
        private static bool popupCheckWarned;

        /// <summary>
        /// True while an inspection popup (card, NPC, blueprint or inventory) is open. The game's own
        /// signal: GraphicsManager.CurrentInspectionPopup is assigned on every popup-open path and
        /// nulled by CloseAllPopups / ClearInspectedCard (.decomp/GraphicsManager.cs); the
        /// activeInHierarchy check covers the frames between Hide() and the field being cleared.
        /// Used by the persistent count indicator (Plugin.OnGUI).
        /// </summary>
        public static bool IsCardPopupOpen()
        {
            try
            {
                if (GameManager.Instance == null) return false;
                var g = MBSingleton<GraphicsManager>.Instance;
                if (g == null || !g) return false;
                var popup = g.CurrentInspectionPopup;
                return popup != null && popup && popup.gameObject.activeInHierarchy;
            }
            catch (Exception ex)
            {
                if (!popupCheckWarned)
                {
                    popupCheckWarned = true;
                    Logger.LogDebug($"[HUD] IsCardPopupOpen check failed (count indicator stays hidden): {ex}");
                }
                return false;
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
                        RunLog($"using the captured action object directly for '{cap.ActionName}' (not in the live card's DismantleActions)");
                    }
                }
                else
                {
                    // Receiving card left the board (travel, consumed target): find the same action
                    // on any live card — e.g. the direction action on the NEW location card.
                    if (FindActionAnywhere(cap, out card, out action))
                        RunLog($"target changed - found '{cap.ActionName}' on '{CardName(card)}'");
                }

                if (card == null || action == null)
                {
                    RunLog(card == null
                        ? $"no live card '{cap.ReceivingUid}' on this board and no other card offers '{cap.ActionName}'"
                        : $"live card '{CardName(card)}' no longer offers '{cap.ActionName}'");
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
                RunLog($"no live card '{cap.ReceivingUid}' left on this board for the stack action");
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
                RunLog($"no live receiving card '{cap.ReceivingUid}' on this board for the drag-drop");
                failReason = completed > 0 ? "no more targets" : "target card not found";
                return false;
            }
            var given = ResolveCard(cap.GivenCard, cap.GivenUid, receiving);
            if (given == null)
            {
                RunLog($"no live given card '{cap.GivenUid}' left to drag onto '{CardName(receiving)}'");
                failReason = completed > 0 ? "source used up" : "source card not found";
                return false;
            }
            var action = cap.Action as CardOnCardAction;
            if (action == null) { failReason = "action no longer available"; return false; }

            // A programmatic move does not go through the vanilla drag path, so its
            // CannotBeTransferred gate never runs for us (CLAUDE.md §Programmatic Card
            // Movement). Re-check it here, mirroring InGameCardBase.CanTransferLiquids.
            if (cap.IsLiquidTransfer && (LiquidTransferBlocked(given) || LiquidTransferBlocked(receiving)))
            {
                failReason = "liquid can no longer be transferred";
                return false;
            }

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

        /// <summary>
        /// Whole-group mode (default): rebuild the whole captured group from live cards and sweep it
        /// in one PerformGroupInventoryAction, exactly as the player's click did. Per-Card Group
        /// Repeat: walk the captured group in order and dispatch only the FIRST member not yet
        /// processed this run (same funnel, one-element lists), so one iteration = one card and the
        /// run ends with "no more targets" once the group is exhausted.
        /// </summary>
        private static bool DispatchGroup(Captured cap, int completed, ref Coroutine running, ref string failReason)
        {
            bool perCard = PerCardGroupMode;
            var cards = new List<InGameCardBase>();
            var actions = new List<DismantleCardAction>();
            for (int i = 0; i < cap.GroupCards.Count; i++)
            {
                // Per-card: exclude everything already dispatched this run; a captured member that
                // was consumed falls back (via ResolveCard) to another live card of the same UID.
                var c = ResolveCard(cap.GroupCards[i], i < cap.GroupUids.Count ? cap.GroupUids[i] : null, null,
                    perCard ? perCardDispatched : cards);
                if (c == null || cards.Contains(c)) continue;
                cards.Add(c);
                actions.Add(cap.GroupActions[i]);
                if (perCard) break;
            }
            if (cards.Count == 0)
            {
                RunLog(perCard
                    ? $"all {cap.GroupCards.Count} captured group member(s) already processed or gone ({perCardDispatched.Count} dispatched this run)"
                    : $"none of the {cap.GroupCards.Count} captured group member(s) is live on this board");
                failReason = completed > 0 ? "no more targets" : "target cards not found";
                return false;
            }

            if (!ActionAvailable(actions[0], cards[0], null, ref failReason)) return false;

            running = GameManager.PerformGroupInventoryAction(cards, actions, _FastMode: false, InGameNPCOrPlayer.PlayerAgent);
            if (running == null) { failReason = "the game rejected the action"; return false; }
            if (perCard) perCardDispatched.Add(cards[0]);
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
                    RunLog($"the game's availability check refused '{action.ActionName.DefaultText}' on '{CardName(card)}': {failReason}");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                RunLog($"availability check threw ({ex.GetType().Name}) - dispatching anyway");
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

        /// <summary>
        /// First stat floor crossed, or null. The three fixed vanilla floors are checked first, then
        /// every Extra Stat Thresholds entry in the order written; the generic list is additive and
        /// goes through the same CheckThreshold as the fixed three.
        /// </summary>
        private static string CheckStatThresholds()
        {
            string stop = CheckThreshold("Satiation", SatiationGuid, Plugin.SatiationStopThreshold.Value)
                ?? CheckThreshold("Hydration", HydrationGuid, Plugin.HydrationStopThreshold.Value)
                ?? CheckThreshold("Stamina", StaminaGuid, Plugin.StaminaStopThreshold.Value);
            if (stop != null) return stop;

            var extra = ExtraStatThresholds;
            for (int i = 0; i < extra.Count; i++)
            {
                stop = CheckThreshold(null, extra[i].Guid, extra[i].Percent);
                if (stop != null) return stop;
            }
            return null;
        }

        /// <param name="label">Fixed player-facing name, or null to use the resolved stat's own GameName.</param>
        private static string CheckThreshold(string label, string guid, int threshold)
        {
            if (threshold <= 0) return null;
            string who = label ?? guid;
            try
            {
                var model = UniqueIDScriptable.GetFromID<GameStat>(guid);
                var gm = GameManager.Instance;
                if (model == null)
                {
                    // An opted-in safety stop that can never fire is worse than none - say so.
                    RunLog($"CheckThreshold('{who}'): stat GUID {guid} resolved to null; this stop cannot fire.");
                    return null;
                }
                if (gm == null || gm.StatsDict == null) return null;
                if (!gm.StatsDict.TryGetValue(model, out var stat) || stat == null) return null;
                float max = stat.CurrentMinMaxValue.y;
                if (max <= 0f) return null;
                float pct = stat.SimpleCurrentValue / max * 100f;
                if (pct < threshold)
                {
                    string name = label ?? StatDisplayName(model);
                    RunLog($"{name} is {stat.SimpleCurrentValue:0.#}/{max:0.#} ({pct:0.#}%), under its {threshold}% floor");
                    return $"{name} below {threshold}%";
                }
            }
            catch (Exception ex) { Logger.LogDebug($"[Repeat] CheckThreshold('{who}') lookup failed: {ex}"); }
            return null;
        }

        /// <summary>Player-facing name for a generic-threshold stat: its localized GameName, else its asset name.</summary>
        private static string StatDisplayName(GameStat model)
        {
            try
            {
                string n = model.GameName != null ? model.GameName.ToString() : null;
                if (!string.IsNullOrEmpty(n)) return n;
                n = model.GameName != null ? model.GameName.DefaultText : null;
                if (!string.IsNullOrEmpty(n)) return n;
            }
            catch (Exception ex) { Logger.LogDebug($"[Repeat] StatDisplayName failed for '{model.name}': {ex}"); }
            return model.name;
        }

        // GameManager.CurrentActionBlockers is private; AnyActionBlockers only says "some
        // blocker is active". Cache the field once and read the game's own message off it.
        private static System.Reflection.FieldInfo blockersField;
        private static bool blockersFieldResolved;

        /// <summary>The active status blocker's own message ("You are too exhausted…"), or null.</summary>
        private static string ActionBlockerMessage()
        {
            try
            {
                if (!blockersFieldResolved)
                {
                    blockersFieldResolved = true;
                    blockersField = AccessTools.Field(typeof(GameManager), "CurrentActionBlockers");
                    if (blockersField == null)
                        Logger.LogDebug("[Repeat] GameManager.CurrentActionBlockers not found - falling back to a generic stop reason.");
                }
                var gm = GameManager.Instance;
                if (blockersField == null || gm == null) return null;
                var list = blockersField.GetValue(gm) as List<StatusActionBlocker>;
                if (list == null) return null;
                for (int i = 0; i < list.Count; i++)
                    if (list[i] != null && !string.IsNullOrEmpty(list[i].BlockedMessage))
                        return list[i].BlockedMessage;
            }
            catch (Exception ex) { Logger.LogDebug($"[Repeat] ActionBlockerMessage read failed: {ex}"); }
            return null;
        }

        /// <summary>
        /// True when every container the player is carrying is full. Fails safe: a player
        /// carrying no container at all (or an unreadable equipment line) never trips this,
        /// so the stop can only end a repeat that genuinely has nowhere to put its output.
        /// </summary>
        private static bool CarriedInventoryFull()
        {
            try
            {
                var g = MBSingleton<GraphicsManager>.Instance;
                if (g == null || !g) return false;
                // Top-level equipment only - the bags/pouches themselves, not their contents.
                var equipped = g.GetEquippedCards(_CountInInventories: false);
                if (equipped == null) return false;
                bool sawContainer = false;
                for (int i = 0; i < equipped.Count; i++)
                {
                    var c = equipped[i];
                    if (!IsLive(c)) continue;
                    if (c.CardModel.CannotPutItemsIn || c.MaxWeightCapacity <= 0f) continue;
                    sawContainer = true;
                    if (!c.InventoryFull) return false;
                }
                return sawContainer;
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"[Repeat] CarriedInventoryFull check failed: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Mirrors the CannotBeTransferred half of InGameCardBase.CanTransferLiquids: the
        /// effective liquid is the card's own LiquidVersion if it has one, else whatever it
        /// currently contains.
        /// </summary>
        private static bool LiquidTransferBlocked(InGameCardBase c)
        {
            if (c == null || !c) return false;
            try
            {
                CardData liquid = c.LiquidVersion ? c.LiquidVersion : c.ContainedLiquidModel;
                return liquid && liquid.CannotBeTransferred;
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"[Repeat] LiquidTransferBlocked read failed on '{CardName(c)}': {ex}");
                return false;
            }
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

        /// <summary>Player-facing name for the funnel a repeat is replaying through.</summary>
        private static string KindLabel(FunnelKind kind)
        {
            switch (kind)
            {
                case FunnelKind.StackAction: return "stack";
                case FunnelKind.CardOnCard: return "drag-drop";
                case FunnelKind.GroupAction: return "group";
                default: return "action";
            }
        }

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
