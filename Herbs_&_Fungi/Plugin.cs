using BepInEx;
using BepInEx.Logging;
using CSFFModFramework.Api;
using HarmonyLib;

namespace Herbs_And_Fungi;

[BepInDependency("crispywhips.CSFFModFramework", BepInDependency.DependencyFlags.SoftDependency)]
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
internal class Plugin : ContentModPlugin
{
    private const string PluginGuid = "crispywhips.Herbs_And_Fungi";
    public const string PluginName = "Herbs and Fungi";
    public const string PluginVersion = "1.13.0";

    internal new static ManualLogSource Logger { get; private set; }
    internal static Plugin Instance { get; private set; }

    internal static BepInEx.Configuration.ConfigEntry<float> ForageDropDensityScale { get; private set; }

    protected override void OnModAwake()
    {
        Instance = this;

        // Set up logger for static access
        Logger = base.Logger;

        ForageDropDensityScale = Config.Bind(
            "Performance",
            "ForageDropDensityScale",
            1.0f,
            "Multiplier applied to all HerbsAndFungi-injected forage/dig drop chances (0.25-1.0 recommended). Lower this instead of uninstalling the mod if forage variety feels too dense.");
    }

    protected override void RegisterPatches(Harmony harmony)
    {
        TryApply("GameLoadPatch", () => Herbs_And_Fungi.Patcher.GameLoadPatch.ApplyPatch(harmony));
        TryApply("PickleVatRoutePatch", () => Herbs_And_Fungi.Patcher.PickleVatRoutePatch.ApplyPatch(harmony));
        TryApply("ApothecaryQuestGatePatch", () => Herbs_And_Fungi.Patcher.ApothecaryQuestGatePatch.ApplyPatch(harmony));
        // Forest Scout perk gating moved to WorldMap/MapNodes.json SealableGates (framework
        // CSFFModFramework.Injection.SealableGateService) — HFForestGatePatch.cs retired.
    }
}