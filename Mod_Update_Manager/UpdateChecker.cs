using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace mod_update_manager
{
    /// <summary>
    /// Manages the update checking process for all installed mods
    /// </summary>
    public class UpdateChecker
    {
        private NexusApiClient _apiClient;
        private ModMappingManager _mappingManager;
        private NexusModDiscovery _modDiscovery;
        private ModPreferences _preferences;
        private List<InstalledModInfo> _installedMods;
        private bool _isChecking;
        private int _checksCompleted;
        private int _totalChecks;

        public event Action<InstalledModInfo> OnModChecked;
        public event Action<List<InstalledModInfo>> OnAllChecksComplete;
        public event Action<string> OnStatusUpdate;

        public bool IsChecking => _isChecking;
        public float Progress => _totalChecks > 0 ? (float)_checksCompleted / _totalChecks : 0f;
        public List<InstalledModInfo> InstalledMods => _installedMods;

        public UpdateChecker(NexusApiClient apiClient, ModMappingManager mappingManager, NexusModDiscovery modDiscovery = null, ModPreferences preferences = null)
        {
            _apiClient = apiClient;
            _mappingManager = mappingManager;
            _modDiscovery = modDiscovery;
            _preferences = preferences;
            _installedMods = new List<InstalledModInfo>();
        }

        /// <summary>
        /// Scans for installed mods and applies saved preferences
        /// </summary>
        public void ScanMods()
        {
            OnStatusUpdate?.Invoke("Scanning installed mods...");
            _installedMods = ModScanner.ScanInstalledMods();
            // The plugin set just changed under us, so the cached [BepInDependency] cross-reference
            // (N6, Conflicts tab) is stale; it recomputes lazily the next time that tab is drawn.
            DependencyValidator.Invalidate();
            if (_preferences != null)
                foreach (var m in _installedMods) _preferences.ApplyToMod(m);
            OnStatusUpdate?.Invoke($"Found {_installedMods.Count} installed mods");
        }

        /// <summary>
        /// Starts checking all mods for updates
        /// </summary>
        public void CheckAllMods(MonoBehaviour runner)
        {
            if (_isChecking)
            {
                Plugin.Logger.LogDebug("Update check already in progress");
                return;
            }

            if (!_apiClient.HasApiKey)
            {
                OnStatusUpdate?.Invoke("No API key configured - cannot check for updates");
                OnAllChecksComplete?.Invoke(_installedMods);
                return;
            }

            runner.StartCoroutine(CheckAllModsCoroutine());
        }

        private IEnumerator CheckAllModsCoroutine()
        {
            var modsToCheck = _installedMods.Where(m => GetNexusModId(m) != null && !m.IsIgnored).ToList();

            var invalidVersionMods = modsToCheck.Where(m =>
                m.Version == "Parse Error" || m.Version == "Unknown").ToList();
            foreach (var mod in invalidVersionMods)
            {
                mod.CheckFailed = true;
                mod.CheckError = "Mod version could not be determined";
                modsToCheck.Remove(mod);
            }

            yield return RunChecksCoroutine(modsToCheck, "mods with Nexus IDs");

            foreach (var mod in _installedMods.Where(m => GetNexusModId(m) == null))
            {
                // Don't list Mod_Update_Manager itself in the "Unable to Check" tab
                if (KnownModRegistry.IsSelfMod(mod.Name) || KnownModRegistry.IsSelfMod(mod.FolderName))
                    continue;
                mod.LatestVersion = "No Nexus ID";
                mod.CheckFailed = true;
                mod.CheckError = "No Nexus Mod ID configured";
            }

            var updatesAvailable = _installedMods.Count(m => m.NeedsUpdate);
            OnStatusUpdate?.Invoke($"Check complete! {updatesAvailable} updates available.");
            OnAllChecksComplete?.Invoke(_installedMods);
        }

        /// <summary>
        /// True when at least one already-checked mod's most recent result was an HTTP 429
        /// from Nexus (<see cref="InstalledModInfo.RateLimited"/>). Drives the "Re-check
        /// rate-limited" button's visibility (N5).
        /// </summary>
        public bool HasRateLimitedMods => _installedMods.Any(m => m.RateLimited);

        /// <summary>
        /// Re-runs the update check for ONLY the mods whose last check hit Nexus's 429 rate
        /// limit - never the full list. Reuses the same staggered check path (and its 0.5s
        /// delay between requests) as a normal full check. Distinct from - and does not
        /// implement - the separate 429 auto-backoff idea, which stays unbuilt by design.
        /// </summary>
        public void RecheckRateLimitedMods(MonoBehaviour runner)
        {
            if (_isChecking)
            {
                Plugin.Logger.LogDebug("Update check already in progress");
                return;
            }

            if (!_apiClient.HasApiKey)
            {
                OnStatusUpdate?.Invoke("No API key configured - cannot check for updates");
                return;
            }

            var modsToRecheck = _installedMods.Where(m => m.RateLimited && GetNexusModId(m) != null).ToList();
            if (modsToRecheck.Count == 0)
            {
                OnStatusUpdate?.Invoke("No rate-limited mods to re-check.");
                return;
            }

            runner.StartCoroutine(RecheckRateLimitedModsCoroutine(modsToRecheck));
        }

        private IEnumerator RecheckRateLimitedModsCoroutine(List<InstalledModInfo> modsToRecheck)
        {
            yield return RunChecksCoroutine(modsToRecheck, "rate-limited mod(s)");

            var stillRateLimited = modsToRecheck.Count(m => m.RateLimited);
            Plugin.Logger.LogInfo($"Re-checked {modsToRecheck.Count} rate-limited mod(s); {stillRateLimited} still rate limited.");
            OnStatusUpdate?.Invoke(stillRateLimited > 0
                ? $"Re-check complete: {stillRateLimited} mod(s) still rate limited."
                : "Re-check complete: all previously rate-limited mods checked successfully.");
            OnAllChecksComplete?.Invoke(_installedMods);
        }

        /// <summary>
        /// Shared staggered-check loop used by both a full <see cref="CheckAllModsCoroutine"/>
        /// pass and a filtered <see cref="RecheckRateLimitedModsCoroutine"/> pass. Drives
        /// <see cref="_isChecking"/>/<see cref="_checksCompleted"/>/<see cref="_totalChecks"/>
        /// for the shared <see cref="Progress"/> property, so only one such loop may run at a
        /// time (both callers early-out on <see cref="_isChecking"/>).
        /// </summary>
        private IEnumerator RunChecksCoroutine(List<InstalledModInfo> modsToCheck, string statusContext)
        {
            _isChecking = true;
            _checksCompleted = 0;
            _totalChecks = modsToCheck.Count;
            OnStatusUpdate?.Invoke($"Checking {_totalChecks} {statusContext}...");

            // Stagger requests by 0.5s to avoid 429 rate-limit bursts on large mod lists.
            foreach (var mod in modsToCheck)
            {
                var nexusId = GetNexusModId(mod);
                mod.NexusUrl = KnownModRegistry.GetNexusUrl(nexusId);

                _apiClient.GetModInfo(nexusId, (response, error, isRateLimited) =>
                {
                    if (response != null)
                    {
                        mod.LatestVersion = response.Version;
                        mod.Summary = response.Summary;
                        mod.EndorsementCount = response.EndorsementCount;
                        mod.RateLimited = false;

                        var equivalent = KnownModRegistry.GetVersionEquivalent(nexusId, mod.Version);
                        bool isEquivalent = equivalent != null &&
                            VersionComparer.Compare(equivalent, response.Version) == 0;

                        mod.NeedsUpdate = !isEquivalent && VersionComparer.NeedsUpdate(mod.Version, response.Version);
                        mod.IsMajorVersionUpdate = mod.NeedsUpdate &&
                            ModComparisonView.IsMajorUpdate(mod.Version, response.Version);
                        mod.CheckFailed = false;

                        // Deliberately NOT persisted to the user mappings file. That file outranks
                        // KnownModRegistry in GetNexusModId, so copying every checked ID into it (as
                        // this did through 2.1.58) froze each registry ID for good and hid any later
                        // registry correction, and rewrote the file once per response.

                        Plugin.Logger.LogDebug($"{mod.Name}: {mod.Version} -> {response.Version} (Needs update: {mod.NeedsUpdate})");
                    }
                    else
                    {
                        mod.CheckFailed = true;
                        mod.CheckError = error;
                        mod.RateLimited = isRateLimited;
                        Plugin.Logger.LogDebug($"Failed to check {mod.Name}: {error}");
                    }

                    _checksCompleted++;
                    OnModChecked?.Invoke(mod);
                });

                yield return new WaitForSecondsRealtime(0.5f);
            }

            // Poll until all staggered requests finish
            while (_checksCompleted < _totalChecks)
            {
                OnStatusUpdate?.Invoke($"Checking mods... ({_checksCompleted}/{_totalChecks})");
                yield return new WaitForSecondsRealtime(0.2f);
            }

            _isChecking = false;

            // Single coalesced disk write at the end of the pass — replaces the
            // per-response SaveDiskCache that used to hitch the main thread
            // dozens of times during a fresh startup update check.
            _apiClient.FlushDiskCache();
        }

        /// <summary>
        /// Gets the Nexus Mod ID for a mod (from ModInfo.json, mapping file, discovered mods, or known registry)
        /// </summary>
        private string GetNexusModId(InstalledModInfo mod)
        {
            // 1. Check mod's own ModInfo.json
            if (!string.IsNullOrEmpty(mod.NexusModId))
                return mod.NexusModId;

            // 2. Check user mappings file
            var mapped = _mappingManager.GetNexusModId(mod.Name)
                ?? _mappingManager.GetNexusModId(mod.FolderName);
            if (mapped != null)
                return mapped;

            // 3. Check discovered mods (from background Nexus ID scanning)
            if (_modDiscovery != null)
            {
                var discovered = _modDiscovery.GetModIdFromDiscovery(mod.Name)
                    ?? _modDiscovery.GetModIdFromDiscovery(mod.FolderName);
                if (discovered != null)
                    return discovered;
            }

            // 4. Check built-in known mods registry
            var known = KnownModRegistry.GetNexusModId(mod.Name)
                ?? KnownModRegistry.GetNexusModId(mod.FolderName);
            // "self" sentinel means this is Mod_Update_Manager itself — treat as no ID
            return known == "self" ? null : known;
        }

        /// <summary>
        /// Gets mods that need updates
        /// </summary>
        public List<InstalledModInfo> GetModsNeedingUpdate()
        {
            return _installedMods.Where(m => m.NeedsUpdate).ToList();
        }

        /// <summary>
        /// Gets mods that are up to date
        /// </summary>
        public List<InstalledModInfo> GetUpToDateMods()
        {
            return _installedMods.Where(m => !m.NeedsUpdate && !m.CheckFailed && !string.IsNullOrEmpty(m.LatestVersion)).ToList();
        }

        /// <summary>
        /// Gets mods that couldn't be checked
        /// </summary>
        public List<InstalledModInfo> GetUncheckedMods()
        {
            return _installedMods.Where(m =>
                (m.CheckFailed || string.IsNullOrEmpty(m.LatestVersion)) &&
                !KnownModRegistry.IsSelfMod(m.Name) && !KnownModRegistry.IsSelfMod(m.FolderName)).ToList();
        }
    }
}
