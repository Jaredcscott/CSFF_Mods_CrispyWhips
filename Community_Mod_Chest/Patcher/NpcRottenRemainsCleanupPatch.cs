using System.Collections;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Strips Rotten Remains out of every NPC's own inventory. Food NPCs are carrying
    /// (deliveries, foraged stock, restock items) spoils into Rotten Remains over time same as
    /// it would for the player, but no NPC action ever clears it back out — left alone it piles
    /// up in the NPC's inventory forever.
    ///
    /// Subscribes to TickEvents.DtpTick (fires every 15 in-game minutes) — same cadence as
    /// TraitsTickHandler, no new poll registered (CLAUDE.md: no new polls).
    /// </summary>
    internal static class NpcRottenRemainsCleanupPatch
    {
        private static string _rottenRemainsUid;

        public static void Initialize()
        {
            _rottenRemainsUid = VanillaIds.Get("RottenRemains");
            if (string.IsNullOrEmpty(_rottenRemainsUid))
            {
                Plugin.Logger.LogWarning("[NpcRottenRemainsCleanupPatch] RottenRemains GUID not found in VanillaIds — cleanup inactive.");
                return;
            }

            TickEvents.DtpTick += OnDtpTick;
            Plugin.Logger.LogDebug("[NpcRottenRemainsCleanupPatch] NPC Rotten Remains cleanup active.");
        }

        private static void OnDtpTick()
        {
            var gm = CardUtil.GetGameManagerInstance();
            if (gm == null) return;
            if (Reflect.GetMember(gm, "AllNPCs") is not IEnumerable allNpcs) return;

            foreach (var npc in allNpcs)
            {
                if (npc == null) continue;
                var associatedCard = Reflect.GetMember(npc, "AssociatedCard");
                if (associatedCard == null) continue;

                int removed = Inventory.Consume(associatedCard, _rottenRemainsUid, int.MaxValue);
                if (removed > 0)
                    Plugin.Logger.LogDebug($"[NpcRottenRemainsCleanupPatch] Removed {removed} Rotten Remains from an NPC's inventory.");
            }
        }
    }
}
