using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace WaterDrivenInfrastructure;

[BepInDependency("crispywhips.CSFFModFramework", BepInDependency.DependencyFlags.SoftDependency)]
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
internal class Plugin : BaseUnityPlugin
{
    private const string PluginGuid = "crispywhips.waterdriveninfrastructure";
    public const string PluginName = "WaterDrivenInfrastructure";
    public const string PluginVersion = "1.2.2";

    internal new static ManualLogSource Logger;
    internal static Plugin Instance { get; private set; }
    private static Harmony _harmony;

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }

    private void Awake()
    {
        Instance = this;

        Logger = base.Logger;

        _harmony = new Harmony(PluginGuid);
        try
        {
            Patcher.GameLoadPatch.ApplyPatch(_harmony);
            Patcher.ActionInterceptPatch.ApplyPatch(_harmony);
            Patcher.FishpondPopulationPatch.ApplyPatch(_harmony);
            Logger.LogInfo($"{PluginName} v{PluginVersion} loaded.");
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Failed to apply Harmony patches: {ex}");
        }
    }
}
