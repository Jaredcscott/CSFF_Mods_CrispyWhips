using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace mod_update_manager
{
    /// <summary>
    /// Manages backups of mod versions
    /// </summary>
    public class BackupManager
    {
        private string _backupPath;

        public event Action<string> OnBackupCreated;
        public event Action<string> OnBackupRestored;
        public event Action<string> OnBackupDeleted;

        public BackupManager(string backupPath)
        {
            _backupPath = backupPath;
            if (!Directory.Exists(_backupPath))
            {
                Directory.CreateDirectory(_backupPath);
            }
        }

        /// <summary>
        /// Create a backup of a mod before updating
        /// </summary>
        public bool CreateBackup(string modFolderPath, string modName, string version)
        {
            try
            {
                if (!Directory.Exists(modFolderPath))
                    return false;

                var modBackupPath = Path.Combine(_backupPath, modName);
                if (!Directory.Exists(modBackupPath))
                {
                    Directory.CreateDirectory(modBackupPath);
                }

                var versionBackupPath = Path.Combine(modBackupPath, $"{version}_{System.DateTime.Now:yyyy-MM-dd_HH-mm-ss}");

                if (Directory.Exists(versionBackupPath))
                {
                    Directory.Delete(versionBackupPath, true);
                }

                CopyDirectory(modFolderPath, versionBackupPath);

                OnBackupCreated?.Invoke($"Created backup of {modName} v{version}");
                Plugin.Logger.LogInfo($"Backup created: {versionBackupPath}");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"Failed to create backup: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Restore a mod from backup
        /// </summary>
        public bool RestoreBackup(string modFolderPath, string modName, string backupPath)
        {
            try
            {
                if (!Directory.Exists(backupPath))
                    return false;

                // Clear current mod folder
                if (Directory.Exists(modFolderPath))
                {
                    Directory.Delete(modFolderPath, true);
                }

                // Restore from backup
                CopyDirectory(backupPath, modFolderPath);

                OnBackupRestored?.Invoke($"Restored {modName} from backup");
                Plugin.Logger.LogInfo($"Backup restored: {modFolderPath}");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"Failed to restore backup: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get all backups for a mod
        /// </summary>
        public List<string> GetModBackups(string modName)
        {
            try
            {
                var modBackupPath = Path.Combine(_backupPath, modName);
                if (!Directory.Exists(modBackupPath))
                    return new List<string>();

                return Directory.GetDirectories(modBackupPath)
                    .OrderByDescending(d => Directory.GetCreationTime(d))
                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        /// <summary>
        /// Delete a backup
        /// </summary>
        public bool DeleteBackup(string backupPath)
        {
            try
            {
                if (Directory.Exists(backupPath))
                {
                    Directory.Delete(backupPath, true);
                    OnBackupDeleted?.Invoke(Path.GetFileName(backupPath));
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"Failed to delete backup: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get total backup size for a mod in MB
        /// </summary>
        public double GetBackupSize(string modName)
        {
            try
            {
                var modBackupPath = Path.Combine(_backupPath, modName);
                if (!Directory.Exists(modBackupPath))
                    return 0;

                var totalBytes = Directory.GetFiles(modBackupPath, "*", SearchOption.AllDirectories)
                    .Sum(f => new FileInfo(f).Length);

                return totalBytes / (1024.0 * 1024.0); // Convert to MB
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Clean old backups keeping only the last N versions
        /// </summary>
        public int CleanOldBackups(string modName, int keepCount)
        {
            try
            {
                var backups = GetModBackups(modName);
                var toDelete = backups.Skip(keepCount).ToList();

                foreach (var backup in toDelete)
                {
                    DeleteBackup(backup);
                }

                return toDelete.Count;
            }
            catch
            {
                return 0;
            }
        }

        private void CopyDirectory(string sourceDir, string destDir)
        {
            if (!Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }

            foreach (var subDir in Directory.GetDirectories(sourceDir))
            {
                var destSubDir = Path.Combine(destDir, Path.GetFileName(subDir));
                CopyDirectory(subDir, destSubDir);
            }
        }
    }
}
