namespace Quick_Transfer.Patcher
{
    /// <summary>
    /// Per-container settings (plan Features 11 and 12), both keyed on the open container's UniqueID
    /// (<see cref="BatchContext.InspectedUniqueId"/>): the exclusion list, where bulk transfer is off, and the
    /// remembered amounts, where a modifier uses a stored count in place of its preset. Both live in plain config
    /// strings so ConfigurationManager and a text editor see them, and every method re-reads the entry, so an edit
    /// takes effect on the next click. Nothing here touches the game.
    /// </summary>
    internal static class ContainerSettings
    {
        private const string AllWord = "all";

        // ---- Exclusion list (Feature 11) ----

        private static List<string> ReadExcluded()
        {
            var list = new List<string>();
            var raw = Plugin.ExcludedContainers.Value;
            if (string.IsNullOrEmpty(raw)) return list;
            foreach (var part in raw.Split(','))
            {
                var uid = part.Trim();
                if (uid.Length > 0 && !list.Contains(uid)) list.Add(uid);
            }
            return list;
        }

        /// <summary>True when bulk transfer is switched off for the container with this UniqueID.</summary>
        public static bool IsExcluded(string uid)
            => !string.IsNullOrEmpty(uid) && ReadExcluded().Contains(uid);

        /// <summary>Adds the container to the list, or removes it when listed. Returns true when it is excluded afterwards.</summary>
        public static bool ToggleExcluded(string uid)
        {
            var list = ReadExcluded();
            bool nowExcluded = !list.Remove(uid);
            if (nowExcluded) list.Add(uid);
            Plugin.ExcludedContainers.Value = string.Join(",", list);
            return nowExcluded;
        }

        // ---- Remembered amounts (Feature 12) ----

        /// <summary>
        /// The combo a remembered amount is stored under: "Shift" or "Ctrl" when exactly one of them is held with
        /// presets on and Full Stack Mode off, else null. Ctrl+Shift is a mode (All, Half, All but one) and is never
        /// remembered; a configured non-Ctrl/Shift modifier uses the custom amount, also never remembered.
        /// </summary>
        public static string ComboNow()
        {
            if (!Plugin.EnableModifierPresets.Value || Plugin.FullStackMode.Value) return null;
            bool ctrl = Plugin.CtrlHeld(), shift = Plugin.ShiftHeld();
            if (ctrl == shift) return null;
            return ctrl ? "Ctrl" : "Shift";
        }

        // "uid/Shift=5;uid/Ctrl=all" as an ordered list, so a write keeps the order the player sees.
        private static List<KeyValuePair<string, string>> ReadAmounts()
        {
            var list = new List<KeyValuePair<string, string>>();
            var raw = Plugin.ContainerAmounts.Value;
            if (string.IsNullOrEmpty(raw)) return list;
            foreach (var part in raw.Split(';'))
            {
                int eq = part.LastIndexOf('=');
                if (eq <= 0) continue;
                var key = part.Substring(0, eq).Trim();
                var value = part.Substring(eq + 1).Trim();
                if (key.Length > 0 && value.Length > 0) list.Add(new KeyValuePair<string, string>(key, value));
            }
            return list;
        }

        private static void WriteAmounts(List<KeyValuePair<string, string>> list)
        {
            var parts = new List<string>();
            foreach (var kv in list) parts.Add(kv.Key + "=" + kv.Value);
            Plugin.ContainerAmounts.Value = string.Join(";", parts);
        }

        private static string AmountKey(string uid, string combo) => uid + "/" + combo;

        /// <summary>
        /// The amount remembered for this container under this combo, as a count or <see cref="Plugin.AllSentinel"/>
        /// for "all", or null when none is stored (or the stored text is not a count, which is ignored, not an error).
        /// </summary>
        public static int? RememberedAmount(string uid, string combo)
        {
            if (string.IsNullOrEmpty(uid) || combo == null) return null;
            var key = AmountKey(uid, combo);
            foreach (var kv in ReadAmounts())
            {
                if (!string.Equals(kv.Key, key, StringComparison.Ordinal)) continue;
                if (string.Equals(kv.Value, AllWord, StringComparison.OrdinalIgnoreCase)) return Plugin.AllSentinel;
                if (int.TryParse(kv.Value, out var n) && n >= 1) return Mathf.Min(n, Plugin.AllSentinel);
                Plugin.Logger.LogDebug($"[QT] Container Amounts: ignoring '{kv.Key}={kv.Value}' (not a count or 'all')");
                return null;
            }
            return null;
        }

        /// <summary>
        /// Press of the Remember Amount Key: stores <paramref name="amount"/> (the combo's current preset) for the
        /// container, or forgets the entry when it already holds that same amount. Returns true when an amount is
        /// remembered afterwards. The word "all" is stored for the whole-stack sentinel, never 9999.
        /// </summary>
        public static bool ToggleRemembered(string uid, string combo, int amount)
        {
            var key = AmountKey(uid, combo);
            var value = amount >= Plugin.AllSentinel ? AllWord : amount.ToString();
            var list = ReadAmounts();
            int index = list.FindIndex(kv => string.Equals(kv.Key, key, StringComparison.Ordinal));
            bool remembered;
            if (index >= 0 && string.Equals(list[index].Value, value, StringComparison.OrdinalIgnoreCase))
            {
                list.RemoveAt(index);
                remembered = false;
            }
            else if (index >= 0)
            {
                list[index] = new KeyValuePair<string, string>(key, value);
                remembered = true;
            }
            else
            {
                list.Add(new KeyValuePair<string, string>(key, value));
                remembered = true;
            }
            WriteAmounts(list);
            return remembered;
        }
    }
}
