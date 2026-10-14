using System;
using System.IO;
using System.Text;
using BepInEx.Logging;

namespace Skill_Speed_Boost.Patcher;

/// <summary>
/// Consecutive in-game days on which the player gained ANY skill XP: one streak, not one per skill.
/// Persisted to SkillStreak.tsv beside AreaFamiliarity.tsv, the same way and with the same scope:
/// by default BepInEx/config/SkillSpeedBoost/, shared by every save and character, and with
/// PerSaveProgression on that save slot's own Slot&lt;N&gt; folder (ProgressionStore decides). No
/// save file is touched either way. The shared scope is why a streak resets when the player loads
/// an older save or starts a new character: the day counter goes backwards (or jumps), which reads
/// exactly like a skipped day.
///
/// Written only when the streak changes, which is at most once per in-game day, and again on quit.
/// </summary>
internal static class SkillStreakService
{
    internal const string FileName = "SkillStreak.tsv";

    private static ManualLogSource Logger => Plugin.Logger;

    // Sentinel for "no active day recorded yet". NextStreak treats it as a gap.
    private const int NoDay = int.MinValue;

    private static int _lastActiveDay = NoDay;
    private static int _streakDays;
    // Asked of ProgressionStore on every use, never cached: the folder moves when a run starts in
    // another save slot under PerSaveProgression. The name is the field it replaced.
    private static string _savePath => ProgressionStore.PathFor(FileName);
    private static bool _initialized;
    private static bool _dirty;

    public static int CurrentStreakDays => _streakDays;

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
            Logger?.LogWarning($"[SkillStreak] Initialize failed: {ex.Message}");
            _initialized = true;
        }
    }

    /// <summary>
    /// Forgets the streak in memory and reads it again from wherever ProgressionStore now points.
    /// Called by ProgressionStore after it has saved any unsaved change to the folder it is leaving.
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
            Load();
        }
        catch (Exception ex)
        {
            Logger?.LogWarning($"[SkillStreak] Reload failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Records that skill XP was gained on <paramref name="currentDay"/> and returns the streak
    /// length including today (1 on the first day of a streak).
    /// </summary>
    public static int NoteActivity(int currentDay)
    {
        if (!_initialized) Initialize();

        int next = NextStreak(_lastActiveDay, _streakDays, currentDay);
        if (next != _streakDays || currentDay != _lastActiveDay)
        {
            _streakDays = next;
            _lastActiveDay = currentDay;
            _dirty = true;
            Save();
        }
        return _streakDays;
    }

    /// <summary>
    /// The streak after gaining XP on <paramref name="currentDay"/>: unchanged on the same day, one
    /// longer on the day after <paramref name="lastActiveDay"/>, and back to 1 after a gap, when the
    /// day went BACKWARDS (an earlier save was loaded), or when no day was recorded yet
    /// (lastActiveDay == int.MinValue). A stored streak below 1 is read as 1, so a damaged file can
    /// never leave today's activity uncounted.
    /// Pure, with no game types, so SkillSpeedBoost-BonusLogic.Tests.ps1 compiles and runs it: keep
    /// the body C# 5, which is what PowerShell 5.1's Add-Type compiles.
    /// </summary>
    internal static int NextStreak(int lastActiveDay, int streakDays, int currentDay)
    {
        int held = streakDays < 1 ? 1 : streakDays;
        if (lastActiveDay == int.MinValue) return 1;
        if (currentDay == lastActiveDay) return held;
        if (currentDay == lastActiveDay + 1) return held + 1;
        return 1;
    }

    /// <summary>
    /// Extra XP for a streak of <paramref name="streakDays"/>: perDay for every day, counting at most
    /// maxDays of them. 0.02 per day capped at 10 days is +2% on day 1 and +20% from day 10 on.
    /// Pure; keep the body C# 5.
    /// </summary>
    internal static float StreakBonus(int streakDays, int maxDays, float perDay)
    {
        int days = Math.Min(streakDays, maxDays);
        if (days <= 0 || perDay <= 0f) return 0f;
        return days * perDay;
    }

    public static void Save(bool forceWrite = false)
    {
        if (string.IsNullOrEmpty(_savePath)) return;
        if (!forceWrite && !_dirty) return;
        // Nothing recorded yet: do not create a file for a feature the player never used.
        if (_lastActiveDay == NoDay && !File.Exists(_savePath)) return;
        try
        {
            var sb = new StringBuilder(64);
            sb.Append("LastActiveDay").Append('\t').Append(_lastActiveDay).Append('\n');
            sb.Append("CurrentStreakDays").Append('\t').Append(_streakDays).Append('\n');
            File.WriteAllText(_savePath, sb.ToString());
            _dirty = false;
        }
        catch (Exception ex)
        {
            Logger?.LogWarning($"[SkillStreak] Save failed: {ex.Message}");
        }
    }

    private static void Load()
    {
        _lastActiveDay = NoDay;
        _streakDays = 0;
        if (string.IsNullOrEmpty(_savePath) || !File.Exists(_savePath)) return;
        try
        {
            foreach (var line in File.ReadAllLines(_savePath))
            {
                if (!AreaFamiliarityService.TryParseKeyedLine(line, out var key, out var value)) continue;
                if (string.Equals(key, "LastActiveDay", StringComparison.OrdinalIgnoreCase)) _lastActiveDay = value;
                else if (string.Equals(key, "CurrentStreakDays", StringComparison.OrdinalIgnoreCase)) _streakDays = value;
            }
            Logger?.LogDebug($"[SkillStreak] Loaded a {_streakDays}-day streak last active on day {_lastActiveDay}.");
        }
        catch (Exception ex)
        {
            Logger?.LogWarning($"[SkillStreak] Load failed: {ex.Message}");
        }
    }
}
