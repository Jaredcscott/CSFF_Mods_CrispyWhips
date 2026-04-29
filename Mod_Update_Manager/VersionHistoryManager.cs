using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace mod_update_manager
{
    /// <summary>
    /// Tracks version history for installed mods
    /// </summary>
    public class VersionHistoryManager
    {
        private string _historyPath;
        private Dictionary<string, List<VersionRecord>> _versionHistory;

        public class VersionRecord
        {
            public string Version { get; set; }
            public System.DateTime InstalledDate { get; set; }
            public string PreviousVersion { get; set; }
            public bool IsBackedUp { get; set; }
        }

        public VersionHistoryManager(string historyPath)
        {
            _historyPath = historyPath;
            _versionHistory = new Dictionary<string, List<VersionRecord>>();
            LoadHistory();
        }

        /// <summary>
        /// Record a version change
        /// </summary>
        public void RecordVersionChange(string modName, string newVersion, string previousVersion = null)
        {
            try
            {
                if (!_versionHistory.ContainsKey(modName))
                {
                    _versionHistory[modName] = new List<VersionRecord>();
                }

                var record = new VersionRecord
                {
                    Version = newVersion,
                    InstalledDate = System.DateTime.Now,
                    PreviousVersion = previousVersion
                };

                _versionHistory[modName].Add(record);
                SaveHistory();

                Plugin.Logger.LogInfo($"Recorded version change for {modName}: {previousVersion} -> {newVersion}");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"Failed to record version: {ex.Message}");
            }
        }

        /// <summary>
        /// Mark a version as backed up
        /// </summary>
        public void MarkAsBackedUp(string modName, string version)
        {
            try
            {
                if (_versionHistory.ContainsKey(modName))
                {
                    var record = _versionHistory[modName].FirstOrDefault(r => r.Version == version);
                    if (record != null)
                    {
                        record.IsBackedUp = true;
                        SaveHistory();
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"Failed to mark backup: {ex.Message}");
            }
        }

        /// <summary>
        /// Get version history for a mod
        /// </summary>
        public List<VersionRecord> GetHistory(string modName)
        {
            if (_versionHistory.ContainsKey(modName))
            {
                return _versionHistory[modName].OrderByDescending(r => r.InstalledDate).ToList();
            }
            return new List<VersionRecord>();
        }

        /// <summary>
        /// Get the current version and previous version
        /// </summary>
        public (string current, string previous) GetVersions(string modName)
        {
            if (!_versionHistory.ContainsKey(modName) || _versionHistory[modName].Count == 0)
                return (null, null);

            var history = _versionHistory[modName].OrderByDescending(r => r.InstalledDate).ToList();
            return (history[0].Version, history.Count > 1 ? history[1].Version : null);
        }

        /// <summary>
        /// Get all mods with version history
        /// </summary>
        public List<string> GetTrackedMods()
        {
            return _versionHistory.Keys.ToList();
        }

        /// <summary>
        /// Calculate how many updates a mod has received
        /// </summary>
        public int GetUpdateCount(string modName)
        {
            if (!_versionHistory.ContainsKey(modName))
                return 0;

            return _versionHistory[modName].Count - 1; // -1 for initial install
        }

        /// <summary>
        /// Get average time between updates in days
        /// </summary>
        public double GetAverageUpdateFrequency(string modName)
        {
            if (!_versionHistory.ContainsKey(modName) || _versionHistory[modName].Count < 2)
                return 0;

            var history = _versionHistory[modName].OrderBy(r => r.InstalledDate).ToList();
            var totalDays = (history[history.Count - 1].InstalledDate - history[0].InstalledDate).TotalDays;
            var updates = history.Count - 1;

            return updates > 0 ? totalDays / updates : 0;
        }

        private void LoadHistory()
        {
            try
            {
                if (!Directory.Exists(_historyPath))
                {
                    Directory.CreateDirectory(_historyPath);
                }

                var historyFile = Path.Combine(_historyPath, "version_history.json");
                if (!File.Exists(historyFile))
                    return;

                var json = File.ReadAllText(historyFile);
                var lines = json.Split('\n');

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    try
                    {
                        var parts = line.Split('|');
                        if (parts.Length >= 4)
                        {
                            var modName = parts[0];
                            var version = parts[1];
                            var prevVersion = parts[2];
                            var dateStr = parts[3];

                            if (!_versionHistory.ContainsKey(modName))
                                _versionHistory[modName] = new List<VersionRecord>();

                            if (System.DateTime.TryParse(dateStr, out var date))
                            {
                                _versionHistory[modName].Add(new VersionRecord
                                {
                                    Version = version,
                                    InstalledDate = date,
                                    PreviousVersion = prevVersion
                                });
                            }
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"Failed to load version history: {ex.Message}");
            }
        }

        private void SaveHistory()
        {
            try
            {
                if (!Directory.Exists(_historyPath))
                {
                    Directory.CreateDirectory(_historyPath);
                }

                var historyFile = Path.Combine(_historyPath, "version_history.json");
                var lines = new List<string>();

                foreach (var modName in _versionHistory.Keys)
                {
                    foreach (var record in _versionHistory[modName])
                    {
                        lines.Add($"{modName}|{record.Version}|{record.PreviousVersion}|{record.InstalledDate:o}");
                    }
                }

                File.WriteAllText(historyFile, string.Join("\n", lines));
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"Failed to save version history: {ex.Message}");
            }
        }
    }
}
