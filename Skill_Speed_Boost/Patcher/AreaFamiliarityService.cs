using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx.Logging;
using CSFFModFramework.Api;

namespace Skill_Speed_Boost.Patcher;

/// <summary>
/// Tracks per-location forage/work counts and computes a familiarity XP multiplier.
/// State persists to flat TSV files under BepInEx/config/SkillSpeedBoost/ (or its Slot&lt;N&gt;
/// folder while PerSaveProgression is on; ProgressionStore decides which):
///   AreaFamiliarity.tsv      key TAB visits        (unchanged since before 1.11.0)
///   AreaFamiliarityDays.tsv  key TAB lastVisitedDay (1.11.0+, read only by the optional decay)
///
/// The last-visited day lives in its OWN file on purpose. Pre-1.11.0 builds parse everything after
/// the first tab of an AreaFamiliarity.tsv line as the visit count, so a third column there would
/// make an older build (a player rolling back) drop every row and then save the empty result over
/// the file - the entire visit history gone. A sibling file is simply ignored by an older build.
/// </summary>
internal static class AreaFamiliarityService
{
    internal const string VisitsFileName = "AreaFamiliarity.tsv";
    internal const string DaysFileName = "AreaFamiliarityDays.tsv";

    private static ManualLogSource Logger => Plugin.Logger;
    private static readonly Dictionary<string, int> _visits = new(StringComparer.OrdinalIgnoreCase);
    // In-game day of the last visit that earned XP, per location. A location with no entry (every
    // row written before 1.11.0) counts as visited today until the next visit dates it: an upgrade
    // can never decay history it has no date for.
    private static readonly Dictionary<string, int> _lastVisitedDay = new(StringComparer.OrdinalIgnoreCase);
    // Asked of ProgressionStore on every use, never cached: the folder moves when a run starts in
    // another save slot under PerSaveProgression. The names are the fields they replaced.
    private static string _savePath => ProgressionStore.PathFor(VisitsFileName);
    private static string _daysPath => ProgressionStore.PathFor(DaysFileName);
    private static bool _initialized;
    private static bool _dirty;
    private static int _saveCounter;

    private const int SaveEveryN = 8;

    public static void Initialize()
    {
        if (_initialized) return;
        try
        {
            Directory.CreateDirectory(ProgressionStore.ActiveDirectory);
            Load();
            _initialized = true;
        }
        catch (Exception ex)
        {
            Logger?.LogWarning($"[AreaFamiliarity] Initialize failed: {ex.Message}");
            _initialized = true;
        }
    }

    /// <summary>
    /// Forgets what is in memory and reads the files again from wherever ProgressionStore now points.
    /// Called by ProgressionStore after it has saved this service's unsaved changes to the folder it
    /// is leaving, so nothing is lost by the forgetting.
    /// </summary>
    internal static void Reload()
    {
        if (!_initialized)
        {
            Initialize();
            return;
        }
        try
        {
            _dirty = false;
            _saveCounter = 0;
            Load();
        }
        catch (Exception ex)
        {
            Logger?.LogWarning($"[AreaFamiliarity] Reload failed: {ex.Message}");
        }
    }

    public static void RecordVisit(string locationUid)
    {
        if (string.IsNullOrEmpty(locationUid)) return;
        if (!_initialized) Initialize();

        _visits.TryGetValue(locationUid, out var count);
        var newCount = count + 1;
        _visits[locationUid] = newCount;
        // Dated even while decay is off, so switching decay on later has real days to work from.
        _lastVisitedDay[locationUid] = GameQuery.CurrentDay;
        _dirty = true;

        if (++_saveCounter >= SaveEveryN)
        {
            _saveCounter = 0;
            Save();
        }
    }

    public static int GetVisits(string locationUid)
    {
        if (string.IsNullOrEmpty(locationUid)) return 0;
        if (!_initialized) Initialize();
        return _visits.TryGetValue(locationUid, out var count) ? count : 0;
    }

    /// <summary>
    /// Returns the XP multiplier for the given location. Scales from (1 + AreaFamiliarityMinBonus)
    /// at 0 visits up to (1 + AreaFamiliarityMaxBonus) at AreaFamiliarityVisitsForMaxBonus visits,
    /// then plateaus.
    ///
    /// With AreaFamiliarityDecayEnabled the visit count fed to that ramp first loses
    /// AreaFamiliarityDecayPerDay for every day past AreaFamiliarityDecayAfterDays since the last
    /// visit. The decay is computed here, at read time, and is NEVER written back to the stored
    /// count: turning decay off, or visiting again, restores the full standing at once.
    /// </summary>
    public static float GetMultiplier(string locationUid)
    {
        if (!Plugin.AreaFamiliarityEnabled) return 1f;

        float visits = GetVisits(locationUid);
        if (Plugin.AreaFamiliarityDecayEnabled)
        {
            visits = DecayedVisits(visits, DaysSinceLastVisit(locationUid),
                Plugin.AreaFamiliarityDecayAfterDays, Plugin.AreaFamiliarityDecayPerDay);
        }

        return ComputeMultiplier(visits, Plugin.AreaFamiliarityVisitsForMaxBonus,
            Plugin.AreaFamiliarityMinBonus, Plugin.AreaFamiliarityMaxBonus);
    }

    /// <summary>
    /// Whole in-game days since the location's last dated visit, never negative. 0 for an undated
    /// row (see _lastVisitedDay), and 0 when the current day is EARLIER than the stored one, which is
    /// what loading an older save or starting a new character looks like: unless PerSaveProgression
    /// is on, this file is shared by every save, like the visit counts, so a day number from another save proves nothing about
    /// how long this player has been away.
    /// </summary>
    private static int DaysSinceLastVisit(string locationUid)
    {
        if (string.IsNullOrEmpty(locationUid)) return 0;
        if (!_lastVisitedDay.TryGetValue(locationUid, out var lastDay)) return 0;
        int days = GameQuery.CurrentDay - lastDay;
        return days > 0 ? days : 0;
    }

    /// <summary>
    /// The familiarity ramp. With decay off it receives the stored visit count unchanged, and it is
    /// arithmetically the pre-1.11.0 formula, so the default configuration is bit-for-bit the old
    /// behaviour. AreaFamiliarityMinBonus defaults to 0, which collapses it to 1 + Max*ratio. Min is
    /// clamped to Max first: a config where Min exceeds Max would otherwise make the ramp run
    /// backwards (a fresh location worth more than a well-known one).
    /// Pure, with no game types, so SkillSpeedBoost-BonusLogic.Tests.ps1 compiles and runs it: keep
    /// the body C# 5, which is what PowerShell 5.1's Add-Type compiles.
    /// </summary>
    internal static float ComputeMultiplier(float visits, int visitsForMaxBonus, float minBonus, float maxBonus)
    {
        minBonus = Math.Min(minBonus, maxBonus);
        if (visits <= 0f) return 1f + minBonus;

        int visitsForMax = Math.Max(1, visitsForMaxBonus);
        float ratio = Math.Min(1f, visits / visitsForMax);
        return 1f + minBonus + (maxBonus - minBonus) * ratio;
    }

    /// <summary>
    /// Visits that still count after a gap: unchanged within the grace period, then
    /// lostPerDay fewer for each further day, never below 0. Pure; keep the body C# 5.
    /// </summary>
    internal static float DecayedVisits(float rawVisits, int daysSinceLastVisit, int graceDays, float lostPerDay)
    {
        if (daysSinceLastVisit <= graceDays || lostPerDay <= 0f) return rawVisits;
        float left = rawVisits - (daysSinceLastVisit - graceDays) * lostPerDay;
        return left > 0f ? left : 0f;
    }

    /// <summary>
    /// Reads one "key TAB number" line. Anything after a second tab is ignored rather than making
    /// the number unparseable, so a line some later build extends with more columns still loads.
    /// Pure; keep the body C# 5.
    /// </summary>
    internal static bool TryParseKeyedLine(string line, out string key, out int value)
    {
        key = null;
        value = 0;
        if (string.IsNullOrEmpty(line)) return false;
        int tab = line.IndexOf('\t');
        if (tab <= 0 || tab >= line.Length - 1) return false;
        int next = line.IndexOf('\t', tab + 1);
        string number = next < 0 ? line.Substring(tab + 1) : line.Substring(tab + 1, next - tab - 1);
        if (!int.TryParse(number.Trim(), out value)) return false;
        key = line.Substring(0, tab);
        return true;
    }

    public static void Save(bool forceWrite = false)
    {
        if (string.IsNullOrEmpty(_savePath)) return;
        if (!forceWrite && !_dirty) return;
        try
        {
            File.WriteAllText(_savePath, Serialize(_visits));
            if (!string.IsNullOrEmpty(_daysPath) && (_lastVisitedDay.Count > 0 || File.Exists(_daysPath)))
                File.WriteAllText(_daysPath, Serialize(_lastVisitedDay));
            _dirty = false;
        }
        catch (Exception ex)
        {
            Logger?.LogWarning($"[AreaFamiliarity] Save failed: {ex.Message}");
        }
    }

    // Hand-built, one "key TAB value LF" line per entry: the format these files have always had, and
    // the one a pre-1.11.0 build can still read (the BonusLogic test parses this output with a copy
    // of the 1.10.6 loader). Pure; keep the body C# 5.
    internal static string Serialize(Dictionary<string, int> map)
    {
        var sb = new StringBuilder(map.Count * 32);
        foreach (var kv in map)
        {
            if (string.IsNullOrEmpty(kv.Key)) continue;
            if (kv.Key.IndexOf('\t') >= 0 || kv.Key.IndexOf('\n') >= 0) continue;
            sb.Append(kv.Key).Append('\t').Append(kv.Value).Append('\n');
        }
        return sb.ToString();
    }

    private static void Load()
    {
        _visits.Clear();
        _lastVisitedDay.Clear();
        int loaded = LoadInto(_savePath, _visits, positiveOnly: true);
        int dated = LoadInto(_daysPath, _lastVisitedDay, positiveOnly: false);
        if (loaded > 0)
            Logger?.LogDebug($"[AreaFamiliarity] Loaded {loaded} location familiarity entries ({dated} with a last-visited day).");
    }

    private static int LoadInto(string path, Dictionary<string, int> map, bool positiveOnly)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return 0;
        try
        {
            int loaded = 0;
            foreach (var line in File.ReadAllLines(path))
            {
                if (!TryParseKeyedLine(line, out var key, out var value)) continue;
                if (positiveOnly && value <= 0) continue;
                map[key] = value;
                loaded++;
            }
            return loaded;
        }
        catch (Exception ex)
        {
            Logger?.LogWarning($"[AreaFamiliarity] Load of {Path.GetFileName(path)} failed: {ex.Message}");
            return 0;
        }
    }
}
