namespace Utility_Belt;

/// <summary>
/// One row per module Utility Belt will carry. Each module keeps its standalone mod's GUID (plan 1.1,
/// option b), so BepInEx loads exactly one copy of it: the newer of Utility Belt's bundled DLL and the
/// standalone's own. In the scaffold nothing is bundled yet; later phases flip <see cref="Module.Bundled"/>
/// one module at a time.
/// </summary>
internal static class ModuleRegistry
{
    internal sealed class Module
    {
        public readonly string Guid;
        public readonly string DisplayName;
        public readonly string BundledDll;
        public readonly bool Bundled;

        public Module(string guid, string displayName, string bundledDll, bool bundled)
        {
            Guid = guid;
            DisplayName = displayName;
            BundledDll = bundledDll;
            Bundled = bundled;
        }
    }

    internal static readonly Module[] Modules =
    {
        new Module("crispywhips.quick_transfer", "Quick Transfer", "Quick_Transfer.dll", true),
        new Module("crispywhips.repeat_action", "Repeat Action", "Repeat_Action.dll", true),
        new Module("crispywhips.skill_speed_boost", "Skill Speed Boost", "Skill_Speed_Boost.dll", true),
    };

    /// <summary>
    /// Logs, at Info, which file each module's plugin loaded from. Public BepInEx API only: no reflection.
    /// The caller gates it behind the Diagnostics option (diagnostics default off).
    /// </summary>
    internal static void LogSources(ManualLogSource log)
    {
        foreach (var module in Modules)
        {
            if (Chainloader.PluginInfos.TryGetValue(module.Guid, out var info))
            {
                log.LogInfo($"{module.DisplayName} {info.Metadata.Version} from {RelativeToPluginPath(info.Location)}");
            }
            else
            {
                log.LogInfo($"{module.DisplayName}: not loaded");
            }
        }
    }

    // Net 4.8 has no Path.GetRelativePath. A location outside BepInEx/plugins is reported in full.
    private static string RelativeToPluginPath(string location)
    {
        if (string.IsNullOrEmpty(location))
        {
            return "(unknown location)";
        }

        var root = Paths.PluginPath;
        if (!string.IsNullOrEmpty(root)
            && location.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            && location.Length > root.Length
            && (location[root.Length] == Path.DirectorySeparatorChar || location[root.Length] == Path.AltDirectorySeparatorChar))
        {
            return location.Substring(root.Length + 1);
        }

        return location;
    }
}
