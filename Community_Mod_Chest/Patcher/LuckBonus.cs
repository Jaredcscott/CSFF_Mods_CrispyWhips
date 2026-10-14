using System;
using System.Collections.Generic;
using UnityEngine;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Luck's readers (round 2, B4). Vanilla Luck (GameStat 0aa0ed30405a3ae499ca690c069a007e, range 0
    /// to 288, base 0, no rate of its own) is written by the Spirit Hare and by Born Mark and read by
    /// nothing in the base game, so a Born Mark holder had a raised stat that did nothing. Three CMC rolls
    /// now read it: the chest search's detection chance (<see cref="CopperChestPatch"/>), the Market
    /// Stall's robbery chance (<see cref="MarketStallPatch"/>) and the Iron Rod's rare catches
    /// (<see cref="IronRodFishingPatch"/>, which hands the stat to the game's own drop-weight modifier).
    ///
    /// <para>The mapping is <c>clamp01(Luck / <see cref="LuckPlateau"/>)</c> times
    /// <see cref="MaxAdjustment"/>, from the CURRENT value (<c>SimpleCurrentValue</c>, never the base).
    /// The plateau is 36, not the 288 cap, because Born Mark is a RATE (+1.2 a tick) that Luck's own tier
    /// decay (-1 a tick across 2 to 36) cancels: a Born Mark holder settles at about 36 and never nears 288
    /// (Documentation/Research/Community_Mod_Chest/engine_reads.md section W0.7, SIMULATED). A normaliser to
    /// 288 would give that holder about 0.006 of the scale.</para>
    ///
    /// <para>Small on purpose: vanilla may wire Luck to something of its own later (memory
    /// reference_vestigial_vanilla_stats), and the two would then stack.</para>
    /// </summary>
    internal static class LuckBonus
    {
        /// <summary>UniqueID of the vanilla Luck GameStat. Born Mark's PassiveStatModifiers name the same GUID.</summary>
        internal const string LuckStatUid = "0aa0ed30405a3ae499ca690c069a007e";

        /// <summary>The Luck value that counts as full. Born Mark's plateau, not the stat's 288 cap.</summary>
        internal const float LuckPlateau = 36f;

        /// <summary>The most Luck takes off a roll's chance, reached at <see cref="LuckPlateau"/>.
        /// PLACEHOLDER (plan open question 3): tune by playing, keep it small.</summary>
        internal const float MaxAdjustment = 0.05f;

        private static readonly HashSet<string> _warned = new(StringComparer.Ordinal);

        /// <summary>The adjustment (0 to <see cref="MaxAdjustment"/>) for a Luck reading; a reading below 0
        /// (HiddenStat's "unreadable") adjusts nothing.</summary>
        internal static float AdjustmentFor(float luck)
        {
            if (luck < 0f) return 0f;
            return MaxAdjustment * Mathf.Clamp01(luck / LuckPlateau);
        }

        /// <summary>Reads Luck's current value once and returns its adjustment. An unreadable stat
        /// (<see cref="HiddenStat.Get"/> returns -1, and Luck's minimum is 0 so that is unambiguous) applies
        /// no adjustment and warns once per roll site; <see cref="HiddenStat"/> already logs the specific
        /// cause at Debug.</summary>
        internal static float Adjustment(string site)
        {
            float luck = HiddenStat.Get(LuckStatUid);
            if (luck < 0f)
            {
                WarnOnce(site + "-luck-unreadable",
                    $"[LuckBonus] Luck ({LuckStatUid}) is not readable at the {site} roll - no Luck adjustment is applied to it.");
                return 0f;
            }
            return AdjustmentFor(luck);
        }

        private static void WarnOnce(string cause, string message)
        {
            if (_warned.Add(cause)) Plugin.Logger.LogWarning(message);
        }
    }
}
