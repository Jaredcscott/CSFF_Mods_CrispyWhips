using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Logging;

namespace Skill_Speed_Boost.Patcher;

/// <summary>
/// Tracks per-location forage/work counts and computes a familiarity XP multiplier.
/// State persists to a flat TSV file under BepInEx/config/SkillSpeedBoost/.
/// </summary>
internal static class AreaFamiliarityService
{
    private static ManualLogSource Logger => Plugin.Logger;
    private static readonly Dictionary<string, int> _visits = new(StringComparer.OrdinalIgnoreCase);
    private static string _savePath;
    private static bool _initialized;
    private static bool _dirty;
    private static int _saveCounter;

    private const int SaveEveryN = 8;

    public static void Initialize()
    {
        if (_initialized) return;
        try
        {
            var dir = Path.Combine(Paths.ConfigPath, "SkillSpeedBoost");
            Directory.CreateDirectory(dir);
            _savePath = Path.Combine(dir, "AreaFamiliarity.tsv");
            Load();
            _initialized = true;
        }
        catch (Exception ex)
        {
            Logger?.LogWarning($"[AreaFamiliarity] Initialize failed: {ex.Message}");
            _initialized = true;
        }
    }

    public static void RecordVisit(string locationUid)
    {
        if (string.IsNullOrEmpty(locationUid)) return;
        if (!_initialized) Initialize();

        _visits.TryGetValue(locationUid, out var count);
        var newCount = count + 1;
        _visits[locationUid] = newCount;
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
    /// Returns the XP multiplier for the given location, ≥1.0. Linearly scales from 1.0 at 0 visits
    /// up to (1 + AreaFamiliarityMaxBonus) at AreaFamiliarityVisitsForMaxBonus visits, then plateaus.
    /// </summary>
    public static float GetMultiplier(string locationUid)
    {
        if (!Plugin.AreaFamiliarityEnabled) return 1f;
        var visits = GetVisits(locationUid);
        if (visits <= 0) return 1f;

        float maxBonus = Plugin.AreaFamiliarityMaxBonus;
        int visitsForMax = Math.Max(1, Plugin.AreaFamiliarityVisitsForMaxBonus);
        float ratio = Math.Min(1f, (float)visits / visitsForMax);
        return 1f + maxBonus * ratio;
    }

    public static void Save(bool forceWrite = false)
    {
        if (string.IsNullOrEmpty(_savePath)) return;
        if (!forceWrite && !_dirty) return;
        try
        {
            var sb = new StringBuilder(_visits.Count * 32);
            foreach (var kv in _visits)
            {
                if (string.IsNullOrEmpty(kv.Key)) continue;
                if (kv.Key.IndexOf('\t') >= 0 || kv.Key.IndexOf('\n') >= 0) continue;
                sb.Append(kv.Key).Append('\t').Append(kv.Value).Append('\n');
            }
            File.WriteAllText(_savePath, sb.ToString());
            _dirty = false;
        }
        catch (Exception ex)
        {
            Logger?.LogWarning($"[AreaFamiliarity] Save failed: {ex.Message}");
        }
    }

    private static void Load()
    {
        _visits.Clear();
        if (string.IsNullOrEmpty(_savePath) || !File.Exists(_savePath)) return;
        try
        {
            var lines = File.ReadAllLines(_savePath);
            int loaded = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrEmpty(line)) continue;
                int tab = line.IndexOf('\t');
                if (tab <= 0 || tab >= line.Length - 1) continue;
                var key = line.Substring(0, tab);
                var valStr = line.Substring(tab + 1);
                if (int.TryParse(valStr, out var val) && val > 0)
                {
                    _visits[key] = val;
                    loaded++;
                }
            }
            if (loaded > 0)
                Logger?.LogDebug($"[AreaFamiliarity] Loaded {loaded} location familiarity entries.");
        }
        catch (Exception ex)
        {
            Logger?.LogWarning($"[AreaFamiliarity] Load failed: {ex.Message}");
        }
    }
}
