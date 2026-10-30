using BepInEx;
using BepInEx.Logging;
using CSFFModFramework.Api;
using HarmonyLib;
using HomesteadPerks.Patcher;

namespace HomesteadPerks;

[BepInDependency("crispywhips.CSFFModFramework", BepInDependency.DependencyFlags.SoftDependency)]
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
internal class Plugin : ContentModPlugin
{
    private const string PluginGuid = "crispywhips.homestead_perks";
    public const string PluginName = "Homestead Perks";
    public const string PluginVersion = "1.3.10";

    internal new static ManualLogSource Logger { get; private set; }
    internal static Plugin Instance { get; private set; }

    protected override void OnModAwake()
    {
        Instance = this;
        Logger = base.Logger;
    }

    protected override void RegisterPatches(Harmony harmony)
    {
        // The Homestead Kit's unpack (Cabin Kit, two Rain Cistern Kits and the stockpile, on Place) is
        // KitContents.json, read by CSFF Mod Framework 2.29.0+ (was Patcher/HomesteadKitPatch.cs).
        // Path Kit: refuse a Build Path press that would spend a kit for nothing (no route, or built).
        TryApply("PathKitPatch", () => PathKitPatch.Initialize());
    }
}
