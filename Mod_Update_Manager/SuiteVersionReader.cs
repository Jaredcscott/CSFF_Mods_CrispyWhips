using System;
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

        /// <summary>
        /// Reads the version string from ModInfo.json inside the embedded ZIP.
        /// Returns null if the resource is not found or ModInfo.json is missing from the archive.
        /// </summary>
        public static string ReadEmbeddedVersion(SuiteModEntry entry)
        {
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
                    var json = System.Text.Encoding.UTF8.GetString(bytes);
                    var parsed = SimpleJson.DeserializeModInfo(json);
                    return parsed?.Version;
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogDebug($"SuiteVersionReader: failed reading embedded version for {entry.FolderName}: {ex.Message}");
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
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Populates EmbeddedVersion, InstalledVersion, and Status on every registry entry.
        /// Call once at startup from Plugin.Awake.
        /// </summary>
        public static void RefreshAll()
        {
            foreach (var entry in SuiteModRegistry.All)
            {
                entry.EmbeddedVersion = ReadEmbeddedVersion(entry) ?? "—";
                entry.InstalledVersion = ReadInstalledVersion(entry) ?? "Not installed";

                if (entry.EmbeddedVersion == "—")
                    entry.Status = SuiteInstallStatus.Unknown;
                else if (entry.InstalledVersion == "Not installed")
                    entry.Status = SuiteInstallStatus.NotInstalled;
                else if (VersionComparer.Compare(entry.EmbeddedVersion, entry.InstalledVersion) > 0)
                    entry.Status = SuiteInstallStatus.OutOfDate;
                else
                    entry.Status = SuiteInstallStatus.UpToDate;
            }
        }
    }
}
