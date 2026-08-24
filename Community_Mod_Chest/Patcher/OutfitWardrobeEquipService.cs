using System;
using System.Collections.Generic;
using CSFFModFramework.Api;
using HarmonyLib;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Per-outfit equip/dequip for the Outfit Wardrobe (cmcOutfitWardrobe) — the single merged
    /// successor to the old Clothes Rack / Coat Rack / Wardrobe / Weapon Rack &amp; Armor Stand.
    /// Its 54 InventorySlots are three fixed 18-slot blocks (<see cref="SlotsPerOutfit"/>, one
    /// per non-wound EquipmentTag), one per outfit. "Equip Outfit N" moves every card sitting in
    /// that block onto the character if it fits an empty equipment slot; "Unequip Outfit N" moves
    /// every currently equipped card back into that block specifically (not wherever the whole
    /// inventory has room — see below).
    ///
    /// Rewritten 2026-08-17 from the old ClothesRackEquipService, which had two confirmed latent
    /// bugs: (1) it read the rack's contents via the generic CardUtil.GetInventoryList helper,
    /// which resolves InGameCardBase.CardsInInventory — a List&lt;InventorySlot&gt;, not a flat
    /// card list — so every entry was actually an InventorySlot wrapper; (2) its IsAlive() null
    /// check assumed every reflected object was a UnityEngine.Object, but InventorySlot AND
    /// DynamicLayoutSlot are both plain C# classes, not Unity objects, so that check was always
    /// false for them. Together these meant "Dress" always iterated zero items. This rewrite uses
    /// direct compile-time types against the mod's referenced Assembly-CSharp-nstrip.dll (already
    /// used by CopperChestPatch/CardUtil elsewhere) instead of reflection, which also removes the
    /// entire class of "reflected member not found" failure points the old class carried.
    ///
    /// EQUIP path (rack -> body): flatten the outfit's slice of CardsInInventory into its actual
    /// cards (InventorySlot.AllCards), then for each one CharacterScreen.FindSlotFor finds a live
    /// empty equipment DynamicLayoutSlot for its EquipmentTags. DynamicLayoutSlot.AssignCard does
    /// the physical attach (and equip bookkeeping internally), but its private AddCard does NOT
    /// detach the card from the rack's own inventory list — RemoveCardFromInventory +
    /// SetCurrentContainer(null) do that first, mirroring DropInInventory's own detach step.
    ///
    /// UNEQUIP path (body -> outfit N specifically): InGameCardBase.DropInInventory is the normal
    /// "move into this container" primitive, but its target-slot search (GetIndexForInventory)
    /// ignores the _From parameter for a normal (non-legacy) inventory and always scans from index
    /// 0 — it cannot be scoped to one outfit's 18-slot block. TryPlaceInOutfit below finds a free
    /// slot within the target block itself and replicates DropInInventory's placement steps
    /// (detach from equipment slot, detach from any container, AddCardToInventory at that specific
    /// index) so "Unequip Outfit 2" cannot land an item in Outfit 1 or 3's block.
    /// </summary>
    internal static class OutfitWardrobeEquipService
    {
        private const string WardrobeUid = "cmcOutfitWardrobe";
        private const int OutfitCount = 3;
        private const int SlotsPerOutfit = 18;

        private static bool _initialized;

        public static void Initialize(Harmony harmony)
        {
            if (_initialized) return;
            _initialized = true;

            for (int i = 0; i < OutfitCount; i++)
            {
                int outfitIndex = i; // per-iteration capture — a `for` loop variable is NOT
                                      // captured fresh per closure the way `foreach` is.
                int outfitNumber = i + 1;
                string equipName = $"Equip Outfit {outfitNumber}";
                string unequipName = $"Unequip Outfit {outfitNumber}";

                ActionRouter.Register(new ActionHandler
                {
                    Name = $"OutfitWardrobeEquip{outfitNumber}",
                    CardPredicate = ctx => ctx.CardUid == WardrobeUid && ctx.ActionName == equipName,
                    Timing = ActionTiming.AfterWrapped,
                    After = ctx => Equip(ctx.Card as InGameCardBase, outfitIndex, outfitNumber),
                });

                ActionRouter.Register(new ActionHandler
                {
                    Name = $"OutfitWardrobeUnequip{outfitNumber}",
                    CardPredicate = ctx => ctx.CardUid == WardrobeUid && ctx.ActionName == unequipName,
                    Timing = ActionTiming.AfterWrapped,
                    After = ctx => Unequip(ctx.Card as InGameCardBase, outfitIndex, outfitNumber),
                });
            }

            Plugin.Logger.LogDebug("[OutfitWardrobeEquipService] initialized.");
        }

        // ── Equip (outfit block -> body) ────────────────────────────────────────

        private static void Equip(InGameCardBase rack, int outfitIndex, int outfitNumber)
        {
            try
            {
                if (rack == null) return;

                ScrollPopupToOutfit(rack, outfitIndex);

                var characterScreen = MBSingleton<GraphicsManager>.Instance
                    ? MBSingleton<GraphicsManager>.Instance.CharacterWindow
                    : null;
                if (!characterScreen)
                {
                    Plugin.Logger.LogWarning("[OutfitWardrobeEquipService] Equip: could not resolve a live CharacterScreen instance.");
                    return;
                }

                var candidates = CollectOutfitCards(rack, outfitIndex);
                if (candidates.Count == 0)
                {
                    Plugin.Logger.LogDebug($"[OutfitWardrobeEquipService] Equip Outfit {outfitNumber}: block empty.");
                    return;
                }

                int equipped = 0;
                foreach (var item in candidates)
                {
                    var cardModel = item.CardModel;
                    if (!cardModel) continue;
                    if (cardModel.CannotBeTransferred)
                    {
                        Plugin.Logger.LogDebug("[OutfitWardrobeEquipService] Equip: skipped a CannotBeTransferred card.");
                        continue;
                    }

                    var slot = characterScreen.FindSlotFor(cardModel, false, -1);
                    if (!slot) continue; // not equipment, no free slot, or already at MaxEquipped

                    rack.RemoveCardFromInventory(item);
                    item.SetCurrentContainer(null);
                    slot.AssignCard(item, true);
                    equipped++;
                }

                if (equipped > 0)
                    Plugin.Logger.LogInfo($"[OutfitWardrobeEquipService] Equip Outfit {outfitNumber}: equipped {equipped} item(s).");
                else
                    Plugin.Logger.LogDebug($"[OutfitWardrobeEquipService] Equip Outfit {outfitNumber}: nothing could be equipped (no matching slot, or already worn).");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[OutfitWardrobeEquipService] Equip Outfit {outfitNumber} failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // ── Unequip (body -> outfit block specifically) ─────────────────────────

        private static void Unequip(InGameCardBase rack, int outfitIndex, int outfitNumber)
        {
            try
            {
                if (rack == null) return;

                ScrollPopupToOutfit(rack, outfitIndex);

                var characterScreen = MBSingleton<GraphicsManager>.Instance
                    ? MBSingleton<GraphicsManager>.Instance.CharacterWindow
                    : null;
                if (!characterScreen)
                {
                    Plugin.Logger.LogWarning("[OutfitWardrobeEquipService] Unequip: could not resolve a live CharacterScreen instance.");
                    return;
                }

                var slotsLine = characterScreen.EquipmentSlotsLine;
                if (slotsLine?.Slots == null || slotsLine.Slots.Count == 0)
                {
                    Plugin.Logger.LogDebug("[OutfitWardrobeEquipService] Unequip: no equipment slots readable.");
                    return;
                }

                var equippedItems = new List<InGameCardBase>();
                foreach (var slot in slotsLine.Slots)
                    if (slot != null && slot.AssignedCard) equippedItems.Add(slot.AssignedCard);

                int from = outfitIndex * SlotsPerOutfit;
                int to = from + SlotsPerOutfit;

                var gm = MBSingleton<GameManager>.Instance;
                int stored = 0, skippedFull = 0;
                foreach (var item in equippedItems)
                {
                    var cardModel = item.CardModel;
                    if (cardModel && cardModel.CannotBeTransferred)
                    {
                        Plugin.Logger.LogDebug("[OutfitWardrobeEquipService] Unequip: skipped a CannotBeTransferred card.");
                        continue;
                    }

                    if (!TryPlaceInOutfit(rack, item, from, to))
                    {
                        skippedFull++;
                        continue;
                    }

                    characterScreen.UnequipCard(item, true);
                    if (gm) gm.UpdateCardEquippedPassiveEffects();
                    stored++;
                }

                if (skippedFull > 0)
                    Plugin.Logger.LogInfo($"[OutfitWardrobeEquipService] Unequip Outfit {outfitNumber}: block ran out of room — {skippedFull} item(s) left equipped.");
                if (stored > 0)
                    Plugin.Logger.LogInfo($"[OutfitWardrobeEquipService] Unequip Outfit {outfitNumber}: stored {stored} item(s).");
                else if (skippedFull == 0)
                    Plugin.Logger.LogDebug($"[OutfitWardrobeEquipService] Unequip Outfit {outfitNumber}: nothing was equipped.");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[OutfitWardrobeEquipService] Unequip Outfit {outfitNumber} failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // ── Shared helpers ───────────────────────────────────────────────────────

        /// <summary>
        /// If the wardrobe's inspection popup is currently open, scrolls its inventory grid
        /// straight to outfit N's block — clicking "Equip/Unequip Outfit N" doubles as a jump
        /// button to that section (paired with the "Outfit N" labels from
        /// OutfitWardrobeSectionsPatch), even when the click itself is a no-op (e.g. the block
        /// is empty). A no-op if the popup isn't open or is showing a different card.
        /// </summary>
        private static void ScrollPopupToOutfit(InGameCardBase rack, int outfitIndex)
        {
            try
            {
                var gm = MBSingleton<GraphicsManager>.Instance;
                var popup = gm ? gm.CurrentInspectionPopup : null;
                if (popup == null || popup.CurrentCard != rack) return;

                var line = popup.InventorySlotsLine;
                if (line?.Slots == null) return;

                int from = outfitIndex * SlotsPerOutfit;
                if (from >= line.Slots.Count) return;

                line.MoveViewTo(line.Slots[from], true, false);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogDebug($"[OutfitWardrobeEquipService] ScrollPopupToOutfit failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Flattens the [outfitIndex * SlotsPerOutfit, +SlotsPerOutfit) slice of the rack's
        /// CardsInInventory (a List&lt;InventorySlot&gt;, each holding its own AllCards list) into
        /// the actual live cards sitting in that block.
        /// </summary>
        private static List<InGameCardBase> CollectOutfitCards(InGameCardBase rack, int outfitIndex)
        {
            var result = new List<InGameCardBase>();
            var slots = rack.CardsInInventory;
            if (slots == null) return result;

            int from = outfitIndex * SlotsPerOutfit;
            int to = Math.Min(from + SlotsPerOutfit, slots.Count);
            for (int i = from; i < to; i++)
            {
                var slot = slots[i];
                if (slot?.AllCards == null) continue;
                foreach (var card in slot.AllCards)
                    if (card) result.Add(card);
            }
            return result;
        }

        /// <summary>
        /// Finds a free slot within [from, to) of the rack's own CardsInInventory and moves
        /// <paramref name="item"/> into it, replicating InGameCardBase.DropInInventory's placement
        /// steps (detach from its current equipment slot / container, AddCardToInventory at the
        /// chosen index) with a manually-scoped index instead of DropInInventory's own
        /// always-from-0 search. Returns false (item left untouched) if the block has no room.
        /// </summary>
        private static bool TryPlaceInOutfit(InGameCardBase rack, InGameCardBase item, int from, int to)
        {
            var cardModel = item.CardModel;
            if (!cardModel || !rack.CanReceiveInInventory(cardModel, item.ContainedLiquidModel))
                return false;

            var slots = rack.CardsInInventory;
            if (slots == null) return false;

            int hi = Math.Min(to, slots.Count);
            int index = -1;
            for (int i = from; i < hi; i++)
            {
                if (slots[i] != null && slots[i].IsFree) { index = i; break; }
            }
            if (index < 0) return false;

            if (item.CurrentSlot) item.CurrentSlot.RemoveSpecificCard(item, true);
            if (item.CurrentContainer) item.CurrentContainer.RemoveCardFromInventory(item);
            item.SetCurrentContainer(rack);
            item.SetSlot(null, true);
            item.CurrentSlotInfo = new SlotInfo(SlotsTypes.Inventory, index);
            rack.AddCardToInventory(item, index);
            rack.Pulse(0f);
            return true;
        }
    }
}
