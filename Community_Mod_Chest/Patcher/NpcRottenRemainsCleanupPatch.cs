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
    /// Subscribes to TickEvents.DtpTick (fires every 15 in-game minutes) — no new poll registered
    /// (CLAUDE.md: no new polls).
    /// </summary>
    internal static class NpcRottenRemainsCleanupPatch
    {
        private static string _rottenRemainsUid;

        // One latch per cause (root CLAUDE.md Silent Catch Blocks; modwide A13 and G5, 2026-11-12).
        private static readonly System.Collections.Generic.HashSet<string> _warned = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);

        private static void WarnOnce(string cause, string message)
        {
            if (_warned.Add(cause)) Plugin.Logger.LogWarning("[NpcRottenRemainsCleanupPatch] " + message);
        }

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
            // The gm == null return above is quiet on purpose (it fires every tick before a run boots). A
            // GameManager whose AllNPCs no longer reads as a list is a game-update rename: one line, by name.
            if (!Reflect.TryGetMember(gm, "AllNPCs", out var roster) || roster is not IEnumerable allNpcs)
            {
                WarnOnce("allnpcs-unreadable", "GameManager.AllNPCs did not resolve to a list (renamed by a game update?); Rotten Remains are no longer cleared from NPC inventories. Logged once.");
                return;
            }

            foreach (var npc in allNpcs)
            {
                if (npc == null) continue;
                // TryGetMember tells a missing member from an NPC that simply has no board card yet (null value).
                if (!Reflect.TryGetMember(npc, "AssociatedCard", out var associatedCard))
                {
                    WarnOnce("associatedcard-unreadable", npc.GetType().Name + ".AssociatedCard did not resolve (renamed by a game update?); Rotten Remains are no longer cleared from NPC inventories. Logged once.");
                    return;
                }
                if (associatedCard == null) continue;

                int removed = Inventory.Consume(associatedCard, _rottenRemainsUid, int.MaxValue);
                if (removed > 0)
                    Plugin.Logger.LogDebug($"[NpcRottenRemainsCleanupPatch] Removed {removed} Rotten Remains from an NPC's inventory.");
            }
        }
    }
}
