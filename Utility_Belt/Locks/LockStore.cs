using System.Globalization;
using System.Text;

namespace Utility_Belt.Locks;

/// <summary>
/// The item-lock store (plan B1 step 1): one flat TSV per save slot under BepInEx/config/UtilityBelt/,
/// never inside the game's save. A lock is keyed by (CardID, CreatedOnTick): CardSaveData.SaveCard keeps no
/// per-instance id, CardID is the card's MODEL UniqueID, and CreatedInSaveDataTick is rewritten by
/// MoveCard, RemoveCardFromInventoryInBG and the container save, so it is not part of the key (plan 2.2 R4).
/// Cards of one drop share a tick and so lock together; a transform makes a new card, so a lock does not
/// follow it.
/// </summary>
/// <remarks>
/// File shape, hand-built lines with a version column from day one:
///   # comment lines (rewritten on every save)
///   1 TAB CardID TAB CreatedOnTick
/// A line whose version is not "1" belongs to a newer build. It is kept verbatim and written back
/// unchanged, so a player rolling back to this build never loses rows a newer build added (the lesson of
/// SSB's AreaFamiliarity file, where an older build dropped every row it could not parse and then saved
/// the empty result). Tick 0 is a real tick (a new game starts there), so presence is decided by the set,
/// never by a zero value.
/// </remarks>
internal static class LockStore
{
    internal const string CurrentVersion = "1";
    internal const int NoSlot = -1;

    private static readonly HashSet<string> _keys = new(StringComparer.Ordinal);
    private static readonly List<string> _foreign = new();
    private static readonly HashSet<string> _warned = new();
    private static int _slot = NoSlot;

    internal static int Slot => _slot;
    internal static int Count => _keys.Count;

    internal static string Directory => Path.Combine(Paths.ConfigPath, "UtilityBelt");

    internal static string PathFor(int slot) =>
        Path.Combine(Directory, "ItemLocks_Slot" + slot.ToString(CultureInfo.InvariantCulture) + ".tsv");

    internal static string Key(string cardId, int createdOnTick) =>
        cardId + "\t" + createdOnTick.ToString(CultureInfo.InvariantCulture);

    internal static bool Contains(string cardId, int createdOnTick)
    {
        if (_slot == NoSlot || string.IsNullOrEmpty(cardId))
        {
            return false;
        }

        return _keys.Contains(Key(cardId, createdOnTick));
    }

    /// <summary>Flips the lock on one key and saves at once. Returns the new state.</summary>
    internal static bool Toggle(string cardId, int createdOnTick)
    {
        if (_slot == NoSlot || string.IsNullOrEmpty(cardId))
        {
            return false;
        }

        var key = Key(cardId, createdOnTick);
        bool locked;
        if (_keys.Remove(key))
        {
            locked = false;
        }
        else
        {
            _keys.Add(key);
            locked = true;
        }

        Save();
        return locked;
    }

    /// <summary>Reads the slot's file into memory, replacing whatever was loaded before.</summary>
    internal static void LoadSlot(int slot)
    {
        _keys.Clear();
        _foreign.Clear();
        _slot = slot;
        if (slot == NoSlot)
        {
            return;
        }

        var path = PathFor(slot);
        try
        {
            if (!File.Exists(path))
            {
                return;
            }

            foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                var line = raw.TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                var parts = line.Split('\t');
                if (parts[0] == CurrentVersion && parts.Length >= 3 && !string.IsNullOrEmpty(parts[1])
                    && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var tick))
                {
                    _keys.Add(Key(parts[1], tick));
                }
                else
                {
                    // Another version's row, or one this build cannot read: keep it as it is.
                    _foreign.Add(line);
                }
            }
        }
        catch (Exception ex)
        {
            WarnOnce("load:" + ex.GetType().Name, $"[UB] Item locks: could not read {path} ({ex.GetType().Name}: {ex.Message}); this slot starts with no locks and the file is left as it is until a lock changes.");
        }
    }

    /// <summary>Drops every lock in the slot (a new game in that slot).</summary>
    internal static void Clear()
    {
        if (_keys.Count == 0 && _foreign.Count == 0)
        {
            return;
        }

        _keys.Clear();
        _foreign.Clear();
        Save();
    }

    /// <summary>
    /// Removes every lock whose key is not in <paramref name="live"/>. Returns how many went. Rows of other
    /// versions are not judged here and stay.
    /// </summary>
    internal static int Prune(HashSet<string> live)
    {
        var gone = 0;
        foreach (var key in new List<string>(_keys))
        {
            if (!live.Contains(key))
            {
                _keys.Remove(key);
                gone++;
            }
        }

        if (gone > 0)
        {
            Save();
        }

        return gone;
    }

    private static void Save()
    {
        if (_slot == NoSlot)
        {
            return;
        }

        var path = PathFor(_slot);
        try
        {
            if (_keys.Count == 0 && _foreign.Count == 0)
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                return;
            }

            System.IO.Directory.CreateDirectory(Directory);
            var sb = new StringBuilder();
            sb.Append("# Utility Belt item locks for save slot ").Append(_slot.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("# version\tCardID\tCreatedOnTick (one line per lock; cards of one drop share a tick and lock together)\n");
            var sorted = new List<string>(_keys);
            sorted.Sort(StringComparer.Ordinal);
            foreach (var key in sorted)
            {
                sb.Append(CurrentVersion).Append('\t').Append(key).Append('\n');
            }

            foreach (var line in _foreign)
            {
                sb.Append(line).Append('\n');
            }

            var tmp = path + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Replace(tmp, path, null);
            }
            else
            {
                File.Move(tmp, path);
            }
        }
        catch (Exception ex)
        {
            WarnOnce("save:" + ex.GetType().Name, $"[UB] Item locks: could not write {path} ({ex.GetType().Name}: {ex.Message}); locks set now hold until the game closes.");
        }
    }

    private static void WarnOnce(string cause, string message)
    {
        if (_warned.Add(cause))
        {
            Plugin.Logger.LogWarning(message);
        }
    }
}
