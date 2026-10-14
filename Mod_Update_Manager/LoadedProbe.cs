using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;

namespace mod_update_manager
{
    /// <summary>
    /// Reads the two facts LoadedStatus compares. BepInEx's own plugin list is complete by the time the window
    /// opens (every plugin loads at startup). The framework's ModRegistry is reached by reflection, by assembly
    /// name, so Mod Update Manager keeps no reference to the framework and no [BepInDependency]; a missing
    /// framework or a changed shape answers null (unknown), never an invented "not loaded". It is read at
    /// window time and never from a load-time postfix, because ModRegistry.All logs a warning when asked
    /// before the framework finished loading.
    /// </summary>
    public static class LoadedProbe
    {
        private static bool _warnedBepInEx;
        private static bool _warnedFramework;

        /// <summary>The plugins BepInEx loaded, in the order its list enumerates, or null when it cannot be read.</summary>
        public static List<LoadedPlugin> ReadPlugins()
        {
            try
            {
                var infos = Chainloader.PluginInfos;
                if (infos == null) return null;
                var list = new List<LoadedPlugin>();
                foreach (var kv in infos)
                {
                    var info = kv.Value;
                    if (info == null) continue;
                    list.Add(new LoadedPlugin
                    {
                        Guid = info.Metadata != null ? info.Metadata.GUID : kv.Key,
                        Name = info.Metadata != null ? info.Metadata.Name : null,
                        Version = info.Metadata != null && info.Metadata.Version != null ? info.Metadata.Version.ToString() : "",
                        Location = info.Location
                    });
                }
                return list;
            }
            catch (Exception ex)
            {
                if (!_warnedBepInEx)
                {
                    _warnedBepInEx = true;
                    MumLog.Warning($"Load status: BepInEx's plugin list could not be read, so rows show unknown: {ex.Message}");
                }
                return null;
            }
        }

        /// <summary>The folders the framework loaded mod data from, or null when there is no framework or its
        /// registry could not be read.</summary>
        public static List<string> ReadFrameworkDirs()
        {
            try
            {
                var asm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, "CSFFModFramework", StringComparison.Ordinal));
                if (asm == null) return null; // no framework installed: not a failure, nothing to warn about

                var type = asm.GetType("CSFFModFramework.Api.ModRegistry", false);
                var all = type?.GetProperty("All", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null) as IEnumerable;
                if (all == null) return WarnFramework("ModRegistry.All was not found");

                var dirs = new List<string>();
                PropertyInfo dirProp = null;
                foreach (var item in all)
                {
                    if (item == null) continue;
                    if (dirProp == null) dirProp = item.GetType().GetProperty("DirectoryPath", BindingFlags.Public | BindingFlags.Instance);
                    if (dirProp == null) return WarnFramework("ModInfo.DirectoryPath was not found");
                    var dir = dirProp.GetValue(item, null) as string;
                    if (!string.IsNullOrEmpty(dir)) dirs.Add(dir);
                }
                return dirs;
            }
            catch (Exception ex)
            {
                return WarnFramework(ex.Message);
            }
        }

        private static List<string> WarnFramework(string why)
        {
            if (!_warnedFramework)
            {
                _warnedFramework = true;
                MumLog.Warning($"Load status: the framework's mod list could not be read ({why}), so framework data rows show nothing.");
            }
            return null;
        }
    }
}
