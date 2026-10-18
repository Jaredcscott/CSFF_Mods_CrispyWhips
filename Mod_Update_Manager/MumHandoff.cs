using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace mod_update_manager
{
    /// <summary>One suite mod the handoff note says had a newer version on Nexus.</summary>
    public sealed class HandoffMod
    {
        public string Folder;
        public string Installed;
        public string Latest;
    }

    /// <summary>
    /// The note an older Mod Update Manager leaves for the next one: which MUM wrote it, the newer MUM
    /// version it saw on Nexus, and the suite mods whose Nexus version was newer than the installed one.
    /// </summary>
    public sealed class HandoffNote
    {
        public string WrittenBy;
        public string NewerMum;
        public string Utc;
        public List<HandoffMod> Mods = new List<HandoffMod>();
    }

    /// <summary>What a newly started MUM does with the suite rows after an upgrade.</summary>
    public sealed class HandoffPlan
    {
        /// <summary>The MUM version the player came from.</summary>
        public string From;
        /// <summary>The installed rows this MUM bundles a newer copy of: the ones it ticks.</summary>
        public List<string> Ticked = new List<string>();
        /// <summary>Rows the note named whose bundled copy is still older than the version Nexus had.</summary>
        public List<string> StillBehind = new List<string>();
    }

    /// <summary>
    /// The upgrade handoff between two Mod Update Manager versions. MUM cannot replace its own running
    /// DLL, so when an update check finds a newer MUM on Nexus, the window recommends downloading it,
    /// and WriteNote leaves a small text file in BepInEx/config (which a MUM upgrade never replaces).
    /// The next MUM reads it at startup: when it is newer than the one that wrote it, Plan ticks every
    /// installed suite row it bundles a newer copy of, the window opens on Install and Update, and the
    /// player presses Apply once. A player upgrading from a MUM that wrote no note gets the same plan
    /// from the version log, which records MUM's own folder changing version.
    ///
    /// Nothing is installed without the Apply press. Rows that are not installed are never ticked: the
    /// handoff updates the mods the player has, it does not add new ones.
    ///
    /// Host-free: no BepInEx or UnityEngine reference. Development_Tools/Tests/MUM-CompiledLogic.Tests.ps1
    /// compiles and drives it.
    /// </summary>
    public static class MumHandoff
    {
        public const string FileName = "ModUpdateManager_Handoff.txt";
        public const string Header = "mum-handoff 1";

        /// <summary>True when Nexus lists a MUM version newer than the running one.</summary>
        public static bool NewerMumAvailable(string ownVersion, string nexusVersion)
        {
            if (string.IsNullOrWhiteSpace(nexusVersion) || string.IsNullOrWhiteSpace(ownVersion)) return false;
            return VersionComparer.Compare(nexusVersion, ownVersion) > 0;
        }

        /// <summary>True when the note was left by an older MUM for this one to act on.</summary>
        public static bool IsForThisMum(HandoffNote note, string ownVersion)
        {
            if (note == null || string.IsNullOrWhiteSpace(note.WrittenBy)) return false;
            return VersionComparer.Compare(ownVersion, note.WrittenBy) > 0;
        }

        /// <summary>
        /// The MUM version this one was upgraded from, or null when this start is not an upgrade: the
        /// note's writer when an older MUM left one, otherwise the version log's record of MUM's own
        /// folder moving up to this version (a MUM older than 2.1.70 leaves no note). A first install
        /// (no earlier version) and a downgrade are not upgrades.
        /// </summary>
        public static string UpgradedFrom(HandoffNote note, IEnumerable<VersionEvent> changes, string ownVersion)
        {
            if (IsForThisMum(note, ownVersion)) return note.WrittenBy;
            if (changes == null) return null;
            foreach (var e in changes)
            {
                if (e == null || string.IsNullOrWhiteSpace(e.From)) continue;
                if (!KnownModRegistry.IsSelfMod(e.Folder)) continue;
                if (VersionComparer.Compare(ownVersion, e.From) > 0) return e.From;
            }
            return null;
        }

        /// <summary>
        /// The note to leave after a check: the newer MUM version and every suite mod whose Nexus
        /// version is newer than its installed one. <paramref name="checkedMods"/> pairs a folder with
        /// (installed, latest) as the check read them; folders outside the suite are left out.
        /// </summary>
        public static HandoffNote BuildNote(string ownVersion, string newerMum, DateTime utcNow,
            IEnumerable<SuiteModEntry> suite, IDictionary<string, KeyValuePair<string, string>> checkedMods)
        {
            var note = new HandoffNote { WrittenBy = ownVersion, NewerMum = newerMum, Utc = utcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") };
            if (suite == null || checkedMods == null) return note;
            foreach (var e in suite)
            {
                if (!checkedMods.TryGetValue(e.FolderName, out var v)) continue;
                if (string.IsNullOrWhiteSpace(v.Value) || VersionComparer.Compare(v.Value, v.Key) <= 0) continue;
                note.Mods.Add(new HandoffMod { Folder = e.FolderName, Installed = v.Key, Latest = v.Value });
            }
            return note;
        }

        /// <summary>
        /// The ticks a newly started MUM sets. Every installed row with a newer bundled copy is ticked,
        /// note or not (the bundled copies are what this MUM can install); a row the note named whose
        /// bundled copy is still older than what Nexus had is listed in StillBehind so the window can
        /// say so instead of reading as done.
        /// </summary>
        public static HandoffPlan Plan(string fromVersion, HandoffNote note, IEnumerable<SuiteModEntry> suite)
        {
            var plan = new HandoffPlan { From = fromVersion };
            var byFolder = new Dictionary<string, SuiteModEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in suite)
            {
                byFolder[e.FolderName] = e;
                if (e.Status == SuiteInstallStatus.OutOfDate) plan.Ticked.Add(e.FolderName);
            }
            if (note != null)
            {
                foreach (var m in note.Mods)
                {
                    if (!byFolder.TryGetValue(m.Folder ?? "", out var e)) continue;
                    if (VersionComparer.Compare(e.EmbeddedVersion, m.Latest) < 0) plan.StillBehind.Add(e.DisplayName ?? e.FolderName);
                }
            }
            return plan;
        }

        public static string Serialize(HandoffNote note)
        {
            var sb = new StringBuilder();
            sb.Append(Header).Append('\n');
            sb.Append("written_by\t").Append(Clean(note.WrittenBy)).Append('\n');
            sb.Append("newer_mum\t").Append(Clean(note.NewerMum)).Append('\n');
            sb.Append("utc\t").Append(Clean(note.Utc)).Append('\n');
            foreach (var m in note.Mods)
                sb.Append("mod\t").Append(Clean(m.Folder)).Append('\t').Append(Clean(m.Installed)).Append('\t').Append(Clean(m.Latest)).Append('\n');
            return sb.ToString();
        }

        /// <summary>The note in <paramref name="text"/>, or null when it is not one (wrong header, no writer).</summary>
        public static HandoffNote Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var lines = text.Replace("\r\n", "\n").Split('\n');
            if (lines.Length == 0 || lines[0].Trim() != Header) return null;
            var note = new HandoffNote();
            for (int i = 1; i < lines.Length; i++)
            {
                var parts = lines[i].Split('\t');
                switch (parts[0])
                {
                    case "written_by": if (parts.Length > 1) note.WrittenBy = parts[1]; break;
                    case "newer_mum": if (parts.Length > 1) note.NewerMum = parts[1]; break;
                    case "utc": if (parts.Length > 1) note.Utc = parts[1]; break;
                    case "mod":
                        if (parts.Length >= 4 && parts[1].Length > 0)
                            note.Mods.Add(new HandoffMod { Folder = parts[1], Installed = parts[2], Latest = parts[3] });
                        break;
                }
            }
            return string.IsNullOrWhiteSpace(note.WrittenBy) ? null : note;
        }

        /// <summary>Reads the note at <paramref name="path"/>; null when there is none or it cannot be read.</summary>
        public static HandoffNote Read(string path)
        {
            try
            {
                return File.Exists(path) ? Parse(File.ReadAllText(path, Encoding.UTF8)) : null;
            }
            catch (Exception ex)
            {
                MumLog.Info?.Invoke($"Handoff note not read ({path}): {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>Writes the note through a temp file, so a game closed mid-write leaves the old note or the new one.</summary>
        public static bool Write(string path, HandoffNote note)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var temp = path + ".tmp";
                File.WriteAllText(temp, Serialize(note), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
                return true;
            }
            catch (Exception ex)
            {
                MumLog.Warning?.Invoke($"Handoff note not written ({path}): {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        /// <summary>Removes the note once acted on; a note that cannot be removed is logged and left.</summary>
        public static void Delete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                MumLog.Warning?.Invoke($"Handoff note not removed ({path}): {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static string Clean(string s) => (s ?? "").Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
    }
}
