using BepInEx;
using BepInEx.Logging;
using CSFFModFramework.Api;
using HarmonyLib;
using HarshWilds.Patcher;

namespace HarshWilds;

[BepInDependency("crispywhips.CSFFModFramework", BepInDependency.DependencyFlags.SoftDependency)]
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
internal class Plugin : ContentModPlugin
{
    private const string PluginGuid = "crispywhips.harsh_wilds";
    public const string PluginName = "Harsh Wilds";
    public const string PluginVersion = "0.11.2";

    internal new static ManualLogSource Logger { get; private set; }
    internal static Plugin Instance { get; private set; }

    protected override void OnModAwake()
    {
        Instance = this;
        Logger = base.Logger;
        Switchboard.Bind(Config);
    }

    protected override void RegisterPatches(Harmony harmony)
    {
        // One [Threats] config switch per threat, mirrored into a hidden GameStat the threat's
        // content gates on (Documentation/Design/Harsh_Wilds_As_Built.md section 2).
        TryApply("Switchboard", () => Switchboard.ApplyPatch(harmony));
        // The bandit's theft is checked against its safety contract as the game loaded it, and removed
        // if it does not hold (Documentation/Research/HarshWilds/bandits.md section 5).
        TryApply("BanditTheftGuard", () => BanditTheftGuard.ApplyPatch(harmony));
        // The crop blight's damage is checked the same way: its crop allowlist must have resolved, or the
        // remote durability edit would read as "every card" (events_stats.md section 5b).
        TryApply("BlightSpreadGuard", () => BlightSpreadGuard.ApplyPatch(harmony));
        // The heron's take is checked the same way: its pond list must have resolved to exactly the two
        // fishponds, which it does not when Water-Driven Infrastructure is absent, or the remote durability
        // edit would read as "every card" (plan section 8).
        TryApply("HeronRaidGuard", () => HeronRaidGuard.ApplyPatch(harmony));
        // The smithing burn (plan section 20) and the foraging bites (section 21) hook the player's own actions through
        // the framework's ActionRouter, registered once per process here (never from a per-run event).
        TryApply("SmithingBurnHook", () => SmithingBurnHook.RegisterOnce());
        // The athame's incense rite actions name a sibling mod's lit incense card in a condition; those whose card did not
        // resolve (the mod is absent) are removed at load (plan section 26).
        TryApply("IncenseRiteGuard", () => IncenseRiteGuard.ApplyPatch(harmony));
    }
}
