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
    public const string PluginVersion = "1.2.4";

    internal new static ManualLogSource Logger { get; private set; }
    internal static Plugin Instance { get; private set; }

    protected override void OnModAwake()
    {
        Instance = this;
        Logger = base.Logger;
    }

    protected override void RegisterPatches(Harmony harmony)
    {
        // Homestead Kit unpacks into the Cabin Kit, two Rain Cistern Kits, and the full
        // material stockpile on Place, instead of carrying it all from character creation.
        TryApply("HomesteadKitPatch", () => HomesteadKitPatch.Initialize());
    }
}
