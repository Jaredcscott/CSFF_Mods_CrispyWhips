using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Repeat_Action.Patcher
{
    /// <summary>
    /// Multi-gate capture system for player-only action detection:
    ///
    ///   Gate 1 (Primary): InspectionPopup.OnButtonClicked PREFIX � fires on player UI clicks.
    ///   Gate 2 (Fallback): Mouse-click + Player check � catches actions that bypass the popup
    ///                      (e.g. location Clear, exploration actions).
    ///   Capture: GameManager.ActionRoutine PREFIX � captures action details when either gate is open.
    ///
    /// System actions (Refresh Weather, Mouse Damage, etc.) are filtered because they fire
    /// without a physical mouse click and without going through InspectionPopup.
    /// </summary>
    public static class ActionPatch
    {
        private static ManualLogSource Logger => Plugin.Logger;

        // ===== Cached reflection references =====
        private static Type gameManagerType;
        private static Type inspectionPopupType;
        private static Type cardGraphicsType;
        private static Type cardActionType;
        private static Type inGameCardBaseType;
        private static Type npcOrPlayerType;

        private static PropertyInfo gmInstanceProp;
        private static FieldInfo gmCurrentGameStateField;
        private static FieldInfo npcOrPlayerPlayerField;       // InGameNPCOrPlayer.Player (bool)
        private static PropertyInfo ipCurrentCardProp;
        private static MethodInfo ipOnButtonClickedMethod;
        private static MethodInfo ipOnGroupInventoryActionClickedMethod; // Cached OnGroupInventoryActionClicked method
        private static MethodInfo cgOnPointerClickMethod;
        private static MethodInfo gameManagerActionRoutineMethod; // Cached ActionRoutine method
        private static MethodInfo gameManagerCocMethod;             // Cached CardOnCardActionRoutine method
        private static MethodInfo cardActionSimpleConditionsCheck;  // Cached SimpleConditionsCheck method
        private static MethodInfo cardActionQuickRequirementsCheck; // Cached QuickRequirementsCheck method

        // ===== Gate 1 state: set by InspectionPopup.OnButtonClicked =====
        private static bool playerClickedAction = false;
        private static int playerClickedIndex = -1;
        private static bool playerClickedStack = false;
        private static object playerClickedPopup = null;       // InspectionPopup instance
        private static int playerClickedFrame = -999;          // Frame when Gate 1 fired (for timeout)
        private static bool playerClickedViaGroupActionUI = false; // True only when Gate 1b (OnGroupInventoryActionClicked) fired, not Gate 1 (OnButtonClicked)

        // ===== Captured action (set by Gate 2 when playerClickedAction is true) =====
        private static object lastAction;                      // CardAction
        private static object lastReceivingCard;                // InGameCardBase
        private static object lastUser;                         // InGameNPCOrPlayer (struct, boxed)
        private static object lastGivenCard;                    // InGameCardBase or null
        private static string lastActionName;
        private static int lastActionIndex = -1;                // Button index in InspectionPopup
        private static object lastInspectionPopup;              // The popup instance for replay

        // ===== Captured exact args for perfect replay =====
        private static object[] lastCocArgs;                    // Exact args from CardOnCardActionRoutine
        private static MethodInfo lastCocMethod;                // Cached method ref
        private static int lastCocActionParamIdx = -1;          // Index of _Action param
        private static int lastCocGivenParamIdx = -1;           // Index of _GivenCard param  
        private static int lastCocReceivingParamIdx = -1;       // Index of _ReceivingCard param
        private static int lastCocUserParamIdx = -1;            // Index of _User param
        private static int lastCocCaptureFrame = -1;            // Frame when Gate 3 captured (prevents Gate 2 overwrite)

        // ===== Saved UniqueIDs (survive card clearing � cleared cards lose CardModel) =====
        private static string savedReceivingUniqueId;           // Set at capture time, used by TryRefresh
        private static string savedGivenUniqueId;               // Set at capture time, used by TryRefresh

        private static object[] lastActionRoutineArgs;          // Exact args from ActionRoutine
        private static MethodInfo lastActionRoutineMethod;      // Cached method ref
        private static int lastArActionIdx = -1;                // Index of _Action param
        private static int lastArReceivingIdx = -1;             // Index of _ReceivingCard param
        private static int lastArUserIdx = -1;                  // Index of _User param
        private static int lastArGivenIdx = -1;                 // Index of _GivenCard param

        // ===== Group inventory action flag =====
        private static bool lastIsGroupInventoryAction = false;  // True if captured via Gate 2b (Forage/Clear/Harvest)

        // ===== Repeat state =====
        private static bool isRepeating = false;
        private static bool cancelRequested = false;

        // ===== Cached Rest action (from SpecialActionSet / TimeSkipOptions) =====
        private static object cachedRestAction = null;

        public static bool HasLastAction => lastAction != null;
        public static string LastActionName => lastActionName ?? "Unknown";
        public static bool IsRepeating => isRepeating;

        public static void CancelRepeat()
        {
            cancelRequested = true;
        }

        // =====================================================================
        // PATCH APPLICATION
        // =====================================================================
        public static void ApplyPatch(Harmony harmony)
        {
            try
            {
                // ----- Resolve types -----
                gameManagerType = AccessTools.TypeByName("GameManager");
                inspectionPopupType = AccessTools.TypeByName("InspectionPopup");
                cardGraphicsType = AccessTools.TypeByName("CardGraphics");
                cardActionType = AccessTools.TypeByName("CardAction");
                inGameCardBaseType = AccessTools.TypeByName("InGameCardBase");
                npcOrPlayerType = AccessTools.TypeByName("InGameNPCOrPlayer");

                if (gameManagerType == null || inspectionPopupType == null)
                {
                    Logger.LogError($"Critical types missing! GM={gameManagerType != null}, IP={inspectionPopupType != null}");
                    return;
                }

                // ----- Cache GameManager accessors -----
                gmInstanceProp = gameManagerType.GetProperty("Instance",
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                if (gmInstanceProp == null)
                    gmInstanceProp = gameManagerType.BaseType?.GetProperty("Instance",
                        BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

                gmCurrentGameStateField = AccessTools.Field(gameManagerType, "CurrentGameState");

                Logger.Log(BepInEx.Logging.LogLevel.Debug, $"GameManager.Instance: {(gmInstanceProp != null ? "found" : "NOT FOUND")}");
                Logger.Log(BepInEx.Logging.LogLevel.Debug, $"GameManager.CurrentGameState: {(gmCurrentGameStateField != null ? "found" : "NOT FOUND")}");

                // ----- Cache InGameNPCOrPlayer.Player field -----
                if (npcOrPlayerType != null)
                    npcOrPlayerPlayerField = AccessTools.Field(npcOrPlayerType, "Player");

                // ----- Cache InspectionPopup accessors -----
                ipCurrentCardProp = AccessTools.Property(inspectionPopupType, "CurrentCard");
                ipOnButtonClickedMethod = AccessTools.Method(inspectionPopupType, "OnButtonClicked",
                    new Type[] { typeof(int), typeof(bool) });
                if (cardGraphicsType != null)
                    cgOnPointerClickMethod = AccessTools.Method(cardGraphicsType, "OnPointerClick");

                Logger.Log(BepInEx.Logging.LogLevel.Debug, $"InspectionPopup.OnButtonClicked: {(ipOnButtonClickedMethod != null ? "found" : "NOT FOUND")}");
                Logger.Log(BepInEx.Logging.LogLevel.Debug, $"InspectionPopup.CurrentCard: {(ipCurrentCardProp != null ? "found" : "NOT FOUND")}");
                Logger.Log(BepInEx.Logging.LogLevel.Debug, $"CardGraphics.OnPointerClick: {(cgOnPointerClickMethod != null ? "found" : "NOT FOUND")}");

                // ===== GATE 1: Patch InspectionPopup.OnButtonClicked =====
                // This ONLY fires when the player clicks an action button in the UI.
                if (ipOnButtonClickedMethod != null)
                {
                    harmony.Patch(ipOnButtonClickedMethod,
                        prefix: new HarmonyMethod(typeof(ActionPatch), nameof(OnButtonClicked_Prefix)));
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, "GATE 1: Patched InspectionPopup.OnButtonClicked (player click detection)");
                }
                else
                {
                    Logger.LogError("Cannot patch InspectionPopup.OnButtonClicked - player detection won't work!");
                }

                // ===== GATE 2: Patch GameManager.ActionRoutine =====
                // Captures action details, but ONLY when Gate 1 flag is set.
                gameManagerActionRoutineMethod = AccessTools.Method(gameManagerType, "ActionRoutine");
                if (gameManagerActionRoutineMethod != null)
                {
                    harmony.Patch(gameManagerActionRoutineMethod,
                        prefix: new HarmonyMethod(typeof(ActionPatch), nameof(ActionRoutine_Prefix)));
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, "GATE 2: Patched GameManager.ActionRoutine (action detail capture)");
                }
                else
                {
                    Logger.LogError("GameManager.ActionRoutine not found!");
                }

                // ===== GATE 3: Patch GameManager.CardOnCardActionRoutine =====
                // Captures drag-and-drop actions (inherently player-initiated, no gate needed)
                var cocActionMethod = AccessTools.Method(gameManagerType, "CardOnCardActionRoutine");
                gameManagerCocMethod = cocActionMethod;
                if (cocActionMethod != null)
                {
                    harmony.Patch(cocActionMethod,
                        prefix: new HarmonyMethod(typeof(ActionPatch), nameof(CardOnCardActionRoutine_Prefix)));
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, "GATE 3: Patched GameManager.CardOnCardActionRoutine (drag-drop capture)");
                }
                else
                {
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, "GameManager.CardOnCardActionRoutine not found - drag-drop actions won't be capturable");
                }

                // ===== GATE 1b: Patch InspectionPopup.OnGroupInventoryActionClicked =====
                // Forage/Clear on location cards goes through this method instead of OnButtonClicked.
                ipOnGroupInventoryActionClickedMethod = AccessTools.Method(inspectionPopupType, "OnGroupInventoryActionClicked");
                if (ipOnGroupInventoryActionClickedMethod != null)
                {
                    harmony.Patch(ipOnGroupInventoryActionClickedMethod,
                        prefix: new HarmonyMethod(typeof(ActionPatch), nameof(OnGroupInventoryActionClicked_Prefix)));
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, "GATE 1b: Patched InspectionPopup.OnGroupInventoryActionClicked (group action click detection)");
                }
                else
                {
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, "InspectionPopup.OnGroupInventoryActionClicked not found");
                }

                // ===== GATE 2b: Patch GameManager.PerformGroupInventoryAction =====
                // Captures the action details when a group inventory action (Forage/Clear) is executed.
                var groupActionMethod = AccessTools.Method(gameManagerType, "PerformGroupInventoryAction");
                if (groupActionMethod != null)
                {
                    harmony.Patch(groupActionMethod,
                        prefix: new HarmonyMethod(typeof(ActionPatch), nameof(PerformGroupInventoryAction_Prefix)));
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, "GATE 2b: Patched GameManager.PerformGroupInventoryAction (group action capture)");
                }
                else
                {
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, "GameManager.PerformGroupInventoryAction not found");
                }

                // Cache validation method lookups
                if (cardActionType != null)
                {
                    cardActionSimpleConditionsCheck = AccessTools.Method(cardActionType, "SimpleConditionsCheck");
                    cardActionQuickRequirementsCheck = AccessTools.Method(cardActionType, "QuickRequirementsCheck");
                }

                Logger.Log(BepInEx.Logging.LogLevel.Debug, "ActionPatch v4 applied - dual-gate player-only capture");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to apply patches: {ex}");
            }
        }

        // =====================================================================
        // GATE 1: InspectionPopup.OnButtonClicked � Player click detection
        // =====================================================================

        /// <summary>
        /// Fires when the player clicks an action button in the card inspection popup.
        /// Sets a flag so the next ActionRoutine call knows it's player-initiated.
        /// Signature: OnButtonClicked(int _Index, bool _Stack)
        /// </summary>
        static void OnButtonClicked_Prefix(object __instance, int _Index, bool _Stack)
        {
            try
            {
                // Don't capture during our own repeats
                if (isRepeating) return;

                // Don't capture event popup buttons at all
                // These are always button #0 "Continue" on event cards and should never be repeated
                if (_Index == 0)
                {
                    if (ipCurrentCardProp != null)
                    {
                        var currentCard = ipCurrentCardProp.GetValue(__instance);
                        string cardName = GetCardDisplayName(currentCard);
                        
                        // If this looks like an event card, completely skip capture
                        if (cardName != null && (cardName.Contains("Anxiety") || cardName.Contains("Dehydration") || 
                            cardName.Contains("Starving") || cardName.Contains("control") || cardName.Contains("event")))
                        {
                            Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Gate1] Ignoring event popup button on '{cardName}'");
                            return;
                        }
                    }
                }

                // Set the player-click flag for Gate 2
                playerClickedAction = true;
                playerClickedViaGroupActionUI = false; // Regular button click, NOT a group-action button
                playerClickedIndex = _Index;
                playerClickedStack = _Stack;
                playerClickedPopup = __instance;
                playerClickedFrame = Time.frameCount;

                // Log which card's popup this is
                string displayCardName = "unknown";
                if (ipCurrentCardProp != null)
                {
                    var currentCard = ipCurrentCardProp.GetValue(__instance);
                    displayCardName = GetCardDisplayName(currentCard);
                }

                Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Gate1] Player clicked button #{_Index} (stack={_Stack}) on card: {displayCardName}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[Gate1] Error: {ex.Message}");
            }
        }

        // =====================================================================
        // GATE 1b: InspectionPopup.OnGroupInventoryActionClicked
        // =====================================================================

        /// <summary>
        /// Fires when the player clicks a group inventory action button (Forage, Clear on locations).
        /// These actions bypass OnButtonClicked entirely, using a separate code path.
        /// Sets the same player-click flag for Gate 2/2b.
        /// </summary>
        static void OnGroupInventoryActionClicked_Prefix(object __instance, int _Index)
        {
            try
            {
                if (isRepeating) return;

                playerClickedAction = true;
                playerClickedViaGroupActionUI = true; // Group-action button (Forage/Clear), use OnGroupInventoryActionClicked for replay
                playerClickedIndex = _Index;
                playerClickedStack = false;
                playerClickedPopup = __instance;
                playerClickedFrame = Time.frameCount;

                string displayCardName = "unknown";
                if (ipCurrentCardProp != null)
                {
                    var currentCard = ipCurrentCardProp.GetValue(__instance);
                    displayCardName = GetCardDisplayName(currentCard);
                }

                Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Gate1b] Player clicked group action #{_Index} on card: {displayCardName}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[Gate1b] Error: {ex.Message}");
            }
        }

        // =====================================================================
        // GATE 2b: GameManager.PerformGroupInventoryAction
        // =====================================================================

        /// <summary>
        /// Captures group inventory actions (Forage, Clear on location cards).
        /// Signature: PerformGroupInventoryAction(List cards, List actions, bool _FastMode, InGameNPCOrPlayer _User)
        /// These actions use a different execution path than ActionRoutine.
        /// We extract the first action from the list and capture it like a normal action.
        /// </summary>
        static void PerformGroupInventoryAction_Prefix(object __instance, object[] __args)
        {
            try
            {
                if (isRepeating) return;

                // Check if Gate 1b set the player-click flag
                bool fromPopup = playerClickedAction && (Time.frameCount - playerClickedFrame) < Plugin.Gate1TimeoutFrames.Value;

                if (!fromPopup)
                {
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Capture] SKIPPED group action - no player click detected");
                    return;
                }

                playerClickedAction = false; // consume one-shot
                bool capturedViaGroupActionUI = playerClickedViaGroupActionUI;
                playerClickedViaGroupActionUI = false;

                // Args: [0]=List<InGameCardBase> cards, [1]=List<CardAction> actions, [2]=bool _FastMode, [3]=InGameNPCOrPlayer _User
                if (__args == null || __args.Length < 4)
                {
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Capture] Group action has unexpected arg count: {__args?.Length}");
                    return;
                }

                var actionsList = __args[1] as System.Collections.IList;
                var cardsList = __args[0] as System.Collections.IList;
                var user = __args[3];

                if (actionsList == null || actionsList.Count == 0)
                {
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Capture] Group action has no actions list");
                    return;
                }

                // Get the first action and first card
                object action = actionsList[0];
                object receivingCard = (cardsList != null && cardsList.Count > 0) ? cardsList[0] : null;

                // Verify player
                if (user != null && npcOrPlayerPlayerField != null)
                {
                    bool isPlayer = (bool)npcOrPlayerPlayerField.GetValue(user);
                    if (!isPlayer)
                    {
                        Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Capture] SKIPPED group action by NPC");
                        return;
                    }
                }

                string actionName = GetActionDisplayName(action);
                string cardName = GetCardDisplayName(receivingCard);

                Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Capture] Group action: {actionName} on {cardName} (button #{playerClickedIndex})");

                if (!IsPermittedAction(actionName))
                {
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Capture] SKIPPED non-permitted group action: {actionName}");
                    lastAction = null;
                    lastActionName = actionName;
                    lastReceivingCard = null;
                    lastGivenCard = null;
                    lastActionIndex = -1;
                    lastInspectionPopup = null;
                    lastCocArgs = null;
                    lastCocMethod = null;
                    lastActionRoutineArgs = null;
                    lastActionRoutineMethod = null;
                    return;
                }

                // Save for replay
                lastAction = action;
                lastReceivingCard = receivingCard;
                lastUser = user;
                lastGivenCard = null;
                lastActionName = actionName;
                lastActionIndex = playerClickedIndex;
                lastInspectionPopup = playerClickedPopup;
                // Use OnGroupInventoryActionClicked for replay ONLY if the original click came through
                // OnGroupInventoryActionClicked (Forage/Clear buttons). DismantleActions with CollectionName
                // (e.g., "Harvest" on Reed Patch) go through OnButtonClicked -> PerformGroupInventoryAction,
                // so they must replay via OnButtonClicked, not OnGroupInventoryActionClicked.
                lastIsGroupInventoryAction = capturedViaGroupActionUI;
                savedReceivingUniqueId = GetCardUniqueId(receivingCard);
                savedGivenUniqueId = null;

                // Clear drag-drop args � this is a popup action
                lastCocArgs = null;
                lastCocMethod = null;

                // Capture as ActionRoutine-compatible args for TryDirectExecution fallback
                if (gameManagerActionRoutineMethod != null)
                {
                    lastActionRoutineMethod = gameManagerActionRoutineMethod;
                    lastActionRoutineArgs = new object[] { action, receivingCard, user, false, false, false, null };
                    var arParams = gameManagerActionRoutineMethod.GetParameters();
                    lastArActionIdx = -1;
                    lastArReceivingIdx = -1;
                    lastArUserIdx = -1;
                    lastArGivenIdx = -1;
                    for (int idx = 0; idx < arParams.Length; idx++)
                    {
                        var pName = arParams[idx].Name.ToLowerInvariant();
                        if (pName.Contains("action") && !pName.Contains("card"))
                            lastArActionIdx = idx;
                        else if (pName.Contains("receiving"))
                            lastArReceivingIdx = idx;
                        else if (pName.Contains("user") || pName.Contains("player"))
                            lastArUserIdx = idx;
                        else if (pName.Contains("given"))
                            lastArGivenIdx = idx;
                    }
                }

                Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Capture] Captured group action '{actionName}' on '{cardName}' for repeat");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[Capture] PerformGroupInventoryAction error: {ex.Message}");
            }
        }

        // =====================================================================
        // GATE 2: GameManager.ActionRoutine � Conditional action capture
        // =====================================================================

        /// <summary>
        /// Captures action details when either gate indicates a player action:
        ///   - Gate 1: InspectionPopup.OnButtonClicked set the flag (within timeout)
        ///   - Gate 2: Player physically clicked mouse recently AND _User.Player == true
        /// System actions pass neither gate.
        /// </summary>
        static void ActionRoutine_Prefix(object __instance,
            object _Action, object _ReceivingCard, object _User,
            bool _FastMode, bool _DontPlaySounds,
            bool _ModifiersAlreadyCollected, object _GivenCard)
        {
            try
            {
                // Skip during our own repeats
                if (isRepeating) return;

                // --- Determine if this is a player action ---
                bool fromPopup = false;
                bool fromMouseClick = false;
                int buttonIndex = -1;
                object popupInstance = null;

                // Gate 1: InspectionPopup.OnButtonClicked fired recently
                if (playerClickedAction && (Time.frameCount - playerClickedFrame) < Plugin.Gate1TimeoutFrames.Value)
                {
                    fromPopup = true;
                    buttonIndex = playerClickedIndex;
                    popupInstance = playerClickedPopup;
                    playerClickedAction = false; // consume one-shot
                }
                else if (playerClickedAction)
                {
                    // Gate 1 flag expired - clear stale state
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Capture] Gate 1 flag expired (stale)");
                    playerClickedAction = false;
                    playerClickedViaGroupActionUI = false;
                }

                // Gate 2 (fallback): Physical mouse click + Player check
                if (!fromPopup)
                {
                    bool isPlayer = false;
                    if (_User != null && npcOrPlayerPlayerField != null)
                    {
                        isPlayer = (bool)npcOrPlayerPlayerField.GetValue(_User);
                    }

                    if (isPlayer && Plugin.PlayerRecentlyClicked)
                    {
                        fromMouseClick = true;
                        // Try to find the button index from the receiving card's DismantleActions
                        buttonIndex = FindActionIndex(_Action, _ReceivingCard);
                        // Try to find the InspectionPopup associated with the receiving card
                        popupInstance = FindActiveInspectionPopup(_ReceivingCard);
                    }
                }

                // Neither gate triggered - this is a system action, skip it
                if (!fromPopup && !fromMouseClick)
                {
                    string skippedName = GetActionDisplayName(_Action);
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Capture] SKIPPED system action: {skippedName}");
                    return;
                }

                // Check if Gate 3 (CardOnCardActionRoutine) already captured this frame.
                // CardOnCardActionRoutine internally calls ActionRoutine, which triggers this prefix.
                // If Gate 3 already captured, skip Gate 2 to avoid overwriting drag-drop args.
                if (lastCocCaptureFrame == Time.frameCount && lastCocArgs != null)
                {
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Capture] SKIPPED Gate 2 � Gate 3 already captured this frame (drag-drop nested call)");
                    return;
                }

                // NPC filter (belt-and-suspenders even for Gate 1)
                if (_User != null && npcOrPlayerPlayerField != null)
                {
                    bool isPlayer = (bool)npcOrPlayerPlayerField.GetValue(_User);
                    if (!isPlayer)
                    {
                        Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Capture] SKIPPED non-player action (NPC)");
                        return;
                    }
                }

                string actionName = GetActionDisplayName(_Action);
                string cardName = GetCardDisplayName(_ReceivingCard);
                string source = fromPopup ? "popup" : "mouse+player";

                Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Capture] Player action ({source}): {actionName} on {cardName} (button #{buttonIndex})");

                // Only capture permitted actions
                if (!IsPermittedAction(actionName))
                {
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Capture] SKIPPED non-permitted action: {actionName}");
                    // Clear last action so stale permitted actions don't persist
                    lastAction = null;
                    lastActionName = actionName; // Keep name for "not supported" message
                    lastReceivingCard = null;
                    lastGivenCard = null;
                    lastActionIndex = -1;
                    lastInspectionPopup = null;
                    lastCocArgs = null;
                    lastCocMethod = null;
                    lastActionRoutineArgs = null;
                    lastActionRoutineMethod = null;
                    return;
                }

                // Save everything needed for replay
                lastAction = _Action;
                lastReceivingCard = _ReceivingCard;
                lastUser = _User;
                lastGivenCard = _GivenCard;
                lastActionName = actionName;
                lastActionIndex = buttonIndex;
                lastInspectionPopup = popupInstance;
                lastIsGroupInventoryAction = false; // Regular button actions use OnButtonClicked
                savedReceivingUniqueId = GetCardUniqueId(_ReceivingCard);
                savedGivenUniqueId = _GivenCard != null ? GetCardUniqueId(_GivenCard) : null;

                // Capture exact ActionRoutine args for perfect replay
                if (gameManagerActionRoutineMethod != null)
                {
                    lastActionRoutineMethod = gameManagerActionRoutineMethod;
                    lastActionRoutineArgs = new object[] { _Action, _ReceivingCard, _User, _FastMode, _DontPlaySounds, _ModifiersAlreadyCollected, _GivenCard };

                    // Map parameter indices
                    var arParams = gameManagerActionRoutineMethod.GetParameters();
                    lastArActionIdx = -1;
                    lastArReceivingIdx = -1;
                    lastArUserIdx = -1;
                    lastArGivenIdx = -1;
                    for (int idx = 0; idx < arParams.Length; idx++)
                    {
                        var pName = arParams[idx].Name.ToLowerInvariant();
                        if (pName.Contains("action") && !pName.Contains("card"))
                            lastArActionIdx = idx;
                        else if (pName.Contains("receiving"))
                            lastArReceivingIdx = idx;
                        else if (pName.Contains("user") || pName.Contains("player"))
                            lastArUserIdx = idx;
                        else if (pName.Contains("given"))
                            lastArGivenIdx = idx;
                    }
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Capture] Captured exact ActionRoutine args ({arParams.Length} params)");
                }

                // Clear drag-drop args since this is a button action
                lastCocArgs = null;
                lastCocMethod = null;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[Capture] Error: {ex.Message}");
            }
        }

        // =====================================================================
        // GATE 3: GameManager.CardOnCardActionRoutine � Drag-drop capture
        // =====================================================================

        /// <summary>
        /// Captures drag-and-drop actions (e.g., dragging fiber onto thread recipe).
        /// These are inherently player-initiated so no gate check needed.
        /// Signature: CardOnCardActionRoutine(CardOnCardAction _Action, InGameCardBase _GivenCard, 
        ///   InGameCardBase _ReceivingCard, InGameNPCOrPlayer _User)
        /// </summary>
        static void CardOnCardActionRoutine_Prefix(object __instance, object[] __args,
            object _Action, object _GivenCard, object _ReceivingCard, object _User)
        {
            try
            {
                // Don't capture during our own repeats
                if (isRepeating) return;

                // Verify this is a player action
                bool isPlayer = false;
                if (_User != null && npcOrPlayerPlayerField != null)
                {
                    isPlayer = (bool)npcOrPlayerPlayerField.GetValue(_User);
                }

                if (!isPlayer)
                {
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Capture] Drag-drop by NPC - ignoring");
                    return;
                }

                string actionName = GetActionDisplayName(_Action);
                string givenCardName = GetCardDisplayName(_GivenCard);
                string receivingCardName = GetCardDisplayName(_ReceivingCard);

                Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Capture] Player drag-drop: {actionName} (given={givenCardName}, receiving={receivingCardName})");

                // Only capture permitted actions
                if (!IsPermittedAction(actionName))
                {
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Capture] SKIPPED non-permitted drag-drop: {actionName}");
                    // Clear last action so stale permitted actions don't persist
                    lastAction = null;
                    lastActionName = actionName; // Keep name for "not supported" message
                    lastReceivingCard = null;
                    lastGivenCard = null;
                    lastActionIndex = -1;
                    lastInspectionPopup = null;
                    lastCocArgs = null;
                    lastCocMethod = null;
                    lastActionRoutineArgs = null;
                    lastActionRoutineMethod = null;
                    return;
                }

                // Store action details
                lastAction = _Action;
                lastReceivingCard = _ReceivingCard;
                lastUser = _User;
                lastGivenCard = _GivenCard;
                lastActionName = actionName;
                lastActionIndex = -99; // Special marker for drag-drop actions
                lastInspectionPopup = null; // No popup for drag-drop
                lastIsGroupInventoryAction = false; // Drag-drop actions don't use group inventory
                savedReceivingUniqueId = GetCardUniqueId(_ReceivingCard);
                savedGivenUniqueId = GetCardUniqueId(_GivenCard);

                // CRITICAL: Capture exact args for perfect replay
                // Clone the array so modifications to our copy don't affect game state
                lastCocArgs = (object[])__args.Clone();
                lastCocMethod = gameManagerCocMethod;
                lastCocCaptureFrame = Time.frameCount;  // Mark frame so Gate 2 won't overwrite

                // Map parameter indices by name for card refreshing during replay
                if (lastCocMethod != null)
                {
                    var parameters = lastCocMethod.GetParameters();
                    lastCocActionParamIdx = -1;
                    lastCocGivenParamIdx = -1;
                    lastCocReceivingParamIdx = -1;
                    lastCocUserParamIdx = -1;

                    for (int idx = 0; idx < parameters.Length; idx++)
                    {
                        // Skip boolean parameters � only map card/action object slots
                        // (e.g., _UseReceivingForSlot contains "receiving" but is a bool, not a card)
                        if (parameters[idx].ParameterType == typeof(bool)) continue;

                        var pName = parameters[idx].Name.ToLowerInvariant();
                        if (pName.Contains("action") && !pName.Contains("card"))
                            lastCocActionParamIdx = idx;
                        else if (pName.Contains("given"))
                            lastCocGivenParamIdx = idx;
                        else if (pName.Contains("receiving"))
                            lastCocReceivingParamIdx = idx;
                        else if (pName.Contains("user") || pName.Contains("player"))
                            lastCocUserParamIdx = idx;
                    }

                    Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Capture] Captured exact CardOnCardActionRoutine args ({__args.Length} params)");
                    // Log boolean values for debugging
                    for (int idx = 0; idx < parameters.Length; idx++)
                    {
                        if (parameters[idx].ParameterType == typeof(bool))
                            Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Capture]   param[{idx}] '{parameters[idx].Name}' = {__args[idx]}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"[Capture] CardOnCard error: {ex.Message}");
            }
        }

        // =====================================================================
        // REPEAT LOGIC
        // =====================================================================

        /// <summary>
        /// Repeat the last captured player action by clicking the same button
        /// in the InspectionPopup, letting the game handle all validation.
        /// Falls back to direct ActionRoutine call if popup replay fails.
        /// </summary>
        public static IEnumerator RepeatLastAction(int count)
        {
            if (!HasLastAction)
            {
                // Show specific message if we have the action name but it wasn't permitted
                if (!string.IsNullOrEmpty(lastActionName))
                    Plugin.ShowNotification($"'{lastActionName}' is not supported");
                else
                    Plugin.ShowNotification("No action to repeat");
                yield break;
            }

            // Check if this is a drag-drop action (button index -99)
            bool isDragDrop = (lastActionIndex == -99);

            if (lastActionIndex < 0 && !isDragDrop)
            {
                Plugin.ShowNotification("No button index recorded");
                Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] No button index - can't replay");
                yield break;
            }

            var gm = GetGameManager();
            if (gm == null)
            {
                Plugin.ShowNotification("Game not ready");
                yield break;
            }

            isRepeating = true;
            cancelRequested = false;

            Plugin.ShowNotification($"Repeating: {lastActionName} x{count}");
            string actionType = isDragDrop ? "drag-drop" : $"button #{lastActionIndex}";
            Logger.LogInfo($"[Repeat] Starting: '{lastActionName}' x{count} ({actionType})");

            int completed = 0;

            for (int i = 0; i < count; i++)
            {
                // --- Cancel check ---
                if (cancelRequested)
                {
                    Plugin.ShowNotification($"Cancelled ({completed}/{count})");
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Cancelled at {i + 1}/{count}");
                    break;
                }

                // --- Wait for game idle (SELECT state) ---
                float waitTime = 0f;
                while (!IsGameIdle() && waitTime < Plugin.ActionCompletionTimeout.Value)
                {
                    yield return new WaitForSeconds(0.1f);
                    waitTime += 0.1f;
                    if (cancelRequested) break;
                }

                if (cancelRequested)
                {
                    Plugin.ShowNotification($"Cancelled ({completed}/{count})");
                    break;
                }

                if (waitTime >= Plugin.ActionCompletionTimeout.Value)
                {
                    Plugin.ShowNotification("Timed out waiting");
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] Timed out waiting for idle");
                    break;
                }

                // Settling delay after idle
                yield return null;

                // Determine action characteristics for validation
                bool requireQuantityChange = RequiresQuantityProgressValidation();
                bool popupOnlyExecution = RequiresPopupOnlyExecution();
                bool cardlessAction = lastReceivingCard == null || IsLikelyCardlessAction();

                Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Action flags: requireQty={requireQuantityChange}, isDragDrop={isDragDrop}, cardless={cardlessAction}");

                // Save UniqueID for finding replacement cards after consumption
                // Use saved ID as fallback � cleared cards lose their CardModel
                string receivingUniqueId = GetCardUniqueId(lastReceivingCard);
                if (string.IsNullOrEmpty(receivingUniqueId))
                    receivingUniqueId = savedReceivingUniqueId;

                // Snapshot game tick BEFORE execution (universal action-executed detector)
                int tickBefore = GetGameTick();
                if (requireQuantityChange)
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Pre-eat tick={tickBefore}, card='{receivingUniqueId}'");

                // Refresh card references from scene (QuickTransfer-style live card confirmation)
                bool isTravelAction = IsTravelAction();
                if (isTravelAction)
                {
                    // TRAVEL FLOW: Rest ? check blockers ? find direction ? execute travel.
                    // Resting first ensures stamina is recovered for the journey and
                    // advances the game tick so the new location's cards are ready.
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] Resting before travel...");
                    bool preRested = TryPerformRestAction(gm as MonoBehaviour);
                    if (preRested)
                    {
                        // Two-phase idle wait (same pattern as post-execution wait).
                        // Phase 1: Let the rest coroutine START � mandatory frame yields
                        // prevent detecting "idle" before the coroutine has even begun.
                        yield return null;
                        yield return null;
                        yield return null;

                        // Wait for game to leave idle (rest action starting)
                        float restStartWait = 0f;
                        while (IsGameIdle() && restStartWait < 2f)
                        {
                            yield return null;
                            restStartWait += Time.deltaTime;
                        }

                        // Phase 2: Wait for game to return to idle (rest completing)
                        float preRestWait = 0f;
                        while (!IsGameIdle() && preRestWait < Plugin.PreTravelRestTimeout.Value)
                        {
                            yield return new WaitForSeconds(0.1f);
                            preRestWait += 0.1f;
                            if (cancelRequested) break;
                        }
                        Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Pre-travel rest completed after {preRestWait:F1}s");

                        // Extended settle � rest may have trailing animations/state transitions
                        yield return new WaitForSeconds(0.5f);

                        // Final idle confirmation after settle
                        float postSettleWait = 0f;
                        while (!IsGameIdle() && postSettleWait < 5f)
                        {
                            yield return new WaitForSeconds(0.1f);
                            postSettleWait += 0.1f;
                            if (cancelRequested) break;
                        }
                    }
                    if (cancelRequested)
                    {
                        Plugin.ShowNotification($"Cancelled ({completed}/{count})");
                        break;
                    }

                    // Check for blockers after rest (dehydration, starvation, events)
                    if (Plugin.StopOnLowStats.Value && HasActionBlockers())
                    {
                        Plugin.ShowNotification($"Stopped - Event triggered! ({completed}/{count})");
                        Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Blocker after pre-travel rest at {completed}/{count}");
                        break;
                    }

                    // Now find the CURRENT location card with a matching direction action.
                    // After each travel, the player is at a new location with different cards.
                    if (!RefreshTravelContext())
                    {
                        Plugin.ShowNotification($"Can't go {lastActionName} ({completed}/{count})");
                        Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] No location with '{lastActionName}' action found");
                        break;
                    }
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Travel context: {GetCardDisplayName(lastReceivingCard)} button #{lastActionIndex}");

                    // Recompute cardlessAction since RefreshTravelContext updated lastReceivingCard
                    cardlessAction = lastReceivingCard == null || IsLikelyCardlessAction();
                }
                else if (lastReceivingCard != null && !TryRefreshReceivingCard())
                {
                    // Card missing - could be consumed/transformed by previous action
                    // For drag-drop (e.g., chopping trees), targets respawn on tick boundaries.
                    // Wait for tick advancement and retry before giving up.
                    if (isDragDrop && completed > 0)
                    {
                        // Target consumed (e.g., tree chopped). Perform a Rest to advance
                        // the game tick so the target can respawn, then look for it.
                        Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] Drag-drop target missing, resting to advance tick...");
                        bool restAndFound = false;
                        for (int restAttempt = 0; restAttempt < 3 && !cancelRequested; restAttempt++)
                        {
                            bool rested = TryPerformRestAction(gm as MonoBehaviour);
                            if (!rested)
                            {
                                Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] Rest action failed, falling back to tick wait");
                                yield return new WaitForSeconds(2f);
                            }
                            else
                            {
                                // Wait for rest to complete (idle)
                                float restWait = 0f;
                                while (!IsGameIdle() && restWait < 10f)
                                {
                                    yield return new WaitForSeconds(0.1f);
                                    restWait += 0.1f;
                                    if (cancelRequested) break;
                                }
                                Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Rest completed after {restWait:F1}s idle wait");
                                yield return new WaitForSeconds(0.3f);
                            }

                            if (TryRefreshReceivingCard())
                            {
                                restAndFound = true;
                                Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] Found respawned target after rest");
                                break;
                            }
                        }
                        if (!restAndFound)
                        {
                            Plugin.ShowNotification($"No more targets ({completed}/{count})");
                            Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Target not found after rest attempts");
                            break;
                        }
                    }
                    else if (completed > 0)
                    {
                        Plugin.ShowNotification($"Card transformed ({completed}/{count})");
                        Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Target card consumed/transformed after {completed} iterations");
                    }
                    else
                    {
                        Plugin.ShowNotification($"Target missing ({completed}/{count})");
                        Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Target card missing at {i + 1}/{count}");
                    }
                    break;
                }

                // For drag-drop, also refresh the given card (e.g., twine being used to craft)
                if (isDragDrop && lastGivenCard != null)
                {
                    if (!TryRefreshGivenCard())
                    {
                        // Source card exhausted (or all dirty items washed)
                        if (completed > 0)
                        {
                            string msg = IsWashAction()
                                ? $"Done - all washed ({completed}/{count})"
                                : $"Source exhausted ({completed}/{count})";
                            Plugin.ShowNotification(msg);
                            Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Given card exhausted after {completed} iterations");
                        }
                        else
                        {
                            Plugin.ShowNotification($"Source card missing ({completed}/{count})");
                            Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Given card missing at {i + 1}/{count}");
                        }
                        break;
                    }

                    // Check if given card has enough quantity for drag-drop crafting.
                    // BUT: tools (axes, knives) report qty=0 via GetCardItemQuantity because
                    // they track durability differently. Only treat as exhausted if the card
                    // is actually destroyed � a living card with qty=0 is just a tool.
                    float? givenQuantity = GetCardItemQuantity(lastGivenCard);
                    if (givenQuantity.HasValue && givenQuantity.Value < 0.01f)
                    {
                        if (IsCardDestroyed(lastGivenCard))
                        {
                            Plugin.ShowNotification($"Source exhausted ({completed}/{count})");
                            Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Given card destroyed and qty=0");
                            break;
                        }
                        else
                        {
                            Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Given card qty={givenQuantity.Value} but card alive (tool?) - continuing");
                        }
                    }
                }

                // For consumables AND receiving card in drag-drop, check quantity before executing
                if (requireQuantityChange || (isDragDrop && lastReceivingCard != null))
                {
                    // First verify card still exists
                    if (lastReceivingCard != null)
                    {
                        var asUnity = lastReceivingCard as UnityEngine.Object;
                        if (asUnity == null || !asUnity)
                        {
                            if (completed > 0)
                            {
                                Plugin.ShowNotification($"Done - all consumed ({completed}/{count})");
                                Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Receiving card destroyed after {completed} iterations");
                            }
                            else
                            {
                                Plugin.ShowNotification($"Card missing ({completed}/{count})");
                                Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Receiving card destroyed at {i + 1}/{count}");
                            }
                            break;
                        }
                    }

                    // Then check quantity (both for eating actions and drag-drop receiving cards)
                    float? currentQuantity = GetCardItemQuantity(lastReceivingCard);
                    if (currentQuantity.HasValue && currentQuantity.Value < 0.01f)
                    {
                        bool isWash = IsWashAction();
                        if (completed > 0)
                        {
                            string msg = isWash
                                ? $"Done - water empty ({completed}/{count})"
                                : $"Done - all consumed ({completed}/{count})";
                            Plugin.ShowNotification(msg);
                            Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] No quantity remaining after {completed} iterations (qty={currentQuantity.Value})");
                        }
                        else
                        {
                            string msg = isWash
                                ? $"Water source empty ({completed}/{count})"
                                : $"No quantity available ({completed}/{count})";
                            Plugin.ShowNotification(msg);
                            Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] No quantity remaining (qty={currentQuantity.Value}) at {i + 1}/{count}");
                        }
                        break;
                    }
                    
                    if (currentQuantity.HasValue)
                    {
                        Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Pre-execution quantity check: {currentQuantity.Value} remaining");
                    }
                }

                // --- Validate action still available (before executing) ---
                // Skip validation for group inventory actions (Forage, Clear, Harvest) —
                // their captured action objects become stale (conditions check fails on
                // dynamically-generated action lists). The game validates internally when
                // OnGroupInventoryActionClicked is invoked.
                if (isTravelAction)
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Travel pre-validate: action={lastAction != null}, card={lastReceivingCard != null}, cardless={cardlessAction}, idx={lastActionIndex}");
                if (!lastIsGroupInventoryAction)
                {
                    bool canPerform = CheckActionAvailable();
                    if (!canPerform)
                    {
                        Plugin.ShowNotification($"Action unavailable ({completed}/{count})");
                        Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Conditions not met at {i + 1}/{count} (action={lastAction != null}, card={lastReceivingCard != null})");
                        break;
                    }
                }

                // Record blocker state BEFORE execution (for detecting NEW blockers after action)
                bool blockerBeforeExecution = Plugin.StopOnLowStats.Value && HasActionBlockers();

                // Determine if this is specifically a drink action (not eat)
                bool isDrinkAction = (lastActionName ?? "").IndexOf("drink", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isEatAction = requireQuantityChange && !isDrinkAction;

                // Snapshot quantity for consumable actions (Eat/Drink) to verify real progress.
                float? quantityBefore = requireQuantityChange ? GetCardItemQuantity(lastReceivingCard) : null;
                
                // For drink actions specifically, snapshot the liquid quantity
                float? liquidBefore = isDrinkAction ? GetLiquidQuantity(lastReceivingCard) : null;
                if (isDrinkAction)
                {
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Pre-drink: liquid={liquidBefore}, qty={quantityBefore}");
                }
                
                // Snapshot given card quantity for drag-drop consumption verification
                float? givenQuantityBefore = isDragDrop ? GetCardItemQuantity(lastGivenCard) : null;
                if (isDragDrop)
                {
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Pre-drag quantities: given={givenQuantityBefore}, receiving={GetCardItemQuantity(lastReceivingCard)}");
                }
                
                // For eat actions, save the actual Unity Object reference to detect destruction
                UnityEngine.Object receivingCardBeforeExecution = null;
                if (requireQuantityChange && lastReceivingCard != null)
                {
                    receivingCardBeforeExecution = lastReceivingCard as UnityEngine.Object;
                }
                
                // --- Execute action ---
                bool executed = false;

                // Special handling for drag-drop actions
                if (isDragDrop)
                {
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] Drag-drop action - calling CardOnCardActionRoutine");
                    executed = TryDragDropExecution(gm as MonoBehaviour);
                }
                else if (isTravelAction)
                {
                    // TRAVEL: Use standard popup approach � RefreshTravelContext already
                    // identified the correct location card and direction button index.
                    // EnsurePopupHasCard handles opening/verifying the popup internally.
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Travel exec: '{lastActionName}' button #{lastActionIndex} on {GetCardDisplayName(lastReceivingCard)}");

                    // Brief idle gate � pre-travel rest should have left us idle
                    float prePopupWait = 0f;
                    while (!IsGameIdle() && prePopupWait < 3f)
                    {
                        yield return new WaitForSeconds(0.1f);
                        prePopupWait += 0.1f;
                    }
                    if (prePopupWait > 0f)
                        Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Travel idle wait: {prePopupWait:F1}s");
                    yield return null;

                    for (int popupAttempt = 0; popupAttempt < 3 && !executed; popupAttempt++)
                    {
                        if (popupAttempt > 0)
                        {
                            Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Travel popup retry {popupAttempt}/2");
                            yield return new WaitForSeconds(0.5f);
                            RefreshTravelContext();
                        }

                        bool popupReady = EnsurePopupHasCard();
                        Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Travel EnsurePopupHasCard: {popupReady}");
                        if (popupReady)
                        {
                            yield return null;
                            yield return null;
                            float popupWait = 0f;
                            while (!IsGameIdle() && popupWait < 2f)
                            {
                                yield return null;
                                popupWait += Time.deltaTime;
                            }
                            executed = ClickPopupButton();
                            Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Travel ClickPopupButton: {executed}");
                        }
                        else
                        {
                            // EnsurePopupHasCard failed � force-open the card manually
                            Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] Travel popup not ready, force-opening card");
                            if (TryOpenTargetCardPopup())
                            {
                                yield return null;
                                yield return null;
                                lastInspectionPopup = FindActiveInspectionPopup(lastReceivingCard);
                                if (lastInspectionPopup != null)
                                {
                                    yield return null;
                                    executed = ClickPopupButton();
                                    Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Travel force-open click: {executed}");
                                }
                                else
                                {
                                    Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] Travel popup not found after force-open");
                                }
                            }
                            else
                            {
                                Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] Failed to open location card");
                            }
                        }
                    }

                    // Fallback: try direct ActionRoutine call
                    if (!executed)
                    {
                        Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] Travel popup failed, trying direct execution");
                        executed = TryDirectExecution(gm as MonoBehaviour);
                        Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Travel direct execution: {executed}");
                    }
                }
                else if (cardlessAction)
                {
                    // Cardless actions (meditate, rest) are more reliable through ActionRoutine.
                    executed = TryDirectExecution(gm as MonoBehaviour);
                    if (!executed)
                    {
                        Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] Direct call failed, trying popup replay");
                        executed = TryPopupReplay();
                    }
                }
                else if (isDrinkAction)
                {
                    // DRINK: liquid receiving cards (LQ_Water inside containers like
                    // unsealed storage pots, clay jars, kettles) have no on-board
                    // CardGraphics, so the popup-reopen path can't navigate to them.
                    // Direct ActionRoutine works for drink because only liquid quantity
                    // changes — no card destruction, no popup state to maintain.
                    executed = TryDirectExecution(gm as MonoBehaviour);
                    if (!executed)
                    {
                        Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] Drink direct call failed, trying popup approach");
                        for (int popupAttempt = 0; popupAttempt < 3 && !executed; popupAttempt++)
                        {
                            if (popupAttempt > 0)
                            {
                                yield return new WaitForSeconds(0.3f);
                                if (lastReceivingCard != null)
                                    TryRefreshReceivingCard();
                            }
                            if (EnsurePopupHasCard())
                            {
                                yield return null;
                                yield return null;
                                float popupWait = 0f;
                                while (!IsGameIdle() && popupWait < 1f)
                                {
                                    yield return null;
                                    popupWait += Time.deltaTime;
                                }
                                executed = ClickPopupButton();
                            }
                        }
                    }

                    if (!executed)
                    {
                        Plugin.ShowNotification($"Can't drink - target unreachable ({completed}/{count})");
                        Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] Drink: both direct and popup paths failed");
                        break;
                    }
                }
                else if (requireQuantityChange)
                {
                    // EAT: Must use popup-based approach � the game requires the
                    // InspectionPopup to be active for eat to consume cards.
                    // PerformAction/ActionRoutine bypass popup state and silently fail.
                    // IMPORTANT: If popup can't be opened, break IMMEDIATELY.
                    // Do NOT fall through to general retry � clicking a stale popup
                    // queues action on the next card the player opens.
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] Eat action - using popup approach");

                    // Try popup with retries � after an action, popup may need time to reopen
                    for (int popupAttempt = 0; popupAttempt < 3 && !executed; popupAttempt++)
                    {
                        if (popupAttempt > 0)
                        {
                            Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Popup retry {popupAttempt}/2 for eat...");
                            // Wait before retrying � game needs time to finish previous action
                            yield return new WaitForSeconds(0.3f);
                            if (lastReceivingCard != null)
                                TryRefreshReceivingCard();
                        }

                        bool popupReady = EnsurePopupHasCard();
                        if (popupReady)
                        {
                            yield return null;
                            yield return null;
                            float popupWait = 0f;
                            while (!IsGameIdle() && popupWait < 1f)
                            {
                                yield return null;
                                popupWait += Time.deltaTime;
                            }
                            executed = ClickPopupButton();
                        }
                    }

                    // Eat: if popup failed, stop immediately � do NOT use general retry
                    if (!executed)
                    {
                        Plugin.ShowNotification($"Can't eat - no target card ({completed}/{count})");
                        Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] Eat popup failed - stopping to prevent queued actions");
                        break;
                    }
                }
                else
                {
                    // ALL other popup-based actions (craft, forage, clear, etc.):
                    // Open popup -> wait for idle + button population -> click.
                    // Retry popup with waits if first attempt fails (popup may close after action).
                    for (int popupAttempt = 0; popupAttempt < 3 && !executed; popupAttempt++)
                    {
                        if (popupAttempt > 0)
                        {
                            Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Popup retry {popupAttempt}/2...");
                            yield return new WaitForSeconds(0.3f);
                            if (lastReceivingCard != null)
                                TryRefreshReceivingCard();
                        }

                        bool popupReady = EnsurePopupHasCard();
                        if (popupReady)
                        {
                            yield return null;
                            yield return null;
                            float popupWait = 0f;
                            while (!IsGameIdle() && popupWait < 1f)
                            {
                                yield return null;
                                popupWait += Time.deltaTime;
                            }
                            executed = ClickPopupButton();
                            Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Popup click attempt {popupAttempt}: ready={popupReady}, clicked={executed}");
                        }
                        else
                        {
                            Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Popup attempt {popupAttempt}: NOT ready");
                        }
                    }

                    if (!executed && !lastIsGroupInventoryAction)
                    {
                        // Direct execution uses captured ActionRoutine args — skip for group
                        // inventory actions since their captured action objects become stale.
                        Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] Popup retries failed, trying direct call");
                        executed = TryDirectExecution(gm as MonoBehaviour);
                        Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Direct execution: {executed}");
                    }
                }

                // Retry loop if initial execution failed
                if (!executed)
                {
                    for (int retry = 1; retry <= 3 && !executed; retry++)
                    {
                        Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Execution failed, retry {retry}/3");
                        yield return null;

                        if (lastReceivingCard != null)
                            TryRefreshReceivingCard();

                        bool opened = TryOpenTargetCardPopup();
                        if (!opened)
                        {
                            Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] Can't open target card - skipping button click");
                            continue;
                        }
                        yield return null;
                        yield return null;

                        executed = ClickPopupButton();
                    }

                    if (!executed)
                    {
                        Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] Popup retries exhausted, trying direct ActionRoutine call");
                        {
                            if (lastReceivingCard != null)
                                TryRefreshReceivingCard();

                            executed = TryDirectExecution(gm as MonoBehaviour);
                        }
                    }

                    if (!executed)
                        Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] All replay methods failed");
                }

                if (!executed)
                {
                    Plugin.ShowNotification($"Execution failed ({completed}/{count})");
                    Logger.LogInfo($"[Repeat] All execution methods failed at {i + 1}/{count}");
                    break;
                }

                // CRITICAL: Two-phase idle wait for action completion.
                // Phase 1: Wait for the game to LEAVE idle state (action has started).
                //   Actions are async coroutines � after click/invoke, the game needs
                //   frames to transition out of idle. A fixed delay is unreliable because
                //   different actions take different amounts of time to start.
                // Phase 2: Wait for the game to RETURN to idle state (action complete).

                // Pre-Phase 1: Mandatory frame yields to let the action coroutine start.
                // Without this, the game state hasn't transitioned yet and Phase 1
                // immediately thinks the action never left idle (0.00s timeout).
                // 3 frames gives Unity time to process the invoked coroutine.
                yield return null;
                yield return null;
                yield return null;

                // Phase 1: Wait for game to leave idle (action starting)
                // Short timeout: instant actions (Make Clay) never leave idle.
                float startWait = 0f;
                float startTimeout = isDragDrop ? 2f : 0.5f;
                while (IsGameIdle() && startWait < startTimeout)
                {
                    yield return null;
                    startWait += Time.deltaTime;
                }

                if (startWait < startTimeout)
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Action left idle after {startWait:F2}s");

                // Phase 2: Wait for game to return to idle (action completing)
                float idleWaitTime = 0f;
                while (!IsGameIdle() && idleWaitTime < 30f)
                {
                    yield return new WaitForSeconds(0.1f);
                    idleWaitTime += 0.1f;
                    if (cancelRequested) break;
                }

                if (!IsGameIdle())
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] Action did not complete within timeout");

                // Final settling delay � actions need time to destroy/clear cards
                float settleTime = isDragDrop ? 0.2f : (requireQuantityChange ? 0.3f : 0f);
                if (settleTime > 0f)
                    yield return new WaitForSeconds(settleTime);

                // --- Post-execution validation ---

                if (isDrinkAction)
                {
                    // === DRINK VALIDATION: Card stays, liquid quantity should decrease ===
                    float? liquidAfter = GetLiquidQuantity(lastReceivingCard);
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Post-drink: liquid {liquidBefore} ? {liquidAfter}");

                    if (liquidBefore.HasValue && liquidAfter.HasValue)
                    {
                        if (liquidAfter.Value >= liquidBefore.Value - 0.001f)
                        {
                            Plugin.ShowNotification($"Stopped - no fluid change ({completed}/{count})");
                            Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] Drink had no liquid change; stopping");
                            break;
                        }

                        completed++;
                        Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Iteration {completed}/{count} executed (liquid consumed)");

                        if (liquidAfter.Value < 0.01f)
                        {
                            Plugin.ShowNotification($"Done - container empty ({completed}/{count})");
                            Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] Container is empty, stopping");
                            break;
                        }
                    }
                    else
                    {
                        // Fall back to generic quantity check
                        float? quantityAfter = GetCardItemQuantity(lastReceivingCard);
                        if (quantityBefore.HasValue && quantityAfter.HasValue && quantityAfter.Value < quantityBefore.Value - 0.001f)
                        {
                            completed++;
                            Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Iteration {completed}/{count} executed (quantity decreased)");
                        }
                        else
                        {
                            Plugin.ShowNotification($"Stopped - cannot verify drink ({completed}/{count})");
                            Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Cannot verify drink: liquidBefore={liquidBefore}, liquidAfter={liquidAfter}, qtyBefore={quantityBefore}, qtyAfter={quantityAfter}");
                            break;
                        }
                    }
                }
                else if (requireQuantityChange)
                {
                    // === EAT VALIDATION ===
                    // Use multiple indicators since stacked items don't change visible card count.
                    // Priority: game tick change > card Destroyed property > Unity Object destroyed > quantity change
                    int tickAfter = GetGameTick();
                    bool tickAdvanced = (tickBefore >= 0 && tickAfter >= 0 && tickAfter != tickBefore);
                    bool cardDestroyed = IsCardDestroyed(lastReceivingCard);
                    bool unityDestroyed = (receivingCardBeforeExecution != null && !receivingCardBeforeExecution);
                    
                    // Quantity-based fallback
                    float? quantityAfter = GetCardItemQuantity(lastReceivingCard);
                    bool quantityDecreased = (quantityBefore.HasValue && quantityAfter.HasValue && 
                                              quantityAfter.Value < quantityBefore.Value - 0.001f);

                    Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Post-eat: tick {tickBefore}?{tickAfter} (adv={tickAdvanced}), destroyed={cardDestroyed}, unityDead={unityDestroyed}, qty={quantityBefore}?{quantityAfter}");

                    if (tickAdvanced || cardDestroyed || unityDestroyed || quantityDecreased)
                    {
                        completed++;
                        Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Iteration {completed}/{count} executed (eat verified)");

                        // If card was destroyed/consumed, try to find another of same type
                        if (cardDestroyed || unityDestroyed)
                        {
                            if (receivingUniqueId != null && TryRefreshReceivingCard())
                            {
                                Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] Found another card of same type, continuing");
                            }
                            else
                            {
                                Plugin.ShowNotification($"Done - all consumed ({completed}/{count})");
                                Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] No more cards of this type remaining");
                                break;
                            }
                        }
                    }
                    else
                    {
                        Plugin.ShowNotification($"Stopped - no change ({completed}/{count})");
                        Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Eat action had no detectable effect; stopping");
                        break;
                    }
                }
                else if (isDragDrop)
                {
                    // Drag-drop validation: check if resources are EXHAUSTED (not consumed)
                    // Many drag-drop items don't expose readable quantities, so assume success
                    // and only stop if we detect definitive exhaustion
                    
                    TryRefreshGivenCard();
                    TryRefreshReceivingCard();
                    
                    float? givenQuantityAfter = GetCardItemQuantity(lastGivenCard);
                    float? receivingQuantityAfter = GetCardItemQuantity(lastReceivingCard);
                    
                    // Log quantity changes for both cards (critical for diagnosing consumption issues)
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Post-drag quantities: given={givenQuantityBefore}\u2192{givenQuantityAfter}, receiving={receivingQuantityAfter}");

                    if (IsWashAction())
                    {
                        // === WASH VALIDATION ===
                        // Wash transforms the given card (dirty item) into a clean version.
                        // Given card destroyed = success. Verify via game tick advancement.
                        int washTickAfter = GetGameTick();
                        bool washTickAdvanced = (tickBefore >= 0 && washTickAfter >= 0 && washTickAfter != tickBefore);

                        if (!washTickAdvanced)
                        {
                            Plugin.ShowNotification($"Stopped - wash had no effect ({completed}/{count})");
                            Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] Wash action had no tick change; stopping");
                            break;
                        }

                        completed++;
                        Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Iteration {completed}/{count} executed (wash)");

                        // Check water supply on receiving card (river vs container).
                        // Rivers return null (infinite water); containers return liquid level.
                        float? waterLevel = GetLiquidQuantity(lastReceivingCard);
                        if (waterLevel.HasValue && waterLevel.Value < 0.01f)
                        {
                            Plugin.ShowNotification($"Done - water empty ({completed}/{count})");
                            Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Water source empty (liquid={waterLevel.Value}), stopping");
                            break;
                        }

                        // Check if more dirty items are available to wash.
                        // TryRefreshGivenCard (called above) already searched for a replacement.
                        bool hasMoreItems = false;
                        if (lastGivenCard != null)
                        {
                            var asUnity = lastGivenCard as UnityEngine.Object;
                            hasMoreItems = (asUnity != null && (bool)asUnity);
                        }

                        if (!hasMoreItems)
                        {
                            Plugin.ShowNotification($"Done - all washed ({completed}/{count})");
                            Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] No more dirty items to wash");
                            break;
                        }
                    }
                    else
                    {
                    // Check if given card is exhausted (destroyed)
                    // Tools (axes, knives) report qty=0 via GetCardItemQuantity because they
                    // track durability differently. Only treat as exhausted if actually destroyed.
                    bool givenExhausted = false;
                    if (lastGivenCard != null)
                    {
                        var asUnity = lastGivenCard as UnityEngine.Object;
                        if (asUnity == null || !asUnity)
                        {
                            givenExhausted = true;
                            Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] Given card destroyed");
                        }
                    }

                    if (givenExhausted)
                    {
                        completed++;
                        Plugin.ShowNotification($"Done - source exhausted ({completed}/{count})");
                        Logger.LogInfo($"[Repeat] STOP: given card exhausted ({completed}/{count})");
                        break;
                    }

                    // Check if receiving card is exhausted (destroyed/consumed by the action)
                    // For repeatable targets like trees, the card is destroyed each time
                    // but respawns on the next game tick. Wait for tick + find replacement.
                    bool receivingExhausted = false;
                    if (lastReceivingCard != null)
                    {
                        var asUnity = lastReceivingCard as UnityEngine.Object;
                        if (asUnity == null || !asUnity)
                        {
                            receivingExhausted = true;
                            Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] Receiving card destroyed");
                        }
                        else if (receivingQuantityAfter.HasValue && receivingQuantityAfter.Value < 0.01f)
                        {
                            receivingExhausted = true;
                            Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Receiving quantity exhausted: {receivingQuantityAfter.Value}");
                        }
                    }

                    if (receivingExhausted)
                    {
                        completed++;
                        Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Iteration {completed}/{count} executed (receiving consumed)");

                        // If more iterations remain, rest to advance the tick, then
                        // find a replacement target (e.g., trees respawn after chop)
                        if (i < count - 1)
                        {
                            Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] Receiving consumed, resting to advance tick...");
                            bool restAndFound = false;
                            for (int restAttempt = 0; restAttempt < 3 && !cancelRequested; restAttempt++)
                            {
                                bool rested = TryPerformRestAction(gm as MonoBehaviour);
                                if (!rested)
                                {
                                    Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] Rest action failed, falling back to time wait");
                                    yield return new WaitForSeconds(2f);
                                }
                                else
                                {
                                    float restWait = 0f;
                                    while (!IsGameIdle() && restWait < 10f)
                                    {
                                        yield return new WaitForSeconds(0.1f);
                                        restWait += 0.1f;
                                        if (cancelRequested) break;
                                    }
                                    Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Rest completed after {restWait:F1}s");
                                    yield return new WaitForSeconds(0.3f);
                                }

                                if (TryRefreshReceivingCard())
                                {
                                    restAndFound = true;
                                    Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] Found respawned target after rest");
                                    break;
                                }
                            }
                            if (cancelRequested) break;
                            if (restAndFound)
                            {
                                continue; // Next iteration
                            }
                            else
                            {
                                Plugin.ShowNotification($"Done - no more targets ({completed}/{count})");
                                Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] No replacement target found after rest");
                                break;
                            }
                        }
                        else
                        {
                            // Last iteration anyway, just finish
                            break;
                        }
                    }

                    // Otherwise assume success - action executed and resources are still available
                    completed++;
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Iteration {completed}/{count} executed");

                    // For chop/cut/fell actions on big trees: rest between iterations
                    // to let the player recover stamina and avoid exhaustion.
                    if (i < count - 1 && IsChopAction())
                    {
                        Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] Resting between chops...");
                        bool rested = TryPerformRestAction(gm as MonoBehaviour);
                        if (rested)
                        {
                            float restWait = 0f;
                            while (!IsGameIdle() && restWait < 10f)
                            {
                                yield return new WaitForSeconds(0.1f);
                                restWait += 0.1f;
                                if (cancelRequested) break;
                            }
                            Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Rest between chops completed after {restWait:F1}s");
                            yield return new WaitForSeconds(0.2f);
                        }
                    }
                    } // end non-wash drag-drop
                }
                else
                {
                    // Generic popup action completed. Check if the game tick advanced
                    // as a basic indicator that something actually happened.
                    int genericTickAfter = GetGameTick();
                    bool genericTickAdvanced = (tickBefore >= 0 && genericTickAfter >= 0 && genericTickAfter != tickBefore);

                    // Check if the receiving card was consumed/transformed by this action
                    bool cardConsumed = false;
                    bool cardTransformed = false;
                    if (lastReceivingCard != null && !cardlessAction)
                    {
                        var asUnity = lastReceivingCard as UnityEngine.Object;
                        if (asUnity == null || !asUnity)
                        {
                            cardConsumed = true;
                        }
                        else
                        {
                            // Card alive but may have transformed (e.g., nettle stem -> fibers)
                            string postId = GetCardUniqueId(lastReceivingCard);
                            if (!string.IsNullOrEmpty(receivingUniqueId) && !string.IsNullOrEmpty(postId)
                                && !string.Equals(postId, receivingUniqueId, StringComparison.Ordinal))
                            {
                                cardConsumed = true;
                                cardTransformed = true;
                                Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Card transformed mid-action: '{receivingUniqueId}' -> '{postId}'");
                            }
                        }
                    }

                    // If neither tick advanced nor card consumed, the action had no effect — stop
                    if (!genericTickAdvanced && !cardConsumed && !cardlessAction)
                    {
                        Plugin.ShowNotification($"Stopped - no effect ({completed}/{count})");
                        Logger.LogInfo($"[Repeat] STOP: no effect at {i + 1}/{count} (tickBefore={tickBefore}, tickAfter={genericTickAfter}, consumed={cardConsumed}, cardless={cardlessAction})");
                        break;
                    }

                    completed++;
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Iteration {completed}/{count} OK (tick={genericTickAdvanced}, consumed={cardConsumed})");

                    // If card was consumed, try to find a replacement for next iteration
                    if (cardConsumed && i < count - 1)
                    {
                        // If card transformed (not destroyed), search using original UniqueID, not the transformed card
                        if (cardTransformed)
                        {
                            // Force refresh to search for original card type, not the transformation result
                            if (!TryRefreshReceivingCardByOriginalId(receivingUniqueId))
                            {
                                Plugin.ShowNotification($"Done - no more cards ({completed}/{count})");
                                Logger.LogInfo($"[Repeat] STOP: no more cards after transform (original='{receivingUniqueId}')");
                                break;
                            }
                        }
                        else if (!TryRefreshReceivingCard())
                        {
                            Plugin.ShowNotification($"Done - no more cards ({completed}/{count})");
                            Logger.LogInfo("[Repeat] STOP: no replacement card after consumption");
                            break;
                        }
                        Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] Found replacement card, continuing");
                    }
                }

                // --- Check for NEW blockers caused by this action ---
                // Only stop if a blocker appeared during execution (wasn't there before)
                if (Plugin.StopOnLowStats.Value && !blockerBeforeExecution && HasActionBlockers())
                {
                    Plugin.ShowNotification($"Stopped - Event triggered! ({completed}/{count})");
                    Logger.LogInfo($"[Repeat] STOP: blocker triggered at {completed}/{count}");
                    break;
                }
            }

            isRepeating = false;
            // Only show "Done" if at least 1 completed — otherwise preserve the intermediate
            // error message (e.g. "Stopped - no effect" or "Execution failed") so the user
            // can see why the repeat failed.
            if (completed > 0)
                Plugin.ShowNotification($"Done ({completed}/{count})");
            Logger.LogInfo($"[Repeat] Complete: {completed}/{count}");
        }

        // =====================================================================
        // EXECUTION METHODS
        // =====================================================================

        /// <summary>
        /// Ensures the InspectionPopup is open and showing the correct target card.
        /// Does NOT click any button � call ClickPopupButton() separately after yielding.
        /// </summary>
        private static bool EnsurePopupHasCard()
        {
            try
            {
                // Find/verify popup
                bool hasClickMethod = (lastIsGroupInventoryAction && ipOnGroupInventoryActionClickedMethod != null)
                    || ipOnButtonClickedMethod != null;
                if (lastInspectionPopup == null || !hasClickMethod || lastActionIndex < 0)
                    lastInspectionPopup = FindActiveInspectionPopup(lastReceivingCard);

                if (lastInspectionPopup == null || !hasClickMethod || lastActionIndex < 0)
                    return false;

                var popupUnity = lastInspectionPopup as UnityEngine.Object;
                if (popupUnity == null || !popupUnity)
                {
                    lastInspectionPopup = FindActiveInspectionPopup(lastReceivingCard);
                    if (lastInspectionPopup == null)
                        return false;
                }

                if (ipCurrentCardProp == null) return true; // Can't check, assume ok

                var currentCard = ipCurrentCardProp.GetValue(lastInspectionPopup);
                if (currentCard == null || (lastReceivingCard != null && !IsSameCardTarget(currentCard, lastReceivingCard)))
                {
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] Opening target card in popup...");
                    if (!TryOpenTargetCardPopup()) return false;
                    currentCard = ipCurrentCardProp.GetValue(lastInspectionPopup);
                    if (currentCard == null)
                    {
                        Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] Popup still has no current card after open");
                        return false;
                    }
                }

                string currentCardName = GetCardDisplayName(currentCard);
                Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Popup ready with card: {currentCardName}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[Repeat] EnsurePopupHasCard error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Clicks the stored button index on the popup. Call AFTER EnsurePopupHasCard + yield.
        /// </summary>
        private static bool ClickPopupButton()
        {
            try
            {
                if (lastInspectionPopup == null || lastActionIndex < 0)
                    return false;

                var popupUnity = lastInspectionPopup as UnityEngine.Object;
                if (popupUnity == null || !popupUnity) return false;

                return InvokePopupClick();
            }
            catch (Exception ex)
            {
                Logger.LogError($"[Repeat] ClickPopupButton error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Primary replay: Call InspectionPopup.OnButtonClicked with the saved index.
        /// The game will re-validate everything and execute if valid.
        /// NOTE: This opens popup + clicks atomically. Prefer EnsurePopupHasCard + yield + ClickPopupButton.
        /// </summary>
        private static bool TryPopupReplay()
        {
            try
            {
                if (lastInspectionPopup == null || lastActionIndex < 0)
                {
                    lastInspectionPopup = FindActiveInspectionPopup(lastReceivingCard);
                }

                // Need at least one click method available
                bool hasClickMethod = (lastIsGroupInventoryAction && ipOnGroupInventoryActionClickedMethod != null)
                    || ipOnButtonClickedMethod != null;
                if (lastInspectionPopup == null || !hasClickMethod || lastActionIndex < 0)
                    return false;

                // Verify the popup's Unity object is still alive
                var popupUnity = lastInspectionPopup as UnityEngine.Object;
                if (popupUnity == null || !popupUnity)
                {
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] InspectionPopup destroyed, trying FindObjectOfType");
                    lastInspectionPopup = FindActiveInspectionPopup(lastReceivingCard);
                    if (lastInspectionPopup == null)
                        return false;
                }

                // Verify popup has the correct card open; if not, reopen target card first.
                if (ipCurrentCardProp != null)
                {
                    var currentCard = ipCurrentCardProp.GetValue(lastInspectionPopup);
                    if (currentCard == null)
                    {
                        if (lastReceivingCard == null)
                        {
                            Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] Popup current card is null (cardless action), continuing");
                            return InvokePopupClick();
                        }

                        Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] Popup has no current card, attempting to open target card");
                        if (!TryOpenTargetCardPopup()) return false;
                        currentCard = ipCurrentCardProp.GetValue(lastInspectionPopup);
                        if (currentCard == null)
                        {
                            Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] Popup still has no current card after open attempt");
                            return false;
                        }
                    }

                    string currentCardName = GetCardDisplayName(currentCard);
                    string targetCardName = GetCardDisplayName(lastReceivingCard);
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Popup card: {currentCardName}, target: {targetCardName}");

                    if (!IsSameCardTarget(currentCard, lastReceivingCard))
                    {
                        Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] Popup card mismatch, attempting to open target card");
                        if (!TryOpenTargetCardPopup()) return false;

                        currentCard = ipCurrentCardProp.GetValue(lastInspectionPopup);
                        if (currentCard == null || !IsSameCardTarget(currentCard, lastReceivingCard))
                        {
                            Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] Failed to focus target card in popup");
                            return false;
                        }
                    }
                }

                return InvokePopupClick();
            }
            catch (Exception ex)
            {
                Logger.LogError($"[Repeat] TryPopupReplay error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Invokes the correct popup click method based on whether this is a group inventory action.
        /// </summary>
        private static bool InvokePopupClick()
        {
            if (lastInspectionPopup == null || lastActionIndex < 0) return false;
            if (lastIsGroupInventoryAction && ipOnGroupInventoryActionClickedMethod != null)
            {
                Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Clicking group action #{lastActionIndex} via OnGroupInventoryActionClicked");
                ipOnGroupInventoryActionClickedMethod.Invoke(lastInspectionPopup, new object[] { lastActionIndex });
                return true;
            }
            if (ipOnButtonClickedMethod != null)
            {
                Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Clicking button #{lastActionIndex} via OnButtonClicked");
                ipOnButtonClickedMethod.Invoke(lastInspectionPopup, new object[] { lastActionIndex, false });
                return true;
            }
            return false;
        }

        /// <summary>
        /// Find and execute a "Rest" action to advance the game tick.
        /// Used between drag-drop chops when the target respawns on tick boundaries.
        /// Scans all visible cards for a DismantleAction named "Rest" and invokes it.
        /// </summary>
        private static bool TryPerformRestAction(MonoBehaviour gmMono)
        {
            try
            {
                if (gmMono == null) return false;

                if (gameManagerActionRoutineMethod == null)
                {
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] ActionRoutine not found for rest");
                    return false;
                }

                object restAction = cachedRestAction;

                // Validate cached action is still alive (ScriptableObject can survive scenes)
                if (restAction != null)
                {
                    var unityObj = restAction as UnityEngine.Object;
                    if (unityObj == null || !unityObj)
                    {
                        Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] Cached rest action destroyed, re-scanning");
                        restAction = null;
                        cachedRestAction = null;
                    }
                }

                // Primary: find Rest in SpecialActionSet (the WAITING popup / TimeSkipOptions)
                // Rest is NOT on any card's DismantleActions � it lives on a SpecialActionSet.
                if (restAction == null)
                {
                    var specialActionSetType = AccessTools.TypeByName("SpecialActionSet");
                    if (specialActionSetType != null)
                    {
                        var allSets = Resources.FindObjectsOfTypeAll(specialActionSetType);
                        Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Found {allSets.Length} SpecialActionSet(s)");
                        foreach (var set in allSets)
                        {
                            var actionsField = AccessTools.Field(set.GetType(), "Actions");
                            var actions = actionsField?.GetValue(set) as System.Collections.IList;
                            if (actions == null || actions.Count == 0) continue;

                            for (int i = 0; i < actions.Count; i++)
                            {
                                string name = GetActionDisplayName(actions[i]);
                                if (string.Equals(name, "Rest", StringComparison.OrdinalIgnoreCase))
                                {
                                    restAction = actions[i];
                                    cachedRestAction = restAction;
                                    string setName = (set as UnityEngine.Object)?.name ?? "unknown";
                                    Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Found Rest in SpecialActionSet '{setName}' at index {i}");
                                    break;
                                }
                            }
                            if (restAction != null) break;
                        }
                    }
                    else
                    {
                        Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] SpecialActionSet type not found");
                    }
                }

                // Fallback: scan card DismantleActions (for modded furniture like chairs/stools)
                if (restAction == null && cardGraphicsType != null)
                {
                    var allGraphics = UnityEngine.Object.FindObjectsOfType(cardGraphicsType);
                    foreach (var graphics in allGraphics)
                    {
                        if (graphics == null) continue;
                        var card = GetCardFromGraphics(graphics);
                        if (card == null || IsCardDestroyed(card)) continue;

                        var cardModel = AccessTools.Property(card.GetType(), "CardModel")?.GetValue(card);
                        if (cardModel == null) continue;

                        var dismantleActions = AccessTools.Field(cardModel.GetType(), "DismantleActions")?.GetValue(cardModel);
                        if (!(dismantleActions is System.Collections.IList actionList) || actionList.Count == 0)
                            continue;

                        for (int idx = 0; idx < actionList.Count; idx++)
                        {
                            string actionName = GetActionDisplayName(actionList[idx]);
                            if (string.Equals(actionName, "Rest", StringComparison.OrdinalIgnoreCase))
                            {
                                restAction = actionList[idx];
                                cachedRestAction = restAction;
                                Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Found Rest on card {GetCardDisplayName(card)}");
                                break;
                            }
                        }
                        if (restAction != null) break;
                    }
                }

                if (restAction == null)
                {
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] No 'Rest' action found in SpecialActionSets or cards");
                    return false;
                }

                // Invoke Rest via ActionRoutine (null receiving card � Rest is cardless)
                Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] Invoking Rest action...");
                var coroutine = gameManagerActionRoutineMethod.Invoke(gmMono, new object[]
                {
                    restAction,
                    null,      // Rest has no receiving card
                    lastUser,
                    false,     // _FastMode
                    false,     // _DontPlaySounds
                    false,     // _ModifiersAlreadyCollected
                    null       // _GivenCard
                });

                if (coroutine is IEnumerator enumerator)
                {
                    gmMono.StartCoroutine(enumerator);
                    return true;
                }

                Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] ActionRoutine did not return coroutine");
                return false;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[Repeat] TryPerformRestAction error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Fallback replay: Call GameManager.ActionRoutine directly.
        /// Uses exact captured args from the original call, only refreshing card references.
        /// </summary>
        private static bool TryDirectExecution(MonoBehaviour gmMono)
        {
            try
            {
                if (gmMono == null || lastAction == null) return false;

                // Prefer exact captured args if available
                if (lastActionRoutineArgs != null && lastActionRoutineMethod != null)
                {
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Direct ActionRoutine for '{lastActionName}' (using captured args)");

                    // Clone and update card references
                    object[] args = (object[])lastActionRoutineArgs.Clone();
                    if (lastArReceivingIdx >= 0) args[lastArReceivingIdx] = lastReceivingCard;
                    if (lastArGivenIdx >= 0) args[lastArGivenIdx] = lastGivenCard;

                    var coroutine = lastActionRoutineMethod.Invoke(gmMono, args);
                    if (coroutine is IEnumerator enumerator)
                    {
                        gmMono.StartCoroutine(enumerator);
                        return true;
                    }
                    return false;
                }

                // Fallback: construct args manually
                if (gameManagerActionRoutineMethod == null) return false;

                Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Direct ActionRoutine for '{lastActionName}' (fallback args)");

                var fallbackCoroutine = gameManagerActionRoutineMethod.Invoke(gmMono, new object[]
                {
                    lastAction,
                    lastReceivingCard,
                    lastUser,
                    false,  // _FastMode
                    false,  // _DontPlaySounds
                    false,  // _ModifiersAlreadyCollected
                    lastGivenCard
                });

                if (fallbackCoroutine is IEnumerator fallbackEnum)
                {
                    gmMono.StartCoroutine(fallbackEnum);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[Repeat] TryDirectExecution error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Execute a drag-drop action by replaying CardOnCardActionRoutine with exact captured args.
        /// CardOnCardActionRoutine properly consumes BOTH given and receiving cards.
        /// Falls back to ActionRoutine if CardOnCardActionRoutine is unavailable.
        /// </summary>
        private static bool TryDragDropExecution(MonoBehaviour gmMono)
        {
            try
            {
                if (gmMono == null || lastAction == null || lastGivenCard == null || lastReceivingCard == null)
                {
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] Cannot execute drag-drop: missing action/cards");
                    return false;
                }

                // PRIMARY: Use CardOnCardActionRoutine with exact captured args.
                // This properly consumes BOTH given and receiving cards.
                Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] TryDragDrop: cocArgs={lastCocArgs != null}, cocMethod={lastCocMethod != null}");
                if (lastCocArgs != null && lastCocMethod != null)
                {
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Drag-drop via CardOnCardActionRoutine for '{lastActionName}' " +
                        $"(given={GetCardDisplayName(lastGivenCard)}, receiving={GetCardDisplayName(lastReceivingCard)}, {lastCocArgs.Length} params)");

                    // Clone and update card references to refreshed ones
                    object[] args = (object[])lastCocArgs.Clone();
                    if (lastCocGivenParamIdx >= 0) args[lastCocGivenParamIdx] = lastGivenCard;
                    if (lastCocReceivingParamIdx >= 0) args[lastCocReceivingParamIdx] = lastReceivingCard;

                    var coroutine = lastCocMethod.Invoke(gmMono, args);
                    if (coroutine is IEnumerator enumerator)
                    {
                        gmMono.StartCoroutine(enumerator);
                        return true;
                    }
                    return false;
                }

                // FALLBACK: Use ActionRoutine (produces output but may not consume given card)
                if (gameManagerActionRoutineMethod != null)
                {
                    try
                    {
                        Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Fallback: ActionRoutine for drag-drop '{lastActionName}' (no captured CoCArgs)");

                        var coroutine = gameManagerActionRoutineMethod.Invoke(gmMono, new object[]
                        {
                            lastAction,
                            lastReceivingCard,
                            lastUser,
                            false,  // _FastMode
                            false,  // _DontPlaySounds
                            false,  // _ModifiersAlreadyCollected
                            lastGivenCard
                        });

                        if (coroutine is IEnumerator enumerator)
                        {
                            gmMono.StartCoroutine(enumerator);
                            return true;
                        }
                    }
                    catch (Exception arEx)
                    {
                        Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] ActionRoutine fallback failed: {arEx.Message}");
                    }
                }

                Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] No method available for drag-drop execution");
                return false;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[Repeat] TryDragDropExecution error: {ex.Message}");
                return false;
            }
        }

        // =====================================================================
        // VALIDATION
        // =====================================================================

        /// <summary>
        /// Check if the game is idle (SELECT state = 0) and ready for another action.
        /// </summary>
        private static bool IsGameIdle()
        {
            try
            {
                var gm = GetGameManager();
                if (gm == null) return false;

                if (gmCurrentGameStateField != null)
                {
                    var state = gmCurrentGameStateField.GetValue(gm);
                    return Convert.ToInt32(state) == 0; // GameStates.SELECT
                }

                return true;
            }
            catch { return true; }
        }

        /// <summary>
        /// Check if event blockers are active (dehydration popups, encounters, etc.)
        /// </summary>
        private static bool HasActionBlockers()
        {
            try
            {
                var gm = GetGameManager();
                if (gm == null) return false;

                var anyBlockers = AccessTools.Property(gameManagerType, "AnyActionBlockers");
                if (anyBlockers != null)
                {
                    var blocked = anyBlockers.GetValue(gm);
                    if (blocked is bool b && b)
                    {
                        Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Safety] Action blockers active");
                        return true;
                    }
                }

                return false;
            }
            catch { return false; }
        }

        /// <summary>
        /// Check if the action's conditions are still met using the game's own validation.
        /// </summary>
        private static bool CheckActionAvailable()
        {
            if (lastAction == null) return false;

            try
            {
                // Check receiving card still exists (not destroyed/consumed)
                if (lastReceivingCard != null)
                {
                    var asUnity = lastReceivingCard as UnityEngine.Object;
                    if (asUnity == null || !asUnity)
                    {
                        Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Validate] Receiving card destroyed");
                        return false;
                    }
                }

                // Use game's SimpleConditionsCheck
                if (cardActionSimpleConditionsCheck != null)
                {
                    var result = cardActionSimpleConditionsCheck.Invoke(lastAction, new object[] { lastReceivingCard, lastUser });
                    if (result is bool canDo)
                    {
                        Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Validate] SimpleConditionsCheck: {canDo}");
                        if (!canDo) return false;
                    }
                }

                // Use game's QuickRequirementsCheck
                if (cardActionQuickRequirementsCheck != null)
                {
                    var result = cardActionQuickRequirementsCheck.Invoke(lastAction, new object[] { lastReceivingCard, lastUser, true });
                    if (result is bool canDo2)
                    {
                        Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Validate] QuickRequirementsCheck: {canDo2}");
                        if (!canDo2) return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[Validate] Error: {ex.Message}");
                return true; // On error, try anyway - game will block invalid actions
            }
        }

        /// <summary>
        /// Permitted action allowlist. Only these actions can be captured and repeated.
        /// </summary>
        private static readonly string[] PermittedActionKeywords = new[]
        {
            "forage",
            "clear",
            "drink",
            "eat",
            "extract",     // Extract Fibers
            "chop",        // Chop Wood
            "cut",         // Cut Tree variant
            "fell",        // Fell a tree
            "twine",       // Make Twine
            "travel",      // Travel between locations
            "north",       // Directional travel
            "south",       // Directional travel
            "east",        // Directional travel
            "west",        // Directional travel
            "meditat",     // Meditate/Meditation
            "grind",       // Grind items to powder
            "craft",       // Craft Tourniquet, etc.
            "dig",         // Dig up mud
            "clay",        // Make clay from mud
            "mud",         // Mud-related actions
            "harvest",     // Harvest plants (meadow grass, nettles, etc.) by hand or with tools
            "soak",        // Soak Reeds
            "mine",        // Mine veins (flint, copper, witchstone)
            "sharpen",     // Sharpen / Sharpen Stone on tools and weapons
            "relax",       // Relax on furniture (log stool, etc.)
            "rest",        // Rest on chairs, stools, stamina bar, time skip
            "pick up",     // Pick up soaking reeds/flax/nettle stems
            // EA 0.61 Additions
            "tan",         // Tan leather
            "sew",         // Sew leather items
            "bless",       // Bless Grove ritual
            "curse",       // Curse Grove ritual
            "enchant",     // Enchantment rituals
            "invoke",      // Spirit invocation
            "summon",      // Spirit summoning
            "commune",     // Spirit communion
            "perform",     // Perform ritual actions
            "bind",        // Spirit binding actions
            "call",        // Spirit calling
            "wash",        // Wash dirty items (intestines, etc.) in water
            // Food & drink processing
            "mince",       // Mince meat/herbs
            "crush",       // Crush items
            "mix",         // Mix ingredients / Mix with Water
            "stir",        // Stir (cooking)
            "knead",       // Knead dough
            "peel",        // Peel & Cut vegetables
            "scrape",      // Scrape materials / Scrape Salt
            "carve",       // Carve wood/bone
            "fill",        // Fill containers (water, flour)
            "pour",        // Pour liquids (Pour Lye, etc.)
            "brew",        // Brew Fairybrew, Firebrew, etc.
            "ferment",     // Add Fermented items (wine making)
            // Tier 1 additions (action audit, 2026-04-26)
            "dismantle",   // Dismantle items into materials
            "feed",        // Feed Charcoal/Fuel/Firewood/Embers/Tinder/Husk/Pine Needles/etc.
            "repair",      // Repair clothing/tools/beds
            "apply",       // Apply, Apply to Wound, Apply Salve, Apply Insecticide/Fungicide/Poison
            "trim",        // Trim fur/skin
            "smith",       // Smith heated blanks
            "skin",        // Skin carcass
            "butcher",     // Butcher carcass
            "bleed",       // Bleed carcass
            "disembowel",  // Disembowel carcass
            "gut",         // Gut fish/small animals
            "flesh",       // Flesh fur/skin
            "mill",        // Mill grain on rotary quern
            "press",       // Press fat/seeds for oil
            "mash",        // Mash berries
            "thresh",      // Thresh stalks
            "stitch",      // Stitch wounds
            "thread",      // Thread needle
            "train",       // Train with weapon
            "practice",    // Practice / Practice Rock Throwing
            "add",         // Drag-drop ingredient additions (Add Eggs/Garlic/Fireroot/etc.)
            "shovel",      // Shovel Snow
            "clean",       // Clean structures (Cabin, etc.)
            "till",        // Till field
            "wet",         // Wet garden plot
            "water",       // Water plants (CI on garden plots)
        };

        private static bool IsPermittedAction(string actionName)
        {
            if (string.IsNullOrEmpty(actionName)) return false;
            foreach (var keyword in PermittedActionKeywords)
            {
                if (actionName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static bool RequiresPopupOnlyExecution()
        {
            var actionName = lastActionName ?? string.Empty;
            return actionName.IndexOf("eat", StringComparison.OrdinalIgnoreCase) >= 0
                || actionName.IndexOf("drink", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Checks if a consumable action (Eat/Drink) is likely consuming cards by name.
        /// These actions typically destroy the receiving card entirely.
        /// </summary>
        private static bool IsConsumableAction()
        {
            var actionName = lastActionName ?? string.Empty;
            return actionName.IndexOf("eat", StringComparison.OrdinalIgnoreCase) >= 0
                || actionName.IndexOf("drink", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsLikelyCardlessAction()
        {
            var actionName = lastActionName ?? string.Empty;
            return actionName.IndexOf("meditate", StringComparison.OrdinalIgnoreCase) >= 0
                || actionName.IndexOf("rest", StringComparison.OrdinalIgnoreCase) >= 0
                || actionName.IndexOf("sleep", StringComparison.OrdinalIgnoreCase) >= 0
                || actionName.IndexOf("continue", StringComparison.OrdinalIgnoreCase) >= 0
                || actionName.IndexOf("wait", StringComparison.OrdinalIgnoreCase) >= 0
                || actionName.IndexOf("relax", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsTravelAction()
        {
            var actionName = lastActionName ?? string.Empty;
            return actionName.IndexOf("travel", StringComparison.OrdinalIgnoreCase) >= 0
                || actionName.IndexOf("north", StringComparison.OrdinalIgnoreCase) >= 0
                || actionName.IndexOf("south", StringComparison.OrdinalIgnoreCase) >= 0
                || actionName.IndexOf("east", StringComparison.OrdinalIgnoreCase) >= 0
                || actionName.IndexOf("west", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Detect chop/cut/fell tree actions. Rest is inserted between iterations
        /// to recover stamina and allow respawn of small trees.
        /// </summary>
        private static bool IsChopAction()
        {
            var actionName = lastActionName ?? string.Empty;
            return actionName.IndexOf("chop", StringComparison.OrdinalIgnoreCase) >= 0
                || actionName.IndexOf("cut", StringComparison.OrdinalIgnoreCase) >= 0
                || actionName.IndexOf("fell", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Detect wash actions (Wash intestines, Wash dressing, etc.).
        /// Given card (dirty item) transforms each time; receiving card (water) may deplete.
        /// </summary>
        private static bool IsWashAction()
        {
            var actionName = lastActionName ?? string.Empty;
            return actionName.IndexOf("wash", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool RequiresQuantityProgressValidation()
        {
            var actionName = lastActionName ?? string.Empty;
            return actionName.IndexOf("eat", StringComparison.OrdinalIgnoreCase) >= 0
                || actionName.IndexOf("drink", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// For travel/direction actions: find the current location card in the scene
        /// that has a DismantleAction matching the remembered direction name.
        /// Updates lastReceivingCard, lastAction, lastActionIndex, and captured args.
        /// </summary>
        private static bool RefreshTravelContext()
        {
            try
            {
                string directionName = lastActionName;
                if (string.IsNullOrEmpty(directionName)) return false;

                var allGraphics = UnityEngine.Object.FindObjectsOfType(cardGraphicsType);
                foreach (var graphics in allGraphics)
                {
                    if (graphics == null) continue;
                    var card = GetCardFromGraphics(graphics);
                    if (card == null) continue;

                    // Skip destroyed cards
                    if (IsCardDestroyed(card)) continue;

                    // Get CardModel.DismantleActions
                    var cardModel = AccessTools.Property(card.GetType(), "CardModel")?.GetValue(card);
                    if (cardModel == null) continue;

                    var dismantleField = AccessTools.Field(cardModel.GetType(), "DismantleActions");
                    var dismantleActions = dismantleField?.GetValue(cardModel);
                    if (!(dismantleActions is System.Collections.IList actionList) || actionList.Count == 0)
                        continue;

                    // Search for matching direction action by name
                    for (int idx = 0; idx < actionList.Count; idx++)
                    {
                        var action = actionList[idx];
                        string actionName = GetActionDisplayName(action);
                        if (string.Equals(actionName, directionName, StringComparison.OrdinalIgnoreCase))
                        {
                            // Found matching direction on this card
                            string cardName = GetCardDisplayName(card);
                            Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Travel refresh: found '{directionName}' (button #{idx}) on {cardName}");

                            lastAction = action;
                            lastReceivingCard = card;
                            lastActionIndex = idx;
                            savedReceivingUniqueId = GetCardUniqueId(card);
                            lastInspectionPopup = FindActiveInspectionPopup(card);

                            // Update ActionRoutine captured args
                            if (lastActionRoutineArgs != null)
                            {
                                lastActionRoutineArgs = (object[])lastActionRoutineArgs.Clone();
                                if (lastArActionIdx >= 0) lastActionRoutineArgs[lastArActionIdx] = action;
                                if (lastArReceivingIdx >= 0) lastActionRoutineArgs[lastArReceivingIdx] = card;
                            }

                            return true;
                        }
                    }
                }

                Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] RefreshTravelContext: no card with '{directionName}' action found");
                return false;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[Repeat] RefreshTravelContext error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get the current game tick (DayTimePoints) from GameManager.
        /// Used to detect if an action advanced game time.
        /// </summary>
        private static int GetGameTick()
        {
            try
            {
                if (gmInstanceProp == null) return -1;
                var gm = gmInstanceProp.GetValue(null);
                if (gm == null) return -1;
                var prop = AccessTools.Property(gameManagerType, "DayTimePoints");
                if (prop == null) return -1;
                return (int)prop.GetValue(gm);
            }
            catch { return -1; }
        }

        /// <summary>
        /// Check if a card has been marked as Destroyed by the game.
        /// </summary>
        private static bool IsCardDestroyed(object card)
        {
            if (card == null) return true;
            try
            {
                // Check Unity Object alive status
                var asUnity = card as UnityEngine.Object;
                if (asUnity == null || !asUnity) return true;

                // Check the game's own Destroyed property
                var prop = AccessTools.Property(card.GetType(), "Destroyed");
                if (prop != null)
                    return (bool)prop.GetValue(card);
                return false;
            }
            catch { return false; }
        }

        /// <summary>
        /// Get the most relevant trackable quantity for a card.
        /// Tries liquid quantity first (water containers), then usage durability, etc.
        /// Returns null if no quantity field is found or accessible.
        /// </summary>
        private static float? GetCardItemQuantity(object card)
        {
            if (card == null) return null;

            try
            {
                // Try liquid quantity first (for water containers � drinking drains this)
                object value = GetMemberValueSilent(card, "CurrentLiquidQuantity");
                if (value != null)
                {
                    float liquid = Convert.ToSingle(value);
                    // Only return liquid quantity if the container actually has liquid capacity
                    // (avoid returning 0 for non-container cards)
                    if (liquid > 0f) return liquid;
                    
                    // Check if this is a container at all (has MaxLiquidCapacity)
                    var cardModel = GetMemberValueSilent(card, "CardModel");
                    if (cardModel != null)
                    {
                        var maxLiquid = GetMemberValueSilent(cardModel, "MaxLiquidCapacity");
                        if (maxLiquid != null && Convert.ToSingle(maxLiquid) > 0f)
                            return liquid; // Container with 0 liquid = empty
                    }
                }

                // Try usage durability (for tools, items with uses)
                value = GetMemberValueSilent(card, "CurrentUsageDurability");
                if (value != null)
                {
                    float usage = Convert.ToSingle(value);
                    if (usage > 0f) return usage;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Get the liquid quantity specifically for drink actions.
        /// Returns the CurrentLiquidQuantity directly.
        /// </summary>
        private static float? GetLiquidQuantity(object card)
        {
            if (card == null) return null;
            try
            {
                object value = GetMemberValueSilent(card, "CurrentLiquidQuantity");
                if (value != null) return Convert.ToSingle(value);
                return null;
            }
            catch { return null; }
        }

        private static bool TryRefreshReceivingCard()
        {
            try
            {
                // Use saved UniqueID as fallback � cleared cards lose their CardModel/UniqueID
                string uniqueId = lastReceivingCard != null ? GetCardUniqueId(lastReceivingCard) : null;
                if (string.IsNullOrEmpty(uniqueId))
                    uniqueId = savedReceivingUniqueId;

                if (string.IsNullOrEmpty(uniqueId))
                {
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] TryRefreshReceivingCard: No UniqueID available");
                    return false;
                }

                var refreshedCard = FindMatchingCardInScene(lastReceivingCard, null, uniqueId);
                if (refreshedCard != null)
                {
                    if (!ReferenceEquals(refreshedCard, lastReceivingCard))
                        Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] Refreshed target card reference from scene");

                    lastReceivingCard = refreshedCard;
                    return true;
                }

                // No live card found � verify old reference is truly alive (not just a cleared Unity Object)
                if (lastReceivingCard != null)
                {
                    var asUnity = lastReceivingCard as UnityEngine.Object;
                    if (asUnity != null && asUnity)
                    {
                        var cardModel = AccessTools.Property(lastReceivingCard.GetType(), "CardModel")?.GetValue(lastReceivingCard);
                        if (cardModel != null)
                        {
                            // Card is alive, but did it transform into a different item?
                            // If UniqueID changed (e.g., nettle stem -> fibers), treat as consumed.
                            string currentId = GetCardUniqueId(lastReceivingCard);
                            if (!string.IsNullOrEmpty(currentId) && !string.IsNullOrEmpty(uniqueId)
                                && !string.Equals(currentId, uniqueId, StringComparison.Ordinal))
                            {
                                Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Card transformed: expected '{uniqueId}', now '{currentId}'");
                                return false;
                            }
                            return true;  // Card truly alive and same type
                        }
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] TryRefreshReceivingCard error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// After a card transforms (e.g., nettle stem -> fibers), search for another card
        /// with the ORIGINAL UniqueID, not the transformation result. Used for extract-type actions.
        /// </summary>
        private static bool TryRefreshReceivingCardByOriginalId(string originalId)
        {
            try
            {
                // Use provided original ID, or fall back to saved
                if (string.IsNullOrEmpty(originalId))
                    originalId = savedReceivingUniqueId;

                if (string.IsNullOrEmpty(originalId))
                {
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] TryRefreshReceivingCardByOriginalId: No original UniqueID available");
                    return false;
                }

                Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Searching for more cards with original ID '{originalId}'");

                // Find ANY card with the original UniqueID in the scene
                var refreshedCard = FindMatchingCardInScene(null, null, originalId);
                if (refreshedCard != null)
                {
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] Found another card with original ID, continuing extractions");
                    lastReceivingCard = refreshedCard;
                    return true;
                }

                Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] No more cards with original ID found in scene");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] TryRefreshReceivingCardByOriginalId error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Count how many cards with the given UniqueID exist in the scene.
        /// Used to detect consumption of stacked items (e.g., eating fish reduces count from 3?2).
        /// </summary>
        private static int CountCardsInScene(string uniqueId)
        {
            if (string.IsNullOrEmpty(uniqueId) || cardGraphicsType == null) return 0;

            int count = 0;
            try
            {
                var allGraphics = UnityEngine.Object.FindObjectsOfType(cardGraphicsType);
                foreach (var graphics in allGraphics)
                {
                    if (graphics == null) continue;
                    var card = GetCardFromGraphics(graphics);
                    if (card == null) continue;
                    string cardId = GetCardUniqueId(card);
                    if (string.Equals(cardId, uniqueId, StringComparison.Ordinal))
                        count++;
                }
            }
            catch { }
            return count;
        }

        private static bool TryRefreshGivenCard()
        {
            try
            {
                if (lastGivenCard == null) return false;

                // Use saved UniqueID as fallback � cleared cards lose their CardModel/UniqueID
                string givenId = GetCardUniqueId(lastGivenCard);
                if (string.IsNullOrEmpty(givenId))
                    givenId = savedGivenUniqueId;

                if (string.IsNullOrEmpty(givenId))
                {
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] TryRefreshGivenCard: No UniqueID available");
                    return false;
                }

                // CRITICAL: For same-type drag-drop (e.g., Fiber+Fiber=Twine),
                // the given card must NOT be the same object as the receiving card.
                string receivingId = GetCardUniqueId(lastReceivingCard);
                if (string.IsNullOrEmpty(receivingId))
                    receivingId = savedReceivingUniqueId;
                bool sameType = string.Equals(givenId, receivingId, StringComparison.Ordinal);

                object excludeCard = sameType ? lastReceivingCard : null;
                var refreshedCard = FindMatchingCardInScene(lastGivenCard, excludeCard, givenId);
                if (refreshedCard != null)
                {
                    if (!ReferenceEquals(refreshedCard, lastGivenCard))
                        Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] Refreshed given card reference from scene (sameType={sameType})");

                    lastGivenCard = refreshedCard;
                    return true;
                }

                // No live card found � verify old reference is truly alive (not just a cleared Unity Object)
                var asUnity = lastGivenCard as UnityEngine.Object;
                if (asUnity != null && asUnity)
                {
                    var cardModel = AccessTools.Property(lastGivenCard.GetType(), "CardModel")?.GetValue(lastGivenCard);
                    if (cardModel != null) return true;  // Card truly alive
                }
                return false;
            }
            catch (Exception ex)
            {
                Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] TryRefreshGivenCard error: {ex.Message}");
                return false;
            }
        }

        private static bool TryOpenTargetCardPopup()
        {
            try
            {
                if (cardGraphicsType == null || cgOnPointerClickMethod == null || lastReceivingCard == null)
                    return false;

                var targetGraphics = FindMatchingCardGraphics(lastReceivingCard);
                if (targetGraphics == null)
                {
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] Could not find matching CardGraphics for target card");
                    return false;
                }

                if (EventSystem.current == null)
                {
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] EventSystem.current is null");
                    return false;
                }

                var previousPopup = lastInspectionPopup;

                var pointer = new PointerEventData(EventSystem.current)
                {
                    button = PointerEventData.InputButton.Left
                };

                cgOnPointerClickMethod.Invoke(targetGraphics, new object[] { pointer });
                lastInspectionPopup = FindActiveInspectionPopup(lastReceivingCard) ?? previousPopup;

                if (lastInspectionPopup == null)
                {
                    Logger.Log(BepInEx.Logging.LogLevel.Debug, "[Repeat] Failed to reacquire InspectionPopup after card click");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] TryOpenTargetCardPopup error: {ex.Message}");
                return false;
            }
        }

        private static object FindMatchingCardInScene(object referenceCard, object excludeCard = null, string overrideUniqueId = null)
        {
            var graphics = FindMatchingCardGraphics(referenceCard, excludeCard, overrideUniqueId);
            if (graphics == null) return null;
            return GetCardFromGraphics(graphics);
        }

        private static object FindMatchingCardGraphics(object referenceCard, object excludeCard = null, string overrideUniqueId = null)
        {
            try
            {
                if (cardGraphicsType == null) return null;
                if (referenceCard == null && string.IsNullOrEmpty(overrideUniqueId)) return null;

                string targetUniqueId = overrideUniqueId ?? GetCardUniqueId(referenceCard);
                if (string.IsNullOrEmpty(targetUniqueId)) return null;

                object targetSlot = referenceCard != null ? GetCardSlot(referenceCard) : null;
                var allGraphics = UnityEngine.Object.FindObjectsOfType(cardGraphicsType);

                // Pass 1: strict match (UniqueID + same slot)
                if (targetSlot != null)
                {
                    foreach (var graphics in allGraphics)
                    {
                        if (graphics == null) continue;

                        var card = GetCardFromGraphics(graphics);
                        if (card == null) continue;
                        if (excludeCard != null && ReferenceEquals(card, excludeCard)) continue;

                        // Skip cleared/dead cards (CardModel becomes null when consumed)
                        string cardUniqueId = GetCardUniqueId(card);
                        if (string.IsNullOrEmpty(cardUniqueId)) continue;
                        if (!string.Equals(cardUniqueId, targetUniqueId, StringComparison.Ordinal))
                            continue;

                        var cardSlot = GetCardSlot(card);
                        if (cardSlot != null && ReferenceEquals(cardSlot, targetSlot))
                            return graphics;
                    }
                }

                // Pass 2: relaxed match (UniqueID only)
                foreach (var graphics in allGraphics)
                {
                    if (graphics == null) continue;

                    var card = GetCardFromGraphics(graphics);
                    if (card == null) continue;
                    if (excludeCard != null && ReferenceEquals(card, excludeCard)) continue;

                    // Skip cleared/dead cards
                    string cardUniqueId = GetCardUniqueId(card);
                    if (string.IsNullOrEmpty(cardUniqueId)) continue;
                    if (!string.Equals(cardUniqueId, targetUniqueId, StringComparison.Ordinal))
                        continue;

                    return graphics;
                }
            }
            catch (Exception ex)
            {
                Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[Repeat] FindMatchingCardGraphics error: {ex.Message}");
            }

            return null;
        }

        private static object GetCardFromGraphics(object cardGraphicsInstance)
        {
            try
            {
                if (cardGraphicsType == null || cardGraphicsInstance == null) return null;

                var value = GetMemberValueSilent(cardGraphicsInstance, "CardLogic")
                         ?? GetMemberValueSilent(cardGraphicsInstance, "Card")
                         ?? GetMemberValueSilent(cardGraphicsInstance, "_card");

                if (value != null)
                    return value;
            }
            catch { }

            return null;
        }

        private static bool IsSameCardTarget(object cardA, object cardB)
        {
            if (ReferenceEquals(cardA, cardB)) return true;
            if (cardA == null || cardB == null) return false;

            string idA = GetCardUniqueId(cardA);
            string idB = GetCardUniqueId(cardB);
            if (string.IsNullOrEmpty(idA) || string.IsNullOrEmpty(idB)) return false;
            if (!string.Equals(idA, idB, StringComparison.Ordinal)) return false;

            var slotA = GetCardSlot(cardA);
            var slotB = GetCardSlot(cardB);
            if (slotA != null && slotB != null)
                return ReferenceEquals(slotA, slotB);

            return true;
        }

        private static string GetCardUniqueId(object card)
        {
            try
            {
                if (card == null) return null;

                var cardModel = AccessTools.Property(card.GetType(), "CardModel")?.GetValue(card);
                if (cardModel == null) return null;

                var uniqueIDField = AccessTools.Field(cardModel.GetType(), "UniqueID");
                return uniqueIDField?.GetValue(cardModel) as string;
            }
            catch
            {
                return null;
            }
        }

        private static object GetCardSlot(object card)
        {
            try
            {
                if (card == null) return null;

                var cardType = card.GetType();
                var slot = AccessTools.Property(cardType, "CurrentSlot")?.GetValue(card)
                        ?? AccessTools.Property(cardType, "ContainerSlot")?.GetValue(card)
                        ?? AccessTools.Property(cardType, "ParentSlot")?.GetValue(card)
                        ?? AccessTools.Field(cardType, "CurrentSlot")?.GetValue(card)
                        ?? AccessTools.Field(cardType, "ContainerSlot")?.GetValue(card)
                        ?? AccessTools.Field(cardType, "ParentSlot")?.GetValue(card);

                if (slot != null) return slot;

                var cardLogic = AccessTools.Property(cardType, "CardLogic")?.GetValue(card)
                             ?? AccessTools.Field(cardType, "CardLogic")?.GetValue(card);
                if (cardLogic != null)
                {
                    return AccessTools.Property(cardLogic.GetType(), "SlotOwner")?.GetValue(cardLogic)
                        ?? AccessTools.Field(cardLogic.GetType(), "SlotOwner")?.GetValue(cardLogic);
                }
            }
            catch { }

            return null;
        }

        private static object GetMemberValueSilent(object obj, string name)
        {
            if (obj == null || string.IsNullOrEmpty(name)) return null;

            try
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var type = obj.GetType();

                var prop = type.GetProperty(name, flags);
                if (prop != null)
                    return prop.GetValue(obj, null);

                var field = type.GetField(name, flags);
                if (field != null)
                    return field.GetValue(obj);
            }
            catch { }

            return null;
        }

        // =====================================================================
        // HELPERS
        // =====================================================================

        private static object GetGameManager()
        {
            try
            {
                if (gmInstanceProp != null)
                    return gmInstanceProp.GetValue(null);

                if (gameManagerType != null)
                    return UnityEngine.Object.FindObjectOfType(gameManagerType);
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Find the active InspectionPopup in the scene (there's typically one).
        /// Used as fallback when Gate 2 (mouse click) captures an action.
        /// </summary>
        private static object FindActiveInspectionPopup(object preferredCard = null)
        {
            try
            {
                if (inspectionPopupType != null)
                {
                    var popups = UnityEngine.Object.FindObjectsOfType(inspectionPopupType);
                    if (popups == null || popups.Length == 0)
                        return null;

                    object firstAlive = null;
                    object firstWithCard = null;

                    foreach (var popup in popups)
                    {
                        var popupUnity = popup as UnityEngine.Object;
                        if (popupUnity == null || !popupUnity) continue;

                        var popupMb = popup as MonoBehaviour;
                        if (popupMb != null && !popupMb.gameObject.activeInHierarchy) continue;

                        if (firstAlive == null)
                            firstAlive = popup;

                        object currentCard = null;
                        if (ipCurrentCardProp != null)
                            currentCard = ipCurrentCardProp.GetValue(popup);

                        if (currentCard != null)
                        {
                            if (firstWithCard == null)
                                firstWithCard = popup;

                            if (preferredCard != null && IsSameCardTarget(currentCard, preferredCard))
                                return popup;
                        }
                    }

                    if (preferredCard != null)
                        return firstWithCard ?? firstAlive;

                    return firstWithCard ?? firstAlive;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Find the index of a CardAction in a card's DismantleActions list.
        /// DismantleCardAction inherits from CardAction, so _Action is a DismantleCardAction
        /// and we can compare directly with items in the list.
        /// Used for button index when Gate 2 (mouse click) captures an action.
        /// </summary>
        private static int FindActionIndex(object action, object receivingCard)
        {
            try
            {
                if (receivingCard == null || action == null) return -1;

                var cardModel = AccessTools.Property(inGameCardBaseType, "CardModel");
                var cardData = cardModel?.GetValue(receivingCard);
                if (cardData == null) return -1;

                var dismantleField = AccessTools.Field(cardData.GetType(), "DismantleActions");
                var dismantleActions = dismantleField?.GetValue(cardData);
                if (dismantleActions is System.Collections.IList actionList)
                {
                    // Direct comparison: DismantleCardAction IS a CardAction
                    for (int i = 0; i < actionList.Count; i++)
                    {
                        var dismantleAction = actionList[i];
                        // Check if this IS the same action object
                        if (ReferenceEquals(dismantleAction, action))
                            return i;
                    }

                    // Fallback: match by name (case-insensitive)
                    string targetName = GetActionDisplayName(action);
                    if (!string.IsNullOrEmpty(targetName))
                    {
                        for (int i = 0; i < actionList.Count; i++)
                        {
                            string listActionName = GetActionDisplayName(actionList[i]);
                            if (string.Equals(listActionName, targetName, StringComparison.OrdinalIgnoreCase))
                                return i;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log(BepInEx.Logging.LogLevel.Debug, $"[FindActionIndex] Error: {ex.Message}");
            }
            return -1;
        }

        /// <summary>
        /// Get display name of a CardAction via ActionName.DefaultText or name fallbacks.
        /// </summary>
        private static string GetActionDisplayName(object action)
        {
            if (action == null) return "null";

            try
            {
                // Try ActionName.DefaultText (LocalizedString)
                var actionNameField = AccessTools.Field(action.GetType(), "ActionName");
                if (actionNameField != null)
                {
                    var locString = actionNameField.GetValue(action);
                    if (locString != null)
                    {
                        var dtField = AccessTools.Field(locString.GetType(), "DefaultText");
                        var defaultText = dtField != null
                            ? dtField.GetValue(locString)
                            : AccessTools.Property(locString.GetType(), "DefaultText")?.GetValue(locString);
                        if (defaultText is string s && !string.IsNullOrEmpty(s))
                            return s;
                    }
                }

                // Try name property
                var unityObj = action as UnityEngine.Object;
                if (unityObj != null)
                    return unityObj.name;

                return action.GetType().Name;
            }
            catch
            {
                return action.GetType().Name;
            }
        }

        /// <summary>
        /// Get display name from an InGameCardBase via CardModel.CardName.
        /// </summary>
        private static string GetCardDisplayName(object card)
        {
            if (card == null) return "null";

            try
            {
                var cardModel = AccessTools.Property(card.GetType(), "CardModel");
                var data = cardModel?.GetValue(card);
                if (data != null)
                {
                    var cardNameField = AccessTools.Field(data.GetType(), "CardName");
                    if (cardNameField != null)
                    {
                        var locString = cardNameField.GetValue(data);
                        if (locString != null)
                        {
                            var dtField2 = AccessTools.Field(locString.GetType(), "DefaultText");
                            var defaultText = dtField2 != null
                                ? dtField2.GetValue(locString)
                                : AccessTools.Property(locString.GetType(), "DefaultText")?.GetValue(locString);
                            if (defaultText is string s && !string.IsNullOrEmpty(s))
                                return s;
                        }
                    }

                    var uniqueIDField = AccessTools.Field(data.GetType(), "UniqueID");
                    if (uniqueIDField != null)
                    {
                        var uid = uniqueIDField.GetValue(data) as string;
                        if (!string.IsNullOrEmpty(uid)) return uid;
                    }
                }

                var unityObj = card as UnityEngine.Object;
                if (unityObj != null)
                    return unityObj.name;

                return card.GetType().Name;
            }
            catch
            {
                return card.GetType().Name;
            }
        }
    }
}
