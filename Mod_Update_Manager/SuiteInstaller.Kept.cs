// Host-free: no BepInEx or UnityEngine reference. The opt-in "keep the previous version" half of
// SuiteInstaller (the same static class, a second file): Development_Tools/Tests/MUM-CompiledLogic.Tests.ps1
// compiles this file beside SuiteInstaller.cs and drives it against scratch folders.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace mod_update_manager
{
    /// <summary>
    /// The player's "keep the previous version when updating a suite mod" option, as SuiteInstaller
    /// reads it. Off by default and Off here: with it off nothing is ever kept and nothing kept is
    /// ever pruned, so an Apply leaves nothing under BepInEx/mum_work/ but its own empty folders.
    /// </summary>
    public sealed class SuiteKeepPolicy
    {
        public const int MinKeptVersions = 1;
        public const int MaxKeptVersions = 3;
        public const int DefaultKeptVersions = 1;

        /// <summary>The option off: delete the replaced copy, prune nothing.</summary>
        public static readonly SuiteKeepPolicy Off = new SuiteKeepPolicy(false, 0);

        /// <summary>True when the replaced copy is kept instead of deleted.</summary>
        public bool Enabled { get; }

        /// <summary>
        /// How many kept copies of one mod are held, MinKeptVersions to MaxKeptVersions (a hand-edited
        /// config value outside that is clamped); 0 when the option is off.
        /// </summary>
        public int Versions { get; }

        public SuiteKeepPolicy(bool enabled, int versions)
        {
            Enabled = enabled;
            Versions = enabled ? Math.Min(MaxKeptVersions, Math.Max(MinKeptVersions, versions)) : 0;
        }
    }

    /// <summary>
    /// SuiteInstaller's kept copies. With the option on, an Apply moves the replaced folder to
    /// &lt;work root&gt;/kept/&lt;Folder&gt;/&lt;version&gt;/ instead of deleting it (one rename on the same
    /// volume, after CarrySpriteCache has moved the framework's sprite cache to the new copy, so the
    /// cache is never kept twice), and Rollback swaps a kept copy back in the way Apply swaps a new one
    /// in. The layout is the work root's, never plugins/: kept copies are invisible to BepInEx, the
    /// framework's ModDiscovery and ModScanner for the same reason staging is (see SuiteInstaller).
    ///
    /// Every deletion here is held to one owned-path check, TryKeptPath, which accepts only
    /// &lt;work root&gt;/kept/&lt;plain folder name&gt;/&lt;plain version&gt; with the work root outside plugins/
    /// and refuses everything else. The version is read from a file (the replaced copy's ModInfo.json),
    /// so it is untrusted: SanitizeVersionSegment turns it into one safe name or refuses it.
    /// </summary>
    public static partial class SuiteInstaller
    {
        /// <summary>The work root's folder for kept copies: &lt;work root&gt;/kept/&lt;Folder&gt;/&lt;version&gt;.</summary>
        public const string KeptDirName = "kept";

        /// <summary>The longest version a kept folder is named after.</summary>
        public const int MaxVersionSegmentLength = 64;

        private const string ModInfoFileName = "ModInfo.json";

        // The plugins roots whose "keep is inert without a work folder" line has been written, so a
        // batch of nine mods says it once.
        private static readonly HashSet<string> KeepInertLogged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ── The version segment ─────────────────────────────────────────────

        private static bool IsVersionChar(char c) =>
            (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '.' || c == '_' || c == '-' || c == '+';

        /// <summary>
        /// True when version is safe to be one folder name under kept/&lt;Folder&gt;/: ASCII letters, digits
        /// and . _ - + only (so no separator and no drive colon), at most MaxVersionSegmentLength long,
        /// starting like a version (a digit, or v or V and then a digit, which also keeps every Windows
        /// device name such as CON, NUL or COM1 out), with no ".." anywhere and no trailing dot.
        /// </summary>
        public static bool IsPlainVersionSegment(string version)
        {
            if (string.IsNullOrEmpty(version) || version.Length > MaxVersionSegmentLength) return false;
            foreach (var c in version)
                if (!IsVersionChar(c)) return false;
            int first = (version[0] == 'v' || version[0] == 'V') ? 1 : 0;
            if (first >= version.Length || version[first] < '0' || version[first] > '9') return false;
            if (version.Contains("..")) return false;
            return version[version.Length - 1] != '.';
        }

        /// <summary>
        /// The kept-folder name for a version read from a file: every character outside the plain set
        /// becomes an underscore ("1.2.3 beta" is "1.2.3_beta"), and the result is returned only when it
        /// is a plain version segment, else null. A null raw, or one that cannot be made safe (it
        /// starts with a dot, holds "..", is a device name), is null, and the copy is not kept.
        /// </summary>
        public static string SanitizeVersionSegment(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var sb = new StringBuilder();
            foreach (var c in raw.Trim())
                sb.Append(IsVersionChar(c) ? c : '_');
            var segment = sb.ToString();
            return IsPlainVersionSegment(segment) ? segment : null;
        }

        private static bool SameVersionName(string a, string b) =>
            a != null && b != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        // ── The owned kept layout ───────────────────────────────────────────

        /// <summary>
        /// The path of &lt;work root&gt;/kept/&lt;folderName&gt;, rebuilt here from the roots and one plain
        /// folder name and checked against the owned layout: the work root exists (null is no layout,
        /// since a kept copy is never made inside plugins/), lies outside plugins/, and the result is
        /// exactly one folder deep under kept/. False, logged, when the check fails.
        /// </summary>
        private static bool TryKeptFolder(string root, string work, string folderName, out string folder)
        {
            folder = null;
            string candidate = null;
            if (work != null && IsPlainFolderName(folderName))
                candidate = Path.GetFullPath(Path.Combine(work, KeptDirName, folderName));
            bool owned = candidate != null
                && IsOutside(Path.GetFullPath(work), root) && IsOutside(candidate, root)
                && SamePath(Path.GetDirectoryName(candidate), Path.GetFullPath(Path.Combine(work, KeptDirName)))
                && string.Equals(Path.GetFileName(candidate), folderName, StringComparison.OrdinalIgnoreCase)
                && !SamePath(candidate, TargetPathFor(root, folderName));
            if (!owned)
            {
                MumLog.Error($"Suite: refused to use a kept folder for {folderName}: not a folder this installer owns.");
                return false;
            }
            folder = candidate;
            return true;
        }

        /// <summary>
        /// The one owned-path check for a kept copy: &lt;work root&gt;/kept/&lt;folderName&gt;/&lt;version&gt;,
        /// rebuilt here from the roots, one plain folder name and one plain version, and accepted only
        /// when it is exactly that shape with the work root outside plugins/. Refuses everything else:
        /// a version with a separator or "..", a work root inside plugins/, the plugin folder itself.
        /// Call it immediately before every move or delete of a kept copy.
        /// </summary>
        private static bool TryKeptPath(string root, string work, string folderName, string version, out string path)
        {
            path = null;
            if (!TryKeptFolder(root, work, folderName, out string folder)) return false;
            string candidate = IsPlainVersionSegment(version) ? Path.GetFullPath(Path.Combine(folder, version)) : null;
            bool owned = candidate != null
                && SamePath(Path.GetDirectoryName(candidate), folder)
                && string.Equals(Path.GetFileName(candidate), version, StringComparison.OrdinalIgnoreCase);
            if (!owned)
            {
                MumLog.Error($"Suite: refused to use a kept copy of {folderName} named \"{version}\": not a kept copy this installer owns.");
                return false;
            }
            path = candidate;
            return true;
        }

        // The version in a mod folder's ModInfo.json, or null when it cannot be read.
        private static string ReadModInfoVersion(string dir)
        {
            try
            {
                var file = Path.Combine(dir, ModInfoFileName);
                if (!File.Exists(file)) return null;
                return SimpleJson.DeserializeModInfo(File.ReadAllText(file))?.Version;
            }
            catch (Exception ex)
            {
                MumLog.Debug($"Suite: could not read the version in {dir}: {ex.Message}");
                return null;
            }
        }

        // A copy worth offering: its ModInfo.json and at least one DLL beside it. A folder without
        // either is not a mod anyone could roll back to.
        private static bool IsOfferableCopy(string dir)
        {
            try
            {
                return File.Exists(Path.Combine(dir, ModInfoFileName))
                    && Directory.GetFiles(dir, "*.dll", SearchOption.TopDirectoryOnly).Length > 0;
            }
            catch (Exception ex)
            {
                MumLog.Debug($"Suite: could not look inside {dir}: {ex.Message}");
                return false;
            }
        }

        // The kept copy of folderName at version, whole and not a link, through the owned-path check.
        private static bool KeptCopyIsWhole(string root, string work, string folderName, string version)
        {
            if (!TryKeptPath(root, work, folderName, version, out string kept)) return false;
            return Directory.Exists(kept) && !IsLink(kept) && IsOfferableCopy(kept);
        }

        /// <summary>
        /// The versions kept for one mod, newest first, offered only when whole (a ModInfo.json and a
        /// DLL) and not a link; an empty list when there is no work root or nothing is kept. Reads the
        /// folders but changes nothing, and needs no probe: kept/ exists only where a swap has already
        /// worked.
        /// </summary>
        public static List<string> ListKeptVersions(string pluginsRoot, string workRoot, string folderName)
        {
            var versions = new List<string>();
            if (string.IsNullOrEmpty(pluginsRoot) || string.IsNullOrEmpty(workRoot)) return versions;
            var root = Path.GetFullPath(pluginsRoot);
            var work = Path.GetFullPath(workRoot);
            if (!IsPlainFolderName(folderName) || !LayoutIsSafe(root, work)) return versions;
            if (!TryKeptFolder(root, work, folderName, out string folder) || !Directory.Exists(folder)) return versions;
            try
            {
                foreach (var dir in Directory.GetDirectories(folder))
                {
                    var name = Path.GetFileName(dir);
                    if (IsPlainVersionSegment(name) && KeptCopyIsWhole(root, work, folderName, name))
                        versions.Add(name);
                }
            }
            catch (Exception ex)
            {
                MumLog.Debug($"Suite: could not list the kept copies of {folderName}: {ex.Message}");
            }
            versions.Sort(CompareNewestFirst);
            return versions;
        }

        private static int CompareNewestFirst(string a, string b)
        {
            int c = VersionComparer.Compare(b, a);
            return c != 0 ? c : string.Compare(b, a, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Which kept versions to delete so that keepCount remain: the copy just kept is always one of
        /// them, and the other slots go to the newest of the rest; whatever is past that is returned,
        /// oldest last. keepCount below 1 (the option off) prunes nothing. Pure: it reads no folder.
        /// </summary>
        public static List<string> PlanPrune(IEnumerable<string> keptVersions, string justKept, int keepCount)
        {
            var doomed = new List<string>();
            if (keepCount < 1) return doomed;
            var others = new List<string>();
            foreach (var version in keptVersions)
                if (!SameVersionName(version, justKept)) others.Add(version);
            others.Sort(CompareNewestFirst);
            int room = Math.Max(0, keepCount - (justKept == null ? 0 : 1));
            for (int i = room; i < others.Count; i++)
                doomed.Add(others[i]);
            return doomed;
        }

        // ── Keeping the replaced copy ───────────────────────────────────────

        /// <summary>
        /// Moves the replaced copy (the backup folder) to &lt;work root&gt;/kept/&lt;folder&gt;/&lt;version&gt;,
        /// the version read from its own ModInfo.json. False, with the copy left in the backup folder
        /// for the ordinary delete, when it cannot or should not be kept: no work root (said once per
        /// session at Info; a kept copy is never made inside plugins/), a link, no readable or safe
        /// version, no ModInfo.json and DLL, the same version as the copy now installed (a re-apply
        /// would only keep a duplicate), a kept copy of that version already there, or a failed move.
        /// So the option can never make an Apply fail or leave a folder where it must not be.
        /// </summary>
        private static bool KeepReplacedCopy(string root, string work, string folderName, out string keptVersion)
        {
            keptVersion = null;
            if (work == null)
            {
                if (KeepInertLogged.Add(root))
                    MumLog.Info($"Suite: keeping the previous version needs the work folder beside {root}, which this session cannot use (see the earlier line about staging), so replaced copies are deleted as usual.");
                return false;
            }
            if (!TryOwnedPath(root, work, folderName, OwnedFolder.Backup, out string backup)) return false;
            if (!Directory.Exists(backup) || IsLink(backup)) return false;

            var segment = SanitizeVersionSegment(ReadModInfoVersion(backup));
            if (segment == null)
            {
                MumLog.Warning($"Suite: {folderName}: the replaced copy's version cannot name a kept folder, so it was not kept.");
                return false;
            }
            if (!IsOfferableCopy(backup))
            {
                MumLog.Debug($"Suite: {folderName}: the replaced copy has no ModInfo.json and DLL, so it was not kept.");
                return false;
            }
            if (SameVersionName(segment, SanitizeVersionSegment(ReadModInfoVersion(TargetPathFor(root, folderName)))))
            {
                MumLog.Debug($"Suite: {folderName}: the replaced copy is the version now installed, so it was not kept.");
                return false;
            }
            if (!TryKeptPath(root, work, folderName, segment, out string dest)) return false;
            if (Directory.Exists(dest))
            {
                MumLog.Warning($"Suite: {folderName}: a kept copy of {segment} already exists, so the replaced copy was not kept.");
                return false;
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                Directory.Move(backup, dest);
            }
            catch (Exception ex)
            {
                MumLog.Warning($"Suite: {folderName}: could not keep the replaced copy as {segment} ({ex.Message}); it is deleted instead.");
                return false;
            }
            keptVersion = segment;
            MumLog.Debug($"Suite: kept the replaced copy of {folderName} as {dest}.");
            return true;
        }

        /// <summary>
        /// Deletes the kept copies past the count, a step at a time and by the same stepwise delete as
        /// the replaced copy: each one's path is rebuilt through TryKeptPath before every step, and a
        /// link inside it is removed as a link, never followed.
        /// </summary>
        private static IEnumerable<int> PruneKeptSteps(string root, string work, string folderName, string justKept, int keepCount, SuiteInstallProgress progress)
        {
            var doomed = PlanPrune(ListKeptVersions(root, work, folderName), justKept, keepCount);
            foreach (var version in doomed)
            {
                var target = version;
                foreach (var step in RemoveOwnedFolderSteps(
                    () => TryKeptPath(root, work, folderName, target, out string kept) ? kept : null,
                    folderName, "older kept copy " + target, progress))
                    yield return step;
            }
        }

        // ── Rolling back ────────────────────────────────────────────────────

        /// <summary>
        /// Rolls one mod back to a kept copy in one call: a synchronous drain of RollbackSteps, for a
        /// caller that does not need to draw between steps (the Pester gates drive this).
        /// </summary>
        public static SuiteInstallOutcome Rollback(string pluginsRoot, string workRoot, string folderName, string displayName, string version, SuiteKeepPolicy keep, Action<string> status)
        {
            var progress = new SuiteInstallProgress();
            var steps = RollbackSteps(pluginsRoot, workRoot, folderName, displayName, version, keep, status, progress);
            while (steps.MoveNext()) { }
            return progress.Outcome;
        }

        /// <summary>
        /// Rolls one mod back to the kept copy of <paramref name="version"/> a step at a time: the same
        /// swap an Apply makes, with the kept folder as the source and not the staging folder. It clears
        /// leftovers first, runs the two renames of the swap in one step, then keeps the copy it replaced
        /// as the new kept one (the same rules as an Apply, so the sprite cache is carried to the
        /// restored copy and never kept twice) and, with the option on, prunes past the count. A kept
        /// copy that is missing or not whole, or a work root that cannot be used, fails with the installed
        /// copy untouched; a failed swap puts the installed copy back and never deletes the kept copy.
        /// workRoot is ChooseWorkRoot's answer; null means no kept layout and no rollback.
        /// </summary>
        public static IEnumerator RollbackSteps(string pluginsRoot, string workRoot, string folderName, string displayName, string version, SuiteKeepPolicy keep, Action<string> status, SuiteInstallProgress progress)
        {
            if (progress == null) throw new ArgumentNullException(nameof(progress));
            if (keep == null) keep = SuiteKeepPolicy.Off;
            progress.Phase = SuiteInstallPhase.Checking;
            progress.Outcome = SuiteInstallOutcome.FailedUntouched;
            progress.Done = 0;
            progress.Total = 0;

            var root = Path.GetFullPath(pluginsRoot);
            var work = string.IsNullOrEmpty(workRoot) ? null : Path.GetFullPath(workRoot);
            if (!IsPlainFolderName(folderName) || !IsUnder(TargetPathFor(root, folderName), root))
            {
                MumLog.Error($"Suite: path traversal detected for {folderName}, aborting the rollback.");
                status?.Invoke($"FAILED: {displayName} - its folder name points outside BepInEx/plugins.");
                progress.Phase = SuiteInstallPhase.Done;
                yield break;
            }
            if (work == null || !LayoutIsSafe(root, work))
            {
                MumLog.Error($"Suite: refused to roll {folderName} back: there is no usable work folder beside {root}.");
                status?.Invoke($"FAILED: {displayName} - there is no work folder to roll back through this session; nothing was changed.");
                progress.Phase = SuiteInstallPhase.Done;
                yield break;
            }
            if (!ClearLeftovers(root, work, folderName, displayName, status))
            {
                progress.Phase = SuiteInstallPhase.Done;
                yield break;
            }
            if (!KeptCopyIsWhole(root, work, folderName, version))
            {
                MumLog.Error($"Suite: {folderName}: the kept copy {version} is not there or is not whole, left as installed.");
                status?.Invoke($"FAILED: {displayName} - the kept copy v{version} is not there or is incomplete; your installed copy was not changed.");
                progress.Phase = SuiteInstallPhase.Done;
                yield break;
            }
            yield return progress;

            // The swap, whole, in this one step; the copy it replaces is kept, or deleted, in the next.
            progress.Outcome = SwapFrom(root, work, folderName, displayName, status, version);
            if (progress.Outcome != SuiteInstallOutcome.Installed || !Directory.Exists(BackupPathFor(root, work, folderName)))
            {
                // A failed swap, or nothing was installed to replace.
                progress.Phase = SuiteInstallPhase.Done;
                yield break;
            }

            if (KeepReplacedCopy(root, work, folderName, out string keptVersion))
            {
                progress.Phase = SuiteInstallPhase.Pruning;
                progress.Total = 0;
                progress.Done = 0;
                yield return progress;
                // With the option off keep.Versions is 0 and PlanPrune deletes nothing: a rollback
                // swaps one kept copy for another and never changes how many are held.
                foreach (var _ in PruneKeptSteps(root, work, folderName, keptVersion, keep.Versions, progress))
                    yield return progress;
                progress.Phase = SuiteInstallPhase.Done;
                yield break;
            }

            progress.Phase = SuiteInstallPhase.Removing;
            progress.Total = 0;
            progress.Done = 0;
            yield return progress;
            foreach (var _ in RemoveReplacedCopySteps(root, work, folderName, progress))
                yield return progress;
            progress.Phase = SuiteInstallPhase.Done;
        }
    }
}
