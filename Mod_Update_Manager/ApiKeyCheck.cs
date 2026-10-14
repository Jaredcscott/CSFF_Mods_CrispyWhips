namespace mod_update_manager
{
    /// <summary>What Nexus's answer to the key check says about the key.</summary>
    public enum ApiKeyResult
    {
        /// <summary>Nexus accepted the key.</summary>
        Accepted,
        /// <summary>Nexus does not accept the key (HTTP 401), or the key cannot be sent as written.</summary>
        Rejected,
        /// <summary>Nexus answered 429: the key was neither accepted nor rejected.</summary>
        RateLimited,
        /// <summary>No usable HTTP answer: a network error, or no status code at all.</summary>
        Unreachable,
        /// <summary>Any other status (a server error such as 503, a 403, a redirect): the key was not judged.</summary>
        Inconclusive
    }

    /// <summary>
    /// Reads the answer to the Settings tab's key check. Only the HTTP status and the network flag
    /// go in: the body of that reply is expected to carry details of the account behind the key,
    /// and nothing here, or in the request that feeds it, ever looks at it.
    ///
    /// Host-free: no BepInEx or UnityEngine reference. Development_Tools/Tests/MUM-CompiledLogic.Tests.ps1
    /// compiles and drives it.
    /// </summary>
    public static class ApiKeyCheck
    {
        /// <param name="responseCode">the request's responseCode; 0 when no status line arrived.</param>
        /// <param name="networkError">the request's isNetworkError flag. Wins over the status: a transfer
        /// that dies after the headers arrived can still report 200.</param>
        public static ApiKeyResult Classify(long responseCode, bool networkError)
        {
            if (networkError || responseCode <= 0) return ApiKeyResult.Unreachable;
            if (responseCode >= 200 && responseCode < 300) return ApiKeyResult.Accepted;
            if (responseCode == 401) return ApiKeyResult.Rejected;
            if (responseCode == 429) return ApiKeyResult.RateLimited;
            return ApiKeyResult.Inconclusive;
        }
    }
}
