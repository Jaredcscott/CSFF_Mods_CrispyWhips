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
        // OwnershipToggle.CurrentParentPopup is private (.decomp/OwnershipToggle.cs:18), so it
        // needs reflection. InGameCardBase.AllowedUsers is PUBLIC on EA 0.68b
        // (.decomp/InGameCardBase.cs:56) and is read typed below: through 1.0.5 it was looked up
        // with BindingFlags.NonPublic only, which returns null for a public field, so the
        // ownership trace returned on its null guard and never printed a line.
        private static readonly FieldInfo CurrentParentPopupField =
            typeof(OwnershipToggle).GetField("CurrentParentPopup", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static bool _warnedOwnershipFieldMissing;

        public static void ApplyPatch(Harmony harmony)
        {
            var canBePerformed = AccessTools.Method(typeof(AffectItemsDutyAction), nameof(AffectItemsDutyAction.CanBePerformed));
            harmony.Patch(canBePerformed, postfix: new HarmonyMethod(typeof(PartnerDiagnosticsPatch), nameof(LogAffectItemsVerdict)));

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

        // ── duty item selection (brain-tanning stuck-on-first-hide, and every other ─────────
        //    AffectItemsDutyAction duty a Partner weighs: firekeeping, cleaning, WDI stations)
        // Until 1.1.0 this logged only duties whose name contained "BrainTan", which gave no
        // view of any other duty (the 2026-08-16 WDI duty-starvation investigation saw nothing
        // for its wdiOperate* duties). CanBePerformed runs for every such duty on every
        // re-evaluation, so a raw per-call line would flood the log the way the FindCardToEquip
        // trace did before 1.0.5. Log only when a Partner's verdict for one duty step CHANGES:
        // the result, whether the action has started, and which cards a started action holds
        // (the "same hide every time" evidence). Allied NPCs only, like every trace here.
        private const int MaxActionCardsListed = 6;
        private static readonly Dictionary<InGameNPC, Dictionary<string, string>> _lastVerdictByNpc =
            new Dictionary<InGameNPC, Dictionary<string, string>>();

        private static void LogAffectItemsVerdict(AffectItemsDutyAction __instance, InGameNPC _FromNPC, NPCDuty _FromDuty,
            bool _ActionStarted, int _FromStep, bool __result)
        {
            try
            {
                if (!Plugin.EnableDiagnostics.Value) return;
                if (_FromNPC == null || !_FromNPC.AlliedWithPlayer || _FromDuty == null) return;

                string state = $"CanBePerformed={__result} started={_ActionStarted} affect={__instance?.AffectType}";
                if (_ActionStarted)
                    state += $" actionCards=[{DescribeCards(_FromNPC.CurrentDuty.ActionCards)}]";

                if (!_lastVerdictByNpc.TryGetValue(_FromNPC, out var byStep))
                {
                    byStep = new Dictionary<string, string>();
                    _lastVerdictByNpc[_FromNPC] = byStep;
                }
                string stepKey = $"{_FromDuty.name}#{_FromStep}";
                if (byStep.TryGetValue(stepKey, out var previous) && previous == state) return;
                byStep[stepKey] = state;

                Logger.LogInfo($"[PartnerDiagnostics] Duty '{_FromDuty.name}' step {_FromStep} on '{_FromNPC.name}': {state}");
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"[PartnerDiagnostics] LogAffectItemsVerdict failed: {ex}");
            }
        }

        // Card name plus the Unity instance id the TestHarness also reports, so a log line can
        // be matched to a board card. Capped: a started duty rarely holds more than a few.
        private static string DescribeCards(List<InGameCardBase> cards)
        {
            if (cards == null || cards.Count == 0) return "";
            var parts = cards.Take(MaxActionCardsListed).Select(c =>
                c == null ? "(destroyed)"
                : $"{(c.CardModel != null ? c.CardModel.name : "?")}#{c.GetInstanceID()}{(c.Destroyed ? "(destroyed)" : "")}");
            string listed = string.Join(", ", parts);
            return cards.Count > MaxActionCardsListed ? $"{listed}, +{cards.Count - MaxActionCardsListed} more" : listed;
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
                if (CurrentParentPopupField == null)
                {
                    if (!_warnedOwnershipFieldMissing)
                    {
                        _warnedOwnershipFieldMissing = true;
                        Logger.LogWarning("[PartnerDiagnostics] OwnershipToggle.CurrentParentPopup field not found - cauldron ownership diagnostic cannot run (layout changed?).");
                    }
                    return;
                }

                var popup = CurrentParentPopupField.GetValue(__instance) as InspectionPopup;
                var titleCard = popup != null ? popup.TitleSectionCard : null;
                if (titleCard == null) return;

                var allowedUsers = titleCard.AllowedUsers;
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
