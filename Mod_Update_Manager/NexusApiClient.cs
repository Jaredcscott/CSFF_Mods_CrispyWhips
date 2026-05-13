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
        private bool _cachingEnabled;
        private bool _diskCacheDirty;

        public NexusApiClient(string apiKey, MonoBehaviour coroutineRunner, string diskCachePath = null, bool cachingEnabled = true)
        {
            _apiKey = apiKey;
            _coroutineRunner = coroutineRunner;
            _cachingEnabled = cachingEnabled;
            _diskCachePath = cachingEnabled ? diskCachePath : null;
            _responseCache = new Dictionary<string, (NexusModResponse, System.DateTime)>();

            if (_cachingEnabled && !string.IsNullOrEmpty(_diskCachePath))
                LoadDiskCache();
        }

        public void SetCachingEnabled(bool enabled, string diskCachePath)
        {
            if (_cachingEnabled == enabled && string.Equals(_diskCachePath, enabled ? diskCachePath : null, StringComparison.OrdinalIgnoreCase))
                return;

            if (_cachingEnabled)
                FlushDiskCache();

            _cachingEnabled = enabled;
            _diskCachePath = enabled ? diskCachePath : null;
            _diskCacheDirty = false;
            _responseCache.Clear();

            if (_cachingEnabled && !string.IsNullOrEmpty(_diskCachePath))
                LoadDiskCache();
        }

        // Persist the response cache to disk so subsequent game launches skip already-checked mods.
        private void LoadDiskCache()
        {
            try
            {
                if (!_cachingEnabled || string.IsNullOrEmpty(_diskCachePath)) return;
                if (!System.IO.File.Exists(_diskCachePath)) return;
                var json = System.IO.File.ReadAllText(_diskCachePath);
                foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(json, "\\\"((?:\\\\.|[^\\\"])*)\\\"\\s*:\\s*\\{([^{}]*)\\}"))
                {
                    var modId = Unescape(match.Groups[1].Value);
                    var body = match.Groups[2].Value;
                    var version = ReadJsonString(body, "version");
                    var cachedAt = ReadJsonString(body, "cachedAt");

                    if (string.IsNullOrEmpty(modId) || string.IsNullOrEmpty(version) || string.IsNullOrEmpty(cachedAt)) continue;
                    if (!System.DateTime.TryParse(cachedAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var ts)) continue;
                    if ((System.DateTime.UtcNow - ts).TotalHours >= CACHE_EXPIRY_HOURS) continue;

                    _responseCache[modId] = (new NexusModResponse
                    {
                        Version = version,
                        Name = ReadJsonString(body, "name"),
                        Author = ReadJsonString(body, "author"),
                    }, ts);
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"Could not load Nexus cache from disk: {ex}");
            }
        }

        /// <summary>
        /// Flushes the in-memory response cache to disk if anything changed since the
        /// last flush. Replaces the per-callback writes that fired ~once per Nexus
        /// response — those caused dozens of synchronous disk writes during a startup
        /// update check, hitching frames while the main scene was still spinning up.
        /// Callers that mutate <see cref="_responseCache"/> set <see cref="_diskCacheDirty"/>
        /// and either explicitly call this, or rely on <see cref="FlushDiskCache"/>
        /// from the update-check coroutine.
        /// </summary>
        public void FlushDiskCache()
        {
            if (!_cachingEnabled) return;
            if (!_diskCacheDirty) return;
            _diskCacheDirty = false;
            SaveDiskCache();
        }

        private void SaveDiskCache()
        {
            if (!_cachingEnabled) return;
            if (string.IsNullOrEmpty(_diskCachePath)) return;
            try
            {
                var cacheDir = System.IO.Path.GetDirectoryName(_diskCachePath);
                if (!string.IsNullOrEmpty(cacheDir) && !System.IO.Directory.Exists(cacheDir))
                    System.IO.Directory.CreateDirectory(cacheDir);

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
                Plugin.Logger.LogWarning($"Could not save Nexus cache to disk: {ex}");
            }
        }

        private static string Escape(string s) =>
            s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        private static string ReadJsonString(string json, string key)
        {
            var pattern = $"\\\"{System.Text.RegularExpressions.Regex.Escape(key)}\\\"\\s*:\\s*\\\"((?:\\\\.|[^\\\"])*)\\\"";
            var match = System.Text.RegularExpressions.Regex.Match(json, pattern);
            return match.Success ? Unescape(match.Groups[1].Value) : null;
        }

        private static string Unescape(string s)
        {
            if (s == null) return null;
            return s.Replace("\\\\", "\\")
                    .Replace("\\\"", "\"")
                    .Replace("\\n", "\n")
                    .Replace("\\r", "\r")
                    .Replace("\\t", "\t");
        }

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
            var expiredKeys = new List<string>();

            foreach (var kvp in _responseCache)
            {
                if (!IsCacheValid(kvp.Value.timestamp))
                    expiredKeys.Add(kvp.Key);
            }

            foreach (var key in expiredKeys)
                _responseCache.Remove(key);
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
            if (_cachingEnabled && _responseCache.ContainsKey(modId))
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
                request.SetRequestHeader("User-Agent", $"Mod_Update_Manager/{Plugin.PluginVersion}");

                yield return request.SendWebRequest();

                if (!request.isNetworkError && !request.isHttpError)
                {
                    try
                    {
                        var response = ParseNexusModResponse(request.downloadHandler.text);
                        if (_cachingEnabled)
                        {
                            _responseCache[modId] = (response, System.DateTime.UtcNow);
                            _diskCacheDirty = true;
                        }
                        callback(response, null);
                    }
                    catch (Exception ex)
                    {
                        callback(null, $"Failed to parse response: {ex}");
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
        /// Gets the number of cached responses
        /// </summary>
        public int GetCacheSize()
        {
            if (!_cachingEnabled) return 0;
            return _responseCache.Count;
        }
    }
}
