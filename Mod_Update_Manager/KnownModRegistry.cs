using System;
using System.Collections.Generic;
using System.Linq;

namespace mod_update_manager
{
    /// <summary>
    /// Built-in registry of known CSFF mods on Nexus Mods.
    /// Used as a fallback when a mod has no NexusModId in ModInfo.json or mappings file.
    /// Add entries here as new mods appear on Nexus.
    /// </summary>
    public static class KnownModRegistry
    {
        /// <summary>
        /// Maps known mod identifiers (folder names, mod names) to their Nexus mod IDs.
        /// Key = lowercase identifier, Value = (NexusModId, DisplayName)
        /// </summary>
        private static readonly Dictionary<string, (string NexusId, string DisplayName)> _knownMods =
            new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            // ID  1: reserved legacy framework entry
            // ID  2: CSFF Card Detail Tooltip — replaced by WikiMod
            // ID  3: CardSizeReduce
            { "Pikachu-缩小卡牌-CardSizeReduce", ("3",  "CardSizeReduce") },
            { "CardSizeReduce",                  ("3",  "CardSizeReduce") },

            // ID  4: NPC Tracker — not functioning in EA 0.62b
            // ID  5: reserved legacy framework entry
            // ID  6: SpaceBall (SpCreed)
            { "SpaceBall",                       ("6",  "SpaceBall") },

            // ID  7: PortableRefrigerator-IceBall
            { "PortableRefrigerator-IceBall-1.07", ("7", "Portable Refrigerator - IceBall") },
            { "IceCrystalball",                  ("7",  "Portable Refrigerator - IceBall") },

            // ID  8: FreshnessAdjustment
            { "FreshnessAdjustment",             ("8",  "FreshnessAdjustment") },

            // ID  9: SurvivalSpecialist (YoYo / SpCreed)
            { "SurvivalSpecialist",              ("9",  "SurvivalSpecialist") },

            // IDs 10-11: removed from Nexus by staff
            // ID 12: PlantGrowthRateAdjustment
            { "PlantGrowthRateAdjustment",       ("12", "PlantGrowthRateAdjustment") },

            // ID 13: Theme Support — broken in EA 0.62b
            // ID 14: Tropical Island Status Icon — not working in CSFF
            { "Tropical Island Status Icon",     ("14", "Tropical Island Status Icon") },

            // ID 15: CullenTransportation-RV
            { "CullenTransportation-RV",         ("15", "CullenTransportation - RV") },
            { "CullenTransportation",            ("15", "CullenTransportation - RV") },

            // ID 16: removed from Nexus by staff
            // ID 17: DataPackage For ModEditor — data package, not a playable mod
            // ID 18: BetterPerksBuildings (smaurine82)
            { "BetterPerksBuildings",            ("18", "BetterPerksBuildings") },

            // ID 19: GIF Support — not working; we have internal GIF code
            // ID 20: CheatsPatch — not working in EA 0.62b
            // ID 21: Advanced Copper Tools (crispywhips93)
            { "Advanced_Copper_Tools",           ("21", "Advanced Copper Tools") },
            { "AdvancedCopperTools",             ("21", "Advanced Copper Tools") },
            { "Advanced Copper Tools",           ("21", "Advanced Copper Tools") },

            // ID 22: Herbs and Fungi Expansion (crispywhips93)
            { "Herbs_And_Fungi",                 ("22", "Herbs and Fungi Expansion") },
            { "HerbsAndFungi",                   ("22", "Herbs and Fungi Expansion") },
            { "Herbs And Fungi",                 ("22", "Herbs and Fungi Expansion") },
            { "Herbs and Fungi Expansion",       ("22", "Herbs and Fungi Expansion") },

            // ID 23: Stop Auto Save (crispywhips93)
            { "Stop_Auto_Save",                  ("23", "Stop Auto Save") },
            { "StopAutoSave",                    ("23", "Stop Auto Save") },
            { "Stop Auto Save",                  ("23", "Stop Auto Save") },

            // ID 24: More Field Expansion (crispywhips93)
            { "More_Field_Expansion",            ("24", "More Field Expansion") },
            { "MoreFieldExpansion",              ("24", "More Field Expansion") },
            { "More Field Expansion",            ("24", "More Field Expansion") },

            // ID 25: Quick Transfer (crispywhips93)
            { "Quick_Transfer",                  ("25", "Quick Transfer") },
            { "QuickTransfer",                   ("25", "Quick Transfer") },
            { "Quick Transfer",                  ("25", "Quick Transfer") },

            // ID 26: Repeat Action (crispywhips93)
            { "Repeat_Action",                   ("26", "Repeat Action") },
            { "RepeatAction",                    ("26", "Repeat Action") },
            { "Repeat Action",                   ("26", "Repeat Action") },

            // ID 27: Skill Training Speed Boost (crispywhips93)
            { "Skill_Speed_Boost",               ("27", "Skill Training Speed Boost") },
            { "SkillSpeedBoost",                 ("27", "Skill Training Speed Boost") },
            { "Skill Speed Boost",               ("27", "Skill Training Speed Boost") },
            { "RemoveSkillStaleness",            ("27", "Skill Training Speed Boost") },
            { "Skill Training Speed Boost",      ("27", "Skill Training Speed Boost") },

            // ID 28: WikiMod / iMod (CrazyJunichi)
            { "WikiMod",                         ("28", "WikiMod") },
            { "iMod",                            ("28", "WikiMod") },

            // ID 29: CSFF Modding Starter Kit (crispywhips93)
            { "CSFF_Modding_Starter_Kit",        ("29", "CSFF Modding Starter Kit") },
            { "CSFFModdingStarterKit",           ("29", "CSFF Modding Starter Kit") },
            { "CSFF Modding Starter Kit",        ("29", "CSFF Modding Starter Kit") },

            // ID 30: CSFF Modding Framework (crispywhips93)
            { "CSFFModFramework",                ("30", "CSFF Modding Framework") },
            { "CSFF_Mod_Framework",              ("30", "CSFF Modding Framework") },
            { "CSFF Modding Framework",          ("30", "CSFF Modding Framework") },

            // ID 31: Water Driven Infrastructure (crispywhips93)
            { "WaterDrivenInfrastructure",       ("31", "Water Driven Infrastructure") },
            { "Water_Driven_Infrastructure",     ("31", "Water Driven Infrastructure") },
            { "Water Driven Infrastructure",     ("31", "Water Driven Infrastructure") },

            // ID 32: reserved legacy framework entry
            // ID 33: Old Version Quest List (琳曦儿-旧版任务列表)
            { "琳曦儿-旧版任务列表",              ("33", "Old Version Quest List") },
            { "linxier9.renwu",                  ("33", "Old Version Quest List") },
            { "Old Version Quest List",          ("33", "Old Version Quest List") },

            // ID 34: Fantasy Item — not working in EA 0.62b
            // ID 35: Pet Travel Crate
            { "Pet Travel Crate",                ("35", "Pet Travel Crate") },
            { "PetTravelCrate",                  ("35", "Pet Travel Crate") },

            // ID 36: Campfire Kiln Furnace Fireplace Oven - 8 Slots
            { "Campfire Kiln Furnace Fireplace Oven - 8 Slots", ("36", "Campfire Kiln Furnace Fireplace Oven - 8 Slots") },

            // ID 37: Greenstone of RiverConfluence (linxier1)
            { "Greenstone of RiverConfluence",   ("37", "Greenstone of RiverConfluence") },
        };

        /// <summary>
        /// Tries to find a Nexus mod ID for a given mod name or folder name.
        /// Checks exact match first, then tries common name transformations.
        /// </summary>
        public static string GetNexusModId(string identifier)
        {
            if (string.IsNullOrEmpty(identifier))
                return null;

            // Direct lookup
            if (_knownMods.TryGetValue(identifier, out var entry))
                return entry.NexusId;

            // Try with underscores replaced by spaces and vice versa
            var withSpaces = identifier.Replace("_", " ");
            if (_knownMods.TryGetValue(withSpaces, out entry))
                return entry.NexusId;

            var withUnderscores = identifier.Replace(" ", "_");
            if (_knownMods.TryGetValue(withUnderscores, out entry))
                return entry.NexusId;

            return null;
        }

        /// <summary>
        /// Gets the display name for a known mod
        /// </summary>
        public static string GetDisplayName(string identifier)
        {
            if (string.IsNullOrEmpty(identifier))
                return null;

            if (_knownMods.TryGetValue(identifier, out var entry))
                return entry.DisplayName;

            return null;
        }

        /// <summary>
        /// Returns the Nexus URL for a given mod ID
        /// </summary>
        public static string GetNexusUrl(string nexusModId)
        {
            if (string.IsNullOrEmpty(nexusModId))
                return null;
            return $"https://www.nexusmods.com/cardsurvivalfantasyforest/mods/{nexusModId}";
        }

        /// <summary>
        /// Maps "nexusId|normalizedInstalledVersion" → the Nexus version string it is equivalent to.
        /// Used when mod authors publish version numbers on Nexus that differ from what their
        /// ModInfo.json reports (e.g. "1.05" on Nexus but "1.0.4" inside the mod).
        /// </summary>
        private static readonly Dictionary<string, string> _versionEquivalencies =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // SpaceBall v6: mod internally reports 1.0.4 but Nexus page shows 1.05 (same release)
            { "6|1.0.4", "1.05" },
        };

        /// <summary>
        /// Returns the Nexus version string that the given installed version is equivalent to,
        /// or null if no equivalency is registered.
        /// </summary>
        public static string GetVersionEquivalent(string nexusId, string installedVersion)
        {
            if (string.IsNullOrEmpty(nexusId) || string.IsNullOrEmpty(installedVersion))
                return null;

            var key = $"{nexusId}|{installedVersion.TrimStart('v', 'V')}";
            return _versionEquivalencies.TryGetValue(key, out var equivalent) ? equivalent : null;
        }

        /// <summary>
        /// Gets all unique known mods (deduplicated by NexusId)
        /// </summary>
        public static List<(string NexusId, string DisplayName)> GetAllKnownMods()
        {
            return _knownMods.Values
                .GroupBy(v => v.NexusId)
                .Select(g => g.First())
                .OrderBy(v => v.DisplayName)
                .ToList();
        }
    }
}
