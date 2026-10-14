using BepInEx;
using CSFFModFramework.Api;

namespace ForestLore;

/// <summary>
/// Forest Lore host plugin. The mod ships no Harmony patches and no C#: its places, cards, climbing aids and the
/// one append-only <c>GameSourceModify</c> patch are all declarative JSON the framework loads
/// (<c>WorldMap/MapNodes.json</c>, <c>CardData/</c>, <c>InjectImprovementInto.json</c>, <c>GameSourceModify/</c>).
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
    public const string PluginVersion = "0.1.4";
}
