using System.Reflection;
using BepInEx.Logging;
using UnityEngine.EventSystems;

namespace Quick_Transfer.Patcher
{
    public static class QuickTransferPatch
    {
        private static ManualLogSource Logger => Plugin.Logger;

        private static Type cardGraphicsType;
        private static readonly Dictionary<(Type, string), MemberInfo> memberCache = new Dictionary<(Type, string), MemberInfo>();
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
        // Scans the scene once at start — all stacked cards exist as separate objects.
        // Each transfer updates the moved card's slot reference, so re-verify correctly
        // skips it and advances to the next candidate without a redundant scene scan.
        static IEnumerator TransferCardsCoroutine(object sourceSlot, string uniqueId, int count)
        {
            int transferred = 0;
            int consecutiveFailures = 0;
            const int MaxConsecutiveFailures = 3;

            var allGraphics = UnityEngine.Object.FindObjectsOfType(cardGraphicsType);
            var candidates = BuildCandidateList(allGraphics, sourceSlot, uniqueId);

            int idx = 0;
            while (transferred < count && idx < candidates.Count)
            {
                yield return null;

                var candidate = candidates[idx++];

                if (!IsValidCandidate(candidate, sourceSlot, uniqueId))
                {
                    consecutiveFailures++;
                    if (consecutiveFailures >= MaxConsecutiveFailures)
                    {
                        Logger.LogDebug($"Transferred {1 + transferred} cards (no more matching cards)");
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

        static List<object> BuildCandidateList(object[] allGraphics, object sourceSlot, string uniqueId)
        {
            var candidates = new List<object>();
            if (allGraphics == null) return candidates;
            foreach (var g in allGraphics)
            {
                if (IsValidCandidate(g, sourceSlot, uniqueId))
                    candidates.Add(g);
            }
            return candidates;
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

        private static object GetMemberValue(object obj, params string[] names)
        {
            if (obj == null) return null;
            var type = obj.GetType();

            foreach (var name in names)
            {
                var key = (type, name);

                if (memberCache.TryGetValue(key, out var cached))
                {
                    if (cached == null) continue;

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

                memberCache[key] = null;
            }

            return null;
        }

        #endregion
    }
}
