// Host-free: no BepInEx or UnityEngine reference. Whether BepInEx loaded a plugin from each installed mod's
// folder, whether the framework loaded its data from that folder, and the load-order list (plan feature 21).
// The two facts are read elsewhere (LoadedProbe.cs, by BepInEx's own PluginInfos and by reflection on the
// framework's ModRegistry) and handed in as plain strings, so this file only compares them.
// Development_Tools/Tests/MUM-CompiledLogic.Tests.ps1 compiles this file (with InstalledModInfo) and drives it.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace mod_update_manager
{
    /// <summary>One plugin BepInEx loaded, as PluginInfos lists it.</summary>
    public sealed class LoadedPlugin
    {
        public string Guid;
        public string Name;
        public string Version;
        /// <summary>The absolute path of the plugin's DLL (PluginInfo.Location).</summary>
        public string Location;
    }

    /// <summary>What a fact says about one folder: it was seen, it was not, or the fact could not be read.</summary>
    public enum LoadedFact
    {
        Unknown = 0,
        Yes = 1,
        No = 2
    }

    public sealed class LoadedRow
    {
        public string Folder;
        /// <summary>BepInEx loaded a plugin from this folder.</summary>
        public LoadedFact Plugin;
        /// <summary>The framework loaded this mod's data from this folder. Only a folder that carries a
        /// ModInfo.json can be data the framework loads, so any other folder is No only when the framework
        /// answered, and is never reported as a problem by the UI (see <see cref="NeedsFrameworkNote"/>).</summary>
        public LoadedFact FrameworkData;
        public bool HasModInfo;
    }

    public static class LoadedStatus
    {
        // English-column text of Localization/SimpEn.csv rows Mod_Update_Manager_LoadedYes, _LoadedNo,
        // _LoadedUnknown, _LoadedFrameworkYes, _LoadedFrameworkNo, _LoadOrderHeader, _LoadOrderLine, _LoadOrderNone
        // and _LoadOrderUnknown.
        public static string LoadedYesText => MumText.Get("LoadedYes");
        public static string LoadedNoText => MumText.Get("LoadedNo");
        public static string LoadedUnknownText => MumText.Get("LoadedUnknown");
        public static string FrameworkYesText => MumText.Get("LoadedFrameworkYes");
        public static string FrameworkNoText => MumText.Get("LoadedFrameworkNo");
        public static string LoadOrderHeaderText => MumText.Get("LoadOrderHeader");
        public static string LoadOrderLineFormat => MumText.Get("LoadOrderLine");
        public static string LoadOrderNoneText => MumText.Get("LoadOrderNone");
        public static string LoadOrderUnknownText => MumText.Get("LoadOrderUnknown");

        /// <summary>
        /// One row per installed folder. <paramref name="plugins"/> is null when BepInEx's list could not be
        /// read (every plugin fact is then Unknown, never No); <paramref name="frameworkDirs"/> is null when
        /// the framework is absent or could not be read. An EMPTY list from either is an answer: nothing
        /// loaded.
        /// </summary>
        public static List<LoadedRow> Compute(IEnumerable<InstalledModInfo> folders, IEnumerable<LoadedPlugin> plugins, IEnumerable<string> frameworkDirs)
        {
            var rows = new List<LoadedRow>();
            if (folders == null) return rows;

            var locations = plugins == null ? null : plugins.Select(p => Normalize(p == null ? null : p.Location)).Where(l => l.Length > 0).ToList();
            var dirs = frameworkDirs == null ? null : frameworkDirs.Select(Normalize).Where(d => d.Length > 0).ToList();

            foreach (var mod in folders)
            {
                if (mod == null) continue;
                var folder = Normalize(mod.FolderPath);
                var row = new LoadedRow { Folder = mod.FolderName, HasModInfo = mod.HasModInfo };

                if (folder.Length == 0 || locations == null) row.Plugin = LoadedFact.Unknown;
                else row.Plugin = locations.Any(l => IsInside(l, folder)) ? LoadedFact.Yes : LoadedFact.No;

                if (folder.Length == 0 || dirs == null) row.FrameworkData = LoadedFact.Unknown;
                else row.FrameworkData = dirs.Any(d => string.Equals(d, folder, StringComparison.OrdinalIgnoreCase)) ? LoadedFact.Yes : LoadedFact.No;

                rows.Add(row);
            }
            return rows;
        }

        /// <summary>The line the row shows for BepInEx.</summary>
        public static string PluginText(LoadedRow r)
        {
            if (r == null) return "";
            switch (r.Plugin)
            {
                case LoadedFact.Yes: return LoadedYesText;
                case LoadedFact.No: return LoadedNoText;
                default: return LoadedUnknownText;
            }
        }

        /// <summary>The framework line for a row, or empty when there is nothing to say: a Yes always says
        /// so, a No is said only for a folder that carries a ModInfo.json (a plugin-only mod is not framework
        /// data, so No would be noise), and Unknown says nothing.</summary>
        public static string FrameworkText(LoadedRow r)
        {
            if (r == null) return "";
            if (r.FrameworkData == LoadedFact.Yes) return FrameworkYesText;
            if (r.FrameworkData == LoadedFact.No && r.HasModInfo) return FrameworkNoText;
            return "";
        }

        /// <summary>The load-order lines in the order given (the order BepInEx's list enumerates), numbered
        /// from 1. A null list is "could not be read"; an empty one is "none loaded".</summary>
        public static List<string> OrderLines(IEnumerable<LoadedPlugin> plugins)
        {
            var lines = new List<string>();
            if (plugins == null) { lines.Add(LoadOrderUnknownText); return lines; }
            int n = 0;
            foreach (var p in plugins)
            {
                if (p == null) continue;
                n++;
                lines.Add(string.Format(LoadOrderLineFormat, n, string.IsNullOrEmpty(p.Name) ? p.Guid : p.Name, p.Version, p.Guid));
            }
            if (n == 0) lines.Add(LoadOrderNoneText);
            return lines;
        }

        // Full path, both separators as backslash, no trailing separator. A path that cannot be normalized is empty.
        internal static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            try
            {
                return Path.GetFullPath(path.Trim()).Replace('/', '\\').TrimEnd('\\');
            }
            catch (Exception ex)
            {
                MumLog.Debug($"LoadedStatus: could not normalize the path '{path}', treating it as empty: {ex.Message}");
                return "";
            }
        }

        // True when file is under folder: the folder plus a separator is a prefix of the file, so Foo never matches Foo_Bar.
        private static bool IsInside(string file, string folder)
        {
            return file.StartsWith(folder + "\\", StringComparison.OrdinalIgnoreCase);
        }
    }
}
