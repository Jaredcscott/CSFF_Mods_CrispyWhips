using BepInEx;
using BepInEx.Logging;
using CSFFModFramework.Api;
using FarTerritory.Patcher;
using HarmonyLib;

namespace FarTerritory;

/// <summary>
/// Far Territory host plugin. The map nodes, boards and cards are declarative JSON the framework loads
/// (<c>WorldMap/MapNodes.json</c>, <c>CardData/</c>); the only code is the run-start pre-create of each
/// non-clone board's save entry (<see cref="EnvSaveDataPreCreate"/>), the soft Forest Lore landmark
/// (<see cref="LandmarkSeed"/>), the guard keeping Sirus23's Wild Owl off every Far Territory board
/// (<see cref="OwlIslandGuard"/>) and the card-free climate of its lands (<see cref="ClimateService"/>). <see cref="ContentModPlugin"/> prints the
/// mod's one startup Info line ("{PluginName} v{PluginVersion} loaded.", CLAUDE.md "Mod Logging Norms"), so
/// this class adds none. Plan: Documentation/Design/FarTerritory_As_Built.md.
/// </summary>
[BepInDependency("crispywhips.CSFFModFramework", BepInDependency.DependencyFlags.SoftDependency)]
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
internal class Plugin : ContentModPlugin
{
    private const string PluginGuid = "crispywhips.far_territory";
    public const string PluginName = "Far Territory";
    public const string PluginVersion = "0.1.5";

    /// <summary>This mod's BepInEx logger, for the static patch classes.</summary>
    internal static ManualLogSource ModLog;

    protected override void OnModAwake()
    {
        ModLog = Logger;
    }

    protected override void RegisterPatches(Harmony harmony)
    {
        TryApply("EnvSaveDataPreCreate", EnvSaveDataPreCreate.Initialize);
        TryApply("LandmarkSeed", LandmarkSeed.Initialize);
        TryApply("OwlIslandGuard", () => OwlIslandGuard.Initialize(harmony));
        TryApply("ClimateService", ClimateService.Initialize);
    }
}
