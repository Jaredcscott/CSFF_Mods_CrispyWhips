using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;

namespace Advanced_Copper_Tools;

[BepInDependency("crispywhips.CSFFModFramework", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("crispywhips.Herbs_And_Fungi", BepInDependency.DependencyFlags.SoftDependency)]
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
internal class Plugin : BaseUnityPlugin
{
    private const string PluginGuid = "crispywhips.advanced_copper_tools";
    public const string PluginName = "Advanced_Copper_Tools";
    public const string PluginVersion = "1.7.7";

    internal new static ManualLogSource Logger;
    internal static Plugin Instance { get; private set; }
    internal static ConfigEntry<bool> EnableLegacyStationLiquidHeater { get; private set; }
    private static Harmony _harmony;

    private void Awake()
    {
        Instance = this;

        // Set up logger for static access
        Logger = base.Logger;
        EnableLegacyStationLiquidHeater = Config.Bind(
            "Compatibility",
            "EnableLegacyStationLiquidHeater",
            false,
            "Enables the old beta fallback that directly heats liquid stored on the lit Tea Station card. Leave disabled for current Tea Station reservoir behavior.");

        // Initialize and apply Harmony patches
        _harmony = new Harmony(PluginGuid);
        try
        {
            Advanced_Copper_Tools.Patcher.SawEffectPatch.ApplyPatch(_harmony);
            Advanced_Copper_Tools.Patcher.TeaStationPatch.ApplyPatch(_harmony);
            Advanced_Copper_Tools.Patcher.GameLoadPatch.ApplyPatch(_harmony);
            if (EnableLegacyStationLiquidHeater.Value)
            {
                Advanced_Copper_Tools.Patcher.HeatHeldLiquidPatch.ApplyPatch(_harmony);
            }
            else
            {
                Logger.LogDebug("[HeatHeldLiquid] legacy station liquid heater disabled; Tea Station uses its reservoir stats and Draw Boiled Water fix.");
            }
            Logger.LogInfo($"{PluginName} v{PluginVersion} loaded.");
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Failed to apply Harmony patches: {ex}");
        }
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }
}
