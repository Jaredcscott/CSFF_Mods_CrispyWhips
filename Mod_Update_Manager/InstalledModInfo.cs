// Host-free: no BepInEx or UnityEngine reference. The row ModScanner fills for each installed mod, moved
// here unchanged (ModScanner.cs reads BepInEx's Paths and cannot be compiled outside the game) so that
// ModPreferences and ModSorter, which only read and order these rows, can be compiled and driven by
// Development_Tools/Tests/MUM-CompiledLogic.Tests.ps1.
namespace mod_update_manager
{
    /// <summary>
    /// Represents information about an installed mod
    /// </summary>
    public class InstalledModInfo
    {
        public string FolderName { get; set; }
        public string FolderPath { get; set; }
        public string Name { get; set; }
        public string Author { get; set; }
        public string Version { get; set; }
        public string NexusModId { get; set; }  // Optional: can be specified in ModInfo.json
        public bool HasModInfo { get; set; }

        // Version comparison results (populated after Nexus check)
        public string LatestVersion { get; set; }
        public bool NeedsUpdate { get; set; }
        public bool CheckFailed { get; set; }
        public string CheckError { get; set; }
        public string NexusUrl { get; set; }  // Nexus page URL for this mod

        // Set when the most recent check for this mod hit Nexus's HTTP 429 rate limit
        // (rather than any other failure - missing ID, 404, parse error, etc.); cleared
        // on the next successful check. Stable marker for "Re-check rate-limited" (N5) -
        // never key that action off CheckError's display string.
        public bool RateLimited { get; set; }

        // Nexus metadata (populated after Nexus check)
        public string Summary { get; set; }
        public int EndorsementCount { get; set; }
        public bool IsMajorVersionUpdate { get; set; }

        // User preferences (applied from ModPreferences)
        public bool IsIgnored { get; set; }
        public bool IsFavorited { get; set; }
        public string Notes { get; set; }
    }
}
