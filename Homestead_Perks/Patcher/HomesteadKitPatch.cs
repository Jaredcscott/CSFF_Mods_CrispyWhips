using System.Collections.Generic;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace HomesteadPerks.Patcher
{
    /// <summary>
    /// Homestead perk (hsptraitsperkhomestead) delivery. The perk grants exactly one
    /// Homestead Kit card at character creation instead of carrying the cabin kit, two
    /// cistern kits, and a raw-material bundle directly — that combination would sum well
    /// past the 4000 Encumbrance cap.
    ///
    /// The single Homestead Kit (600 weight) is carried from the start. Its own "Place"
    /// DismantleAction (ReceivingCardChanges.ModType: 3, destroys itself — HSP_HomesteadKit.json)
    /// is intercepted here via AfterWrapped: once it fires, the cabin kit, two cistern kits, and the
    /// full material stockpile spawn on the CURRENT board (wherever the player chose to settle).
    /// CardsToCreate in a DismantleAction's ReceivingCardChanges is never processed by the vanilla
    /// engine — spawning from a DA requires this ActionRouter.AfterWrapped + SpawnService.Spawn()
    /// pattern.
    ///
    /// This is also what makes Heavy Stone (750/unit) and Tree Log (3000/unit) viable in the
    /// bundle — spawned on the ground at Place time, not force-carried from character creation,
    /// so their weight never touches the Encumbrance cap unless the player chooses to pick them up.
    /// </summary>
    internal static class HomesteadKitPatch
    {
        private const string HomesteadKitId = "hsphomesteadkit";
        private const string CabinKitId = "hspcabinkit";
        private const string CisternKitId = "hspraincisternkit";

        // Vanilla GUIDs (Documentation/CSFF_Reference.md / UniqueIDScriptableGUID/CardData.json)
        private const string PlankGuid = "57460207bbf77fa4fb6720aed5d84851";
        private const string MudBrickGuid = "6c2001f42a960db4583cdd65a47ecf3c";
        private const string StoneSmallGuid = "a7384e5147b23a642809451cc4ef24fb";
        private const string StoneHeavyGuid = "8695a7aa22521aa45be582d3c1558f78";
        private const string TreeLogGuid = "0ab556ab6af1efc47a2cba5cdf4ace04";
        private const string ClayGuid = "68c14d265ea6c874ba79444d2e1ef7b3";
        private const string RopeGuid = "a7a58aa687df66e47a42fc13e0fdbeaa";
        private const string MetalNuggetGuid = "4b0f4937a5ecb90499428c8c10288afc";
        private const string CopperNailsId = "advanced_copper_tools_copper_nails";

        // A bare spawn starts each card at its defaults, which for these four is unusable: a Plank's
        // wood ID 0 is no wood at all, Clay rolls quality 0-50, a Tree Log is quality 0 with no fuel,
        // and a Metal Nugget is untyped, quality 0 and spawns at 300 degrees. The values are vanilla's
        // own: a felled Large Oak becomes a log carrying its Fragrance 18, Fuel 35, Quality 96 and ID 1
        // (TreeLargeOak Progress.OnZero, ModType 2), oak planks are ID 1, the nuggets copy vanilla's
        // copper-nugget perk carrier (Pk_5_1_8_SmithingGear_CopperNugget: Temperature 0, Metal
        // Quality 50, Metal Type 100), and 30 is 60% of Clay's 50 cap (player report, Chiwei
        // 2026-09-27). SpawnService.Spawn honours the overrides from framework 2.28.0.
        private static readonly Dictionary<string, float> OakPlank = new Dictionary<string, float>
        {
            { "SpecialDurability4", 1f },
        };
        private static readonly Dictionary<string, float> OakLog = new Dictionary<string, float>
        {
            { "UsageDurability", 18f }, { "FuelCapacity", 35f },
            { "SpecialDurability2", 96f }, { "SpecialDurability4", 1f },
        };
        private static readonly Dictionary<string, float> WorkedClay = new Dictionary<string, float>
        {
            { "SpecialDurability2", 30f },
        };
        private static readonly Dictionary<string, float> CopperNugget = new Dictionary<string, float>
        {
            { "FuelCapacity", 0f }, { "SpecialDurability2", 50f }, { "SpecialDurability4", 100f },
        };

        public static void Initialize()
        {
            ActionRouter.Register(new ActionHandler
            {
                Name = "HomesteadKitPlace",
                CardUid = HomesteadKitId,
                ActionNamePrefix = "Place",
                Timing = ActionTiming.AfterWrapped,
                After = _ => SpawnHomesteadContents(),
            });

            Plugin.Logger.LogDebug("[HomesteadKitPatch] Homestead Kit unpack handler registered.");
        }

        private static void SpawnHomesteadContents()
        {
            // Vanilla balance pass (plan section 1.1, HSP-B2, 1.3.5): the stockpile was halved or more
            // so the perk no longer covers shelter, water and the first metal tier on day 1. 9 nuggets
            // match vanilla Smithing Gear; 20 nails pay one WDI build (Outlet 4 or Forge 10), not all.
            // CMC's Founders Kit (Community_Mod_Chest) hands over the same counts.
            SpawnService.Spawn(CabinKitId);
            SpawnMany(CisternKitId, 2);

            SpawnMany(PlankGuid, 15, OakPlank);
            SpawnMany(MudBrickGuid, 15);
            SpawnMany(StoneSmallGuid, 20);
            SpawnMany(StoneHeavyGuid, 6);
            SpawnMany(TreeLogGuid, 6, OakLog);
            SpawnMany(ClayGuid, 10, WorkedClay);
            SpawnMany(RopeGuid, 5);
            SpawnMany(MetalNuggetGuid, 9, CopperNugget);

            // The Metal Nail ships in CSFFModFramework 2.28.0 and later (and in ACT before it), so the
            // grant asks whether the CARD exists, never whether ACT is installed (Fleet plan feature 3).
            // It spawns at its card defaults (fuel 0, Metal Type Copper), so no stats are passed. One
            // lookup up front: a missing card would otherwise log 20 SpawnService errors.
            if (CardUtil.GetCardDataById(CopperNailsId) != null)
            {
                SpawnMany(CopperNailsId, 20);
                Plugin.Logger.LogDebug("[HomesteadKitPatch] Metal Nail card loaded: spawned 20 copper nails.");
            }
            else
            {
                Plugin.Logger.LogDebug("[HomesteadKitPatch] Metal Nail card not loaded (framework older than 2.28.0?): nails skipped.");
            }

            Plugin.Logger.LogDebug("[HomesteadKitPatch] Homestead contents spawned.");
        }

        private static void SpawnMany(string uid, int quantity, Dictionary<string, float> stats = null)
        {
            for (int i = 0; i < quantity; i++)
                SpawnService.Spawn(uid, stats);
        }
    }
}
