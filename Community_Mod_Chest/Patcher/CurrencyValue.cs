using System;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Shared currency-value formula for the Inn/Academy "Deposit Currency" CIs.
    /// Salt is a flat value; Nuggets and Duros Coins are priced off their own
    /// SpecialDurability4 metal-type reading (memory: reference_metal_type_sd4),
    /// so every metal tier (copper/bronze/tin/iron) is supported without needing
    /// per-metal item variants.
    /// Nuggets carry a 2.25x multiplier over their raw SD4 reading: a coin
    /// credits half its SD4 reading, and DurosCoinage mints 5 coins per nugget,
    /// so an unminted nugget (raw ore, no smithing labor) is priced 10% under
    /// that mint-then-deposit equivalent (copper: 100 SD4 -> 225 credit, vs.
    /// 250 if minted first) — just enough of a discount to encourage coin
    /// production without making raw-ore deposits pointless.
    /// </summary>
    internal static class CurrencyValue
    {
        public const string SaltGuid   = "f91b5676fc26c0e48a4ee6fd9dfc2ffa";
        public const string NuggetGuid = "4b0f4937a5ecb90499428c8c10288afc";
        public const string CoinUid    = "e5f5d2a633f3437780d6198c3d1e9fdd"; // DurosCoinage: Metal Coin (optional soft dep)

        private const float SaltValue = 15f;
        private const float DefaultMetalTypeValue = 100f; // copper fallback if SD4 is unreadable
        private const float NuggetValueMultiplier = 2.25f; // 90% of the 5-coins-per-nugget mint value (2.5x)

        /// <summary>Value of a dragged Salt/Nugget/Coin card, or 0 if it's none of those.</summary>
        public static float ValueOf(object givenCard)
        {
            if (givenCard == null) return 0f;
            string uid = CardUtil.GetCardUniqueId(givenCard);
            if (uid == null) return 0f;

            if (uid.Equals(SaltGuid, StringComparison.OrdinalIgnoreCase))
                return SaltValue;
            if (uid.Equals(NuggetGuid, StringComparison.OrdinalIgnoreCase))
                return MetalTypeValue(givenCard) * NuggetValueMultiplier;
            if (uid.Equals(CoinUid, StringComparison.OrdinalIgnoreCase))
                return MetalTypeValue(givenCard) / 2f;

            return 0f;
        }

        private static float MetalTypeValue(object card)
        {
            float sd4 = CardUtil.GetDurability(card, "SpecialDurability4");
            return float.IsNaN(sd4) || sd4 <= 0f ? DefaultMetalTypeValue : sd4;
        }
    }
}
