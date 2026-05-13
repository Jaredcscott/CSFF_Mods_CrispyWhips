using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.EventSystems;
using Quick_Transfer;

namespace Quick_Transfer.Patcher
{
    /// <summary>
    /// Harmony patches for Quick Transfer functionality.
    /// PREFIX captures the source slot BEFORE the card moves.
    /// POSTFIX transfers additional cards from the saved source slot.
    /// </summary>
    public static class QuickTransferPatch
    {
        private static ManualLogSource Logger => Plugin.Logger;
        
        // Cache for reflection lookups
        private static Type cardGraphicsType;
        
        // Cached reflection members - avoids repeated AccessTools calls
        private static readonly Dictionary<(Type, string), MemberInfo> memberCache = new Dictionary<(Type, string), MemberInfo>();
        
        // Cached methods
        private static MethodInfo onPointerClickMethod;
        
        // Re-entrancy guard
        private static bool isTransferring = false;
        
        // State captured by prefix for use in postfix
        private static object savedSourceSlot = null;
        private static string savedUniqueId = null;
        private static bool savedCtrlRightClick = false;

        public static void ApplyPatch(Harmony harmony)
        {
            try
            {
                cardGraphicsType = AccessTools.TypeByName("CardGraphics");

                if (cardGraphicsType == null)
                {
                    Logger.LogError("Could not find CardGraphics type!");
                    return;
                }
                
                onPointerClickMethod = AccessTools.Method(cardGraphicsType, "OnPointerClick");
                if (onPointerClickMethod != null)
                {
                    var prefixMethod = AccessTools.Method(typeof(QuickTransferPatch), nameof(OnPointerClick_Prefix));
                    var postfixMethod = AccessTools.Method(typeof(QuickTransferPatch), nameof(OnPointerClick_Postfix));
                    harmony.Patch(onPointerClickMethod,
                        prefix: new HarmonyMethod(prefixMethod),
                        postfix: new HarmonyMethod(postfixMethod));
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to apply QuickTransfer patches: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        /// <summary>
        /// PREFIX: Runs BEFORE the card moves. Captures source slot and card info.
        /// </summary>
        static void OnPointerClick_Prefix(object __instance, object _Pointer)
        {
            // Reset state
            savedSourceSlot = null;
            savedUniqueId = null;
            savedCtrlRightClick = false;
            
            // Skip if we're already doing a batch transfer
            if (isTransferring) return;
            
            try
            {
                // Use configurable modifier key
                if (!Plugin.IsModifierKeyHeld()) return;
                
                // Check right-click
                var buttonProp = AccessTools.Property(_Pointer.GetType(), "button");
                var button = buttonProp?.GetValue(_Pointer, null);
                int buttonInt = button != null ? (int)button : -1;
                if (buttonInt != 1) return;
                
                // This IS a ctrl+right-click. Get the card BEFORE it moves.
                var card = GetCardFromGraphics(__instance);
                if (card == null) return;
                
                // Get the card's UniqueID
                var cardModel = GetMemberValue(card, "CardModel");
                if (cardModel == null) return;
                
                savedUniqueId = GetMemberValue(cardModel, "UniqueID")?.ToString();
                if (string.IsNullOrEmpty(savedUniqueId)) return;
                
                // Get the card's CURRENT slot (before it moves)
                var slot = GetMemberValue(card, "CurrentSlot") ??
                           GetMemberValue(card, "ContainerSlot") ??
                           GetMemberValue(card, "ParentSlot");
                
                if (slot == null)
                {
                    var cardLogic = GetMemberValue(card, "CardLogic");
                    if (cardLogic != null)
                    {
                        slot = GetMemberValue(cardLogic, "SlotOwner");
                    }
                }
                
                if (slot == null) return;
                
                savedSourceSlot = slot;
                savedCtrlRightClick = true;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error in prefix: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        /// <summary>
        /// POSTFIX: Runs AFTER the first card has moved. Kicks off coroutine for remaining transfers.
        /// </summary>
        static void OnPointerClick_Postfix(object __instance, object _Pointer)
        {
            // Skip if not a ctrl+right-click or if we're batch-transferring
            if (!savedCtrlRightClick || isTransferring) return;
            
            try
            {
                int additionalCount = Plugin.CurrentTransferAmount - 1;
                if (additionalCount <= 0) return;
                
                // Capture state locally before clearing
                var sourceSlot = savedSourceSlot;
                var uniqueId = savedUniqueId;
                
                if (sourceSlot == null || string.IsNullOrEmpty(uniqueId)) return;
                
                // Start coroutine for remaining transfers (one per frame)
                Plugin.Instance.StartCoroutine(TransferCardsCoroutine(sourceSlot, uniqueId, additionalCount));
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error in postfix: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
            finally
            {
                // Clear saved state
                savedSourceSlot = null;
                savedUniqueId = null;
                savedCtrlRightClick = false;
            }
        }
        
        /// <summary>
        /// Coroutine that transfers cards from the source slot, one per frame.
        /// Fresh scene scan each frame ensures we always find live cards.
        /// </summary>
        static IEnumerator TransferCardsCoroutine(object sourceSlot, string uniqueId, int count)
        {
            int transferred = 0;
            int consecutiveFailures = 0;
            const int MaxConsecutiveFailures = 3;

            while (transferred < count)
            {
                // Wait one frame so the game can finish processing the previous transfer
                yield return null;

                // Fresh scan every frame - no stale cache issues
                var allGraphics = UnityEngine.Object.FindObjectsOfType(cardGraphicsType);
                object matchingCardGraphics = FindMatchingCardGraphics(sourceSlot, uniqueId, allGraphics);

                if (matchingCardGraphics == null)
                {
                    consecutiveFailures++;
                    if (consecutiveFailures >= MaxConsecutiveFailures)
                    {
                        Logger.LogDebug($"Transferred {1 + transferred} cards (no more matching cards)");
                        yield break;
                    }
                    continue;
                }

                // Create a fresh PointerEventData with right-click
                var newPointer = new PointerEventData(EventSystem.current);
                newPointer.button = PointerEventData.InputButton.Right;

                isTransferring = true;
                try
                {
                    onPointerClickMethod.Invoke(matchingCardGraphics, new object[] { newPointer });
                    transferred++;
                    consecutiveFailures = 0;
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Transfer failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
                    Logger.LogDebug($"Transferred {1 + transferred} cards");
                    yield break;
                }
                finally
                {
                    isTransferring = false;
                }
            }

            Logger.LogDebug($"Transferred {1 + transferred} cards");
        }

        /// <summary>
        /// Find a CardGraphics in the scene that matches the given slot and card type.
        /// </summary>
        static object FindMatchingCardGraphics(object targetSlot, string targetUniqueId, object[] allGraphics)
        {
            if (allGraphics == null || allGraphics.Length == 0) return null;

            foreach (var graphics in allGraphics)
            {
                if (graphics == null) continue;

                var card = GetCardFromGraphics(graphics);
                if (card == null) continue;

                // Check if this card is in the target slot
                var cardSlot = GetMemberValue(card, "CurrentSlot") ??
                               GetMemberValue(card, "ContainerSlot") ??
                               GetMemberValue(card, "ParentSlot");

                if (cardSlot == null)
                {
                    var cardLogic = GetMemberValue(card, "CardLogic");
                    if (cardLogic != null)
                    {
                        cardSlot = GetMemberValue(cardLogic, "SlotOwner");
                    }
                }

                if (cardSlot == null || !ReferenceEquals(cardSlot, targetSlot)) continue;

                // Check if this card matches the target type
                var cardModel = GetMemberValue(card, "CardModel");
                if (cardModel == null) continue;

                var cardId = GetMemberValue(cardModel, "UniqueID")?.ToString();
                if (cardId == targetUniqueId)
                {
                    return graphics;
                }
            }

            return null;
        }
        
        /// <summary>
        /// Get the InGameCardBase from a CardGraphics instance using cached reflection lookups.
        /// </summary>
        static object GetCardFromGraphics(object cardGraphicsInstance)
        {
            if (cardGraphicsInstance == null || cardGraphicsType == null) return null;

            // Try CardLogic property first (most common)
            var value = GetMemberValue(cardGraphicsInstance, "CardLogic");
            if (value != null) return value;

            // Try alternate names
            value = GetMemberValue(cardGraphicsInstance, "Card", "_card");
            if (value != null) return value;

            return null;
        }

        #region Helper Methods
        
        /// <summary>
        /// Get a member value with caching to avoid repeated reflection lookups.
        /// </summary>
        private static object GetMemberValue(object obj, params string[] names)
        {
            if (obj == null) return null;
            var type = obj.GetType();
            
            foreach (var name in names)
            {
                var key = (type, name);
                
                // Check cache first
                if (memberCache.TryGetValue(key, out var cached))
                {
                    if (cached == null) continue; // Previously failed lookup
                    
                    try
                    {
                        object value = cached is PropertyInfo prop 
                            ? prop.GetValue(obj) 
                            : ((FieldInfo)cached).GetValue(obj);
                        if (value != null) return value;
                    }
                    catch { }
                    continue;
                }
                
                // Not in cache - look it up
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    memberCache[key] = field;
                    try
                    {
                        var value = field.GetValue(obj);
                        if (value != null) return value;
                    }
                    catch { }
                    continue;
                }
                
                var property = AccessTools.Property(type, name);
                if (property != null)
                {
                    memberCache[key] = property;
                    try
                    {
                        var value = property.GetValue(obj);
                        if (value != null) return value;
                    }
                    catch { }
                    continue;
                }
                
                // Neither found - cache the failure
                memberCache[key] = null;
            }
            
            return null;
        }
        
        #endregion
    }
}
