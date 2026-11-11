using System;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// The Inn Counter's two base prices, in Inn Account units (a Salt deposits 15), before
    /// <see cref="VillagePricing.Apply"/> marks them up while Wanted (x1.2) or Banished (x1.5).
    /// <see cref="InnPatch"/> gates and charges through these constants.
    ///
    /// <para>Purchase Meal is 30 (Nexus c178573716, Chiwei; decided 2026-10-31, mission
    /// csff-nexus-fix-followup-2026-10-31). The 1.69.17 balance pass (owner tile A, kept) cut the meal to
    /// a good home meal's nourishment (Satiation 50, Calories 52) but left it at 50, more than a cooked meat
    /// or fish is worth (vanilla Cooked Lean Fish trades at 25 for Satiation 30). At 30, two Salt buy a
    /// meal and the worst Work Odd Jobs shift (4 Salt) buys two. Stay the Night stays at 50.</para>
    ///
    /// <para>Kept free of Unity and game types, and in C# 5, so
    /// <c>Development_Tools/Tests/CMC-ChiweiOctoberFixes.Tests.ps1</c> can compile this exact file.</para>
    /// </summary>
    public static class InnPrices
    {
        /// <summary>Base price of Purchase Meal.</summary>
        public const float MealPrice = 30f;

        /// <summary>Base price of Stay the Night (a Rented Bed for 6 hours).</summary>
        public const float SleepPrice = 50f;

        /// <summary>How many meals an amount of Inn Account value buys at the base price.</summary>
        public static int MealsBoughtBy(float accountValue)
        {
            if (accountValue <= 0f) return 0;
            return (int)Math.Floor(accountValue / MealPrice);
        }
    }
}
