using System;

namespace ForestLore
{
    /// <summary>
    /// Host-free rules for keeping an over-full Homestead's contents when a save loads (0.1.9, tracker
    /// row T2.1748). No BepInEx, Harmony or UnityEngine reference: this file is compiled as shipped by
    /// Development_Tools/Tests/ForestLore-HomesteadOverCapacityLoad.Tests.ps1 (Windows PowerShell's
    /// Add-Type, so C# 5 only: no expression bodies, no nameof, no string interpolation).
    ///
    /// Why: 0.1.5 lowered the Homestead's MaxWeightCapacity from 10000 to 4000. On load the base game
    /// re-adds every stored card through GameManager.AddCard, which asks the container for a slot with
    /// InGameCardBase.GetIndexForInventory; that call refuses any card heavier than the room left, and
    /// for a container in the player's own environment AddCard then drops the refused card on the Item
    /// row. A 0.1.4 Homestead holding more than 4000 lost its excess that way. The promise (CHANGELOG
    /// 0.1.5) is that nothing already stored is thrown out; adding more is refused until it is under
    /// the limit. HomesteadLoadKeepPatch applies these rules.
    /// </summary>
    public static class HomesteadLoadKeep
    {
        /// <summary>The Homestead shelter's UniqueID (CardData/Location/Homestead.json).</summary>
        public const string HomesteadUid = "fl_homestead";

        /// <summary>
        /// True when the base game's slot lookup refused a card that a save is loading into the
        /// Homestead, so the card must be kept inside anyway. Only during a load (loadDepth above 0,
        /// counted by the patch around GameManager.LoadInventoryCard), only for the Homestead, only for
        /// a card the Homestead accepts at all (its filter, canReceive), and only for a weight-checked
        /// inventory (not a legacy or blueprint one, whose refusals are not about weight).
        /// </summary>
        public static bool ShouldKeep(int vanillaIndex, int loadDepth, string containerUid, bool hasCard, bool canReceive, bool isWeightInventory)
        {
            if (vanillaIndex >= 0) return false;
            if (loadDepth <= 0) return false;
            if (!hasCard || !canReceive || !isWeightInventory) return false;
            return string.Equals(containerUid, HomesteadUid, StringComparison.Ordinal);
        }

        /// <summary>
        /// The slot the base game would have chosen had the weight fitted (GetIndexForInventory's
        /// non-legacy loop): the first free slot or the first slot the card stacks on, else a new slot
        /// at the end. A null slot is neither free nor a stack, as in the base game.
        /// </summary>
        public static int PickSlot(int slotCount, Func<int, bool> isFree, Func<int, bool> stacksWith)
        {
            for (int k = 0; k < slotCount; k++)
            {
                if (isFree(k)) return k;
                if (stacksWith(k)) return k;
            }
            return slotCount;
        }
    }
}
