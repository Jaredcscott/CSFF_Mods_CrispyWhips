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

        public UpdateChecker(NexusApiClient apiClient, ModMappingManager mappingManager, NexusModDiscovery modDiscovery = null)
        {
            _apiClient = apiClient;
            _mappingManager = mappingManager;
            _modDiscovery = modDiscovery;
            _installedMods = new List<InstalledModInfo>();
        }

        /// <summary>
        /// Scans for installed mods
        /// </summary>
        public void ScanMods()
        {
            OnStatusUpdate?.Invoke("Scanning installed mods...");
            _installedMods = ModScanner.ScanInstalledMods();
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
            _isChecking = true;
            _checksCompleted = 0;

            var modsToCheck = _installedMods.Where(m => GetNexusModId(m) != null).ToList();

            var invalidVersionMods = modsToCheck.Where(m =>
                m.Version == "Parse Error" || m.Version == "Unknown").ToList();
            foreach (var mod in invalidVersionMods)
            {
                mod.CheckFailed = true;
                mod.CheckError = "Mod version could not be determined";
                modsToCheck.Remove(mod);
            }
            _totalChecks = modsToCheck.Count;
            OnStatusUpdate?.Invoke($"Checking {_totalChecks} mods with Nexus IDs...");

            // Fire all requests concurrently — no sequential waiting or per-request delays.
            // Nexus API allows 50 req/hour (free) or higher (premium); with ~20 mods we're well under.
            foreach (var mod in modsToCheck)
            {
                var nexusId = GetNexusModId(mod);
                mod.NexusUrl = KnownModRegistry.GetNexusUrl(nexusId);

                _apiClient.GetModInfo(nexusId, (response, error) =>
                {
                    if (response != null)
                    {
                        mod.LatestVersion = response.Version;

                        var equivalent = KnownModRegistry.GetVersionEquivalent(nexusId, mod.Version);
                        bool isEquivalent = equivalent != null &&
                            VersionComparer.Compare(equivalent, response.Version) == 0;

                        mod.NeedsUpdate = !isEquivalent && VersionComparer.NeedsUpdate(mod.Version, response.Version);
                        mod.CheckFailed = false;

                        if (!string.IsNullOrEmpty(response.Name))
                            _mappingManager.SetMapping(mod.FolderName, nexusId, response.Name);

                        Plugin.Logger.LogDebug($"{mod.Name}: {mod.Version} -> {response.Version} (Needs update: {mod.NeedsUpdate})");
                    }
                    else
                    {
                        mod.CheckFailed = true;
                        mod.CheckError = error;
                        Plugin.Logger.LogDebug($"Failed to check {mod.Name}: {error}");
                    }

                    _checksCompleted++;
                    OnModChecked?.Invoke(mod);
                });
            }

            // Poll until all concurrent requests finish
            while (_checksCompleted < _totalChecks)
            {
                OnStatusUpdate?.Invoke($"Checking mods... ({_checksCompleted}/{_totalChecks})");
                yield return new WaitForSeconds(0.2f);
            }

            foreach (var mod in _installedMods.Where(m => GetNexusModId(m) == null))
            {
                mod.LatestVersion = "No Nexus ID";
                mod.CheckFailed = true;
                mod.CheckError = "No Nexus Mod ID configured";
            }

            _isChecking = false;

            // Single coalesced disk write at the end of the pass — replaces the
            // per-response SaveDiskCache that used to hitch the main thread
            // dozens of times during a fresh startup update check.
            _apiClient.FlushDiskCache();

            var updatesAvailable = _installedMods.Count(m => m.NeedsUpdate);
            OnStatusUpdate?.Invoke($"Check complete! {updatesAvailable} updates available.");
            OnAllChecksComplete?.Invoke(_installedMods);
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
            return KnownModRegistry.GetNexusModId(mod.Name)
                ?? KnownModRegistry.GetNexusModId(mod.FolderName);
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
            return _installedMods.Where(m => m.CheckFailed || string.IsNullOrEmpty(m.LatestVersion)).ToList();
        }
    }
}
