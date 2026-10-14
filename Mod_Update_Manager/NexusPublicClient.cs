using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine.Networking;

namespace mod_update_manager
{
    /// <summary>
    /// Sends the requests Mod Update Manager makes to Nexus's public read endpoint, both POSTs to
    /// NexusPublicQuery.Endpoint: the one an update check makes (plan feature 15, FetchMods), naming the
    /// game and the Nexus id of every mod being checked and asking for each one's name, version, status,
    /// update time, endorsement count and summary, and the one the New and updated filter makes (plan
    /// feature 16, FetchFeed), naming the game and a count and asking for the most recently updated mods.
    /// Neither carries an API key, only the identity headers every Mod Update Manager request carries and
    /// the two content headers. The bodies and the reading of the answers are NexusPublicQuery's
    /// (host-free); what a check's answer settles for each mod is PublicCheckPlan's, and what the list
    /// shows is DiscoveryFeedPolicy's. This file and NexusApiClient.cs are the only two that send
    /// anything, which MUM-CompiledLogic.Tests.ps1 checks.
    /// </summary>
    public class NexusPublicClient
    {
        // As for every request NexusApiClient sends: UnityWebRequest has no timeout of its own, and the
        // update check waits on this one, so a request that never completes would leave the check running
        // for the session.
        private const int REQUEST_TIMEOUT_SECONDS = 30;

        /// <summary>
        /// Run inside the update check's coroutine (yield return it). Calls <paramref name="done"/> exactly
        /// once with the answer, or with a failed one when nothing usable came back. Ids that cannot be
        /// sent are left out; when none can, nothing is sent and the answer lists no mod.
        /// </summary>
        public IEnumerator FetchMods(IList<string> nexusIds, Action<NexusPublicAnswer> done)
        {
            var ids = NexusPublicQuery.SelectIds(nexusIds);
            if (ids.Count == 0)
            {
                done(NexusPublicAnswer.Listed(new List<NexusPublicMod>()));
                yield break;
            }

            yield return Post(NexusPublicQuery.BuildBody(ids), NexusPublicQuery.ReadResponse, done);
        }

        /// <summary>
        /// The New and updated list's one request: the most recently updated mods of the game, as many as
        /// DiscoveryFeedPolicy.ClampSize(size) allows. Sent only from the filter's open and its Refresh button
        /// (UpdateManagerUI), never from a check. Calls <paramref name="done"/> exactly once, with a failed
        /// answer when nothing usable came back.
        /// </summary>
        public IEnumerator FetchFeed(int size, Action<NexusPublicAnswer> done)
        {
            return Post(NexusPublicQuery.BuildFeedBody(size), NexusPublicQuery.ReadFeedResponse, done);
        }

        private IEnumerator Post(string body, Func<bool, bool, long, string, NexusPublicAnswer> read, Action<NexusPublicAnswer> done)
        {
            UnityWebRequest request = null;
            string notSent = null;
            try
            {
                request = new UnityWebRequest(NexusPublicQuery.Endpoint, UnityWebRequest.kHttpVerbPOST)
                {
                    uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                    downloadHandler = new DownloadHandlerBuffer(),
                    timeout = REQUEST_TIMEOUT_SECONDS
                };
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Accept", "application/json");
                NexusApiClient.SetIdentityHeaders(request);
            }
            catch (Exception ex)
            {
                notSent = $"{ex.GetType().Name}: {ex.Message}";
                request?.Dispose();
                request = null;
            }

            if (request == null)
            {
                Plugin.Logger.LogWarning($"Nexus public query not sent ({notSent}).");
                done(NexusPublicAnswer.Failed("the request could not be built"));
                yield break;
            }

            using (request)
            {
                yield return request.SendWebRequest();
                done(read(request.isNetworkError, request.isHttpError, request.responseCode, request.downloadHandler?.text));
            }
        }
    }
}
