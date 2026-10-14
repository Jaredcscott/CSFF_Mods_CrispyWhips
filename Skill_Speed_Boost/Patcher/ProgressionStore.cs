using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace Skill_Speed_Boost.Patcher;

/// <summary>
/// Where the progression files live: the one place in the mod that knows the folder.
///
/// AreaFamiliarityService, SkillStreakService and RestedBankService ask <see cref="PathFor"/> for
/// every read and write. With PerSaveProgression off (the default) that is BepInEx/config/SkillSpeedBoost/, the
/// shared folder every 1.11.0 build used, and nothing here ever moves, so the mod reads and
/// writes exactly the files it always did. With it on, each save slot gets a folder of its own,
/// BepInEx/config/SkillSpeedBoost/Slot&lt;N&gt;/, so a second character does not inherit the first
/// one's familiarity, fade days, day streak and rested-XP bank.
///
/// The slot is GameLoad.CurrentGameDataIndex read at every run start, from the
/// GameManager.InitializeStatsAndActions postfix (plan W0.6: the index is final there for a new
/// game and for a load, both of which set it before the game scene loads). A new game is told from
/// a load by GameManager.CurrentSaveData.HasCardsData, false until the run's first save.
///
/// The shared files are never deleted or edited by any of this. The first save LOADED with the
/// option on, into a slot with no folder yet, receives a COPY of them and PerSaveMigrated.txt is
/// written beside them, so no later save gets a second copy. A NEW game never receives one: a new
/// character has no history, and copying the old one into it would hand the first character's
/// progress to the second, the very thing the option exists to stop. A new game started in a slot
/// whose folder still holds an earlier game's files (the game only starts one in an empty slot, so
/// those belong to a deleted save) clears them first while PerSaveResetOnNewGame is on.
///
/// The folder in use is a snapshot taken at each switch, never read live from the setting, so a
/// switch can flush the departing folder's unsaved changes to where they were loaded from before it
/// moves on: BepInEx changes the setting's value before its SettingChanged handler runs.
/// </summary>
internal static class ProgressionStore
{
    private static ManualLogSource Logger => Plugin.Logger;

    internal const string FolderName = "SkillSpeedBoost";
    internal const string MarkerFileName = "PerSaveMigrated.txt";
    internal const int NoSlot = -1;

    /// <summary>
    /// Every file a service keeps in this folder: what the first loaded save receives a copy of and
    /// what a new game clears. A file a later feature stores here joins this list, or it would
    /// neither travel with the first save nor be cleared for a new character. MilestoneService's
    /// SkillMilestones.tsv is the deliberate exception: it never lives in these folders (it has its
    /// own Milestones/Slot&lt;N&gt; folders), never travels, and clears itself on a new game.
    /// </summary>
    internal static readonly string[] ProgressionFiles =
    {
        AreaFamiliarityService.VisitsFileName,
        AreaFamiliarityService.DaysFileName,
        SkillStreakService.FileName,
        RestedBankService.FileName
    };

    private static string _baseDirectory;
    private static string _activeDirectory;
    private static int _slot = NoSlot;

    // Each failure cause warns once, then stays quiet (preflight D17): this runs at every run start.
    private static readonly HashSet<string> _warnedCauses = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>The shared folder, BepInEx/config/SkillSpeedBoost/.</summary>
    internal static string BaseDirectory => _baseDirectory ??= Path.Combine(Paths.ConfigPath, FolderName);

    /// <summary>The folder every <see cref="PathFor"/> resolves against: the shared one until a switch moves it.</summary>
    internal static string ActiveDirectory => _activeDirectory ?? BaseDirectory;

    /// <summary>The save slot read at the last run start, or <see cref="NoSlot"/> before the first one.</summary>
    internal static int CurrentSlot => _slot;

    /// <summary>The path a service reads and writes <paramref name="fileName"/> at.</summary>
    internal static string PathFor(string fileName) => Path.Combine(ActiveDirectory, fileName);

    // ---- Pure members. No game types, C# 5 (no interpolation, no ?., no expression bodies), because
    // ---- Development_Tools/Tests/SkillSpeedBoost-BonusLogic.Tests.ps1 compiles them with Windows
    // ---- PowerShell's Add-Type and runs them against temporary folders.

    /// <summary>
    /// The folder a slot's files live in: Slot&lt;N&gt; under the shared folder when the option is on
    /// and the slot is known, the shared folder itself otherwise.
    /// </summary>
    internal static string SlotDirectory(string baseDir, int slot, bool perSave)
    {
        if (!perSave || slot < 0) return baseDir;
        return Path.Combine(baseDir, "Slot" + slot.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Where <paramref name="fileName"/> lives for that slot and that setting.</summary>
    internal static string SlotPath(string baseDir, int slot, bool perSave, string fileName)
    {
        return Path.Combine(SlotDirectory(baseDir, slot, perSave), fileName);
    }

    /// <summary>
    /// Whether a save being LOADED receives a copy of the shared files: only when no save has had
    /// them yet (no marker), this slot has no folder of its own yet, and there is something to copy.
    /// </summary>
    internal static bool ShouldMigrate(bool markerExists, bool slotDirExists, bool globalFilesExist)
    {
        return !markerExists && !slotDirExists && globalFilesExist;
    }

    /// <summary>
    /// Whether a load writes the marker: the first save loaded into a slot with no folder yet is
    /// "the first save under the option", whether or not there was anything to copy. A slot whose
    /// folder already exists started under the option before (a new game made it), so it leaves the
    /// decision to the next save that has none.
    /// </summary>
    internal static bool ShouldWriteMarker(bool markerExists, bool slotDirExists)
    {
        return !markerExists && !slotDirExists;
    }

    /// <summary>Whether two folder paths name the same folder (case and a trailing separator ignored).</summary>
    internal static bool SameFolder(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        string fullA = Path.GetFullPath(a).TrimEnd('\\', '/');
        string fullB = Path.GetFullPath(b).TrimEnd('\\', '/');
        return string.Equals(fullA, fullB, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether any of the progression files exists in <paramref name="dir"/>.</summary>
    internal static bool AnyExists(string dir, string[] fileNames)
    {
        if (string.IsNullOrEmpty(dir) || fileNames == null) return false;
        foreach (string name in fileNames)
        {
            if (!string.IsNullOrEmpty(name) && File.Exists(Path.Combine(dir, name))) return true;
        }
        return false;
    }

    /// <summary>
    /// Copies each progression file that exists in <paramref name="fromDir"/> into
    /// <paramref name="toDir"/>, never over a file already there. Returns how many it copied. The
    /// source files are only read.
    /// </summary>
    internal static int CopyInto(string fromDir, string toDir, string[] fileNames)
    {
        if (string.IsNullOrEmpty(fromDir) || string.IsNullOrEmpty(toDir) || fileNames == null) return 0;
        Directory.CreateDirectory(toDir);
        int copied = 0;
        foreach (string name in fileNames)
        {
            if (string.IsNullOrEmpty(name)) continue;
            string source = Path.Combine(fromDir, name);
            string target = Path.Combine(toDir, name);
            if (!File.Exists(source) || File.Exists(target)) continue;
            File.Copy(source, target, false);
            copied++;
        }
        return copied;
    }

    /// <summary>
    /// Deletes each progression file in <paramref name="dir"/>, and nothing else: no other file and
    /// not the folder. Refuses outright when <paramref name="dir"/> is the shared folder, whose files
    /// are never deleted. Returns how many it deleted.
    /// </summary>
    internal static int DeleteFrom(string dir, string baseDir, string[] fileNames)
    {
        if (string.IsNullOrEmpty(dir) || fileNames == null) return 0;
        if (SameFolder(dir, baseDir)) return 0;
        int deleted = 0;
        foreach (string name in fileNames)
        {
            if (string.IsNullOrEmpty(name)) continue;
            string path = Path.Combine(dir, name);
            if (!File.Exists(path)) continue;
            File.Delete(path);
            deleted++;
        }
        return deleted;
    }

    /// <summary>
    /// Readies a slot's folder as a run starts in it and says what it did: on a LOAD, the first
    /// save under the option receives a copy of the shared files and the marker is written; on a
    /// NEW game, files an earlier game left in the slot are cleared when
    /// <paramref name="resetOnNewGame"/> is on. Always leaves the folder existing. Never touches the
    /// shared folder's files beyond reading them and adding the marker, and does nothing at all when
    /// handed the shared folder as the slot's.
    /// </summary>
    internal static string PrepareSlotFolder(string baseDir, string slotDir, int slot, bool isNewGame,
        bool resetOnNewGame, string[] fileNames, string markerFileName, string stamp)
    {
        if (string.IsNullOrEmpty(baseDir) || string.IsNullOrEmpty(slotDir)) return "no folder to prepare";
        if (SameFolder(baseDir, slotDir)) return "the shared folder is not a slot's";

        bool slotDirExists = Directory.Exists(slotDir);
        string done;
        if (isNewGame)
        {
            int cleared = (resetOnNewGame && slotDirExists) ? DeleteFrom(slotDir, baseDir, fileNames) : 0;
            done = cleared > 0
                ? "new game: cleared " + cleared + " file(s) an earlier game left in this slot"
                : "new game";
        }
        else
        {
            string markerPath = Path.Combine(baseDir, markerFileName);
            bool markerExists = File.Exists(markerPath);
            bool globalFilesExist = AnyExists(baseDir, fileNames);
            int copied = ShouldMigrate(markerExists, slotDirExists, globalFilesExist)
                ? CopyInto(baseDir, slotDir, fileNames)
                : 0;
            done = copied > 0
                ? "load: the first save under PerSaveProgression, copied " + copied + " shared file(s) into this slot"
                : "load";
            if (ShouldWriteMarker(markerExists, slotDirExists))
            {
                File.WriteAllText(markerPath, MarkerText(slot, copied, stamp));
                done = done + "; wrote " + markerFileName;
            }
        }

        Directory.CreateDirectory(slotDir);
        return done;
    }

    /// <summary>The marker's text, hand-built: it is read by people, never parsed.</summary>
    internal static string MarkerText(int slot, int copied, string stamp)
    {
        string slotName = "Slot" + slot.ToString(CultureInfo.InvariantCulture);
        string what = copied > 0
            ? "copied " + copied + " of the shared progression files in this folder into " + slotName
            : "found no shared progression files to copy into " + slotName;
        return "Skill Speed Boost " + what + " on " + stamp + ", the first save loaded with PerSaveProgression on.\n"
            + "No other save receives a copy while this file exists. Delete it to give the next save loaded without a Slot folder of its own a copy instead.\n"
            + "The shared files beside this one are never changed or deleted by PerSaveProgression; with the option off the mod uses them again.\n";
    }

    // ---- Game-facing members.

    /// <summary>
    /// Called from GameLoadPatch.InitializeStatsAndActions_Postfix at every run start. Reads the save
    /// slot and whether this is a new game, then moves the services to that slot's folder, and hands
    /// both to MilestoneService, whose marks are slot-keyed whatever PerSaveProgression says (plan
    /// D4): Switch's early return with the option off would never reach it. Never throws: it runs
    /// inside GameManager.Awake.
    /// </summary>
    internal static void OnRunStart(object gameManager)
    {
        try
        {
            bool perSave = Plugin.PerSaveProgression;
            // The milestone marks need the slot and the new-game verdict with the option off too, so a
            // failed read warns whenever either feature depends on it.
            bool needsSlot = perSave || Plugin.MilestoneBonusEnabled;
            int slot = ReadSlot(needsSlot);
            bool isNewGame = ReadIsNewGame(gameManager, needsSlot);
            MilestoneService.OnRunStart(slot, isNewGame);
            Switch(slot, isNewGame, isNewGame ? "new game" : "load");
        }
        catch (Exception ex)
        {
            WarnOnce("runstart-throw", $"[PerSave] could not set up the progression folder at run start, so this run keeps the folder it had: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Moves the services to <paramref name="slot"/>'s folder as a LOAD of that slot would: flushes the
    /// departing folder's unsaved changes, readies the new one, and reloads every service from it.
    /// </summary>
    internal static void SwitchSlot(int slot)
    {
        Switch(slot, false, "slot switch");
    }

    /// <summary>
    /// The PerSaveProgression SettingChanged handler. While a run is being played it moves the
    /// services at once (turning the option on counts as loading the save you are in); at the main
    /// menu it does nothing, and the next run start puts them where they belong. Never throws: it runs
    /// inside BepInEx's config events.
    /// </summary>
    internal static void Refresh(string reason)
    {
        try
        {
            if (!IsAlive(CardUtil.GetGameManagerInstance())) return;
            Switch(_slot, false, reason);
        }
        catch (Exception ex)
        {
            WarnOnce("refresh-throw", $"[PerSave] could not move the progression folder ({reason}): {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void Switch(int slot, bool isNewGame, string reason)
    {
        _slot = slot;
        bool perSave = Plugin.PerSaveProgression;
        string target = SlotDirectory(BaseDirectory, slot, perSave);
        bool moving = !SameFolder(target, ActiveDirectory);
        bool slotKnown = perSave && slot >= 0;

        // With the option off the target is always the shared folder the services already use, so
        // this returns before writing, reading or logging anything: 1.11.0's behaviour exactly.
        if (!moving && !(slotKnown && isNewGame)) return;

        // The departing folder's unsaved changes go where they were loaded from, before anything moves.
        AreaFamiliarityService.Save();
        SkillStreakService.Save();
        RestedBankService.Save();

        if (slotKnown)
        {
            try
            {
                string done = PrepareSlotFolder(BaseDirectory, target, slot, isNewGame, Plugin.PerSaveResetOnNewGame,
                    ProgressionFiles, MarkerFileName, DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
                if (done.IndexOf("copied", StringComparison.Ordinal) >= 0 || done.IndexOf("cleared", StringComparison.Ordinal) >= 0)
                    Logger?.LogInfo($"[PerSave] Slot{slot}: {done}.");
                else
                    Logger?.LogDebug($"[PerSave] Slot{slot}: {done}.");
            }
            catch (Exception ex)
            {
                // A slot folder that cannot be readied is not used: the services stay where they are.
                WarnOnce("prepare:" + ex.GetType().Name, $"[PerSave] could not ready the Slot{slot} folder, so the progression files stay in {ActiveDirectory}: {ex.GetType().Name}: {ex.Message}");
                return;
            }
        }

        _activeDirectory = target;
        AreaFamiliarityService.Reload();
        SkillStreakService.Reload();
        RestedBankService.Reload();
        Logger?.LogDebug($"[PerSave] {reason}: progression files now in {ActiveDirectory} (slot {slot}).");
    }

    /// <summary>GameLoad.CurrentGameDataIndex, or <see cref="NoSlot"/> when it cannot be read.</summary>
    private static int ReadSlot(bool warn)
    {
        var gameLoadType = Reflect.TryGetType("GameLoad");
        if (gameLoadType == null) return Unread(warn, "gameload-type", "the GameLoad type was not found");
        var instanceField = gameLoadType.GetField("Instance", Reflect.AllStaticFlags);
        if (instanceField == null) return Unread(warn, "gameload-instance-field", "GameLoad has no static Instance field");
        var gameLoad = instanceField.GetValue(null);
        if (!IsAlive(gameLoad)) return Unread(warn, "gameload-instance", "GameLoad.Instance is null");
        if (!Reflect.TryGetMember(gameLoad, "CurrentGameDataIndex", out var index) || !(index is int slot))
            return Unread(warn, "gameload-index", "GameLoad.CurrentGameDataIndex could not be read as a number");
        return slot >= 0 ? slot : NoSlot;
    }

    private static int Unread(bool warn, string cause, string what)
    {
        if (warn)
            WarnOnce("slot:" + cause, $"[PerSave] {what}, so the save slot is unknown and the progression files stay in the shared folder.");
        return NoSlot;
    }

    /// <summary>
    /// True for a new game: the run's save data holds no cards until its first save. A value that
    /// cannot be read counts as a LOAD, which never clears anything.
    /// </summary>
    private static bool ReadIsNewGame(object gameManager, bool warn)
    {
        if (!IsAlive(gameManager))
        {
            if (warn) WarnOnce("newgame-gm", "[PerSave] no GameManager at run start, so this run is treated as a load (nothing is cleared).");
            return false;
        }
        if (!Reflect.TryGetMember(gameManager, "CurrentSaveData", out var saveData) || saveData == null)
        {
            if (warn) WarnOnce("newgame-savedata", "[PerSave] GameManager.CurrentSaveData could not be read, so this run is treated as a load (nothing is cleared).");
            return false;
        }
        if (!Reflect.TryGetMember(saveData, "HasCardsData", out var hasCards) || !(hasCards is bool has))
        {
            if (warn) WarnOnce("newgame-hascards", "[PerSave] CurrentSaveData.HasCardsData could not be read, so this run is treated as a load (nothing is cleared).");
            return false;
        }
        return !has;
    }

    private static void WarnOnce(string cause, string message)
    {
        if (_warnedCauses.Add(cause)) Logger?.LogWarning(message);
    }

    // A destroyed UnityEngine.Object is not C#-null under an object-typed test (root CLAUDE.md
    // "Harmony Patching Pitfalls"), which Reflect.IsAlive handles.
    private static bool IsAlive(object instance) => Reflect.IsAlive(instance);
}
