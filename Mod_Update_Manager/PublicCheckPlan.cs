using System;

namespace mod_update_manager
{
    /// <summary>What the public answer settles for one mod being checked.</summary>
    public enum PublicCheckOutcome
    {
        /// <summary>The answer lists the mod, published, with a version: that is its latest version.</summary>
        Answered,
        /// <summary>The answer lists the mod with a status other than "published" (hidden, removed, under
        /// moderation): no update is offered for it.</summary>
        NotPublished,
        /// <summary>The answer does not settle the mod and an API key is set: the per-mod request asks.</summary>
        AskOnItsOwn,
        /// <summary>The answer does not settle the mod and no key is set: it is not checked this time.</summary>
        Unknown
    }

    /// <summary>The outcome for one mod, the listed mod behind it (Answered and NotPublished only), and
    /// whether an update is offered.</summary>
    public sealed class PublicCheckResult
    {
        public PublicCheckOutcome Outcome;
        public NexusPublicMod Mod;
        public bool NeedsUpdate;
    }

    /// <summary>
    /// The rules for an update check that asks Nexus's public endpoint once (plan feature 15). A check
    /// runs when a key is set, or when the player turned on UsePublicEndpointWithoutKey (off by default:
    /// the README promises nothing is sent without a key). Every check sends the one public query first;
    /// Decide then says, mod by mod, whether that answer settles it. A mod it does not settle (the
    /// answer failed, could not be read, or does not list the mod) goes to the per-mod request when a
    /// key is set and is reported as not checked when none is: it never reads as up to date. A mod
    /// whose status is not "published" is offered no update (W0-1 section 7).
    ///
    /// Host-free: no BepInEx or UnityEngine reference. Development_Tools/Tests/MUM-CompiledLogic.Tests.ps1
    /// compiles and drives it.
    /// </summary>
    public static class PublicCheckPlan
    {
        /// <summary>The status Nexus gives a mod a player can download.</summary>
        public const string PublishedStatus = "published";

        /// <summary>True when an update check may run at all.</summary>
        public static bool CanCheck(bool hasApiKey, bool usePublicWithoutKey) => hasApiKey || usePublicWithoutKey;

        /// <param name="nexusId">the mod's Nexus id as UpdateChecker resolved it.</param>
        /// <param name="installedVersion">the installed version, compared with the listed one.</param>
        /// <param name="answer">the public answer; null when no query was sent.</param>
        /// <param name="hasApiKey">whether the per-mod request is available.</param>
        public static PublicCheckResult Decide(string nexusId, string installedVersion, NexusPublicAnswer answer, bool hasApiKey)
        {
            var notSettled = new PublicCheckResult { Outcome = hasApiKey ? PublicCheckOutcome.AskOnItsOwn : PublicCheckOutcome.Unknown };
            if (answer == null || !answer.Ok) return notSettled;
            if (!NexusPublicQuery.TryReadId(nexusId, out int id)) return notSettled;

            var mod = answer.Find(id);
            if (mod == null) return notSettled;
            if (!string.Equals(mod.Status, PublishedStatus, StringComparison.OrdinalIgnoreCase))
                return new PublicCheckResult { Outcome = PublicCheckOutcome.NotPublished, Mod = mod, NeedsUpdate = false };
            if (string.IsNullOrWhiteSpace(mod.Version)) return notSettled;

            return new PublicCheckResult
            {
                Outcome = PublicCheckOutcome.Answered,
                Mod = mod,
                NeedsUpdate = UpdateVerdict.NeedsUpdate(nexusId, installedVersion, mod.Version)
            };
        }
    }

    /// <summary>
    /// Whether a mod's latest Nexus version is an update over the installed one, the same for the
    /// public answer and the per-mod request: a version KnownModRegistry records as the same release
    /// under another number is not an update, and otherwise VersionComparer decides.
    ///
    /// Host-free: no BepInEx or UnityEngine reference (it lives in PublicCheckPlan.cs).
    /// </summary>
    public static class UpdateVerdict
    {
        public static bool NeedsUpdate(string nexusId, string installedVersion, string latestVersion)
        {
            var equivalent = KnownModRegistry.GetVersionEquivalent(nexusId, installedVersion);
            bool isEquivalent = equivalent != null && VersionComparer.Compare(equivalent, latestVersion) == 0;
            return !isEquivalent && VersionComparer.NeedsUpdate(installedVersion, latestVersion);
        }
    }
}
