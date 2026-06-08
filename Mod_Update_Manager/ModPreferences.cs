using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace mod_update_manager
{
    public class ModPreferenceEntry
    {
        public bool Ignored;
        public bool Favorited;
        public string Notes;
    }

    /// <summary>
    /// Persists per-mod ignore/favorite/notes preferences to a JSON file
    /// </summary>
    public class ModPreferences
    {
        private readonly string _filePath;
        private Dictionary<string, ModPreferenceEntry> _prefs;

        public ModPreferences(string filePath)
        {
            _filePath = filePath;
            _prefs = new Dictionary<string, ModPreferenceEntry>(StringComparer.OrdinalIgnoreCase);
            Load();
        }

        public bool IsIgnored(string folderName) => TryGet(folderName)?.Ignored ?? false;
        public bool IsFavorited(string folderName) => TryGet(folderName)?.Favorited ?? false;
        public string GetNotes(string folderName) => TryGet(folderName)?.Notes ?? "";

        public void SetIgnored(string folderName, bool ignored)
        {
            GetOrCreate(folderName).Ignored = ignored;
            Save();
        }

        public void SetFavorited(string folderName, bool favorited)
        {
            GetOrCreate(folderName).Favorited = favorited;
            Save();
        }

        public void SetNotes(string folderName, string notes)
        {
            GetOrCreate(folderName).Notes = notes ?? "";
            Save();
        }

        /// <summary>
        /// Stamps installed mod with its stored preferences
        /// </summary>
        public void ApplyToMod(InstalledModInfo mod)
        {
            var entry = TryGet(mod.FolderName);
            if (entry == null) return;
            mod.IsIgnored = entry.Ignored;
            mod.IsFavorited = entry.Favorited;
            mod.Notes = entry.Notes;
        }

        /// <summary>
        /// Generates a plain-text mod list export suitable for bug reports
        /// </summary>
        public static string ExportText(List<InstalledModInfo> mods)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Installed Mods");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine(new string('-', 60));

            foreach (var m in mods)
            {
                var status = m.NeedsUpdate ? "UPDATE AVAILABLE"
                    : (m.CheckFailed ? "UNCHECKED" : "UP TO DATE");
                var tags = (m.IsFavorited ? " [FAV]" : "") + (m.IsIgnored ? " [IGNORED]" : "");
                sb.AppendLine($"{m.Name} v{m.Version}{tags}");
                sb.AppendLine($"  Author: {m.Author ?? "Unknown"}  |  Status: {status}");
                if (m.NeedsUpdate)
                    sb.AppendLine($"  Latest: v{m.LatestVersion}" +
                        (m.IsMajorVersionUpdate ? "  [MAJOR UPDATE]" : ""));
                if (!string.IsNullOrEmpty(m.Notes))
                    sb.AppendLine($"  Notes: {m.Notes}");
            }
            return sb.ToString();
        }

        // ── private helpers ──────────────────────────────────────────────────

        private ModPreferenceEntry TryGet(string folderName)
        {
            if (string.IsNullOrEmpty(folderName)) return null;
            _prefs.TryGetValue(folderName, out var entry);
            return entry;
        }

        private ModPreferenceEntry GetOrCreate(string folderName)
        {
            if (!_prefs.TryGetValue(folderName, out var entry))
            {
                entry = new ModPreferenceEntry();
                _prefs[folderName] = entry;
            }
            return entry;
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_filePath)) return;
                var json = File.ReadAllText(_filePath);
                var objectPattern = new Regex("\"([^\"]+)\"\\s*:\\s*\\{([^{}]*)\\}", RegexOptions.Singleline);
                foreach (Match m in objectPattern.Matches(json))
                {
                    var key = m.Groups[1].Value;
                    var body = m.Groups[2].Value;
                    _prefs[key] = new ModPreferenceEntry
                    {
                        Ignored = ReadBool(body, "ignored"),
                        Favorited = ReadBool(body, "favorited"),
                        Notes = ReadString(body, "notes") ?? ""
                    };
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"Could not load mod preferences: {ex}");
            }
        }

        private void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var sb = new StringBuilder("{");
                bool first = true;
                foreach (var kvp in _prefs)
                {
                    if (!kvp.Value.Ignored && !kvp.Value.Favorited && string.IsNullOrEmpty(kvp.Value.Notes))
                        continue;
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append($"\n  \"{Esc(kvp.Key)}\": {{");
                    sb.Append($"\"ignored\": {(kvp.Value.Ignored ? "true" : "false")}, ");
                    sb.Append($"\"favorited\": {(kvp.Value.Favorited ? "true" : "false")}, ");
                    sb.Append($"\"notes\": \"{Esc(kvp.Value.Notes ?? "")}\"");
                    sb.Append('}');
                }
                sb.Append("\n}");
                File.WriteAllText(_filePath, sb.ToString());
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"Could not save mod preferences: {ex}");
            }
        }

        private static bool ReadBool(string json, string key)
        {
            var m = Regex.Match(json, $"\"{Regex.Escape(key)}\"\\s*:\\s*(true|false)", RegexOptions.IgnoreCase);
            return m.Success && m.Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadString(string json, string key)
        {
            var m = Regex.Match(json, $"\"{Regex.Escape(key)}\"\\s*:\\s*\"((?:\\\\.|[^\\\"])*)\"");
            return m.Success
                ? m.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\")
                : null;
        }

        private static string Esc(string s) =>
            s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
