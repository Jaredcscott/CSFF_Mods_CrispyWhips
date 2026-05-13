using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace mod_update_manager
{
    /// <summary>
    /// Utility class for comparing version strings
    /// </summary>
    public static class VersionComparer
    {
        /// <summary>
        /// Compares two version strings
        /// Returns: -1 if v1 < v2, 0 if equal, 1 if v1 > v2
        /// </summary>
        public static int Compare(string version1, string version2)
        {
            if (string.IsNullOrEmpty(version1) && string.IsNullOrEmpty(version2))
                return 0;
            if (string.IsNullOrEmpty(version1))
                return -1;
            if (string.IsNullOrEmpty(version2))
                return 1;

            // Normalize versions - remove leading 'v' or 'V'
            version1 = NormalizeVersion(version1);
            version2 = NormalizeVersion(version2);

            // Try semantic version comparison first
            var parts1 = ParseVersion(version1);
            var parts2 = ParseVersion(version2);

            var maxLength = Math.Max(parts1.Length, parts2.Length);

            for (int i = 0; i < maxLength; i++)
            {
                var p1 = i < parts1.Length ? parts1[i] : 0;
                var p2 = i < parts2.Length ? parts2[i] : 0;

                if (p1 < p2) return -1;
                if (p1 > p2) return 1;
            }

            return 0;
        }

        /// <summary>
        /// Checks if version1 is older than version2 (needs update)
        /// </summary>
        public static bool NeedsUpdate(string installedVersion, string latestVersion)
        {
            return Compare(installedVersion, latestVersion) < 0;
        }

        /// <summary>
        /// Normalizes a version string for comparison
        /// </summary>
        private static string NormalizeVersion(string version)
        {
            if (string.IsNullOrEmpty(version))
                return "0";

            // Remove leading 'v' or 'V'
            version = version.TrimStart('v', 'V');

            // Remove any suffix like -alpha, -beta, etc. for basic comparison
            var dashIndex = version.IndexOf('-');
            if (dashIndex > 0)
            {
                version = version.Substring(0, dashIndex);
            }

            // Expand compact leading-zero segments: "1.05" → "1.0.5", "1.02" → "1.0.2"
            // A segment like "05" signals minor=0, patch=5 (mod authors write it as shorthand).
            // Segments without a leading zero (e.g. "10", "11") are genuine two-digit minor/patch numbers.
            version = ExpandLeadingZeroSegments(version.Trim());

            return version;
        }

        /// <summary>
        /// Expands version segments that have a leading zero: "05" → "0.5".
        /// Only affects exactly-2-digit segments whose first char is '0'.
        /// Leaves segments like "10", "11", "100" untouched.
        /// </summary>
        private static string ExpandLeadingZeroSegments(string version)
        {
            var segments = version.Split('.');
            var expanded = new System.Text.StringBuilder();
            for (int i = 0; i < segments.Length; i++)
            {
                if (i > 0) expanded.Append('.');
                var seg = segments[i];
                if (seg.Length == 2 && seg[0] == '0' && char.IsDigit(seg[1]))
                {
                    // "05" → "0.5"
                    expanded.Append('0');
                    expanded.Append('.');
                    expanded.Append(seg[1]);
                }
                else
                {
                    expanded.Append(seg);
                }
            }
            return expanded.ToString();
        }

        /// <summary>
        /// Parses a version string into numeric parts
        /// </summary>
        private static int[] ParseVersion(string version)
        {
            var parts = new List<int>();
            var segments = version.Split('.', '_', '-');

            foreach (var segment in segments)
            {
                // Extract numeric part from segment
                var match = Regex.Match(segment, @"^\d+");
                if (match.Success && int.TryParse(match.Value, out int num))
                {
                    parts.Add(num);
                }
            }

            // Ensure at least one part
            if (parts.Count == 0)
            {
                parts.Add(0);
            }

            return parts.ToArray();
        }
    }
}
