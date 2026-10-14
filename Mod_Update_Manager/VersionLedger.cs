// Host-free: no BepInEx or UnityEngine reference. The record of which version of each mod this install has
// held and where it was seen (plan feature 19), kept in one small text file and read back at launch.
// Development_Tools/Tests/MUM-CompiledLogic.Tests.ps1 compiles this file (with InstalledModInfo) and drives
// it against scratch files.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace mod_update_manager
{
    /// <summary>One change of a mod's installed version, or the first sight of a mod.</summary>
    public sealed class VersionEvent
    {
        public string Folder;
        /// <summary>The version before the change; empty when the mod was first seen.</summary>
        public string From;
        public string To;
        /// <summary>When it was seen, as yyyy-MM-ddTHH:mm:ssZ (UTC), which sorts as text.</summary>
        public string Utc;
        /// <summary>One of <see cref="VersionLedger.SourceBaseline"/>, <see cref="VersionLedger.SourceScan"/>,
        /// <see cref="VersionLedger.SourceApply"/>.</summary>
        public string Source;
    }

    /// <summary>
    /// One line per event, <c>folder|from|to|utc|source</c>, plus at most one <c>#seen|utc</c> line that
    /// marks when the player last pressed Mark all as seen. The first launch with no events records every
    /// installed mod once as a baseline and shows nothing; after that a changed (or newly appeared) version
    /// is an event, and an event newer than the seen mark is what the NEW chip shows. The file is written
    /// to a temp file and moved into place, so a crash never leaves a truncated one, and a line that cannot
    /// be read is skipped and counted in a single log line, never silently.
    /// </summary>
    public sealed class VersionLedger
    {
        public const string SourceBaseline = "baseline";
        public const string SourceScan = "scan";
        public const string SourceApply = "apply";

        /// <summary>The most events kept for one mod; the oldest go first.</summary>
        public const int MaxEventsPerMod = 20;

        // English-column text of Localization/SimpEn.csv rows Mod_Update_Manager_VersionNewChanged,
        // _VersionNewInstalled, _VersionLineChanged, _VersionLineFirstSeen, _VersionSourceScan and
        // _VersionSourceApply ({0}/{1}/{2} are filled by ChipText and LineText).
        public static string NewChangedFormat => MumText.Get("VersionNewChanged");
        public static string NewInstalledFormat => MumText.Get("VersionNewInstalled");
        public static string LineChangedFormat => MumText.Get("VersionLineChanged");
        public static string LineFirstSeenFormat => MumText.Get("VersionLineFirstSeen");
        public static string SourceScanText => MumText.Get("VersionSourceScan");
        public static string SourceApplyText => MumText.Get("VersionSourceApply");

        private const string SeenPrefix = "#seen|";

        private readonly string _path;
        private readonly Func<DateTime> _utcNow;
        private readonly List<VersionEvent> _events = new List<VersionEvent>();
        private string _seenUtc = "";

        /// <summary>How many lines of the file could not be read at load.</summary>
        public int SkippedLines { get; private set; }

        public VersionLedger(string path, Func<DateTime> utcNow = null)
        {
            _path = path;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
            Load();
        }

        /// <summary>
        /// Compares the scan with the last version stored for each mod and records what changed. Returns
        /// the new events (empty on the baseline run). A mod whose version could not be read is left as
        /// it was stored, never recorded as a change.
        /// </summary>
        public List<VersionEvent> Observe(IEnumerable<InstalledModInfo> mods)
        {
            var changes = new List<VersionEvent>();
            if (mods == null) return changes;

            bool baseline = _events.Count == 0;
            var last = LastVersions();
            var stamp = Stamp();
            var added = new List<VersionEvent>();

            foreach (var mod in mods)
            {
                if (mod == null) continue;
                var folder = Clean(mod.FolderName);
                var version = Clean(mod.Version);
                if (folder.Length == 0 || version.Length == 0 || IsUnreadable(version)) continue;

                if (baseline)
                {
                    added.Add(Make(folder, "", version, stamp, SourceBaseline));
                    last[folder] = version;
                    continue;
                }

                string previous;
                if (!last.TryGetValue(folder, out previous))
                {
                    var appeared = Make(folder, "", version, stamp, SourceScan);
                    added.Add(appeared);
                    changes.Add(appeared);
                }
                else if (!string.Equals(previous, version, StringComparison.Ordinal))
                {
                    var moved = Make(folder, previous, version, stamp, SourceScan);
                    added.Add(moved);
                    changes.Add(moved);
                }
                last[folder] = version;
            }

            if (added.Count > 0)
            {
                _events.AddRange(added);
                Prune();
                Save();
            }
            return changes;
        }

        /// <summary>Records an update the suite Apply just made, so the startup scan that follows the
        /// restart finds the stored version already current and adds nothing.</summary>
        public void RecordApply(string folder, string from, string to)
        {
            var f = Clean(folder);
            var newer = Clean(to);
            if (f.Length == 0 || newer.Length == 0 || IsUnreadable(newer)) return;
            var older = Clean(from);
            if (IsUnreadable(older)) older = "";
            if (string.Equals(older, newer, StringComparison.Ordinal)) return;

            _events.Add(Make(f, older, newer, Stamp(), SourceApply));
            Prune();
            Save();
        }

        /// <summary>The newest change to a mod that the player has not marked as seen, or null. The
        /// baseline is never a change.</summary>
        public VersionEvent UnseenFor(string folder)
        {
            var f = Clean(folder);
            for (int i = _events.Count - 1; i >= 0; i--)
            {
                var e = _events[i];
                if (!string.Equals(e.Folder, f, StringComparison.OrdinalIgnoreCase)) continue;
                if (e.Source == SourceBaseline) continue;
                return IsUnseen(e) ? e : null;
            }
            return null;
        }

        /// <summary>True while any mod has a change the player has not marked as seen.</summary>
        public bool AnyUnseen
        {
            get { return _events.Any(e => e.Source != SourceBaseline && IsUnseen(e)); }
        }

        /// <summary>Every event kept for a mod, newest first.</summary>
        public List<VersionEvent> HistoryFor(string folder)
        {
            var f = Clean(folder);
            var list = _events.Where(e => string.Equals(e.Folder, f, StringComparison.OrdinalIgnoreCase)).ToList();
            list.Reverse();
            return list;
        }

        /// <summary>Marks every change so far as seen. The chips go; the history stays.</summary>
        public void MarkAllSeen()
        {
            _seenUtc = Stamp();
            Save();
        }

        /// <summary>The chip's text for a change: "NEW: 1.13.7 to 1.14.0", or "NEW: installed 1.0.0" for a
        /// mod that appeared since the last launch.</summary>
        public static string ChipText(VersionEvent e)
        {
            if (e == null) return "";
            return string.IsNullOrEmpty(e.From)
                ? string.Format(NewInstalledFormat, e.To)
                : string.Format(NewChangedFormat, e.From, e.To);
        }

        /// <summary>One line of a mod's history: the date, the change, and where it was seen.</summary>
        public static string LineText(VersionEvent e)
        {
            if (e == null) return "";
            var date = (e.Utc ?? "").Length >= 10 ? e.Utc.Substring(0, 10) : (e.Utc ?? "");
            var where = e.Source == SourceApply ? SourceApplyText : SourceScanText;
            return string.IsNullOrEmpty(e.From)
                ? string.Format(LineFirstSeenFormat, date, e.To, where)
                : string.Format(LineChangedFormat, date, e.From, e.To, where);
        }

        // ── private helpers ──────────────────────────────────────────────────

        private bool IsUnseen(VersionEvent e)
        {
            return string.CompareOrdinal(e.Utc ?? "", _seenUtc ?? "") > 0;
        }

        private string Stamp()
        {
            return _utcNow().ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        private static VersionEvent Make(string folder, string from, string to, string utc, string source)
        {
            return new VersionEvent { Folder = folder, From = from, To = to, Utc = utc, Source = source };
        }

        private static bool IsUnreadable(string version)
        {
            return version == "Unknown" || version == "Parse Error";
        }

        // The field separator and the line breaks are the file's own syntax, so a folder name or a version
        // carrying one is stored with an underscore instead.
        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Trim().Replace('|', '_').Replace('\r', ' ').Replace('\n', ' ');
        }

        private Dictionary<string, string> LastVersions()
        {
            var last = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in _events) last[e.Folder] = e.To;
            return last;
        }

        private void Prune()
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in _events) counts[e.Folder] = counts.TryGetValue(e.Folder, out var n) ? n + 1 : 1;

            var kept = new List<VersionEvent>();
            foreach (var e in _events)
            {
                if (counts[e.Folder] > MaxEventsPerMod)
                {
                    counts[e.Folder]--; // this one is among the oldest of its mod: drop it
                    continue;
                }
                kept.Add(e);
            }
            _events.Clear();
            _events.AddRange(kept);
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_path)) return;
                foreach (var raw in File.ReadAllLines(_path, Encoding.UTF8))
                {
                    var line = raw.TrimEnd('\r');
                    if (line.Length == 0) continue;

                    if (line.StartsWith(SeenPrefix, StringComparison.Ordinal))
                    {
                        var seen = line.Substring(SeenPrefix.Length).Trim();
                        if (seen.Length > 0) _seenUtc = seen; else SkippedLines++;
                        continue;
                    }

                    var parts = line.Split('|');
                    if (parts.Length != 5
                        || parts[0].Trim().Length == 0 || parts[2].Trim().Length == 0 || parts[3].Trim().Length == 0
                        || (parts[4] != SourceBaseline && parts[4] != SourceScan && parts[4] != SourceApply))
                    {
                        SkippedLines++;
                        continue;
                    }
                    _events.Add(Make(parts[0].Trim(), parts[1].Trim(), parts[2].Trim(), parts[3].Trim(), parts[4]));
                }
                Prune();
                if (SkippedLines > 0)
                    MumLog.Info($"Version log: skipped {SkippedLines} line(s) that could not be read ({_path}).");
            }
            catch (Exception ex)
            {
                MumLog.Warning($"Could not load the version log: {ex}");
            }
        }

        private void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var sb = new StringBuilder();
                foreach (var e in _events)
                    sb.Append(e.Folder).Append('|').Append(e.From).Append('|').Append(e.To).Append('|')
                      .Append(e.Utc).Append('|').Append(e.Source).Append('\n');
                if (!string.IsNullOrEmpty(_seenUtc))
                    sb.Append(SeenPrefix).Append(_seenUtc).Append('\n');

                var temp = _path + ".tmp";
                File.WriteAllText(temp, sb.ToString(), new UTF8Encoding(false));
                if (File.Exists(_path)) File.Replace(temp, _path, null);
                else File.Move(temp, _path);
            }
            catch (Exception ex)
            {
                MumLog.Warning($"Could not save the version log: {ex}");
            }
        }
    }
}
