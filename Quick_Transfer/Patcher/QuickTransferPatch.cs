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

        // Same once-per-cause breadcrumb for the optional features, where a failed read does not
        // skip the transfer but quietly turns the feature off (the tag filter falls back to the
        // clicked item, the spoilage ordering to the usual order).
        internal static void WarnFeatureOnce(string feature, string cause)
        {
            if (warnedSkipCauses.Add(feature + ":" + cause))
                Logger.LogWarning($"{feature} inactive: could not read {cause}. Transfers still work without it. If this repeats after a game update, a member was renamed.");
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
        // Set only while the Tag Filter Key is held and the clicked card carries a listed tag:
        // the batch then matches that tag instead of savedUniqueId.
        private static string savedFilterTag = null;
        // The CardTag object behind savedFilterTag (plan Feature 5): the start notice names the tag by its
        // InGameName, which only the object carries.
        private static object savedFilterTagObject = null;
        private static bool savedCtrlRightClick = false;
        private static int savedTransferCount = 1;
        // True when the click we intercepted is one vanilla will itself act on, i.e. the first card
        // moves without our help. False for a trigger button vanilla ignores, where the batch owes
        // the player the full count rather than count-1.
        private static bool savedVanillaMoves = false;
        // Where the click's card would go, read before vanilla moves it (BatchContext, plan Feature 3).
        private static BatchContext savedContext = null;
        // The clicked slot's pile count before vanilla's own move (plan Feature 4), so the postfix can tell a
        // move the destination took from one it refused. -1 when no vanilla move is expected or the count is unreadable.
        private static int savedPileBefore = -1;
        // The clicked card itself: a pile count alone misreads a move when the card was the last of its pile and the
        // container closed the gap (see CardLeftSlot), so the postfix also asks whether this card left its slot.
        private static object savedClickedCard = null;
        // Whether the batch may draw on the clicked slot's siblings (plan Feature 7), decided once at arming so a
        // config change cannot alter a running batch: a tag batch, Drain Matching Stacks, or a card that cannot pile.
        private static bool savedWiden = false;
        // The spoil fraction (0..1) a card needs to join a Spoiled Only batch (plan Feature 8), or -1 for a batch
        // without the key.
        private static float savedSpoiledMin = -1f;

        // One batch at a time (plan Feature 3). Every armed modifier+click takes the next number; a
        // running batch whose number is no longer the latest stops, so a new click replaces the old
        // batch instead of running beside it. isTransferring guards only each synthesized invoke, so
        // without this a second trigger between frames started a second coroutine.
        private static int batchGeneration = 0;

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

        // Clears everything the prefix captures for the postfix, so a click the prefix leaves (or skips) arms nothing.
        static void ClearSavedClick()
        {
            savedSourceSlot = null;
            savedUniqueId = null;
            savedFilterTag = null;
            savedFilterTagObject = null;
            savedCtrlRightClick = false;
            savedTransferCount = 1;
            savedVanillaMoves = false;
            savedContext = null;
            savedPileBefore = -1;
            savedClickedCard = null;
            savedWiden = false;
            savedSpoiledMin = -1f;
        }

        // The one path on which the prefix skips the game's own click (plan Feature 6; Feature 8 reuses it): the
        // clicked card must stay where it is, which only skipping the original can do, since vanilla moves the
        // clicked card regardless. Arms nothing, so nothing waits on the postfix, which still runs after a skipped
        // original and leaves at once because savedCtrlRightClick is false. A batch that should still run without
        // the clicked card (Feature 8) is started by the caller, from its own locals, before this clears the state.
        static bool SkipClick(string reason)
        {
            ClearSavedClick();
            Logger.LogDebug($"[QT] Click skipped ({reason})");
            return false;
        }

        // PREFIX: captures source slot BEFORE the card moves. Returns true (the game's click runs) on every path
        // but SkipClick's.
        static bool OnPointerClick_Prefix(object __instance, object _Pointer)
        {
            ClearSavedClick();

            if (isTransferring) return true;

            try
            {
                if (!Plugin.IsModifierKeyHeld()) return true;

                var buttonProp = AccessTools.Property(_Pointer.GetType(), "button");
                if (buttonProp == null) { WarnSkipOnce("pointer button"); return true; }
                var button = buttonProp.GetValue(_Pointer, null);
                int buttonInt = button != null ? (int)button : -1;
                if (buttonInt != (int)Plugin.TransferMouseButton.Value) return true;

                var card = GetCardFromGraphics(__instance);
                if (card == null) { WarnSkipOnce("card"); return true; }

                var cardModel = GetMemberValue(card, "CardModel");
                if (cardModel == null) { WarnSkipOnce("CardModel"); return true; }

                // Arm only on a click vanilla acts on. InGameCardBase.OnPointerClick reaches SwapCard only
                // when the card is not pinned (IsPinned) and then only for CardType == Item, so on a pinned
                // placeholder or a placed structure the first card never moves and arming would start a
                // coroutine for a click the game ignored. Left unarmed, the click stays exactly vanilla's.
                if (IsPinnedCard(card) || IsNonItemCard(cardModel)) return true;

                savedUniqueId = GetMemberValue(cardModel, "UniqueID")?.ToString();
                if (string.IsNullOrEmpty(savedUniqueId)) { WarnSkipOnce("UniqueID"); return true; }

                var slot = GetCurrentSlot(card);
                if (slot == null) { WarnSkipOnce("slot"); return true; }

                // Per-container settings (plan Features 11 and 12). Only a click that goes into or out of the OPEN
                // container reads them: an Item-row card pushes into the inspected card, an Inventory card pulls from
                // it (SwapCard), so a Base, Equipment or Trading click never matches a listed container.
                var clickContext = BatchContext.Read(card);
                string clickSlotType = GetMemberValue(slot, "SlotType")?.ToString();
                string containerUid = null;
                if (clickContext.Readable && clickContext.InspectedAlive && !clickContext.TradingWindowActive
                    && (clickSlotType == "Item" || clickSlotType == "Inventory"))
                    containerUid = clickContext.InspectedUniqueId;
                // An excluded container leaves the click exactly vanilla's: one card, no batch.
                if (ContainerSettings.IsExcluded(containerUid))
                {
                    Logger.LogDebug($"[QT] Click left to the game: container {containerUid} is on the exclusion list");
                    return true;
                }

                int requestedAmount = Plugin.GetEffectiveTransferAmount();
                var remembered = ContainerSettings.RememberedAmount(containerUid, ContainerSettings.ComboNow());
                if (remembered != null)
                {
                    Logger.LogDebug($"[QT] Container {containerUid}: remembered amount {remembered.Value} replaces the preset {requestedAmount}");
                    requestedAmount = remembered.Value;
                }
                int transferCount = ResolveTransferCount(requestedAmount, slot);
                // All but one of a pile of one is nothing: that card stays, so the game's own move must not run.
                if (transferCount <= 0) return SkipClick("All but one: the clicked pile holds one card");

                savedSourceSlot = slot;
                if (Plugin.TagFilterHeld()) savedFilterTag = ResolveFilterTag(cardModel, out savedFilterTagObject);
                // Spoiled Only (plan Feature 8): the least spoil fraction a card needs to move, or -1 when the key is
                // up. Read once here, like the widen flag, so a config change cannot alter a running batch.
                savedSpoiledMin = Plugin.SpoiledOnlyHeld() ? Plugin.SpoiledOnlyThreshold.Value / 100f : -1f;
                // A spoiled-only batch always widens, as a tag batch does: "the food about to go" lies in any slot.
                savedWiden = savedSpoiledMin >= 0f || WidensToSiblings(savedFilterTag) || (Plugin.WidenForNonPilingCards.Value && CannotPile(slot, cardModel));
                savedTransferCount = transferCount;

                // The clicked card is judged here, before the game's own click moves it. One that is not spoiled
                // enough must stay, so the game's click is skipped and the batch starts now, with nothing moved
                // yet, and moves only the cards that qualify.
                if (savedSpoiledMin >= 0f && !IsSpoiledEnough(card, savedSpoiledMin))
                {
                    var context = BatchContext.Read(card);
                    int generation = ++batchGeneration;
                    // A new batch replaces the one an undo would reverse (plan Feature 10).
                    BatchRecord.Clear(generation);
                    string uid = savedUniqueId, tag = savedFilterTag;
                    object tagObject = savedFilterTagObject;
                    bool widen = savedWiden;
                    float spoiledMin = savedSpoiledMin;
                    Plugin.ShowNotification(StartNotice(transferCount, tag, tagObject));
                    Logger.LogDebug($"[QT] Spoiled Only: starting coroutine without the clicked card for uid={uid}, tag={tag ?? "none"}, total={transferCount}, min={spoiledMin}");
                    Plugin.Instance.StartCoroutine(TransferCardsCoroutine(slot, uid, tag, transferCount, transferCount, false, context, generation, widen, spoiledMin));
                    return SkipClick("Spoiled Only: the clicked card is not spoiled enough to move");
                }

                // Vanilla's InGameCardBase.OnPointerClick only reaches SwapCard on a right-click, so
                // any other trigger button moves nothing before our coroutine starts.
                savedVanillaMoves = buttonInt == (int)TransferButton.Right;
                if (savedVanillaMoves) { savedPileBefore = GetPileCount(slot, includePin: true); savedClickedCard = card; }
                savedContext = BatchContext.Read(card);
                savedCtrlRightClick = true;
                // Armed: this click replaces whatever batch is still running, even one it will not
                // follow with a coroutine of its own (a count of 1 is vanilla's single move). It also
                // replaces the batch an undo would reverse (plan Feature 10), so after a single-card
                // modifier+click the undo has nothing left to reverse rather than an older batch.
                batchGeneration++;
                BatchRecord.Clear(batchGeneration);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error in prefix: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
            return true;
        }

        // GameManager.HoveredCard (public static InGameCardBase), set by InGameCardBase.OnHoverEnter and cleared
        // by OnHoverExit. Resolved once by assembly name, because ModCore ships a GameManager of its own.
        private static FieldInfo hoveredCardField;
        private static bool hoveredCardResolved;

        // Hover-and-press (plan Feature 9): replays the click the hovered card would have received, so the prefix
        // and postfix run exactly as for a physical modifier+click (modifier, trigger button, pinned and
        // non-Item checks, the batch and its guards). Called from Plugin.Update on the hotkey's key-down with a
        // modifier held. Nothing hovered is the normal case and returns quietly; an unreadable member warns once.
        public static void RequestHoverTransfer()
        {
            const string Feature = "Transfer Hotkey";
            if (isTransferring) return;
            try
            {
                if (onPointerClickMethod == null) { WarnFeatureOnce(Feature, "CardGraphics.OnPointerClick"); return; }

                if (!hoveredCardResolved)
                {
                    hoveredCardResolved = true;
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (asm.GetName().Name != "Assembly-CSharp") continue;
                        var gm = asm.GetType("GameManager");
                        if (gm != null) hoveredCardField = AccessTools.Field(gm, "HoveredCard");
                        break;
                    }
                }
                if (hoveredCardField == null) { WarnFeatureOnce(Feature, "GameManager.HoveredCard"); return; }

                var hovered = hoveredCardField.GetValue(null);
                if (hovered == null || (hovered is UnityEngine.Object hoveredObj && hoveredObj == null))
                {
                    Logger.LogDebug("[QT] Transfer Hotkey: no card is hovered");
                    return;
                }

                var visuals = GetMemberValue(hovered, "CardVisuals");
                if (visuals == null || (visuals is UnityEngine.Object visualsObj && visualsObj == null))
                {
                    WarnFeatureOnce(Feature, "InGameCardBase.CardVisuals");
                    return;
                }

                var pointer = new PointerEventData(EventSystem.current);
                pointer.button = Plugin.TransferMouseButton.Value == TransferButton.Middle
                    ? PointerEventData.InputButton.Middle
                    : PointerEventData.InputButton.Right;
                Logger.LogDebug($"[QT] Transfer Hotkey: replaying a {pointer.button} click on the hovered card");
                onPointerClickMethod.Invoke(visuals, new object[] { pointer });
            }
            catch (Exception ex)
            {
                Logger.LogError($"Transfer Hotkey failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        /// <summary>
        /// Why the last <see cref="TryClickMoveOne"/> moved nothing (in English, for a log line), or null after a move.
        /// </summary>
        public static string LastRefusal { get; private set; }

        /// <summary>
        /// One-card click replay for other mods (Utility Belt plan section 2.2 R9): sends <paramref name="card"/> (an
        /// InGameCardBase) the right-click the game itself turns into a move, through the card's own CardGraphics.OnPointerClick,
        /// so InGameCardBase.OnPointerClick, SwapCard and GraphicsManager.MoveCardToSlot run exactly as for a player's click and
        /// keep every drag guard (weight, a free slot, a locked inventory, a container's filter). The destination is the
        /// game's own choice from what is on screen: a card in a container, on the ground or worn goes to the carried row; a
        /// carried card goes into the open container. One card, no batch, no undo record, no notice. Before the click it
        /// refuses what the game's click would ignore or Quick Transfer never moves: a destroyed or model-less card, a non-Item,
        /// a pinned placeholder, a CannotBeTransferred card (the game's click path does not check it), a card Utility Belt has
        /// locked, a card with no visible slot or no CardVisuals (inside a closed container), and any call while a batch is
        /// running. Returns true when the card left its slot; otherwise false with <see cref="LastRefusal"/> saying why. A
        /// public entry used by nothing in Quick Transfer itself: with no caller it does nothing. Never throws.
        /// </summary>
        public static bool TryClickMoveOne(object card)
        {
            LastRefusal = null;
            try
            {
                if (onPointerClickMethod == null) return RefuseMoveOne("CardGraphics.OnPointerClick was not found");
                if (isTransferring || runningBatches > 0) return RefuseMoveOne("a Quick Transfer batch is running");
                if (card == null || (card is UnityEngine.Object cardObj && cardObj == null)) return RefuseMoveOne("no card");
                if (GetMemberValue(card, "Destroyed") is bool destroyed && destroyed) return RefuseMoveOne("the card is destroyed");
                var cardModel = GetMemberValue(card, "CardModel");
                if (cardModel == null) return RefuseMoveOne("the card has no CardModel");
                if (IsNonItemCard(cardModel)) return RefuseMoveOne("the card is not an Item");
                if (GetMemberValue(cardModel, "CannotBeTransferred") is bool cannot && cannot) return RefuseMoveOne("the card cannot be transferred");
                if (IsPinnedCard(card)) return RefuseMoveOne("the card is a pinned placeholder");
                if (UtilityBeltLockBridge.IsLocked(card)) return RefuseMoveOne("the card is locked in Utility Belt");
                var slot = GetCurrentSlot(card);
                if (slot == null) return RefuseMoveOne("the card is in no visible slot (inside a closed container?)");
                var visuals = GetMemberValue(card, "CardVisuals");
                if (visuals == null || (visuals is UnityEngine.Object visualsObj && visualsObj == null))
                    return RefuseMoveOne("the card has no CardVisuals (inside a closed container, or under its pile's top card?)");

                int pileBefore = GetPileCount(slot, includePin: true);
                var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Right };
                // isTransferring keeps the click prefix from arming a batch should a modifier be held this frame. It also mutes
                // the move's card sound, which is replayed once below exactly as a batch replays its one sound.
                isTransferring = true;
                try
                {
                    onPointerClickMethod.Invoke(visuals, new object[] { pointer });
                }
                finally
                {
                    isTransferring = false;
                }
                PlayBatchCompletionSound();

                int pileAfter = GetPileCount(slot, includePin: true);
                bool moved = CardLeftSlot(card, slot) || (pileBefore >= 0 && pileAfter >= 0 && pileAfter < pileBefore);
                if (!moved) return RefuseMoveOne("the game did not move it (the destination is full or does not take it)");
                Logger.LogDebug("[QT] TryClickMoveOne: moved one card");
                return true;
            }
            catch (Exception ex)
            {
                return RefuseMoveOne($"the click threw ({(ex.InnerException ?? ex).GetType().Name}: {(ex.InnerException ?? ex).Message})");
            }
        }

        static bool RefuseMoveOne(string reason)
        {
            LastRefusal = reason;
            Logger.LogDebug($"[QT] TryClickMoveOne: nothing moved, {reason}");
            return false;
        }

        // Reads the open container for a key press (plan Features 11 and 12): any live card graphic reaches the one
        // GraphicsManager through its own GraphicsM, as a click does. Null, with a notice-free quiet return, when no
        // container is open; an unreadable member warns once.
        static BatchContext ReadOpenContainer(string feature)
        {
            if (cardGraphicsType == null) return null;
            object card = null;
            var allGraphics = UnityEngine.Object.FindObjectsOfType(cardGraphicsType);
            if (allGraphics != null)
                foreach (var g in allGraphics)
                {
                    card = GetCardFromGraphics(g);
                    if (card != null && !(card is UnityEngine.Object dead && dead == null)) break;
                    card = null;
                }
            if (card == null) { Logger.LogDebug($"[QT] {feature}: no card on screen to read the open container from"); return null; }
            var context = BatchContext.Read(card);
            if (!context.Readable) { WarnFeatureOnce(feature, "the open container"); return null; }
            if (!context.InspectedAlive || string.IsNullOrEmpty(context.InspectedUniqueId))
            {
                Logger.LogDebug($"[QT] {feature}: no container is open");
                return null;
            }
            return context;
        }

        // The open container's own name, through ToString() so the game picks the language (as TagDisplayName does);
        // the UniqueID stands in when it cannot be read.
        static string ContainerDisplayName(BatchContext context)
        {
            try
            {
                var model = GetMemberValue(context.InspectedCard, "CardModel");
                var name = model == null ? null : GetMemberValue(model, "CardName")?.ToString();
                return string.IsNullOrWhiteSpace(name) ? context.InspectedUniqueId : name;
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"[QT] Container name unreadable for {context.InspectedUniqueId}: {ex.InnerException?.ToString() ?? ex.ToString()}");
                return context.InspectedUniqueId;
            }
        }

        // Exclude Container Key (plan Feature 11): toggles the open container on the exclusion list and says which way.
        public static void RequestToggleExcluded()
        {
            const string Feature = "Exclude Container Key";
            try
            {
                var context = ReadOpenContainer(Feature);
                if (context == null) return;
                bool excluded = ContainerSettings.ToggleExcluded(context.InspectedUniqueId);
                var name = ContainerDisplayName(context);
                Plugin.ShowNotification(excluded
                    ? OverlayText.Format(OverlayText.ContainerOffKey, OverlayText.ContainerOffDefault, name)
                    : OverlayText.Format(OverlayText.ContainerOnKey, OverlayText.ContainerOnDefault, name));
                Logger.LogDebug($"[QT] Container {context.InspectedUniqueId} is now {(excluded ? "excluded from" : "included in")} bulk transfer");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Exclude Container Key failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // Remember Amount Key (plan Feature 12): stores the held modifier's current preset for the open container, or
        // forgets it when that same amount is already stored. Ctrl+Shift, a configured non-Ctrl/Shift modifier and
        // Full Stack Mode have no preset to remember, so they do nothing.
        public static void RequestToggleRemembered()
        {
            const string Feature = "Remember Amount Key";
            try
            {
                var combo = ContainerSettings.ComboNow();
                if (combo == null) { Logger.LogDebug("[QT] Remember Amount Key: needs presets on and exactly one of Shift or Ctrl held"); return; }
                var context = ReadOpenContainer(Feature);
                if (context == null) return;
                int amount = combo == "Ctrl" ? Plugin.CtrlPresetAmount.Value : Plugin.ShiftPresetAmount.Value;
                bool remembered = ContainerSettings.ToggleRemembered(context.InspectedUniqueId, combo, amount);
                Plugin.ShowNotification(remembered
                    ? OverlayText.Format(OverlayText.RememberedKey, OverlayText.RememberedDefault, Plugin.AmountLabel(amount), ContainerDisplayName(context))
                    : OverlayText.Format(OverlayText.ForgotKey, OverlayText.ForgotDefault, ContainerDisplayName(context)));
                Logger.LogDebug($"[QT] Container {context.InspectedUniqueId}/{combo}: {(remembered ? "remembered " + amount : "forgotten")}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Remember Amount Key failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // Batch coroutines alive right now (TransferCardsCoroutine counts itself in and out). A running batch has not
        // stored its record yet, so an undo pressed during one would reverse the batch before it.
        private static int runningBatches = 0;

        /// <summary>What one undo moved back, summed over its items, and whether its destination changed under it.</summary>
        internal sealed class UndoTally
        {
            public int Moved;
            public bool Stopped;
        }

        // Undo last batch (plan Feature 10): moves the last batch back, the same count of each item it moved, the
        // opposite way, while the same container (or board, character window or trade) is still what the game would
        // send a card to. Called from Plugin.Update on the Undo Key's key-down with a modifier held. One level: the
        // record is cleared by the undo itself, by any new armed modifier+click and by any change of that context
        // (WatchUndoContext), so a second press reads "Nothing to undo". Each item goes through the same coroutine as
        // a batch (IsValidCandidate's CannotBeTransferred, location, pinned and destroyed-card guards included), with
        // no game click of its own (vanillaMoved false) and always widened, so it draws on every slot of that side.
        // It restores counts of an item, not the same physical cards: two cards of one UniqueID can differ in
        // spoilage and durability, and the undo takes whichever of them sits on the other side.
        public static void RequestUndo()
        {
            const string Feature = "Undo";
            if (isTransferring) return;
            try
            {
                if (onPointerClickMethod == null) { WarnFeatureOnce(Feature, "CardGraphics.OnPointerClick"); return; }

                if (runningBatches > 0) { NothingToUndo("a batch is still running"); return; }

                var record = BatchRecord.Last;
                if (record == null || record.Moved <= 0) { NothingToUndo("no batch to reverse"); return; }

                // The undo needs to know the destination still holds: an unreadable context cannot show that, so it
                // refuses (a batch fails open on an unreadable context; an undo fails closed).
                var context = record.Context;
                var now = context?.ReadAgain();
                if (context == null || !context.Readable || now == null || !now.Readable)
                {
                    BatchRecord.Clear(batchGeneration + 1);
                    NothingToUndo("the batch's destination cannot be read");
                    return;
                }
                var changed = context.ChangedFrom(now);
                if (changed != null)
                {
                    BatchRecord.Clear(batchGeneration + 1);
                    NothingToUndo($"the destination changed ({changed})");
                    return;
                }

                var reverseTypes = ReverseSlotTypes(record.ClickedSlotType, now);
                if (reverseTypes == null)
                {
                    BatchRecord.Clear(batchGeneration + 1);
                    NothingToUndo($"no way back from a {record.ClickedSlotType ?? "?"} slot");
                    return;
                }

                // Every recorded item must still be on the other side before any card moves, so an undo is all or
                // nothing at the start (a refused card later can still leave it short, which the notice counts).
                var steps = new List<KeyValuePair<string, int>>();
                foreach (var kv in record.MovedByUid)
                {
                    if (kv.Key == "?") { Logger.LogDebug($"[QT] Undo: {kv.Value} card(s) of an unreadable UniqueID cannot be reversed"); continue; }
                    if (FindUndoCandidate(kv.Key, reverseTypes, now) == null)
                    {
                        BatchRecord.Clear(batchGeneration + 1);
                        NothingToUndo($"no card of {kv.Key} is left in a {string.Join("/", reverseTypes)} slot");
                        return;
                    }
                    steps.Add(kv);
                }
                if (steps.Count == 0) { BatchRecord.Clear(batchGeneration + 1); NothingToUndo("no item of the batch can be reversed"); return; }

                // The undo is a batch of its own: it replaces anything running and clears the record, so a second
                // press does nothing and the undo itself cannot be undone.
                int generation = ++batchGeneration;
                BatchRecord.Clear(generation);
                Logger.LogDebug($"[QT] Undo: reversing {record.Moved} card(s) of {steps.Count} item(s) from {string.Join("/", reverseTypes)} (the batch was clicked in {record.ClickedSlotType})");
                Plugin.Instance.StartCoroutine(UndoCoroutine(steps, reverseTypes, now, generation));
            }
            catch (Exception ex)
            {
                Logger.LogError($"Undo failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        static void NothingToUndo(string reason)
        {
            Plugin.ShowNotification(OverlayText.Get(OverlayText.NothingToUndoKey, OverlayText.NothingToUndoDefault));
            Logger.LogDebug($"[QT] Undo: nothing to undo ({reason})");
        }

        // Runs the undo one item at a time, each as its own batch on the reverse side, then shows "Undid N" with the
        // cards that really moved back. Each item's first card is found again when its turn comes, because the items
        // before it may have closed gaps in that container. Leaves quietly if a newer click replaced it, and with the
        // batch stop notice (shown by the coroutine) if the destination changed.
        static IEnumerator UndoCoroutine(List<KeyValuePair<string, int>> steps, string[] reverseTypes, BatchContext context, int generation)
        {
            var tally = new UndoTally();
            foreach (var step in steps)
            {
                if (generation != batchGeneration) { Logger.LogDebug("[QT] Undo done (superseded by a newer batch)"); yield break; }
                if (tally.Stopped) break;

                var first = FindUndoCandidate(step.Key, reverseTypes, context);
                var slot = first == null ? null : GetCurrentSlot(GetCardFromGraphics(first));
                if (slot == null) { Logger.LogDebug($"[QT] Undo: no card of {step.Key} left to move back"); continue; }

                yield return Plugin.Instance.StartCoroutine(TransferCardsCoroutine(slot, step.Key, null, step.Value, step.Value, false, context, generation, true, -1f, tally));
            }

            Logger.LogDebug($"[QT] Undo done: moved {tally.Moved} card(s) back (stopped={tally.Stopped}, replaced={generation != batchGeneration})");
            if (generation == batchGeneration && !tally.Stopped)
                Plugin.ShowNotification(OverlayText.Format(OverlayText.UndidKey, OverlayText.UndidDefault, tally.Moved));
        }

        // Which slot types the last batch's cards sit in now, read from SwapCard's own routing (.decomp/InGameCardBase.cs):
        // an Item-row card went to Trading with a trade open, into the inspected card's Inventory with a container open,
        // to Equipment with the character window up, else to the Base row; a Base, Inventory or Equipment card went to
        // the Item row; a Trading card went to the Item row or the NPC's TradeInventory; a TradeInventory card went to
        // Trading. Compared by the enum member's name, so no game enum is referenced. Null for a slot type SwapCard
        // never moves a card out of.
        static string[] ReverseSlotTypes(string clickedSlotType, BatchContext context)
        {
            switch (clickedSlotType)
            {
                case "Item":
                    if (context.InspectedAlive) return context.TradingWindowActive ? new[] { "Trading" } : new[] { "Inventory" };
                    return context.CharacterWindowActive ? new[] { "Equipment" } : new[] { "Base" };
                case "Base":
                case "Inventory":
                case "Equipment":
                    return new[] { "Item" };
                case "Trading":
                    return new[] { "Item", "TradeInventory" };
                case "TradeInventory":
                    return new[] { "Trading" };
                default:
                    return null;
            }
        }

        // The first card of <paramref name="uid"/> on the reverse side that a batch may move: a slot of one of the
        // reverse types, IsValidCandidate's guards, and for an Inventory slot a card whose container is the open one
        // (InGameCardBase.CurrentContainer), so a card in some other container's slot is never taken.
        static object FindUndoCandidate(string uid, string[] reverseTypes, BatchContext context)
        {
            var allGraphics = UnityEngine.Object.FindObjectsOfType(cardGraphicsType);
            if (allGraphics == null) return null;
            foreach (var type in reverseTypes)
            {
                foreach (var g in allGraphics)
                {
                    var card = GetCardFromGraphics(g);
                    if (card == null) continue;
                    var slot = GetCurrentSlot(card);
                    if (slot == null) continue;
                    if (!string.Equals(GetMemberValue(slot, "SlotType")?.ToString(), type, StringComparison.Ordinal)) continue;
                    if (!IsValidCandidate(g, slot, uid, null, allowSiblingSlots: false, spoiledMin: -1f)) continue;
                    if (type == "Inventory")
                    {
                        if (!Reflect.TryGetMember(card, "CurrentContainer", out var container))
                        {
                            WarnFeatureOnce("Undo", "InGameCardBase.CurrentContainer");
                            return null;
                        }
                        if (!ReferenceEquals(container, context.InspectedCard)) continue;
                    }
                    return g;
                }
            }
            return null;
        }

        // The undo record is cleared by any change of the context it was made in (plan Feature 10, "cleared by any
        // context change"), so closing a chest and opening it again leaves nothing to undo. Called from Plugin.Update;
        // reads the context every fifth frame and only while a record is held. A press of the Undo Key checks the
        // context itself, so the skipped frames never let a stale record through.
        public static void WatchUndoContext()
        {
            var record = BatchRecord.Last;
            if (record == null || Time.frameCount % 5 != 0) return;
            var context = record.Context;
            if (context == null || !context.Readable) return;
            var changed = context.ChangedFrom(context.ReadAgain());
            if (changed == null) return;
            BatchRecord.Clear(record.Generation + 1);
            Logger.LogDebug($"[QT] Undo record cleared: the destination changed ({changed})");
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
                var filterTag = savedFilterTag;
                var filterTagObject = savedFilterTagObject;
                var totalCount = savedTransferCount;
                var context = savedContext;
                var widen = savedWiden;
                var spoiledMin = savedSpoiledMin;

                if (sourceSlot == null || string.IsNullOrEmpty(uniqueId)) return;

                // The clicked card only counts as moved when the clicked pile shrank. A destination with no room
                // refuses the game's own click too, and the batch must not report a card that never moved.
                bool vanillaConfirmed = vanillaMoved && VanillaMoveTookEffect(sourceSlot, savedPileBefore, savedClickedCard);

                Plugin.ShowNotification(StartNotice(totalCount, filterTag, filterTagObject));

                Logger.LogDebug($"[QT] Postfix: starting coroutine for uid={uniqueId}, tag={filterTag ?? "none"}, total={totalCount}, additional={additionalCount}, vanillaMoved={vanillaMoved}, vanillaConfirmed={vanillaConfirmed}, widen={widen}, spoiledMin={spoiledMin}");
                Plugin.Instance.StartCoroutine(TransferCardsCoroutine(sourceSlot, uniqueId, filterTag, additionalCount, totalCount, vanillaConfirmed, context, batchGeneration, widen, spoiledMin));
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error in postfix: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
            finally
            {
                savedSourceSlot = null;
                savedUniqueId = null;
                savedFilterTag = null;
                savedFilterTagObject = null;
                savedCtrlRightClick = false;
                savedVanillaMoves = false;
                savedContext = null;
                savedPileBefore = -1;
                savedClickedCard = null;
                savedWiden = false;
                savedSpoiledMin = -1f;
            }
        }

        // True when the game's own click took the clicked card out of its pile. The pile count is the same
        // signal the coroutine uses for every later card. An unreadable count (either side) assumes the move
        // took effect, as the coroutine does, so a game update that hides CardPileCount cannot turn every
        // batch's report into "Moved 0".
        static bool VanillaMoveTookEffect(object slot, int pileBefore, object clickedCard)
        {
            int pileAfter = GetPileCount(slot, includePin: true);
            return pileBefore < 0 || pileAfter < 0 || pileAfter < pileBefore || CardLeftSlot(clickedCard, slot);
        }

        // True when a click took <paramref name="card"/> out of <paramref name="slotBefore"/>: the card is gone, or it
        // reports a different slot now. A pile count alone misses it when the card was the last of its pile and the
        // container closed the gap: the next pile slides into the emptied slot, so the count reads HIGHER after the
        // move (measured 2026-10-02, T2.331 part f: the last of three Raw Meat read 1 before and 3 after, so a batch
        // that moved all three reported "Moved 2 of 3"). An unreadable slot is unknown, never "left".
        static bool CardLeftSlot(object card, object slotBefore)
        {
            if (card == null || slotBefore == null) return false;
            if (card is UnityEngine.Object destroyed && destroyed == null) return true;
            var now = GetCurrentSlot(card);
            return now != null && !ReferenceEquals(now, slotBefore);
        }

        // Transfers cards from the source slot one per frame.
        // Re-scans for any matching card each iteration to handle both stacked items
        // (single CardGraphics object representing N cards) and individual card objects.
        // filterTag non-null means a tag-filtered batch: candidates match the tag, not uniqueId.
        // context is where the first card went (plan Feature 3): every later card is re-clicked only
        // while that destination still holds. generation is this batch's number (batchGeneration). requested
        // is what the click asked for, counting the card the game's own click moved (Plugin.AllSentinel for
        // All); vanillaMoved is true only when that click really took a card out of its pile. Every exit ends
        // in FinishBatch, which records what moved (BatchRecord) and, for a batch that was neither stopped nor
        // replaced, tells the player when fewer cards moved than were asked for (plan Feature 4). widen is whether the
        // batch may draw on the clicked slot's siblings, decided at arming (savedWiden); spoiledMin is the Spoiled Only
        // floor (savedSpoiledMin, -1 when off). undo is non-null only for one item of an undo (plan Feature 10): that
        // batch adds what it moved to the undo's tally and stores no record and shows no notice of its own.
        static IEnumerator TransferCardsCoroutine(object sourceSlot, string uniqueId, string filterTag, int count, int requested, bool vanillaMoved, BatchContext context, int generation, bool widen, float spoiledMin, UndoTally undo = null)
        {
            // Counted from the first MoveNext, which StartCoroutine runs at once; the finally below undoes it.
            runningBatches++;
            // The click that started this batch already moved a card when vanilla acted on it.
            int baseline = vanillaMoved ? 1 : 0;
            int transferred = 0;
            // Every card counted as moved, by its UniqueID: the game's own move under the clicked card's
            // UniqueID, each coroutine move under its candidate's (read BEFORE the click, since the card
            // is gone from the slot afterwards).
            var movedByUid = new Dictionary<string, int>(StringComparer.Ordinal);
            if (vanillaMoved) BatchRecord.Count(movedByUid, uniqueId);
            // True when the destination changed under the batch: it shows its own notice instead of a count.
            bool stopped = false;
            // Frames in a row with no card moved: no candidate left, or the destination refused the
            // click. Reset ONLY by a move. It used to be reset whenever a candidate was found, which is
            // every frame of a refused tail, so the refused-move stop below (1.7.4) could never fire and a
            // full container was re-clicked every frame until the player changed the destination
            // (measured in plan Wave 0 W0.1: 464 refused clicks in 15 s into a full Hand Basket).
            int consecutiveFailures = 0;
            const int MaxConsecutiveFailures = 3;

            // Every exit below owes the player the one batch sound that stands in for the per-card
            // sounds the prefix muted - including the ones Unity triggers by disposing the
            // coroutine, which no `yield break` would reach.
            try
            {
                var scanCache = new ScanCache();
                object candidate = FindFirstCandidate(scanCache, sourceSlot, uniqueId, filterTag, widen, spoiledMin);
                Logger.LogDebug($"[QT] Coroutine start: count={count}, initial candidate={(candidate == null ? "NULL" : "found")}");

                while (transferred < count)
                {
                    yield return null;

                    // A newer modifier+click owns the batch now. It shows its own notice, so this one
                    // leaves quietly.
                    if (generation != batchGeneration)
                    {
                        Logger.LogDebug($"[QT] Done (superseded by a newer batch): transferred {baseline + transferred} cards total");
                        yield break;
                    }

                    // The game routes each re-clicked card by live state, so a destination that moved
                    // since the first card (popup closed, character window opened, another container
                    // opened) would send the rest of the batch somewhere the player never picked.
                    // An unreadable context never stops a batch (BatchContext.ChangedFrom returns null).
                    var changed = context?.ChangedFrom(context.ReadAgain());
                    if (changed != null)
                    {
                        stopped = true;
                        Plugin.ShowNotification(OverlayText.Get(OverlayText.StoppedKey, OverlayText.StoppedDefault));
                        Logger.LogDebug($"[QT] Done (stopped: the destination changed, {changed}): transferred {baseline + transferred} cards total");
                        yield break;
                    }

                    // Re-validate cached candidate; re-scan only when it leaves the slot it was found
                    // in (the clicked slot, or a sibling slot when the batch widens to siblings).
                    if (candidate == null || !IsValidCandidate(candidate, sourceSlot, uniqueId, filterTag, widen, spoiledMin))
                        candidate = FindFirstCandidate(scanCache, sourceSlot, uniqueId, filterTag, widen, spoiledMin);

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

                    // Snapshot the slot's pile count so a refused move (destination full/incompatible -
                    // vanilla leaves the card in place, e.g. GraphicsManager.MoveCardToSlot on an
                    // over-weight target) can be told apart from an actual transfer. Invoke() not
                    // throwing does NOT mean the card moved.
                    // Measured on the slot the candidate actually sits in: under Drain Matching
                    // Stacks that is a sibling of the clicked slot, whose own pile is what shrinks.
                    // With draining off it is the clicked slot itself, exactly as before.
                    var candidateCard = GetCardFromGraphics(candidate);
                    object candidateSlot = GetCurrentSlot(candidateCard) ?? sourceSlot;
                    int pileCountBefore = GetPileCount(candidateSlot, includePin: true);
                    // A same-item batch only ever clicks the clicked item (IsValidCandidate matched its UniqueID);
                    // a tag batch clicks whatever carries the tag, so its UniqueID is read off the card now.
                    string candidateUid = filterTag == null ? uniqueId : CandidateUniqueId(candidate);

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

                    int pileCountAfter = GetPileCount(candidateSlot, includePin: true);

                    // A negative count means the pile-count API couldn't be resolved via reflection;
                    // fall back to the prior "assume success" behavior rather than stalling forever.
                    bool countUnknown = pileCountBefore < 0 || pileCountAfter < 0;
                    // The pile count alone misreads the last card of a pile (CardLeftSlot), so the card's own slot counts too.
                    bool progressMade = countUnknown || pileCountAfter < pileCountBefore || CardLeftSlot(candidateCard, candidateSlot);

                    if (progressMade)
                    {
                        transferred++;
                        BatchRecord.Count(movedByUid, candidateUid);
                        consecutiveFailures = 0;
                        continue;
                    }

                    consecutiveFailures++;
                    Logger.LogDebug($"[QT] No progress (pile count unchanged at {pileCountAfter}) - destination likely refused the transfer");
                    if (consecutiveFailures >= MaxConsecutiveFailures)
                    {
                        Logger.LogDebug($"[QT] Done (no progress): transferred {baseline + transferred} cards total, stopping after {MaxConsecutiveFailures} clicks in a row moved nothing");
                        yield break;
                    }
                }

                Logger.LogDebug($"[QT] Done: transferred {baseline + transferred} cards total (reached requested count)");
            }
            finally
            {
                runningBatches--;
                PlayBatchCompletionSound();
                if (undo != null)
                {
                    int stepMoved = 0;
                    foreach (var n in movedByUid.Values) stepMoved += n;
                    undo.Moved += stepMoved;
                    undo.Stopped |= stopped;
                    Logger.LogDebug($"[QT] Undo step for uid={uniqueId} moved {stepMoved} of {requested} (stopped={stopped})");
                }
                else
                {
                    FinishBatch(sourceSlot, filterTag, requested, movedByUid, context, generation, stopped);
                }
            }
        }

        // The UniqueID of a candidate's card, or null when it cannot be read (counted under "?" in the record,
        // with one warning: the headline number does not depend on it).
        static string CandidateUniqueId(object candidate)
        {
            var model = GetMemberValue(GetCardFromGraphics(candidate), "CardModel");
            var uid = model == null ? null : GetMemberValue(model, "UniqueID")?.ToString();
            if (string.IsNullOrEmpty(uid)) WarnFeatureOnce("Moved report", "CardData.UniqueID of a tag-batch card");
            return uid;
        }

        // Every batch exit lands here (the coroutine's finally). Stores the record, then tells the player how
        // many cards moved when that differs from what the click asked for: always for an All batch, which
        // has no count to compare against, and only on a shortfall for a numeric one, so a batch that moved
        // everything keeps today's single "Quick Transfer: N" notice. A batch whose destination changed shows
        // its own notice, and one a newer click replaced leaves quietly (that click shows its own), so neither
        // reports a count. Wrapped so a failed report can never cost the player the batch sound or the exit.
        static void FinishBatch(object sourceSlot, string filterTag, int requested, Dictionary<string, int> movedByUid,
                                BatchContext context, int generation, bool stopped)
        {
            try
            {
                string slotType = GetMemberValue(sourceSlot, "SlotType")?.ToString();
                var record = new BatchRecord(generation, requested, slotType, context, filterTag, movedByUid);
                BatchRecord.Store(record);

                bool superseded = generation != batchGeneration;
                Logger.LogDebug($"[QT] Batch moved {record.Moved} of {(record.RequestedAll ? "all" : requested.ToString())} (slot={slotType ?? "?"}, tag={filterTag ?? "none"}, stopped={stopped}, replaced={superseded})");
                if (stopped || superseded) return;

                if (record.RequestedAll)
                    Plugin.ShowNotification(OverlayText.Format(OverlayText.MovedAllKey, OverlayText.MovedAllDefault, record.Moved));
                else if (record.Moved < requested)
                    Plugin.ShowNotification(OverlayText.Format(OverlayText.MovedKey, OverlayText.MovedDefault, record.Moved, requested));
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"[QT] Batch report failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // The Half and All-but-one presets leave Plugin.GetEffectiveTransferAmount as a sentinel because
        // the overlay has no slot; here, in the click prefix, the clicked slot is in hand. This runs BEFORE
        // vanilla moves the first card, so a stack of 8 reads 8 and yields 4 (Half) or 7 (All but one), with
        // vanilla's own move being the first of those (Half: 7 -> 4, 1 -> 1; All but one: 1 -> 0, which the
        // prefix turns into a skipped click). The pile excludes a pinned placeholder (includePin false): it
        // is a card in the slot's pile, so counting it turned 8 real cards plus a pin into a Half of 5.
        static int ResolveTransferCount(int requested, object slot)
        {
            if (requested != Plugin.HalfSentinel && requested != Plugin.AllButOneSentinel) return requested;
            string preset = requested == Plugin.HalfSentinel ? "Half" : "All but one";

            int pile = GetPileCount(slot, includePin: false);
            if (pile <= 0)
            {
                // Pile count unresolvable: store a concrete preset rather than let the sentinel
                // reach the count accounting downstream.
                Logger.LogDebug($"[QT] {preset} preset: pile count unavailable ({pile}); using the Ctrl preset ({Plugin.CtrlPresetAmount.Value}) instead");
                return Plugin.CtrlPresetAmount.Value;
            }
            return requested == Plugin.HalfSentinel ? Mathf.CeilToInt(pile / 2f) : pile - 1;
        }

        // Reads DynamicLayoutSlot.CardPileCount(bool _IncludePin) via cached reflection. Returns -1 if
        // the method can't be resolved (caller treats -1 as "can't tell — assume success"). The Half
        // preset passes false so a pinned placeholder is not counted as a card; the coroutine's progress
        // compare passes true, where the placeholder is the same on both sides of a click.
        static int GetPileCount(object slot, bool includePin)
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

                var result = cardPileCountMethod.Invoke(slot, new object[] { includePin });
                return result is int count ? count : -1;
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"[QT] GetPileCount reflection failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
                return -1;
            }
        }

        // A slot holds one pile of one item, so a tag-filtered batch ("every food card here") only
        // means anything across the container's other slots: it always widens, as Drain Matching
        // Stacks does for the same item. Read once, at arming (savedWiden), with CannotPile beside it.
        static bool WidensToSiblings(string filterTag) => filterTag != null || Plugin.DrainMatchingStacks.Value;

        // True when the clicked card cannot share its slot with another card (plan Feature 7), so each card of the
        // item sits in a slot of its own and a batch confined to the clicked slot would move only the clicked card.
        // Either side decides: the slot keeps one card per slot (DynamicLayoutSlot.CanHostPile false, set per inventory
        // slot by InspectionPopup for a legacy or cooking inventory such as the Water-Driven Workshop), or the card
        // never piles (CardData.CanPile false: DoesNotPile, a card with its own inventory, a blueprint). Both are the
        // members DynamicLayoutSlot.PileCompatible reads to answer CannotReceive. An unreadable member counts as
        // "can pile" (today's behaviour: no widening) and warns once.
        static bool CannotPile(object slot, object cardModel)
        {
            const string Feature = "Widen For Non-Piling Cards";
            if (!Reflect.TryGetMember(slot, "CanHostPile", out var hostObj) || !(hostObj is bool canHost))
                WarnFeatureOnce(Feature, "DynamicLayoutSlot.CanHostPile");
            else if (!canHost)
                return true;
            if (!Reflect.TryGetMember(cardModel, "CanPile", out var pileObj) || !(pileObj is bool canPile))
            {
                WarnFeatureOnce(Feature, "CardData.CanPile");
                return false;
            }
            return !canPile;
        }

        // The one scene scan a batch is allowed (plan Feature 17, measured 2026-10-10: FindObjectsOfType cost a mean of
        // 20 ms with 16 to 28 card graphics on screen, 14 ms at best; the validation loop over them cost 0.08 ms). It
        // holds the sibling slots' graphics, found once per batch and refreshed only when no valid sibling is left in it.
        sealed class ScanCache
        {
            public UnityEngine.Object[] Graphics;
        }

        private static MethodInfo copyCardPileMethod;
        private static Type copyCardPileMethodOwner;

        // The active card graphics of one slot's own pile, read through DynamicLayoutSlot.CopyCardPile instead of
        // a scene scan. Active only, because FindObjectsOfType returns only active objects and the scan this
        // replaces therefore saw only the visible card of a pile. Null (never an empty list) when the pile cannot
        // be read, so the caller falls back to the scene scan; one warning.
        static List<object> SlotGraphics(object slot)
        {
            if (slot == null) return null;
            try
            {
                var slotType = slot.GetType();
                if (copyCardPileMethod == null || copyCardPileMethodOwner != slotType)
                {
                    copyCardPileMethod = AccessTools.Method(slotType, "CopyCardPile");
                    copyCardPileMethodOwner = slotType;
                }
                if (copyCardPileMethod == null) { WarnFeatureOnce("Fast candidate scan", "DynamicLayoutSlot.CopyCardPile"); return null; }
                var parameters = copyCardPileMethod.GetParameters();
                if (parameters.Length != 1) { WarnFeatureOnce("Fast candidate scan", "DynamicLayoutSlot.CopyCardPile(List)"); return null; }
                var pile = Activator.CreateInstance(parameters[0].ParameterType) as IList;
                if (pile == null) { WarnFeatureOnce("Fast candidate scan", "DynamicLayoutSlot.CopyCardPile(List)"); return null; }
                copyCardPileMethod.Invoke(slot, new object[] { pile });

                var graphics = new List<object>(pile.Count);
                foreach (var card in pile)
                {
                    if (card == null || (card is UnityEngine.Object deadCard && deadCard == null)) continue;
                    var visuals = GetMemberValue(card, "CardVisuals");
                    if (visuals is Component c && c != null && c.gameObject.activeInHierarchy) graphics.Add(visuals);
                }
                return graphics;
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"[QT] SlotGraphics reflection failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
                WarnFeatureOnce("Fast candidate scan", "DynamicLayoutSlot.CopyCardPile call");
                return null;
            }
        }

        // The clicked slot is always drained first. Only once it holds no candidate, and only when
        // the batch widens (Drain Matching Stacks, a tag filter, or a card that cannot pile), does the scan
        // move on to that slot's siblings in the same container. Prioritize Most Spoiled replaces that order
        // with "closest to spoiling first" whenever there are siblings to choose between.
        // The clicked slot is read from its own pile (no scene scan). The siblings come from the batch's one scene
        // scan, reused until it holds no valid candidate: every card of a pile but the visible one is inactive and
        // so absent from it, which is why it is refreshed when exhausted rather than trusted for the whole batch.
        static object FindFirstCandidate(ScanCache cache, object sourceSlot, string uniqueId, string filterTag, bool widen, float spoiledMin)
        {
            var own = SlotGraphics(sourceSlot);
            if (own == null) return FindFirstCandidateBySceneScan(sourceSlot, uniqueId, filterTag, widen, spoiledMin);

            if (widen && Plugin.PrioritizeMostSpoiled.Value)
            {
                if (cache.Graphics != null)
                {
                    var cached = MostSpoiledCandidate(Combined(own, cache.Graphics), sourceSlot, uniqueId, filterTag, spoiledMin);
                    if (cached != null) return cached;
                }
                cache.Graphics = UnityEngine.Object.FindObjectsOfType(cardGraphicsType);
                return MostSpoiledCandidate(Combined(own, cache.Graphics), sourceSlot, uniqueId, filterTag, spoiledMin);
            }
            foreach (var g in own)
            {
                if (IsValidCandidate(g, sourceSlot, uniqueId, filterTag, allowSiblingSlots: false, spoiledMin))
                    return g;
            }
            if (!widen) return null;
            if (cache.Graphics != null)
            {
                foreach (var g in cache.Graphics)
                {
                    if (IsValidCandidate(g, sourceSlot, uniqueId, filterTag, allowSiblingSlots: true, spoiledMin))
                        return g;
                }
            }
            cache.Graphics = UnityEngine.Object.FindObjectsOfType(cardGraphicsType);
            if (cache.Graphics == null) return null;
            foreach (var g in cache.Graphics)
            {
                if (IsValidCandidate(g, sourceSlot, uniqueId, filterTag, allowSiblingSlots: true, spoiledMin))
                    return g;
            }
            return null;
        }

        static List<object> Combined(List<object> own, UnityEngine.Object[] scene)
        {
            var all = new List<object>(own.Count + (scene == null ? 0 : scene.Length));
            all.AddRange(own);
            if (scene != null) all.AddRange(scene);
            return all;
        }

        // The scan before plan Feature 17: every active card graphic in the scene, tested in scene order. Kept as the
        // fallback for a game build whose slot pile cannot be read (SlotGraphics returned null).
        static object FindFirstCandidateBySceneScan(object sourceSlot, string uniqueId, string filterTag, bool widen, float spoiledMin)
        {
            var allGraphics = UnityEngine.Object.FindObjectsOfType(cardGraphicsType);
            if (allGraphics == null) return null;
            if (widen && Plugin.PrioritizeMostSpoiled.Value)
                return MostSpoiledCandidate(allGraphics, sourceSlot, uniqueId, filterTag, spoiledMin);
            foreach (var g in allGraphics)
            {
                if (IsValidCandidate(g, sourceSlot, uniqueId, filterTag, allowSiblingSlots: false, spoiledMin))
                    return g;
            }
            if (!widen) return null;
            foreach (var g in allGraphics)
            {
                if (IsValidCandidate(g, sourceSlot, uniqueId, filterTag, allowSiblingSlots: true, spoiledMin))
                    return g;
            }
            return null;
        }

        // Every candidate IsValidCandidate already accepts (the guards run first, untouched), ranked
        // by how far each has gone toward spoiling; a tie goes to the clicked slot. Anything with no
        // rank (it cannot spoil) waits until nothing ranked is left,
        // then goes in the usual order: clicked slot first, then siblings in scene order. So with
        // nothing perishable in play the pick is exactly the one the plain scan above would make.
        static object MostSpoiledCandidate(IEnumerable<object> allGraphics, object sourceSlot, string uniqueId, string filterTag, float spoiledMin)
        {
            object usual = null;          // what the plain scan would return
            bool usualInClickedSlot = false;
            object best = null;
            float bestUrgency = float.NegativeInfinity;
            bool bestInClickedSlot = false;

            foreach (var g in allGraphics)
            {
                if (!IsValidCandidate(g, sourceSlot, uniqueId, filterTag, allowSiblingSlots: true, spoiledMin)) continue;
                var card = GetCardFromGraphics(g);
                bool inClickedSlot = ReferenceEquals(GetCurrentSlot(card), sourceSlot);
                if (usual == null || (inClickedSlot && !usualInClickedSlot))
                {
                    usual = g;
                    usualInClickedSlot = inClickedSlot;
                }

                var urgency = SpoilageUrgency(card, "Prioritize Most Spoiled");
                if (urgency == null) continue;
                bool better = urgency.Value > bestUrgency
                    || (urgency.Value == bestUrgency && inClickedSlot && !bestInClickedSlot);
                if (better)
                {
                    best = g;
                    bestUrgency = urgency.Value;
                    bestInClickedSlot = inClickedSlot;
                }
            }
            return best ?? usual;
        }

        // How far a card has gone toward spoiling, 0..1, or null when it cannot spoil. Vanilla
        // Spoilage counts UP and rots on OnFull (MeatRaw: FloatValue 0, MaxValue 240,
        // RatePerDaytimePoint +1, HasActionOnFull), but a mod may count down to an OnZero instead,
        // so the sign of the card's AUTHORED rate picks the end to measure against. Never the live
        // CurrentSpoilageRate: effects move it arbitrarily far, and inside a chest it was measured at
        // -99998.5 for a Roasted Acorn whose authored rate is +1 (T2.332, 2026-09-26), which read as
        // "counts down" and ranked the freshest card first. A card that is paused or preserved where
        // it sits is still exactly as far gone, so it ranks by that. CurrentSpoilagePercent reads 1
        // for a card with no active SpoilageTime at all (.decomp/InGameCardBase.cs), which is why
        // Active is checked first rather than trusting the percent.
        // Feature names the setting a failed read turns off, in its one warning.
        static float? SpoilageUrgency(object card, string feature)
        {
            var model = GetMemberValue(card, "CardModel");
            if (model == null) return null;
            if (!Reflect.TryGetMember(model, "SpoilageTime", out var spoilage)) { WarnFeatureOnce(feature, "CardData.SpoilageTime"); return null; }
            if (spoilage == null) return null;
            if (!Reflect.TryGetMember(spoilage, "Active", out var activeObj) || !(activeObj is bool active)) { WarnFeatureOnce(feature, "DurabilityStat.Active"); return null; }
            if (!active) return null;
            if (!Reflect.TryGetMember(spoilage, "RatePerDaytimePoint", out var rateObj) || !(rateObj is float authoredRate)) { WarnFeatureOnce(feature, "DurabilityStat.RatePerDaytimePoint"); return null; }
            if (authoredRate == 0f) return null;
            if (!Reflect.TryGetMember(card, "CurrentSpoilagePercent", out var pctObj) || !(pctObj is float pct)) { WarnFeatureOnce(feature, "InGameCardBase.CurrentSpoilagePercent"); return null; }
            return authoredRate > 0f ? pct : 1f - pct;
        }

        // The first configured Tag Filter Tag the clicked card carries, or null for a plain
        // same-item batch (no tag listed, or none of them on this card). tagObject is that tag's
        // CardTag (null whenever the result is null, or when the card's tag list no longer yields it).
        static string ResolveFilterTag(object cardModel, out object tagObject)
        {
            tagObject = null;
            var wanted = Plugin.TagFilterTagList();
            if (wanted.Count == 0) return null;
            if (!Reflect.TryGetMember(cardModel, "CardTags", out var tagsObj)) { WarnFeatureOnce("Tag filter", "CardData.CardTags"); return null; }
            var carried = TagNames(tagsObj);
            foreach (var tag in wanted)
            {
                if (!carried.Contains(tag)) continue;
                tagObject = FindTagObject(tagsObj, tag);
                return tag;
            }
            return null;
        }

        // The CardTag whose asset name is <paramref name="name"/>, from a CardData.CardTags array.
        static object FindTagObject(object tagsObj, string name)
        {
            if (tagsObj is IEnumerable tags)
                foreach (var t in tags)
                    if (t is UnityEngine.Object o && o != null && string.Equals(o.name, name, StringComparison.Ordinal)) return o;
            return null;
        }

        // The start notice (plan Features 4 and 5): "Quick Transfer: 10", or for a Tag Filter batch
        // "Quick Transfer: 10 (Human Food)" so the player can see which tag the click picked. A batch
        // without a tag is word for word what it was.
        static string StartNotice(int totalCount, string filterTag, object filterTagObject)
        {
            if (filterTag == null) return Plugin.AmountText(totalCount);
            return OverlayText.Format(OverlayText.AmountTagKey, OverlayText.AmountTagDefault,
                Plugin.AmountLabel(totalCount), TagDisplayName(filterTag, filterTagObject));
        }

        // What a tag is called on screen: its CardTag.InGameName, read through ToString() so the game's
        // own rules pick the text (the cached text first, then the language's row, then DefaultText).
        // That is NOT the asset name minus "tag_": tag_Plateable displays "Edible" (plan W0.5). A tag the
        // framework fabricates (tag_Herb) carries the readable English "Herb" as its DefaultText under a
        // key no CSV row names, so it reads English in Chinese mode too, which is not a localization
        // fault of this mod. The asset name stands in only when the name is empty or cannot be read; a
        // member that cannot be read warns once.
        static string TagDisplayName(string assetName, object tag)
        {
            if (tag == null) return assetName;
            try
            {
                if (!Reflect.TryGetMember(tag, "InGameName", out var nameObj)) { WarnFeatureOnce("Tag name", "CardTag.InGameName"); return assetName; }
                var text = nameObj?.ToString();
                return string.IsNullOrWhiteSpace(text) ? assetName : text;
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"[QT] Tag name unreadable for {assetName}, using the asset name: {ex.InnerException?.ToString() ?? ex.ToString()}");
                return assetName;
            }
        }

        // CardData.CardTags is a CardTag[] of ScriptableObjects; a tag's asset name is its tag
        // string (tag_HumanFood). Null or empty means the card carries no tags.
        static HashSet<string> TagNames(object tagsObj)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (tagsObj is IEnumerable tags)
                foreach (var t in tags)
                    if (t is UnityEngine.Object o && o != null) names.Add(o.name);
            return names;
        }

        // spoiledMin is the Spoiled Only floor (plan Feature 8), -1 when off.
        static bool IsValidCandidate(object graphics, object sourceSlot, string uniqueId, string filterTag, bool allowSiblingSlots, float spoiledMin)
        {
            if (graphics == null) return false;
            var card = GetCardFromGraphics(graphics);
            if (card == null) return false;
            // A destroyed UnityEngine.Object is NOT C#-null under this object-typed compare: GetMemberValue's
            // own `value != null` let one through. A destroyed card is never a candidate, and its IsPinned
            // (read below) would be a stale answer.
            if (card is UnityEngine.Object destroyedCard && destroyedCard == null) return false;
            var cardSlot = GetCurrentSlot(card);
            if (cardSlot == null) return false;
            if (!ReferenceEquals(cardSlot, sourceSlot) && !(allowSiblingSlots && IsSiblingSlot(cardSlot, sourceSlot))) return false;
            var cardModel = GetMemberValue(card, "CardModel");
            if (cardModel == null) return false;
            var cardType = GetMemberValue(cardModel, "CardType");
            if (cardType != null && Convert.ToInt32(cardType) == 8) return false;
            var cannotTransfer = GetMemberValue(cardModel, "CannotBeTransferred");
            if (cannotTransfer is bool ct && ct) return false;
            // A pinned card is the faint placeholder a pinned slot keeps: a real card of the pile with its
            // own CardGraphics, which vanilla's click ignores (OnPointerClick). Never a candidate.
            if (IsPinnedCard(card)) return false;
            // A card Utility Belt has locked is never moved by a batch. Absent Utility Belt this is always false.
            if (UtilityBeltLockBridge.IsLocked(card)) return false;
            // The guards above run for a tag match exactly as for a same-item match.
            bool matches;
            if (filterTag != null)
                matches = Reflect.TryGetMember(cardModel, "CardTags", out var tagsObj) && TagNames(tagsObj).Contains(filterTag);
            else
                matches = GetMemberValue(cardModel, "UniqueID")?.ToString() == uniqueId;
            // Spoiled Only judges the card this candidate's click moves, after every guard and the match.
            return matches && (spoiledMin < 0f || IsSpoiledEnough(card, spoiledMin));
        }

        // True when the card is at least spoiledMin (0..1) of the way to spoiling (plan Feature 8). A card that cannot
        // spoil, or whose spoilage cannot be read (one warning), never qualifies, so a Spoiled Only batch errs toward
        // moving nothing.
        static bool IsSpoiledEnough(object card, float spoiledMin)
        {
            var urgency = SpoilageUrgency(card, "Spoiled Only");
            return urgency != null && urgency.Value >= spoiledMin;
        }

        // True when the card is a pinned placeholder (InGameCardBase.IsPinned, a public getter with a
        // private setter). A read that fails or returns something other than a bool leaves the answer
        // false, today's behaviour, and says so once: a rename in a game update must not silently turn
        // every transfer off, nor silently keep placeholders in the pile.
        static bool IsPinnedCard(object card)
        {
            if (!Reflect.TryGetMember(card, "IsPinned", out var pinnedObj) || !(pinnedObj is bool pinned))
            {
                WarnFeatureOnce("Skip pinned cards", "InGameCardBase.IsPinned");
                return false;
            }
            return pinned;
        }

        // True when the card's CardType is anything but Item, compared by the enum member's NAME so no
        // game enum is referenced. Vanilla's OnPointerClick only calls SwapCard for an Item, so a click on
        // a placed structure, a location or any other type moves nothing and must not start a batch. An
        // unreadable CardType leaves the answer false (arm as before) with one warning.
        static bool IsNonItemCard(object cardModel)
        {
            if (!Reflect.TryGetMember(cardModel, "CardType", out var typeObj) || typeObj == null)
            {
                WarnFeatureOnce("Skip non-item clicks", "CardData.CardType");
                return false;
            }
            return !string.Equals(typeObj.ToString(), "Item", StringComparison.Ordinal);
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
