using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;

namespace mod_update_manager
{
    /// <summary>
    /// One declared [BepInDependency] whose target GUID is not present in the installed set.
    /// </summary>
    public class DependencyIssue
    {
        /// <summary>Display name of the plugin that declares the dependency (folder\file.dll).</summary>
        public string DependentName { get; set; }

        /// <summary>The declaring plugin's own [BepInPlugin] GUID, or null if it declares none.</summary>
        public string DependentGuid { get; set; }

        /// <summary>The GUID that was asked for and is not installed.</summary>
        public string MissingGuid { get; set; }

        /// <summary>True for a hard dependency: BepInEx will refuse to load the dependent plugin.</summary>
        public bool IsHard { get; set; }

        /// <summary>Minimum version the declaration asked for, or null.</summary>
        public string MinimumVersion { get; set; }
    }

    /// <summary>
    /// Cross-references every declared [BepInDependency] in the BepInEx tree against the set of
    /// [BepInPlugin] GUIDs actually installed, and reports the ones that cannot be satisfied
    /// (N6, Documentation/Design/Mod_Update_Manager_Audit_Remediation_As_Built.md).
    ///
    /// The installed set is MEASURED, not allowlisted: core/ and patchers/ are scanned for
    /// [BepInPlugin] GUIDs alongside plugins/, so a GUID provided by BepInEx itself would count as
    /// present without anyone maintaining a hardcoded list. (On the reference install, 2026-09-08,
    /// core/ contributes zero plugin GUIDs - the summary line reports the count so that stays
    /// observable rather than assumed.)
    ///
    /// Results are CACHED. DrawConflicts runs every OnGUI pass, and this does disk I/O, so it must
    /// never be recomputed per frame. UpdateChecker.ScanMods invalidates the cache.
    /// </summary>
    public static class DependencyValidator
    {
        private static List<DependencyIssue> _cache;
        private static bool _computed;

        /// <summary>Human-readable outcome of the last scan; null until one has run.</summary>
        public static string LastScanSummary { get; private set; }

        /// <summary>Drops the cached result so the next read rescans the BepInEx tree.</summary>
        public static void Invalidate()
        {
            _computed = false;
            _cache = null;
        }

        /// <summary>
        /// Missing hard dependencies first, then soft, each alphabetical. Never null, never throws.
        /// </summary>
        public static List<DependencyIssue> GetIssues()
        {
            if (!_computed)
            {
                try
                {
                    _cache = Compute();
                }
                catch (Exception ex)
                {
                    // A failure here must degrade to "nothing to report", never break the tab.
                    try { Plugin.Logger?.LogWarning($"Dependency validation failed: {ex.Message}"); } catch { }
                    _cache = new List<DependencyIssue>();
                    LastScanSummary = "Dependency scan failed - see the log.";
                }
                _computed = true;
            }
            return _cache;
        }

        private static List<DependencyIssue> Compute()
        {
            var root = Paths.BepInExRootPath;
            var installedGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var declarations = new List<Tuple<string, string, DeclaredDependency>>();

            int scanned = 0, unreadable = 0, coreGuids = 0, declaringDlls = 0;

            // plugins/: contributes both installed GUIDs and dependency declarations.
            foreach (var dll in EnumerateDlls(Path.Combine(root, "plugins")))
            {
                scanned++;
                var meta = PluginMetadataReader.Read(dll);
                if (meta == null) { unreadable++; continue; }

                foreach (var g in meta.PluginGuids) installedGuids.Add(g);
                if (meta.Dependencies.Count == 0) continue;

                declaringDlls++;
                var display = DisplayName(root, dll);
                var ownGuid = meta.PluginGuids.Count > 0 ? meta.PluginGuids[0] : null;
                foreach (var dep in meta.Dependencies)
                    declarations.Add(Tuple.Create(display, ownGuid, dep));
            }

            // core/ and patchers/: GUID providers only. A plugin depending on something BepInEx
            // itself supplies must not be flagged, and this measures that rather than assuming it.
            foreach (var sub in new[] { "core", "patchers" })
            {
                foreach (var dll in EnumerateDlls(Path.Combine(root, sub)))
                {
                    scanned++;
                    var meta = PluginMetadataReader.Read(dll);
                    if (meta == null) { unreadable++; continue; }
                    foreach (var g in meta.PluginGuids)
                    {
                        if (installedGuids.Add(g)) coreGuids++;
                    }
                }
            }

            var issues = new List<DependencyIssue>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in declarations)
            {
                var dep = d.Item3;
                if (installedGuids.Contains(dep.Guid)) continue;

                // One row per (dependent, missing GUID) pair: a plugin that declares the same
                // dependency twice is a duplicate, not two problems.
                var key = d.Item1 + "\0" + dep.Guid;
                if (!seen.Add(key)) continue;

                issues.Add(new DependencyIssue
                {
                    DependentName = d.Item1,
                    DependentGuid = d.Item2,
                    MissingGuid = dep.Guid,
                    IsHard = dep.IsHard,
                    MinimumVersion = dep.MinimumVersion
                });
            }

            issues = issues
                .OrderByDescending(i => i.IsHard)
                .ThenBy(i => i.DependentName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => i.MissingGuid, StringComparer.OrdinalIgnoreCase)
                .ToList();

            LastScanSummary =
                $"Dependency scan: {scanned} DLL(s) read ({unreadable} unreadable), " +
                $"{installedGuids.Count} plugin GUID(s) installed ({coreGuids} from core/patchers), " +
                $"{declarations.Count} dependency declaration(s) across {declaringDlls} plugin(s), " +
                $"{issues.Count} unsatisfied.";

            // Info, not Debug: when nothing is missing the UI renders nothing, so this line is the
            // only way to tell "scan ran and found everything satisfied" from "scan never ran".
            // Once per scan (first Conflicts view, then only after a rescan), not per frame.
            try { Plugin.Logger?.LogInfo(LastScanSummary); } catch { }

            return issues;
        }

        private static IEnumerable<string> EnumerateDlls(string dir)
        {
            string[] files;
            try
            {
                if (!Directory.Exists(dir)) return new string[0];
                files = Directory.GetFiles(dir, "*.dll", SearchOption.AllDirectories);
            }
            catch (Exception ex)
            {
                try { Plugin.Logger?.LogDebug($"Dependency scan could not enumerate '{dir}': {ex.Message}"); } catch { }
                return new string[0];
            }
            return files;
        }

        private static string DisplayName(string root, string dllPath)
        {
            try
            {
                var pluginsRoot = Path.Combine(root, "plugins");
                if (dllPath.StartsWith(pluginsRoot, StringComparison.OrdinalIgnoreCase))
                    return dllPath.Substring(pluginsRoot.Length).TrimStart('\\', '/');
            }
            catch { }
            return Path.GetFileName(dllPath);
        }
    }
}
