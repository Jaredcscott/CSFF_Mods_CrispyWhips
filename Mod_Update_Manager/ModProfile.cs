// Host-free: no BepInEx or UnityEngine reference. Read-only profiles (plan feature 20): a profile is the list
// of installed mods (folder, name, version) saved under a name as a small text file, which can be compared
// with the current install. Nothing here installs, updates or removes a mod. Development_Tools/Tests/
// MUM-CompiledLogic.Tests.ps1 compiles this file (with InstalledModInfo and VersionComparer) and drives it
// against scratch folders.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace mod_update_manager
{
    public sealed class ProfileRow
    {
        public string Folder;
        public string Name;
        public string Version;
    }

    public enum ProfileDiffKind
    {
        /// <summary>In the profile, not installed here.</summary>
        Missing,
        /// <summary>Installed here, not in the profile.</summary>
        Extra,
        /// <summary>Installed here at an older version than the profile has.</summary>
        Older,
        /// <summary>Installed here at a newer version than the profile has.</summary>
        Newer,
        Same
    }

    public sealed class ProfileDiffRow
    {
        public string Folder;
        public string Name;
        public string ProfileVersion;
        public string CurrentVersion;
        public ProfileDiffKind Kind;
    }

    /// <summary>The snapshot, the text format and the comparison. One line per mod, <c>folder|name|version</c>;
    /// a line that is blank or starts with # is ignored and any other line that cannot be read is counted.</summary>
    public static class ModProfile
    {
        // English-column text of Localization/SimpEn.csv rows Mod_Update_Manager_ProfileMissing, _ProfileExtra,
        // _ProfileOlder and _ProfileNewer ({0}=name, {1}/{2}=versions as the format says).
        public static string MissingFormat => MumText.Get("ProfileMissing");
        public static string ExtraFormat => MumText.Get("ProfileExtra");
        public static string OlderFormat => MumText.Get("ProfileOlder");
        public static string NewerFormat => MumText.Get("ProfileNewer");

        public static List<ProfileRow> Snapshot(IEnumerable<InstalledModInfo> mods)
        {
            var rows = new List<ProfileRow>();
            if (mods == null) return rows;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in mods)
            {
                if (m == null) continue;
                var folder = Clean(m.FolderName);
                if (folder.Length == 0 || !seen.Add(folder)) continue;
                var name = Clean(m.Name);
                var version = Clean(m.Version);
                rows.Add(new ProfileRow { Folder = folder, Name = name.Length == 0 ? folder : name, Version = version.Length == 0 ? "Unknown" : version });
            }
            return rows.OrderBy(r => r.Folder, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static string ToText(IEnumerable<ProfileRow> rows)
        {
            var sb = new StringBuilder();
            foreach (var r in rows) sb.Append(r.Folder).Append('|').Append(r.Name).Append('|').Append(r.Version).Append('\n');
            return sb.ToString();
        }

        public static List<ProfileRow> Parse(string text, out int skipped)
        {
            skipped = 0;
            var rows = new List<ProfileRow>();
            if (string.IsNullOrEmpty(text)) return rows;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.Trim().Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                var parts = line.Split('|');
                if (parts.Length != 3 || parts[0].Trim().Length == 0 || parts[2].Trim().Length == 0)
                {
                    skipped++;
                    continue;
                }
                rows.Add(new ProfileRow { Folder = parts[0].Trim(), Name = parts[1].Trim(), Version = parts[2].Trim() });
            }
            return rows;
        }

        /// <summary>The differences, and the rows that match, between a profile and the current install, in
        /// folder order. "Older" and "newer" are about what is installed HERE compared with the profile.</summary>
        public static List<ProfileDiffRow> Diff(IEnumerable<ProfileRow> profile, IEnumerable<ProfileRow> current)
        {
            var here = new Dictionary<string, ProfileRow>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in current ?? new ProfileRow[0]) here[c.Folder] = c;
            var there = new Dictionary<string, ProfileRow>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in profile ?? new ProfileRow[0]) there[p.Folder] = p;

            var result = new List<ProfileDiffRow>();
            foreach (var folder in there.Keys.Union(here.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                ProfileRow p, c;
                there.TryGetValue(folder, out p);
                here.TryGetValue(folder, out c);
                var row = new ProfileDiffRow
                {
                    Folder = folder,
                    Name = (c != null ? c.Name : p.Name),
                    ProfileVersion = p != null ? p.Version : "",
                    CurrentVersion = c != null ? c.Version : ""
                };
                if (c == null) row.Kind = ProfileDiffKind.Missing;
                else if (p == null) row.Kind = ProfileDiffKind.Extra;
                else
                {
                    var cmp = VersionComparer.Compare(c.Version, p.Version);
                    row.Kind = cmp < 0 ? ProfileDiffKind.Older : cmp > 0 ? ProfileDiffKind.Newer : ProfileDiffKind.Same;
                }
                result.Add(row);
            }
            return result;
        }

        /// <summary>One line for a difference; empty for a row that matches.</summary>
        public static string Describe(ProfileDiffRow r)
        {
            if (r == null) return "";
            switch (r.Kind)
            {
                case ProfileDiffKind.Missing: return string.Format(MissingFormat, r.Name, r.ProfileVersion);
                case ProfileDiffKind.Extra: return string.Format(ExtraFormat, r.Name, r.CurrentVersion);
                case ProfileDiffKind.Older: return string.Format(OlderFormat, r.Name, r.CurrentVersion, r.ProfileVersion);
                case ProfileDiffKind.Newer: return string.Format(NewerFormat, r.Name, r.CurrentVersion, r.ProfileVersion);
                default: return "";
            }
        }

        /// <summary>The name a profile may be saved under: letters, digits, spaces, hyphens and underscores,
        /// at most 60 characters, trimmed. Anything else (a separator, a dot, an empty name) is null.</summary>
        public static string SafeName(string name)
        {
            var n = (name ?? "").Trim();
            if (n.Length == 0 || n.Length > 60) return null;
            return Regex.IsMatch(n, "^[A-Za-z0-9 _-]+$") ? n : null;
        }

        // The separator and the line breaks are the file's own syntax.
        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Trim().Replace('|', '_').Replace('\r', ' ').Replace('\n', ' ');
        }
    }

    /// <summary>The folder of saved profiles. It only reads and writes files whose names are a safe name plus
    /// the profile suffix directly inside that folder, and Delete re-derives its path before it removes
    /// anything.</summary>
    public sealed class ProfileStore
    {
        public const string Suffix = ".profile.txt";
        private readonly string _dir;

        public ProfileStore(string dir)
        {
            _dir = Path.GetFullPath(dir);
        }

        /// <summary>The file a name maps to, or null for a name that is not safe or would land outside the folder.</summary>
        public string PathFor(string name)
        {
            var safe = ModProfile.SafeName(name);
            if (safe == null) return null;
            var full = Path.GetFullPath(Path.Combine(_dir, safe + Suffix));
            var inside = string.Equals(Path.GetDirectoryName(full), _dir.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
            return inside ? full : null;
        }

        public bool Save(string name, IEnumerable<ProfileRow> rows)
        {
            var path = PathFor(name);
            if (path == null) return false;
            try
            {
                Directory.CreateDirectory(_dir);
                File.WriteAllText(path, ModProfile.ToText(rows), new UTF8Encoding(false));
                return true;
            }
            catch (Exception ex)
            {
                MumLog.Warning($"Could not save profile '{name}': {ex}");
                return false;
            }
        }

        public List<string> List()
        {
            var names = new List<string>();
            try
            {
                if (!Directory.Exists(_dir)) return names;
                foreach (var file in Directory.GetFiles(_dir, "*" + Suffix))
                {
                    var fileName = Path.GetFileName(file);
                    var name = fileName.Substring(0, fileName.Length - Suffix.Length);
                    if (ModProfile.SafeName(name) != null) names.Add(name);
                }
            }
            catch (Exception ex)
            {
                MumLog.Warning($"Could not list the saved profiles: {ex}");
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        /// <summary>The rows of a saved profile, or null when it does not exist or cannot be read.</summary>
        public List<ProfileRow> Load(string name, out int skipped)
        {
            skipped = 0;
            var path = PathFor(name);
            if (path == null || !File.Exists(path)) return null;
            try
            {
                return ModProfile.Parse(File.ReadAllText(path, Encoding.UTF8), out skipped);
            }
            catch (Exception ex)
            {
                MumLog.Warning($"Could not read profile '{name}': {ex}");
                return null;
            }
        }

        /// <summary>Removes one profile file. The path is derived again from the name here, so only a file of
        /// that name inside the profiles folder can go.</summary>
        public bool Delete(string name)
        {
            var path = PathFor(name);
            if (path == null || !File.Exists(path)) return false;
            try
            {
                File.Delete(path);
                return true;
            }
            catch (Exception ex)
            {
                MumLog.Warning($"Could not delete profile '{name}': {ex}");
                return false;
            }
        }
    }
}
