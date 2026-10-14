// Host-free: no BepInEx or UnityEngine reference. Development_Tools/Tests/MUM-CompiledLogic.Tests.ps1
// compiles this file (with SuiteModRegistry, PluginMetadataReader, MiniZip and VersionComparer) and
// drives it, on made-up declarations and on the DLLs inside the real embedded ZIPs.
using System;
using System.Collections.Generic;
using System.IO;

namespace mod_update_manager
{
    /// <summary>
    /// A row Apply installs beside the ones the player ticked, because a row it applies depends on it.
    /// </summary>
    public sealed class SuiteRequiredRow
    {
        public SuiteModEntry Entry { get; }

        /// <summary>The rows that need it, in registry order.</summary>
        public List<SuiteModEntry> RequiredBy { get; } = new List<SuiteModEntry>();

        public SuiteRequiredRow(SuiteModEntry entry)
        {
            Entry = entry;
        }
    }

    /// <summary>
    /// The player cleared a row another row needs, and Apply goes ahead without it. IsHard is whether
    /// the dependent declares it as a HARD dependency, which BepInEx enforces by refusing to load the
    /// dependent; a soft one (the framework's) leaves it loading with some features inactive.
    /// </summary>
    public sealed class SuiteDependencyWarning
    {
        public SuiteModEntry Dependent { get; }
        public SuiteModEntry Required { get; }
        public bool IsHard { get; }

        public SuiteDependencyWarning(SuiteModEntry dependent, SuiteModEntry required, bool isHard)
        {
            Dependent = dependent;
            Required = required;
            IsHard = isHard;
        }
    }

    /// <summary>What SuiteDependencies.Resolve found for a set of rows an Apply is going to install.</summary>
    public sealed class SuiteRequirements
    {
        /// <summary>The rows to install as well, framework first, then registry order.</summary>
        public List<SuiteRequiredRow> Added { get; } = new List<SuiteRequiredRow>();

        /// <summary>A required row the player cleared, with the row that needs it.</summary>
        public List<SuiteDependencyWarning> Warnings { get; } = new List<SuiteDependencyWarning>();
    }

    /// <summary>
    /// The installed framework is older than the one this bundle was packed with while content mods
    /// are as new as their bundled copies.
    /// </summary>
    public sealed class SuiteSkew
    {
        public string FrameworkInstalled { get; }
        public string FrameworkBundled { get; }

        /// <summary>How many content mods are installed at or above their bundled version.</summary>
        public int ModCount { get; }

        public SuiteSkew(string installed, string bundled, int modCount)
        {
            FrameworkInstalled = installed;
            FrameworkBundled = bundled;
            ModCount = modCount;
        }
    }

    /// <summary>
    /// Round 2 plan feature 11: what each suite mod needs, read from the DLLs and not typed by hand.
    ///
    /// ReadEmbedded reads every plugin DLL inside a row's embedded ZIP in memory
    /// (PluginMetadataReader.Read(byte[], string)): the [BepInPlugin] GUIDs it registers and the
    /// [BepInDependency] declarations it carries. A declaration names another row when that row's own
    /// DLL registers the GUID (FindByGuid), so Community Mod Chest's hard dependency on
    /// crispywhips.advanced_copper_tools is found because Advanced Copper Tools' DLL registers that
    /// GUID, and no list of who needs whom exists in this mod.
    ///
    /// Resolve says which rows an Apply must install as well: a row an applied row HARD-depends on, or
    /// the framework, which most of the suite declares softly but cannot do without, when that row is
    /// not installed or is older than its bundled copy. A GUID no row registers (a third-party mod, or
    /// BepInEx itself) is never selected, and a soft dependency on a sibling is not a requirement.
    ///
    /// Host-free.
    /// </summary>
    public static class SuiteDependencies
    {
        /// <summary>The framework's row. Apply installs it first, and a failure of it stops the batch.</summary>
        public const string FrameworkFolder = "CSFF_Mod_Framework";

        /// <summary>
        /// Reads the plugin DLLs inside one embedded ZIP into the row's EmbeddedPluginGuids and
        /// EmbeddedDependencies. A ZIP that cannot be read leaves both null (nothing is required of the
        /// row, as before this feature) and says so in the log; a DLL that cannot be read is named in
        /// the log and the rest are still read.
        /// </summary>
        public static void ReadEmbedded(SuiteModEntry entry, Stream zip)
        {
            try
            {
                var guids = new List<string>();
                var deps = new List<DeclaredDependency>();
                foreach (var zipEntry in MiniZip.ReadEntries(zip))
                {
                    if (!zipEntry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;

                    var label = entry.FolderName + "/" + zipEntry.Name;
                    PluginMetadata meta;
                    try
                    {
                        meta = PluginMetadataReader.Read(MiniZip.ExtractBytes(zip, zipEntry), label);
                    }
                    catch (Exception ex)
                    {
                        MumLog.Warning($"SuiteDependencies: could not read {label} ({ex.GetType().Name}: {ex.Message}); what it declares is not known.");
                        continue;
                    }
                    if (meta == null) continue;

                    foreach (var guid in meta.PluginGuids)
                        if (!guids.Contains(guid)) guids.Add(guid);
                    deps.AddRange(meta.Dependencies);
                }
                entry.EmbeddedPluginGuids = guids;
                entry.EmbeddedDependencies = deps;
            }
            catch (Exception ex)
            {
                entry.EmbeddedPluginGuids = null;
                entry.EmbeddedDependencies = null;
                MumLog.Warning($"SuiteDependencies: could not read the plugin DLLs of {entry.FolderName} ({ex.GetType().Name}: {ex.Message}); nothing is known to depend on it or to be needed by it.");
            }
        }

        /// <summary>The row whose own DLL registers guid, or null (a third-party mod, BepInEx itself, or a ZIP not read).</summary>
        public static SuiteModEntry FindByGuid(IEnumerable<SuiteModEntry> entries, string guid)
        {
            if (string.IsNullOrEmpty(guid) || entries == null) return null;
            foreach (var e in entries)
            {
                if (e.EmbeddedPluginGuids == null) continue;
                foreach (var own in e.EmbeddedPluginGuids)
                    if (string.Equals(own, guid, StringComparison.OrdinalIgnoreCase)) return e;
            }
            return null;
        }

        /// <summary>
        /// The suite rows entry declares a HARD dependency on. The failure policy reads this: a row whose
        /// hard dependency did not install is not applied.
        /// </summary>
        public static List<SuiteModEntry> HardDependenciesOf(IEnumerable<SuiteModEntry> entries, SuiteModEntry entry)
        {
            var found = new List<SuiteModEntry>();
            if (entry == null || entry.EmbeddedDependencies == null) return found;
            foreach (var dep in entry.EmbeddedDependencies)
            {
                if (!dep.IsHard) continue;
                var target = FindByGuid(entries, dep.Guid);
                if (target != null && !ReferenceEquals(target, entry) && !found.Contains(target)) found.Add(target);
            }
            return found;
        }

        /// <summary>
        /// The rows an Apply of <paramref name="applied"/> must install as well, and a warning for each
        /// such row <paramref name="declined"/> (the folders the player cleared) keeps out. A row that is
        /// added is followed in turn, so a row pulled in for its own dependencies pulls those in too.
        /// </summary>
        /// <param name="entries">The registry, in registry order.</param>
        /// <param name="applied">The rows that are going to be installed: ticked, and not skipped.</param>
        /// <param name="installedVersionOf">A row's installed version, or null when it is not installed.</param>
        public static SuiteRequirements Resolve(IList<SuiteModEntry> entries, IEnumerable<SuiteModEntry> applied, Func<SuiteModEntry, string> installedVersionOf, ICollection<string> declined)
        {
            var result = new SuiteRequirements();
            var inSet = new HashSet<string>();
            var queue = new Queue<SuiteModEntry>();
            foreach (var row in applied)
                if (inSet.Add(row.FolderName)) queue.Enqueue(row);

            var addedByFolder = new Dictionary<string, SuiteRequiredRow>();
            var warned = new HashSet<string>();

            while (queue.Count > 0)
            {
                var dependent = queue.Dequeue();
                if (dependent.EmbeddedDependencies == null) continue;

                foreach (var dep in dependent.EmbeddedDependencies)
                {
                    var target = FindByGuid(entries, dep.Guid);
                    if (target == null || ReferenceEquals(target, dependent)) continue;

                    // A hard dependency is one BepInEx enforces. A soft one is optional, except the
                    // framework: the content mods declare it softly so they still load without it, and
                    // then do nothing, so it is installed with them.
                    bool isFramework = target.FolderName == FrameworkFolder;
                    if (!dep.IsHard && !isFramework) continue;

                    if (inSet.Contains(target.FolderName))
                    {
                        if (addedByFolder.TryGetValue(target.FolderName, out var already) && !already.RequiredBy.Contains(dependent))
                            already.RequiredBy.Add(dependent);
                        continue;
                    }

                    var installed = installedVersionOf(target);
                    if (!NeedsInstall(target, installed)) continue;

                    if (declined != null && declined.Contains(target.FolderName))
                    {
                        // Only a row that is missing leaves the dependent without it: an older copy
                        // still satisfies a GUID dependency, so BepInEx loads the dependent as before.
                        if (installed == null && warned.Add(dependent.FolderName + "|" + target.FolderName))
                            result.Warnings.Add(new SuiteDependencyWarning(dependent, target, dep.IsHard));
                        continue;
                    }

                    var added = new SuiteRequiredRow(target);
                    added.RequiredBy.Add(dependent);
                    addedByFolder[target.FolderName] = added;
                    result.Added.Add(added);
                    inSet.Add(target.FolderName);
                    queue.Enqueue(target);
                }
            }

            result.Added.Sort((a, b) => ApplyRank(entries, a.Entry).CompareTo(ApplyRank(entries, b.Entry)));
            foreach (var added in result.Added)
                added.RequiredBy.Sort((a, b) => ApplyRank(entries, a).CompareTo(ApplyRank(entries, b)));
            return result;
        }

        /// <summary>Puts rows in the order Apply installs them: the framework first, then registry order.</summary>
        public static void SortForApply(IList<SuiteModEntry> entries, List<SuiteModEntry> rows)
        {
            rows.Sort((a, b) => ApplyRank(entries, a).CompareTo(ApplyRank(entries, b)));
        }

        // A row that could be installed and is missing or older than its bundled copy. A copy installed
        // under another folder satisfies the dependency (BepInEx loads it), and a bundled copy that could
        // not be read cannot be installed, so neither is added.
        private static bool NeedsInstall(SuiteModEntry target, string installed)
        {
            if (target.Status == SuiteInstallStatus.Unknown || target.Status == SuiteInstallStatus.InstalledElsewhere) return false;
            return installed == null || VersionComparer.Compare(target.EmbeddedVersion, installed) > 0;
        }

        private static int ApplyRank(IList<SuiteModEntry> entries, SuiteModEntry row)
        {
            return row.FolderName == FrameworkFolder ? -1 : entries.IndexOf(row);
        }

        /// <summary>
        /// The skew line: the framework installed is older than the one this bundle was packed with
        /// (a row reading [Update Available]) while at least one content mod is installed at or above
        /// its own bundled version (a row reading [Up to Date]), or null when that is not so. Read from
        /// the versions the rows already carry, so it needs no network.
        /// </summary>
        public static SuiteSkew FrameworkSkew(IEnumerable<SuiteModEntry> entries)
        {
            SuiteModEntry framework = null;
            int mods = 0;
            foreach (var e in entries)
            {
                if (e.FolderName == FrameworkFolder) framework = e;
                else if (e.Category == "Content" && e.Status == SuiteInstallStatus.UpToDate) mods++;
            }
            if (framework == null || framework.Status != SuiteInstallStatus.OutOfDate || mods == 0) return null;
            return new SuiteSkew(framework.InstalledVersion, framework.EmbeddedVersion, mods);
        }
    }
}
