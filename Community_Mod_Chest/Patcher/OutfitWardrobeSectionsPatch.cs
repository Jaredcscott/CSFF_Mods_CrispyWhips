using System;
using System.Collections.Generic;
using HarmonyLib;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Labels the Outfit Wardrobe's three 18-slot blocks ("Outfit 1/2/3") directly on the
    /// inventory grid so a player can see where to drop items for each outfit. Before this,
    /// the flat 54-slot grid gave no visual cue where one outfit's slots ended and the next
    /// began — the page-arrow scroll width (CardLine.MoveToSection, viewport-pixel based)
    /// doesn't land on the 18-slot boundaries either, so paging through never lined up with
    /// an outfit edge.
    ///
    /// Reuses the same per-slot TitleText mechanism the engine already uses for blueprint
    /// ingredient labels (InspectionPopup.SetupSlotsWithBlueprintRequirements) — each
    /// DynamicLayoutSlot owns its own SlotTitle text object (CardSlot.cs reads
    /// ParentSlotData.TitleText per-instance every LateUpdate), so this is NOT the
    /// shared-UI-object mutation hazard documented for InspectionPopup.DescriptionText
    /// (CLAUDE.md § Harmony Patching Pitfalls) — nothing here is reused across cards.
    /// DynamicLayoutSlot.ClearSlot (called every SetupInventory pass) does not touch
    /// TitleText, so the label sticks across drag/drop refreshes without needing to be
    /// re-applied on every call — this postfix just keeps it correct regardless.
    /// </summary>
    internal static class OutfitWardrobeSectionsPatch
    {
        private const string WardrobeUid = "cmcOutfitWardrobe";
        private const int OutfitCount = 3;
        private const int SlotsPerOutfit = 18;

        private static bool _initialized;

        public static void ApplyPatch(Harmony harmony)
        {
            if (_initialized) return;

            var method = AccessTools.Method(typeof(InspectionPopup), "SetupInventory");
            if (method == null)
            {
                Plugin.Logger.LogWarning("[OutfitWardrobeSectionsPatch] InspectionPopup.SetupInventory not found; section labels not applied.");
                return;
            }

            harmony.Patch(method, postfix: new HarmonyMethod(typeof(OutfitWardrobeSectionsPatch), nameof(SetupInventory_Postfix)));
            _initialized = true;
            Plugin.Logger.LogDebug("[OutfitWardrobeSectionsPatch] initialized.");
        }

        private static void SetupInventory_Postfix(InspectionPopup __instance, InGameCardBase _Card)
        {
            try
            {
                if (_Card == null || _Card.CardModel == null || _Card.CardModel.UniqueID != WardrobeUid) return;

                var line = __instance.InventorySlotsLine;
                if (line?.Slots == null) return;

                for (int i = 0; i < OutfitCount; i++)
                {
                    int index = i * SlotsPerOutfit;
                    if (index < line.Slots.Count)
                        line.Slots[index].SetTitleText(OutfitLabel(i + 1));
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[OutfitWardrobeSectionsPatch] SetupInventory postfix failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        /// <summary>
        /// Reads the current-language text for "CMC_OutfitWardrobe_Section{N}" the same way
        /// the game's own LocalizationManager resolves any other LocalizedString at runtime,
        /// falling back to the English default if the key isn't found (e.g. a stale deploy).
        /// </summary>
        private static string OutfitLabel(int outfitNumber)
        {
            string key = $"CMC_OutfitWardrobe_Section{outfitNumber}";
            string fallback = $"Outfit {outfitNumber}";
            try
            {
                var locMgrType = AccessTools.TypeByName("LocalizationManager");
                var field = locMgrType != null ? AccessTools.Field(locMgrType, "CurrentTexts") : null;
                if (field?.GetValue(null) is Dictionary<string, string> texts &&
                    texts.TryGetValue(key, out var text) && !string.IsNullOrEmpty(text))
                {
                    return text;
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogDebug($"[OutfitWardrobeSectionsPatch] OutfitLabel lookup failed for '{key}': {ex.Message}");
            }
            return fallback;
        }
    }
}
