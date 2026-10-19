namespace PartnerOverhaul.Patcher
{
    /// <summary>
    /// PO-C6 (plan section 3.2). Opt-in, default OFF (config "Partner Starts Conversations", owner
    /// tile 4 A, 2026-10-15): the Partner sometimes opens PO's own talk scene, PO_PartnerTalk, by
    /// itself. The first NPCDuty PartnerOverhaul authors.
    ///
    /// Mechanism: one GameLoad.LoadMainGameData postfix (pre-menu, after the framework has loaded
    /// the dialog JSON; InGameNPC.Init snapshots NPCAgent.AgentDuties at every run boot, so a ref
    /// added here reaches every Partner, saved or new) appends ONE NPCDutyRef to vanilla
    /// Agent_Partner.AgentDuties. Its duty is an Object.Instantiate copy of vanilla
    /// PartnerDuty_ExpressNeedAttention (so DutyConditions, tick step and give-up options start
    /// engine-valid) whose one step is a copy of that duty's StartDialogDutyAction with Dialog =
    /// PO_PartnerTalk and CanStartFromDifferentLocation false, so it fires only while the Partner is
    /// on the player's board (.decomp/StartDialogDutyAction.cs:17). DialogInterruptPatch already
    /// keeps it out of the player's rituals (it prefixes every StartDialogDutyAction).
    ///
    /// Weights (on the ref, never the duty; PLACEHOLDERS, TUNABLE): base 110, just above vanilla
    /// PartnerDuty_Idle (100) so it only replaces idling; +40 at Dawn and Morning
    /// (TimeOfDayWeightModifiers, .decomp/NPCDutyWeights.cs:14). Gate: Partner_DialogCooldown 0 to 1
    /// with CancelDrop, the same gate as every Express duty, plus MaxPerformPerDay 1. It sits BELOW
    /// every need-driven Express duty by construction, not only by number: those duties reach weight
    /// 1 at their band edges, so no base weight could stay under them, and instead each need band
    /// cancels this duty outright while it is in range (an Add0Weight modifier whose interpolated
    /// weight is -1000000 everywhere in its range; .decomp/NPCStatBasedDropChanceModifier.cs and
    /// NPCDutySelectionInfo.cs:268). Bands: Weight 0..342999 (Need Food), BodyTemperature -99..50
    /// (Need Warmth, widened to "Looks very cold." like Cold Partner Asks For Warmth), Trust 0..1000
    /// (Need Trust), Connection 0..90 (Need Attention), Relationship 0..250 (Ultimatum), Stress
    /// 97..240 (Blame).
    ///
    /// Soft dependency: PO_PartnerTalk unresolved (framework absent or older than 2.15.1) means no
    /// duty is added, and one added on an earlier pass is removed. Idempotent by duty UniqueID. Every
    /// early return logs a warn-once breadcrumb per cause. Calls no CSFFModFramework member.
    /// </summary>
    public static class PartnerStartsTalkDutyPatch
    {
        internal const string AgentPartnerUid = "b6a4cc575cf9ddd41b321ec619db21fd";
        internal const string DonorDutyUid = "fcd07c11a6f7420429cc03913775d3b3"; // PartnerDuty_ExpressNeedAttention
        internal const string DutyUid = "partner_overhaul_duty_start_talk";
        internal const string DutySoName = "PO_PartnerDuty_StartTalk";
        internal const string TalkSceneName = PartnerTalkAnswerPatch.TalkSceneName;

        // PLACEHOLDERS, TUNABLE (plan section 3.2 PO-C6).
        internal const int BaseWeight = 110;
        internal const int MorningWeightBonus = 40;
        internal const int MaxPerDay = 1;
        // PO-C6 point (3) / PO-C7 point (6): a fresh memory (1..96) raises the weight, so the Partner is
        // more likely to start talk after a fight, escape, build or catch. PLACEHOLDER, TUNABLE: it only
        // adds to the base weight; it cannot lift the duty over a need band, which cancels it outright.
        internal const int MemoryWeightBonus = 30;
        internal static readonly string[] MemoryStatUids =
        {
            "poStatMemoryFight", "poStatMemoryEscape", "poStatMemoryBuild", "poStatMemoryCatch",
        };
        private const int CancelWeight = -1000000;

        // Vanilla NPCStat UIDs (Documentation/GameData/.../UniqueIDScriptableGUID/NPCStat.json).
        internal const string DialogCooldownUid = "7b4b2fcda5d5fe14bbe42fddc19afc57";
        // Need bands that cancel the duty while in range: (stat UID, label, lo, hi).
        internal static readonly (string Uid, string Label, float Lo, float Hi)[] NeedBands =
        {
            ("3af35423531a2cc47aa5c516e7dc67fc", "Partner_Weight", 0f, 342999f),
            ("e42deda70e789cd45bb88d02ef986bde", "Partner_BodyTemperature", -99f, 50f),
            ("8392e717de5b9de45bc8f75d3d7479bb", "Partner_Trust", 0f, 1000f),
            ("2018b833d101f7841a34aee6f7f74a44", "Partner_Connection", 0f, 90f),
            ("83352f5454a740d429538456a81784af", "Partner_Relationship", 0f, 250f),
            ("fb1f24dcb39c36b41a16cd48cde74595", "Partner_Stress", 97f, 240f),
        };

        private static BepInEx.Logging.ManualLogSource Logger => Plugin.Logger;
        private static readonly HashSet<string> Warned = new HashSet<string>();

        // Built once per process; the NPCAgent and its duty list are process-lifetime data.
        private static NPCDuty _duty;
        private static StartDialogDutyAction _action;

        public static void ApplyPatch(Harmony harmony)
        {
            if (Plugin.PartnerStartsConversations == null || !Plugin.PartnerStartsConversations.Value)
            {
                Logger.LogDebug("PartnerStartsTalkDutyPatch not applied (option off).");
                return;
            }
            var target = AccessTools.Method(AccessTools.TypeByName("GameLoad"), "LoadMainGameData");
            if (target == null)
            {
                Logger.LogWarning("[PartnerStartsTalk] GameLoad.LoadMainGameData not found; the Partner does not start conversations (game update?).");
                return;
            }
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(PartnerStartsTalkDutyPatch), nameof(LoadMainGameData_Postfix)));
            var select = AccessTools.Method(typeof(InGameNPC), nameof(InGameNPC.SelectDuty));
            if (select == null)
                Logger.LogWarning("[PartnerStartsTalk] InGameNPC.SelectDuty not found; a started talk may not count toward its once-a-day limit (game update?).");
            else
                harmony.Patch(select, postfix: new HarmonyMethod(typeof(PartnerStartsTalkDutyPatch), nameof(SelectDuty_Postfix)));
            var next = AccessTools.Method(typeof(SelectedNPCDuty), nameof(SelectedNPCDuty.NextAction));
            _performedTodayListField = AccessTools.Field(typeof(InGameNPC), "DutiesPerformedTodayList");
            if (next == null || _performedTodayListField == null)
                Logger.LogWarning("[PartnerStartsTalk] SelectedNPCDuty.NextAction or InGameNPC.DutiesPerformedTodayList not found; a finished talk is not counted toward its once-a-day limit (game update?).");
            else
                harmony.Patch(next, prefix: new HarmonyMethod(typeof(PartnerStartsTalkDutyPatch), nameof(NextAction_Prefix)));
            Logger.LogDebug("PartnerStartsTalkDutyPatch applied.");
        }

        private static void WarnOnce(string cause, string detail)
        {
            if (Warned.Add(cause))
                Logger.LogWarning($"[PartnerStartsTalk] {cause}: {detail} (further occurrences suppressed)");
        }

        private static void DebugOnce(string cause, string detail)
        {
            if (Warned.Add(cause))
                Logger.LogDebug($"[PartnerStartsTalk] {cause}: {detail}");
        }

        private static void LoadMainGameData_Postfix()
        {
            try
            {
                if (Plugin.PartnerStartsConversations == null || !Plugin.PartnerStartsConversations.Value) return;
                ApplyDuty();
            }
            catch (Exception ex)
            {
                WarnOnce("postfix-exception", ex.ToString());
            }
        }

        private static void ApplyDuty()
        {
            var agent = UniqueIDScriptable.GetFromID<NPCAgent>(AgentPartnerUid);
            if (agent == null) { WarnOnce("agent-missing", "Agent_Partner not resolvable; no duty added"); return; }
            var refs = agent.AgentDuties ?? new NPCDutyRef[0];
            int ours = Array.FindIndex(refs, r => r != null && r.TargetDuty != null && r.TargetDuty.UniqueID == DutyUid);

            DialogScene talk = FindTalkScene();
            bool talkUsable = talk != null && talk.DefaultStartingLine != null;
            if (!talkUsable)
            {
                if (ours >= 0)
                {
                    agent.AgentDuties = refs.Where(r => !(r != null && r.TargetDuty != null && r.TargetDuty.UniqueID == DutyUid)).ToArray();
                }
                DebugOnce("talk-scene-missing", $"DialogScene '{TalkSceneName}' not loaded or has no starting line (CSFFModFramework absent or older than 2.15.1?); no duty added");
                return;
            }

            if (_duty == null && !BuildDuty()) return;
            _action.Dialog = talk; // refreshed every pass: the scene object is framework-owned
            _duty.Init(); // registers the UniqueID (UniqueIDScriptable.RegisterID, check-before-add) so a save naming it resolves

            if (ours >= 0) return;

            var weights = BuildWeights();
            if (weights == null) return;
            var donorRef = refs.FirstOrDefault(r => r != null && r.TargetDuty != null && r.TargetDuty.UniqueID == DonorDutyUid);
            if (donorRef == null) { WarnOnce("donor-ref-missing", "Agent_Partner has no PartnerDuty_ExpressNeedAttention ref to copy the activation from; no duty added"); return; }

            var dutyRef = new NPCDutyRef
            {
                TargetDuty = _duty,
                ActivatingMode = donorRef.ActivatingMode,
                StartActive = donorRef.StartActive,
                ActivatingConditions = donorRef.ActivatingConditions,
                PreferenceWeights = weights.Value,
            };
            var merged = new List<NPCDutyRef>(refs) { dutyRef };
            agent.AgentDuties = merged.ToArray();
            Logger.LogInfo($"[PartnerStartsTalk] added the '{DutySoName}' duty to Agent_Partner (base weight {BaseWeight}, +{MorningWeightBonus} in the morning; cancelled while a need band is in range).");
        }

        // T2.515, half 1 of 2: every talk line adds Partner_DialogCooldown (+24 on PO_PartnerTalk_Start),
        // which takes this duty out of its own DialogCooldown 0..1 gate while it runs. InGameNPC.UpdateTick
        // runs CheckForDuties (SelectDuty, then SetCurrentDuty) BEFORE it updates the current duty, so on
        // the first tick after the talk closes the game would pick Idle and cancel the talk duty before
        // its update branch ran. A started talk duty that can still be performed stays selected, so
        // that tick reaches the update branch and NextAction_Prefix below can count it. Only this duty;
        // vanilla Express duties (MaxPerformPerDay 0) are left exactly as the game picks them.
        private static void SelectDuty_Postfix(InGameNPC __instance, ref NPCDuty __result)
        {
            try
            {
                if (__result == null || __instance == null) return;
                var current = __instance.CurrentDuty;
                if (!current.IsValid || !current.ActionStarted || current.Duty == __result) return;
                if (current.Duty.UniqueID != DutyUid) return;
                if (current.CannotPerform(__instance)) return;
                __result = current.Duty;
            }
            catch (Exception ex)
            {
                WarnOnce("select-duty-exception", ex.ToString());
            }
        }

        private static FieldInfo _performedTodayListField;

        // T2.515, half 2 of 2: the game counts a duty (DutiesPerformedToday) only in
        // InGameNPC.StartOrUpdateCurrentDuty, after CurrentDuty.Finished, and only on a call that ran
        // the step. StartDialogDutyAction.ActionComplete is !IsInDialog, so the talk's one step is not
        // complete on the tick it opens the dialog; on the first tick after the dialog closes
        // ReadyForNextAction is already true, UpdateTick skips the step update and calls NextAction,
        // and MoveToAction steps past the end and Clears the duty: Finished is never asked and the talk
        // is never counted, so MaxPerformPerDay 1 never applied. (Vanilla one-step Express duties are
        // never counted either; they have no daily limit, so this leaves them alone.) This prefix runs
        // at that NextAction and adds the count exactly as the game's own Finished branch does: same
        // condition (Finished on a valid duty with a current step and no decision outcome), same key
        // (UniqueIDScriptable.SaveID), same dictionary and the private list Save() writes from. It
        // cannot double-count: when the game's own branch counts a duty it also Clears it, so no
        // NextAction ever sees it valid afterwards. OnDayEnded clears both, as for every duty.
        private static void NextAction_Prefix(ref SelectedNPCDuty __instance, InGameNPC _FromNPC)
        {
            try
            {
                if (_FromNPC == null || !__instance.IsValid || __instance.Duty.UniqueID != DutyUid) return;
                if (__instance.CurrentAction == null || __instance.CurrentDecisionOutcome != null) return;
                if (!__instance.Finished(_FromNPC)) return;
                string key = UniqueIDScriptable.SaveID(__instance.Duty);
                if (string.IsNullOrEmpty(key)) return;
                if (!_FromNPC.DutiesPerformedToday.ContainsKey(key))
                {
                    _FromNPC.DutiesPerformedToday.Add(key, 1);
                    (_performedTodayListField.GetValue(_FromNPC) as List<string>)?.Add(key);
                }
                else
                {
                    _FromNPC.DutiesPerformedToday[key]++;
                }
            }
            catch (Exception ex)
            {
                WarnOnce("next-action-exception", ex.ToString());
            }
        }

        private static DialogScene FindTalkScene()
        {
            foreach (var scene in Resources.FindObjectsOfTypeAll<DialogScene>())
                if (scene != null && scene.name == TalkSceneName) return scene;
            return null;
        }

        private static bool BuildDuty()
        {
            var donor = UniqueIDScriptable.GetFromID<NPCDuty>(DonorDutyUid);
            if (donor == null) { WarnOnce("donor-duty-missing", "PartnerDuty_ExpressNeedAttention not resolvable; no duty added"); return false; }
            var donorAction = donor.ActionSequence != null && donor.ActionSequence.Length == 1 ? donor.ActionSequence[0] as StartDialogDutyAction : null;
            if (donorAction == null) { WarnOnce("donor-shape", "PartnerDuty_ExpressNeedAttention is no longer one StartDialogDutyAction step (game update?); no duty added"); return false; }

            var action = UnityEngine.Object.Instantiate(donorAction);
            action.name = DutySoName + "_StartDialog";
            action.hideFlags = HideFlags.DontUnloadUnusedAsset;
            action.CanStartFromDifferentLocation = false;

            var duty = UnityEngine.Object.Instantiate(donor);
            duty.name = DutySoName;
            duty.hideFlags = HideFlags.DontUnloadUnusedAsset;
            duty.UniqueID = DutyUid;
            duty.DutyName = new LocalizedString { LocalizationKey = "PartnerOverhaul_Duty_StartTalk_Name", DefaultText = "Make Small Talk" };
            duty.DutySummary = new LocalizedString { LocalizationKey = "PartnerOverhaul_Duty_StartTalk_Summary", DefaultText = "Wants to talk with you." };
            duty.MaxPerformPerDay = MaxPerDay;
            duty.ActionSequence = new NPCDutyAction[] { action };
            duty.OnCancelActions = new NPCDutyAction[0];

            _action = action;
            _duty = duty;
            return true;
        }

        private static NPCDutyWeights? BuildWeights()
        {
            var cooldown = UniqueIDScriptable.GetFromID<NPCStat>(DialogCooldownUid);
            if (cooldown == null) { WarnOnce("cooldown-stat-missing", "Partner_DialogCooldown not resolvable; no duty added"); return null; }
            var mods = new List<NPCStatBasedDropChanceModifier>
            {
                new NPCStatBasedDropChanceModifier
                {
                    Stat = cooldown, UseAssociatedAgent = true,
                    StatRange = new Vector2(0f, 1f), InterpWeightRange = new Vector2Int(0, 0),
                    WhenOutOfRange = StatDropChanceModOutOfRange.CancelDrop,
                },
            };
            foreach (var band in NeedBands)
            {
                var stat = UniqueIDScriptable.GetFromID<NPCStat>(band.Uid);
                // A missing need stat would let small talk outrank that need: add nothing instead.
                if (stat == null) { WarnOnce("need-stat-missing-" + band.Label, band.Label + " not resolvable; no duty added"); return null; }
                mods.Add(new NPCStatBasedDropChanceModifier
                {
                    Stat = stat, UseAssociatedAgent = true,
                    StatRange = new Vector2(band.Lo, band.Hi), InterpWeightRange = new Vector2Int(CancelWeight, CancelWeight),
                    WhenOutOfRange = StatDropChanceModOutOfRange.Add0Weight,
                });
            }

            // PO's own memory stats (PO-C7). A stat that does not resolve only drops that bonus.
            foreach (var memUid in MemoryStatUids)
            {
                var mem = UniqueIDScriptable.GetFromID<NPCStat>(memUid);
                if (mem == null) { WarnOnce("memory-stat-missing-" + memUid, memUid + " not resolvable; no talk-after-memory weight bonus for it"); continue; }
                mods.Add(new NPCStatBasedDropChanceModifier
                {
                    Stat = mem, UseAssociatedAgent = true,
                    StatRange = new Vector2(1f, 96f), InterpWeightRange = new Vector2Int(MemoryWeightBonus, MemoryWeightBonus),
                    WhenOutOfRange = StatDropChanceModOutOfRange.Add0Weight,
                });
            }

            var weights = new NPCDutyWeights
            {
                BaseWeight = BaseWeight,
                CardWeightModifiers = new CardBasedDropChanceModifier[0],
                StatWeightModifiers = new StatBasedDropChanceModifier[0],
                AgentStatsWeightModifiers = mods.ToArray(),
                TimeOfDayWeightModifiers = BuildMorningBonus(),
                DistanceToOthersWeightModifiers = new DistanceToAgentWeightModifier[0],
                DistanceToEnvTagWeightModifiers = new DistanceToTagWeightModifier[0],
                DistanceToItemWeightModifiers = new DistanceToItemWeightModifier[0],
            };
            return weights;
        }

        // Dawn and Morning (InGameTimeCondition MomentOfDayIs; its fields are private serialized,
        // so the struct is filled boxed). A miss only drops the morning bonus.
        private static TimeOfDayDropChanceModifier[] BuildMorningBonus()
        {
            const BindingFlags inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var typeField = typeof(InGameTimeCondition).GetField("TimeType", inst);
            var maskField = typeof(InGameTimeCondition).GetField("MomentOfDayValue", inst);
            if (typeField == null || maskField == null)
            {
                WarnOnce("time-condition-fields-missing", "InGameTimeCondition.TimeType or MomentOfDayValue not found; the morning weight bonus is skipped");
                return new TimeOfDayDropChanceModifier[0];
            }
            var mask = new MomentsOfDayMask();
            mask.AddMoment(MomentsOfDay.Dawn);
            mask.AddMoment(MomentsOfDay.Morning);
            object boxed = new InGameTimeCondition();
            typeField.SetValue(boxed, DaysOrHours.MomentOfDayIs);
            maskField.SetValue(boxed, mask);
            return new[] { new TimeOfDayDropChanceModifier { TimeOfDay = (InGameTimeCondition)boxed, AddedWeight = MorningWeightBonus } };
        }
    }
}
