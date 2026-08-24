using CSFFModFramework.Api;

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

            SpawnMany(PlankGuid, 30);
            SpawnMany(MudBrickGuid, 30);
            SpawnMany(StoneSmallGuid, 50);
            SpawnMany(StoneHeavyGuid, 20);
            SpawnMany(TreeLogGuid, 15);
            SpawnMany(ClayGuid, 20);
            SpawnMany(RopeGuid, 10);
            SpawnMany(MetalNuggetGuid, 20);

            Plugin.Logger.LogDebug("[HomesteadKitPatch] Homestead contents spawned.");
        }

        private static void SpawnMany(string uid, int quantity)
        {
            for (int i = 0; i < quantity; i++)
                SpawnService.Spawn(uid);
        }
    }
}
