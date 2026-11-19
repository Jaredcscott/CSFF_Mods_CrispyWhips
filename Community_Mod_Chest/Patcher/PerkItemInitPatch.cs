using System;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Sets initial stat values on CardData SOs for items granted via AddedCardsWarpData.
    /// Perk-spawned items start at 0 for all durability stats; patching FloatValue on the
    /// CardData SO causes the game to spawn them with the correct starting value.
    /// Runs on <see cref="FrameworkEvents.GameDataReady"/> so all framework JSON loading is complete.
    /// </summary>
    internal static class PerkItemInitPatch
    {
        public static void Register()
        {
            FrameworkEvents.GameDataReady += OnGameDataReady;
        }

        private static void OnGameDataReady()
        {
            try
            {
                // Iron Fishing Rod — starts at 100 / 600 durability when granted by the Angler perk.
                SetUsageDurability("CMCIronFishingRod", 600f);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"[PerkItemInit] postfix threw: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // ── helpers ───────────────────────────────────────────────────────────────

        private static void SetUsageDurability(string uid, float startValue)
        {
            var card = CardUtil.GetCardDataById(uid);
            if (card == null)
            {
                Plugin.Logger.LogWarning($"[PerkItemInit] CardData '{uid}' not found — skipping.");
                return;
            }

            // DurabilitySystem is a struct — get the boxed copy, mutate FloatValue, write back.
            var durBox = Reflect.GetMember(card, "UsageDurability");
            if (durBox == null)
            {
                Plugin.Logger.LogWarning($"[PerkItemInit] UsageDurability field not found on {uid}.");
                return;
            }

            // Both writes are checked (traits G5): this runs once per load, and the success line used to print
            // whatever happened, so a renamed FloatValue put the rod back at 0 durability with a log that said
            // it was set. Both are attempted, as before.
            bool valueWritten = Reflect.SetMember(durBox, "FloatValue", startValue);
            bool statWritten = Reflect.SetMember(card, "UsageDurability", durBox);
            if (valueWritten && statWritten)
            {
                Plugin.Logger.LogDebug($"[PerkItemInit] {uid} UsageDurability.FloatValue set to {startValue}.");
                return;
            }
            Plugin.Logger.LogWarning($"[PerkItemInit] {uid} starting durability was NOT set to {startValue}: "
                + (valueWritten ? "" : "UsageDurability.FloatValue could not be written. ")
                + (statWritten ? "" : "CardData.UsageDurability could not be written back. ")
                + "A perk-granted one starts at its default (renamed by a game update?).");
        }
    }
}
