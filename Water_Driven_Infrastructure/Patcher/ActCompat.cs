using System;
using System.Collections;
using System.Collections.Generic;
using CSFFModFramework.Util;

namespace WaterDrivenInfrastructure.Patcher
{
    /// <summary>
    /// The single detection point for two separate questions, computed once at LoadMainGameData
    /// postfix time and cached for the run (Fleet plan feature 3):
    /// <see cref="MetalPartsPresent"/>: is the shared Metal Nail card loaded? Since framework 2.28.0
    /// the Metal Nail, Metal Sheet and Tin Solder ship in CSFFModFramework under ACT's old UniqueIDs,
    /// so this is true with or without ACT. GameLoadPatch.DetectSharedMetalParts warns when it is false
    /// (WDI's recipes and Workshop outputs name these shared cards since WDI 1.12.3).
    /// <see cref="IsInstalled"/>: is AdvancedCopperTools itself installed? Probed on an ACT-only card
    /// (the Copper Bathtub), never on a card the framework also ships. BathtubPlumbingPatch asks this.
    /// </summary>
    public static class ActCompat
    {
        public const string CopperNailsUid = "advanced_copper_tools_copper_nails";
        public const string MetalSheetUid = "advanced_copper_tools_metal_sheet";

        // The probe for "ACT itself is installed": a card only ACT ships (the framework hosts the nail,
        // sheet and solder, so none of those can answer this). A Pester contract checks this UniqueID is
        // in AdvancedCopperTools/CardData and NOT in CSFFModFramework/CardData.
        public const string ActOnlyProbeUid = "advanced_copper_tools_copper_bathtub_empty";

        // The Workshop's iron crafts (plan feature 9) hand over ACT's own iron items when ACT is installed.
        // Since ACT 1.18.0 those are the one Metal Nail (CopperNailsUid) and the one Metal Sheet
        // (MetalSheetUid) stamped with this Metal Type (SpecialDurability4), which ACT names "Iron Nail" and
        // "Iron Sheet". ACT's old act_iron_nail and act_iron_sheet are retired: they still load and reforge
        // themselves into the merged cards, but WDI no longer spawns them (or a Workshop craft would hand over
        // a card that changes under the player a tick later). Since framework 2.28.0 the framework ships both cards (plan feature 31),
        // so a Pester contract checks each UniqueID against CSFFModFramework/CardData.
        public const float IronMetalType = 200f;

        // The ACT Copper Brazier's LIT card (plan feature 10). MillRaceNetwork.HasHeatSourceAt counts it as a
        // heat source at a location. The unlit brazier is a separate card, so presence means lit. It never
        // matches when ACT is absent (no such card exists then), so it needs no IsInstalled check. A Pester
        // contract checks this UniqueID against AdvancedCopperTools/CardData.
        public const string CopperBrazierLitUid = "advanced_copper_tools_copper_brazier_placed_lit";

        // The ACT Copper Bathtub's placed Empty and Full cards (plan feature 16b). A running Mill Race Outlet swaps an
        // Empty tub to Full in place (BathtubPlumbingPatch); the Warm tub is never touched. Water Level is the
        // tub's SpecialDurability2, which SetModel leaves at 0, so the handler writes it after the swap. A Pester
        // contract checks both UniqueIDs against AdvancedCopperTools/CardData and that they appear only here.
        public const string BathtubEmptyPlacedUid = "advanced_copper_tools_copper_bathtub_empty_placed";
        public const string BathtubFullPlacedUid = "advanced_copper_tools_copper_bathtub_full_placed";
        public const float BathtubFullWaterLevel = 2400f;

        /// <summary>AdvancedCopperTools itself is installed (an ACT-only card is loaded).</summary>
        public static bool IsInstalled { get; private set; }

        /// <summary>The shared Metal Nail card is loaded (framework 2.28.0+, or ACT on an older framework).</summary>
        public static bool MetalPartsPresent { get; private set; }

        // Blast mirror (plan feature 15): one row per ACT item the Water-Driven Forge and Workshop
        // blast into Metal Nuggets, as (nugget count, Metal Type). The count mirrors NuggetReturn in
        // AdvancedCopperTools/SmeltingRecipes.json. The Metal Type is the one the nuggets take when the
        // blasted item carries no Metal Type of its own (SpecialDurability4): the row's MetalType in that
        // file, or 100 (copper, what the nugget is without one) where the row declares none. Most of ACT's
        // armour and shields have no SpecialDurability4 at all, so this value is what types their nuggets.
        // Metal Type values: Copper 100, Ghost Bronze 110, Tin 120, Tin Bronze 130, White Bronze 140,
        // Iron 200 (the vanilla Metal Nugget accepts exactly these). ACT UniqueIDs live in this file only.
        // Written as literals on purpose: a Pester contract parses this table and checks every UID against
        // AdvancedCopperTools/CardData and every count and type against SmeltingRecipes.json.
        private static readonly Dictionary<string, (int nuggets, float metalType)> BlastRows =
            new Dictionary<string, (int nuggets, float metalType)>(StringComparer.Ordinal)
        {
            // Copper-grade and InheritMetal rows (the item's own Metal Type is read first)
            { "advanced_copper_tools_copper_nails",          (1, 100f) },
            { "advanced_copper_tools_wheel_hub",             (6, 100f) },
            { "advanced_copper_tools_metal_sheet",           (6, 100f) },
            { "advanced_copper_tools_wheel_rim",             (6, 100f) },
            { "advanced_copper_tools_stove_top_mold",        (6, 100f) },
            { "advanced_copper_tools_cast_stove_top",        (6, 100f) },
            { "advanced_copper_tools_large_saw",             (16, 100f) },
            { "advanced_copper_tools_lantern_oilwell",       (6, 100f) },
            { "advanced_copper_tools_lantern_oilwell_lit",   (6, 100f) },
            { "advanced_copper_tools_metal_lantern",         (18, 100f) },
            { "advanced_copper_tools_metal_lantern_lit",     (18, 100f) },
            { "advanced_copper_tools_copper_pantry",         (30, 100f) },
            { "advanced_copper_tools_copper_stove",          (30, 100f) },
            { "advanced_copper_tools_wheel_assembly",        (12, 100f) },
            { "advanced_copper_tools_tea_station_kit",       (48, 100f) },
            { "advanced_copper_tools_copper_bathtub_empty",  (78, 100f) },
            { "advanced_copper_tools_copper_helmet",         (15, 100f) },
            { "advanced_copper_tools_copper_gauntlets",      (11, 100f) },
            { "advanced_copper_tools_copper_greaves",        (15, 100f) },
            { "advanced_copper_tools_copper_breastplate",    (23, 100f) },
            { "advanced_copper_tools_copper_brazier_kit",    (22, 100f) },
            { "advanced_copper_tools_copper_cauldron",       (34, 100f) },
            { "advanced_copper_tools_copper_watering_can",   (10, 100f) },
            { "advanced_copper_tools_copper_rim_chamberpot", (6, 100f) },
            // Retired by ACT 1.18.0 (it reforges into the Metal Sheet); kept while ACT's file keeps its row.
            { "act_iron_sheet",                              (5, 100f) },
            { "act_copper_shield",                           (22, 100f) },
            // Rows WDI has always blasted that are not in ACT's SmeltingRecipes.json: ACT melts these
            // through their own Progress, so there is no row to mirror, only the nugget count WDI used.
            { "advanced_copper_tools_shape_metal_pan",       (4, 100f) },
            { "advanced_copper_tools_wearable_metal_pan",    (5, 100f) },
            { "advanced_copper_tools_copper_tea_kettle",     (18, 100f) },
            { "advanced_copper_tools_bucket",                (48, 100f) },
            // Iron (200)
            { "act_iron_helmet",                             (20, 200f) },
            { "act_iron_gauntlets",                          (15, 200f) },
            { "act_iron_greaves",                            (20, 200f) },
            { "act_iron_breastplate_m",                      (30, 200f) },
            { "act_iron_breastplate_f",                      (30, 200f) },
            { "act_iron_large_saw",                          (20, 200f) },
            { "act_iron_shield",                             (30, 200f) },
            // Tin Bronze (130)
            { "act_bronze_helmet",                           (17, 130f) },
            { "act_bronze_gauntlets",                        (13, 130f) },
            { "act_bronze_greaves",                          (17, 130f) },
            { "act_bronze_breastplate",                      (26, 130f) },
            // Ghost Bronze (110)
            { "act_ghostbronze_shield",                      (25, 110f) },
            { "act_ghostbronze_helmet",                      (17, 110f) },
            { "act_ghostbronze_breastplate",                 (26, 110f) },
            { "act_ghostbronze_gauntlets",                   (13, 110f) },
            { "act_ghostbronze_greaves",                     (17, 110f) },
            // Tin (120)
            { "act_tin_helmet",                              (17, 120f) },
            { "act_tin_breastplate",                         (26, 120f) },
            { "act_tin_gauntlets",                           (13, 120f) },
            { "act_tin_greaves",                             (17, 120f) },
            // White Bronze (140)
            { "act_whitebronze_helmet",                      (17, 140f) },
            { "act_whitebronze_breastplate",                 (26, 140f) },
            { "act_whitebronze_gauntlets",                   (13, 140f) },
            { "act_whitebronze_greaves",                     (17, 140f) },
        };

        /// <summary>
        /// The Blast row of an ACT item: how many nuggets it melts into and the Metal Type they take
        /// when the item has none of its own. A lookup by UniqueID, so it answers false for every item
        /// when ACT is not installed (none of these cards exists then); no install check is needed.
        /// </summary>
        public static bool TryGetBlastRow(string uid, out int nuggets, out float metalType)
        {
            nuggets = 0;
            metalType = 0f;
            if (string.IsNullOrEmpty(uid) || !BlastRows.TryGetValue(uid, out var row)) return false;
            nuggets = row.nuggets;
            metalType = row.metalType;
            return true;
        }

        public static void Detect(IEnumerable allData)
        {
            IsInstalled = false;
            MetalPartsPresent = false;
            if (allData == null) return;

            foreach (var item in allData)
            {
                if (item == null) continue;
                string uid = CardUtil.GetCardUniqueId(item);
                if (uid == CopperNailsUid) MetalPartsPresent = true;
                else if (uid == ActOnlyProbeUid) IsInstalled = true;
                if (MetalPartsPresent && IsInstalled) return;
            }
        }
    }
}
