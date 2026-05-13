using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace mod_update_manager
{
    /// <summary>
    /// Discovers mods on Nexus by iterating through mod IDs and caching results.
    /// Falls back to KnownModRegistry for immediate lookups while background discovery runs.
    /// </summary>
    public class NexusModDiscovery
    {
        private const string DISCOVERY_CACHE_FILE = "nexus_discovery_cache.json";
        private const int DISCOVERY_START_ID = 1;
        private const int DISCOVERY_BATCH_SIZE = 1;
        private const float DISCOVERY_INTERVAL = 90f;

        private NexusApiClient _apiClient;
        private Dictionary<string, (string ModId, string Name, DateTime DiscoveryTime)> _discoveredMods;
        private int _lastScannedId;
        private int _consecutiveMisses;
        private bool _isDiscovering;
        private string _cacheFilePath;
        private int _maxScanId;
        private int _maxConsecutiveMisses;

        public event Action<string, string> OnModDiscovered;  // ModName, ModId

        public NexusModDiscovery(NexusApiClient apiClient, string cacheDirectory, int maxScanId = 2000, int maxConsecutiveMisses = 500)
        {
            _apiClient = apiClient;
            _discoveredMods = new Dictionary<string, (string, string, DateTime)>(StringComparer.OrdinalIgnoreCase);
            _cacheFilePath = Path.Combine(cacheDirectory, DISCOVERY_CACHE_FILE);
            _lastScannedId = DISCOVERY_START_ID;
            _maxScanId = maxScanId;
            _maxConsecutiveMisses = maxConsecutiveMisses;

            LoadDiscoveryCache();
        }

        /// <summary>
        /// Loads previously discovered mods from cache file
        /// </summary>
        private void LoadDiscoveryCache()
        {
            if (!File.Exists(_cacheFilePath))
                return;

            try
            {
                var lines = File.ReadAllLines(_cacheFilePath);
                var modPattern = new System.Text.RegularExpressions.Regex(@"mod_(\d+)=([^|]+)\|(.+)");

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                        continue;

                    var match = modPattern.Match(line);
                    if (match.Success)
                    {
                        var modId = match.Groups[1].Value;
                        var name = match.Groups[2].Value;
                        // Group 3 would be timestamp, but we don't need to parse it

                        if (!string.IsNullOrEmpty(modId) && !string.IsNullOrEmpty(name))
                        {
                            _discoveredMods[name] = (modId, name, DateTime.UtcNow);
                        }
                    }

                    // Also look for lastScannedId line
                    if (line.StartsWith("lastScannedId=") && int.TryParse(line.Substring("lastScannedId=".Length), out int lastId))
                    {
                        _lastScannedId = lastId;
                    }
                }

                Plugin.Logger.LogDebug($"Loaded {_discoveredMods.Count} discovered mods from cache (last scanned ID: {_lastScannedId})");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"Failed to load discovery cache: {ex}");
            }
        }

        /// <summary>
        /// Saves discovered mods to cache file
        /// </summary>
        private void SaveDiscoveryCache()
        {
            try
            {
                var cacheDir = Path.GetDirectoryName(_cacheFilePath);
                if (!Directory.Exists(cacheDir))
                    Directory.CreateDirectory(cacheDir);

                var lines = new List<string>
                {
                    "# Nexus Mod Discovery Cache",
                    $"# Generated: {DateTime.UtcNow:O}",
                    $"lastScannedId={_lastScannedId}"
                };

                foreach (var kvp in _discoveredMods)
                {
                    lines.Add($"mod_{kvp.Value.ModId}={kvp.Value.Name}|{kvp.Value.DiscoveryTime:O}");
                }

                File.WriteAllLines(_cacheFilePath, lines);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"Failed to save discovery cache: {ex}");
            }
        }

        /// <summary>
        /// Looks up a mod by name in discovered mods
        /// </summary>
        public string GetModIdFromDiscovery(string modName)
        {
            if (string.IsNullOrEmpty(modName))
                return null;

            // Direct lookup
            if (_discoveredMods.TryGetValue(modName, out var entry))
                return entry.ModId;

            // Try with spaces/underscores conversion
            var withSpaces = modName.Replace("_", " ");
            if (_discoveredMods.TryGetValue(withSpaces, out entry))
                return entry.ModId;

            var withUnderscores = modName.Replace(" ", "_");
            if (_discoveredMods.TryGetValue(withUnderscores, out entry))
                return entry.ModId;

            return null;
        }

        /// <summary>
        /// Gets all discovered mods
        /// </summary>
        public Dictionary<string, string> GetAllDiscoveredMods()
        {
            return _discoveredMods.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ModId);
        }

        /// <summary>
        /// Starts background discovery of Nexus mods. No-op if already running, if the
        /// scan ceiling has been reached (<see cref="_maxScanId"/>), or if discovery is
        /// disabled (<see cref="_maxScanId"/> &lt;= 0).
        /// </summary>
        public void StartDiscovery(MonoBehaviour runner)
        {
            if (_isDiscovering)
                return;

            if (_maxScanId <= 0)
            {
                Plugin.Logger.LogDebug("Nexus mod discovery disabled (Performance.DiscoveryMaxScanId <= 0)");
                return;
            }

            if (_lastScannedId > _maxScanId)
            {
                Plugin.Logger.LogDebug($"Nexus mod discovery already past ceiling (lastScannedId={_lastScannedId}, max={_maxScanId}) - skipping background scan");
                return;
            }

            _consecutiveMisses = 0;
            _isDiscovering = true;
            runner.StartCoroutine(DiscoveryCoroutine());
        }

        /// <summary>
        /// Background coroutine that discovers mods by scanning Nexus IDs
        /// </summary>
        private IEnumerator DiscoveryCoroutine()
        {
            string stopReason = null;

            while (_isDiscovering && _apiClient.HasApiKey)
            {
                if (_lastScannedId > _maxScanId)
                {
                    stopReason = $"reached scan ceiling (max={_maxScanId})";
                    break;
                }

                if (_maxConsecutiveMisses > 0 && _consecutiveMisses >= _maxConsecutiveMisses)
                {
                    stopReason = $"{_consecutiveMisses} consecutive misses (max={_maxConsecutiveMisses}) - assuming end of catalog";
                    break;
                }

                for (int i = 0; i < DISCOVERY_BATCH_SIZE && _isDiscovering; i++)
                {
                    if (_lastScannedId > _maxScanId) break;

                    int modId = _lastScannedId;
                    _lastScannedId++;

                    bool discoveryComplete = false;
                    bool found = false;

                    _apiClient.GetModInfo(modId.ToString(), (response, error) =>
                    {
                        if (response != null && !string.IsNullOrEmpty(response.Name))
                        {
                            found = true;
                            if (!_discoveredMods.ContainsKey(response.Name))
                            {
                                _discoveredMods[response.Name] = (modId.ToString(), response.Name, DateTime.UtcNow);
                                OnModDiscovered?.Invoke(response.Name, modId.ToString());
                                Plugin.Logger.LogDebug($"[Discovery] Found mod: {response.Name} (ID: {modId})");
                            }
                        }
                        discoveryComplete = true;
                    });

                    // Wait for this request to complete
                    while (!discoveryComplete)
                        yield return null;

                    if (found) _consecutiveMisses = 0;
                    else _consecutiveMisses++;

                    // Small delay between requests
                    yield return new WaitForSeconds(0.2f);
                }

                // Save cache after each batch
                SaveDiscoveryCache();

                // Wait before next batch
                yield return new WaitForSeconds(DISCOVERY_INTERVAL);
            }

            _isDiscovering = false;
            SaveDiscoveryCache();

            if (stopReason != null)
                Plugin.Logger.LogDebug($"Discovery scan stopped: {stopReason}. Scanned up to ID {_lastScannedId - 1}, found {_discoveredMods.Count} mod(s).");
            else
                Plugin.Logger.LogDebug($"Discovery scan complete. Scanned up to ID {_lastScannedId - 1}");
        }

        /// <summary>
        /// Stops the background discovery
        /// </summary>
        public void StopDiscovery()
        {
            _isDiscovering = false;
            SaveDiscoveryCache();
        }

        /// <summary>
        /// Gets discovery progress info
        /// </summary>
        public (int LastScannedId, int TotalDiscovered) GetProgressInfo()
        {
            return (_lastScannedId - 1, _discoveredMods.Count);
        }
    }
}
