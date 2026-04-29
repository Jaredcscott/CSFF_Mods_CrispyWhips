using System;
using System.Collections.Generic;

namespace mod_update_manager
{
    /// <summary>
    /// Provides comparison data between mod versions
    /// </summary>
    public class ModComparisonView
    {
        public class ComparisonInfo
        {
            public string ModName { get; set; }
            public string CurrentVersion { get; set; }
            public string LatestVersion { get; set; }
            public int VersionsBehind { get; set; }
            public string Status { get; set; } // "Up-to-date", "Minor Update", "Major Update", "Outdated"
            public double EstimatedDownloadSizeMb { get; set; }
            public string LastUpdateDate { get; set; }
            public bool HasBreakingChanges { get; set; }
            public List<string> ChangesIncluded { get; set; }
        }

        public class UpdateStats
        {
            public int TotalMods { get; set; }
            public int ModsUpToDate { get; set; }
            public int ModsNeedingUpdate { get; set; }
            public int CriticalUpdates { get; set; }
            public int ModsUnchecked { get; set; }
            public double TotalDownloadSizeMb { get; set; }
            public double EstimatedUpdateTime { get; set; }
        }

        /// <summary>
        /// Generate comparison info for a mod
        /// </summary>
        public static ComparisonInfo GenerateComparison(InstalledModInfo mod)
        {
            var status = DetermineStatus(mod.Version, mod.LatestVersion);
            var versionsBehind = CountVersionsBehind(mod.Version, mod.LatestVersion);

            return new ComparisonInfo
            {
                ModName = mod.Name,
                CurrentVersion = mod.Version,
                LatestVersion = mod.LatestVersion ?? "Unknown",
                VersionsBehind = versionsBehind,
                Status = status,
                EstimatedDownloadSizeMb = 5.0, // Placeholder - would be fetched from API
                LastUpdateDate = System.DateTime.Now.ToString("yyyy-MM-dd"),
                HasBreakingChanges = DetectBreakingChanges(mod.Version, mod.LatestVersion),
                ChangesIncluded = new List<string>()
            };
        }

        /// <summary>
        /// Generate update statistics for all mods
        /// </summary>
        public static UpdateStats GenerateStats(List<InstalledModInfo> mods)
        {
            var stats = new UpdateStats
            {
                TotalMods = mods.Count,
                ModsUpToDate = 0,
                ModsNeedingUpdate = 0,
                CriticalUpdates = 0,
                ModsUnchecked = 0,
                TotalDownloadSizeMb = 0,
                EstimatedUpdateTime = 0
            };

            foreach (var mod in mods)
            {
                if (mod.CheckFailed || string.IsNullOrEmpty(mod.LatestVersion))
                {
                    stats.ModsUnchecked++;
                }
                else if (mod.NeedsUpdate)
                {
                    stats.ModsNeedingUpdate++;
                    if (IsCriticalUpdate(mod.Version, mod.LatestVersion))
                        stats.CriticalUpdates++;

                    stats.TotalDownloadSizeMb += 5.0; // Placeholder
                }
                else
                {
                    stats.ModsUpToDate++;
                }
            }

            // Estimate update time (rough: ~10s per mod + 1MB per 1s)
            stats.EstimatedUpdateTime = (stats.ModsNeedingUpdate * 10) + (stats.TotalDownloadSizeMb);

            return stats;
        }

        /// <summary>
        /// Get detailed comparison text
        /// </summary>
        public static string GetComparisonText(InstalledModInfo mod)
        {
            if (mod.CheckFailed)
                return $"Check failed: {mod.CheckError}";

            if (!mod.NeedsUpdate)
                return $"{mod.Name} is up to date (v{mod.Version})";

            var comparison = GenerateComparison(mod);
            var status = comparison.Status;
            var changeNote = comparison.HasBreakingChanges ? " ⚠ Contains breaking changes" : "";

            return $"{mod.Name}: v{mod.Version} → v{mod.LatestVersion} ({status}){changeNote}";
        }

        private static string DetermineStatus(string currentVersion, string latestVersion)
        {
            if (string.IsNullOrEmpty(latestVersion))
                return "Unknown";

            if (!VersionComparer.NeedsUpdate(currentVersion, latestVersion))
                return "Up-to-date";

            var behind = CountVersionsBehind(currentVersion, latestVersion);
            return behind > 5 ? "Outdated" : behind > 2 ? "Major Update" : "Minor Update";
        }

        private static int CountVersionsBehind(string currentVersion, string latestVersion)
        {
            try
            {
                // Simple version counting: count dots/segments difference
                var currentParts = currentVersion?.Split('.') ?? new string[0];
                var latestParts = latestVersion?.Split('.') ?? new string[0];

                if (currentParts.Length == 0 || latestParts.Length == 0)
                    return 0;

                if (int.TryParse(currentParts[0], out int curMajor) &&
                    int.TryParse(latestParts[0], out int latMajor))
                {
                    return Math.Max(0, latMajor - curMajor);
                }

                return 0;
            }
            catch
            {
                return 0;
            }
        }

        private static bool DetectBreakingChanges(string currentVersion, string latestVersion)
        {
            if (string.IsNullOrEmpty(currentVersion) || string.IsNullOrEmpty(latestVersion))
                return false;

            try
            {
                var currentParts = currentVersion.Split('.');
                var latestParts = latestVersion.Split('.');

                if (currentParts.Length > 0 && latestParts.Length > 0)
                {
                    if (int.TryParse(currentParts[0], out int curMajor) &&
                        int.TryParse(latestParts[0], out int latMajor))
                    {
                        return latMajor > curMajor;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsCriticalUpdate(string currentVersion, string latestVersion)
        {
            // Critical if it's a major version jump or more than 5 versions behind
            var behind = CountVersionsBehind(currentVersion, latestVersion);
            return behind > 3 || DetectBreakingChanges(currentVersion, latestVersion);
        }
    }
}
