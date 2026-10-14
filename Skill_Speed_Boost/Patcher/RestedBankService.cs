using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx.Logging;

namespace Skill_Speed_Boost.Patcher;

/// <summary>
/// The rested-XP bank (plan D3): a skill left alone for whole in-game days banks gains that are
/// multiplied by RestedBankMultiplier. Each skill keeps (lastDay, banked): a gain first accrues
/// RestedBankGainsPerDay for every full day since that skill's last gain, up to RestedBankMaxGains,
/// then spends one banked gain if there is one. It rewards a break, the opposite of the day streak.
///
/// Persisted to SkillRested.tsv beside SkillStreak.tsv, the same way and with the same scope: by
/// default BepInEx/config/SkillSpeedBoost/, shared by every save, and with PerSaveProgression on
/// that save slot's own Slot&lt;N&gt; folder (ProgressionStore decides, and copies and clears this
/// file with the others). Two keyed integer lines per skill, "Skill|banked TAB n" and
/// "Skill|day TAB n", so AreaFamiliarityService.TryParseKeyedLine reads them unchanged. Hand-built,
/// never round-tripped through a serializer (plan S5). Written whenever a gain changes it, and on quit.
///
/// The three magnitudes are read here, not in the composition point, so ChangeStat_Post reads only
/// the RestedBankEnabled flag (plan S1).
/// </summary>
internal static class RestedBankService
{
    internal const string FileName = "SkillRested.tsv";

    private static ManualLogSource Logger => Plugin.Logger;

    // Sentinel for "this skill has no gain recorded yet". AccrueBank banks nothing from it.
    private const int NoDay = int.MinValue;

    private static readonly Dictionary<string, int> _banked = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, int> _lastDay = new(StringComparer.OrdinalIgnoreCase);
    // Asked of ProgressionStore on every use, never cached: the folder moves when a run starts in
    // another save slot under PerSaveProgression.
    private static string _savePath => ProgressionStore.PathFor(FileName);
    private static bool _initialized;
    private static bool _dirty;

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
            Logger?.LogWarning($"[RestedBank] Initialize failed: {ex.Message}");
            _initialized = true;
        }
    }

    /// <summary>
    /// Forgets the bank in memory and reads it again from wherever ProgressionStore now points.
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
            Logger?.LogWarning($"[RestedBank] Reload failed: {ex.Message}");
        }
    }

    /// <summary>
    /// A gain of <paramref name="skillKey"/> on in-game day <paramref name="currentDay"/>: accrues the
    /// days since that skill's last gain, records today as its last gain, then spends one banked gain
    /// and returns RestedBankMultiplier, or returns 1 when nothing is banked.
    /// </summary>
    public static float Consume(string skillKey, int currentDay)
    {
        if (string.IsNullOrEmpty(skillKey)) return 1f;
        if (!_initialized) Initialize();

        int lastDay = _lastDay.TryGetValue(skillKey, out int storedDay) ? storedDay : NoDay;
        int banked = _banked.TryGetValue(skillKey, out int storedBank) ? storedBank : 0;

        int accrued = AccrueBank(lastDay, banked, currentDay, Plugin.RestedBankGainsPerDay, Plugin.RestedBankMaxGains);
        bool spent = SpendOne(accrued, out int remaining);

        if (remaining != banked || currentDay != lastDay || !_banked.ContainsKey(skillKey))
        {
            _banked[skillKey] = remaining;
            _lastDay[skillKey] = currentDay;
            _dirty = true;
            Save();
        }
        return spent ? Plugin.RestedBankMultiplier : 1f;
    }

    /// <summary>
    /// The bank after a gain on <paramref name="currentDay"/>, before it spends: perDay more for each
    /// full day since <paramref name="lastDay"/>, never above maxBanked. A skill with no gain recorded
    /// yet (lastDay == int.MinValue) banks nothing, so a first use is never doubled. The same day, or a
    /// day going BACKWARDS (an earlier save was loaded), accrues nothing. The result is always kept in
    /// 0..maxBanked, so lowering the cap in the config takes effect at the next gain.
    /// Pure, with no game types, so SkillSpeedBoost-BonusLogic.Tests.ps1 compiles and runs it: keep
    /// the body C# 5, which is what PowerShell 5.1's Add-Type compiles.
    /// </summary>
    internal static int AccrueBank(int lastDay, int banked, int currentDay, int perDay, int maxBanked)
    {
        int max = maxBanked < 0 ? 0 : maxBanked;
        long total = banked < 0 ? 0 : banked;
        if (lastDay != int.MinValue && currentDay > lastDay && perDay > 0)
            total += (long)perDay * ((long)currentDay - lastDay);
        if (total > max) total = max;
        return (int)total;
    }

    /// <summary>
    /// Spends one banked gain: true, with one fewer left, when any is banked; false, with the bank
    /// unchanged (never below 0), when none is. Pure; keep the body C# 5.
    /// </summary>
    internal static bool SpendOne(int banked, out int remaining)
    {
        if (banked > 0)
        {
            remaining = banked - 1;
            return true;
        }
        remaining = banked < 0 ? 0 : banked;
        return false;
    }

    /// <summary>
    /// "Skill|banked TAB n" and "Skill|day TAB n" for every skill with a recorded day, in ordinal name
    /// order, one LF-terminated line each. A key holding a tab, a newline or a '|' is skipped: it could
    /// not be read back. Pure; keep the body C# 5.
    /// </summary>
    internal static string Serialize(Dictionary<string, int> banked, Dictionary<string, int> lastDays)
    {
        var sb = new StringBuilder();
        if (lastDays == null) return string.Empty;
        var names = new List<string>(lastDays.Keys);
        names.Sort(StringComparer.Ordinal);
        foreach (string name in names)
        {
            if (string.IsNullOrEmpty(name)) continue;
            if (name.IndexOf('\t') >= 0 || name.IndexOf('\n') >= 0 || name.IndexOf('|') >= 0) continue;
            int bank;
            if (banked == null || !banked.TryGetValue(name, out bank)) bank = 0;
            sb.Append(name).Append("|banked").Append('\t').Append(bank).Append('\n');
            sb.Append(name).Append("|day").Append('\t').Append(lastDays[name]).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Splits a keyed line's key into the skill and which of its two values it holds: true with
    /// isDay false for "Skill|banked", true with isDay true for "Skill|day", false for anything else.
    /// Split at the LAST '|'. Pure; keep the body C# 5.
    /// </summary>
    internal static bool TryParseBankKey(string key, out string skill, out bool isDay)
    {
        skill = null;
        isDay = false;
        if (string.IsNullOrEmpty(key)) return false;
        int bar = key.LastIndexOf('|');
        if (bar <= 0) return false;
        string kind = key.Substring(bar);
        if (string.Equals(kind, "|day", StringComparison.OrdinalIgnoreCase)) isDay = true;
        else if (!string.Equals(kind, "|banked", StringComparison.OrdinalIgnoreCase)) return false;
        skill = key.Substring(0, bar);
        return true;
    }

    public static void Save(bool forceWrite = false)
    {
        if (string.IsNullOrEmpty(_savePath)) return;
        if (!forceWrite && !_dirty) return;
        // Nothing recorded yet: do not create a file for a feature the player never used.
        if (_lastDay.Count == 0 && !File.Exists(_savePath)) return;
        try
        {
            File.WriteAllText(_savePath, Serialize(_banked, _lastDay));
            _dirty = false;
        }
        catch (Exception ex)
        {
            Logger?.LogWarning($"[RestedBank] Save failed: {ex.Message}");
        }
    }

    private static void Load()
    {
        _banked.Clear();
        _lastDay.Clear();
        if (string.IsNullOrEmpty(_savePath) || !File.Exists(_savePath)) return;
        try
        {
            foreach (var line in File.ReadAllLines(_savePath))
            {
                if (!AreaFamiliarityService.TryParseKeyedLine(line, out var key, out var value)) continue;
                if (!TryParseBankKey(key, out var skill, out var isDay)) continue;
                if (isDay) _lastDay[skill] = value;
                else _banked[skill] = value;
            }
            Logger?.LogDebug($"[RestedBank] Loaded the bank of {_lastDay.Count} skill(s).");
        }
        catch (Exception ex)
        {
            Logger?.LogWarning($"[RestedBank] Load failed: {ex.Message}");
        }
    }
}
