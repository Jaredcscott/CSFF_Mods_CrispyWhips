using System;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Rerolls Ash the Inn Cat's hidden <c>SpecialDurability1</c> pose counter once per in-game
    /// day. The value itself just drives <c>CMC_InnCat.json</c>'s native <c>AlternateNames</c>
    /// conditions (0 = no override, seasonal art shows; 1/2/3 = Hunting/Swimming/Lounging) — no
    /// C# touches card art directly. Values 4/5 (Gift/Greeting) are set purely by JSON
    /// <c>Special1Change</c> on the existing Feed/Pet actions and are never rolled here.
    /// </summary>
    internal static class AshPosePatch
    {
        private const string InnCatUid = "cmcInnCat";

        private static int _lastRolledDay = int.MinValue;

        public static void Initialize()
        {
            TickEvents.Interval(5f, TryRoll, "AshPoseRoll");
        }

        // Retries every poll until Ash is actually found (CardFinder is scoped to the player's
        // current environment — see reference_allcards_env_scoped) so a day that finds her
        // off-screen is never permanently skipped, just deferred to the next successful poll.
        private static void TryRoll()
        {
            try
            {
                int today = GameQuery.CurrentDay;
                if (today < 0 || today == _lastRolledDay) return;

                var cat = CardFinder.Find(InnCatUid);
                if (cat == null) return;

                float r = UnityEngine.Random.value;
                float pose = r < 0.55f ? 0f : r < 0.70f ? 1f : r < 0.85f ? 2f : 3f;

                CardUtil.SetDurability(cat, "SpecialDurability1", pose);
                _lastRolledDay = today;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[AshPosePatch] TryRoll failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }
    }
}
