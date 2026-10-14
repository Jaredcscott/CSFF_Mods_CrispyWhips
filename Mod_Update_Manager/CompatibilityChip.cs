using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace mod_update_manager
{
    /// <summary>
    /// Reads the game build a mod author says a release was tested on out of that mod's Nexus
    /// changelog, and compares it with the game actually running. The token is the release-note
    /// convention README.md "Release Notes And Beta Compatibility" asks authors for:
    /// "Compatible with Card Survival: Fantasy Forest EA 0.68a".
    ///
    /// Host-free: Development_Tools/Tests/MUM-CompiledLogic.Tests.ps1 compiles and drives it.
    /// </summary>
    public static class CompatibilityChip
    {
        public enum Verdict
        {
            /// <summary>Either side could not be read as MAJOR.MINOR; show the token uncoloured.</summary>
            Unknown,
            /// <summary>Tested on the running build or a newer one.</summary>
            Current,
            /// <summary>Tested on a build older than the one running.</summary>
            Older
        }

        private static readonly Regex TokenPattern = new Regex(
            @"Compatible with[^\n]*?\bEA\s*(\d+\.\d+[a-z]?)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // Application.version on this game is MAJOR.MINOR plus an optional hotfix letter ("0.68b",
        // the one version string in globalgamemanagers, EA 0.68b, 2026-09-26). An "EA " prefix is
        // tolerated in case a build ever carries one.
        private static readonly Regex BuildPattern = new Regex(
            @"^\s*(?:EA\s*)?(\d+\.\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// Returns the tested-build token ("0.68a") from the newest changelog version that carries
        /// one, or null. Expects newest-first order, as ChangelogParser.Parse returns it.
        /// </summary>
        public static string FindTestedBuild(IList<(string Version, List<string> Entries)> changelog)
        {
            if (changelog == null) return null;
            foreach (var version in changelog)
            {
                if (version.Entries == null) continue;
                foreach (var entry in version.Entries)
                {
                    if (string.IsNullOrEmpty(entry)) continue;
                    var m = TokenPattern.Match(entry);
                    if (m.Success) return m.Groups[1].Value;
                }
            }
            return null;
        }

        /// <summary>
        /// Compares a tested build with the running game version, ignoring hotfix letters on both
        /// sides (the README's own letterless convention): tested 0.68a on a running 0.68b is Current.
        /// </summary>
        public static Verdict Compare(string testedBuild, string runningGameVersion)
        {
            var tested = MajorMinor(testedBuild);
            var running = MajorMinor(runningGameVersion);
            if (tested == null || running == null) return Verdict.Unknown;
            return VersionComparer.Compare(tested, running) >= 0 ? Verdict.Current : Verdict.Older;
        }

        private static string MajorMinor(string build)
        {
            if (string.IsNullOrEmpty(build)) return null;
            var m = BuildPattern.Match(build);
            return m.Success ? m.Groups[1].Value : null;
        }
    }
}
