using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;

namespace mod_update_manager
{
    /// <summary>
    /// Reads mod versions from embedded suite ZIPs (in-memory) and from installed
    /// plugin folders on disk, then classifies each suite mod's install status.
    /// </summary>
    public static class SuiteVersionReader
    {
        private static readonly string PluginsPath = Path.Combine(Paths.BepInExRootPath, "plugins");
        // Where SuiteInstaller keeps the copies the "keep the previous version" option holds
        // (kept/<FolderName>/<version>/), beside plugins/ and so outside every loader's scan.
        private static readonly string WorkRootPath = Path.Combine(Paths.BepInExRootPath, SuiteInstaller.WorkRootDirName);

        /// <summary>
        /// Reads ModInfo.json inside the embedded ZIP once and returns what it says (Name, Version,
        /// Description), with its text in modInfoText for SuiteDescription.Resolve.
        /// Returns null (and a null modInfoText) if the resource is not found or ModInfo.json is
        /// missing from the archive.
        /// </summary>
        public static ModInfoJson ReadEmbeddedInfo(SuiteModEntry entry, out string modInfoText)
        {
            modInfoText = null;
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using (var stream = asm.GetManifestResourceStream(entry.ResourceKey))
                {
                    if (stream == null) return null;
                    var entries = MiniZip.ReadEntries(stream);
                    var modInfoEntry = entries.Find(e => e.Name == "ModInfo.json");
                    if (modInfoEntry == null) return null;
                    var bytes = MiniZip.ExtractBytes(stream, modInfoEntry);
                    modInfoText = System.Text.Encoding.UTF8.GetString(bytes);
                    return SimpleJson.DeserializeModInfo(modInfoText);
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"SuiteVersionReader: failed reading embedded ModInfo.json for {entry.FolderName}: {ex.Message}");
                modInfoText = null;
                return null;
            }
        }

        /// <summary>
        /// Reads the version from the locally installed ModInfo.json in BepInEx/plugins/.
        /// Returns null if the mod is not installed.
        /// </summary>
        public static string ReadInstalledVersion(SuiteModEntry entry)
        {
            var path = Path.Combine(PluginsPath, entry.FolderName, "ModInfo.json");
            if (!File.Exists(path)) return null;
            try
            {
                var json = File.ReadAllText(path);
                var parsed = SimpleJson.DeserializeModInfo(json);
                return parsed?.Version;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"SuiteVersionReader: failed reading installed version for {entry.FolderName}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Reads what the plugin DLLs inside the embedded ZIP declare (their [BepInPlugin] GUIDs and
        /// [BepInDependency] declarations) into the entry, once: the embed does not change while the
        /// game runs, so a row that has been read is not read again. A row whose ZIP could not be read
        /// stays null and is tried again on the next refresh.
        /// </summary>
        private static void ReadEmbeddedDeclarations(SuiteModEntry entry)
        {
            if (entry.EmbeddedPluginGuids != null) return;
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using (var stream = asm.GetManifestResourceStream(entry.ResourceKey))
                {
                    if (stream == null) return;
                    SuiteDependencies.ReadEmbedded(entry, stream);
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"SuiteVersionReader: failed reading the embedded plugin DLLs for {entry.FolderName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Populates EmbeddedVersion, EmbeddedName, EmbeddedModInfoJson, the declared plugin GUIDs and
        /// dependencies, InstalledVersion, Status,
        /// InstalledElsewherePath and the kept versions on every registry entry. Plugin.Awake calls it once with no scan (the
        /// rows are read from the DLL and plugins/&lt;FolderName&gt; alone); Plugin.OnGameDataLoaded and
        /// the Refresh Mod List button call it again after a scan of plugins/, passing the scan, which
        /// is what shows a suite mod installed under another folder (SuiteInstalledElsewhere).
        /// </summary>
        public static void RefreshAll(IEnumerable<InstalledModInfo> scanned = null)
        {
            var locations = ToLocations(scanned);
            foreach (var entry in SuiteModRegistry.All)
            {
                var info = ReadEmbeddedInfo(entry, out var modInfoText);
                entry.EmbeddedVersion = info?.Version ?? "—";
                entry.EmbeddedName = info?.Name;
                entry.EmbeddedModInfoJson = modInfoText;
                ReadEmbeddedDeclarations(entry);

                var installed = ReadInstalledVersion(entry);
                var elsewhere = SuiteInstalledElsewhere.Find(entry.EmbeddedName, PluginsPath, entry.FolderName, locations);
                entry.Status = SuiteInstalledElsewhere.Classify(info?.Version, installed, elsewhere);
                var isElsewhere = entry.Status == SuiteInstallStatus.InstalledElsewhere;
                entry.InstalledElsewherePath = isElsewhere ? elsewhere.FolderPath : null;
                entry.InstalledVersion = installed ?? (isElsewhere ? (elsewhere.Version ?? "Unknown") : "Not installed");

                // The versions the keep option holds for this row, a new list each time (the tab draws
                // the old one while a rollback refreshes this).
                entry.KeptVersions = SuiteInstaller.ListKeptVersions(PluginsPath, WorkRootPath, entry.FolderName);
            }
        }

        // ModScanner's results as the host-free shape SuiteInstalledElsewhere reads.
        private static List<InstalledLocation> ToLocations(IEnumerable<InstalledModInfo> scanned)
        {
            var list = new List<InstalledLocation>();
            if (scanned == null) return list;
            foreach (var m in scanned)
                list.Add(new InstalledLocation { Name = m.Name, FolderPath = m.FolderPath, Version = m.Version, HasModInfo = m.HasModInfo });
            return list;
        }
    }
}
