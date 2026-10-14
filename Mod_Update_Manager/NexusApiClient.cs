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
        public int EndorsementCount { get; set; }
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
        private const string GAME_DOMAIN = NexusPublicQuery.GameDomain;
        private const int CACHE_EXPIRY_HOURS = ResponseCachePolicy.ExpiryHours;

        // UnityWebRequest has no timeout by default. Every caller waits on the callback (the
        // update check polls _checksCompleted, discovery spins on a flag), so one request that
        // never completes would leave IsChecking true for the session - the Check for Updates
        // button greyed out and the scheduler stalled, with nothing in the log. A timeout turns
        // that into an ordinary failed check.
        private const int REQUEST_TIMEOUT_SECONDS = 30;

        // A 429 is retried once after this many real seconds before the mod lands in the "rate
        // limited" bucket and its manual Re-check button. A starting value, not a tuned one: if
        // Nexus's window is longer, the retry 429s too and the mod ends up where it did before.
        private const int RATE_LIMIT_RETRY_DELAY_SECONDS = 5;

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
                foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(json, "\\\"((?:\\\\.|[^\\\"])*)\\\"\\s*:\\s*\\{(.*?)\\}", System.Text.RegularExpressions.RegexOptions.Singleline))
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
                        Summary = ReadJsonString(body, "summary"),
                        EndorsementCount = int.TryParse(ReadJsonString(body, "endorsement_count"), out int ec) ? ec : 0,
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
                    sb.Append($"\"summary\":\"{Escape(r.Summary ?? "")}\",");
                    sb.Append($"\"endorsement_count\":{r.EndorsementCount},");
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

        private static string Unescape(string s) => JsonText.Unescape(s);

        /// <summary>
        /// Sets the headers that say what is asking (NexusRequestHeaders: User-Agent, Application-Name,
        /// Application-Version) on a request. Every request Mod Update Manager sends, here and in
        /// NexusPublicClient, goes through this, so all of them carry the same three.
        /// </summary>
        internal static void SetIdentityHeaders(UnityWebRequest request)
        {
            foreach (var header in NexusRequestHeaders.Identity(Plugin.PluginVersion))
                request.SetRequestHeader(header.Key, header.Value);
        }

        /// <summary>
        /// The cached answer for a mod when the cache policy lets it stand in for a request (caching on,
        /// an entry under the expiry, and no button bypass). The public query uses it to leave a mod out.
        /// </summary>
        public bool TryGetCachedAnswer(string modId, bool bypassCache, out NexusModResponse response)
        {
            response = null;
            if (string.IsNullOrEmpty(modId)) return false;
            var hasEntry = _responseCache.TryGetValue(modId, out var cached);
            var ageHours = hasEntry ? (System.DateTime.UtcNow - cached.timestamp).TotalHours : 0d;
            if (!ResponseCachePolicy.ShouldUseCache(_cachingEnabled, hasEntry, ageHours, bypassCache)) return false;
            response = cached.response;
            return true;
        }

        /// <summary>Keeps an answer the public query gave, as a per-mod request's answer is kept.</summary>
        public void CacheAnswer(string modId, NexusModResponse response)
        {
            if (!_cachingEnabled || string.IsNullOrEmpty(modId) || response == null) return;
            _responseCache[modId] = (response, System.DateTime.UtcNow);
            _diskCacheDirty = true;
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
                EndorsementCount = 0
            };

            if (dict.ContainsKey("mod_id") && int.TryParse(dict["mod_id"], out int modId))
                response.ModId = modId;
            if (dict.ContainsKey("game_id") && int.TryParse(dict["game_id"], out int gameId))
                response.GameId = gameId;
            if (dict.ContainsKey("endorsement_count") && int.TryParse(dict["endorsement_count"], out int endorsements))
                response.EndorsementCount = endorsements;

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
        /// Fetches mod information from Nexus Mods (with caching). The callback's third
        /// parameter is true only when the request AND its one automatic retry both failed with
        /// HTTP 429 - callers that need to track rate-limited mods (see N5, "Re-check
        /// rate-limited") MUST key off this flag, never off the error string, which is display
        /// text and not a stable identifier. <paramref name="bypassCache"/> is true for a check
        /// the player asked for with a button: the cached answer is not used and the fresh one
        /// replaces it. Startup, scheduled and discovery lookups pass false.
        /// </summary>
        public void GetModInfo(string modId, Action<NexusModResponse, string, bool> callback, bool bypassCache = false)
        {
            if (!HasApiKey)
            {
                callback(null, "No API key configured", false);
                return;
            }

            // Check cache first
            var hasEntry = _responseCache.TryGetValue(modId, out var cached);
            var ageHours = hasEntry ? (System.DateTime.UtcNow - cached.timestamp).TotalHours : 0d;
            if (ResponseCachePolicy.ShouldUseCache(_cachingEnabled, hasEntry, ageHours, bypassCache))
            {
                callback(cached.response, null, false);
                return;
            }
            if (hasEntry && !IsCacheValid(cached.timestamp))
                _responseCache.Remove(modId);

            _coroutineRunner.StartCoroutine(GetModInfoCoroutine(modId, callback));
        }

        private IEnumerator GetModInfoCoroutine(string modId, Action<NexusModResponse, string, bool> callback)
        {
            var url = $"{BASE_URL}/games/{GAME_DOMAIN}/mods/{modId}.json";

            // Attempt 0 is the request; attempt 1 is the single retry after a 429, sent with the same
            // URL and headers once the first request is disposed.
            for (int attempt = 0; ; attempt++)
            {
                using (var request = UnityWebRequest.Get(url))
                {
                    request.timeout = REQUEST_TIMEOUT_SECONDS;
                    request.SetRequestHeader("apikey", _apiKey);
                    request.SetRequestHeader("Accept", "application/json");
                    SetIdentityHeaders(request);

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
                            callback(response, null, false);
                        }
                        catch (Exception ex)
                        {
                            callback(null, $"Failed to parse response: {ex}", false);
                        }
                        yield break;
                    }

                    if (request.responseCode == 429 && attempt == 0)
                    {
                        Plugin.Logger.LogInfo($"Nexus rate limit hit checking mod {modId}; retrying once in {RATE_LIMIT_RETRY_DELAY_SECONDS}s.");
                    }
                    else
                    {
                        var errorMsg = $"API request failed: {request.error}";
                        bool isRateLimited = false;
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
                            isRateLimited = true;
                            Plugin.Logger.LogWarning($"Nexus rate limit hit again on the retry for mod {modId}; it can be re-checked from the My Mods tab.");
                        }
                        callback(null, errorMsg, isRateLimited);
                        yield break;
                    }
                }

                // Realtime: CSFF runs at timeScale 0 while it waits for the player and during
                // FadeToBlack, where WaitForSeconds would never finish (CLAUDE.md, Coroutine Timing).
                yield return new WaitForSecondsRealtime(RATE_LIMIT_RETRY_DELAY_SECONDS);
            }
        }

        /// <summary>
        /// Checks the API key with one request to Nexus's validate endpoint. Called only from the
        /// Settings tab's Save button, never at startup. The callback gets the verdict and the HTTP
        /// status behind it (0 when none arrived); an empty key is rejected without a request.
        /// </summary>
        public void ValidateApiKey(Action<ApiKeyResult, long> callback)
        {
            if (!HasApiKey)
            {
                callback(ApiKeyResult.Rejected, 0);
                return;
            }

            _coroutineRunner.StartCoroutine(ValidateApiKeyCoroutine(callback));
        }

        private IEnumerator ValidateApiKeyCoroutine(Action<ApiKeyResult, long> callback)
        {
            var url = $"{BASE_URL}/users/validate.json";

            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = REQUEST_TIMEOUT_SECONDS;

                // A key with a stray control character (a pasted line break) cannot be sent as a
                // header and SetRequestHeader throws; that is a key Nexus would never accept, so say
                // so instead of letting the coroutine die with the Settings tab waiting on it. Only
                // the exception's type is logged: the key never reaches the log.
                bool headersSet = true;
                try
                {
                    request.SetRequestHeader("apikey", _apiKey);
                    request.SetRequestHeader("Accept", "application/json");
                    SetIdentityHeaders(request);
                }
                catch (Exception ex)
                {
                    headersSet = false;
                    Plugin.Logger.LogDebug($"API key check not sent: the key could not be set as a header ({ex.GetType().Name}).");
                }

                if (!headersSet)
                {
                    callback(ApiKeyResult.Rejected, 0);
                    yield break;
                }

                yield return request.SendWebRequest();

                // The status and the network flag are all this reads. A validate reply is expected
                // to carry details of the account behind the key, so its body is never read,
                // logged or shown, whatever it holds.
                callback(ApiKeyCheck.Classify(request.responseCode, request.isNetworkError), request.responseCode);
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

        /// <summary>
        /// Fetches the changelog for a mod. Callback receives a list of (version, entries[]) pairs
        /// sorted newest-first, or an error string.
        /// </summary>
        public void GetModChangelogs(string modId, Action<List<(string Version, List<string> Entries)>, string> callback)
        {
            if (!HasApiKey)
            {
                callback(null, "No API key configured");
                return;
            }
            _coroutineRunner.StartCoroutine(GetChangelogsCoroutine(modId, callback));
        }

        private IEnumerator GetChangelogsCoroutine(string modId, Action<List<(string, List<string>)>, string> callback)
        {
            var url = $"{BASE_URL}/games/{GAME_DOMAIN}/mods/{modId}/changelogs.json";
            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = REQUEST_TIMEOUT_SECONDS;
                request.SetRequestHeader("apikey", _apiKey);
                request.SetRequestHeader("Accept", "application/json");
                SetIdentityHeaders(request);
                yield return request.SendWebRequest();

                if (!request.isNetworkError && !request.isHttpError)
                {
                    try
                    {
                        callback(ChangelogParser.Parse(request.downloadHandler.text), null);
                    }
                    catch (Exception ex)
                    {
                        callback(null, $"Failed to parse changelog: {ex.Message}");
                    }
                }
                else
                {
                    callback(null, $"Changelog fetch failed: {request.error}");
                }
            }
        }

        /// <summary>
        /// Endorses one mod on Nexus with the player's account (plan feature 17): one POST to
        /// /v1/games/cardsurvivalfantasyforest/mods/(id)/endorse.json carrying the API key, the identity
        /// headers and the body {"Version": version}, all built by NexusEndorse from the contract in
        /// Documentation/Research/Mod_Update_Manager/W0-1_Nexus_Read_Paths.md section 4.1 and section 7.
        /// Called only from the Yes button of a My Mods row's "Endorse ... on Nexus with your account?"
        /// question (UpdateManagerUI.StartEndorse); never at startup, from a check, a schedule or a loop, and
        /// sent once, with no retry. The callback gets the outcome, read from the HTTP status and the network
        /// flag alone (NexusEndorse.Classify), and that status (0 when none arrived). Nothing is sent without
        /// a key, or when the id or the version cannot be sent (NotSent).
        /// </summary>
        public void EndorseMod(string modId, string version, Action<EndorseOutcome, long> callback)
        {
            if (!HasApiKey || !NexusEndorse.TryBuild(BASE_URL, modId, version, out var url, out var body))
            {
                callback(EndorseOutcome.NotSent, 0);
                return;
            }

            _coroutineRunner.StartCoroutine(EndorseModCoroutine(url, body, callback));
        }

        private IEnumerator EndorseModCoroutine(string url, string body, Action<EndorseOutcome, long> callback)
        {
            // No download handler: Nexus's reply body is not kept, so nothing here can read, log or show it.
            // A key with a stray control character cannot be set as a header (SetRequestHeader throws); then
            // nothing is sent, and only the exception's type is logged, never the key.
            UnityWebRequest request = null;
            try
            {
                request = new UnityWebRequest(url, NexusEndorse.Method)
                {
                    uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                    timeout = REQUEST_TIMEOUT_SECONDS
                };
                foreach (var header in NexusEndorse.ContentHeaders(_apiKey))
                    request.SetRequestHeader(header.Key, header.Value);
                SetIdentityHeaders(request);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogDebug($"Endorse not sent: the request could not be built ({ex.GetType().Name}).");
                request?.Dispose();
                request = null;
            }

            if (request == null)
            {
                callback(EndorseOutcome.NotSent, 0);
                yield break;
            }

            using (request)
            {
                yield return request.SendWebRequest();
                callback(NexusEndorse.Classify(request.responseCode, request.isNetworkError), request.responseCode);
            }
        }
    }
}
