namespace PartnerOverhaul.Patcher
{
    /// <summary>
    /// New QoL feature, NOT a bug fix — vanilla's NPC item-selection engine has no per-item
    /// value/distance heuristic at all (confirmed: ItemPreferenceComparer sorts purely on
    /// Exclude/PriorityLevel/PreferenceScore) and no in-game UI exists for a player to mark
    /// specific items off-limits to auto-selection (confirmed: no generic custom-action button
    /// exists for NPC interactions beyond Dialog/Encounter). V1 is a coarse config-file list of
    /// CardData UniqueIDs a Partner should never auto-select for Firekeeping/fuel duties — no
    /// in-game toggle yet, documented as a known limitation.
    /// </summary>
    public static class WoodReserveListPatch
    {
        private static BepInEx.Logging.ManualLogSource Logger => Plugin.Logger;
        private static HashSet<string> _reservedUids = new HashSet<string>();
        private static string _lastConfigValue;
        private static bool _loggedInitialCount;

        public static void ApplyPatch(Harmony harmony)
        {
            harmony.Patch(
                AccessTools.Method(typeof(NPCItemSelectionSettings), nameof(NPCItemSelectionSettings.SortFoundItemsByPreference)),
                prefix: new HarmonyMethod(typeof(WoodReserveListPatch), nameof(FilterReservedItems)));
            Logger.LogDebug("WoodReserveListPatch applied.");
        }

        private static void FilterReservedItems(ref List<InGameCardBase> _Items)
        {
            try
            {
                RefreshReservedSetIfNeeded();
                if (_reservedUids.Count == 0 || _Items == null || _Items.Count == 0) return;

                _Items.RemoveAll(item => item != null && item.CardModel != null
                    && _reservedUids.Contains(item.CardModel.UniqueID));
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"[WoodReserveListPatch] FilterReservedItems failed: {ex}");
            }
        }

        private static void RefreshReservedSetIfNeeded()
        {
            var current = Plugin.ReservedFuelUIDs.Value ?? "";
            if (current == _lastConfigValue) return;
            var isFirstEvaluationThisRun = !_loggedInitialCount;
            _lastConfigValue = current;
            _reservedUids = new HashSet<string>(current.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0));

            if (isFirstEvaluationThisRun)
            {
                // Config-echo diagnostic, same class as the framework's own
                // "LocalizationLoader: language=" line -- state the count on the FIRST
                // evaluation every run, even when it is zero, so an empty (default) list
                // reads as "confirmed no-op by design" rather than silence that could mean
                // the config never loaded at all.
                _loggedInitialCount = true;
                Logger.LogInfo(_reservedUids.Count > 0
                    ? $"[WoodReserveListPatch] Reserved fuel/wood UIDs: {_reservedUids.Count} ({string.Join(", ", _reservedUids)})"
                    : "[WoodReserveListPatch] Reserved fuel/wood UIDs: 0 (list empty; the reserve filter is a no-op)");
            }
            else if (_reservedUids.Count > 0)
            {
                Logger.LogInfo($"[WoodReserveListPatch] Reserved fuel/wood UIDs updated: {string.Join(", ", _reservedUids)}");
            }
        }
    }
}
