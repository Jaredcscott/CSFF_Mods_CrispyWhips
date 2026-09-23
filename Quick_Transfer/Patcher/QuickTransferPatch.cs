using System.Reflection;
using BepInEx.Logging;
using UnityEngine.EventSystems;
using CSFFModFramework.Api;

namespace Quick_Transfer.Patcher
{
    public static class QuickTransferPatch
    {
        private static ManualLogSource Logger => Plugin.Logger;

        // Reflect.TryGetMember returns null instead of throwing, so a member renamed by a game update
        // never reaches the click prefix's catch and every transfer would be skipped with no log line.
        // Breadcrumb each cause once per session instead.
        private static readonly HashSet<string> warnedSkipCauses = new HashSet<string>();

        static void WarnSkipOnce(string cause)
        {
            if (warnedSkipCauses.Add(cause))
                Logger.LogWarning($"Transfer skipped: could not read the clicked card's {cause}. If this repeats on ordinary cards after a game update, a member was renamed.");
        }

        private static Type cardGraphicsType;
        private static MethodInfo onPointerClickMethod;

        // Lazily-resolved, cached reflection handle for DynamicLayoutSlot.CardPileCount(bool).
        private static MethodInfo cardPileCountMethod;
        private static Type cardPileCountMethodOwner;

        // SoundManager.PerformCardAppearanceSound(AudioClip[]) - patched to collapse a batch's
        // per-card sounds into one. Null when the type/method couldn't be resolved, in which case
        // the feature is simply off and transfers still work.
        private static MethodInfo cardAppearanceSoundMethod;
        private static object suppressedSoundOwner;
        private static object suppressedSoundClips;

        // Re-entrancy guard
        private static bool isTransferring = false;

        // State captured by prefix for use in postfix
        private static object savedSourceSlot = null;
        private static string savedUniqueId = null;
        private static bool savedCtrlRightClick = false;
        private static int savedTransferCount = 1;
        // True when the click we intercepted is one vanilla will itself act on, i.e. the first card
        // moves without our help. False for a trigger button vanilla ignores, where the batch owes
        // the player the full count rather than count-1.
        private static bool savedVanillaMoves = false;

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
                else
                {
                    Logger.LogError("CardGraphics.OnPointerClick not found — QuickTransfer inactive.");
                }

                ApplyBatchSoundPatch(harmony);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to apply QuickTransfer patches: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // Vanilla plays one card-appearance sound per moved card: SwapCard -> GraphicsManager
        // .MoveCardToSlot, whose last statement is SoundManager.PerformCardAppearanceSound, and
        // RandomSoundPlay pools a fresh AudioSource per call with no throttling. A batch re-invokes
        // that click once per frame, so N cards fire N overlapping sounds. Muting them for the
        // duration of the batch and replaying one at the end restores the vanilla "one move, one
        // sound" feel.
        static void ApplyBatchSoundPatch(Harmony harmony)
        {
            try
            {
                var soundManagerType = Reflect.TryGetType("SoundManager");
                if (soundManagerType == null)
                {
                    Logger.LogWarning("SoundManager type not found - batch sound consolidation disabled.");
                    return;
                }

                cardAppearanceSoundMethod = AccessTools.Method(soundManagerType, "PerformCardAppearanceSound");
                if (cardAppearanceSoundMethod == null)
                {
                    Logger.LogWarning("SoundManager.PerformCardAppearanceSound not found - batch sound consolidation disabled.");
                    return;
                }

                var prefixMethod = AccessTools.Method(typeof(QuickTransferPatch), nameof(PerformCardAppearanceSound_Prefix));
                harmony.Patch(cardAppearanceSoundMethod, prefix: new HarmonyMethod(prefixMethod));
            }
            catch (Exception ex)
            {
                cardAppearanceSoundMethod = null;
                Logger.LogError($"Failed to apply batch sound patch: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // Runs on EVERY card-appearance sound in the game, so it stays a passthrough on a single
        // static bool read. The only path that returns false is one this mod is itself driving:
        // isTransferring is set exclusively around our own synthesized OnPointerClick invoke.
        static bool PerformCardAppearanceSound_Prefix(object __instance, object[] __args)
        {
            if (!isTransferring) return true;
            if (!Plugin.ConsolidateBatchSound.Value) return true;

            // Keep the instance and clips so the batch can replay exactly the sound it swallowed -
            // a card with no WhenCreatedSounds stays silent, same as vanilla.
            suppressedSoundOwner = __instance;
            suppressedSoundClips = __args != null && __args.Length > 0 ? __args[0] : null;
            return false;
        }

        // Called once per batch, from the coroutine's finally. isTransferring is false by then, so
        // this call passes straight through the prefix above.
        static void PlayBatchCompletionSound()
        {
            var owner = suppressedSoundOwner;
            var clips = suppressedSoundClips;
            suppressedSoundOwner = null;
            suppressedSoundClips = null;

            if (owner == null || clips == null || cardAppearanceSoundMethod == null) return;

            try
            {
                cardAppearanceSoundMethod.Invoke(owner, new[] { clips });
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"[QT] Batch completion sound failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // PREFIX: captures source slot BEFORE the card moves.
        static void OnPointerClick_Prefix(object __instance, object _Pointer)
        {
            savedSourceSlot = null;
            savedUniqueId = null;
            savedCtrlRightClick = false;
            savedTransferCount = 1;
            savedVanillaMoves = false;

            if (isTransferring) return;

            try
            {
                if (!Plugin.IsModifierKeyHeld()) return;

                var buttonProp = AccessTools.Property(_Pointer.GetType(), "button");
                var button = buttonProp?.GetValue(_Pointer, null);
                int buttonInt = button != null ? (int)button : -1;
                if (buttonInt != (int)Plugin.TransferMouseButton.Value) return;

                var card = GetCardFromGraphics(__instance);
                if (card == null) { WarnSkipOnce("card"); return; }

                var cardModel = GetMemberValue(card, "CardModel");
                if (cardModel == null) { WarnSkipOnce("CardModel"); return; }

                savedUniqueId = GetMemberValue(cardModel, "UniqueID")?.ToString();
                if (string.IsNullOrEmpty(savedUniqueId)) { WarnSkipOnce("UniqueID"); return; }

                var slot = GetCurrentSlot(card);
                if (slot == null) { WarnSkipOnce("slot"); return; }

                savedSourceSlot = slot;
                savedTransferCount = ResolveTransferCount(Plugin.GetEffectiveTransferAmount(), slot);
                // Vanilla's InGameCardBase.OnPointerClick only reaches SwapCard on a right-click, so
                // any other trigger button moves nothing before our coroutine starts.
                savedVanillaMoves = buttonInt == (int)TransferButton.Right;
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
                var vanillaMoved = savedVanillaMoves;
                int additionalCount = vanillaMoved ? savedTransferCount - 1 : savedTransferCount;
                if (additionalCount <= 0) return;

                var sourceSlot = savedSourceSlot;
                var uniqueId = savedUniqueId;
                var totalCount = savedTransferCount;

                if (sourceSlot == null || string.IsNullOrEmpty(uniqueId)) return;

                Plugin.ShowNotification(Plugin.AmountText(totalCount));

                Logger.LogDebug($"[QT] Postfix: starting coroutine for uid={uniqueId}, total={totalCount}, additional={additionalCount}, vanillaMoved={vanillaMoved}");
                Plugin.Instance.StartCoroutine(TransferCardsCoroutine(sourceSlot, uniqueId, additionalCount, vanillaMoved));
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
                savedVanillaMoves = false;
            }
        }

        // Transfers cards from the source slot one per frame.
        // Re-scans for any matching card each iteration to handle both stacked items
        // (single CardGraphics object representing N cards) and individual card objects.
        static IEnumerator TransferCardsCoroutine(object sourceSlot, string uniqueId, int count, bool vanillaMoved)
        {
            // The click that started this batch already moved a card when vanilla acted on it.
            int baseline = vanillaMoved ? 1 : 0;
            int transferred = 0;
            int consecutiveFailures = 0;
            const int MaxConsecutiveFailures = 3;

            // Every exit below owes the player the one batch sound that stands in for the per-card
            // sounds the prefix muted - including the ones Unity triggers by disposing the
            // coroutine, which no `yield break` would reach.
            try
            {
                object candidate = FindFirstCandidate(sourceSlot, uniqueId);
                Logger.LogDebug($"[QT] Coroutine start: count={count}, initial candidate={(candidate == null ? "NULL" : "found")}");

                while (transferred < count)
                {
                    yield return null;

                    // Re-validate cached candidate; re-scan only when it leaves the slot it was found
                    // in (the clicked slot, or a sibling slot when Drain Matching Stacks is on).
                    if (candidate == null || !IsValidCandidate(candidate, sourceSlot, uniqueId, Plugin.DrainMatchingStacks.Value))
                        candidate = FindFirstCandidate(sourceSlot, uniqueId);

                    if (candidate == null)
                    {
                        consecutiveFailures++;
                        if (consecutiveFailures >= MaxConsecutiveFailures)
                        {
                            Logger.LogDebug($"[QT] Done: transferred {baseline + transferred} cards total (no more matching cards after {transferred} coroutine moves)");
                            yield break;
                        }
                        continue;
                    }

                    consecutiveFailures = 0;

                    // Snapshot the slot's pile count so a refused move (destination full/incompatible -
                    // vanilla leaves the card in place, e.g. GraphicsManager.MoveCardToSlot on an
                    // over-weight target) can be told apart from an actual transfer. Invoke() not
                    // throwing does NOT mean the card moved.
                    // Measured on the slot the candidate actually sits in: under Drain Matching
                    // Stacks that is a sibling of the clicked slot, whose own pile is what shrinks.
                    // With draining off it is the clicked slot itself, exactly as before.
                    object candidateSlot = GetCurrentSlot(GetCardFromGraphics(candidate)) ?? sourceSlot;
                    int pileCountBefore = GetPileCount(candidateSlot);

                    var newPointer = new PointerEventData(EventSystem.current);
                    // Always right: this is the button vanilla's OnPointerClick routes to SwapCard,
                    // whatever button the player configured to TRIGGER the batch.
                    newPointer.button = PointerEventData.InputButton.Right;

                    isTransferring = true;
                    try
                    {
                        onPointerClickMethod.Invoke(candidate, new object[] { newPointer });
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"Transfer failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
                        Logger.LogDebug($"[QT] Done (exception): transferred {baseline + transferred} cards total");
                        yield break;
                    }
                    finally
                    {
                        isTransferring = false;
                    }

                    int pileCountAfter = GetPileCount(candidateSlot);

                    // A negative count means the pile-count API couldn't be resolved via reflection;
                    // fall back to the prior "assume success" behavior rather than stalling forever.
                    bool countUnknown = pileCountBefore < 0 || pileCountAfter < 0;
                    bool progressMade = countUnknown || pileCountAfter < pileCountBefore;

                    if (progressMade)
                    {
                        transferred++;
                        continue;
                    }

                    consecutiveFailures++;
                    Logger.LogDebug($"[QT] No progress (pile count unchanged at {pileCountAfter}) - destination likely refused the transfer");
                    if (consecutiveFailures >= MaxConsecutiveFailures)
                    {
                        Logger.LogDebug($"[QT] Done (no progress): transferred {baseline + transferred} cards total, stopping after {MaxConsecutiveFailures} refused transfers");
                        yield break;
                    }
                }

                Logger.LogDebug($"[QT] Done: transferred {baseline + transferred} cards total (reached requested count)");
            }
            finally
            {
                PlayBatchCompletionSound();
            }
        }

        // The Half preset leaves Plugin.GetEffectiveTransferAmount as a sentinel because the overlay
        // has no slot; here, in the click prefix, the clicked slot is in hand. This runs BEFORE
        // vanilla moves the first card, so a stack of 8 reads 8 and yields 4, with vanilla's own
        // move being the first of those 4 (7 -> 4, 1 -> 1).
        static int ResolveTransferCount(int requested, object slot)
        {
            if (requested != Plugin.HalfSentinel) return requested;

            int pile = GetPileCount(slot);
            if (pile <= 0)
            {
                // Pile count unresolvable: store a concrete preset rather than let the sentinel
                // reach the count accounting downstream.
                Logger.LogDebug($"[QT] Half preset: pile count unavailable ({pile}); using the Ctrl preset ({Plugin.CtrlPresetAmount.Value}) instead");
                return Plugin.CtrlPresetAmount.Value;
            }
            return Mathf.CeilToInt(pile / 2f);
        }

        // Reads DynamicLayoutSlot.CardPileCount(bool) via cached reflection. Returns -1 if the
        // method can't be resolved (caller treats -1 as "can't tell — assume success").
        static int GetPileCount(object slot)
        {
            if (slot == null) return -1;
            try
            {
                var slotType = slot.GetType();
                if (cardPileCountMethod == null || cardPileCountMethodOwner != slotType)
                {
                    cardPileCountMethod = AccessTools.Method(slotType, "CardPileCount");
                    cardPileCountMethodOwner = slotType;
                }
                if (cardPileCountMethod == null) return -1;

                var result = cardPileCountMethod.Invoke(slot, new object[] { true });
                return result is int count ? count : -1;
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"[QT] GetPileCount reflection failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
                return -1;
            }
        }

        // The clicked slot is always drained first. Only once it holds no candidate, and only with
        // Drain Matching Stacks on, does the scan widen to that slot's siblings in the same container.
        static object FindFirstCandidate(object sourceSlot, string uniqueId)
        {
            var allGraphics = UnityEngine.Object.FindObjectsOfType(cardGraphicsType);
            if (allGraphics == null) return null;
            foreach (var g in allGraphics)
            {
                if (IsValidCandidate(g, sourceSlot, uniqueId, allowSiblingSlots: false))
                    return g;
            }
            if (!Plugin.DrainMatchingStacks.Value) return null;
            foreach (var g in allGraphics)
            {
                if (IsValidCandidate(g, sourceSlot, uniqueId, allowSiblingSlots: true))
                    return g;
            }
            return null;
        }

        static bool IsValidCandidate(object graphics, object sourceSlot, string uniqueId, bool allowSiblingSlots)
        {
            if (graphics == null) return false;
            var card = GetCardFromGraphics(graphics);
            if (card == null) return false;
            var cardSlot = GetCurrentSlot(card);
            if (cardSlot == null) return false;
            if (!ReferenceEquals(cardSlot, sourceSlot) && !(allowSiblingSlots && IsSiblingSlot(cardSlot, sourceSlot))) return false;
            var cardModel = GetMemberValue(card, "CardModel");
            if (cardModel == null) return false;
            var cardType = GetMemberValue(cardModel, "CardType");
            if (cardType != null && Convert.ToInt32(cardType) == 8) return false;
            var cannotTransfer = GetMemberValue(cardModel, "CannotBeTransferred");
            if (cannotTransfer is bool ct && ct) return false;
            var cardId = GetMemberValue(cardModel, "UniqueID")?.ToString();
            return cardId == uniqueId;
        }

        // "Same container" means both DynamicLayoutSlots were built by the same DynamicViewLayoutGroup
        // and share a SlotType. ParentLayoutGroup is the public field the layout-group constructor
        // assigns (.decomp/DynamicLayoutSlot.cs, `DynamicLayoutSlot(SlotSettings, DynamicElementRef,
        // DynamicViewLayoutGroup)`) and the standalone-CardSlot constructor leaves null - so one open
        // inventory's slots share it, the board's item area shares another, and the two never
        // match. A null on either side means the container cannot be resolved; then the strict
        // same-slot rule stands rather than widening the match blindly.
        static bool IsSiblingSlot(object cardSlot, object sourceSlot)
        {
            if (cardSlot == null || sourceSlot == null) return false;
            if (cardSlot.GetType() != sourceSlot.GetType()) return false;

            var cardGroup   = GetMemberValue(cardSlot, "ParentLayoutGroup");
            var sourceGroup = GetMemberValue(sourceSlot, "ParentLayoutGroup");
            if (cardGroup == null || sourceGroup == null || !ReferenceEquals(cardGroup, sourceGroup)) return false;

            var cardSlotType   = GetMemberValue(cardSlot, "SlotType");
            var sourceSlotType = GetMemberValue(sourceSlot, "SlotType");
            return cardSlotType != null && sourceSlotType != null && cardSlotType.Equals(sourceSlotType);
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
