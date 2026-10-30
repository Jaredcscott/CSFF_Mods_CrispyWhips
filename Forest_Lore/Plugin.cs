using BepInEx;
using CSFFModFramework.Api;
using HarmonyLib;

namespace ForestLore;

/// <summary>
/// Forest Lore host plugin. The mod ships one Harmony patch, <see cref="HomesteadLoadKeepPatch"/> (0.1.9): a save
/// that stored more than the Homestead's 4000 weight (a 0.1.4 Homestead held 10000) keeps every stored card inside it
/// on load. Everything else is declarative JSON: its places, cards, climbing aids and its
/// <c>GameSourceModify/</c> patches are all declarative JSON the framework loads
/// (<c>WorldMap/MapNodes.json</c>, <c>CardData/</c>, <c>InjectImprovementInto.json</c>, <c>GameSourceModify/</c>).
/// The patches: two append a drop or a drop-chance modifier to a vanilla card (the Peat Bog's Dig Peat Bog Body
/// find, a Climbing Aid drop-chance modifier on The Shaft's Climb Up success), two give the art-less vanilla Rusty Axe and Blunt Rusty Axe a
/// picture, and two edit this mod's own clone locations in place (no Clay from the Abandoned Homestead's Clear
/// actions, 2 Wet Peat Turf per dig at the Mud Springs).
/// <see cref="ContentModPlugin"/> prints the mod's one startup Info line
/// ("{PluginName} v{PluginVersion} loaded.", CLAUDE.md "Mod Logging Norms"), so this class adds none.
/// Plan: Documentation/Design/ForestLore_As_Built.md.
/// </summary>
[BepInDependency("crispywhips.CSFFModFramework", BepInDependency.DependencyFlags.SoftDependency)]
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
internal class Plugin : ContentModPlugin
{
    private const string PluginGuid = "crispywhips.forest_lore";
    public const string PluginName = "Forest Lore";
    public const string PluginVersion = "0.1.9";

    protected override void RegisterPatches(Harmony harmony)
    {
        TryApply("HomesteadLoadKeepPatch", () => HomesteadLoadKeepPatch.ApplyPatch(harmony));
    }
}
