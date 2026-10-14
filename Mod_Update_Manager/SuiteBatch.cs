using System;
using System.Collections.Generic;

namespace mod_update_manager
{
    /// <summary>
    /// One row the Install and Update tab's Apply left as installed, and why.
    /// </summary>
    public sealed class SuiteSkippedRow
    {
        public SuiteModEntry Entry { get; }
        public string Reason { get; }

        public SuiteSkippedRow(SuiteModEntry entry, string reason)
        {
            Entry = entry;
            Reason = reason;
        }
    }

    /// <summary>
    /// The batch rules behind the suite tab's Apply, lifted out of UpdateManagerUI.ApplySuiteUpdates
    /// so a gate can drive them: which selected rows are applied and in what order, which are left
    /// as installed and why, and which of the applied rows failed.
    ///
    /// Plan decides, the caller extracts each row of Rows and tells Record how it went. Record keeps
    /// the done line's two counts and the failed set that the restart banner's "Retry failed" button
    /// selects again.
    ///
    /// Round 2 feature 11: Plan also adds the rows an applied row depends on (SuiteDependencies.Resolve:
    /// a hard dependency on another suite row, or the framework, that is missing or older than its
    /// bundled copy), each listed in Added with the rows that need it, and lists in Warnings a required
    /// row the player cleared. The failure policy lives here too: before extracting each row the caller
    /// asks BlockedReason, which says to leave a row alone once the framework has failed (nothing
    /// further is touched) or once a row it hard-depends on has not installed.
    ///
    /// Host-free: no BepInEx or UnityEngine reference. Development_Tools/Tests/MUM-CompiledLogic.Tests.ps1
    /// compiles and drives it.
    /// </summary>
    public sealed class SuiteBatch
    {
        private readonly HashSet<string> _failed = new HashSet<string>();
        private readonly HashSet<string> _blocked = new HashSet<string>();
        private List<SuiteModEntry> _entries = new List<SuiteModEntry>();

        /// <summary>The rows to extract, in the order to extract them: the framework first, then registry order.</summary>
        public List<SuiteModEntry> Rows { get; } = new List<SuiteModEntry>();

        /// <summary>The selected rows Apply leaves as installed, each with its reason.</summary>
        public List<SuiteSkippedRow> Skipped { get; } = new List<SuiteSkippedRow>();

        /// <summary>The rows in Rows that nobody ticked: each is installed because the rows listed with it need it.</summary>
        public List<SuiteRequiredRow> Added { get; } = new List<SuiteRequiredRow>();

        /// <summary>A required row the player cleared, with the row that goes ahead without it.</summary>
        public List<SuiteDependencyWarning> Warnings { get; } = new List<SuiteDependencyWarning>();

        /// <summary>Rows Record was told succeeded.</summary>
        public int Ok { get; private set; }

        /// <summary>Rows Record was told failed.</summary>
        public int Fail { get; private set; }

        /// <summary>The folders of the rows Record was told failed.</summary>
        public IReadOnlyCollection<string> Failed => _failed;

        /// <summary>
        /// The folders the restart banner's "Retry failed" button selects again: the rows that failed
        /// and the rows Block left alone because a row they need did not install, which would otherwise
        /// stay unticked (and unreachable) until the game restarts.
        /// </summary>
        public IReadOnlyCollection<string> Retry
        {
            get
            {
                var folders = new List<string>(_failed);
                folders.AddRange(_blocked);
                return folders;
            }
        }

        /// <summary>True once the framework row failed: nothing after it is touched.</summary>
        public bool Stopped => _failed.Contains(SuiteDependencies.FrameworkFolder);

        /// <summary>
        /// Why a row of Rows must not be extracted now, or null to go ahead. Ask before each row. A failed
        /// framework stops the batch with nothing further touched; any other failed (or held back) row
        /// holds back every row that hard-depends on it, found by the GUIDs the DLLs declare.
        /// </summary>
        public string BlockedReason(SuiteModEntry entry)
        {
            if (Stopped)
                return "the framework failed to install, so nothing further was touched";

            foreach (var needed in SuiteDependencies.HardDependenciesOf(_entries, entry))
            {
                if (_failed.Contains(needed.FolderName) || _blocked.Contains(needed.FolderName))
                    return $"{needed.DisplayName} did not install and {entry.DisplayName} needs it";
            }
            return null;
        }

        /// <summary>Leaves a row alone for a reason BlockedReason gave: it is listed in Skipped and kept for Retry.</summary>
        public void Block(SuiteModEntry entry, string reason)
        {
            _blocked.Add(entry.FolderName);
            Skipped.Add(new SuiteSkippedRow(entry, reason));
        }

        /// <summary>
        /// The folders "Select Out of Date / Not Installed" ticks: the rows that are out of date or not
        /// installed. A row installed under another folder is neither (it reads [Installed elsewhere]).
        /// </summary>
        public static List<string> SelectOutOfDate(IEnumerable<SuiteModEntry> entries)
        {
            var folders = new List<string>();
            foreach (var e in entries)
                if (e.Status == SuiteInstallStatus.OutOfDate || e.Status == SuiteInstallStatus.NotInstalled)
                    folders.Add(e.FolderName);
            return folders;
        }

        /// <summary>
        /// The folders "Select All" ticks: every row except one installed under another folder, which
        /// Apply would only skip.
        /// </summary>
        public static List<string> SelectAll(IEnumerable<SuiteModEntry> entries)
        {
            var folders = new List<string>();
            foreach (var e in entries)
                if (e.Status != SuiteInstallStatus.InstalledElsewhere)
                    folders.Add(e.FolderName);
            return folders;
        }

        /// <summary>Plan with no row cleared: nothing the player has declined to install.</summary>
        public static SuiteBatch Plan(IEnumerable<SuiteModEntry> entries, ICollection<string> selected, Func<SuiteModEntry, string> installedVersionOf)
            => Plan(entries, selected, installedVersionOf, null);

        /// <param name="entries">The registry, in registry order. Enumerated more than once, so pass a list.</param>
        /// <param name="selected">The FolderNames the player ticked.</param>
        /// <param name="installedVersionOf">A row's installed version, or null when it is not installed.</param>
        /// <param name="declined">The FolderNames of required rows the player cleared (null for none). A ticked row is never declined.</param>
        public static SuiteBatch Plan(IEnumerable<SuiteModEntry> entries, ICollection<string> selected, Func<SuiteModEntry, string> installedVersionOf, ICollection<string> declined)
        {
            var batch = new SuiteBatch();

            // Framework must be first if selected
            var ordered = new List<SuiteModEntry>();
            foreach (var e in entries)
                if (selected.Contains(e.FolderName) && e.FolderName == "CSFF_Mod_Framework")
                    ordered.Add(e);
            foreach (var e in entries)
                if (selected.Contains(e.FolderName) && e.FolderName != "CSFF_Mod_Framework")
                    ordered.Add(e);

            // Apply never moves a mod backwards, and never wipes one it cannot replace. "Select All"
            // and the row checkboxes can pick any row, including one whose installed copy is NEWER
            // than this MUM's embed (a player who took a hotfix from Nexus after the suite was
            // packed: the row reads [Up to Date]) and one whose embed could not be read ([Unknown]:
            // the extract would fail). Both are left as installed and counted in the done line.
            // A same-version row still reinstalls, which is the repair path. A row whose mod is already
            // installed under another folder ([Installed elsewhere]) is skipped too: installing it here
            // would put a second copy beside that one.
            foreach (var entry in ordered)
            {
                var installedNow = installedVersionOf(entry);
                string skipReason = null;
                if (entry.Status == SuiteInstallStatus.Unknown)
                    skipReason = "its bundled copy could not be read";
                else if (entry.Status == SuiteInstallStatus.InstalledElsewhere)
                    skipReason = $"already installed in {entry.InstalledElsewherePath}, so installing here would add a second copy";
                else if (installedNow != null && VersionComparer.Compare(entry.EmbeddedVersion, installedNow) < 0)
                    skipReason = $"installed {installedNow} is newer than bundled {entry.EmbeddedVersion}";

                if (skipReason != null)
                {
                    batch.Skipped.Add(new SuiteSkippedRow(entry, skipReason));
                    continue;
                }

                batch.Rows.Add(entry);
            }

            // The rows an applied row needs (feature 11). Only a row that will really be installed asks
            // for anything: a ticked row skipped above pulls nothing in. The rows added are installed
            // in the same order as the rest, and the registry is kept for BlockedReason.
            var registry = new List<SuiteModEntry>(entries);
            var needs = SuiteDependencies.Resolve(registry, batch.Rows, installedVersionOf, declined);
            foreach (var required in needs.Added)
            {
                batch.Added.Add(required);
                batch.Rows.Add(required.Entry);
            }
            if (needs.Added.Count > 0)
                SuiteDependencies.SortForApply(registry, batch.Rows);
            batch.Warnings.AddRange(needs.Warnings);
            batch._entries = registry;

            return batch;
        }

        /// <summary>Records how one row of Rows went. A folder is recorded once per batch.</summary>
        public void Record(string folder, bool succeeded)
        {
            if (succeeded)
            {
                Ok++;
                return;
            }

            Fail++;
            _failed.Add(folder);
        }
    }
}
