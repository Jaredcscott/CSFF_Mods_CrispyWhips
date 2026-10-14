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
        private NexusPublicClient _publicClient;
        private Func<bool> _usePublicWithoutKey;
        private ModMappingManager _mappingManager;
        private NexusModDiscovery _modDiscovery;
        private ModPreferences _preferences;
        private List<InstalledModInfo> _installedMods;

        // English-column text for the rows the public query can leave on a mod (Localization/SimpEn.csv,
        // SimpCn.csv): Mod_Update_Manager_PublicNotListed, _PublicFailedNoKey and _NotPublished
        // ({0}=the status Nexus gave).
        private static string PublicNotListedText => MumText.Get("PublicNotListed");
        private static string PublicFailedNoKeyText => MumText.Get("PublicFailedNoKey");
        private static string NotPublishedFormat => MumText.Get("NotPublished");
        private bool _isChecking;
        private int _checksCompleted;
        private int _totalChecks;

        public event Action<InstalledModInfo> OnModChecked;
        public event Action<List<InstalledModInfo>> OnAllChecksComplete;
        public event Action<string> OnStatusUpdate;

        public bool IsChecking => _isChecking;
        public float Progress => _totalChecks > 0 ? (float)_checksCompleted / _totalChecks : 0f;
        public List<InstalledModInfo> InstalledMods => _installedMods;

        public UpdateChecker(NexusApiClient apiClient, ModMappingManager mappingManager, NexusModDiscovery modDiscovery = null, ModPreferences preferences = null,
            NexusPublicClient publicClient = null, Func<bool> usePublicWithoutKey = null)
        {
            _apiClient = apiClient;
            _publicClient = publicClient;
            _usePublicWithoutKey = usePublicWithoutKey;
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
            OnStatusUpdate?.Invoke(MumText.Get("CheckerScanning"));
            _installedMods = ModScanner.ScanInstalledMods();
            // The plugin set just changed under us, so the cached [BepInDependency] cross-reference
            // (N6, Conflicts tab) is stale; it recomputes lazily the next time that tab is drawn.
            // So is the conflict scan's per-DLL plugin GUID read.
            DependencyValidator.Invalidate();
            ConflictDetector.Invalidate();
            if (_preferences != null)
                foreach (var m in _installedMods) _preferences.ApplyToMod(m);
            OnStatusUpdate?.Invoke(string.Format(MumText.Get("CheckerFoundFormat"), _installedMods.Count));
        }

        /// <summary>
        /// Starts checking all mods for updates. <paramref name="bypassCache"/> is true when the
        /// player asked for this check with the Check for Updates button, so Nexus is asked even
        /// where a cached answer is still fresh; the startup and scheduled checks pass false.
        /// </summary>
        public void CheckAllMods(MonoBehaviour runner, bool bypassCache = false)
        {
            if (_isChecking)
            {
                Plugin.Logger.LogDebug("Update check already in progress");
                return;
            }

            // A check runs with a key, or without one only when the player turned on
            // UsePublicEndpointWithoutKey (off by default: nothing is sent without a key).
            if (!PublicCheckPlan.CanCheck(_apiClient.HasApiKey, _usePublicWithoutKey != null && _usePublicWithoutKey()))
            {
                OnStatusUpdate?.Invoke(MumText.Get("CheckerNoKey"));
                OnAllChecksComplete?.Invoke(_installedMods);
                return;
            }

            runner.StartCoroutine(CheckAllModsCoroutine(bypassCache));
        }

        private IEnumerator CheckAllModsCoroutine(bool bypassCache)
        {
            var modsToCheck = _installedMods.Where(m => GetNexusModId(m) != null && !m.IsIgnored).ToList();

            var invalidVersionMods = modsToCheck.Where(m =>
                m.Version == "Parse Error" || m.Version == "Unknown").ToList();
            foreach (var mod in invalidVersionMods)
            {
                mod.CheckFailed = true;
                mod.CheckError = MumText.Get("CheckerVersionUnknown");
                modsToCheck.Remove(mod);
            }

            yield return RunChecksCoroutine(modsToCheck, MumText.Get("CheckerContextModsWithIds"), bypassCache);

            foreach (var mod in _installedMods.Where(m => GetNexusModId(m) == null))
            {
                // Don't list Mod_Update_Manager itself in the "Unable to Check" tab
                if (KnownModRegistry.IsSelfMod(mod.Name) || KnownModRegistry.IsSelfMod(mod.FolderName))
                    continue;
                mod.LatestVersion = "No Nexus ID";
                mod.CheckFailed = true;
                mod.CheckError = MumText.Get("CheckerNoNexusId");
            }

            var updatesAvailable = _installedMods.Count(m => m.NeedsUpdate);
            OnStatusUpdate?.Invoke(string.Format(MumText.Get("CheckerCompleteFormat"), updatesAvailable));
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
        /// delay between requests) as a normal full check. The manual fallback for a mod that
        /// still 429s after NexusApiClient's one automatic retry. A button press, so it always
        /// asks Nexus rather than a cached answer.
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
                OnStatusUpdate?.Invoke(MumText.Get("CheckerNoKey"));
                return;
            }

            var modsToRecheck = _installedMods.Where(m => m.RateLimited && GetNexusModId(m) != null).ToList();
            if (modsToRecheck.Count == 0)
            {
                OnStatusUpdate?.Invoke(MumText.Get("CheckerNoRateLimited"));
                return;
            }

            runner.StartCoroutine(RecheckRateLimitedModsCoroutine(modsToRecheck));
        }

        private IEnumerator RecheckRateLimitedModsCoroutine(List<InstalledModInfo> modsToRecheck)
        {
            yield return RunChecksCoroutine(modsToRecheck, MumText.Get("CheckerContextRateLimited"), true);

            var stillRateLimited = modsToRecheck.Count(m => m.RateLimited);
            Plugin.Logger.LogInfo($"Re-checked {modsToRecheck.Count} rate-limited mod(s); {stillRateLimited} still rate limited.");
            OnStatusUpdate?.Invoke(stillRateLimited > 0
                ? string.Format(MumText.Get("CheckerRecheckStillFormat"), stillRateLimited)
                : MumText.Get("CheckerRecheckAll"));
            OnAllChecksComplete?.Invoke(_installedMods);
        }

        /// <summary>
        /// Shared check loop used by both a full <see cref="CheckAllModsCoroutine"/>
        /// pass and a filtered <see cref="RecheckRateLimitedModsCoroutine"/> pass. Drives
        /// <see cref="_isChecking"/>/<see cref="_checksCompleted"/>/<see cref="_totalChecks"/>
        /// for the shared <see cref="Progress"/> property, so only one such loop may run at a
        /// time (both callers early-out on <see cref="_isChecking"/>). <paramref name="bypassCache"/>
        /// is handed to every request of the pass. One public query asks about every mod first
        /// (<see cref="CheckPublicCoroutine"/>); the staggered per-mod requests below run only for the
        /// mods it did not settle, and only when a key is set.
        /// </summary>
        private IEnumerator RunChecksCoroutine(List<InstalledModInfo> modsToCheck, string statusContext, bool bypassCache)
        {
            _isChecking = true;
            _checksCompleted = 0;
            _totalChecks = modsToCheck.Count;
            OnStatusUpdate?.Invoke(string.Format(MumText.Get("CheckerCheckingFormat"), _totalChecks, statusContext));

            var askOnItsOwn = new List<InstalledModInfo>();
            yield return CheckPublicCoroutine(modsToCheck, bypassCache, askOnItsOwn);

            // Stagger requests by 0.5s to avoid 429 rate-limit bursts on large mod lists.
            foreach (var mod in askOnItsOwn)
            {
                var nexusId = GetNexusModId(mod);
                mod.NexusUrl = KnownModRegistry.GetNexusUrl(nexusId);

                _apiClient.GetModInfo(nexusId, (response, error, isRateLimited) =>
                {
                    if (response != null)
                    {
                        ApplyLatest(mod, response.Version, response.Summary, response.EndorsementCount,
                            UpdateVerdict.NeedsUpdate(nexusId, mod.Version, response.Version));
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
                }, bypassCache);

                yield return new WaitForSecondsRealtime(0.5f);
            }

            // Poll until all staggered requests finish
            while (_checksCompleted < _totalChecks)
            {
                OnStatusUpdate?.Invoke(string.Format(MumText.Get("CheckerProgressFormat"), _checksCompleted, _totalChecks));
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
        /// The Nexus ids the player already has: the id every installed mod resolves to (the same
        /// resolution an update check uses, GetNexusModId) and the id of every mapping row, a mapped mod
        /// being one the player has told Mod Update Manager about. The New and updated filter leaves
        /// these out (DiscoveryFeedPolicy.Select, which also leaves out Mod Update Manager's own id, the
        /// one GetNexusModId reports as no id). Read from what is already in memory; nothing is sent.
        /// </summary>
        public List<string> InstalledOrMappedNexusIds()
        {
            var ids = new List<string>();
            foreach (var mod in _installedMods)
            {
                var id = GetNexusModId(mod);
                if (id != null) ids.Add(id);
            }
            foreach (var mapping in _mappingManager.GetAllMappings())
            {
                if (!string.IsNullOrWhiteSpace(mapping.NexusModId)) ids.Add(mapping.NexusModId);
            }
            return ids;
        }

        /// <summary>
        /// The public half of a check (plan feature 15). A mod whose cached answer the cache policy
        /// accepts is settled from it; every other mod goes into ONE request to Nexus's public endpoint,
        /// which carries no key. PublicCheckPlan.Decide then settles each mod from that answer or, when it
        /// cannot (the answer failed, could not be read, or does not list the mod), adds it to
        /// <paramref name="askOnItsOwn"/> for the per-mod request when a key is set, and reports it as not
        /// checked when none is. A mod the answer does not settle never reads as up to date.
        /// </summary>
        private IEnumerator CheckPublicCoroutine(List<InstalledModInfo> mods, bool bypassCache, List<InstalledModInfo> askOnItsOwn)
        {
            bool hasApiKey = _apiClient.HasApiKey;
            var toAsk = new List<InstalledModInfo>();
            foreach (var mod in mods)
            {
                var nexusId = GetNexusModId(mod);
                mod.NexusUrl = KnownModRegistry.GetNexusUrl(nexusId);
                if (_apiClient.TryGetCachedAnswer(nexusId, bypassCache, out var cached))
                {
                    ApplyLatest(mod, cached.Version, cached.Summary, cached.EndorsementCount,
                        UpdateVerdict.NeedsUpdate(nexusId, mod.Version, cached.Version));
                    _checksCompleted++;
                    OnModChecked?.Invoke(mod);
                }
                else
                {
                    toAsk.Add(mod);
                }
            }
            if (toAsk.Count == 0) yield break;

            NexusPublicAnswer answer = null;
            if (_publicClient != null)
                yield return _publicClient.FetchMods(toAsk.Select(GetNexusModId).ToList(), received => answer = received);

            if (answer == null || !answer.Ok)
            {
                var why = answer == null ? "no public client" : answer.Failure;
                Plugin.Logger.LogWarning(hasApiKey
                    ? $"Nexus public query failed ({why}); checking {toAsk.Count} mod(s) one at a time instead."
                    : $"Nexus public query failed ({why}) and no API key is set, so {toAsk.Count} mod(s) were not checked.");
            }

            foreach (var mod in toAsk)
            {
                var nexusId = GetNexusModId(mod);
                var result = PublicCheckPlan.Decide(nexusId, mod.Version, answer, hasApiKey);
                switch (result.Outcome)
                {
                    case PublicCheckOutcome.Answered:
                        ApplyLatest(mod, result.Mod.Version, result.Mod.Summary, result.Mod.Endorsements, result.NeedsUpdate);
                        _apiClient.CacheAnswer(nexusId, new NexusModResponse
                        {
                            ModId = result.Mod.ModId,
                            Name = result.Mod.Name,
                            Version = result.Mod.Version,
                            Summary = result.Mod.Summary,
                            Status = result.Mod.Status,
                            EndorsementCount = result.Mod.Endorsements
                        });
                        break;
                    case PublicCheckOutcome.NotPublished:
                        mod.NeedsUpdate = false;
                        mod.IsMajorVersionUpdate = false;
                        mod.CheckFailed = true;
                        mod.CheckError = string.Format(NotPublishedFormat, result.Mod.Status);
                        mod.RateLimited = false;
                        Plugin.Logger.LogDebug($"{mod.Name}: Nexus lists mod {nexusId} with status '{result.Mod.Status}', so no update is offered.");
                        break;
                    case PublicCheckOutcome.AskOnItsOwn:
                        askOnItsOwn.Add(mod);
                        continue;
                    default:
                        mod.CheckFailed = true;
                        mod.CheckError = answer != null && answer.Ok ? PublicNotListedText : PublicFailedNoKeyText;
                        mod.RateLimited = false;
                        Plugin.Logger.LogDebug($"{mod.Name}: not checked ({mod.CheckError})");
                        break;
                }
                _checksCompleted++;
                OnModChecked?.Invoke(mod);
            }
        }

        /// <summary>A latest version read from Nexus, by either path, written onto the mod's row.</summary>
        private void ApplyLatest(InstalledModInfo mod, string latestVersion, string summary, int endorsements, bool needsUpdate)
        {
            mod.LatestVersion = latestVersion;
            mod.Summary = summary;
            mod.EndorsementCount = endorsements;
            mod.RateLimited = false;
            mod.NeedsUpdate = needsUpdate;
            mod.IsMajorVersionUpdate = mod.NeedsUpdate &&
                ModComparisonView.IsMajorUpdate(mod.Version, latestVersion);
            mod.CheckFailed = false;

            // Deliberately NOT persisted to the user mappings file. That file outranks
            // KnownModRegistry in GetNexusModId, so copying every checked ID into it (as
            // this did through 2.1.58) froze each registry ID for good and hid any later
            // registry correction, and rewrote the file once per response.

            Plugin.Logger.LogDebug($"{mod.Name}: {mod.Version} -> {latestVersion} (Needs update: {mod.NeedsUpdate})");
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
