using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace mod_update_manager
{
    public enum SuiteInstallOutcome
    {
        /// <summary>The new copy is in place, every file verified at its full length.</summary>
        Installed,

        /// <summary>
        /// Nothing changed: the mod's plugin folder holds exactly what it held before Apply (or is
        /// still absent, for a mod that was not installed).
        /// </summary>
        FailedUntouched,

        /// <summary>
        /// The swap failed and could not be undone, so the plugin folder is missing and the previous
        /// copy sits in the backup folder (&lt;work root&gt;/old/&lt;Folder&gt;, or
        /// plugins/&lt;Folder&gt;__mum_old in the in-plugins layout). The only outcome that leaves a
        /// mod unloadable.
        /// </summary>
        FailedFolderMissing
    }

    /// <summary>The part of one mod's install that a step of SuiteInstaller.InstallSteps has reached.</summary>
    public enum SuiteInstallPhase
    {
        /// <summary>Reading the bundled archive and checking every entry, before anything is written.</summary>
        Checking,

        /// <summary>Unpacking files into the staging folder.</summary>
        Extracting,

        /// <summary>Checking the length of every unpacked file in the staging folder.</summary>
        Verifying,

        /// <summary>The new copy is in place; the replaced copy is being deleted from the backup folder.</summary>
        Removing,

        /// <summary>
        /// The new copy is in place and the replaced one was kept (the "keep the previous version"
        /// option); kept copies past the chosen count are being deleted from the work root's kept/ folder.
        /// </summary>
        Pruning,

        /// <summary>The run has ended and Outcome is final.</summary>
        Done
    }

    /// <summary>
    /// What one run of SuiteInstaller.InstallSteps has done so far, read between steps for the status
    /// line, and how the run ended. Host-free, like SuiteInstaller.
    /// </summary>
    public sealed class SuiteInstallProgress
    {
        public SuiteInstallPhase Phase { get; internal set; } = SuiteInstallPhase.Checking;

        /// <summary>
        /// How far this phase has got: entries checked, files unpacked, files verified or files of the
        /// replaced copy deleted. Directory entries are not counted as files.
        /// </summary>
        public int Done { get; internal set; }

        /// <summary>How many there are to do in this phase, counted the same way as Done.</summary>
        public int Total { get; internal set; }

        /// <summary>
        /// Where the mod stands if the run stops here: FailedUntouched until the swap has put the new
        /// copy in place, Installed from then on. Final once Phase is Done.
        /// </summary>
        public SuiteInstallOutcome Outcome { get; internal set; } = SuiteInstallOutcome.FailedUntouched;
    }

    /// <summary>
    /// Installs one embedded suite ZIP into plugins/&lt;FolderName&gt;/ without ever leaving that
    /// folder half-replaced, and without ever putting a second copy of a mod where a loader reads.
    /// The archive is checked whole, extracted into &lt;work root&gt;/staging/&lt;FolderName&gt; and
    /// size-verified there; only then is the installed folder renamed to
    /// &lt;work root&gt;/old/&lt;FolderName&gt; and the staging folder renamed into its place. Any
    /// failure before those renames leaves the installed folder byte-for-byte as it was.
    ///
    /// The work root is BepInEx/mum_work/, a sibling of plugins/: BepInEx scans only plugins/
    /// (recursively, every *.dll), the framework's ModDiscovery reads plugins/&lt;X&gt;/ModInfo.json
    /// and one level deeper, and ModScanner lists every plugins/ subfolder with a ModInfo.json, so a
    /// staging or backup copy that a game closed mid-update left inside plugins/ would be loaded and
    /// listed as a second copy of the mod. Outside plugins/ it is invisible to all three
    /// (Documentation/Research/Mod_Update_Manager/W0-2_BepInEx_Plugin_Scan.md).
    ///
    /// Renames rather than overwrites, because Apply runs inside the game with every suite DLL
    /// loaded: Mono maps each assembly and closes its file handle, and a mapped file cannot be
    /// overwritten, while its folder can still be renamed, across parents on one volume too, and its
    /// old copy deleted (measured under the game's own Mono with Assembly.LoadFile, 2026-10-02,
    /// W0-3_Apply_Timing_And_Rename.md section 5). An open handle does block the folder rename,
    /// which then fails cleanly and leaves the mod as installed.
    ///
    /// A swap renames across the two parents both ways, so ChooseWorkRoot probes that once per
    /// session; when the probe fails (the two on different volumes, say through a junction) the
    /// installer falls back to the earlier in-plugins layout (&lt;Folder&gt;__mum_staging and
    /// &lt;Folder&gt;__mum_old beside the plugin folder, used by the 2.1.62 and 2.1.63 builds made
    /// before 2026-10-02, neither published), and Install is passed a null work root.
    ///
    /// Every folder this class deletes is one it owns, its path rebuilt from the plugins root, the
    /// work root and the folder name at the delete and checked against the owned layout there
    /// (TryOwnedPath, used by DeleteOwnedFolder and before every step of RemoveReplacedCopySteps):
    /// &lt;work root&gt;/staging/&lt;name&gt;, &lt;work root&gt;/old/&lt;name&gt;,
    /// plugins/&lt;name&gt;__mum_staging and plugins/&lt;name&gt;__mum_old, plus the probe's empty
    /// folders, removed only by a non-recursive delete. The one deletion that is not of a
    /// leftover is the opt-in "keep the previous version" option's pruning, and it is held to the
    /// same rule: &lt;work root&gt;/kept/&lt;name&gt;/&lt;version&gt; and nothing else (TryKeptPath, in
    /// SuiteInstaller.Kept.cs, where the option, the pruning and Rollback live).
    ///
    /// The work runs in steps (InstallSteps), so the game can draw a frame between them while Apply
    /// runs inside it: Apply used to run whole inside one OnGUI call, and the game stopped drawing for
    /// all of it. Install is a synchronous drain of the same steps.
    ///
    /// Host-free by design (no BepInEx, no UnityEngine): Development_Tools/Tests/MUM-CompiledLogic.Tests.ps1
    /// compiles this file and drives it against a scratch plugins tree.
    /// </summary>
    public static partial class SuiteInstaller
    {
        /// <summary>The work root's folder name, a sibling of plugins/ inside BepInEx/.</summary>
        public const string WorkRootDirName = "mum_work";

        // The work root's two subfolders: staging/<Folder> holds a new copy while it is extracted
        // and checked, old/<Folder> holds the replaced copy between the two renames of a swap.
        public const string StagingDirName = "staging";
        public const string OldDirName = "old";

        // The earlier in-plugins layout: used only when the probe sends a session to it, and the
        // names ClearLeftovers migrates out of plugins/.
        public const string StagingSuffix = "__mum_staging";
        public const string BackupSuffix = "__mum_old";

        // ChooseWorkRoot's probe: an empty folder made in the work root, renamed into plugins/ and
        // back, then deleted.
        private const string ProbeDirName = "probe";
        private const string PluginsProbeDirName = "__mum_probe";

        // The framework's rendered-sprite cache (CSFFModFramework SpriteTextureCache, 1.43 GB in 474
        // files on the reference install, 2026-09-26). Never in a suite ZIP, so it is carried from
        // the old folder to the new one by a rename, never copied.
        private const string SpriteCacheDirName = "SpriteCache";

        // Step sizes for InstallSteps. Tunables, not final values: taken from the slice-cost table in
        // Documentation/Research/Mod_Update_Manager/W0-3_Apply_Timing_And_Rename.md section 4, which
        // timed this installer on the game's own Mono runtime outside Unity, with the game running and
        // other programs busy, and aimed one step at one 60 fps frame at the 95th percentile. The
        // in-game look at Apply (tracker T2.370) is where they get measured again.

        /// <summary>Files unpacked per step (W0-3's recommendation).</summary>
        public static int FilesPerStep = 8;

        /// <summary>
        /// Bytes unpacked per step, a safeguard that ends a step early when files are large. W0-3 found
        /// it never binds before FilesPerStep on the suite as packed then.
        /// </summary>
        public static long BytesPerStep = 1L << 20;

        /// <summary>Archive entries checked, or unpacked files size-checked, per step (W0-3's recommendation).</summary>
        public static int EntriesPerCheckStep = 100;

        /// <summary>Files of the replaced copy deleted per step (W0-3's recommendation).</summary>
        public static int FilesPerDeleteStep = 16;

        private enum OwnedFolder { Staging, Backup, LegacyStaging, LegacyBackup }

        // One probe verdict per (plugins root, work root) for the session.
        private static readonly Dictionary<string, bool> ProbedWorkRoots = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The work root to install through, or null for the in-plugins layout. Probed at the first
        /// call of the session for this pair of roots and remembered: an empty folder is created in
        /// the work root, renamed into plugins/ and back, then deleted. When either rename fails, or
        /// the work root sits inside plugins/, every Apply this session uses the in-plugins layout,
        /// and that is logged once at Info.
        /// </summary>
        public static string ChooseWorkRoot(string pluginsRoot, string workRoot)
        {
            var root = Path.GetFullPath(pluginsRoot);
            var work = Path.GetFullPath(workRoot);
            var key = root + "|" + work;
            if (ProbedWorkRoots.TryGetValue(key, out bool known))
                return known ? work : null;

            bool usable = ProbeWorkRoot(root, work, out string why);
            ProbedWorkRoots[key] = usable;
            if (usable)
                MumLog.Debug($"Suite: updates are staged in {work}, outside {root}.");
            else
                MumLog.Info($"Suite: updates cannot be staged in {work} ({why}), so this session stages each mod beside its folder inside {root} instead; a copy left there by a game closed mid-update is loaded as a second copy until that mod's next Apply clears it.");
            return usable ? work : null;
        }

        private static bool ProbeWorkRoot(string root, string work, out string why)
        {
            if (!IsOutside(work, root))
            {
                why = "it is inside the plugins folder";
                return false;
            }
            try
            {
                Directory.CreateDirectory(work);
                // An earlier probe cut off part-way can leave either folder behind, always empty.
                RemoveEmptyProbeFolders(root, work);
                Directory.CreateDirectory(Path.Combine(work, ProbeDirName));
                Directory.Move(Path.Combine(work, ProbeDirName), Path.Combine(root, PluginsProbeDirName));
                Directory.Move(Path.Combine(root, PluginsProbeDirName), Path.Combine(work, ProbeDirName));
                why = null;
                return true;
            }
            catch (Exception ex)
            {
                MumLog.Debug($"Suite: work-folder probe in {work} failed: {ex.Message}");
                why = ex.Message;
                return false;
            }
            finally
            {
                RemoveEmptyProbeFolders(root, work);
            }
        }

        // Non-recursive: Directory.Delete(path, false) removes an empty folder and throws on one that
        // holds anything, so nothing the probe did not create can be lost here.
        private static void RemoveEmptyProbeFolders(string root, string work)
        {
            foreach (var path in new[] { Path.Combine(work, ProbeDirName), Path.Combine(root, PluginsProbeDirName) })
            {
                if (!Directory.Exists(path)) continue;
                try
                {
                    Directory.Delete(path, false);
                }
                catch (Exception ex)
                {
                    MumLog.Warning($"Suite: could not remove the probe folder {path}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Installs one mod in one call: a synchronous drain of InstallSteps, for a caller that does
        /// not need to draw between steps (the Pester gates drive this). workRoot is ChooseWorkRoot's
        /// answer: a folder outside plugins/, or null for the in-plugins layout.
        /// </summary>
        public static SuiteInstallOutcome Install(Stream zip, string pluginsRoot, string workRoot, string folderName, string displayName, Action<string> status)
        {
            return Install(zip, pluginsRoot, workRoot, folderName, displayName, status, SuiteKeepPolicy.Off);
        }

        /// <summary>
        /// Install with the "keep the previous version" option as the player set it: with keep on, the
        /// replaced copy is moved to &lt;work root&gt;/kept/&lt;folder&gt;/&lt;version&gt; instead of deleted.
        /// </summary>
        public static SuiteInstallOutcome Install(Stream zip, string pluginsRoot, string workRoot, string folderName, string displayName, Action<string> status, SuiteKeepPolicy keep)
        {
            var progress = new SuiteInstallProgress();
            var steps = InstallSteps(zip, pluginsRoot, workRoot, folderName, displayName, status, progress, keep);
            while (steps.MoveNext()) { }
            return progress.Outcome;
        }

        /// <summary>
        /// Installs one mod a step at a time. Each MoveNext does one bounded piece of the work and
        /// returns true with progress updated, so the caller can let the game draw a frame before the
        /// next; it returns false when the run has ended, and progress.Outcome is then final. A step
        /// checks at most EntriesPerCheckStep archive entries, unpacks at most FilesPerStep files (or
        /// BytesPerStep bytes), size-checks at most EntriesPerCheckStep files, or deletes at most
        /// FilesPerDeleteStep files of the replaced copy, and every phase ends on a step of its own.
        ///
        /// The two renames of the swap, and the rollback when the second fails, run inside one step and
        /// never with a yield between them. So a run that is never finished (the game closed between
        /// two steps) leaves either the installed copy or the new one in plugins/, whole, and anything
        /// else only in the work root, where the next Apply of that mod clears it. workRoot is
        /// ChooseWorkRoot's answer: a folder outside plugins/, or null for the in-plugins layout.
        /// </summary>
        public static IEnumerator InstallSteps(Stream zip, string pluginsRoot, string workRoot, string folderName, string displayName, Action<string> status, SuiteInstallProgress progress)
        {
            return InstallSteps(zip, pluginsRoot, workRoot, folderName, displayName, status, progress, SuiteKeepPolicy.Off);
        }

        /// <summary>
        /// InstallSteps with the "keep the previous version" option. After the swap, with keep on and a
        /// work root, the replaced copy is moved (one rename on the same volume, after CarrySpriteCache
        /// has moved the sprite cache across, so it is never kept twice) to
        /// &lt;work root&gt;/kept/&lt;folder&gt;/&lt;version&gt;, and kept copies past the count are deleted a
        /// step at a time. With keep off, or when the copy cannot be kept, the replaced copy is deleted
        /// as before, so the option never makes an Apply fail.
        /// </summary>
        public static IEnumerator InstallSteps(Stream zip, string pluginsRoot, string workRoot, string folderName, string displayName, Action<string> status, SuiteInstallProgress progress, SuiteKeepPolicy keep)
        {
            if (progress == null) throw new ArgumentNullException(nameof(progress));
            if (keep == null) keep = SuiteKeepPolicy.Off;
            progress.Phase = SuiteInstallPhase.Checking;
            progress.Outcome = SuiteInstallOutcome.FailedUntouched;
            progress.Done = 0;
            progress.Total = 0;

            var root = Path.GetFullPath(pluginsRoot);
            // null: the in-plugins layout that ChooseWorkRoot falls back to.
            var work = string.IsNullOrEmpty(workRoot) ? null : Path.GetFullPath(workRoot);
            if (!IsPlainFolderName(folderName) || !IsUnder(TargetPathFor(root, folderName), root))
            {
                MumLog.Error($"Suite: path traversal detected for {folderName}, aborting.");
                status?.Invoke($"FAILED: {displayName} - its folder name points outside BepInEx/plugins.");
                progress.Phase = SuiteInstallPhase.Done;
                yield break;
            }
            if (!LayoutIsSafe(root, work))
            {
                MumLog.Error($"Suite: refused to install {folderName}: the work folder {work} is inside {root}, where a staged copy would be loaded as a second copy of the mod.");
                status?.Invoke($"FAILED: {displayName} - its staging folder would be inside BepInEx/plugins; nothing was changed.");
                progress.Phase = SuiteInstallPhase.Done;
                yield break;
            }

            if (!ClearLeftovers(root, work, folderName, displayName, status))
            {
                progress.Phase = SuiteInstallPhase.Done;
                yield break;
            }

            // Read and check the WHOLE archive before anything is written. An archive that cannot be
            // extracted (truncated, or packed with Deflate, which MiniZip cannot read) fails here. The
            // entry list is read in this first step and the entries are checked a slice per step.
            var stagingDir = StagingPathFor(root, work, folderName);
            var planned = new List<KeyValuePair<MiniZip.Entry, string>>();
            List<MiniZip.Entry> entries = null;
            Exception archiveError = null;
            try
            {
                entries = MiniZip.ReadEntries(zip);
            }
            catch (Exception ex)
            {
                MumLog.Warning($"Suite: {folderName}: could not read the bundled archive's entries: {ex.Message}");
                archiveError = ex;
            }
            int entriesChecked = 0;
            progress.Total = entries == null ? 0 : entries.Count;
            while (archiveError == null && entriesChecked < entries.Count)
            {
                int end = SliceEnd(entriesChecked, entries.Count, EntriesPerCheckStep);
                try
                {
                    for (; entriesChecked < end; entriesChecked++)
                        PlanEntry(zip, entries[entriesChecked], stagingDir, planned);
                }
                catch (Exception ex)
                {
                    MumLog.Warning($"Suite: {folderName}: checking the bundled archive's entries failed: {ex.Message}");
                    archiveError = ex;
                }
                if (archiveError != null) break;
                progress.Done = entriesChecked;
                yield return progress;
            }
            if (archiveError != null)
            {
                MumLog.Error($"Suite: {folderName}: bundled archive cannot be extracted, left as installed: {archiveError}");
                status?.Invoke($"FAILED: {displayName} - its bundled copy cannot be extracted ({archiveError.Message}); your installed copy was not changed.");
                progress.Phase = SuiteInstallPhase.Done;
                yield break;
            }

            int fileCount = 0;
            foreach (var item in planned)
                if (!IsDirectoryEntry(item.Key)) fileCount++;

            progress.Phase = SuiteInstallPhase.Extracting;
            progress.Total = fileCount;
            progress.Done = 0;
            Exception extractError = null;
            try
            {
                Directory.CreateDirectory(StagingPathFor(root, work, folderName));
            }
            catch (Exception ex)
            {
                MumLog.Warning($"Suite: {folderName}: could not create the staging folder: {ex.Message}");
                extractError = ex;
            }
            int nextEntry = 0;
            while (extractError == null && nextEntry < planned.Count)
            {
                try
                {
                    nextEntry = ExtractSlice(zip, planned, nextEntry, progress);
                }
                catch (Exception ex)
                {
                    MumLog.Warning($"Suite: {folderName}: extracting into staging failed at entry {nextEntry}: {ex.Message}");
                    extractError = ex;
                }
                if (extractError != null) break;
                yield return progress;
            }
            if (extractError != null)
            {
                MumLog.Error($"Suite: {folderName}: extract into staging failed, left as installed: {extractError}");
                DeleteOwnedFolder(root, work, folderName, OwnedFolder.Staging);
                status?.Invoke($"FAILED: {displayName} - {extractError.Message}; your installed copy was not changed.");
                progress.Phase = SuiteInstallPhase.Done;
                yield break;
            }

            // Every file entry must have landed at its full length. A partial write can leave a file
            // short or absent with nothing thrown (a full disk mid-copy, a scanner holding the file),
            // and this is the only pass that would notice. Entries are Stored, so UncompressedSize is
            // the exact expected length.
            progress.Phase = SuiteInstallPhase.Verifying;
            progress.Total = fileCount;
            progress.Done = 0;
            var missing = new List<string>();
            int entriesVerified = 0;
            while (entriesVerified < planned.Count)
            {
                int end = SliceEnd(entriesVerified, planned.Count, EntriesPerCheckStep);
                for (; entriesVerified < end; entriesVerified++)
                {
                    var item = planned[entriesVerified];
                    if (IsDirectoryEntry(item.Key)) continue;
                    var landed = new FileInfo(item.Value);
                    if (!landed.Exists || landed.Length != item.Key.UncompressedSize)
                        missing.Add(item.Key.Name);
                    progress.Done++;
                }
                yield return progress;
            }
            if (missing.Count > 0)
            {
                foreach (var name in missing)
                    MumLog.Warning($"Suite verify: {folderName} missing or short file after extract: {name}");
                MumLog.Error($"Suite verify: {folderName}: {missing.Count} file(s) missing or short in staging; left as installed.");
                DeleteOwnedFolder(root, work, folderName, OwnedFolder.Staging);
                status?.Invoke($"FAILED: {displayName} - {missing.Count} file(s) did not extract fully; your installed copy was not changed.");
                progress.Phase = SuiteInstallPhase.Done;
                yield break;
            }

            // The swap, whole, in this one step.
            progress.Outcome = SwapIn(root, work, folderName, displayName, status);
            if (progress.Outcome != SuiteInstallOutcome.Installed || !Directory.Exists(BackupPathFor(root, work, folderName)))
            {
                // A failed swap, or a first install, which replaced nothing.
                progress.Phase = SuiteInstallPhase.Done;
                yield break;
            }

            // The new copy is in place. With the keep option on, the replaced copy is moved into the
            // work root's kept/ folder instead of deleted, and the kept copies past the count go.
            if (keep.Enabled && KeepReplacedCopy(root, work, folderName, out string keptVersion))
            {
                progress.Phase = SuiteInstallPhase.Pruning;
                progress.Total = 0;
                progress.Done = 0;
                yield return progress;
                foreach (var _ in PruneKeptSteps(root, work, folderName, keptVersion, keep.Versions, progress))
                    yield return progress;
                progress.Phase = SuiteInstallPhase.Done;
                yield break;
            }

            // Otherwise what is left is deleting the replaced copy, a step at a time.
            progress.Phase = SuiteInstallPhase.Removing;
            progress.Total = 0;
            progress.Done = 0;
            yield return progress;
            foreach (var _ in RemoveReplacedCopySteps(root, work, folderName, progress))
                yield return progress;
            progress.Phase = SuiteInstallPhase.Done;
        }

        // The end of a slice of at most `step` items starting at `start`, never past `count`, and with
        // no overflow when a step size is set very large.
        private static int SliceEnd(int start, int count, int step)
        {
            int size = Math.Max(1, step);
            return count - start <= size ? count : start + size;
        }

        // Unpacks planned entries from `start` until FilesPerStep files or BytesPerStep bytes have been
        // written in this call, or the list ends, and returns the index to resume from. Directory
        // entries are made as they come and count toward neither.
        private static int ExtractSlice(Stream zip, List<KeyValuePair<MiniZip.Entry, string>> planned, int start, SuiteInstallProgress progress)
        {
            int files = 0;
            long bytes = 0;
            int i = start;
            while (i < planned.Count)
            {
                var item = planned[i];
                i++;
                if (IsDirectoryEntry(item.Key))
                {
                    Directory.CreateDirectory(item.Value);
                    continue;
                }
                var dir = Path.GetDirectoryName(item.Value);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                MiniZip.ExtractToFile(zip, item.Key, item.Value);
                progress.Done++;
                files++;
                bytes += item.Key.UncompressedSize;
                if (files >= FilesPerStep || bytes >= BytesPerStep) break;
            }
            return i;
        }

        /// <summary>
        /// Puts the verified staging folder in place of the installed one. Every path is re-derived
        /// from the roots and the folder name at the step that uses it, and the two renames of the
        /// swap run back to back with nothing between them: InstallSteps calls this inside one step.
        /// The replaced copy is left in the backup folder for RemoveReplacedCopySteps to delete.
        /// </summary>
        private static SuiteInstallOutcome SwapIn(string root, string work, string folderName, string displayName, Action<string> status)
        {
            return SwapFrom(root, work, folderName, displayName, status, null);
        }

        /// <summary>
        /// The swap itself, for an install (keptVersion null: the new copy is the verified staging
        /// folder) and for Rollback (keptVersion names the kept copy to bring back). The replaced copy
        /// is left in the backup folder either way. A failed rollback never deletes the kept copy it
        /// was bringing back, which is the player's, where a failed install deletes its staging folder.
        /// </summary>
        private static SuiteInstallOutcome SwapFrom(string root, string work, string folderName, string displayName, Action<string> status, string keptVersion)
        {
            bool fromKept = keptVersion != null;
            // A failed install clears its staging folder; the copy a rollback is bringing back stays.
            void DropStaging()
            {
                if (!fromKept) DeleteOwnedFolder(root, work, folderName, OwnedFolder.Staging);
            }

            bool hadInstall = Directory.Exists(TargetPathFor(root, folderName));
            if (hadInstall)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(BackupPathFor(root, work, folderName)));
                    Directory.Move(TargetPathFor(root, folderName), BackupPathFor(root, work, folderName));
                }
                catch (Exception ex)
                {
                    MumLog.Error($"Suite: {folderName}: could not move the installed copy aside (a file in it is open?), left as installed: {ex.Message}");
                    DropStaging();
                    status?.Invoke($"FAILED: {displayName} - a file in its folder is in use ({ex.Message}); your installed copy was not changed.");
                    return SuiteInstallOutcome.FailedUntouched;
                }
            }

            try
            {
                // Re-derived here, where it is used: the staging folder, or the kept copy through the
                // owned-path check.
                string source = fromKept
                    ? (TryKeptPath(root, work, folderName, keptVersion, out string keptDir) ? keptDir : null)
                    : StagingPathFor(root, work, folderName);
                if (source == null) throw new InvalidOperationException("the kept copy is not in the owned layout");
                Directory.Move(source, TargetPathFor(root, folderName));
            }
            catch (Exception ex)
            {
                MumLog.Error($"Suite: {folderName}: could not move the new copy into place: {ex.Message}");
                if (!hadInstall)
                {
                    DropStaging();
                    status?.Invoke($"FAILED: {displayName} - {ex.Message}; nothing was installed.");
                    return SuiteInstallOutcome.FailedUntouched;
                }
                if (!Directory.Exists(TargetPathFor(root, folderName)) && Directory.Exists(BackupPathFor(root, work, folderName)))
                {
                    try
                    {
                        Directory.Move(BackupPathFor(root, work, folderName), TargetPathFor(root, folderName));
                        MumLog.Warning($"Suite: {folderName}: put the installed copy back after the failed swap.");
                        DropStaging();
                        status?.Invoke($"FAILED: {displayName} - {ex.Message}; your installed copy was put back unchanged.");
                        return SuiteInstallOutcome.FailedUntouched;
                    }
                    catch (Exception rollbackEx)
                    {
                        MumLog.Error($"Suite: {folderName}: could not put the installed copy back either; it is at {BackupPathFor(root, work, folderName)}: {rollbackEx.Message}");
                    }
                }
                status?.Invoke($"[Install Incomplete] {displayName}: its folder could not be put back. Your previous copy is in {DisplayPath(root, BackupPathFor(root, work, folderName))}; apply again after restarting.");
                return SuiteInstallOutcome.FailedFolderMissing;
            }

            if (hadInstall)
                CarrySpriteCache(folderName, BackupPathFor(root, work, folderName), TargetPathFor(root, folderName));

            // The Info line naming the version is ModSuiteExtractor's, which knows the version.
            MumLog.Debug($"Suite: swapped the new copy of {folderName} into place.");
            return SuiteInstallOutcome.Installed;
        }

        /// <summary>
        /// Deletes the replaced copy (&lt;work root&gt;/old/&lt;name&gt;, or plugins/&lt;name&gt;__mum_old)
        /// FilesPerDeleteStep files per step, then whatever is left (the emptied folders, and any link)
        /// with one DeleteOwnedFolder. Before every step the folder's path is rebuilt from the roots and
        /// the folder name and checked against the owned layout, and a file is deleted only when it is
        /// inside that folder. The file list never descends into a link (a junction or a symbolic link
        /// to a folder): Directory.Delete removes a link without following it, and a file-by-file delete
        /// that followed one would delete what it points at.
        ///
        /// A failure here is only a disk-space leak: the install is already complete, and the old copy
        /// is outside plugins/ (or, in the in-plugins layout, cleared by the next Apply). A file the
        /// running game still maps may refuse deletion; ClearLeftovers retries at the next Apply.
        /// </summary>
        private static IEnumerable<int> RemoveReplacedCopySteps(string root, string work, string folderName, SuiteInstallProgress progress)
        {
            return RemoveOwnedFolderSteps(
                () => TryOwnedPath(root, work, folderName, OwnedFolder.Backup, out string backup) ? backup : null,
                folderName, "replaced copy", progress);
        }

        /// <summary>
        /// The stepwise delete behind RemoveReplacedCopySteps and the kept-copy pruning, one body for
        /// both so the link rule and the re-check hold for both. owned is called again immediately
        /// before every step and returns the folder's path rebuilt from the roots and names and checked
        /// against the owned layout, or null when the check refuses; nothing here holds a path across a
        /// step. what and folderName only name the folder in the log.
        /// </summary>
        private static IEnumerable<int> RemoveOwnedFolderSteps(Func<string> owned, string folderName, string what, SuiteInstallProgress progress)
        {
            List<string> files = null;
            var links = new List<string>();
            var listRoot = owned();
            if (listRoot != null && Directory.Exists(listRoot))
            {
                try
                {
                    files = ListFilesNotFollowingLinks(listRoot, links);
                }
                catch (Exception ex)
                {
                    MumLog.Debug($"Suite: {folderName}: could not list the {what}, deleting it whole instead: {ex.Message}");
                }
            }

            if (files != null)
            {
                progress.Total = files.Count;
                progress.Done = 0;
                int next = 0;
                while (next < files.Count)
                {
                    // Rebuilt and checked again at every step: no path held over from an earlier step
                    // aims this delete.
                    var stepRoot = owned();
                    if (stepRoot == null) break;
                    int end = SliceEnd(next, files.Count, FilesPerDeleteStep);
                    bool stopped = false;
                    for (; next < end; next++)
                    {
                        if (!IsUnder(files[next], stepRoot))
                        {
                            MumLog.Error($"Suite: refused to delete {files[next]}: not inside {stepRoot}.");
                            stopped = true;
                            break;
                        }
                        try
                        {
                            File.Delete(files[next]);
                        }
                        catch (Exception ex)
                        {
                            MumLog.Debug($"Suite: {folderName}: could not delete {files[next]} from the {what}: {ex.Message}");
                            stopped = true;
                            break;
                        }
                        progress.Done++;
                    }
                    if (stopped) break;
                    yield return next;
                }
            }

            // A link inside the copy is taken away as a link: a non-recursive delete removes the link
            // and leaves what it points at, whatever the runtime's recursive delete would do with it.
            var linkRoot = links.Count > 0 ? owned() : null;
            if (linkRoot != null)
            {
                foreach (var link in links)
                {
                    if (!IsUnder(link, linkRoot)) continue;
                    try
                    {
                        Directory.Delete(link, false);
                    }
                    catch (Exception ex)
                    {
                        MumLog.Debug($"Suite: {folderName}: could not remove the link {link} from the {what}: {ex.Message}");
                    }
                }
            }

            if (!DeleteResolvedFolder(owned))
                MumLog.Warning($"Suite: {folderName}: the {what} could not be fully deleted and stays at {owned() ?? listRoot}.");
        }

        // Every file under dir, never descending into a link, and nothing at all when dir is itself a
        // link (DeleteOwnedFolder removes that one as a link). The links met inside are added to links.
        private static List<string> ListFilesNotFollowingLinks(string dir, List<string> links)
        {
            var files = new List<string>();
            if (IsLink(dir)) return files;
            var pending = new Stack<string>();
            pending.Push(dir);
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                files.AddRange(Directory.GetFiles(current));
                foreach (var sub in Directory.GetDirectories(current))
                {
                    if (IsLink(sub)) links.Add(sub);
                    else pending.Push(sub);
                }
            }
            return files;
        }

        // A junction, a mount point or a symbolic link: a folder that is really somewhere else.
        private static bool IsLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

        /// <summary>
        /// Clears what an interrupted earlier Apply left: first the in-plugins suffix folders (of the
        /// earlier layout, or of a session the probe sent to it), restoring a lone __mum_old, so an
        /// install that has them migrates out of plugins/ at that mod's next Apply; then the work
        /// root's. Returns false, with the plugin folder untouched, when that cannot be done safely.
        /// </summary>
        private static bool ClearLeftovers(string root, string work, string folderName, string displayName, Action<string> status)
        {
            if (!ClearLeftoversIn(root, work, folderName, displayName, status, OwnedFolder.LegacyBackup, OwnedFolder.LegacyStaging))
                return false;
            // In the in-plugins layout the work folders ARE the legacy ones, already cleared above.
            return work == null || ClearLeftoversIn(root, work, folderName, displayName, status, OwnedFolder.Backup, OwnedFolder.Staging);
        }

        private static bool ClearLeftoversIn(string root, string work, string folderName, string displayName, Action<string> status,
                                             OwnedFolder backupKind, OwnedFolder stagingKind)
        {
            if (Directory.Exists(OwnedPath(root, work, folderName, backupKind)))
            {
                if (!Directory.Exists(TargetPathFor(root, folderName)))
                {
                    // A previous Apply moved the installed copy aside and never moved a new one in
                    // (the game closed between the two renames). That aside copy IS the install.
                    try
                    {
                        Directory.Move(OwnedPath(root, work, folderName, backupKind), TargetPathFor(root, folderName));
                        MumLog.Warning($"Suite: {folderName}: restored the copy an interrupted earlier Apply had moved aside to {OwnedPath(root, work, folderName, backupKind)}.");
                    }
                    catch (Exception ex)
                    {
                        MumLog.Error($"Suite: {folderName}: could not restore {OwnedPath(root, work, folderName, backupKind)}: {ex.Message}");
                        status?.Invoke($"FAILED: {displayName} - an earlier interrupted update left your copy in {DisplayPath(root, OwnedPath(root, work, folderName, backupKind))}, and it could not be moved back ({ex.Message}).");
                        return false;
                    }
                }
                else if (!DeleteOwnedFolder(root, work, folderName, backupKind))
                {
                    status?.Invoke($"FAILED: {displayName} - the copy an earlier update replaced ({DisplayPath(root, OwnedPath(root, work, folderName, backupKind))}) could not be deleted; restart the game and apply again. Your installed copy was not changed.");
                    return false;
                }
                else
                {
                    MumLog.Warning($"Suite: {folderName}: deleted the copy an earlier Apply replaced but could not delete at the time.");
                }
            }

            if (Directory.Exists(OwnedPath(root, work, folderName, stagingKind)))
            {
                MumLog.Warning($"Suite: {folderName}: deleting a staging folder left by an interrupted earlier Apply ({OwnedPath(root, work, folderName, stagingKind)}).");
                if (!DeleteOwnedFolder(root, work, folderName, stagingKind))
                {
                    status?.Invoke($"FAILED: {displayName} - a staging folder from an earlier update ({DisplayPath(root, OwnedPath(root, work, folderName, stagingKind))}) could not be deleted; your installed copy was not changed.");
                    return false;
                }
            }
            return true;
        }

        // Checks one archive entry and, when it is safe and readable, adds it with its staging path.
        // Throws, having written nothing, when the entry could not be extracted.
        private static void PlanEntry(Stream zip, MiniZip.Entry zipEntry, string stagingDir, List<KeyValuePair<MiniZip.Entry, string>> planned)
        {
            var destPath = Path.GetFullPath(Path.Combine(stagingDir, zipEntry.Name));
            if (!IsUnder(destPath, stagingDir))
            {
                MumLog.Warning($"Skipping unsafe ZIP entry: {zipEntry.Name}");
                return;
            }
            if (!IsDirectoryEntry(zipEntry))
                MiniZip.Validate(zip, zipEntry);
            planned.Add(new KeyValuePair<MiniZip.Entry, string>(zipEntry, destPath));
        }

        private static void CarrySpriteCache(string folderName, string backupDir, string targetDir)
        {
            var from = Path.Combine(backupDir, SpriteCacheDirName);
            var to = Path.Combine(targetDir, SpriteCacheDirName);
            if (!Directory.Exists(from)) return;
            if (Directory.Exists(to))
            {
                MumLog.Warning($"Suite: {folderName}: the new copy already has a {SpriteCacheDirName} folder; the old one is dropped.");
                return;
            }
            try
            {
                Directory.Move(from, to);
            }
            catch (Exception ex)
            {
                MumLog.Warning($"Suite: {folderName}: could not carry {SpriteCacheDirName} over ({ex.Message}); it is rebuilt on the next launch.");
            }
        }

        /// <summary>
        /// Recursively deletes one owned folder, and nothing else: the path comes from TryOwnedPath,
        /// rebuilt and checked immediately before the delete. Returns true when the folder no longer
        /// exists.
        /// </summary>
        private static bool DeleteOwnedFolder(string root, string work, string folderName, OwnedFolder kind)
        {
            return DeleteResolvedFolder(() => TryOwnedPath(root, work, folderName, kind, out string owned) ? owned : null);
        }

        /// <summary>
        /// The recursive delete of one owned folder, whichever layout owns it: resolve is called here,
        /// at the delete, and returns the rebuilt, checked path or null when the check refuses.
        /// </summary>
        private static bool DeleteResolvedFolder(Func<string> resolve)
        {
            var path = resolve();
            if (path == null) return false;
            if (!Directory.Exists(path)) return true;
            try
            {
                // A link (the mod folder was a junction, say) is removed as a link, never followed: a
                // recursive delete through it would empty the folder it points at.
                Directory.Delete(path, !IsLink(path));
            }
            catch (Exception ex)
            {
                MumLog.Warning($"Suite: could not delete {path}: {ex.Message}");
            }
            return !Directory.Exists(path);
        }

        /// <summary>
        /// The path of one owned folder, rebuilt here from the constants, the roots and the folder
        /// name, never taken from StagingPathFor or BackupPathFor (so a wrong formula there can never
        /// aim a delete), and checked against the owned layout: exactly &lt;work root&gt;/staging/&lt;name&gt;
        /// or &lt;work root&gt;/old/&lt;name&gt; with both outside plugins/, or exactly
        /// plugins/&lt;name&gt;__mum_staging or plugins/&lt;name&gt;__mum_old, and never the plugin folder
        /// itself. False, logged, when the check fails. Call it immediately before every delete.
        /// </summary>
        private static bool TryOwnedPath(string root, string work, string folderName, OwnedFolder kind, out string path)
        {
            path = null;
            if (!IsPlainFolderName(folderName))
            {
                MumLog.Error($"Suite: refused to delete a folder for {folderName}: not one plain folder name.");
                return false;
            }
            string candidate;
            bool owned;
            if (work != null && (kind == OwnedFolder.Staging || kind == OwnedFolder.Backup))
            {
                candidate = Path.GetFullPath(Path.Combine(work, kind == OwnedFolder.Staging ? StagingDirName : OldDirName, folderName));
                owned = IsOutside(work, root) && IsOutside(candidate, root);
            }
            else
            {
                bool staging = kind == OwnedFolder.Staging || kind == OwnedFolder.LegacyStaging;
                candidate = Path.GetFullPath(Path.Combine(root, folderName + (staging ? StagingSuffix : BackupSuffix)));
                owned = IsUnder(candidate, root) && SamePath(Path.GetDirectoryName(candidate), root);
            }
            if (!owned || SamePath(candidate, TargetPathFor(root, folderName)))
            {
                MumLog.Error($"Suite: refused to delete {candidate}: not a folder this installer owns.");
                return false;
            }
            path = candidate;
            return true;
        }

        private static string OwnedPath(string root, string work, string folderName, OwnedFolder kind)
        {
            switch (kind)
            {
                case OwnedFolder.Staging: return StagingPathFor(root, work, folderName);
                case OwnedFolder.Backup: return BackupPathFor(root, work, folderName);
                case OwnedFolder.LegacyStaging: return TargetPathFor(root, folderName) + StagingSuffix;
                default: return TargetPathFor(root, folderName) + BackupSuffix;
            }
        }

        public static string TargetPathFor(string root, string folderName) =>
            Path.GetFullPath(Path.Combine(root, folderName));

        /// <summary>Where a new copy is extracted: the work root's staging/, or beside the plugin folder when work is null.</summary>
        public static string StagingPathFor(string root, string work, string folderName) =>
            work == null ? TargetPathFor(root, folderName) + StagingSuffix
                         : Path.GetFullPath(Path.Combine(work, StagingDirName, folderName));

        /// <summary>Where the replaced copy waits between the two renames: the work root's old/, or beside the plugin folder when work is null.</summary>
        public static string BackupPathFor(string root, string work, string folderName) =>
            work == null ? TargetPathFor(root, folderName) + BackupSuffix
                         : Path.GetFullPath(Path.Combine(work, OldDirName, folderName));

        // A work root inside plugins/ (or plugins/ itself) would put the staged copy where loaders read.
        private static bool LayoutIsSafe(string root, string work) => work == null || IsOutside(work, root);

        // One plain folder name: no separator, no "." or "..", nothing Windows would trim or reject.
        // Path.GetFullPath would otherwise let a name such as "..\plugins\X" stay inside plugins/
        // while its staging and backup paths climbed out of the work root's subfolders.
        private static bool IsPlainFolderName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name == "." || name == "..") return false;
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
            if (name.IndexOf(Path.DirectorySeparatorChar) >= 0 || name.IndexOf(Path.AltDirectorySeparatorChar) >= 0) return false;
            return name.TrimEnd('.', ' ') == name;
        }

        // "BepInEx/mum_work/old/<Folder>": the path relative to the folder that holds plugins/, named
        // by that folder, with forward slashes, for the status line; the full path when it is elsewhere.
        private static string DisplayPath(string root, string path)
        {
            var holder = Path.GetDirectoryName(TrimSeparators(root));
            if (string.IsNullOrEmpty(holder) || !IsUnder(path, holder)) return path;
            return (Path.GetFileName(TrimSeparators(holder)) + "/" + path.Substring(TrimSeparators(holder).Length + 1)).Replace('\\', '/');
        }

        // Strictly inside root. The trailing separator is what stops a sibling such as
        // "plugins/CSFF_Mod_Framework_old" passing as a prefix of "plugins/CSFF_Mod_Framework".
        private static bool IsUnder(string fullPath, string rootFullPath)
        {
            var rootWithSep = TrimSeparators(rootFullPath) + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase);
        }

        // Neither inside root nor root itself.
        private static bool IsOutside(string fullPath, string rootFullPath) =>
            !IsUnder(fullPath, rootFullPath) && !SamePath(fullPath, rootFullPath);

        private static bool SamePath(string a, string b) =>
            string.Equals(TrimSeparators(a), TrimSeparators(b), StringComparison.OrdinalIgnoreCase);

        private static string TrimSeparators(string path) =>
            path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        private static bool IsDirectoryEntry(MiniZip.Entry zipEntry) =>
            zipEntry.Name.EndsWith("/") || zipEntry.Name.EndsWith("\\");
    }
}
