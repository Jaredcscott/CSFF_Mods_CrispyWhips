using System;
using System.Collections.Generic;

namespace Advanced_Copper_Tools.Patcher
{
    // The spiritual ward of the merged armor (ACT armor merge, owner decision A4, 2026-10-22).
    //
    // Before the merge each metal was its own card, and a Ghost Bronze piece carried a DMG_Spiritual armor
    // modifier on its own ArmorValues. One merged card has one ArmorValues block, and the game scales a whole
    // location by ArmorValueDurabilitiesMultiplier, so the ratio of spiritual to plain armor could not differ
    // by metal in JSON (Documentation/Research/AdvancedCopperTools/armor-merge.md section 6). ArmorWardPatch
    // therefore replaces CardData.ArmorValueMultiplier with Target / CopperBase while ACT's wound prefix has
    // seen a purely spiritual blow, so the piece blocks exactly Target against it. The merged JSON carries no
    // DMG_Spiritual modifier (it would count twice).
    //
    // Plain C# 5 with no game types on purpose: Development_Tools/Tests/ACT-ArmorWard.Tests.ps1 compiles this
    // file with Add-Type and checks every number against the pre-merge card JSON (git show of the old blobs).
    public static class ArmorWardTable
    {
        // Metal Type (SpecialDurability4) rungs, in the order of every row below.
        public static readonly int[] Rungs = new int[] { 100, 110, 120, 130, 140, 200 };

        // The five merged cards (the copper UniqueIDs, kept for save compatibility).
        public static readonly string[] MergedUids = new string[]
        {
            "advanced_copper_tools_copper_helmet",
            "advanced_copper_tools_copper_breastplate",
            "advanced_copper_tools_copper_gauntlets",
            "advanced_copper_tools_copper_greaves",
            "act_copper_shield",
        };

        // The armor the merged card's JSON states per armored location (the copper value).
        public static readonly float[] CopperBase = new float[] { 30f, 30f, 15f, 15f, 80f };

        // What each piece blocks per armored location against a purely spiritual blow, by rung.
        // Body pieces: the old card's base plus its DMG_Spiritual modifier (Ghost Bronze only had one).
        // Shields: copper 80+50, Ghost Bronze 85+80, iron 95+50 as before; the tin, tin bronze and white
        // bronze shields are new with the merge, base 82.5 / 87.5 / 92.5 plus the vanilla Shield's +50.
        public static readonly float[][] Target = new float[][]
        {
            //                 Cu     GB     Tin    TinBr  WhBr   Iron
            new float[] { 30f,   47.5f, 32.5f, 37.5f, 42.5f, 45f },   // helmet
            new float[] { 30f,   47.5f, 32.5f, 37.5f, 42.5f, 45f },   // breastplate
            new float[] { 15f,   24f,   16f,   17.5f, 19f,   20f },   // gauntlets, per arm
            new float[] { 15f,   24f,   16f,   17.5f, 19f,   20f },   // greaves, per leg
            new float[] { 130f,  165f,  132.5f, 137.5f, 142.5f, 145f }, // shield, shield arm
        };

        private static readonly Dictionary<string, int> PieceIndex = BuildPieceIndex();

        private static Dictionary<string, int> BuildPieceIndex()
        {
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < MergedUids.Length; i++) map[MergedUids[i]] = i;
            return map;
        }

        public static bool IsMerged(string uid)
        {
            return uid != null && PieceIndex.ContainsKey(uid);
        }

        // The multiplier that makes the merged piece block Target against a purely spiritual blow.
        // False for a card that is not a merged piece or a rung that is not in the table (the caller then
        // leaves the game's own multiplier alone).
        public static bool TryGetFactor(string uid, int rung, out float factor)
        {
            factor = 1f;
            int piece;
            if (uid == null || !PieceIndex.TryGetValue(uid, out piece)) return false;
            int r = Array.IndexOf(Rungs, rung);
            if (r < 0) return false;
            factor = Target[piece][r] / CopperBase[piece];
            return true;
        }
    }
}
