using System.Reflection;
using BepInEx.Logging;
using UnityEngine.EventSystems;
using CSFFModFramework.Api;

namespace Quick_Transfer.Patcher
{
    public static class QuickTransferPatch
    {
        private static ManualLogSource Logger => Plugin.Logger;

        private static Type cardGraphicsType;
        private static MethodInfo onPointerClickMethod;

        // Re-entrancy guard
        private static bool isTransferring = false;

        // State captured by prefix for use in postfix
        private static object savedSourceSlot = null;
        private static string savedUniqueId = null;
        private static bool savedCtrlRightClick = false;
        private static int savedTransferCount = 1;

        public static void ApplyPatch(Harmony harmony)
        {
            try
            {
                cardGraphicsType = Reflect.TryGetType("CardGraphics");

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

        // PREFIX: captures source slot BEFORE the card moves.
        static void OnPointerClick_Prefix(object __instance, object _Pointer)
        {
            savedSourceSlot = null;
            savedUniqueId = null;
            savedCtrlRightClick = false;
            savedTransferCount = 1;

            if (isTransferring) return;

            try
            {
                if (!Plugin.IsModifierKeyHeld()) return;

                var buttonProp = AccessTools.Property(_Pointer.GetType(), "button");
                var button = buttonProp?.GetValue(_Pointer, null);
                int buttonInt = button != null ? (int)button : -1;
                if (buttonInt != 1) return;

                var card = GetCardFromGraphics(__instance);
                if (card == null) return;

                var cardModel = GetMemberValue(card, "CardModel");
                if (cardModel == null) return;

                savedUniqueId = GetMemberValue(cardModel, "UniqueID")?.ToString();
                if (string.IsNullOrEmpty(savedUniqueId)) return;

                var slot = GetCurrentSlot(card);
                if (slot == null) return;

                savedSourceSlot = slot;
                savedTransferCount = Plugin.GetEffectiveTransferAmount();
                savedCtrlRightClick = true;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error in prefix: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // POSTFIX: first card has moved; kick off coroutine for remaining transfers.
        static void OnPointerClick_Postfix(object __instance, object _Pointer)
        {
            if (!savedCtrlRightClick || isTransferring) return;

            try
            {
                int additionalCount = savedTransferCount - 1;
                if (additionalCount <= 0) return;

                var sourceSlot = savedSourceSlot;
                var uniqueId = savedUniqueId;
                var totalCount = savedTransferCount;

                if (sourceSlot == null || string.IsNullOrEmpty(uniqueId)) return;

                string label = totalCount >= 9999 ? "All" : totalCount.ToString();
                Plugin.ShowNotification($"Quick Transfer: {label}");

                Logger.LogDebug($"[QT] Postfix: starting coroutine for uid={uniqueId}, total={totalCount}, additional={additionalCount}");
                Plugin.Instance.StartCoroutine(TransferCardsCoroutine(sourceSlot, uniqueId, additionalCount));
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error in postfix: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
            finally
            {
                savedSourceSlot = null;
                savedUniqueId = null;
                savedCtrlRightClick = false;
            }
        }

        // Transfers cards from the source slot one per frame.
        // Re-scans for any matching card each iteration to handle both stacked items
        // (single CardGraphics object representing N cards) and individual card objects.
        static IEnumerator TransferCardsCoroutine(object sourceSlot, string uniqueId, int count)
        {
            int transferred = 0;
            int consecutiveFailures = 0;
            const int MaxConsecutiveFailures = 3;

            object candidate = FindFirstCandidate(sourceSlot, uniqueId);
            Logger.LogDebug($"[QT] Coroutine start: count={count}, initial candidate={(candidate == null ? "NULL" : "found")}");

            while (transferred < count)
            {
                yield return null;

                // Re-validate cached candidate; re-scan only when it leaves the source slot.
                if (candidate == null || !IsValidCandidate(candidate, sourceSlot, uniqueId))
                    candidate = FindFirstCandidate(sourceSlot, uniqueId);

                if (candidate == null)
                {
                    consecutiveFailures++;
                    if (consecutiveFailures >= MaxConsecutiveFailures)
                    {
                        Logger.LogDebug($"[QT] Done: transferred {1 + transferred} cards total (no more matching cards after {transferred} coroutine moves)");
                        yield break;
                    }
                    continue;
                }

                consecutiveFailures = 0;
                var newPointer = new PointerEventData(EventSystem.current);
                newPointer.button = PointerEventData.InputButton.Right;

                isTransferring = true;
                try
                {
                    onPointerClickMethod.Invoke(candidate, new object[] { newPointer });
                    transferred++;
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Transfer failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
                    Logger.LogDebug($"[QT] Done (exception): transferred {1 + transferred} cards total");
                    yield break;
                }
                finally
                {
                    isTransferring = false;
                }
            }

            Logger.LogDebug($"[QT] Done: transferred {1 + transferred} cards total (reached requested count)");
        }

        static object FindFirstCandidate(object sourceSlot, string uniqueId)
        {
            var allGraphics = UnityEngine.Object.FindObjectsOfType(cardGraphicsType);
            if (allGraphics == null) return null;
            foreach (var g in allGraphics)
            {
                if (IsValidCandidate(g, sourceSlot, uniqueId))
                    return g;
            }
            return null;
        }

        static bool IsValidCandidate(object graphics, object sourceSlot, string uniqueId)
        {
            if (graphics == null) return false;
            var card = GetCardFromGraphics(graphics);
            if (card == null) return false;
            var cardSlot = GetCurrentSlot(card);
            if (cardSlot == null || !ReferenceEquals(cardSlot, sourceSlot)) return false;
            var cardModel = GetMemberValue(card, "CardModel");
            if (cardModel == null) return false;
            var cardType = GetMemberValue(cardModel, "CardType");
            if (cardType != null && Convert.ToInt32(cardType) == 8) return false;
            var cannotTransfer = GetMemberValue(cardModel, "CannotBeTransferred");
            if (cannotTransfer is bool ct && ct) return false;
            var cardId = GetMemberValue(cardModel, "UniqueID")?.ToString();
            return cardId == uniqueId;
        }

        static object GetCardFromGraphics(object cardGraphicsInstance)
        {
            if (cardGraphicsInstance == null || cardGraphicsType == null) return null;
            return GetMemberValue(cardGraphicsInstance, "CardLogic", "Card", "_card");
        }

        // Encapsulates the slot-lookup fallback chain used in both prefix and candidate matching.
        static object GetCurrentSlot(object card)
        {
            var slot = GetMemberValue(card, "CurrentSlot", "ContainerSlot", "ParentSlot");
            if (slot != null) return slot;
            var cardLogic = GetMemberValue(card, "CardLogic");
            return cardLogic != null ? GetMemberValue(cardLogic, "SlotOwner") : null;
        }

        #region Reflection helpers

        // Delegates the actual property/field resolution + per-(Type,name) caching to the
        // framework's Reflect API. Preserves the original fallback semantics exactly: try each
        // candidate name in turn and only fall through to the next name if the resolved value
        // is null (not merely if the member is absent) — callers rely on this to skip fields
        // that exist on the type but aren't populated on a given instance (e.g. CurrentSlot vs
        // ContainerSlot vs ParentSlot).
        private static object GetMemberValue(object obj, params string[] names)
        {
            if (obj == null || names == null) return null;
            foreach (var name in names)
            {
                if (Reflect.TryGetMember(obj, name, out var value) && value != null)
                    return value;
            }
            return null;
        }

        #endregion
    }
}
