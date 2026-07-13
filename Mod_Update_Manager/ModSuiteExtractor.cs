using System;
using System.IO;
using System.Reflection;
using BepInEx;

namespace mod_update_manager
{
    /// <summary>
    /// Extracts an embedded suite ZIP into BepInEx/plugins/&lt;FolderName&gt;/.
    /// Performs a clean-then-extract to avoid orphaned files, preserves the
    /// framework's SpriteCache, and guards against ZIP path traversal.
    /// </summary>
    public static class ModSuiteExtractor
    {
        private static readonly string PluginsPath = Path.Combine(Paths.BepInExRootPath, "plugins");

        public static bool Extract(SuiteModEntry entry, Action<string> status)
        {
            try
            {
                var targetDir = Path.GetFullPath(Path.Combine(PluginsPath, entry.FolderName));

                // Safety: ensure target is inside plugins/
                if (!targetDir.StartsWith(Path.GetFullPath(PluginsPath), StringComparison.OrdinalIgnoreCase))
                {
                    Plugin.Logger.LogError($"ExtractSuiteMod: path traversal detected for {entry.FolderName}, aborting.");
                    return false;
                }

                // Wipe the folder, but preserve SpriteCache/ (framework only; expensive to rebuild)
                if (Directory.Exists(targetDir))
                {
                    status?.Invoke($"Cleaning {entry.FolderName}...");
                    foreach (var file in Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories))
                    {
                        // Skip anything inside a SpriteCache directory
                        if (file.IndexOf(Path.DirectorySeparatorChar + "SpriteCache" + Path.DirectorySeparatorChar,
                                         StringComparison.OrdinalIgnoreCase) >= 0)
                            continue;
                        try { File.Delete(file); } catch { /* file may be briefly locked; non-fatal */ }
                    }
                }
                else
                {
                    Directory.CreateDirectory(targetDir);
                }

                // Load embedded ZIP
                var asm = Assembly.GetExecutingAssembly();
                using (var stream = asm.GetManifestResourceStream(entry.ResourceKey))
                {
                    if (stream == null)
                    {
                        status?.Invoke($"ERROR: embedded resource not found for {entry.FolderName}");
                        return false;
                    }

                    status?.Invoke($"Extracting {entry.DisplayName}...");
                    var zipEntries = MiniZip.ReadEntries(stream);
                    foreach (var zipEntry in zipEntries)
                    {
                        // Guard against path traversal inside the ZIP
                        var destPath = Path.GetFullPath(Path.Combine(targetDir, zipEntry.Name));
                        if (!destPath.StartsWith(targetDir, StringComparison.OrdinalIgnoreCase))
                        {
                            Plugin.Logger.LogWarning($"Skipping unsafe ZIP entry: {zipEntry.Name}");
                            continue;
                        }

                        if (zipEntry.Name.EndsWith("/") || zipEntry.Name.EndsWith("\\"))
                        {
                            Directory.CreateDirectory(destPath);
                        }
                        else
                        {
                            var dir = Path.GetDirectoryName(destPath);
                            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                            MiniZip.ExtractToFile(stream, zipEntry, destPath);
                        }
                    }
                }

                status?.Invoke($"{entry.DisplayName} installed successfully.");
                Plugin.Logger.LogInfo($"Suite: installed {entry.FolderName} v{entry.EmbeddedVersion}");
                return true;
            }
            catch (Exception ex)
            {
                status?.Invoke($"FAILED: {entry.DisplayName} — {ex.Message}");
                Plugin.Logger.LogError($"ModSuiteExtractor: {entry.FolderName}: {ex}");
                return false;
            }
        }
    }
}
