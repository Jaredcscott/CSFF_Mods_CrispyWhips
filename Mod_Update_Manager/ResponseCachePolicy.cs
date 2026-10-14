namespace mod_update_manager
{
    /// <summary>
    /// Decides whether a Nexus answer already held in memory may stand in for a request.
    /// Startup, scheduled and discovery lookups use the 24-hour cache; the Check for Updates and
    /// Re-check Rate-Limited buttons bypass it, so a release from this morning shows at once.
    ///
    /// Host-free: no BepInEx or UnityEngine reference. Development_Tools/Tests/MUM-CompiledLogic.Tests.ps1
    /// compiles and drives it.
    /// </summary>
    public static class ResponseCachePolicy
    {
        /// <summary>How long a cached answer stands in for a request, in hours.</summary>
        public const int ExpiryHours = 24;

        /// <param name="cachingEnabled">The CachingEnabled option.</param>
        /// <param name="hasEntry">True when an answer for this mod is held.</param>
        /// <param name="ageHours">Hours since that answer was fetched; ignored without an entry.</param>
        /// <param name="bypass">True for a check the player asked for with a button.</param>
        public static bool ShouldUseCache(bool cachingEnabled, bool hasEntry, double ageHours, bool bypass)
        {
            return cachingEnabled && hasEntry && !bypass && ageHours < ExpiryHours;
        }
    }
}
