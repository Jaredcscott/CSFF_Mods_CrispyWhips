using System;
using System.Collections.Generic;
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
                entry.LastInstallIncomplete = false;
                var targetDir = Path.GetFullPath(Path.Combine(PluginsPath, entry.FolderName));

                // Safety: ensure target is inside plugins/
                if (!IsUnder(targetDir, Path.GetFullPath(PluginsPath)))
                {
                    Plugin.Logger.LogError($"ExtractSuiteMod: path traversal detected for {entry.FolderName}, aborting.");
                    return false;
                }

                // Load embedded ZIP
                var asm = Assembly.GetExecutingAssembly();
                using (var stream = asm.GetManifestResourceStream(entry.ResourceKey))
                {
                    if (stream == null)
                    {
                        Plugin.Logger.LogError($"ModSuiteExtractor: embedded resource {entry.ResourceKey} not found; {entry.FolderName} left as installed.");
                        status?.Invoke($"ERROR: embedded resource not found for {entry.FolderName}");
                        return false;
                    }

                    // Read and check the WHOLE archive before the wipe below. The wipe deletes the
                    // working install, so an archive that cannot be extracted (truncated, or packed
                    // with Deflate, which MiniZip cannot read and which .NET wrote on 2026-07-11 per
                    // Pack-Suite.ps1) must fail while that install is still on disk. Validate throws
                    // on the first such entry, into the catch below.
                    var zipEntries = MiniZip.ReadEntries(stream);
                    var planned = new List<KeyValuePair<MiniZip.Entry, string>>();
                    foreach (var zipEntry in zipEntries)
                    {
                        // Guard against path traversal inside the ZIP
                        var destPath = Path.GetFullPath(Path.Combine(targetDir, zipEntry.Name));
                        if (!IsUnder(destPath, targetDir))
                        {
                            Plugin.Logger.LogWarning($"Skipping unsafe ZIP entry: {zipEntry.Name}");
                            continue;
                        }

                        if (!IsDirectoryEntry(zipEntry))
                            MiniZip.Validate(stream, zipEntry);
                        planned.Add(new KeyValuePair<MiniZip.Entry, string>(zipEntry, destPath));
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
                            try { File.Delete(file); }
                            catch (Exception ex) { Plugin.Logger.LogWarning($"Suite wipe: could not delete {file} (locked?); may orphan: {ex.Message}"); }
                        }
                    }
                    else
                    {
                        Directory.CreateDirectory(targetDir);
                    }

                    status?.Invoke($"Extracting {entry.DisplayName}...");
                    foreach (var item in planned)
                    {
                        if (IsDirectoryEntry(item.Key))
                        {
                            Directory.CreateDirectory(item.Value);
                        }
                        else
                        {
                            var dir = Path.GetDirectoryName(item.Value);
                            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                            MiniZip.ExtractToFile(stream, item.Key, item.Value);
                        }
                    }

                    // Post-extract verification: confirm every file entry actually landed on disk at
                    // its full length. Mirrors the deploy-verify "deployed count == source count"
                    // discipline - a partial write can leave a destination file short or absent with
                    // nothing thrown (a locked destination the OS still accepted the open call for, a
                    // full disk mid-copy), and this is the only pass that would ever notice. Entries
                    // are Stored, so UncompressedSize is the exact expected length.
                    var missingPaths = new List<string>();
                    foreach (var item in planned)
                    {
                        if (IsDirectoryEntry(item.Key))
                            continue; // directory entry, nothing to verify

                        var landed = new FileInfo(item.Value);
                        if (!landed.Exists || landed.Length != item.Key.UncompressedSize)
                            missingPaths.Add(item.Key.Name);
                    }

                    if (missingPaths.Count > 0)
                    {
                        entry.LastInstallIncomplete = true;
                        foreach (var missing in missingPaths)
                            Plugin.Logger.LogWarning($"Suite verify: {entry.FolderName} missing or short file after extract: {missing}");
                        Plugin.Logger.LogError($"Suite verify: {entry.FolderName} install incomplete, {missingPaths.Count} file(s) missing or short.");
                        status?.Invoke($"[Install Incomplete] {entry.DisplayName}: {missingPaths.Count} file(s) missing after extract.");
                        return false;
                    }
                }

                entry.LastInstallIncomplete = false;
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

        // Strictly inside root. The trailing separator is what stops a sibling such as
        // "plugins/CSFF_Mod_Framework_old" passing as a prefix of "plugins/CSFF_Mod_Framework".
        private static bool IsUnder(string fullPath, string rootFullPath)
        {
            var root = rootFullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                       + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDirectoryEntry(MiniZip.Entry zipEntry) =>
            zipEntry.Name.EndsWith("/") || zipEntry.Name.EndsWith("\\");
    }
}
