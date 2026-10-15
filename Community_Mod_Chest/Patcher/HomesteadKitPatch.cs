using System.Collections.Generic;
using CSFFModFramework.Api;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Homestead trait (traits_perk_homestead) delivery. The perk grants exactly one
    /// Homestead Kit card at character creation instead of carrying the cabin kit, two
    /// cistern kits, and a raw-material bundle directly — that combination summed well
    /// past the 4000 Encumbrance cap (see memory reference_characterperk_equipped_added_cards_weight;
    /// CMC 1.48.4 shrank the bundle to fit, but that left it far short of the intended
    /// "decent starting supply").
    ///
    /// Instead, the single Homestead Kit (600 weight) is carried from the start. Its own
    /// "Place" DismantleAction (ReceivingCardChanges.ModType: 3, destroys itself — CMC_HomesteadKit.json)
    /// is intercepted here via AfterWrapped: once it fires, the cabin kit, two cistern kits, and the
    /// full material stockpile spawn on the CURRENT board (wherever the player chose to settle).
    /// CardsToCreate in a DismantleAction's ReceivingCardChanges is never processed by the vanilla
    /// engine (CLAUDE.md §CardInteractions vs DismantleActions) — spawning from a DA requires this
    /// ActionRouter.AfterWrapped + SpawnService.Spawn() pattern (proven in Sirus23ModCollection's
    /// CompanionHuntPatch.cs).
    ///
    /// This is also what makes Heavy Stone (750/unit) and Tree Log (3000/unit) viable in the
    /// bundle again — spawned on the ground at Place time, not force-carried from character
    /// creation, so their weight never touches the Encumbrance cap unless the player chooses
    /// to pick them up.
    /// </summary>
    internal static class HomesteadKitPatch
    {
        private const string HomesteadKitId = "cmchomesteadkit";
        private const string CabinKitId = "cmchomesteadcabinkit";
        private const string CisternKitId = "cmchomesteadraincisternkit";

        // Vanilla GUIDs (Documentation/CSFF_Reference.md / UniqueIDScriptableGUID/CardData.json)
        private const string PlankGuid = "57460207bbf77fa4fb6720aed5d84851";
        private const string MudBrickGuid = "6c2001f42a960db4583cdd65a47ecf3c";
        private const string StoneSmallGuid = "a7384e5147b23a642809451cc4ef24fb";
        private const string StoneHeavyGuid = "8695a7aa22521aa45be582d3c1558f78";
        private const string TreeLogGuid = "0ab556ab6af1efc47a2cba5cdf4ace04";
        private const string ClayGuid = "68c14d265ea6c874ba79444d2e1ef7b3";
        private const string RopeGuid = "a7a58aa687df66e47a42fc13e0fdbeaa";
        private const string CopperNailUid = "advanced_copper_tools_copper_nails";

        // A bare spawn starts each card at its defaults, which for these three is unusable: a Plank's
        // wood ID 0 is no wood at all, Clay rolls quality 0-50, and a Tree Log is quality 0 with no
        // fuel. The values are vanilla's own: a felled Large Oak becomes a log carrying its Fragrance
        // 18, Fuel 35, Quality 96 and ID 1 (TreeLargeOak Progress.OnZero, ModType 2), oak planks are
        // ID 1, and 30 is 60% of Clay's 50 cap (player report, Chiwei 2026-09-27). SpawnService.Spawn
        // honours the overrides from framework 2.28.0.
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
            SpawnService.Spawn(CabinKitId);
            SpawnMany(CisternKitId, 2);

            // Vanilla balance pass (1.69.17, plan Part II CMC-V-B11): about half the old stockpile, matching
            // HomesteadPerks' kit (vanilla's 1-Star Cabin Start gives the cabin alone). It was 30 Planks,
            // 30 Mud Bricks, 50 Stone, 20 Heavy Stone, 15 Tree Logs, 20 Clay, 10 Rope and 50 nails.
            SpawnMany(PlankGuid, 15, OakPlank);
            SpawnMany(MudBrickGuid, 15);
            SpawnMany(StoneSmallGuid, 20);
            SpawnMany(StoneHeavyGuid, 6);
            SpawnMany(TreeLogGuid, 6, OakLog);
            SpawnMany(ClayGuid, 10, WorkedClay);
            SpawnMany(RopeGuid, 5);
            SpawnMany(CopperNailUid, 20);

            Plugin.Logger.LogDebug("[HomesteadKitPatch] Homestead contents spawned.");
        }

        private static void SpawnMany(string uid, int quantity, Dictionary<string, float> stats = null)
        {
            for (int i = 0; i < quantity; i++)
                SpawnService.Spawn(uid, stats);
        }
    }
}
