// Host-free: no BepInEx or UnityEngine reference, and nothing here sends anything or touches a file.
// Development_Tools/Tests/MUM-CompiledLogic.Tests.ps1 compiles this file (with NexusPublicQuery and
// KnownModRegistry, which it reads the game domain, ids and version equivalents through) and drives it.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace mod_update_manager
{
    /// <summary>What Nexus's answer to an endorse request says, read from its HTTP status alone.</summary>
    public enum EndorseOutcome
    {
        /// <summary>HTTP 200 or 201: Nexus took the endorsement.</summary>
        Endorsed,
        /// <summary>Any 4xx other than 401 and 429: Nexus refused it. Nexus refuses an account that has not
        /// downloaded the mod, one that downloaded it under 15 minutes ago and the mod's own author, and no
        /// source names which status goes with which reason (W0-1 section 4.2), so the message names them all.</summary>
        Refused,
        /// <summary>HTTP 401: Nexus did not accept the key (W0-1 section 3.1: a v1 request without a usable
        /// key is answered 401).</summary>
        KeyRejected,
        /// <summary>HTTP 429: Nexus is rate limiting requests.</summary>
        RateLimited,
        /// <summary>Any other status: 202 (accepted but not processed in time, W0-1 section 4.2), another 2xx,
        /// a 3xx or a 5xx. Whether the endorsement was saved is not known.</summary>
        Unconfirmed,
        /// <summary>A network error, or no status at all. Whether the endorsement was saved is not known.</summary>
        Unreachable,
        /// <summary>Nothing was sent: there is no key, or the Nexus id or the version cannot be sent.</summary>
        NotSent
    }

    /// <summary>
    /// The one request the Endorse button on a My Mods row sends (plan feature 17), built from the v1
    /// contract in Documentation/Research/Mod_Update_Manager/W0-1_Nexus_Read_Paths.md section 4.1 and
    /// section 7: a POST to {v1 base}/games/{game domain}/mods/{id}/endorse.json carrying the API key in an
    /// apikey header, Content-Type application/json, and the JSON body {"Version": the installed version},
    /// the form Vortex, Mod Organizer 2 and FluentNexus send. The version is always sent (W0-1: never test it
    /// as optional). The answer is read from its HTTP status alone (Classify); Nexus's reply body is never
    /// read. Also here: when a row offers the button (ShouldOffer). NexusApiClient.EndorseMod sends what
    /// TryBuild and ContentHeaders produce, and nothing else.
    /// </summary>
    public static class NexusEndorse
    {
        /// <summary>The HTTP method of the request.</summary>
        public const string Method = "POST";

        /// <summary>The body's type: JSON, as the three clients that run in production send it.</summary>
        public const string ContentType = "application/json";

        /// <summary>The placeholders ModScanner writes when a mod's version cannot be read; UpdateChecker
        /// skips the same two. Neither is a version Nexus lists, so neither is ever sent.</summary>
        public const string UnknownVersion = "Unknown";
        public const string ParseErrorVersion = "Parse Error";

        /// <summary>True when a version can be sent: not blank once trimmed, and not one of ModScanner's
        /// two placeholders.</summary>
        public static bool IsSendableVersion(string version)
        {
            var v = (version ?? "").Trim();
            if (v.Length == 0) return false;
            if (v == UnknownVersion) return false;
            if (v == ParseErrorVersion) return false;
            return true;
        }

        /// <summary>
        /// The version a row's request names: the version Nexus lists for the installed one when
        /// KnownModRegistry records that the two differ (node-nexus-api documents the field as "has to
        /// correspond to a version that actually exists", W0-1 section 4.1), else the installed version
        /// itself. TryBuild trims it.
        /// </summary>
        public static string VersionToSend(string nexusId, string installedVersion)
        {
            return KnownModRegistry.GetVersionEquivalent((nexusId ?? "").Trim(), installedVersion) ?? installedVersion;
        }

        /// <summary>
        /// The request's address and body, or false (both null) when it cannot be sent: no v1 base, a Nexus
        /// id that is not a positive whole number, or a version IsSendableVersion refuses. modId is read the
        /// way the public query reads one (NexusPublicQuery.TryReadId), so nothing else reaches the address.
        /// </summary>
        public static bool TryBuild(string v1Base, string modId, string version, out string url, out string body)
        {
            url = null;
            body = null;
            if (string.IsNullOrWhiteSpace(v1Base)) return false;
            if (!NexusPublicQuery.TryReadId(modId, out int id)) return false;
            if (!IsSendableVersion(version)) return false;

            var sendable = version.Trim();
            url = v1Base.Trim().TrimEnd('/') + "/games/" + NexusPublicQuery.GameDomain + "/mods/"
                + id.ToString(CultureInfo.InvariantCulture) + "/endorse.json";
            body = "{\"Version\":\"" + EscapeJson(sendable) + "\"}";
            return true;
        }

        /// <summary>The request's own headers, in the order they are set: the key, the body's type and the
        /// answer's type. The identity headers every request carries (NexusRequestHeaders) are set beside
        /// them by NexusApiClient.SetIdentityHeaders.</summary>
        public static KeyValuePair<string, string>[] ContentHeaders(string apiKey)
        {
            return new[]
            {
                new KeyValuePair<string, string>("apikey", apiKey ?? ""),
                new KeyValuePair<string, string>("Content-Type", ContentType),
                new KeyValuePair<string, string>("Accept", "application/json"),
            };
        }

        /// <param name="responseCode">the request's responseCode; 0 when no status line arrived.</param>
        /// <param name="networkError">the request's isNetworkError flag. Wins over the status: a transfer
        /// that dies after the headers arrived can still report 200.</param>
        public static EndorseOutcome Classify(long responseCode, bool networkError)
        {
            if (networkError || responseCode <= 0) return EndorseOutcome.Unreachable;
            if (responseCode == 200 || responseCode == 201) return EndorseOutcome.Endorsed;
            if (responseCode == 401) return EndorseOutcome.KeyRejected;
            if (responseCode == 429) return EndorseOutcome.RateLimited;
            if (responseCode >= 400 && responseCode < 500) return EndorseOutcome.Refused;
            return EndorseOutcome.Unconfirmed;
        }

        /// <summary>
        /// True when a My Mods row shows the Endorse button: EnableEndorseButton is on, an API key is set,
        /// the row's mod has a Nexus id (from its ModInfo.json, a mapping or the built-in registry, as an
        /// update check resolves it), and its installed version can be sent.
        /// </summary>
        public static bool ShouldOffer(bool optionOn, bool hasApiKey, string nexusId, string installedVersion)
        {
            if (!optionOn) return false;
            if (!hasApiKey) return false;
            if (!NexusPublicQuery.TryReadId(nexusId, out _)) return false;
            return IsSendableVersion(installedVersion);
        }

        private static string EscapeJson(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// The question an Endorse button opens, "Endorse (mod) on Nexus with your account?", held for one row
    /// at a time. Its Yes button sends only through TryConfirm, which is true once per question and only
    /// for the row it was asked for: Yes with no question open sends nothing, a second Yes sends nothing,
    /// and No (Cancel) closes it.
    /// </summary>
    public sealed class EndorseConfirm
    {
        private string _asking;

        /// <summary>Opens the question for one row (its folder name), closing any other.</summary>
        public void Ask(string rowKey)
        {
            _asking = string.IsNullOrEmpty(rowKey) ? null : rowKey;
        }

        /// <summary>Closes the question without sending.</summary>
        public void Cancel()
        {
            _asking = default(string);
        }

        /// <summary>True while the question is open for this row.</summary>
        public bool IsAsking(string rowKey)
        {
            return _asking != null && string.Equals(_asking, rowKey, StringComparison.Ordinal);
        }

        /// <summary>The Yes button: true when the question was open for this row, and closes it, so one Yes
        /// is one request.</summary>
        public bool TryConfirm(string rowKey)
        {
            if (!IsAsking(rowKey)) return false;
            _asking = null;
            return true;
        }
    }
}
