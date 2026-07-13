using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using CSFFModFramework.Api;
using HarmonyLib;

namespace Advanced_Copper_Tools;

[BepInDependency("crispywhips.CSFFModFramework", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("crispywhips.Herbs_And_Fungi", BepInDependency.DependencyFlags.SoftDependency)]
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
internal class Plugin : ContentModPlugin
{
    private const string PluginGuid = "crispywhips.advanced_copper_tools";
    public const string PluginName = "Advanced_Copper_Tools";
    public const string PluginVersion = "1.11.7";

    internal new static ManualLogSource Logger { get; private set; }
    internal static Plugin Instance { get; private set; }
    internal static ConfigEntry<bool> EnableLegacyStationLiquidHeater { get; private set; }

    protected override void OnModAwake()
    {
        Instance = this;

        // Set up logger for static access
        Logger = base.Logger;
        EnableLegacyStationLiquidHeater = Config.Bind(
            "Compatibility",
            "EnableLegacyStationLiquidHeater",
            false,
            "Enables the old beta fallback that directly heats liquid stored on the lit Tea Station card. Leave disabled for current Tea Station reservoir behavior.");
    }

    protected override void RegisterPatches(Harmony harmony)
    {
        TryApply("SawEffectPatch", () => Advanced_Copper_Tools.Patcher.SawEffectPatch.ApplyPatch(harmony));
        TryApply("TeaStationPatch", () => Advanced_Copper_Tools.Patcher.TeaStationPatch.ApplyPatch(harmony));
        TryApply("GameLoadPatch", () => Advanced_Copper_Tools.Patcher.GameLoadPatch.ApplyPatch(harmony));
        TryApply("IronNailSmeltPatch", () => Advanced_Copper_Tools.Patcher.IronNailSmeltPatch.ApplyPatch(harmony));
        TryApply("IronVeinQualityPatch", () => Advanced_Copper_Tools.Patcher.IronVeinQualityPatch.ApplyPatch(harmony));
        // Cave Prospector perk gating moved to WorldMap/MapNodes.json SealableGates (framework
        // CSFFModFramework.Injection.SealableGateService) — ACTCaveGatePatch.cs retired.
        if (EnableLegacyStationLiquidHeater.Value)
        {
            TryApply("HeatHeldLiquidPatch", () => Advanced_Copper_Tools.Patcher.HeatHeldLiquidPatch.ApplyPatch(harmony));
        }
        else
        {
            Logger.LogDebug("[HeatHeldLiquid] legacy station liquid heater disabled; Tea Station uses its reservoir stats and Draw Boiled Water fix.");
        }
    }

    protected override void OnModDestroy()
    {
        Instance = null;
        Logger = null;
    }
}
