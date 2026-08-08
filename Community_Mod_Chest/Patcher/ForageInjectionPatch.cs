using System;
using BepInEx.Logging;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Raises the Village CT8's <c>MaxWeightCapacity</c> so the player can place significantly
    /// more structures there.
    ///
    /// <para>Forage drop injection (Village Farm/Foraging Forest) and the Foraging Forest's
    /// SpecialDurability3 capacity override moved to declarative
    /// <c>DropInjections.json</c>/<c>WorldMap/MapNodes.json CapacityStats</c> 2026-07-02
    /// (framework <c>CSFFModFramework.Injection.DropInjector</c>/<c>EnvCapacityPatcher</c>) —
    /// <c>MaxWeightCapacity</c> has no declarative equivalent (not one of the SD1-4 fields
    /// <c>CapacityStats</c> covers), so it stays here.
    /// </para>
    /// </summary>
    internal static class ForageInjectionPatch
    {
        private static ManualLogSource Logger => Plugin.Logger;
        private const string VillageLocUid = "cmcLocVillage";
        private const float AddedCapacity = 25000f;

        public static void Register()
        {
            FrameworkEvents.GameDataReady += OnGameDataReady;
        }

        private static void OnGameDataReady()
        {
            try
            {
                var villageLoc = CardUtil.GetCardDataById(VillageLocUid);
                if (villageLoc != null)
                    PatchVillageCapacity(villageLoc);
                else
                    Logger.LogWarning("[CMCForage] Village location not found — skipping capacity patch.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[CMCForage] {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // Adds 25000 to the Village CT8 card's MaxWeightCapacity so the player can place
        // significantly more structures there. InGameCardBase.InventoryFull checks
        // InventoryWeight >= MaxWeightCapacity, so increasing this directly raises the build limit.
        static void PatchVillageCapacity(object locCard)
        {
            try
            {
                var current = Reflect.GetFloat(locCard, "MaxWeightCapacity", float.NaN);
                if (float.IsNaN(current))
                {
                    Logger.LogWarning("[CMCForage] Village: MaxWeightCapacity field not found — capacity patch skipped.");
                    return;
                }
                Reflect.SetMember(locCard, "MaxWeightCapacity", current + AddedCapacity);
                Logger.LogInfo($"[CMCForage] Village: MaxWeightCapacity {current} -> {current + AddedCapacity}.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[CMCForage] PatchVillageCapacity: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }
    }
}
