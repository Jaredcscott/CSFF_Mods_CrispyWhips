// Host-free: no BepInEx or UnityEngine reference, and nothing here touches a file. The orders the My Mods
// list can be sorted in (plan feature 18). Development_Tools/Tests/MUM-CompiledLogic.Tests.ps1 compiles
// this file (with InstalledModInfo, which it orders) and drives it.
using System;
using System.Collections.Generic;
using System.Linq;

namespace mod_update_manager
{
    /// <summary>
    /// The orders the sort button over the My Mods list cycles through, in the order it cycles them.
    /// Every one keeps starred favorites at the top.
    /// </summary>
    public enum ModSortMode
    {
        /// <summary>By name: the order the list has always had, and the default.</summary>
        Name = 0,
        /// <summary>Mods with an update available first, then by name.</summary>
        UpdateStatus = 1,
        /// <summary>Most endorsed first, then by name.</summary>
        Endorsements = 2,
        /// <summary>By author, mods with no author last, then by name.</summary>
        Author = 3
    }

    public static class ModSorter
    {
        /// <summary>The order a list has before the player picks one, and the one a missing, unreadable or
        /// unknown saved choice falls back to.</summary>
        public const ModSortMode DefaultMode = ModSortMode.Name;

        /// <summary>
        /// The rows in the order the mode names, as a new list (the argument is not changed). Favorites come
        /// first in every mode; inside each group the mode's own key decides, then the name, and rows that
        /// tie on all of those keep the order they arrived in (the sort is stable). The name compare is the
        /// culture-aware one the list has always used.
        /// </summary>
        public static List<InstalledModInfo> Sort(IEnumerable<InstalledModInfo> mods, ModSortMode mode)
        {
            if (mods == null) return new List<InstalledModInfo>();

            var byFavorite = mods.OrderByDescending(m => m.IsFavorited);
            IOrderedEnumerable<InstalledModInfo> ordered;
            switch (mode)
            {
                case ModSortMode.UpdateStatus:
                    ordered = byFavorite.ThenByDescending(m => m.NeedsUpdate).ThenBy(m => m.Name);
                    break;
                case ModSortMode.Endorsements:
                    ordered = byFavorite.ThenByDescending(m => m.EndorsementCount).ThenBy(m => m.Name);
                    break;
                case ModSortMode.Author:
                    ordered = byFavorite.ThenBy(m => string.IsNullOrWhiteSpace(m.Author)).ThenBy(m => (m.Author ?? "").Trim()).ThenBy(m => m.Name);
                    break;
                default:
                    ordered = byFavorite.ThenBy(m => m.Name);
                    break;
            }
            return ordered.ToList();
        }

        /// <summary>The mode after this one, wrapping from the last back to the first.</summary>
        public static ModSortMode Next(ModSortMode mode)
        {
            switch (mode)
            {
                case ModSortMode.Name: return ModSortMode.UpdateStatus;
                case ModSortMode.UpdateStatus: return ModSortMode.Endorsements;
                case ModSortMode.Endorsements: return ModSortMode.Author;
                default: return ModSortMode.Name; // Author, or a value no mode has
            }
        }

        /// <summary>The word a mode is saved as in the preferences file.</summary>
        public static string KeyOf(ModSortMode mode)
        {
            switch (mode)
            {
                case ModSortMode.UpdateStatus: return "update";
                case ModSortMode.Endorsements: return "endorsements";
                case ModSortMode.Author: return "author";
                default: return "name";
            }
        }

        /// <summary>The mode a saved word names. Anything else (nothing saved, an empty word, a word no mode
        /// is saved as, a number) reads as the default, never as a mode by position.</summary>
        public static ModSortMode ParseKey(string key)
        {
            switch ((key ?? "").Trim().ToLowerInvariant())
            {
                case "update": return ModSortMode.UpdateStatus;
                case "endorsements": return ModSortMode.Endorsements;
                case "author": return ModSortMode.Author;
                default: return DefaultMode; // an empty, unknown or numeric key
            }
        }
    }
}
