using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;

namespace Advanced_Copper_Tools;

[BepInDependency("crispywhips.CSFFModFramework", "2.0.0")]
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
internal class Plugin : BaseUnityPlugin
{
    private const string PluginGuid = "crispywhips.advanced_copper_tools";
    public const string PluginName = "Advanced_Copper_Tools";
    public const string PluginVersion = "1.7.2";

    internal new static ManualLogSource Logger;
    internal static Plugin Instance { get; private set; }
    private static Harmony _harmony;

    private void Awake()
    {
        Instance = this;

        // Set up logger for static access
        Logger = base.Logger;

        // Initialize and apply Harmony patches
        _harmony = new Harmony(PluginGuid);
        try
        {
            Advanced_Copper_Tools.Patcher.SawEffectPatch.ApplyPatch(_harmony);
            Advanced_Copper_Tools.Patcher.TeaStationPatch.ApplyPatch(_harmony);
            Advanced_Copper_Tools.Patcher.HeatHeldLiquidPatch.ApplyPatch(_harmony);
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
