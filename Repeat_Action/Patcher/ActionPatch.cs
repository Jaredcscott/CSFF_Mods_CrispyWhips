using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
    public static partial class ActionPatch
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
            public string ActionName;                    // ActionName.DefaultText: every comparison and log uses this
            public string ActionDisplay;                 // ActionName.ToString(), the player's language: shown only (Feature 16)
            public InGameCardBase ReceivingCard;
            public string ReceivingUid;
            public InGameCardBase GivenCard;             // CardOnCard only
            public string GivenUid;
            public InGameCardBase GivenContainer;        // CardOnCard only: the container the dragged card sat in (null = loose on a
                                                         // board or hand row), re-read from the live card every iteration
            public bool HasGivenContainer;               // CardOnCard only: GivenContainer was read from a real card, so "loose" (null)
                                                         // stays apart from "never read"
            public bool GivenConsumedByAction;           // CardOnCard only: the action itself destroys or transforms the dragged card
                                                         // (food, a material), so its going away is never a tool break
            public bool GivenBreaksAtZero;               // CardOnCard only: the dragged card's model destroys or transforms itself when
                                                         // its usage bar empties (a tool's UsageDurability OnZero), read before the dispatch
            public bool IsLiquidTransfer;                // CardOnCard only
            public bool StackLiquids;                    // StackAction only
            public List<InGameCardBase> GroupCards;      // GroupAction only
            public List<string> GroupUids;
            public GroupInventoryAction GroupDef;        // GroupAction only: the button's definition, so a
                                                         // replay rebuilds the group the way the game does
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
                blockedKeywordsCache = MergeKeywords(BuiltInBlockedKeywords, raw);
                Logger.LogDebug($"[Repeat] Blocklist: {string.Join(", ", blockedKeywordsCache)}");
                return blockedKeywordsCache;
            }
        }

        // Actions that never replace a non-rest `last` (CaptureAction, PerformGroupInventoryAction_Prefix): built-ins
        // + the player's "Extra Rest-Like Actions" config, parsed and cached exactly as the blocklist above.
        private static readonly string[] BuiltInRestLikeKeywords = { "rest", "relax" };
        private static string[] restLikeKeywordsCache = BuiltInRestLikeKeywords;
        private static string restLikeKeywordsSource;

        private static string[] RestLikeKeywords
        {
            get
            {
                string raw = Plugin.ExtraRestLikeActions != null ? Plugin.ExtraRestLikeActions.Value : null;
                if (string.Equals(raw, restLikeKeywordsSource, StringComparison.Ordinal)) return restLikeKeywordsCache;
                restLikeKeywordsSource = raw;
                restLikeKeywordsCache = MergeKeywords(BuiltInRestLikeKeywords, raw);
                Logger.LogDebug($"[Repeat] Rest-like actions: {string.Join(", ", restLikeKeywordsCache)}");
                return restLikeKeywordsCache;
            }
        }

        /// <summary>The built-in words plus each non-empty, not-yet-listed entry of a comma-separated config string.</summary>
        private static string[] MergeKeywords(string[] builtIns, string raw)
        {
            if (string.IsNullOrEmpty(raw) || raw.Trim().Length == 0) return builtIns;
            var merged = new List<string>(builtIns);
            foreach (var part in raw.Split(','))
            {
                string k = part.Trim();
                if (k.Length > 0 && !merged.Contains(k)) merged.Add(k);
            }
            return merged.ToArray();
        }

        // Extra Stat Thresholds: "guid:percent, guid:>percent" - stop limits on any GameStat, checked
        // after the three fixed vanilla ones. "guid:percent" is a floor (stops when the stat falls below
        // that share of its current maximum); a leading '>' on the percent makes it a ceiling (stops when
        // the stat rises above that share), for a stat where high is the danger. Re-parsed only when the
        // config string changes (BepInEx lets the player edit it mid-session). A malformed entry is
        // skipped with a breadcrumb so one typo cannot silently disable the others.
        private struct ExtraThreshold { public string Guid; public int Percent; public bool Ceiling; }
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
                        // A leading '>' (after the split on the LAST colon) marks a ceiling. Only one is
                        // stripped, so '>>60' still fails the number parse below and is skipped.
                        bool ceiling = pctText.Length > 0 && pctText[0] == '>';
                        if (ceiling) pctText = pctText.Substring(1).Trim();
                        // A stat cannot rise above its own maximum, so a ceiling of 100 or more could never fire.
                        if (guid.Length == 0 || !int.TryParse(pctText, out int pct) || pct < 1 || pct > 100 || (ceiling && pct >= 100))
                        {
                            RunLog($"Extra Stat Thresholds: skipping malformed entry '{entry}' (expected guid:percent with percent 1-100, or guid:>percent with percent 1-99)");
                            continue;
                        }
                        parsed.Add(new ExtraThreshold { Guid = guid, Percent = pct, Ceiling = ceiling });
                    }
                }
                extraThresholdsCache = parsed;
                RunLog($"Extra Stat Thresholds: {parsed.Count} active entr{(parsed.Count == 1 ? "y" : "ies")}");
                return extraThresholdsCache;
            }
        }

        // Stop At Quantity: "uniqueid:count" - stop once the player's location holds at least count cards of that
        // UniqueID. Parsed the Extra Stat Thresholds way: re-parsed only when the config string changes, a malformed
        // entry skipped with a breadcrumb. Whether a UniqueID names a card is asked per run (ReportUnknownQuantityCards),
        // not here, so the parse never depends on when the game's card database is ready.
        private struct QuantityStop { public string Uid; public int Count; }
        private static readonly List<QuantityStop> noQuantityStops = new List<QuantityStop>();
        private static List<QuantityStop> quantityStopsCache = noQuantityStops;
        private static string quantityStopsSource;

        private static List<QuantityStop> QuantityStops
        {
            get
            {
                string raw = Plugin.StopAtQuantity != null ? Plugin.StopAtQuantity.Value : null;
                if (string.Equals(raw, quantityStopsSource, StringComparison.Ordinal)) return quantityStopsCache;
                quantityStopsSource = raw;
                var parsed = new List<QuantityStop>();
                if (!string.IsNullOrEmpty(raw) && raw.Trim().Length > 0)
                {
                    foreach (var part in raw.Split(','))
                    {
                        string entry = part.Trim();
                        if (entry.Length == 0) continue;
                        int sep = entry.LastIndexOf(':');
                        string uid = sep > 0 ? entry.Substring(0, sep).Trim() : "";
                        string countText = sep > 0 ? entry.Substring(sep + 1).Trim() : "";
                        if (uid.Length == 0 || !int.TryParse(countText, out int n) || n < 1)
                        {
                            RunLog($"Stop At Quantity: skipping malformed entry '{entry}' (expected uniqueid:count with count 1 or more)");
                            continue;
                        }
                        parsed.Add(new QuantityStop { Uid = uid, Count = n });
                    }
                }
                quantityStopsCache = parsed;
                RunLog($"Stop At Quantity: {parsed.Count} active entr{(parsed.Count == 1 ? "y" : "ies")}");
                return quantityStopsCache;
            }
        }

        private static Captured last;
        private static Captured pinned;                  // Pinned Action: fired by FirePinnedAction; in memory only
        private static string lastRejectedName;          // for the "'X' is not supported" toast and its log line (English)
        private static string lastRejectedDisplay;       // the same name in the player's language, for the toast (Feature 16)
        private static bool isRepeating;
        private static bool cancelRequested;
        // Set by OnRunStart together with cancelRequested: the cancel came from a new game starting (a load), not
        // from the player's key, so the run reports "game ended" like the quit-to-menu path. Reset per run.
        private static bool endedByNewRun;
        private static int groupCaptureFrame = -1;      // PerformGroupInventoryAction loops PerformAction internally

        // The group builder's definition (GroupInventoryAction) is only an argument to the button's
        // Setup(), never stored on it, so it is recorded per button here and handed to the group
        // capture when that button's Perform() fires in the same frame.
        private static readonly Dictionary<GroupInventoryActionButton, GroupInventoryAction> groupDefsByButton =
            new Dictionary<GroupInventoryActionButton, GroupInventoryAction>();
        private static GroupInventoryAction pendingGroupDef;
        private static int pendingGroupDefFrame = -1;
        private static bool groupDefMissingWarned;

        // ActionAvailable warns once per exception type; the run stops either way.
        private static readonly HashSet<string> availabilityFailuresWarned = new HashSet<string>();

        // Per-Card Group Repeat: the live cards already dispatched during the CURRENT run, so each
        // iteration lands on the next still-unprocessed member of the captured group. Reset per run.
        private static readonly List<InGameCardBase> perCardDispatched = new List<InGameCardBase>();

        private static bool PerCardGroupMode => Plugin.PerCardGroupRepeat != null && Plugin.PerCardGroupRepeat.Value;

        // Lock To Original Target: the acted-on card may not be swapped for a same-UID sibling or
        // for another card offering the same action. Never applies to a drag-drop's given card.
        // Set only while a refuel's TryDispatch runs (DispatchAndWait ignoreLock): Lock To Original Target holds a run to the
        // card it captured, never a refuel, whose pinned food card was eaten by the capture itself.
        private static bool lockSuspended;
        private static bool LockToOriginal => !lockSuspended && Plugin.LockToOriginalTarget != null && Plugin.LockToOriginalTarget.Value;

        /// <summary>
        /// Lock To Original Target for one resolution: on only when there IS an original card. A step rebuilt from the
        /// saved sequence library (Feature 20) starts with none, so its first dispatch takes the first card of its kind
        /// here, and the lock holds the step to that card from then on (the dispatch stores the card it used).
        /// Every capture has an original, so a one-action run is unaffected.
        /// </summary>
        private static bool LockedTo(InGameCardBase original) => LockToOriginal && !ReferenceEquals(original, null);
        // Stop reasons code compares stay English (Messages.En); Messages.Shown translates them only for the toast.
        private static readonly string LockedTargetGone = Messages.En("locked target is gone");

        private static readonly string CancelledReason = Messages.En("cancelled");
        private static readonly string GameEndedReason = Messages.En("game ended");
        private static readonly string EncounterStopReason = Messages.En("an encounter interrupted the run");

        public static bool HasLastAction => last != null;
        public static bool HasPinnedAction => pinned != null;
        public static string LastActionName => last?.ActionName ?? lastRejectedName ?? "Unknown";
        /// <summary>The rejected action's name in the player's language, for the "not supported" toast.</summary>
        public static string RejectedActionDisplay => lastRejectedDisplay ?? lastRejectedName;
        /// <summary>What Shift+R would replay, in the player's language, for the count indicator; null when nothing is captured.</summary>
        public static string NextActionDisplay => last == null ? null : DisplayName(last);
        /// <summary>The pinned action's name for the count indicator, or null when nothing is pinned.</summary>
        public static string PinnedActionName => pinned == null ? null : DisplayName(pinned);
        public static bool IsRepeating => isRepeating;

        // Pause and resume: pauseRequested is the key's toggle, pausedNow is true only while RunRepeat is
        // actually waiting at the top of an iteration. Both are cleared in RunRepeat's finally.
        private static bool pauseRequested;
        private static bool pausedNow;
        public static bool IsPaused => pausedNow;

        /// <summary>The Pause Key: ask a running repeat to pause after its current iteration, or to resume.</summary>
        public static void TogglePause()
        {
            if (!isRepeating)
            {
                Plugin.ShowNotification(Messages.Text("No repeat is running"));
                return;
            }
            if (pausedNow)
            {
                pauseRequested = false;
                Plugin.ShowNotification(Messages.Text("Resuming"));
                RunLog("pause: resumed by the Pause Key");
            }
            else if (pauseRequested)
            {
                pauseRequested = false;
                Plugin.ShowNotification(Messages.Text("Pause cancelled"));
                RunLog("pause: request withdrawn before the run reached it");
            }
            else
            {
                pauseRequested = true;
                Plugin.ShowNotification(Messages.Text("Pausing after this iteration"));
                RunLog("pause: requested, the run pauses before its next iteration");
            }
        }

        public static void CancelRepeat() => cancelRequested = true;

        // =====================================================================
        // A NEW GAME STARTING ENDS A RUN IN PROGRESS (plan feature 5, W0.4)
        // =====================================================================
        // Going to the main menu already ends a run: the menu scene has no GameManager, so a wait loop sees a
        // null Instance and stops with "game ended". A LOAD does not (Wave 0 finding W0.4, measured): no wait
        // loop saw a null Instance, the run carried on in the loaded game, and its first pause after the load
        // read 6.8 s against the 2.0 s configured, so the next iteration dispatched at once.
        //
        // OnRunStart is subscribed ONCE per process (Plugin.Awake) and unsubscribed in Plugin.OnDestroy, never
        // from a method that can run twice: OnGMInitialized re-fires on every run start (root CLAUDE.md
        // "Per-Run Event Registration"), so a subscription made per run would stack one handler per reload.
        // It listens on two events of the new GameManager, both public static System.Action fields
        // (.decomp/GameManager.cs): OnGMStartInitialization, invoked at the top of its Awake, and
        // OnGMInitialized, invoked at the end of FinishInitializing. The pack names OnGMInitialized; on its own
        // it leaves the whole initialization window in which the new game reads idle (a fresh GameManager's
        // state is SELECT with no root action), so an iteration can dispatch into a game that is still
        // loading before the handler runs. The Awake-time event is the one meant to stop that; whether it does
        // is what the scenario's AlteredMindstate read measures. The first fire ends the run (the loop leaves
        // on its next frame), so the second finds isRepeating false and does nothing; the only run it could
        // wrongly end is one the player starts inside the few frames between the two events.
        //
        // It does not touch `pinned` or `last`: both re-resolve by UniqueID, and the README says a pin lasts
        // until you quit the game.
        private static void OnRunStart()
        {
            CancelRecordingForNewGame();
            if (!isRepeating) return;
            endedByNewRun = true;
            cancelRequested = true;
            RunLog("a new game started while this repeat was active: ending the repeat");
        }

        /// <summary>Called once from Plugin.Awake. Unsubscribe-before-subscribe keeps it a single subscription even if it ever ran twice.</summary>
        internal static void SubscribeRunStart()
        {
            try { ChangeRunStartSubscription(true); }
            catch (Exception ex)
            {
                Logger.LogWarning($"[Repeat] Could not subscribe to the game's run-start events ({ex.GetType().Name}: {ex.Message}); loading a save during a run will not end it.");
            }
        }

        /// <summary>Called from Plugin.OnDestroy.</summary>
        internal static void UnsubscribeRunStart()
        {
            try { ChangeRunStartSubscription(false); }
            catch (Exception ex) { Logger.LogDebug($"[Repeat] Could not unsubscribe from the game's run-start events: {ex.GetType().Name}: {ex.Message}"); }
        }

        // The two typed game references live in their own non-inlined method, so a game update that renames one
        // surfaces inside the callers' try (the shape Plugin.PlayerIsTyping uses), not while JIT-compiling Awake.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ChangeRunStartSubscription(bool subscribe)
        {
            GameManager.OnGMStartInitialization -= OnRunStart;
            GameManager.OnGMInitialized -= OnRunStart;
            if (!subscribe) return;
            GameManager.OnGMStartInitialization += OnRunStart;
            GameManager.OnGMInitialized += OnRunStart;
        }

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

                var gb = typeof(GroupInventoryActionButton);
                Patch(harmony,
                    AccessTools.Method(gb, nameof(GroupInventoryActionButton.Setup),
                        new[] { typeof(int), typeof(GroupInventoryAction), typeof(InGameCardBase) }),
                    "GroupInventoryActionButton.Setup", nameof(GroupButtonSetup_Postfix), postfix: true);
                Patch(harmony,
                    AccessTools.Method(gb, nameof(GroupInventoryActionButton.Perform)),
                    "GroupInventoryActionButton.Perform", nameof(GroupButtonPerform_Prefix));

                Logger.LogDebug("ActionPatch v2 applied - native-dispatch capture on 5 GameManager funnels and the group button");
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
        /// mod keeps to its one-Info-line-at-startup budget. Each patch is isolated: the
        /// patches bind the original's parameters BY NAME, so a renamed parameter makes
        /// harmony.Patch throw, and that must cost only this patch, not every one after it.
        /// </summary>
        private static void Patch(Harmony harmony, System.Reflection.MethodInfo target, string label, string patchName, bool postfix = false)
        {
            if (target == null)
            {
                Logger.LogError($"Patch target '{label}' not found - the game's signature likely changed in a game update. Repeat will not capture this action kind.");
                return;
            }
            var ps = target.GetParameters();
            var sig = new string[ps.Length];
            for (int i = 0; i < ps.Length; i++) sig[i] = $"{ps[i].ParameterType.Name} {ps[i].Name}";
            Logger.LogDebug($"[Funnel] {label}({string.Join(", ", sig)})");

            try
            {
                var method = new HarmonyMethod(typeof(ActionPatch), patchName);
                if (postfix) harmony.Patch(target, postfix: method);
                else harmony.Patch(target, prefix: method);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Could not patch '{label}' - Repeat will not capture this action kind: {ex}");
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
            // The popup GameObject test alone missed Encounter_Finished (seen in Wave 0 W0.5): EncounterPopup.PressContinue
            // deactivates the popup's GameObject and only then starts DoEndOfEncounterActions, which performs
            // Encounter_Finished through PerformAction (not fast mode) while OngoingEncounter is still true; it is
            // cleared after that action (.decomp/EncounterPopup.cs). So an ongoing encounter refuses capture too.
            return !InEncounterOrDialog() && !EncounterOngoing();
        }

        private static bool encounterReadFailed;

        /// <summary>
        /// True while an encounter is in progress, from its start popup to the end of its result
        /// (EncounterPopup.OngoingEncounter: set in StartEncounter, cleared in CancelEncounter or after the
        /// Encounter_Finished action). InEncounterOrDialog's GameObject test reads false for the whole start popup
        /// (W0.5), which is all of an encounter the player escapes at the start. Read every frame while a run waits,
        /// so a failure warns once and then reads false, as if no encounter were open.
        /// </summary>
        private static bool EncounterOngoing()
        {
            try
            {
                var enc = MBSingleton<EncounterPopup>.Instance;
                return enc != null && enc && enc.OngoingEncounter;
            }
            catch (Exception ex)
            {
                if (!encounterReadFailed)
                {
                    encounterReadFailed = true;
                    Logger.LogWarning($"[Repeat] Could not read whether an encounter is in progress; Stop After Encounter cannot fire and encounter actions may be recorded: {ex.GetType().Name}: {ex.Message}");
                }
                else Logger.LogDebug($"[Repeat] EncounterOngoing read failed again: {ex.GetType().Name}");
                return false;
            }
        }

        /// <summary>Stop After Encounter's latch: true (with a Verbose Run Diagnostics line) when an encounter is in progress now.</summary>
        private static bool EncounterSeenNow(int iteration, string where)
        {
            if (!EncounterOngoing()) return false;
            RunLog($"iteration {iteration}: an encounter is in progress, seen {where}");
            return true;
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
                    if (last == null) Reject(_Action);
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
                // A pour reaches this funnel through the non-stack GameManager.PerformCardOnCardAction, which always
                // passes _IsLiquidTransferAction: false (.decomp/GameManager.cs), so read the transfer from the action
                // too: CardData.GenerateLiquidTransferAction sets ModifyLiquid on both sides (harness T2.164, 2026-10-05).
                if (last != null && last.Kind == FunnelKind.CardOnCard && ReferenceEquals(last.Action, _Action))
                {
                    last.IsLiquidTransfer = _IsLiquidTransferAction
                        || (_Action.GivenCardChanges.ModifyLiquid && _Action.ReceivingCardChanges.ModifyLiquid);
                    RecordLiquidFlag(last); // a recorded pour is kept for the session only (Feature 20)
                }
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
                if (IsBlockedAction(name)) { if (last == null) Reject(_Actions[0]); return; }
                if (IsRestLike(name) && last != null && !IsRestLike(last.ActionName)) return;

                // A replay rebuilds the group from the button's definition. Without it only the
                // click-time action copies exist, and replaying those re-applies their stale time
                // cost and flavour totals, so the action is refused rather than captured.
                if (pendingGroupDefFrame != Time.frameCount)
                {
                    if (!groupDefMissingWarned)
                    {
                        groupDefMissingWarned = true;
                        Logger.LogWarning($"[Capture] Group action '{name}' arrived without its group button's definition (the GroupInventoryActionButton patches did not run), so group actions cannot be repeated this session.");
                    }
                    if (last == null) Reject(_Actions[0]);
                    return;
                }
                pendingGroupDefFrame = -1;

                var uids = new List<string>(_Cards.Count);
                for (int i = 0; i < _Cards.Count; i++) uids.Add(UidOf(_Cards[i]));

                last = new Captured
                {
                    Kind = FunnelKind.GroupAction,
                    Action = _Actions[0],
                    ActionKey = _Actions[0].ActionName.LocalizationKey,
                    ActionName = name,
                    ActionDisplay = LocalizedText(_Actions[0].ActionName),
                    ReceivingCard = _Cards[0],
                    ReceivingUid = uids[0],
                    GroupCards = new List<InGameCardBase>(_Cards),
                    GroupUids = uids,
                    GroupDef = pendingGroupDef,
                };
                Reject(null);
                Logger.LogDebug($"[Capture] GroupAction: '{name}' on {_Cards.Count} card(s)");
                RecordStep(last);
            }
            catch (Exception ex) { Logger.LogError($"[Capture] PerformGroupInventoryAction error: {ex}"); }
        }

        static void GroupButtonSetup_Postfix(GroupInventoryActionButton __instance, GroupInventoryAction _Action)
        {
            try
            {
                if (__instance == null) return;
                if (groupDefsByButton.Count > 64) PruneDestroyedButtons();
                groupDefsByButton[__instance] = _Action;
            }
            catch (Exception ex) { Logger.LogError($"[Capture] GroupInventoryActionButton.Setup error: {ex}"); }
        }

        static void GroupButtonPerform_Prefix(GroupInventoryActionButton __instance)
        {
            try
            {
                if (__instance != null && groupDefsByButton.TryGetValue(__instance, out var def))
                {
                    pendingGroupDef = def;
                    pendingGroupDefFrame = Time.frameCount;
                }
            }
            catch (Exception ex) { Logger.LogError($"[Capture] GroupInventoryActionButton.Perform error: {ex}"); }
        }

        /// <summary>Buttons are pooled, so this stays small; drop any destroyed with their popup.</summary>
        private static void PruneDestroyedButtons()
        {
            var dead = new List<GroupInventoryActionButton>();
            foreach (var b in groupDefsByButton.Keys) if (!b) dead.Add(b);
            for (int i = 0; i < dead.Count; i++) groupDefsByButton.Remove(dead[i]);
        }

        /// <summary>Records an action that cannot be repeated, for the "not supported" toast; null clears it.</summary>
        private static void Reject(CardAction action)
        {
            lastRejectedName = action != null ? action.ActionName.DefaultText : null;
            lastRejectedDisplay = action != null ? LocalizedText(action.ActionName) : null;
        }

        private static void CaptureAction(FunnelKind kind, CardAction action, InGameCardBase receiving, InGameCardBase given, bool stackLiquids)
        {
            string name = action.ActionName.DefaultText;
            if (IsBlockedAction(name)) { if (last == null) Reject(action); return; }

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
                ActionDisplay = LocalizedText(action.ActionName),
                ReceivingCard = receiving,
                ReceivingUid = UidOf(receiving),
                GivenCard = given,
                GivenUid = UidOf(given),
                // Read now, while the card is still where the player dragged it from: the drag has not run yet and the
                // card is destroyed or moved by it, so its container cannot be read afterwards.
                GivenContainer = ContainerOf(given),
                HasGivenContainer = IsLive(given),
                StackLiquids = stackLiquids,
            };
            Reject(null);
            Logger.LogDebug($"[Capture] {kind}: '{name}' on '{CardName(receiving)}'");
            RecordStep(last);
        }

        // =====================================================================
        // REPLAY
        // =====================================================================

        public static IEnumerator RepeatLastAction(int count) => RunRepeat(last, count, fromPin: false);

        public static IEnumerator FirePinnedAction(int count) => RunRepeat(pinned, count, fromPin: true);

        /// <summary>
        /// Pin the action Shift+R would replay right now. The pin holds the same Captured object as
        /// last: a later capture replaces last with a NEW Captured and never touches this one, so
        /// the pin keeps its action while Shift+R moves on.
        /// </summary>
        public static void PinCurrentAction()
        {
            if (last == null)
            {
                Plugin.ShowNotification(Messages.Text("No action to pin"));
                Logger.LogInfo("[Repeat] No action recorded to pin");
                return;
            }
            pinned = last;
            string display = DisplayName(pinned);
            Plugin.ShowNotification(Messages.Text("Pinned: {0}", display));
            Logger.LogInfo($"[Repeat] Pinned: '{display}' ({pinned.Kind})");
        }

        /// <summary>
        /// The replay loop behind both Shift+R (the last capture) and the pinned action. A pinned run
        /// differs only in its labels and in clearing the pin when its target is gone.
        /// </summary>
        private static IEnumerator RunRepeat(Captured cap, int count, bool fromPin)
        {
            if (cap == null)
            {
                if (fromPin) Plugin.ShowNotification(Messages.Text("No pinned action"));
                else if (string.IsNullOrEmpty(lastRejectedName)) Plugin.ShowNotification(Messages.Text("No action to repeat"));
                else Plugin.ShowNotification(Messages.Text("'{0}' is not supported", RejectedActionDisplay));
                yield break;
            }
            // Driven by hand rather than yielded as a nested coroutine, so every frame it waits is a frame of this
            // coroutine, exactly as when the run's body lived here.
            var run = RunSteps(new List<Captured> { cap }, count, fromPin, null);
            while (run.MoveNext()) yield return run.Current;
        }

        /// <summary>
        /// The run itself, for one captured action (Shift+R, the pin) or a recorded sequence (Feature 20). Each repeat
        /// dispatches every step in order, and every step passes the same checks a single action's iteration does
        /// before it (pause, idle, encounter, blockers, stat limits and the refuel, inventory, nightfall, season,
        /// in-game hours, quantity), so a sequence is held to exactly what a one-action run is. A step that cannot be
        /// dispatched stops the run naming it, and is never skipped. With one step and no <paramref name="sequenceName"/>
        /// every log line and toast is the one-action run's.
        /// </summary>
        private static IEnumerator RunSteps(List<Captured> steps, int count, bool fromPin, string sequenceName)
        {
            if (isRepeating)
            {
                Plugin.ShowNotification(Messages.Text("A repeat is already running"));
                yield break;
            }
            if (GameManager.Instance == null)
            {
                Plugin.ShowNotification(Messages.Text("Game not ready"));
                yield break;
            }

            isRepeating = true;
            runSerial++;
            cancelRequested = false;
            endedByNewRun = false;
            perCardDispatched.Clear();
            int completed = 0;
            bool isSequence = sequenceName != null;
            string display = isSequence ? sequenceName : DisplayName(steps[0]);
            string kind = StepKindLabel(steps[0], fromPin);
            string stopReason = null;
            // Stop After Encounter: latched by every wait below (an encounter the repeated action draws is open only
            // during that action's own wait, W0.5) and read before the next dispatch. Tracked whatever the setting.
            bool encounterSeen = false;

            // Stop After In-Game Hours: whole game ticks since the run began (CurrentTickInfo.z), never real time (CLAUDE.md
            // Coroutine Timing). Hours convert with the game's own day length, read once per run (W0.6: DailyPoints 96,
            // so 4 ticks of 15 minutes an hour on EA 0.68b). Tracked whatever the setting, like the guards above.
            int? startTick = CurrentGlobalTick();
            float? ticksPerHour = TicksPerHour();
            int? lastIterStartTick = startTick;
            int maxIterTicks = 0;

            // Run Boundary Guards compare against what the run started in: the season, and whether
            // it has seen daylight (a run started at night is not stopped by the night it began in).
            string startSeasonId = CurrentSeasonId();
            bool sawDaylight = IsNightNow() == false;

            // One outcome object for every DispatchAndWait of this run (the main dispatch and any refuel).
            var dispatch = new DispatchOutcome();
            int refuels = 0;

            // count <= 0 is the "unlimited" sentinel: run until a stop condition fires, with
            // MaxUnboundedIterations as a hard backstop against a never-failing action.
            bool unlimited = count <= Plugin.UnlimitedCount;
            int limit = unlimited ? Mathf.Max(1, Plugin.MaxUnboundedIterations.Value) : count;
            string target = unlimited ? Messages.En("unlimited") : Messages.En("x{0}", count);

            try
            {
                Plugin.ReportRunProgress(0, count, unlimited);
                if (isSequence)
                {
                    Plugin.ShowNotification(Messages.Text("Playing {0}: {1}", display, unlimited ? "0" : $"0/{count}"));
                    Logger.LogInfo($"[Repeat] Starting: sequence '{display}' ({steps.Count} steps) {target}");
                }
                else
                {
                    Plugin.ShowNotification(Messages.Text("Repeating {0}: {1} {2}", Messages.Shown(kind), display, Messages.Shown(target)));
                    Logger.LogInfo($"[Repeat] Starting: '{display}' {target} ({steps[0].Kind}{(fromPin ? ", pinned" : "")})");
                }
                ReportUnknownQuantityCards();

                for (int i = 0; i < limit; i++)
                {
                    int si;
                    for (si = 0; si < steps.Count; si++)
                    {
                        var cap = steps[si];
                        string stepDisplay = isSequence ? DisplayName(cap) : display;
                        string stepKind = isSequence ? StepKindLabel(cap, false) : kind;
                        string what = isSequence ? $"iteration {completed + 1} step {si + 1} of {steps.Count}" : $"iteration {completed + 1}";
                        if (cancelRequested) { stopReason = CancelledReason; break; }

                        // Pause and resume: wait here, frame by frame (never WaitForSeconds), outside Action Completion
                        // Timeout. A load while paused sets cancelRequested (OnRunStart) and a quit to the menu destroys
                        // GameManager; both end the run. The game ticks spent paused are not the run's own game time, so
                        // both Stop After In-Game Hours baselines move by them: startTick for the elapsed total, and
                        // lastIterStartTick because the span to the next iteration start would otherwise read the pause
                        // as one very long iteration.
                        if (pauseRequested)
                        {
                            int? pauseStartTick = CurrentGlobalTick();
                            bool pauseGameEnded = false;
                            pausedNow = true;
                            string progressNow = unlimited ? completed.ToString() : $"{completed}/{count}";
                            Plugin.ShowNotification(Messages.Text("Paused at {0} - {1} to resume", progressNow, Plugin.PauseKey.Value));
                            RunLog($"pause: paused at {progressNow}");
                            while (pauseRequested && !cancelRequested)
                            {
                                yield return null;
                                if (GameManager.Instance == null) { pauseGameEnded = true; break; }
                            }
                            pausedNow = false;
                            if (pauseGameEnded)
                            {
                                stopReason = GameEndedReason;
                                RunLog("pause: GameManager went away while paused (quit to menu or load?)");
                                break;
                            }
                            int? pauseEndTick = CurrentGlobalTick();
                            if (pauseStartTick.HasValue && pauseEndTick.HasValue)
                            {
                                int pausedTicks = pauseEndTick.Value - pauseStartTick.Value;
                                if (startTick.HasValue) startTick += pausedTicks;
                                if (lastIterStartTick.HasValue) lastIterStartTick += pausedTicks;
                                RunLog($"pause: {pausedTicks} game tick(s) spent paused are not counted toward Stop After In-Game Hours");
                            }
                            if (cancelRequested) { stopReason = CancelledReason; break; }
                        }

                        // Wait for the game to be idle. Frame yields + unscaled time — never
                        // WaitForSeconds (stalls at timeScale=0, CLAUDE.md §Coroutine Timing).
                        float waited = 0f;
                        while (!IsGameIdle() && waited < Plugin.ActionCompletionTimeout.Value && !cancelRequested)
                        {
                            yield return null;
                            waited += Time.unscaledDeltaTime;
                            if (!encounterSeen) encounterSeen = EncounterSeenNow(completed + 1, "while waiting for the game to settle");
                        }
                        if (cancelRequested) { stopReason = CancelledReason; break; }
                        if (!IsGameIdle())
                        {
                            stopReason = Messages.En("timed out waiting for the game to settle");
                            RunLog($"iteration {completed + 1}: gave up after {waited:0.0}s waiting for idle (state={GameManager.CurrentState}, performingAction={GameManager.PerformingAction})");
                            break;
                        }
                        yield return null;

                        // Stop After Encounter, before the next dispatch: an encounter drawn by the last iteration (seen
                        // during its wait) or one open now. Never answers or skips the encounter (CLAUDE.md Encounter
                        // System Patching): a natural one is already over by the time its iteration's wait ends.
                        if (!encounterSeen) encounterSeen = EncounterSeenNow(completed + 1, "before the dispatch");
                        if (encounterSeen && Plugin.StopAfterEncounter.Value)
                        {
                            stopReason = EncounterStopReason;
                            RunLog($"iteration {completed + 1}: not dispatched, an encounter interrupted this run (Stop After Encounter)");
                            break;
                        }

                        if (Plugin.StopOnLowStats.Value && GameManager.Instance.AnyActionBlockers)
                        {
                            // Surface the game's own reason for the block (starving, exhausted, ...)
                            // rather than guessing at one.
                            stopReason = ActionBlockerMessage() ?? Messages.En("blocked by your condition");
                            RunLog($"iteration {completed + 1}: a status blocker is active (Stop On Low Stats) - '{stopReason}'");
                            break;
                        }
                        string statStop = CheckStatThresholds(out bool statCeiling);
                        // Refuel From Pinned Action (Feature 17): a floor (never a ceiling) may be answered by firing the pin
                        // once, unless this run IS the pin. The floor is still judged here, in its place; the refuel itself
                        // waits until every other stop below has passed, so a run that stops anyway never eats the food.
                        string refuelFor = null;
                        if (statStop != null)
                        {
                            bool canRefuel = !statCeiling && Plugin.RefuelFromPinnedAction.Value
                                && pinned != null && !ReferenceEquals(cap, pinned);
                            if (canRefuel && refuels >= Plugin.MaxRefuelsPerRun.Value)
                            {
                                Logger.LogInfo($"[Repeat] Refuel: {statStop}, but this run has already refuelled {refuels} time(s) (Maximum Refuels Per Run)");
                                stopReason = Messages.En("{0} (refuel limit reached)", statStop);
                                break;
                            }
                            if (!canRefuel) { stopReason = statStop; break; }
                            refuelFor = statStop;
                        }

                        if (Plugin.StopOnInventoryFull.Value && CarriedInventoryFull())
                        {
                            stopReason = Messages.En("inventory full");
                            RunLog($"iteration {completed + 1}: every carried container is full (Stop On Inventory Full)");
                            break;
                        }

                        // Tracked whatever the setting, so turning Stop At Nightfall on mid-run still knows
                        // whether this run has seen daylight.
                        bool? night = IsNightNow();
                        if (night == false) sawDaylight = true;
                        if (Plugin.StopAtNightfall.Value)
                        {
                            if (night == null)
                                RunLog($"iteration {completed + 1}: the time of day cannot be read, so Stop At Nightfall cannot fire this iteration");
                            else if (night == true && sawDaylight)
                            {
                                stopReason = Messages.En("night is falling");
                                RunLog($"iteration {completed + 1}: it is {CurrentMoment()} after daylight earlier in this run (Stop At Nightfall)");
                                break;
                            }
                        }

                        if (Plugin.StopAtSeasonChange.Value)
                        {
                            string seasonId = CurrentSeasonId();
                            if (seasonId == null)
                                RunLog($"iteration {completed + 1}: the season cannot be read, so Stop At Season Change cannot fire this iteration");
                            else if (startSeasonId == null) startSeasonId = seasonId; // unreadable at start: first reading is the baseline
                            else if (!string.Equals(seasonId, startSeasonId, StringComparison.Ordinal))
                            {
                                stopReason = Messages.En("the season changed");
                                RunLog($"iteration {completed + 1}: the season is now '{seasonId}', the run started in '{startSeasonId}' (Stop At Season Change)");
                                break;
                            }
                        }

                        // The longest span between two iteration starts so far is the worst case for the next one, so the
                        // run stops before an iteration that would pass the limit; the first iteration always runs.
                        int? nowTick = CurrentGlobalTick();
                        if (completed > 0 && nowTick.HasValue && lastIterStartTick.HasValue)
                            maxIterTicks = Mathf.Max(maxIterTicks, nowTick.Value - lastIterStartTick.Value);
                        float hourLimit = Plugin.StopAfterInGameHours.Value;
                        if (hourLimit > 0f && completed > 0)
                        {
                            if (!nowTick.HasValue || !startTick.HasValue || !ticksPerHour.HasValue)
                                RunLog($"iteration {completed + 1}: the game clock or day length cannot be read, so Stop After In-Game Hours cannot fire this iteration");
                            else
                            {
                                int elapsed = nowTick.Value - startTick.Value;
                                float limitTicks = hourLimit * ticksPerHour.Value;
                                if (elapsed + maxIterTicks > limitTicks)
                                {
                                    stopReason = Messages.En("reached the in-game time limit");
                                    RunLog($"iteration {completed + 1}: {elapsed} tick(s) since the run began plus up to {maxIterTicks} for the next iteration would pass the {hourLimit:0.##} in-game hour limit ({limitTicks:0.##} ticks at {ticksPerHour.Value:0.##} an hour) (Stop After In-Game Hours)");
                                    break;
                                }
                            }
                        }
                        if (nowTick.HasValue) lastIterStartTick = nowTick;

                        // Stop At Quantity before the dispatch: a run whose location already holds enough does not start.
                        string quantityStop = CheckQuantityStops(completed + 1, "before the dispatch");
                        if (quantityStop != null) { stopReason = quantityStop; break; }

                        if (refuelFor != null)
                        {
                            // The pin, exactly as Shift+F would fire it (same Captured, same funnel, no name search), except
                            // that Lock To Original Target does not hold it to the one card it was captured on: an Eat pin
                            // has eaten that card, and a refuel is meant to take the next of its kind.
                            var pin = pinned;
                            refuels++;
                            string pinDisplay = DisplayName(pin);
                            string pinKind = Messages.En("pinned {0}", KindLabel(pin.Kind));
                            Logger.LogInfo($"[Repeat] Refuel {refuels}/{Plugin.MaxRefuelsPerRun.Value}: {refuelFor}, firing the pinned '{pinDisplay}' before iteration {completed + 1}");
                            Plugin.ShowNotification(Messages.Text("Refuelling: {0}", pinDisplay));
                            dispatch.EncounterSeen = encounterSeen;
                            var refuel = DispatchAndWait(pin, completed, $"iteration {completed + 1}: refuel {refuels}", pinDisplay, pinKind, dispatch, ignoreLock: true);
                            while (refuel.MoveNext()) yield return refuel.Current;
                            encounterSeen = dispatch.EncounterSeen;
                            if (!dispatch.Finished)
                            {
                                if (dispatch.Dispatched)
                                {
                                    // The game ended or the player cancelled during the refuel: the same stop as mid-action.
                                    stopReason = dispatch.StopReason;
                                }
                                else
                                {
                                    // Never cleared here: the pin is the player's to change, and putting food back is enough.
                                    Logger.LogInfo($"[Repeat] Refuel: the pinned '{pinDisplay}' could not be dispatched - {dispatch.StopReason}");
                                    stopReason = Messages.En("{0} (pinned action unavailable)", refuelFor);
                                }
                                break;
                            }
                            // Stop After In-Game Hours: the refuel is not an iteration, so its game time must not become the
                            // span the next iteration is judged by (it still counts toward the run's elapsed total).
                            int? afterRefuelTick = CurrentGlobalTick();
                            if (afterRefuelTick.HasValue) lastIterStartTick = afterRefuelTick;
                            string afterRefuel = CheckStatThresholds(out bool _);
                            if (afterRefuel == refuelFor)
                            {
                                stopReason = Messages.En("{0} (refuel did not help)", refuelFor);
                                break;
                            }
                            Logger.LogInfo(afterRefuel == null
                                ? $"[Repeat] Refuel {refuels}: the stats are back over their floors; carrying on with iteration {completed + 1}"
                                : $"[Repeat] Refuel {refuels}: '{refuelFor}' is answered, but now '{afterRefuel}'; checking again before iteration {completed + 1}");
                            // Back to the top of the loop for the same iteration: cancel, pause, idle, Stop After Encounter
                            // (a refuel can draw one) and every limit are checked again before the dispatch.
                            si--;
                            continue;
                        }

                        // Driven by hand rather than yielded as a nested coroutine, so every frame it waits is a frame of
                        // this run exactly as when these lines were inline.
                        // Iteration hook (RunHooks.cs): after the refuel branch above, so a refuel's own spawns are outside the window.
                        RaiseIterationStarting(completed + 1, what);
                        dispatch.EncounterSeen = encounterSeen;
                        var step = DispatchAndWait(cap, completed, what, stepDisplay, stepKind, dispatch);
                        while (step.MoveNext()) yield return step.Current;
                        encounterSeen = dispatch.EncounterSeen;
                        if (!dispatch.Finished)
                        {
                            stopReason = dispatch.StopReason;
                            // A sequence names the step it could not dispatch; it is never skipped (a skipped "water" would
                            // harvest dry). A cancel or a game ending mid-step keeps its own reason, which code compares.
                            if (isSequence && !dispatch.Dispatched)
                                stopReason = Messages.En("step {0} of {1} ({2}): {3}", si + 1, steps.Count, stepDisplay, stopReason);
                            // A pin whose card or action is gone on its first iteration would fail the
                            // same way every time it is fired, so it is cleared and the stop says so. An
                            // unmet requirement (low stamina, a missing tool) can pass later: the pin stays.
                            if (!dispatch.Dispatched && fromPin && completed == 0 && dispatch.TargetGone && ReferenceEquals(cap, pinned))
                            {
                                pinned = null;
                                stopReason = Messages.En("{0} - pin cleared", stopReason);
                                RunLog("the pinned action's target is gone - pin cleared");
                            }
                            break;
                        }

                        // A repeat is done when its last step is: one step for a single action, every step for a sequence.
                        bool lastStep = si == steps.Count - 1;
                        if (lastStep)
                        {
                            completed++;
                            Plugin.ReportRunProgress(completed, count, unlimited);
                            if (isSequence) Plugin.ShowNotification(Messages.Text("Playing {0}: {1}", display, unlimited ? completed.ToString() : $"{completed}/{count}"));
                            else if (unlimited) Plugin.ShowNotification(Messages.Text("{0}: {1}", display, completed));
                            else if (count > 1) Plugin.ShowNotification(Messages.Text("{0}: {1}/{2}", display, completed, count));
                        }
                        int doneIteration = lastStep ? completed : completed + 1;
                        RaiseIterationDone(doneIteration, lastStep, what);

                        // Stop At Quantity as soon as the iteration finishes, so a run that has just made enough ends without
                        // its Iteration Delay, and a last iteration that makes enough says so rather than "Complete". Ahead of
                        // the tool check: when both happen at once, having enough is what the run was for.
                        string quantityDone = CheckQuantityStops(doneIteration, "after it finished");
                        if (quantityDone != null) { stopReason = quantityDone; break; }

                        if (cap.Kind == FunnelKind.CardOnCard && Plugin.StopOnToolBreak.Value && GivenToolBroke(cap, out string brokeHow))
                        {
                            // Use Spare Tool On Break: the next iteration's ResolveCard already rolls onto another card of
                            // the captured UID (the broken one is no longer live or no longer matches it), so carrying on
                            // only needs one to be there.
                            var spare = Plugin.UseSpareToolOnBreak.Value ? FindSpareTool(cap) : null;
                            if (spare == null)
                            {
                                stopReason = Messages.En("tool changed");
                                RunLog($"iteration {doneIteration}: drag-drop tool '{cap.GivenUid}' {brokeHow} (Stop On Tool Break)");
                                break;
                            }
                            RunLog($"iteration {doneIteration}: drag-drop tool '{cap.GivenUid}' {brokeHow}; tool changed: carrying on with a spare '{CardName(spare)}' (Use Spare Tool On Break)");
                        }

                    }
                    // A step that broke out of the loop above stopped the run (stopReason is set on every such path).
                    if (si < steps.Count) break;

                    // Iteration Delay: a real-time pause before the next iteration, none after the
                    // last. Frame yields + unscaled time like the waits above (never WaitForSeconds),
                    // and a phase of its own, so it never counts toward Action Completion Timeout.
                    float delay = Plugin.IterationDelaySeconds.Value;
                    if (delay > 0f && i + 1 < limit)
                    {
                        bool gameEnded = false;
                        float paused = 0f;
                        while (paused < delay && !cancelRequested)
                        {
                            yield return null;
                            if (GameManager.Instance == null) { gameEnded = true; break; }
                            paused += Time.unscaledDeltaTime;
                            if (!encounterSeen) encounterSeen = EncounterSeenNow(completed, "during the Iteration Delay");
                        }
                        if (gameEnded)
                        {
                            stopReason = GameEndedReason;
                            RunLog($"iteration {completed}: GameManager went away during the Iteration Delay (quit to menu or load?)");
                            break;
                        }
                        // A cancel during the pause is caught by the check at the top of the loop.
                        if (!cancelRequested)
                            RunLog($"iteration {completed}: paused {paused:0.0}s before the next iteration (Iteration Delay)");
                    }
                }
            }
            finally
            {
                isRepeating = false;
                pauseRequested = false;
                pausedNow = false;
                Plugin.EndRunProgress();
            }

            // A cancel that came from a new game starting (OnRunStart) is the game ending, not the player's key:
            // one stop line, worded as the quit-to-menu path words it.
            if (endedByNewRun && stopReason == CancelledReason) stopReason = GameEndedReason;

            string progress = unlimited ? completed.ToString() : $"{completed}/{count}";
            if (stopReason == null)
            {
                // In unlimited mode, running out of iterations is the cap, not a clean finish.
                if (unlimited)
                {
                    Plugin.ShowNotification(Messages.Text("Stopped - reached the {0}-iteration limit ({1})", limit, progress));
                    Logger.LogInfo($"[Repeat] Stopped after {progress}: hit MaxUnboundedIterations ({limit})");
                }
                else
                {
                    Plugin.ShowNotification(Messages.Text("Complete: {0}", progress));
                    Logger.LogInfo($"[Repeat] Complete: {progress}");
                }
            }
            else
            {
                // The toast is translated; the log keeps the English reason, which harness scenarios assert.
                Plugin.ShowNotification(Messages.Text("Stopped - {0} ({1})", Messages.Shown(stopReason), progress));
                Logger.LogInfo($"[Repeat] Stopped after {progress}: {stopReason}");
            }

            // Play Completion Sound: only for a run that did something and ended on its own. A cancel
            // is the player's own keypress, and after "game ended" there is no run left to hear it in
            // (after a load a GameManager exists again, so the Instance test alone would not say so).
            if (Plugin.PlayCompletionSound.Value && completed > 0 && stopReason != CancelledReason && stopReason != GameEndedReason && GameManager.Instance != null)
                PlayCompletionSound();
        }

        /// <summary>
        /// What one <see cref="DispatchAndWait"/> did. An iterator cannot return a value, so the caller hands one in
        /// and reads it once the iterator is exhausted.
        /// </summary>
        private sealed class DispatchOutcome
        {
            public bool EncounterSeen;  // in and out: the run's Stop After Encounter latch, set by the waits
            public bool Dispatched;     // TryDispatch accepted the action
            public bool Finished;       // it was dispatched and the game is idle again: the iteration counts
            public bool TargetGone;     // refused because the card or action no longer exists (TryDispatch)
            public string StopReason;   // when not Finished: the refusal, GameEndedReason or CancelledReason
        }

        /// <summary>
        /// Dispatch <paramref name="cap"/> once through the game's own funnel (TryDispatch) and wait, frame by frame,
        /// for the game to finish it. Shared by a run's own dispatch and by a refuel from the pinned action (Feature
        /// 17). <paramref name="what"/> opens each log line ("iteration 3", "iteration 3: refuel 1").
        /// </summary>
        private static IEnumerator DispatchAndWait(Captured cap, int completed, string what, string display, string kind, DispatchOutcome result, bool ignoreLock = false)
        {
            result.Dispatched = false;
            result.Finished = false;
            result.TargetGone = false;
            result.StopReason = null;

            bool accepted;
            Coroutine running;
            string failReason;
            bool targetGone;
            lockSuspended = ignoreLock;
            try { accepted = TryDispatch(cap, completed, out running, out failReason, out targetGone); }
            finally { lockSuspended = false; }

            // Requirements-unmet hook (RunHooks.cs): only a refusal site that found a subscriber leaves pendingUnmet set, so with
            // nothing subscribed this block is skipped and the refusal below is exactly as before. A handler may ask for ONE
            // retry of this dispatch; the retry runs with the hook suppressed, so it can neither raise the event again nor loop.
            var unmet = pendingUnmet;
            pendingUnmet = null;
            if (!accepted && unmet != null)
            {
                unmet.Iteration = completed + 1;
                unmet.What = what;
                unmet.ActionName = display;
                if (unmet.Reason == null) unmet.Reason = failReason;
                if (RaiseRequirementsUnmet(unmet))
                {
                    RunLog($"{what}: refused ({failReason}); a requirements-unmet handler moved something and asked for one retry");
                    // Let the move land: frame yields and unscaled time, never WaitForSeconds (CLAUDE.md Coroutine Timing),
                    // bounded by Action Completion Timeout like every other wait in a run.
                    yield return null;
                    float settle = 0f;
                    while (!IsGameIdle() && settle < Plugin.ActionCompletionTimeout.Value && !cancelRequested)
                    {
                        yield return null;
                        if (GameManager.Instance == null) break;
                        settle += Time.unscaledDeltaTime;
                    }
                    if (GameManager.Instance == null)
                    {
                        result.StopReason = GameEndedReason;
                        RunLog($"{what}: GameManager went away before the retry (quit to menu or load?)");
                        yield break;
                    }
                    if (cancelRequested) { result.StopReason = CancelledReason; yield break; }
                    if (!IsGameIdle())
                    {
                        RunLog($"{what}: the game did not settle within {settle:0.0}s after the handler's move; no retry");
                    }
                    else
                    {
                        lockSuspended = ignoreLock;
                        unmetSuppressed = true;
                        try { accepted = TryDispatch(cap, completed, out running, out failReason, out targetGone); }
                        finally { lockSuspended = false; unmetSuppressed = false; pendingUnmet = null; }
                        RunLog(accepted ? $"{what}: the retry was accepted" : $"{what}: the retry was refused too - {failReason}");
                    }
                }
            }
            if (!accepted)
            {
                result.StopReason = failReason;
                result.TargetGone = targetGone;
                RunLog($"{what}: dispatch refused - {failReason}");
                yield break;
            }
            result.Dispatched = true;
            RunLog($"{what}: dispatched '{display}' ({kind})");

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
                if (!result.EncounterSeen) result.EncounterSeen = EncounterSeenNow(completed + 1, "during the action");
            }
            // Phase 2: wait for the action (and any confirm dialog) to finish.
            while (!gameEnded && !IsGameIdle() && !cancelRequested)
            {
                yield return null;
                if (GameManager.Instance == null) { gameEnded = true; break; }
                if (!result.EncounterSeen) result.EncounterSeen = EncounterSeenNow(completed + 1, "during the action");
            }
            if (gameEnded)
            {
                result.StopReason = GameEndedReason;
                RunLog($"{what}: GameManager went away mid-action (quit to menu or load?)");
                yield break;
            }
            if (cancelRequested) { result.StopReason = CancelledReason; yield break; }
            result.Finished = true;
        }

        /// <summary>
        /// Vanilla's "cooking done" chime (SoundManager.DefaultCookingComplete), played through the
        /// same call vanilla makes when a cooking recipe finishes. DefaultProgressFull was the first
        /// choice, but on EA 0.68b it and every other Default*Full clip are unassigned (null) on the
        /// live SoundManager (harness probe 2026-09-28), so it is only a fallback in case an update
        /// assigns it and drops the cooking clip.
        /// </summary>
        private static void PlayCompletionSound()
        {
            try
            {
                var sm = MBSingleton<SoundManager>.Instance;
                if (sm == null || !sm)
                {
                    Logger.LogDebug("[Repeat] Completion sound skipped: the game's SoundManager is missing");
                    return;
                }
                var clip = sm.DefaultCookingComplete != null ? sm.DefaultCookingComplete : sm.DefaultProgressFull;
                if (clip == null)
                {
                    Logger.LogDebug("[Repeat] Completion sound skipped: neither DefaultCookingComplete nor DefaultProgressFull has a clip");
                    return;
                }
                sm.PerformSingleSound(clip, _WithRandomPitch: false, _WithRandomVolume: false);
                Logger.LogDebug($"[Repeat] Completion sound played ({clip.name})");
            }
            catch (Exception ex) { Logger.LogDebug($"[Repeat] Completion sound failed: {ex}"); }
        }

        /// <summary>The season the game currently reports (GameManager.CurrentSeason, refreshed every tick), or null.</summary>
        private static string CurrentSeasonId()
        {
            try
            {
                var gm = GameManager.Instance;
                if (gm == null) return null;
                var season = gm.CurrentSeason;
                return season != null && !string.IsNullOrEmpty(season.SeasonID) ? season.SeasonID : null;
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"[Repeat] CurrentSeasonId read failed: {ex}");
                return null;
            }
        }

        /// <summary>
        /// True from dusk until dawn, false in daylight, null when the clock cannot be read. Uses the
        /// game's own moment of day (Season.GetCurrentMomentOfDay, computed live from the clock); the
        /// season's day starts at dawn, so every moment from Dusk on (Dusk, EarlyNight, Midnight,
        /// LateNight) is evening or night.
        /// </summary>
        private static bool? IsNightNow()
        {
            try
            {
                var gm = GameManager.Instance;
                if (gm == null) return null;
                var season = gm.CurrentSeason;
                if (season == null) return null;
                return (int)season.GetCurrentMomentOfDay >= (int)MomentsOfDay.Dusk;
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"[Repeat] IsNightNow read failed: {ex}");
                return null;
            }
        }

        /// <summary>The game's global tick (CurrentTickInfo.z, whole ticks since day 1 began), or null when it cannot be read.</summary>
        private static int? CurrentGlobalTick()
        {
            try
            {
                var gm = GameManager.Instance;
                if (gm == null) return null;
                return gm.CurrentTickInfo.z;
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"[Repeat] CurrentGlobalTick read failed: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Game ticks in one in-game hour: DaySettings.DailyPoints / 24 (W0.6: DailyPoints 96 on EA 0.68b, one tick 15
        /// minutes), never a literal 4. Null when it cannot be read or is not positive.
        /// </summary>
        private static float? TicksPerHour()
        {
            try
            {
                var gm = GameManager.Instance;
                if (gm == null) return null;
                int daily = gm.DaySettings.DailyPoints;
                if (daily <= 0)
                {
                    Logger.LogDebug($"[Repeat] TicksPerHour: DaySettings.DailyPoints is {daily}");
                    return null;
                }
                return daily / 24f;
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"[Repeat] TicksPerHour read failed: {ex}");
                return null;
            }
        }

        private static string CurrentMoment()
        {
            try
            {
                var gm = GameManager.Instance;
                var season = gm != null ? gm.CurrentSeason : null;
                return season != null ? season.GetCurrentMomentOfDay.ToString() : "?";
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"[Repeat] CurrentMoment read failed: {ex}");
                return "?";
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

        private static bool closeButtonWarned;
        private static readonly Vector3[] closeButtonCorners = new Vector3[4];

        /// <summary>
        /// The open inspection popup's close "x" as an IMGUI rect (top-left origin, pixels), so the count indicator can keep
        /// clear of it. False when there is no popup, the button is hidden (an unclosable popup), or it cannot be read.
        /// </summary>
        public static bool TryGetPopupCloseButtonRect(out Rect guiRect)
        {
            guiRect = default;
            try
            {
                var g = MBSingleton<GraphicsManager>.Instance;
                if (g == null || !g) return false;
                var popup = g.CurrentInspectionPopup;
                if (popup == null || !popup) return false;
                var button = popup.CloseButton;
                if (button == null || !button || !button.activeInHierarchy) return false;
                var rt = button.transform as RectTransform;
                if (rt == null) return false;
                var canvas = rt.GetComponentInParent<Canvas>();
                if (canvas != null) canvas = canvas.rootCanvas;
                Camera cam = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                rt.GetWorldCorners(closeButtonCorners);
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                for (int i = 0; i < 4; i++)
                {
                    Vector2 p = RectTransformUtility.WorldToScreenPoint(cam, closeButtonCorners[i]);
                    minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                    minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
                }
                // Screen space has its origin bottom-left, IMGUI top-left.
                guiRect = new Rect(minX, Screen.height - maxY, maxX - minX, maxY - minY);
                return true;
            }
            catch (Exception ex)
            {
                if (!closeButtonWarned)
                {
                    closeButtonWarned = true;
                    Logger.LogDebug($"[HUD] Close button position read failed (count indicator keeps its default place): {ex}");
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

        /// <param name="targetGone">True when the refusal means the captured card or action no longer
        /// exists to act on (as opposed to an unmet requirement that can pass later); a pinned action
        /// that fails this way on its first iteration is cleared.</param>
        private static bool TryDispatch(Captured cap, int completed, out Coroutine running, out string failReason, out bool targetGone)
        {
            running = null;
            failReason = null;
            targetGone = false;
            pendingUnmet = null;
            try
            {
                switch (cap.Kind)
                {
                    case FunnelKind.Action: return DispatchAction(cap, completed, ref running, ref failReason, ref targetGone);
                    case FunnelKind.StackAction: return DispatchStack(cap, completed, ref running, ref failReason, ref targetGone);
                    case FunnelKind.CardOnCard: return DispatchCardOnCard(cap, completed, ref running, ref failReason, ref targetGone);
                    case FunnelKind.GroupAction: return DispatchGroup(cap, completed, ref running, ref failReason, ref targetGone);
                }
                failReason = Messages.En("unknown action kind");
                return false;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[Repeat] Dispatch error: {ex.InnerException?.ToString() ?? ex.ToString()}");
                failReason = Messages.En("internal error (see log)");
                // A captured reference that throws will throw again: treat it as gone, so a pin
                // built on it is cleared rather than failing the same way on every fire.
                targetGone = true;
                return false;
            }
        }

        private static bool DispatchAction(Captured cap, int completed, ref Coroutine running, ref string failReason, ref bool targetGone)
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
                bool locked = LockedTo(cap.ReceivingCard);
                card = ResolveCard(cap.ReceivingCard, cap.ReceivingUid, allowSibling: !locked);
                if (card != null)
                {
                    action = ResolveActionOnCard(card, cap);
                    if (action == null && cap.Action != null)
                    {
                        // Popup-owned runtime action, not in DismantleActions but still dispatchable
                        // against the live card. (Blueprint Build is not one: it is never captured.)
                        action = cap.Action;
                        RunLog($"using the captured action object directly for '{cap.ActionName}' (not in the live card's DismantleActions)");
                    }
                }
                else if (!locked)
                {
                    // Receiving card left the board (travel, consumed target): find the same action
                    // on any live card — e.g. the direction action on the NEW location card.
                    if (FindActionAnywhere(cap, out card, out action))
                        RunLog($"target changed - found '{cap.ActionName}' on '{CardName(card)}'");
                }
                else
                {
                    action = null;
                }

                if (card == null || action == null)
                {
                    targetGone = true;
                    if (card == null && locked)
                    {
                        RunLog($"the exact card '{cap.ReceivingUid}' is no longer on this board (Lock To Original Target)");
                        failReason = LockedTargetGone;
                        return false;
                    }
                    RunLog(card == null
                        ? $"no live card '{cap.ReceivingUid}' on this board and no other card offers '{cap.ActionName}'"
                        : $"live card '{CardName(card)}' no longer offers '{cap.ActionName}'");
                    failReason = completed > 0 ? Messages.En("no more '{0}' targets", DisplayName(cap)) : Messages.En("target card not found");
                    return false;
                }
                cap.ReceivingCard = card; // keep instance affinity for the next iteration
            }

            if (!ActionAvailable(action, card, null, ref failReason)) return false;

            running = GameManager.PerformAction(action, card, _FastMode: false, InGameNPCOrPlayer.PlayerAgent);
            if (running == null) { failReason = Messages.En("the game rejected the action"); return false; }
            return true;
        }

        private static bool DispatchStack(Captured cap, int completed, ref Coroutine running, ref string failReason, ref bool targetGone)
        {
            bool locked = LockedTo(cap.ReceivingCard);
            var card = ResolveCard(cap.ReceivingCard, cap.ReceivingUid, allowSibling: !locked);
            if (card == null)
            {
                targetGone = true;
                if (locked)
                {
                    RunLog($"the exact card '{cap.ReceivingUid}' is no longer on this board (Lock To Original Target)");
                    failReason = LockedTargetGone;
                    return false;
                }
                RunLog($"no live card '{cap.ReceivingUid}' left on this board for the stack action");
                failReason = completed > 0 ? Messages.En("stack used up") : Messages.En("target card not found");
                return false;
            }
            cap.ReceivingCard = card;
            var action = ResolveActionOnCard(card, cap) ?? cap.Action;
            if (action == null) { targetGone = true; failReason = Messages.En("action no longer available"); return false; }

            if (!ActionAvailable(action, card, null, ref failReason)) return false;

            // True stack semantics when the card still sits in a board slot; otherwise degrade
            // to one card per iteration.
            var slot = card.CurrentSlot;
            running = slot != null
                ? GameManager.PerformStackAction(action, slot, cap.StackLiquids, InGameNPCOrPlayer.PlayerAgent)
                : GameManager.PerformAction(action, card, _FastMode: false, InGameNPCOrPlayer.PlayerAgent);
            if (running == null) { failReason = Messages.En("the game rejected the action"); return false; }
            return true;
        }

        private static bool DispatchCardOnCard(Captured cap, int completed, ref Coroutine running, ref string failReason, ref bool targetGone)
        {
            bool locked = LockedTo(cap.ReceivingCard);
            // A liquid target that is still here is still the card the player poured into, even when the liquid in it
            // changed model in place (froze, or any other swap): keep it, so the transfer guard below reads its new
            // liquid. Re-finding it by the captured UniqueID matched only cards of the OLD liquid and poured into one of
            // them instead (harness T2.164, 2026-10-05: wine frozen in the target, both repeated pours went elsewhere).
            var receiving = cap.IsLiquidTransfer && IsLive(cap.ReceivingCard) && InPlayerEnv(cap.ReceivingCard)
                ? cap.ReceivingCard
                : ResolveCard(cap.ReceivingCard, cap.ReceivingUid, allowSibling: !locked);
            if (receiving == null)
            {
                targetGone = true;
                if (locked)
                {
                    RunLog($"the exact receiving card '{cap.ReceivingUid}' is no longer on this board (Lock To Original Target)");
                    failReason = LockedTargetGone;
                    return false;
                }
                RunLog($"no live receiving card '{cap.ReceivingUid}' on this board for the drag-drop");
                failReason = completed > 0 ? Messages.En("no more targets") : Messages.En("target card not found");
                return false;
            }
            // The dragged card is the source, not the target: it always rolls onto the next of its
            // kind, lock or no lock.
            // The dragged card comes from the container the previous one came from when it still can (the player's own
            // drag on the first iteration), then from any card of that kind in the player's environment.
            // The target's own liquid is never the source: a receiving container's ContainedLiquid matches the
            // given liquid's UniqueID, and pouring it into itself is a no-op the run would count as an iteration.
            var receivingLiquid = receiving.ContainedLiquid;
            var givenExclude = receivingLiquid != null && receivingLiquid ? new List<InGameCardBase> { receivingLiquid } : null;
            var given = ResolveCard(cap.GivenCard, cap.GivenUid, receiving, givenExclude,
                preferContainer: cap.GivenContainer, hasPreference: cap.HasGivenContainer);
            if (given == null)
            {
                targetGone = true;
                RunLog($"no live given card '{cap.GivenUid}' left to drag onto '{CardName(receiving)}'");
                failReason = completed > 0 ? Messages.En("source used up") : Messages.En("source card not found");
                NoteSourceUsedUp(cap, receiving);
                return false;
            }
            var action = cap.Action as CardOnCardAction;
            if (action == null) { targetGone = true; failReason = Messages.En("action no longer available"); return false; }

            // Stop Below Tool Durability: the dragged card only. CurrentUsagePercent is the usage bar as a 0 to 1
            // share of the card's full usage durability, and 1 for a card with no usage stat or a maximum of 0
            // (.decomp/InGameCardBase.cs), so a dragged card that does not wear never trips this. Not targetGone:
            // the card is still there, so a pin survives the stop as it does any unmet requirement.
            int toolFloor = Plugin.StopBelowToolDurability != null ? Plugin.StopBelowToolDurability.Value : 0;
            if (toolFloor > 0)
            {
                float toolPercent = given.CurrentUsagePercent * 100f;
                RunLog($"tool durability: '{CardName(given)}' at {toolPercent:0.#}% of its full durability (floor {toolFloor}%)");
                if (toolPercent < toolFloor)
                {
                    failReason = Messages.En("tool durability below {0}%", toolFloor);
                    return false;
                }
            }

            // A programmatic move does not go through the vanilla drag path, so its
            // CannotBeTransferred gate never runs for us (CLAUDE.md §Programmatic Card
            // Movement). Re-check it here, mirroring InGameCardBase.CanTransferLiquids.
            if (cap.IsLiquidTransfer && (LiquidTransferBlocked(given) || LiquidTransferBlocked(receiving)))
            {
                failReason = Messages.En("liquid can no longer be transferred");
                return false;
            }

            if (!ActionAvailable(action, receiving, given, ref failReason)) return false;

            // Track the instances actually used so tool-transform detection and the next
            // iteration's resolution follow the live cards, not the frame-0 originals.
            cap.ReceivingCard = receiving;
            cap.GivenCard = given;
            // Re-read from the live card each iteration, never trusted across one (the card sits wherever the game last put it).
            cap.GivenContainer = ContainerOf(given);
            cap.HasGivenContainer = true;
            // Read now for Stop On Tool Break: a broken tool's model is cleared once the game pools the card, so it
            // cannot be read after the iteration.
            var givenChange = action.GivenCardChanges.ModType;
            cap.GivenConsumedByAction = givenChange == CardModifications.Destroy || givenChange == CardModifications.Transform;
            var usage = given.CardModel.UsageDurability;
            cap.GivenBreaksAtZero = usage != null && usage.HasActionOnZero && usage.OnZero != null && usage.OnZero.DestroysReceivingCard;

            running = GameManager.PerformCardOnCardActionStack(action, new List<InGameCardBase> { given },
                receiving, cap.IsLiquidTransfer, InGameNPCOrPlayer.PlayerAgent);
            if (running == null) { failReason = Messages.En("the game rejected the action"); return false; }
            return true;
        }

        /// <summary>
        /// Rebuild the group every iteration exactly as the game's group button does:
        /// GroupInventoryAction.TryAddingCardToGroupActionList over the live members makes fresh
        /// action copies, drops any member whose requirements fail, and gives the FIRST member the
        /// group's time cost plus the flavour and spice totals of the cards actually present.
        /// Replaying the click-time copies instead re-applied their stale totals (once per card in
        /// Per-Card mode, with spice flavour re-added on every call) and skipped the member checks.
        /// Whole-group mode (default): every live captured member, one sweep per iteration.
        /// Per-Card Group Repeat: the first captured member not yet processed this run whose
        /// requirements pass, as a group of one, so one iteration = one card and the run ends with
        /// "no more targets" once the group is exhausted.
        /// </summary>
        private static bool DispatchGroup(Captured cap, int completed, ref Coroutine running, ref string failReason, ref bool targetGone)
        {
            bool perCard = PerCardGroupMode;
            bool locked = LockToOriginal;
            var cards = new List<InGameCardBase>();
            var actions = new List<DismantleCardAction>();
            // Cards already tried this iteration (plus, per card, those processed this run), so a
            // consumed member's same-UID fallback in ResolveCard never lands on one card twice.
            var tried = perCard ? new List<InGameCardBase>(perCardDispatched) : new List<InGameCardBase>();
            int liveMembers = 0;
            string blockedMessage = null;
            for (int i = 0; i < cap.GroupCards.Count; i++)
            {
                var c = ResolveCard(cap.GroupCards[i], i < cap.GroupUids.Count ? cap.GroupUids[i] : null, null, tried, allowSibling: !locked || ReferenceEquals(cap.GroupCards[i], null));
                if (c == null) continue;
                // A member rebuilt from the saved library (null) keeps the card it found, as a capture keeps its own.
                if (ReferenceEquals(cap.GroupCards[i], null)) cap.GroupCards[i] = c;
                tried.Add(c);
                liveMembers++;
                string msg = RefreshGroupCandidates(cap.GroupDef, c);
                if (blockedMessage == null) blockedMessage = msg;
                cap.GroupDef.TryAddingCardToGroupActionList(c, InGameNPCOrPlayer.PlayerAgent, ref cards, ref actions);
                if (perCard && cards.Count > 0) break;
            }
            if (cards.Count == 0)
            {
                if (liveMembers == 0)
                {
                    targetGone = true;
                    RunLog(perCard
                        ? $"all {cap.GroupCards.Count} captured group member(s) already processed or gone ({perCardDispatched.Count} dispatched this run)"
                        : $"none of the {cap.GroupCards.Count} captured group member(s) is live on this board");
                    failReason = completed > 0 ? Messages.En("no more targets") : Messages.En("target cards not found");
                }
                else
                {
                    failReason = string.IsNullOrEmpty(blockedMessage) ? Messages.En("requirements no longer met") : blockedMessage;
                    RunLog($"{liveMembers} live group member(s), but the game's group builder accepted none for '{cap.ActionName}': {failReason}");
                }
                return false;
            }

            running = GameManager.PerformGroupInventoryAction(cards, actions, _FastMode: false, InGameNPCOrPlayer.PlayerAgent);
            if (running == null) { failReason = Messages.En("the game rejected the action"); return false; }
            if (perCard) perCardDispatched.Add(cards[0]);
            return true;
        }

        /// <summary>
        /// The builder's SimpleConditionsCheck reads state only CollectActionModifiers maintains, so
        /// refresh every action on the card that the builder may pick (the group's action tag or an
        /// alternate) against the live card first: the same staleness ActionAvailable guards against
        /// for single actions. Returns the first action-modifier block message, or null.
        /// </summary>
        private static string RefreshGroupCandidates(GroupInventoryAction def, InGameCardBase c)
        {
            var das = c.DismantleActions;
            if (das == null) return null;
            string blocked = null;
            for (int i = 0; i < das.Length; i++)
            {
                var a = das[i];
                if (a == null || !IsGroupCandidate(def, a)) continue;
                a.CollectActionModifiers(c, null, null);
                if (blocked == null && !string.IsNullOrEmpty(a.ActionBlockedMessage)) blocked = a.ActionBlockedMessage;
            }
            return blocked;
        }

        /// <summary>Mirrors the tag test in GroupInventoryAction.TryAddingCardToGroupActionList.</summary>
        private static bool IsGroupCandidate(GroupInventoryAction def, DismantleCardAction a)
        {
            if (a.HasActionTag(def.Action)) return true;
            if (def.AlternateActionTags == null) return false;
            for (int j = 0; j < def.AlternateActionTags.Length; j++)
                if (a.HasActionTag(def.AlternateActionTags[j])) return true;
            return false;
        }

        /// <summary>
        /// The same availability test the UI runs to enable a button: refresh the action's
        /// modifier state against the live cards FIRST (SimpleConditionsCheck reads fields
        /// only CollectActionModifiers maintains — stale state was the 1.x "Action
        /// unavailable" bug), then check. Fails CLOSED: nothing downstream re-checks (the game's
        /// ActionRoutine runs no condition test, EA 0.68b), so an action whose check throws would
        /// otherwise run with its requirements unchecked. Both game checks accept a null card, so
        /// cardless actions (Rest, time skip) take the same path.
        /// </summary>
        private static bool ActionAvailable(CardAction action, InGameCardBase card, InGameCardBase given, ref string failReason)
        {
            try
            {
                action.CollectActionModifiers(card, given, null);
                if (!action.SimpleConditionsCheck(card, InGameNPCOrPlayer.PlayerAgent))
                {
                    string msg = action.ActionBlockedMessage;
                    failReason = string.IsNullOrEmpty(msg) ? Messages.En("requirements no longer met") : msg;
                    RunLog($"the game's availability check refused '{action.ActionName.DefaultText}' on '{CardName(card)}': {failReason}");
                    NoteRequirementsUnmet(action, card, given, failReason);
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                string type = ex.GetType().Name;
                if (availabilityFailuresWarned.Add(type))
                    Logger.LogWarning($"[Repeat] The game's availability check threw on '{action.ActionName.DefaultText}', so the run stops instead of dispatching it unchecked. Later {type}s from this check go to Verbose Run Diagnostics only. {ex}");
                else
                    RunLog($"availability check threw ({type}) on '{action.ActionName.DefaultText}' - run stopped");
                failReason = Messages.En("availability check failed (see log)");
                return false;
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
        /// Use Spare Tool On Break: a live card of the captured dragged UID, other than the one that just broke, in the
        /// player's current environment. Asks ResolveCard itself (AllCards through InPlayerEnv and IsLive, never the bare
        /// list, which also holds cards from other places), so "there is a spare" means exactly what the next
        /// iteration's resolution will find.
        /// </summary>
        private static InGameCardBase FindSpareTool(Captured cap)
            => string.IsNullOrEmpty(cap.GivenUid) ? null : ResolveCard(null, cap.GivenUid, exclude: cap.GivenCard);

        /// <summary>
        /// Resolve a replay target from the cards in the player's CURRENT environment. Always
        /// scans AllCards rather than trusting the captured reference: a stale ref can stay
        /// Unity-alive after travel and would otherwise replay onto a card that is no longer in
        /// play. AllCards alone is NOT this board - travel re-adds background cards whose
        /// environment is elsewhere (every NPC's card, cards in an allied NPC's location), so
        /// each entry must also pass InPlayerEnv; a ContainedLiquid inherits its container's
        /// verdict. Prefers the exact instance the player used; falls back to another live card
        /// of the same UniqueID unless allowSibling is false (Lock To Original Target), which only
        /// narrows the result set, so the environment filter above still applies unchanged.
        /// With hasPreference, the first such sibling whose container is preferContainer (null meaning
        /// loose on a board or hand row) is returned ahead of the first sibling in AllCards order, so a
        /// repeated drag-drop keeps drawing from the place the previous dragged card came from; a
        /// preferContainer that is no longer live is no preference. Only the ORDER of the candidates
        /// changes: every card this method returned before is still returned when nothing matches the
        /// preference, and the environment filter stays in front of every candidate.
        /// </summary>
        private static InGameCardBase ResolveCard(InGameCardBase original, string uid, InGameCardBase exclude = null, List<InGameCardBase> excludeList = null, bool allowSibling = true,
            InGameCardBase preferContainer = null, bool hasPreference = false)
        {
            var gm = GameManager.Instance;
            if (gm == null || gm.AllCards == null) return null;
            // ReferenceEquals, never ==: a container that was destroyed since it was read compares equal to null under
            // Unity's overload, which would turn "that bag is gone" into "prefer loose cards".
            bool prefer = hasPreference && allowSibling && (ReferenceEquals(preferContainer, null) || IsLive(preferContainer));
            InGameCardBase fallback = null;
            InGameCardBase preferred = null;
            for (int i = 0; i < gm.AllCards.Count; i++)
            {
                var c = gm.AllCards[i];
                if (!InPlayerEnv(c)) continue;
                var hit = MatchCard(c, original, uid, exclude, excludeList);
                if (hit == null)
                {
                    hit = MatchCard(c.ContainedLiquid, original, uid, exclude, excludeList);
                    // A liquid takes its container's lock: Utility Belt locks the waterskin, not the water inside it,
                    // so MatchCard's own lock test never sees it. The exact instance the player used stays eligible.
                    if (hit != null && !ReferenceEquals(hit, original) && UtilityBeltLockBridge.IsLocked(c)) hit = null;
                }
                if (hit == null) continue;
                if (ReferenceEquals(hit, original)) return hit; // exact instance the player used
                if (fallback == null && allowSibling) fallback = hit;
                if (prefer && preferred == null && ReferenceEquals(ContainerOf(hit), preferContainer)) preferred = hit;
            }
            return preferred != null ? preferred : fallback;
        }

        /// <summary>
        /// The container a card sits in right now, or null when it is loose on a board or hand row. A destroyed
        /// container reads as null here, as it does in the game's own `if (!CurrentContainer)` checks, so the result
        /// is either a live card or a real null and can be compared with ReferenceEquals.
        /// </summary>
        private static InGameCardBase ContainerOf(InGameCardBase c)
        {
            if (c == null || !c) return null;
            var k = c.CurrentContainer;
            return k ? k : null;
        }

        /// <summary>
        /// True when the card is in the player's current environment. Carried cards qualify: travel
        /// moves them and their contents to the new environment (InGameCardBase.UpdatePlayerEnvironment).
        /// </summary>
        private static bool InPlayerEnv(InGameCardBase c) => c != null && c && c.CardEnvironment.MatchesPlayerEnv;

        private static InGameCardBase MatchCard(InGameCardBase c, InGameCardBase original, string uid, InGameCardBase exclude, List<InGameCardBase> excludeList)
        {
            if (!IsLive(c) || ReferenceEquals(c, exclude)) return null;
            if (excludeList != null && excludeList.Contains(c)) return null;
            if (!string.IsNullOrEmpty(uid))
            {
                if (!string.Equals(c.CardModel.UniqueID, uid, StringComparison.Ordinal)) return null;
                // A card Utility Belt has locked is never picked up as a NEW target or source (this is where every
                // replay re-resolves one). The exact instance the player used stays eligible, as it was chosen by hand.
                if (!ReferenceEquals(c, original) && UtilityBeltLockBridge.IsLocked(c)) return null;
                return c;
            }
            return ReferenceEquals(c, original) ? c : null;
        }

        private static CardAction ResolveActionOnCard(InGameCardBase card, Captured cap)
            => FindInActions(card.DismantleActions, cap);

        private static CardAction FindInActions(DismantleCardAction[] actions, Captured cap, bool allowNameMatch = true)
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

            if (allowNameMatch && !string.IsNullOrEmpty(cap.ActionName))
            {
                for (int i = 0; i < actions.Length; i++)
                    if (actions[i] != null && string.Equals(actions[i].ActionName.DefaultText, cap.ActionName, StringComparison.OrdinalIgnoreCase))
                        return actions[i];
            }
            return null;
        }

        /// <summary>
        /// Cards whose actions may be matched by display name alone when the original target is
        /// gone: location and environment cards, where travel lands on a new card carrying the
        /// same direction action. Everywhere else a name is shared by unrelated cards (nearly every
        /// vanilla food has an action named "Eat", each under its own LocalizationKey), so a
        /// name-only match could move an Eat repeat on to a different, even poisonous, food.
        /// </summary>
        private static bool AllowsNameOnlyMatch(InGameCardBase c)
        {
            var type = c.CardModel.CardType;
            return type == CardTypes.Explorable || type == CardTypes.Environment;
        }

        /// <summary>
        /// Fallback when the captured receiving card no longer exists anywhere: find the same
        /// action on any live card in the player's environment, by identity or LocalizationKey on
        /// any card, and by display name only on location/environment cards (AllowsNameOnlyMatch).
        /// Covers travel (direction action on the new location card) and same-kind respawned targets.
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
                if (!IsLive(c) || !InPlayerEnv(c)) continue;
                var a = FindInActions(c.DismantleActions, cap, allowNameMatch: AllowsNameOnlyMatch(c));
                // A card Utility Belt has locked is never the stand-in target (this fallback is what a run reaches once
                // ResolveCard skipped the last locked sibling).
                if (a != null && UtilityBeltLockBridge.IsLocked(c)) continue;
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
        /// First stat limit crossed, or null. The three fixed vanilla floors are checked first, then
        /// every Extra Stat Thresholds entry in the order written; the generic list is additive and
        /// goes through the same CheckThreshold as the fixed three.
        /// </summary>
        /// <param name="ceiling">True when the limit crossed is a Feature 6 ceiling ('guid:>percent'), which a refuel
        /// never answers; false for a floor, or when nothing was crossed.</param>
        private static string CheckStatThresholds(out bool ceiling)
        {
            ceiling = false;
            string stop = CheckThreshold(Messages.En("Satiation"), SatiationGuid, Plugin.SatiationStopThreshold.Value, false)
                ?? CheckThreshold(Messages.En("Hydration"), HydrationGuid, Plugin.HydrationStopThreshold.Value, false)
                ?? CheckThreshold(Messages.En("Stamina"), StaminaGuid, Plugin.StaminaStopThreshold.Value, false);
            if (stop != null) return stop;

            var extra = ExtraStatThresholds;
            for (int i = 0; i < extra.Count; i++)
            {
                stop = CheckThreshold(null, extra[i].Guid, extra[i].Percent, extra[i].Ceiling);
                if (stop != null) { ceiling = extra[i].Ceiling; return stop; }
            }
            return null;
        }

        /// <param name="label">Fixed player-facing name, or null to use the resolved stat's own GameName.</param>
        /// <param name="ceiling">False: stop when the stat is below <paramref name="threshold"/> percent of its current
        /// maximum (the fixed floors, and an Extra entry without '>'). True: stop when it is above that share.</param>
        private static string CheckThreshold(string label, string guid, int threshold, bool ceiling)
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
                if (!gm.StatsDict.TryGetValue(model, out var stat) || stat == null)
                {
                    // Resolves, but is not a stat this run tracks (e.g. absent from this gamemode).
                    RunLog($"CheckThreshold('{who}'): stat GUID {guid} is not active in this run; this stop cannot fire.");
                    return null;
                }
                float max = stat.CurrentMinMaxValue.y;
                if (max <= 0f)
                {
                    RunLog($"CheckThreshold('{who}'): stat GUID {guid} has max {max:0.#}, so a percentage {(ceiling ? "ceiling" : "floor")} is undefined; this stop cannot fire.");
                    return null;
                }
                float pct = stat.SimpleCurrentValue / max * 100f;
                if (ceiling ? pct > threshold : pct < threshold)
                {
                    string name = label ?? StatDisplayName(model);
                    RunLog($"{name} is {stat.SimpleCurrentValue:0.#}/{max:0.#} ({pct:0.#}%), {(ceiling ? "over" : "under")} its {threshold}% {(ceiling ? "ceiling" : "floor")}");
                    return ceiling ? Messages.En("{0} above {1}%", name, threshold) : Messages.En("{0} below {1}%", name, threshold);
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

        private static CardData QuantityCardModel(string uid)
        {
            try { return UniqueIDScriptable.GetFromID<CardData>(uid); }
            catch (Exception ex) { Logger.LogDebug($"[Repeat] Stop At Quantity: looking up '{uid}' failed: {ex}"); }
            return null;
        }

        /// <summary>
        /// Stop At Quantity, once at the start of a run: a breadcrumb for every entry whose UniqueID names no card. Such an
        /// entry can never fire, and CheckQuantityStops skips it without a word, so this is where the log says so.
        /// </summary>
        private static void ReportUnknownQuantityCards()
        {
            var stops = QuantityStops;
            for (int i = 0; i < stops.Count; i++)
                if (QuantityCardModel(stops[i].Uid) == null)
                    RunLog($"Stop At Quantity: no card has the UniqueID '{stops[i].Uid}'; that entry cannot fire.");
        }

        /// <summary>
        /// Stop At Quantity: "{card name} reached {count}" for the first entry the player's location holds at least
        /// count cards of, else null. <paramref name="iteration"/> and <paramref name="when"/> place the check in its log line.
        /// </summary>
        private static string CheckQuantityStops(int iteration, string when)
        {
            var stops = QuantityStops;
            for (int i = 0; i < stops.Count; i++)
            {
                var model = QuantityCardModel(stops[i].Uid);
                if (model == null) continue; // reported at run start by ReportUnknownQuantityCards
                int have = CountCards(stops[i].Uid);
                if (have < 0)
                {
                    RunLog($"iteration {iteration}, {when}: the game's card list cannot be read, so Stop At Quantity cannot fire");
                    return null;
                }
                if (have < stops[i].Count) continue;
                // The card's name in the player's language (Feature 16), as CardName gives it for a live card.
                string name = LocalizedText(model.CardName);
                if (string.IsNullOrEmpty(name)) name = stops[i].Uid;
                RunLog($"iteration {iteration}, {when}: your location holds {have} '{name}' ({stops[i].Uid}), at least the {stops[i].Count} set for it (Stop At Quantity)");
                return Messages.En("{0} reached {1}", name, stops[i].Count);
            }
            return null;
        }

        /// <summary>
        /// Stop At Quantity's count: the AllCards entries of that UniqueID in the player's current environment, carried
        /// cards and a carried bag's contents included. AllCards alone is not this board (CLAUDE.md "AllCards is not the
        /// board"), so every entry passes InPlayerEnv as ResolveCard's do; W0.7 measured one entry per card of a pile, so
        /// there is no slot arithmetic. -1 when the list cannot be read.
        /// </summary>
        private static int CountCards(string uid)
        {
            var gm = GameManager.Instance;
            if (gm == null || gm.AllCards == null) return -1;
            int n = 0;
            for (int i = 0; i < gm.AllCards.Count; i++)
            {
                var c = gm.AllCards[i];
                if (InPlayerEnv(c) && MatchCard(c, null, uid, null, null) != null) n++;
            }
            return n;
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

        /// <summary>
        /// Stop On Tool Break: true when the dragged card used by the last iteration broke. The game breaks a tool by
        /// running its UsageDurability OnZero, whose Destroy or Transform both go through GameManager.RemoveCard: the old
        /// card is destroyed (Destroyed set, its CardModel and UniqueID left as they were until the pool resets it about
        /// five seconds later, or Unity-destroyed when pooling is off) and a Transform spawns the result as a NEW card
        /// (.decomp/GameManager.cs, ApplyCardStateChange). So a broken tool almost never reads as "same card, new UID";
        /// it reads as gone. Gone counts as broken only for a card whose own model breaks when its usage bar empties,
        /// whose usage bar is at zero, and which the action itself did not destroy or transform: a dragged food or
        /// material the action consumes is gone by design and never stops the run. A live card whose UID changed (an
        /// in-place model swap) still counts, as before.
        /// </summary>
        private static bool GivenToolBroke(Captured cap, out string how)
        {
            how = null;
            var c = cap.GivenCard;
            // ReferenceEquals, never ==: a Unity-destroyed card compares equal to null, and destroyed is the case looked for.
            if (ReferenceEquals(c, null) || string.IsNullOrEmpty(cap.GivenUid) || cap.GivenConsumedByAction) return false;
            bool gone = !c || c.Destroyed || c.CardModel == null;
            if (!gone)
            {
                string uid = UidOf(c);
                if (string.IsNullOrEmpty(uid) || string.Equals(uid, cap.GivenUid, StringComparison.Ordinal)) return false;
                how = $"is now '{uid}'";
                return true;
            }
            // The usage field is plain managed data, so it still reads on a destroyed card (the pool's reset zeroes it).
            if (!cap.GivenBreaksAtZero || c.CurrentUsageDurability > 0.0001f) return false;
            how = "broke (its usage ran out and the game removed it)";
            return true;
        }

        // =====================================================================
        // NAME HELPERS
        // =====================================================================

        /// <summary>A run's kind label for one step, as the run's opening toast and every dispatch line show it.</summary>
        private static string StepKindLabel(Captured cap, bool fromPin)
        {
            string kind = KindLabel(cap.Kind);
            if (cap.Kind == FunnelKind.GroupAction && PerCardGroupMode) kind = Messages.En("group, per card");
            if (fromPin) kind = Messages.En("pinned {0}", kind);
            return kind;
        }

        /// <summary>Player-facing name for the funnel a repeat is replaying through.</summary>
        private static string KindLabel(FunnelKind kind)
        {
            switch (kind)
            {
                case FunnelKind.StackAction: return Messages.En("stack");
                case FunnelKind.CardOnCard: return Messages.En("drag-drop");
                case FunnelKind.GroupAction: return Messages.En("group");
                default: return Messages.En("action");
            }
        }

        /// <summary>The captured action's name in the player's language (Feature 16): shown, never compared.</summary>
        private static string DisplayName(Captured cap)
            => !string.IsNullOrEmpty(cap.ActionDisplay) ? cap.ActionDisplay
             : !string.IsNullOrEmpty(cap.ActionName) ? cap.ActionName : Messages.En("action");

        /// <summary>A card's name in the player's language (Feature 16): shown and logged, never compared.</summary>
        private static string CardName(InGameCardBase c)
        {
            if (c == null || !c) return "?";
            var m = c.CardModel;
            if (m == null) return "?";
            string n = LocalizedText(m.CardName);
            return string.IsNullOrEmpty(n) ? "?" : n;
        }

        /// <summary>
        /// LocalizedString.ToString(): the player's language, else DefaultText (.decomp/LocalizedString.cs). In English
        /// it is the DefaultText, since the game ships no English table. Falls back to DefaultText if the read throws.
        /// </summary>
        private static string LocalizedText(LocalizedString s)
        {
            try
            {
                string t = s.ToString();
                if (!string.IsNullOrEmpty(t)) return t;
            }
            catch (Exception ex) { Logger.LogDebug($"[Repeat] reading the localized text of '{s.DefaultText}' failed: {ex.GetType().Name}"); }
            return s.DefaultText;
        }

        private static bool IsBlockedAction(string actionName)
        {
            if (string.IsNullOrEmpty(actionName)) return false;
            foreach (var keyword in BlockedActionKeywords)
                if (ContainsWord(actionName, keyword)) return true;
            return false;
        }

        /// <summary>
        /// Rest, Relax and every Extra Rest-Like Actions entry, as whole words of the action's English name
        /// (ActionName.DefaultText, like the blocklist), so the list works the same in every game language.
        /// </summary>
        private static bool IsRestLike(string actionName)
        {
            if (string.IsNullOrEmpty(actionName)) return false;
            foreach (var keyword in RestLikeKeywords)
                if (ContainsWord(actionName, keyword)) return true;
            return false;
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
