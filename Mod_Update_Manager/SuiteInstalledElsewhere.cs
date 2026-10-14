// Host-free: no BepInEx or UnityEngine reference. Development_Tools/Tests/MUM-CompiledLogic.Tests.ps1
// compiles this file (with SuiteInstaller, whose folder names it reads, and VersionComparer) and drives it.
using System;
using System.Collections.Generic;
using System.IO;

namespace mod_update_manager
{
    /// <summary>
    /// One mod folder a scan of BepInEx/plugins found: the host-free shape of ModScanner's
    /// InstalledModInfo that SuiteInstalledElsewhere reads, so a gate can build one without BepInEx.
    /// </summary>
    public sealed class InstalledLocation
    {
        /// <summary>The Name in the folder's ModInfo.json (ModScanner falls back to the folder name without one).</summary>
        public string Name { get; set; }
        public string FolderPath { get; set; }
        public string Version { get; set; }
        /// <summary>True when the folder has a ModInfo.json that was read. A folder without one is not a match: the framework does not discover it either.</summary>
        public bool HasModInfo { get; set; }
    }

    /// <summary>
    /// Round 2 plan feature 10: a suite mod that is already installed under a folder other than
    /// BepInEx/plugins/&lt;FolderName&gt; (what extracting a release zip into a folder named after it
    /// makes: plugins/Herbs_And_Fungi_1-14-0/Herbs_And_Fungi) is seen before Apply adds a second copy
    /// beside it. The framework's ModDiscovery keeps one of two same-Name folders by content-file count
    /// while BepInEx loads the newest DLL, so a second copy can mix versions.
    ///
    /// Find matches the bundled ModInfo.json Name against a scan's results by Name, case-insensitively,
    /// as ModDiscovery's own dedup does. It leaves out the canonical folder itself, and every folder of
    /// the installer's own: a staging or backup folder (the suffixes SuiteInstaller uses when it falls
    /// back to the in-plugins layout) and anything under the work root BepInEx/mum_work. Classify is the
    /// one place the suite tab's status is decided, so the precedence is gated.
    ///
    /// Host-free.
    /// </summary>
    public static class SuiteInstalledElsewhere
    {
        /// <summary>
        /// The copy of the mod named embeddedName that is not at pluginsRoot/folderName and is not one of
        /// the installer's own folders, or null when there is none. With several, the path that sorts
        /// first, so the answer does not depend on scan order.
        /// </summary>
        public static InstalledLocation Find(string embeddedName, string pluginsRoot, string folderName, IEnumerable<InstalledLocation> locations)
        {
            if (string.IsNullOrWhiteSpace(embeddedName) || locations == null) return null;

            string canonical = string.IsNullOrWhiteSpace(pluginsRoot) || string.IsNullOrWhiteSpace(folderName)
                ? null
                : FullPath(TrimSeparators(pluginsRoot) + Path.DirectorySeparatorChar + folderName);

            InstalledLocation best = null;
            foreach (var loc in locations)
            {
                if (loc == null || !loc.HasModInfo || string.IsNullOrEmpty(loc.FolderPath)) continue;
                if (!string.Equals(loc.Name, embeddedName, StringComparison.OrdinalIgnoreCase)) continue;
                if (canonical != null && SamePath(loc.FolderPath, canonical)) continue;
                if (InInstallerFolder(pluginsRoot, loc.FolderPath)) continue;
                if (best == null || string.CompareOrdinal(FullPath(loc.FolderPath), FullPath(best.FolderPath)) < 0) best = loc;
            }
            return best;
        }

        /// <summary>
        /// A suite row's status. A bundled copy that could not be read is Unknown. A copy in the row's own
        /// folder decides the rest by version, and wins over one elsewhere (Apply updates the canonical
        /// folder, which adds no second copy). With nothing in the canonical folder, a copy elsewhere is
        /// InstalledElsewhere, and no copy at all is NotInstalled. embeddedVersion and installedVersion
        /// are null when unread or absent.
        /// </summary>
        public static SuiteInstallStatus Classify(string embeddedVersion, string installedVersion, InstalledLocation elsewhere)
        {
            if (embeddedVersion == null) return SuiteInstallStatus.Unknown;
            if (installedVersion == null)
                return elsewhere != null ? SuiteInstallStatus.InstalledElsewhere : SuiteInstallStatus.NotInstalled;
            return VersionComparer.Compare(embeddedVersion, installedVersion) > 0 ? SuiteInstallStatus.OutOfDate : SuiteInstallStatus.UpToDate;
        }

        // Any folder on the path (below plugins/ when it is under it) that is a staging or backup folder
        // of SuiteInstaller's, or its work root. A segment test, not a leaf test: ModScanner also reads
        // one level inside a folder, so a copy nested in plugins/X__mum_old/ has an ordinary leaf name.
        private static bool InInstallerFolder(string pluginsRoot, string path)
        {
            var full = FullPath(path);
            var rel = full;
            if (!string.IsNullOrWhiteSpace(pluginsRoot))
            {
                var root = FullPath(pluginsRoot);
                if (full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    rel = full.Substring(root.Length + 1);
            }
            foreach (var seg in rel.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (seg.EndsWith(SuiteInstaller.StagingSuffix, StringComparison.OrdinalIgnoreCase)
                    || seg.EndsWith(SuiteInstaller.BackupSuffix, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(seg, SuiteInstaller.WorkRootDirName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool SamePath(string a, string b) =>
            string.Equals(FullPath(a), FullPath(b), StringComparison.OrdinalIgnoreCase);

        private static string TrimSeparators(string path) =>
            path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // The path as the OS reads it, without a trailing separator. A path GetFullPath refuses is
        // compared as written, and the Debug line says so.
        private static string FullPath(string path)
        {
            try
            {
                return TrimSeparators(Path.GetFullPath(path));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                MumLog.Debug($"SuiteInstalledElsewhere: a path could not be normalised ({ex.GetType().Name}); it is compared as written.");
                return TrimSeparators(path);
            }
        }
    }
}
