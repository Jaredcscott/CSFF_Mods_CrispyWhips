using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;

namespace Skill_Speed_Boost.Patcher;

/// <summary>
/// The milestone burst (plan D4): the first time a skill's trained base reaches each breakpoint
/// (every MilestoneEvery points: 25, 50, 75, ... at the default) that gain gets MilestoneBonusXp
/// extra skill points, once. The burst is added AFTER the composed write, so it is not scaled by any
/// multiplier and MaxComposedMultiplier does not cap it; a skill set to 0x never reaches it, because
/// ChangeStat_Post's 0x early-out returns first.
///
/// "Once" is remembered per CHARACTER, so a reload cannot pay a breakpoint twice: the highest
/// breakpoint paid for each skill is kept in SkillMilestones.tsv, one "Skill TAB index" line per
/// skill (plan S5; read and written with AreaFamiliarityService's keyed-line reader and writer). The
/// file is ALWAYS keyed by save slot, whatever PerSaveProgression says, because a shared mark would
/// starve every later character of the bursts the first one took. It lives in
/// BepInEx/config/SkillSpeedBoost/Milestones/Slot&lt;N&gt;/, built with ProgressionStore.SlotPath with
/// perSave true, and deliberately NOT in PerSaveProgression's own Slot&lt;N&gt; folders: that option
/// reads a slot folder's existence as "this slot already started under the option" and would refuse
/// the first loaded save its copy of the shared history if a milestone mark had created the folder
/// while the option was off. For the same reason it is not on ProgressionStore.ProgressionFiles: it
/// never travels with the first loaded save (marks belong to one character) and its clearing is its
/// own, below.
///
/// What clears a slot's marks: a NEW game started in that slot, every time, whether PerSaveProgression
/// is on or off and whatever PerSaveResetOnNewGame says. The game only starts a new game in an empty
/// slot, so marks found there belong to a deleted save's character; a mark exists only to stop a
/// reload of THAT character re-paying a breakpoint, which a new character cannot do. A load never
/// clears anything. A slot that cannot be read keeps its marks in Milestones/ itself (shared, with
/// one warning), the same fallback ProgressionStore uses.
///
/// Saved on every award, and on quit. MilestoneEvery and MilestoneBonusXp are read here, not in the
/// composition point, so ChangeStat_Post reads only the MilestoneBonusEnabled flag (plan S1).
/// </summary>
internal static class MilestoneService
{
    internal const string FileName = "SkillMilestones.tsv";
    internal const string FolderName = "Milestones";

    private static ManualLogSource Logger => Plugin.Logger;

    // Highest breakpoint index paid, per skill, for the slot whose file is loaded.
    private static readonly Dictionary<string, int> _marks = new(StringComparer.OrdinalIgnoreCase);
    private static string _loadedPath;
    private static bool _dirty;
    private static bool _loadFailed;
    private static int _slot = ProgressionStore.NoSlot;
    private static bool _slotUnknownWarned;
    private static readonly HashSet<string> _warnedCauses = new(StringComparer.Ordinal);

    // ---- Pure members. No game types, C# 5 (no interpolation, no ?., no expression bodies), because
    // ---- Development_Tools/Tests/SkillSpeedBoost-BonusLogic.Tests.ps1 compiles and runs them.

    /// <summary>
    /// The breakpoint index a gain from <paramref name="before"/> to <paramref name="after"/> newly
    /// reaches, or -1. Breakpoint k sits at k * every (k from 1). A gain reaches k when the base was
    /// below k * every and is now at or above it; landing exactly on a breakpoint reaches it, starting
    /// on one does not. When one gain passes two breakpoints only the NEWEST counts, so there is one
    /// burst per gain. A breakpoint at or below <paramref name="lastAwardedIndex"/> (the mark) was
    /// paid before, typically before a reload took the base back down, and is refused.
    /// </summary>
    internal static int Crossed(float before, float after, float every, int lastAwardedIndex)
    {
        if (every <= 0f || !(after > before)) return -1;
        // A hair of tolerance, so a base a float step short of a breakpoint (24.99999) still reaches it.
        double reachedRaw = Math.Floor(((double)after + 0.0001) / every);
        double hadRaw = Math.Floor(((double)before + 0.0001) / every);
        if (reachedRaw > int.MaxValue) reachedRaw = int.MaxValue;
        if (hadRaw > int.MaxValue) hadRaw = int.MaxValue;
        int reached = (int)reachedRaw;
        int had = (int)hadRaw;
        if (reached < 1 || reached <= had) return -1;
        if (reached <= lastAwardedIndex) return -1;
        return reached;
    }

    /// <summary>
    /// Where a slot's marks live: Milestones/Slot&lt;N&gt;/SkillMilestones.tsv under the shared folder,
    /// through ProgressionStore.SlotPath with perSave true whatever PerSaveProgression says, or
    /// Milestones/SkillMilestones.tsv when the slot is unknown.
    /// </summary>
    internal static string MarksPath(string baseDir, int slot)
    {
        return ProgressionStore.SlotPath(Path.Combine(baseDir, FolderName), slot, true, FileName);
    }

    // ---- Game-facing members.

    /// <summary>
    /// Called by ProgressionStore.OnRunStart at every run start with the slot it read and whether
    /// this is a new game. A new game in a known slot deletes that slot's marks file (only that one
    /// file); then the marks are read again from the slot's file at the next gain. Never throws.
    /// </summary>
    internal static void OnRunStart(int slot, bool isNewGame)
    {
        try
        {
            Save();
            _slot = slot;
            _loadedPath = null;
            _marks.Clear();
            _dirty = false;

            if (slot < 0)
            {
                if (Plugin.MilestoneBonusEnabled && !_slotUnknownWarned)
                {
                    _slotUnknownWarned = true;
                    Logger?.LogWarning("[Milestone] the save slot is unknown, so this run's milestone marks are kept in the shared Milestones folder: a breakpoint one character reaches is then not paid to another.");
                }
                return;
            }

            if (!isNewGame) return;
            string path = MarksPath(ProgressionStore.BaseDirectory, slot);
            if (!File.Exists(path)) return;
            File.Delete(path);
            Logger?.LogInfo($"[Milestone] Slot{slot}: new game: cleared the milestone marks an earlier game left in this slot.");
        }
        catch (Exception ex)
        {
            WarnOnce("runstart:" + ex.GetType().Name, $"[Milestone] could not ready the milestone marks at run start: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// A gain of <paramref name="skillKey"/> that moved its base from <paramref name="before"/> to
    /// <paramref name="after"/>: true, with the breakpoint index and the burst, when it newly reaches a
    /// breakpoint, with <paramref name="at"/> the skill value it sits at; the mark is recorded and saved
    /// at once. False otherwise.
    /// </summary>
    internal static bool TryAward(string skillKey, float before, float after, out int index, out int at, out float bonus)
    {
        index = -1;
        at = 0;
        bonus = 0f;
        if (string.IsNullOrEmpty(skillKey)) return false;
        try
        {
            EnsureLoaded();
            int every = Plugin.MilestoneEvery;
            int last = _marks.TryGetValue(skillKey, out int stored) ? stored : 0;
            int reached = Crossed(before, after, every, last);
            if (reached < 0) return false;

            _marks[skillKey] = reached;
            _dirty = true;
            Save();
            index = reached;
            at = reached * every;
            bonus = Plugin.MilestoneBonusXp;
            return true;
        }
        catch (Exception ex)
        {
            WarnOnce("award:" + ex.GetType().Name, $"[Milestone] could not check a milestone, so none is paid: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public static void Save(bool forceWrite = false)
    {
        if (string.IsNullOrEmpty(_loadedPath) || _loadFailed) return;
        if (!forceWrite && !_dirty) return;
        // Nothing recorded yet: do not create a file for a feature the player never used.
        if (_marks.Count == 0 && !File.Exists(_loadedPath)) return;
        try
        {
            string dir = Path.GetDirectoryName(_loadedPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_loadedPath, AreaFamiliarityService.Serialize(_marks));
            _dirty = false;
        }
        catch (Exception ex)
        {
            WarnOnce("save:" + ex.GetType().Name, $"[Milestone] Save failed: {ex.Message}");
        }
    }

    // Reads the current slot's marks the first time they are needed after a run start.
    private static void EnsureLoaded()
    {
        string path = MarksPath(ProgressionStore.BaseDirectory, _slot);
        if (string.Equals(path, _loadedPath, StringComparison.OrdinalIgnoreCase)) return;
        _marks.Clear();
        _dirty = false;
        _loadFailed = false;
        _loadedPath = path;
        if (!File.Exists(path)) return;
        try
        {
            foreach (var line in File.ReadAllLines(path))
            {
                if (!AreaFamiliarityService.TryParseKeyedLine(line, out var skill, out var markIndex)) continue;
                _marks[skill] = markIndex;
            }
            Logger?.LogDebug($"[Milestone] Loaded the marks of {_marks.Count} skill(s) from {path}.");
        }
        catch (Exception ex)
        {
            // Never write over a file that could not be read: its marks would be lost for good.
            _loadFailed = true;
            WarnOnce("load:" + ex.GetType().Name, $"[Milestone] Load failed, so breakpoints can pay again this run and the marks file is left as it is: {ex.Message}");
        }
    }

    private static void WarnOnce(string cause, string message)
    {
        if (_warnedCauses.Add(cause)) Logger?.LogWarning(message);
    }
}
