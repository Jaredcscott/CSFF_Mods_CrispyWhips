using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using BepInEx;

namespace mod_update_manager
{
    /// <summary>
    /// Represents information about an installed mod
    /// </summary>
    public class InstalledModInfo
    {
        public string FolderName { get; set; }
        public string FolderPath { get; set; }
        public string Name { get; set; }
        public string Author { get; set; }
        public string Version { get; set; }
        public string NexusModId { get; set; }  // Optional: can be specified in ModInfo.json
        public bool HasModInfo { get; set; }
        
        // Version comparison results (populated after Nexus check)
        public string LatestVersion { get; set; }
        public bool NeedsUpdate { get; set; }
        public bool CheckFailed { get; set; }
        public string CheckError { get; set; }
        public string NexusUrl { get; set; }  // Nexus page URL for this mod
    }

    /// <summary>
    /// ModInfo.json structure (extended to support NexusModId)
    /// </summary>
    public class ModInfoJson
    {
        public string Name { get; set; }
        public string Author { get; set; }
        public string Version { get; set; }
        public string NexusModId { get; set; }  // Optional field for Nexus integration
    }

    /// <summary>
    /// Scans the BepInEx plugins folder for installed mods
    /// </summary>
    public static class ModScanner
    {
        private static string PluginsPath => Path.Combine(Paths.BepInExRootPath, "plugins");

        /// <summary>
        /// Scans all mod folders in BepInEx/plugins and returns mod information
        /// </summary>
        public static List<InstalledModInfo> ScanInstalledMods()
        {
            var mods = new List<InstalledModInfo>();

            try
            {
                if (!Directory.Exists(PluginsPath))
                {
                    Plugin.Logger.LogWarning($"Plugins path not found: {PluginsPath}");
                    return mods;
                }

                var directories = Directory.GetDirectories(PluginsPath);
                Plugin.Logger.LogDebug($"Found {directories.Length} folders in plugins directory");

                foreach (var dir in directories)
                {
                    var modInfo = ScanModFolder(dir);
                    if (modInfo != null)
                    {
                        mods.Add(modInfo);
                    }
                }

                // Also scan for loose DLLs in the plugins root (mods without a subfolder)
                var looseDlls = Directory.GetFiles(PluginsPath, "*.dll", SearchOption.TopDirectoryOnly);
                foreach (var dllPath in looseDlls)
                {
                    var dllName = Path.GetFileNameWithoutExtension(dllPath);

                    // Skip known non-mod DLLs
                    if (dllName.Equals("LitJSON", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Skip if we already found this mod in a subfolder
                    if (mods.Exists(m => m.Name.Equals(dllName, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    mods.Add(new InstalledModInfo
                    {
                        FolderName = dllName,
                        FolderPath = PluginsPath,
                        Name = dllName,
                        Author = "Unknown",
                        Version = "Unknown",
                        HasModInfo = false
                    });

                    Plugin.Logger.LogDebug($"Found loose DLL mod: {dllName}");
                }

                Plugin.Logger.LogDebug($"Successfully scanned {mods.Count} mods");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"Error scanning mods: {ex.Message}");
            }

            return mods;
        }

        /// <summary>
        /// Scans a single mod folder for ModInfo.json
        /// </summary>
        private static InstalledModInfo ScanModFolder(string folderPath)
        {
            var folderName = Path.GetFileName(folderPath);
            var modInfoPath = Path.Combine(folderPath, "ModInfo.json");

            var modInfo = new InstalledModInfo
            {
                FolderName = folderName,
                FolderPath = folderPath,
                HasModInfo = false
            };

            // Try to read ModInfo.json
            if (File.Exists(modInfoPath))
            {
                try
                {
                    var jsonContent = File.ReadAllText(modInfoPath);
                    var modInfoJson = SimpleJson.DeserializeModInfo(jsonContent);

                    if (modInfoJson != null)
                    {
                        modInfo.Name = modInfoJson.Name ?? folderName;
                        modInfo.Author = modInfoJson.Author ?? "Unknown";
                        modInfo.Version = modInfoJson.Version ?? "Unknown";
                        modInfo.NexusModId = modInfoJson.NexusModId;
                        modInfo.HasModInfo = true;

                        Plugin.Logger.LogDebug($"Found mod: {modInfo.Name} v{modInfo.Version} by {modInfo.Author}");
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogWarning($"Failed to parse ModInfo.json in {folderName}: {ex.Message}");
                    modInfo.Name = folderName;
                    modInfo.Version = "Parse Error";
                }
            }
            else
            {
                // No ModInfo.json - try to detect from DLL or folder name
                modInfo.Name = folderName;
                modInfo.Version = "Unknown";
                modInfo.Author = "Unknown";
                
                // Check for any DLLs to confirm it's a mod
                var dlls = Directory.GetFiles(folderPath, "*.dll", SearchOption.TopDirectoryOnly);
                if (dlls.Length == 0)
                {
                    // Check subdirectories
                    dlls = Directory.GetFiles(folderPath, "*.dll", SearchOption.AllDirectories);
                }
                
                if (dlls.Length == 0)
                {
                    Plugin.Logger.LogDebug($"Skipping {folderName} - no DLL found");
                    return null; // Not a mod folder
                }

                Plugin.Logger.LogDebug($"Found mod without ModInfo.json: {folderName}");
            }

            return modInfo;
        }

        public static string GetGameVersion()
        {
            try
            {
                var version = UnityEngine.Application.version;
                if (!string.IsNullOrEmpty(version))
                    return version;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"Could not get game version: {ex.Message}");
            }

            return "Unknown";
        }
    }
}
