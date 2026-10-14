using System;
using System.Collections;
using System.IO;
using System.Reflection;
using BepInEx;

namespace mod_update_manager
{
    /// <summary>
    /// Installs an embedded suite ZIP into BepInEx/plugins/&lt;FolderName&gt;/. The in-game half:
    /// resolves the plugins folder, the work folder beside it and the embedded resource, then hands
    /// them to SuiteInstaller, which stages and verifies in BepInEx/mum_work/ and swaps, so the
    /// installed copy is never left half-replaced and no second copy sits where BepInEx loads.
    /// </summary>
    public static class ModSuiteExtractor
    {
        private static readonly string PluginsPath = Path.Combine(Paths.BepInExRootPath, "plugins");
        // A sibling of plugins/, outside every BepInEx scan. SuiteInstaller.ChooseWorkRoot probes it
        // at the first Apply of the session and answers null (stage inside plugins/) when it cannot
        // rename into plugins/.
        private static readonly string WorkRootPath = Path.Combine(Paths.BepInExRootPath, SuiteInstaller.WorkRootDirName);

        /// <summary>
        /// Installs one embedded suite mod a step at a time: each MoveNext runs one step of
        /// SuiteInstaller.InstallSteps and returns true while there is more to do, so the caller can let
        /// the game draw a frame between steps (UpdateManagerUI.ApplySuiteCoroutine waits one frame
        /// after each). progress carries the phase and the counts for the status line and, once
        /// MoveNext returns false, the outcome: Installed when the mod was installed. keep is the
        /// player's "keep the previous version" option, read once when the Apply started.
        /// </summary>
        public static IEnumerator ExtractSteps(SuiteModEntry entry, SuiteKeepPolicy keep, Action<string> status, SuiteInstallProgress progress)
        {
            entry.LastInstallIncomplete = false;

            Stream stream = null;
            string workRoot = null;
            bool failed = false;
            try
            {
                stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(entry.ResourceKey);
                if (stream == null)
                {
                    Plugin.Logger.LogError($"ModSuiteExtractor: embedded resource {entry.ResourceKey} not found; {entry.FolderName} left as installed.");
                    status?.Invoke($"ERROR: embedded resource not found for {entry.FolderName}");
                    failed = true;
                }
                else
                {
                    workRoot = SuiteInstaller.ChooseWorkRoot(PluginsPath, WorkRootPath);
                }
            }
            catch (Exception ex)
            {
                status?.Invoke($"FAILED: {entry.DisplayName} - {ex.Message}");
                Plugin.Logger.LogError($"ModSuiteExtractor: {entry.FolderName}: {ex}");
                failed = true;
            }
            if (failed)
            {
                stream?.Dispose();
                yield break;
            }

            try
            {
                var steps = SuiteInstaller.InstallSteps(stream, PluginsPath, workRoot, entry.FolderName, entry.DisplayName, status, progress, keep);
                while (true)
                {
                    bool more;
                    try
                    {
                        more = steps.MoveNext();
                    }
                    catch (Exception ex)
                    {
                        // progress.Outcome still says where the mod stands: untouched before the swap.
                        status?.Invoke($"FAILED: {entry.DisplayName} - {ex.Message}");
                        Plugin.Logger.LogError($"ModSuiteExtractor: {entry.FolderName}: {ex}");
                        more = false;
                    }
                    if (!more) break;
                    yield return progress;
                }
            }
            finally
            {
                stream.Dispose();
            }

            // Only a swap that could neither finish nor be undone leaves the folder missing;
            // every other failure leaves the installed copy exactly as it was.
            entry.LastInstallIncomplete = progress.Outcome == SuiteInstallOutcome.FailedFolderMissing;
            if (progress.Outcome != SuiteInstallOutcome.Installed)
                yield break;

            status?.Invoke($"{entry.DisplayName} installed successfully.");
            Plugin.Logger.LogInfo($"Suite: installed {entry.FolderName} v{entry.EmbeddedVersion}");
        }

        /// <summary>
        /// Rolls one suite mod back to a kept copy a step at a time (SuiteInstaller.RollbackSteps), the
        /// way ExtractSteps installs: each MoveNext runs one step and returns true while there is more,
        /// progress carries the phase and, once it returns false, the outcome (Installed when the kept
        /// copy is in place). The kept copy is the player's own: it is chosen from the versions
        /// SuiteInstaller.ListKeptVersions offered, and SuiteInstaller checks it again before the swap.
        /// </summary>
        public static IEnumerator RollbackSteps(SuiteModEntry entry, string version, SuiteKeepPolicy keep, Action<string> status, SuiteInstallProgress progress)
        {
            string workRoot = null;
            bool failed = false;
            try
            {
                workRoot = SuiteInstaller.ChooseWorkRoot(PluginsPath, WorkRootPath);
            }
            catch (Exception ex)
            {
                status?.Invoke($"FAILED: {entry.DisplayName} - {ex.Message}");
                Plugin.Logger.LogError($"ModSuiteExtractor: rollback of {entry.FolderName}: {ex}");
                failed = true;
            }
            if (failed) yield break;

            var steps = SuiteInstaller.RollbackSteps(PluginsPath, workRoot, entry.FolderName, entry.DisplayName, version, keep, status, progress);
            while (true)
            {
                bool more;
                try
                {
                    more = steps.MoveNext();
                }
                catch (Exception ex)
                {
                    // progress.Outcome still says where the mod stands: untouched before the swap.
                    status?.Invoke($"FAILED: {entry.DisplayName} - {ex.Message}");
                    Plugin.Logger.LogError($"ModSuiteExtractor: rollback of {entry.FolderName}: {ex}");
                    more = false;
                }
                if (!more) break;
                yield return progress;
            }

            entry.LastInstallIncomplete = progress.Outcome == SuiteInstallOutcome.FailedFolderMissing;
            if (progress.Outcome != SuiteInstallOutcome.Installed)
                yield break;

            status?.Invoke(string.Format(RolledBackFormat, entry.DisplayName, version));
            Plugin.Logger.LogInfo($"Suite: rolled {entry.FolderName} back to v{version}");
        }

        // The status line when a rollback has finished. Row Mod_Update_Manager_SuiteRolledBack in
        // Localization/SimpEn.csv and SimpCn.csv; nothing reads it until runtime localization lands.
        private const string RolledBackFormat = "{0} rolled back to v{1}.";
    }
}
