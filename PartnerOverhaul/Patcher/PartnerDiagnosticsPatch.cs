namespace PartnerOverhaul.Patcher
{
    /// <summary>
    /// LogInfo-only diagnostics for 4 reported Partner bugs whose root cause is NOT yet confirmed
    /// (fishing-line AI freeze, brain-tanning stuck-on-first-hide, clothes/carry-weight/temperature,
    /// cauldron ownership reset). No behavior changes — this exists to gather a real
    /// LogOutput.log so a future fix can be built from evidence instead of a guess, per root
    /// CLAUDE.md's Debugging Discipline. Gated by Plugin.EnableDiagnostics (default OFF since
    /// 1.0.5 — every Info site below honours that gate; ask a reporter to enable it).
    /// </summary>
    public static class PartnerDiagnosticsPatch
    {
        private static BepInEx.Logging.ManualLogSource Logger => Plugin.Logger;
        private static readonly FieldInfo AllowedUsersField =
            typeof(InGameCardBase).GetField("AllowedUsers", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo CurrentParentPopupField =
            typeof(OwnershipToggle).GetField("CurrentParentPopup", BindingFlags.Instance | BindingFlags.NonPublic);

        public static void ApplyPatch(Harmony harmony)
        {
            var canBePerformed = AccessTools.Method(typeof(AffectItemsDutyAction), nameof(AffectItemsDutyAction.CanBePerformed));
            harmony.Patch(canBePerformed, postfix: new HarmonyMethod(typeof(PartnerDiagnosticsPatch), nameof(LogBrainTanCandidates)));

            var findCardToEquip = AccessTools.Method(typeof(InGameNPC), "FindCardToEquip");
            if (findCardToEquip != null)
                harmony.Patch(findCardToEquip, postfix: new HarmonyMethod(typeof(PartnerDiagnosticsPatch), nameof(LogEquipState)));
            else
                Logger.LogWarning("[PartnerDiagnosticsPatch] InGameNPC.FindCardToEquip not found — clothes/weight diagnostic skipped.");

            var ownershipRefresh = AccessTools.Method(typeof(OwnershipToggle), "Refresh");
            if (ownershipRefresh != null)
                harmony.Patch(ownershipRefresh, postfix: new HarmonyMethod(typeof(PartnerDiagnosticsPatch), nameof(LogOwnershipState)));
            else
                Logger.LogWarning("[PartnerDiagnosticsPatch] OwnershipToggle.Refresh not found — cauldron ownership diagnostic skipped.");

            if (Plugin.EnableDiagnostics.Value)
                Plugin.Instance.StartCoroutine(PollAlliedNpcDuties());

            Logger.LogDebug("PartnerDiagnosticsPatch applied.");
        }

        // ── fishing-line freeze: watch whether an allied NPC's CurrentDuty ever changes ────
        private static IEnumerator PollAlliedNpcDuties()
        {
            var lastDutyByNpc = new Dictionary<InGameNPC, string>();
            while (true)
            {
                yield return new WaitForSecondsRealtime(10f);
                if (!Plugin.EnableDiagnostics.Value) continue;
                try
                {
                    var gm = GameManager.Instance;
                    if (gm == null) continue;
                    foreach (var npc in gm.AlliedNPCs)
                    {
                        if (npc == null) continue;
                        string dutyName = npc.CurrentDuty.Duty != null ? npc.CurrentDuty.Duty.name : "(none)";
                        bool hasPrevious = lastDutyByNpc.TryGetValue(npc, out var previous);
                        if (hasPrevious && previous == dutyName) continue;
                        Logger.LogInfo($"[PartnerDiagnostics] Partner '{npc.name}' CurrentDuty={dutyName}"
                            + (hasPrevious ? $" (was {previous})" : " (first observation)"));
                        lastDutyByNpc[npc] = dutyName;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogDebug($"[PartnerDiagnostics] PollAlliedNpcDuties failed: {ex}");
                }
            }
        }

        // ── brain-tanning stuck-on-first-hide ──────────────────────────────────────────────
        private static void LogBrainTanCandidates(InGameNPC _FromNPC, NPCDuty _FromDuty, bool __result)
        {
            try
            {
                if (!Plugin.EnableDiagnostics.Value) return;
                if (_FromDuty == null || _FromDuty.name == null || !_FromDuty.name.Contains("BrainTan")) return;
                Logger.LogInfo($"[PartnerDiagnostics] BrainTan duty '{_FromDuty.name}' on '{_FromNPC?.name}': CanBePerformed={__result}");
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"[PartnerDiagnostics] LogBrainTanCandidates failed: {ex}");
            }
        }

        // ── clothes / carry-weight / temperature ───────────────────────────────────────────
        private static readonly Dictionary<InGameNPC, string> _lastEquipStateByNpc = new Dictionary<InGameNPC, string>();

        private static void LogEquipState(InGameNPC __instance)
        {
            try
            {
                if (!Plugin.EnableDiagnostics.Value || __instance == null) return;
                var equipped = __instance.EquippedCards;
                string names = equipped == null || equipped.Count == 0
                    ? "(none)"
                    : string.Join(", ", equipped.Select(c => c != null && c.CardModel != null ? c.CardModel.name : "?"));
                _lastEquipStateByNpc.TryGetValue(__instance, out var previous);
                if (previous == names) return;
                Logger.LogInfo($"[PartnerDiagnostics] '{__instance.name}' EquippedCards after FindCardToEquip: {names}");
                _lastEquipStateByNpc[__instance] = names;
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"[PartnerDiagnostics] LogEquipState failed: {ex}");
            }
        }

        // ── cauldron ownership reset ────────────────────────────────────────────────────────
        private static void LogOwnershipState(object __instance)
        {
            try
            {
                if (!Plugin.EnableDiagnostics.Value) return;
                if (CurrentParentPopupField == null || AllowedUsersField == null) return;

                var popup = CurrentParentPopupField.GetValue(__instance) as InspectionPopup;
                var titleCard = popup != null ? popup.TitleSectionCard : null;
                if (titleCard == null) return;

                var allowedUsers = AllowedUsersField.GetValue(titleCard) as ICollection;
                Logger.LogInfo($"[PartnerDiagnostics] OwnershipToggle.Refresh on '{titleCard.name}' "
                    + $"(instance {titleCard.GetInstanceID()}): AllowedUsers count={allowedUsers?.Count ?? -1}");
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"[PartnerDiagnostics] LogOwnershipState failed: {ex}");
            }
        }
    }
}
