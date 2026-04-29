using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace mod_update_manager
{
    /// <summary>
    /// Response from Nexus Mods API for mod information
    /// </summary>
    public class NexusModResponse
    {
        public string Name { get; set; }
        public string Version { get; set; }
        public string Author { get; set; }
        public int ModId { get; set; }
        public int GameId { get; set; }
        public string Summary { get; set; }
        public string Status { get; set; }
        public string Changelog { get; set; }
        public List<NexusModFile> Files { get; set; }
        public int DownloadCount { get; set; }
    }

    /// <summary>
    /// File information from Nexus Mods API
    /// </summary>
    public class NexusModFile
    {
        public int FileId { get; set; }
        public string FileName { get; set; }
        public string Version { get; set; }
        public long SizeKb { get; set; }
        public string UploadedTime { get; set; }
    }

    /// <summary>
    /// Handles communication with the Nexus Mods API
    /// </summary>
    public class NexusApiClient
    {
        private const string BASE_URL = "https://api.nexusmods.com/v1";
        private const string GAME_DOMAIN = "cardsurvivalfantasyforest";
        private const int CACHE_EXPIRY_HOURS = 24;

        private string _apiKey;
        private MonoBehaviour _coroutineRunner;
        private string _diskCachePath;
        private Dictionary<string, (NexusModResponse response, System.DateTime timestamp)> _responseCache;
        private Dictionary<string, (string changelog, System.DateTime timestamp)> _changelogCache;

        public NexusApiClient(string apiKey, MonoBehaviour coroutineRunner, string diskCachePath = null)
        {
            _apiKey = apiKey;
            _coroutineRunner = coroutineRunner;
            _diskCachePath = diskCachePath;
            _responseCache = new Dictionary<string, (NexusModResponse, System.DateTime)>();
            _changelogCache = new Dictionary<string, (string, System.DateTime)>();

            if (!string.IsNullOrEmpty(_diskCachePath))
                LoadDiskCache();
        }

        // Persist the response cache to disk so subsequent game launches skip already-checked mods.
        private void LoadDiskCache()
        {
            try
            {
                if (!System.IO.File.Exists(_diskCachePath)) return;
                var json = System.IO.File.ReadAllText(_diskCachePath);
                var entries = SimpleJson.ParseJsonObject(json);
                foreach (var kvp in entries)
                {
                    var entry = SimpleJson.ParseJsonObject(kvp.Value);
                    if (!entry.ContainsKey("version") || !entry.ContainsKey("cachedAt")) continue;
                    if (!System.DateTime.TryParse(entry["cachedAt"], out var ts)) continue;
                    if ((System.DateTime.UtcNow - ts).TotalHours >= CACHE_EXPIRY_HOURS) continue;
                    _responseCache[kvp.Key] = (new NexusModResponse
                    {
                        Version = entry["version"],
                        Name = entry.ContainsKey("name") ? entry["name"] : null,
                        Author = entry.ContainsKey("author") ? entry["author"] : null,
                    }, ts);
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"Could not load Nexus cache from disk: {ex.Message}");
            }
        }

        private void SaveDiskCache()
        {
            if (string.IsNullOrEmpty(_diskCachePath)) return;
            try
            {
                var sb = new StringBuilder("{");
                bool first = true;
                foreach (var kvp in _responseCache)
                {
                    if (!IsCacheValid(kvp.Value.timestamp)) continue;
                    if (!first) sb.Append(',');
                    first = false;
                    var r = kvp.Value.response;
                    sb.Append($"\"{Escape(kvp.Key)}\":{{");
                    sb.Append($"\"version\":\"{Escape(r.Version ?? "")}\",");
                    sb.Append($"\"name\":\"{Escape(r.Name ?? "")}\",");
                    sb.Append($"\"author\":\"{Escape(r.Author ?? "")}\",");
                    sb.Append($"\"cachedAt\":\"{kvp.Value.timestamp:O}\"");
                    sb.Append('}');
                }
                sb.Append('}');
                System.IO.File.WriteAllText(_diskCachePath, sb.ToString());
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"Could not save Nexus cache to disk: {ex.Message}");
            }
        }

        private static string Escape(string s) =>
            s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        /// <summary>
        /// Updates the API key
        /// </summary>
        public void SetApiKey(string apiKey)
        {
            _apiKey = apiKey;
        }

        /// <summary>
        /// Checks if we have a valid API key configured
        /// </summary>
        public bool HasApiKey => !string.IsNullOrEmpty(_apiKey);

        /// <summary>
        /// Parses a Nexus mod response from JSON
        /// </summary>
        private NexusModResponse ParseNexusModResponse(string json)
        {
            var dict = SimpleJson.ParseJsonObject(json);
            var response = new NexusModResponse
            {
                Name = dict.ContainsKey("name") ? dict["name"] : null,
                Version = dict.ContainsKey("version") ? dict["version"] : null,
                Author = dict.ContainsKey("author") ? dict["author"] : null,
                Summary = dict.ContainsKey("summary") ? dict["summary"] : null,
                Status = dict.ContainsKey("status") ? dict["status"] : null,
                Changelog = dict.ContainsKey("changelog") ? dict["changelog"] : null,
                Files = new List<NexusModFile>(),
                DownloadCount = 0
            };

            if (dict.ContainsKey("mod_id") && int.TryParse(dict["mod_id"], out int modId))
                response.ModId = modId;
            if (dict.ContainsKey("game_id") && int.TryParse(dict["game_id"], out int gameId))
                response.GameId = gameId;
            if (dict.ContainsKey("endorsement_count") && int.TryParse(dict["endorsement_count"], out int downloads))
                response.DownloadCount = downloads;

            return response;
        }

        /// <summary>
        /// Check if cache entry is still valid
        /// </summary>
        private bool IsCacheValid(System.DateTime timestamp)
        {
            return (System.DateTime.UtcNow - timestamp).TotalHours < CACHE_EXPIRY_HOURS;
        }

        /// <summary>
        /// Clear expired cache entries
        /// </summary>
        public void ClearExpiredCache()
        {
            var now = System.DateTime.UtcNow;
            var expiredKeys = new List<string>();

            foreach (var kvp in _responseCache)
            {
                if (!IsCacheValid(kvp.Value.timestamp))
                    expiredKeys.Add(kvp.Key);
            }

            foreach (var key in expiredKeys)
                _responseCache.Remove(key);

            expiredKeys.Clear();
            foreach (var kvp in _changelogCache)
            {
                if (!IsCacheValid(kvp.Value.timestamp))
                    expiredKeys.Add(kvp.Key);
            }

            foreach (var key in expiredKeys)
                _changelogCache.Remove(key);
        }

        /// <summary>
        /// Fetches mod information from Nexus Mods (with caching)
        /// </summary>
        public void GetModInfo(string modId, Action<NexusModResponse, string> callback)
        {
            if (!HasApiKey)
            {
                callback(null, "No API key configured");
                return;
            }

            // Check cache first
            if (_responseCache.ContainsKey(modId))
            {
                var cached = _responseCache[modId];
                if (IsCacheValid(cached.timestamp))
                {
                    callback(cached.response, null);
                    return;
                }
                else
                {
                    _responseCache.Remove(modId);
                }
            }

            _coroutineRunner.StartCoroutine(GetModInfoCoroutine(modId, callback));
        }

        private IEnumerator GetModInfoCoroutine(string modId, Action<NexusModResponse, string> callback)
        {
            var url = $"{BASE_URL}/games/{GAME_DOMAIN}/mods/{modId}.json";

            using (var request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("apikey", _apiKey);
                request.SetRequestHeader("Accept", "application/json");
                request.SetRequestHeader("User-Agent", "Mod_Update_Manager/1.0.0");

                yield return request.SendWebRequest();

                if (!request.isNetworkError && !request.isHttpError)
                {
                    try
                    {
                        var response = ParseNexusModResponse(request.downloadHandler.text);
                        _responseCache[modId] = (response, System.DateTime.UtcNow);
                        SaveDiskCache();
                        callback(response, null);
                    }
                    catch (Exception ex)
                    {
                        callback(null, $"Failed to parse response: {ex.Message}");
                    }
                }
                else
                {
                    var errorMsg = $"API request failed: {request.error}";
                    if (request.responseCode == 401)
                    {
                        errorMsg = "Invalid API key";
                    }
                    else if (request.responseCode == 404)
                    {
                        errorMsg = "Mod not found on Nexus";
                    }
                    else if (request.responseCode == 429)
                    {
                        errorMsg = "Rate limited - try again later";
                    }
                    callback(null, errorMsg);
                }
            }
        }

        /// <summary>
        /// Validates the API key by making a test request
        /// </summary>
        public void ValidateApiKey(Action<bool, string> callback)
        {
            if (!HasApiKey)
            {
                callback(false, "No API key provided");
                return;
            }

            _coroutineRunner.StartCoroutine(ValidateApiKeyCoroutine(callback));
        }

        private IEnumerator ValidateApiKeyCoroutine(Action<bool, string> callback)
        {
            var url = $"{BASE_URL}/users/validate.json";
            
            using (var request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("apikey", _apiKey);
                request.SetRequestHeader("Accept", "application/json");

                yield return request.SendWebRequest();

                if (!request.isNetworkError && !request.isHttpError)
                {
                    callback(true, "API key valid");
                }
                else
                {
                    callback(false, $"API key validation failed: {request.error}");
                }
            }
        }

        /// <summary>
        /// Searches for mods by name on Nexus Mods
        /// </summary>
        public void SearchMods(string searchTerm, Action<List<NexusModResponse>, string> callback)
        {
            if (!HasApiKey)
            {
                callback(null, "No API key configured");
                return;
            }

            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                callback(null, "Search term is empty");
                return;
            }

            _coroutineRunner.StartCoroutine(SearchModsCoroutine(searchTerm, callback));
        }

        private IEnumerator SearchModsCoroutine(string searchTerm, Action<List<NexusModResponse>, string> callback)
        {
            // Nexus API v1 doesn't have a direct search endpoint, so we use the collections endpoint as fallback
            // For now, we'll return a not-implemented message
            callback(null, "Nexus Mods API v1 does not support mod search. Use manual mapping instead.");
            yield return null;
        }

        /// <summary>
        /// Fetches changelog for a specific mod version
        /// </summary>
        public void GetChangelog(string modId, Action<string, string> callback)
        {
            if (!HasApiKey)
            {
                callback(null, "No API key configured");
                return;
            }

            // Check cache first
            if (_changelogCache.ContainsKey(modId))
            {
                var cached = _changelogCache[modId];
                if (IsCacheValid(cached.timestamp))
                {
                    callback(cached.changelog, null);
                    return;
                }
                else
                {
                    _changelogCache.Remove(modId);
                }
            }

            _coroutineRunner.StartCoroutine(GetChangelogCoroutine(modId, callback));
        }

        private IEnumerator GetChangelogCoroutine(string modId, Action<string, string> callback)
        {
            var url = $"{BASE_URL}/games/{GAME_DOMAIN}/mods/{modId}/changelogs.json";

            using (var request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("apikey", _apiKey);
                request.SetRequestHeader("Accept", "application/json");
                request.SetRequestHeader("User-Agent", "Mod_Update_Manager/1.0.0");

                yield return request.SendWebRequest();

                if (!request.isNetworkError && !request.isHttpError)
                {
                    try
                    {
                        var responseText = request.downloadHandler.text;
                        // Cache the response
                        _changelogCache[modId] = (responseText, System.DateTime.UtcNow);
                        callback(responseText, null);
                    }
                    catch (Exception ex)
                    {
                        callback(null, $"Failed to parse changelog: {ex.Message}");
                    }
                }
                else
                {
                    var errorMsg = $"Failed to fetch changelog: {request.error}";
                    if (request.responseCode == 404)
                        errorMsg = "No changelog available for this mod";
                    callback(null, errorMsg);
                }
            }
        }

        /// <summary>
        /// Gets the number of cached responses
        /// </summary>
        public int GetCacheSize()
        {
            return _responseCache.Count + _changelogCache.Count;
        }
    }
}
