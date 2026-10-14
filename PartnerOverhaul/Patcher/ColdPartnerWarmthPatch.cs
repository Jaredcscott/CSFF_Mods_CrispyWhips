namespace PartnerOverhaul.Patcher
{
    /// <summary>
    /// Opt-in (default OFF), owner decision 2026-10-04 "Warn the player" (plan section 9): a cold
    /// Partner on the player's board voices its need for warmth BEFORE it is hypothermic.
    ///
    /// Vanilla: Agent_Partner.AgentDuties[3] offers PartnerDuty_ExpressNeedWarmth (an
    /// AlwaysActive duty whose one step opens the "Express Need Warmth" dialog) behind two
    /// AgentStatsWeightModifiers, both WhenOutOfRange CancelDrop: Partner_DialogCooldown 0 to 1 and
    /// Partner_BodyTemperature -99 to 0 ("Looks hypothermic."). MEASURED 2026-10-10 (T3.82's
    /// research run): once selected it becomes her current duty and the line reaches the screen while
    /// she is on the player's board, and vanilla's gate reaches only down to body temperature 1.
    /// A following Partner with no fire in range therefore says nothing until she is already past
    /// "Looks very cold." (1 to 50) and one step from dying of the cold.
    ///
    /// Hook: a postfix on NPCDutyRef.CalculateWeight (the method W0.4 named), for the ExpressNeedWarmth
    /// ref only. When vanilla cancelled it, the NPC is an allied Partner on the player's board and its
    /// Partner_BodyTemperature is in (0, 50], the weight report is rebuilt from a COPY of the duty's
    /// weights in which the body-temperature range ends at 50 instead of 0. The cooldown modifier is
    /// left as it is, so the rebuilt report still cancels while the cooldown is running: she asks once
    /// per cooldown and never every tick. No other duty's weight is touched, nobody is moved, and no
    /// shared data object is edited (the copy is private to the call), so turning the option off
    /// restores vanilla at the next start. A postfix on the struct method
    /// NPCStatBasedDropChanceModifier.ToReport was tried first and read garbage for __result and
    /// __instance (2026-10-10), which is why the hook sits on the class method instead.
    /// </summary>
    public static class ColdPartnerWarmthPatch
    {
        private static BepInEx.Logging.ManualLogSource Logger => Plugin.Logger;

        internal const string ExpressNeedWarmthDutyUid = "79526618b9d1cd348a6d463808118334";
        internal const string PartnerBodyTemperatureUid = "e42deda70e789cd45bb88d02ef986bde";
        internal const string ExpressNeedWarmthDialogName = "PartnerDialog_ExpressNeedWarmth";
        // "Looks very cold." is 1 to 50 on Partner_BodyTemperature; vanilla already covers -99 to 0.
        internal const float ColdBandMax = 50f;

        private static FieldInfo _currentWeightField;
        private static NPCStat _bodyTemperatureStat;
        private static bool _warnedStat;
        private static bool _warnedPatch;
        private static bool _warnedLogPatch;
        private static bool _warnedNoModifiers;
        private static bool _warnedNoBodyTempModifier;
        private static bool _recalculating;

        public static void ApplyPatch(Harmony harmony)
        {
            if (Plugin.ColdPartnerAsksForWarmth == null || !Plugin.ColdPartnerAsksForWarmth.Value)
            {
                Logger.LogDebug("ColdPartnerWarmthPatch not applied (option off).");
                return;
            }
            var calc = AccessTools.Method(typeof(NPCDutyRef), nameof(NPCDutyRef.CalculateWeight));
            var start = AccessTools.Method(typeof(StartDialogDutyAction), nameof(StartDialogDutyAction.StartDutyAction));
            _currentWeightField = AccessTools.Field(typeof(NPCDutyRef), "CurrentWeight");
            if (calc == null || start == null || _currentWeightField == null)
            {
                Logger.LogWarning($"[ColdPartnerWarmth] a target was not found (CalculateWeight {calc != null}, StartDutyAction {start != null}, CurrentWeight {_currentWeightField != null}); the option does nothing (game update?).");
                return;
            }
            harmony.Patch(calc, postfix: new HarmonyMethod(typeof(ColdPartnerWarmthPatch), nameof(CalculateWeight_Postfix)));
            harmony.Patch(start, prefix: new HarmonyMethod(typeof(ColdPartnerWarmthPatch), nameof(StartDutyAction_Prefix)));
            Logger.LogDebug("ColdPartnerWarmthPatch applied.");
        }

        // Typed parameters, never object. _ForNPC and _DebugAllConditions are the original's names.
        private static void CalculateWeight_Postfix(NPCDutyRef __instance, InGameNPC _ForNPC, bool _DebugAllConditions)
        {
            if (_recalculating || __instance == null || __instance.TargetDuty == null) return;
            if (__instance.TargetDuty.UniqueID != ExpressNeedWarmthDutyUid) return;
            try
            {
                // Vanilla did not cancel it: nothing to do.
                if (__instance.WeightInfo.SelectionHasNotBeenCancelled) return;
                if (_ForNPC == null || !_ForNPC.AlliedWithPlayer || !_ForNPC.CurrentEnvironment.MatchesPlayerEnv) return;
                if (_bodyTemperatureStat == null)
                    _bodyTemperatureStat = UniqueIDScriptable.GetFromID<NPCStat>(PartnerBodyTemperatureUid);
                if (_bodyTemperatureStat == null)
                {
                    if (!_warnedStat)
                    {
                        _warnedStat = true;
                        Logger.LogWarning("[ColdPartnerWarmth] Partner_BodyTemperature not resolvable - vanilla gate kept.");
                    }
                    return;
                }
                float value = _ForNPC.GetStatValue(_bodyTemperatureStat);
                if (value <= 0f || value > ColdBandMax) return;

                // A private copy of the weights: the struct copies, the array is cloned (its
                // elements are structs, so editing the clone never reaches the shared data).
                var weights = __instance.PreferenceWeights;
                var mods = weights.AgentStatsWeightModifiers;
                if (mods == null)
                {
                    if (!_warnedNoModifiers)
                    {
                        _warnedNoModifiers = true;
                        Logger.LogWarning("[ColdPartnerWarmth] ExpressNeedWarmth has no stat weight modifiers - vanilla gate kept.");
                    }
                    return;
                }
                var widened = (NPCStatBasedDropChanceModifier[])mods.Clone();
                bool found = false;
                for (int i = 0; i < widened.Length; i++)
                {
                    if (widened[i].Stat == null || widened[i].Stat.UniqueID != PartnerBodyTemperatureUid) continue;
                    var range = widened[i].StatRange;
                    widened[i].StatRange = new Vector2(range.x, Mathf.Max(range.y, ColdBandMax));
                    found = true;
                }
                if (!found)
                {
                    if (!_warnedNoBodyTempModifier)
                    {
                        _warnedNoBodyTempModifier = true;
                        Logger.LogWarning("[ColdPartnerWarmth] ExpressNeedWarmth has no Partner_BodyTemperature weight modifier - vanilla gate kept.");
                    }
                    return;
                }
                weights.AgentStatsWeightModifiers = widened;

                _recalculating = true;
                try
                {
                    var info = new NPCDutySelectionInfo(_ForNPC, __instance.TargetDuty);
                    info.PopulateReport(weights, _DebugAllConditions);
                    __instance.WeightInfo = info;
                    _currentWeightField.SetValue(__instance, (float)info.TotalWeight);
                }
                finally { _recalculating = false; }

                if (Plugin.EnableDiagnostics != null && Plugin.EnableDiagnostics.Value)
                    Logger.LogInfo($"[ColdPartnerWarmth] ExpressNeedWarmth gate widened for {_ForNPC.name} at body temperature {value:0.##}: notSelectable {__instance.WeightInfo.NotSelectable}, weight {__instance.WeightInfo.TotalWeight}.");
            }
            catch (Exception ex)
            {
                if (!_warnedPatch)
                {
                    _warnedPatch = true;
                    Logger.LogWarning($"[ColdPartnerWarmth] widening the body-temperature gate failed (vanilla gate kept): {ex}");
                }
            }
        }

        // One Info line each time the option made her ask: the dialog duty is starting for the
        // allied Partner, and her body temperature is in the band vanilla would have cancelled.
        private static void StartDutyAction_Prefix(StartDialogDutyAction __instance, InGameNPC _OnNPC)
        {
            try
            {
                if (__instance == null || __instance.Dialog == null || __instance.Dialog.name != ExpressNeedWarmthDialogName) return;
                if (_OnNPC == null || !_OnNPC.AlliedWithPlayer) return;
                if (_bodyTemperatureStat == null)
                    _bodyTemperatureStat = UniqueIDScriptable.GetFromID<NPCStat>(PartnerBodyTemperatureUid);
                if (_bodyTemperatureStat == null)
                {
                    if (!_warnedStat)
                    {
                        _warnedStat = true;
                        Logger.LogWarning("[ColdPartnerWarmth] Partner_BodyTemperature not resolvable - the ask is not logged.");
                    }
                    return;
                }
                float value = _OnNPC.GetStatValue(_bodyTemperatureStat);
                if (value > 0f && value <= ColdBandMax)
                    Logger.LogInfo($"[ColdPartnerWarmth] {_OnNPC.name} asked for warmth (body temperature {value:0.##}).");
            }
            catch (Exception ex)
            {
                if (!_warnedLogPatch)
                {
                    _warnedLogPatch = true;
                    Logger.LogWarning($"[ColdPartnerWarmth] logging the ask failed: {ex}");
                }
            }
        }
    }
}
