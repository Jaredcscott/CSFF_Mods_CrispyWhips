using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace WaterDrivenInfrastructure;

[BepInDependency("crispywhips.CSFFModFramework", BepInDependency.DependencyFlags.SoftDependency)]
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
internal class Plugin : BaseUnityPlugin
{
    private const string PluginGuid = "crispywhips.waterdriveninfrastructure";
    public const string PluginName = "WaterDrivenInfrastructure";
    public const string PluginVersion = "1.12.8";

    internal new static ManualLogSource Logger;
    internal static Plugin Instance { get; private set; }
    internal static ConfigEntry<bool> WinterStationLock { get; private set; }
    private static Harmony _harmony;

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }

    private void Awake()
    {
        Instance = this;

        Logger = base.Logger;

        WinterStationLock = Config.Bind(
            "Seasons",
            "Winter Station Lock",
            false,
            "When true, water-powered stations (sawmill, grinding mill, forge, workshop, ore sluice, fishpond) stop working in winter at every location, even beside a river, while their water is frozen; a heat source at the same location lets them run (a lit Campfire, Fire Pit, Fireplace, Oven, Kiln, Furnace, Bloomery or Forge, a lit Copper Brazier from Advanced Copper Tools, or a Water-Driven Forge or Workshop at least half hot). When false (the default) only a frozen Mill Race Outlet stops a station in winter, and stations at a river or lake keep working. A change here takes effect after a full game relaunch, not a save reload.");

        _harmony = new Harmony(PluginGuid);
        try
        {
            Patcher.GameLoadPatch.ApplyPatch(_harmony);
            Patcher.MillRaceNetwork.ApplyPatch(_harmony);
            Patcher.ActionInterceptPatch.ApplyPatch(_harmony);
            Patcher.FishpondPopulationPatch.ApplyPatch(_harmony);
            Patcher.MillDutyPatch.ApplyPatch(_harmony);
            Patcher.BathtubPlumbingPatch.ApplyPatch(_harmony);
            Patcher.BronzeGearTabPatch.ApplyPatch(_harmony);
            Patcher.BaseTemperatureHeal.Initialize();
Logger.LogInfo($"{PluginName} v{PluginVersion} loaded.");
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Failed to apply Harmony patches: {ex}");
        }
    }
}
