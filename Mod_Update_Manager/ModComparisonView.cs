using System;
using System.Collections.Generic;

namespace mod_update_manager
{
    public class ModComparisonView
    {
        public class UpdateStats
        {
            public int TotalMods { get; set; }
            public int ModsUpToDate { get; set; }
            public int ModsNeedingUpdate { get; set; }
            public int CriticalUpdates { get; set; }
            public int ModsUnchecked { get; set; }
        }

        public static UpdateStats GenerateStats(List<InstalledModInfo> mods)
        {
            var stats = new UpdateStats
            {
                TotalMods = mods.Count,
                ModsUpToDate = 0,
                ModsNeedingUpdate = 0,
                CriticalUpdates = 0,
                ModsUnchecked = 0
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
                }
                else
                {
                    stats.ModsUpToDate++;
                }
            }

            return stats;
        }

        private static int CountVersionsBehind(string currentVersion, string latestVersion)
        {
            try
            {
                var curParts = (currentVersion ?? "").Split('.');
                var latParts = (latestVersion ?? "").Split('.');
                int maxLen = Math.Max(curParts.Length, latParts.Length);
                for (int i = 0; i < maxLen; i++)
                {
                    int cur = i < curParts.Length && int.TryParse(curParts[i], out int c) ? c : 0;
                    int lat = i < latParts.Length && int.TryParse(latParts[i], out int l) ? l : 0;
                    if (lat != cur)
                        return Math.Max(0, lat - cur);
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

                if (currentParts.Length > 0 && latestParts.Length > 0 &&
                    int.TryParse(currentParts[0], out int curMajor) &&
                    int.TryParse(latestParts[0], out int latMajor))
                {
                    return latMajor > curMajor;
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
            var behind = CountVersionsBehind(currentVersion, latestVersion);
            return behind > 3 || DetectBreakingChanges(currentVersion, latestVersion);
        }
    }
}
