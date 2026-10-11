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
    /// Scope (1.1.0): the filter applies only when an ALLIED Partner is selecting items for
    /// PartnerDuty_Firekeeping. SortFoundItemsByPreference is shared by every NPC (wild animals
    /// included) and every duty's item selection, so before 1.1.0 a reserved UID was withheld
    /// from all of them, which the config text never promised. The scope is the duty, not the
    /// card, so mod cards that carry Firekeeping at card level stay covered.
    /// </summary>
    public static class WoodReserveListPatch
    {
        private static BepInEx.Logging.ManualLogSource Logger => Plugin.Logger;
        private static HashSet<string> _reservedUids = new HashSet<string>();
        private static string _lastConfigValue;
        private static bool _loggedInitialCount;

        // PartnerDuty_Firekeeping (vanilla NPCDuty UniqueID, EA 0.68b export).
        internal const string PartnerDutyFirekeepingUid = "6147eff5b0fe3014ca57364f4d075514";

        public static void ApplyPatch(Harmony harmony)
        {
            harmony.Patch(
                AccessTools.Method(typeof(NPCItemSelectionSettings), nameof(NPCItemSelectionSettings.SortFoundItemsByPreference)),
                prefix: new HarmonyMethod(typeof(WoodReserveListPatch), nameof(FilterReservedItems)));
            Logger.LogDebug("WoodReserveListPatch applied.");
        }

        // _FromNPC and _FromDuty are injected by Harmony by parameter name from
        // SortFoundItemsByPreference's own signature. Kept TYPED (never object): a destroyed
        // Unity object is only null under UnityEngine.Object's == overload.
        private static void FilterReservedItems(ref List<InGameCardBase> _Items, InGameNPC _FromNPC, NPCDuty _FromDuty)
        {
            try
            {
                RefreshReservedSetIfNeeded();
                if (_reservedUids.Count == 0 || _Items == null || _Items.Count == 0) return;
                if (!AppliesTo(_FromDuty, _FromNPC != null && _FromNPC.AlliedWithPlayer)) return;

                _Items.RemoveAll(item => item != null && item.CardModel != null
                    && _reservedUids.Contains(item.CardModel.UniqueID));
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"[WoodReserveListPatch] FilterReservedItems failed: {ex}");
            }
        }

        /// <summary>
        /// True only for an allied Partner's Firekeeping item selection: the one place the
        /// "Reserved Fuel/Wood UIDs" option promises to act. Internal so a harness leg can call
        /// it with a duty UniqueID string (TestHarness resolves the string to the NPCDuty).
        /// </summary>
        internal static bool AppliesTo(NPCDuty duty, bool allied)
        {
            return allied && duty != null && duty.UniqueID == PartnerDutyFirekeepingUid;
        }

        /// <summary>True when the UniqueID is on the reserve list (re-reads the config value first).</summary>
        internal static bool IsReserved(string uid)
        {
            RefreshReservedSetIfNeeded();
            return uid != null && _reservedUids.Contains(uid);
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
                // evaluation every run, so a configured list reads as "confirmed loaded".
                // The EMPTY case is the shipped default, so it is Debug rather than Info:
                // it would otherwise cost every player a log line to say nothing happened.
                // The "confirmed no-op by design vs. config never loaded" distinction it was
                // written for is still available by enabling Debug logging.
                _loggedInitialCount = true;
                if (_reservedUids.Count > 0)
                    Logger.LogInfo($"[WoodReserveListPatch] Reserved fuel/wood UIDs: {_reservedUids.Count} ({string.Join(", ", _reservedUids)})");
                else
                    Logger.LogDebug("[WoodReserveListPatch] Reserved fuel/wood UIDs: 0 (list empty; the reserve filter is a no-op)");
            }
            else if (_reservedUids.Count > 0)
            {
                Logger.LogInfo($"[WoodReserveListPatch] Reserved fuel/wood UIDs updated: {string.Join(", ", _reservedUids)}");
            }
        }
    }
}
