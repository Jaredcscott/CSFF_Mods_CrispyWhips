// Host-free: no BepInEx or UnityEngine reference. Every string the Mod Update Manager window shows comes
// through Get (plan feature 22). The English rows are in MumText.Table.cs (generated from
// Localization/SimpEn.csv); Plugin sets a lookup once that asks the game's own dictionary, so a Chinese-language
// player sees the SimpCn.csv rows, and when there is no lookup, no answer, or a Chinese answer the font cannot
// draw, the English row is shown instead. Development_Tools/Tests/MUM-CompiledLogic.Tests.ps1 compiles this file
// and drives it, and MUM-Localization.Tests.ps1 holds the table to the CSVs and the sources to the table.
using System;
using System.Collections.Generic;

namespace mod_update_manager
{
    public static partial class MumText
    {
        /// <summary>The part every row's key starts with; a name given to Get is this plus the name.</summary>
        public const string KeyPrefix = "Mod_Update_Manager_";

        private static Func<string, string> _lookup;
        private static Func<string, bool> _canDraw;
        private static readonly Dictionary<string, string> Cache = new Dictionary<string, string>();
        private static readonly HashSet<string> Warned = new HashSet<string>();

        /// <summary>Sets how a full key is looked up in the game's dictionary; it answers null when there is no
        /// row. Null removes the lookup, which leaves every string English.</summary>
        public static void SetLookup(Func<string, string> lookup)
        {
            _lookup = lookup;
            ResetCache();
        }

        /// <summary>Sets the glyph check: it is asked, with the first Chinese character of a looked-up string,
        /// whether the font can draw it. A false answer shows that string in English instead of an empty box.</summary>
        public static void SetGlyphCheck(Func<string, bool> canDraw)
        {
            _canDraw = canDraw;
            ResetCache();
        }

        /// <summary>Forgets every looked-up answer, so the next Get asks again (the window calls this each time it
        /// opens, so a language change between openings is picked up).</summary>
        public static void ResetCache()
        {
            Cache.Clear();
        }

        /// <summary>The names of every English row.</summary>
        public static IEnumerable<string> Names
        {
            get { return EnglishRows.Keys; }
        }

        /// <summary>The English row for a name, or null when there is none.</summary>
        public static string English(string name)
        {
            string text;
            return name != null && EnglishRows.TryGetValue(name, out text) ? text : null;
        }

        /// <summary>The text for a name: the dictionary's row when a lookup answers one the font can draw,
        /// otherwise the English row. A name with no row is a bug in the caller: it is logged once and the
        /// name itself is returned, so the gap shows on screen.</summary>
        public static string Get(string name)
        {
            string cached;
            if (name != null && Cache.TryGetValue(name, out cached)) return cached;

            var english = English(name);
            if (english == null)
            {
                WarnOnce("missing:" + name, "MumText: no row named '" + name + "', showing the name.");
                return name ?? "";
            }

            var result = english;
            var lookup = _lookup;
            if (lookup != null)
            {
                string found = null;
                try { found = lookup(KeyPrefix + name); }
                catch (Exception ex) { WarnOnce("lookup", "MumText: the dictionary lookup threw, so text stays English: " + ex.Message); }

                if (!string.IsNullOrEmpty(found))
                {
                    var cjk = FirstCjk(found);
                    var canDraw = _canDraw;
                    if (cjk != null && canDraw != null && !SafeCanDraw(canDraw, cjk))
                        WarnOnce("glyph", "MumText: the window font has no glyph for '" + cjk + "', so translated text stays English.");
                    else
                        result = found;
                }
            }

            if (name != null) Cache[name] = result;
            return result;
        }

        private static bool SafeCanDraw(Func<string, bool> canDraw, string character)
        {
            try { return canDraw(character); }
            catch (Exception ex)
            {
                WarnOnce("glyph-threw", "MumText: the glyph check threw, so translated text stays English: " + ex.Message);
                return false;
            }
        }

        /// <summary>The first Chinese character in a string as a one-character string, or null if it has none.</summary>
        public static string FirstCjk(string text)
        {
            if (text == null) return null;
            foreach (var c in text)
            {
                if ((c >= '⺀' && c <= '鿿') || (c >= '豈' && c <= '﫿') || (c >= '＀' && c <= '￯'))
                    return c.ToString();
            }
            return null;
        }

        private static void WarnOnce(string token, string message)
        {
            if (Warned.Add(token)) MumLog.Warning(message);
        }
    }
}
