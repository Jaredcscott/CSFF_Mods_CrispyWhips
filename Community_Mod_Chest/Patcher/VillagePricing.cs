using System;
using System.Collections.Generic;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// The one place the village's own counters read how it feels about you (round 2, B2). Two inputs, the
    /// crime band (<see cref="VillageCrimePatch.BandOf"/>) and Village Reputation
    /// (<see cref="VillageReputationPatch.TryCurrentReputation"/>), and two sides:
    ///
    /// <para><b>Charge</b> (the default): what you PAY the village. InnPatch.BalanceGate, DeductBalance and its
    /// blocked-button tooltip price a meal and a bed through <see cref="Apply"/>, and AcademyPatch's study gate,
    /// tooltip and StudyCompleted price tuition through it. The multiplier is 1 while Clean (so a Clean player
    /// pays the base price whatever their Reputation, which never discounts a Charge), <see cref="SuspectedMarkup"/>
    /// while Suspected, <see cref="WantedMarkup"/> while Wanted and <see cref="BanishedMarkup"/> while Banished.
    /// Nothing here ever refuses: the Academy still teaches a Banished student, it only charges more.</para>
    ///
    /// <para><b>Earn</b>: what the Market Stall's daily sale pays you (MarketStallPatch.SellNextItem). The
    /// Reputation bonus curve (<see cref="ReputationBonus"/>, replacing the old all-or-nothing 1.25 latch at full
    /// Reputation) times <see cref="BanishedSaleFactor"/> while Banished. The curve's top is
    /// <see cref="StallBonusCeiling"/>, exactly the old latch, so the top is never double-counted.</para>
    ///
    /// <para><b>NEVER priced: the restitution paths.</b> A Banished player has to stay able to raise restitution
    /// money, so no handler in <see cref="RestitutionHandlers"/> may reach this class, directly or through a helper
    /// it calls; <see cref="RestitutionJsonActionKeys"/> lists the paths that are pure JSON and have no handler at
    /// all. CMC-Round2.Tests.ps1 reads both lists. A later feature that adds a restitution or income handler (the
    /// Inn wagers, the odd-jobs board) adds its name here.</para>
    ///
    /// <para>Every markup, sale factor and curve knot below is a tunable placeholder. A stat that cannot be read
    /// prices as Clean with Reputation 0 and leaves one warn-once line per cause, never a per-sale line.</para>
    /// </summary>
    internal static class VillagePricing
    {
        internal enum PriceSide { Charge, Earn }

        // ---- Tunable placeholders (CMC-Round2.Tests.ps1 asserts their ordering and the curve's top) ----

        /// <summary>Charge multiplier while Suspected (1-24 crime). 1 means no markup: the Fugitive trait starts here.</summary>
        internal const float SuspectedMarkup = 1.00f;
        /// <summary>Charge multiplier while Wanted (25-59 crime): a meal or bed 50 becomes 60, tuition 100 becomes 120.</summary>
        internal const float WantedMarkup = 1.20f;
        /// <summary>Charge multiplier while Banished (60+ crime): 50 becomes 75, 100 becomes 150.</summary>
        internal const float BanishedMarkup = 1.50f;

        /// <summary>Earn factor while Wanted. 1 means the stall is unaffected until Banished.</summary>
        internal const float WantedSaleFactor = 1.00f;
        /// <summary>Earn factor while Banished: the stall's daily sale pays half as much.</summary>
        internal const float BanishedSaleFactor = 0.50f;

        /// <summary>The top of the Reputation bonus curve and the cap on any Earn multiplier: exactly the 1.25 the
        /// stall paid at full Reputation before this curve existed.</summary>
        internal const float StallBonusCeiling = 1.25f;

        /// <summary>Reputation knots, ascending. Below the first knot the bonus is the first value; at or above the
        /// last it is the last. Reputation runs -100..100.</summary>
        private static readonly float[] CurveReputation = { 0f, 25f, 50f, 75f, 100f };
        /// <summary>Bonus at each knot, straight lines between them. The last value is <see cref="StallBonusCeiling"/>.</summary>
        private static readonly float[] CurveBonus = { 1.00f, 1.05f, 1.10f, 1.17f, 1.25f };

        // ---- Never priced (read by CMC-Round2.Tests.ps1) ----

        /// <summary>ActionRouter handler names (the Name of each registration, up to any ':' suffix) on paths a
        /// Banished player raises restitution money or income through: the copper chests' Sell, the Miller's Make
        /// It Right, and the Town Wood Pile's stock and Player's Pay. None may call the helper.</summary>
        internal static readonly string[] RestitutionHandlers =
        {
            "CopperChestSellGate",
            "CopperChestSellPayout",
            "CopperChestMillerRestitution",
            "TownWoodPileStockGuard",
            "TownWoodPileStock",
            "TownWoodPileCollectCopper",
            "TownWoodPileCollectSalt",
            "InnWagerDiceGate",
            "InnWagerDiceRoll",
            "InnWagerMorrisGate",
            "InnWagerMorrisPlay",
            "InnWagerArmGate",
            "InnWagerArmBout",
            "InnOddJobGate",
            "InnOddJobDelivered",
        };

        /// <summary>Restitution paths that are pure JSON (no handler can reach this class): the Inn Counter's Work
        /// Odd Jobs and Community Service, DismantleActions whose LocalizationKey is listed here.</summary>
        internal static readonly string[] RestitutionJsonActionKeys =
        {
            "CMC_InnCounter_DA_Work",
            "CMC_InnCounter_DA_Service",
        };

        private static readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);

        // ---- The helper ----

        /// <summary>The village's multiplier for you right now. Charge (default): 1 or more, the crime-band markup.
        /// Earn: the Reputation bonus times the crime-band sale factor, at most <see cref="StallBonusCeiling"/>.</summary>
        internal static float Multiplier(PriceSide side = PriceSide.Charge)
        {
            float crime = ReadCrime();
            float reputation = side == PriceSide.Earn ? ReadReputation() : float.NaN;
            return Compute(side, crime, reputation);
        }

        /// <summary>A base price after the Charge multiplier, rounded to whole currency units.</summary>
        internal static float Apply(float basePrice)
        {
            return (float)Math.Round(basePrice * Multiplier(PriceSide.Charge), MidpointRounding.AwayFromZero);
        }

        /// <summary>The pure form of <see cref="Multiplier"/>: no stat reads, so a harness probe or a test can
        /// feed it any crime and Reputation.</summary>
        internal static float Compute(PriceSide side, float crime, float reputation)
        {
            var band = VillageCrimePatch.BandOf(crime);
            if (side == PriceSide.Charge) return MarkupFor(band);
            return Math.Min(StallBonusCeiling, ReputationBonus(reputation) * SaleFactorFor(band));
        }

        internal static float MarkupFor(VillageCrimePatch.CrimeBand band)
        {
            switch (band)
            {
                case VillageCrimePatch.CrimeBand.Suspected: return SuspectedMarkup;
                case VillageCrimePatch.CrimeBand.Wanted: return WantedMarkup;
                case VillageCrimePatch.CrimeBand.Banished: return BanishedMarkup;
                default: return 1f;
            }
        }

        internal static float SaleFactorFor(VillageCrimePatch.CrimeBand band)
        {
            switch (band)
            {
                case VillageCrimePatch.CrimeBand.Wanted: return WantedSaleFactor;
                case VillageCrimePatch.CrimeBand.Banished: return BanishedSaleFactor;
                default: return 1f;
            }
        }

        /// <summary>The stall's Reputation bonus: straight lines between the curve knots, never above
        /// <see cref="StallBonusCeiling"/>. An unreadable Reputation (NaN) is the first knot, no bonus.</summary>
        internal static float ReputationBonus(float reputation)
        {
            int last = CurveReputation.Length - 1;
            if (float.IsNaN(reputation) || reputation <= CurveReputation[0]) return CurveBonus[0];
            if (reputation >= CurveReputation[last]) return Math.Min(StallBonusCeiling, CurveBonus[last]);
            for (int i = 1; i <= last; i++)
            {
                if (reputation > CurveReputation[i]) continue;
                float span = CurveReputation[i] - CurveReputation[i - 1];
                float t = span <= 0f ? 1f : (reputation - CurveReputation[i - 1]) / span;
                return Math.Min(StallBonusCeiling, CurveBonus[i - 1] + t * (CurveBonus[i] - CurveBonus[i - 1]));
            }
            return Math.Min(StallBonusCeiling, CurveBonus[last]);
        }

        // ---- Stat reads (a miss leaves one warn-once line per cause, never a per-sale line) ----

        private static float ReadCrime()
        {
            try
            {
                if (VillageCrimePatch.TryCurrentCrime(out float crime, out string cause)) return crime;
                WarnOnce("crime-" + cause, "[VillagePricing] Village Crime is not readable (" + cause + ") - the village prices you as Clean until it is.");
                return 0f;
            }
            catch (Exception ex)
            {
                WarnOnce("crime-exception", "[VillagePricing] Reading Village Crime failed - the village prices you as Clean: " + (ex.InnerException?.ToString() ?? ex.ToString()));
                return 0f;
            }
        }

        private static float ReadReputation()
        {
            try
            {
                if (VillageReputationPatch.TryCurrentReputation(out float reputation, out string cause)) return reputation;
                WarnOnce("reputation-" + cause, "[VillagePricing] Village Reputation is not readable (" + cause + ") - the stall earns no Reputation bonus until it is.");
                return float.NaN;
            }
            catch (Exception ex)
            {
                WarnOnce("reputation-exception", "[VillagePricing] Reading Village Reputation failed - the stall earns no Reputation bonus: " + (ex.InnerException?.ToString() ?? ex.ToString()));
                return float.NaN;
            }
        }

        private static void WarnOnce(string cause, string message)
        {
            if (_warned.Add(cause)) Plugin.Logger.LogWarning(message);
        }
    }
}
