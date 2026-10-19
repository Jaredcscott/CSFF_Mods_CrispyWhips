namespace PartnerOverhaul.Patcher
{
    /// <summary>
    /// PO-C12 (plan section 3.2, R6 answered "yes, through the Partner's inventory"). The three vanilla
    /// expedition report scenes (PartnerDialog_ReportForaging / _ReportHunting / _ReportFishing) each hold
    /// ONE line ("Your partner has returned from a foraging expedition.") with ONE plain "Continue" answer
    /// that ends the dialog. This patch turns that answer's next step into a weighted pick over PO's own
    /// spoken follow-ups ("Found mushrooms past the stream."), copied from a never-shown router line per
    /// expedition (PO_PartnerTalk_Report_Forage / _Hunt / _Fish, ScriptableObject/DialogLine).
    ///
    /// No haul marker stat: the haul is dropped into the Partner's own inventory
    /// (Foraging Action ActionDroppedCards = InNPCInventory) and a weighted step scores
    /// CardWeightModifiers (OnlyInInventory) against NPC.AssociatedCard, which is the Partner. So the
    /// weights live in JSON and need no C# write. Vanilla's report line and its effects are untouched;
    /// only the Continue answer's routing changes.
    ///
    /// Why C# and not GameSourceModify (R1): the report lines are run-scoped DialogLines, absent at menu,
    /// so a GSM patch on them is skipped as unresolved. Same shape as PartnerTalkAnswerPatch: applied
    /// idempotently at every run boot (GameManager.InitializeStatsAndActions postfix) and again whenever
    /// a report scene is shown (DialogsPopup.Show prefix).
    ///
    /// Opt-in, default OFF (config "Expedition Stories"): it changes a Partner-started vanilla dialog.
    /// With the config off, or a router that does not resolve (CSFFModFramework absent or older than
    /// 2.15.1), a Continue answer this patch changed is put back to vanilla (no weights), so the report is
    /// exactly vanilla. Calls no CSFFModFramework member.
    /// </summary>
    public static class PartnerExpeditionStoryPatch
    {
        internal const string RouterPrefix = "PO_PartnerTalk_Report_";

        // Vanilla scene name -> PO router line name. Names come from the export (DialogScene/*.json).
        private static readonly string[][] SceneRouters =
        {
            new[] { "PartnerDialog_ReportForaging", "PO_PartnerTalk_Report_Forage" },
            new[] { "PartnerDialog_ReportHunting", "PO_PartnerTalk_Report_Hunt" },
            new[] { "PartnerDialog_ReportFishing", "PO_PartnerTalk_Report_Fish" },
        };

        private static BepInEx.Logging.ManualLogSource Logger => Plugin.Logger;
        private static readonly HashSet<string> Warned = new HashSet<string>();

        private static FieldInfo _useWeightsField;
        private static FieldInfo _weightsField;
        private static bool _reflectionResolved;
        private static bool _reflectionOk;

        // Router lookup: scanned at most once per run boot (reset in the boot postfix).
        private static readonly Dictionary<string, DialogLine> Routers = new Dictionary<string, DialogLine>();
        private static readonly HashSet<string> RoutersScanned = new HashSet<string>();

        public static void ApplyPatch(Harmony harmony)
        {
            var initStats = AccessTools.Method(typeof(GameManager), "InitializeStatsAndActions");
            if (initStats == null)
                Logger.LogWarning("[PartnerExpeditionStoryPatch] GameManager.InitializeStatsAndActions not found; the follow-ups are wired only when a report opens.");
            else
                harmony.Patch(initStats, postfix: new HarmonyMethod(typeof(PartnerExpeditionStoryPatch), nameof(InitStats_Postfix)));

            var show = AccessTools.Method(typeof(DialogsPopup), nameof(DialogsPopup.Show));
            if (show == null)
            {
                Logger.LogWarning("[PartnerExpeditionStoryPatch] DialogsPopup.Show not found; expedition follow-ups are not applied.");
                return;
            }
            harmony.Patch(show, prefix: new HarmonyMethod(typeof(PartnerExpeditionStoryPatch), nameof(Show_Prefix)));
            Logger.LogDebug("PartnerExpeditionStoryPatch applied.");
        }

        private static void WarnOnce(string cause, string detail)
        {
            if (Warned.Add(cause))
                Logger.LogWarning($"[PartnerExpeditionStoryPatch] {cause}: {detail} (further occurrences suppressed)");
        }

        private static void DebugOnce(string cause, string detail)
        {
            if (Warned.Add(cause))
                Logger.LogDebug($"[PartnerExpeditionStoryPatch] {cause}: {detail}");
        }

        private static bool ResolveReflection()
        {
            if (_reflectionResolved) return _reflectionOk;
            _reflectionResolved = true;
            const BindingFlags inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            _useWeightsField = typeof(DialogAnswer).GetField("UseWeightsForNextStep", inst);
            _weightsField = typeof(DialogAnswer).GetField("NextStepsWithWeights", inst);
            if (_useWeightsField == null) WarnOnce("UseWeightsForNextStep-field-missing", "DialogAnswer.UseWeightsForNextStep not found; expedition follow-ups are not applied");
            if (_weightsField == null) WarnOnce("NextStepsWithWeights-field-missing", "DialogAnswer.NextStepsWithWeights not found; expedition follow-ups are not applied");
            _reflectionOk = _useWeightsField != null && _weightsField != null;
            return _reflectionOk;
        }

        private static string RouterFor(string sceneName)
        {
            foreach (var pair in SceneRouters)
                if (pair[0] == sceneName) return pair[1];
            return null;
        }

        private static void InitStats_Postfix()
        {
            try
            {
                Routers.Clear();
                RoutersScanned.Clear();

                int seen = 0;
                foreach (var scene in Resources.FindObjectsOfTypeAll<DialogScene>())
                {
                    if (scene == null) continue;
                    string router = RouterFor(scene.name);
                    if (router == null) continue;
                    seen++;
                    EnsureScene(scene, router);
                }
                if (seen == 0)
                    DebugOnce("report-scenes-not-loaded-at-boot", "no expedition report scene loaded at run boot; the follow-ups are wired when a report opens");
            }
            catch (Exception ex)
            {
                WarnOnce("InitStats_Postfix-exception", ex.ToString());
            }
        }

        private static void Show_Prefix(DialogScene _Scene, InGameNPC _TalkingTo)
        {
            try
            {
                if (_Scene == null) return;
                string router = RouterFor(_Scene.name);
                if (router == null) return;
                LogHaul(_Scene, _TalkingTo);
                EnsureScene(_Scene, router);
            }
            catch (Exception ex)
            {
                WarnOnce("Show_Prefix-exception", ex.ToString());
            }
        }

        /// <summary>R6's unproven link (nothing empties the Partner's inventory before the Done step):
        /// with diagnostics on, log what the Partner carries when the report opens, so a harness run can
        /// confirm it. Read-only.</summary>
        private static void LogHaul(DialogScene scene, InGameNPC partner)
        {
            if (Plugin.EnableDiagnostics == null || !Plugin.EnableDiagnostics.Value) return;
            try
            {
                var card = partner != null ? partner.AssociatedCard : null;
                var slots = card != null ? card.CardsInInventory : null;
                if (slots == null)
                {
                    Logger.LogInfo($"[PartnerExpeditionStoryPatch] {scene.name}: the talked-to NPC has no readable inventory.");
                    return;
                }
                var names = new List<string>();
                foreach (var slot in slots)
                {
                    if (slot == null || slot.AllCards == null) continue;
                    foreach (var c in slot.AllCards)
                        if (c != null && c.CardModel != null) names.Add(c.CardModel.name);
                }
                Logger.LogInfo($"[PartnerExpeditionStoryPatch] {scene.name}: Partner inventory at the report = [{string.Join(", ", names.ToArray())}]");
            }
            catch (Exception ex)
            {
                WarnOnce("LogHaul-exception", ex.ToString());
            }
        }

        private static DialogLine FindRouter(string routerName)
        {
            DialogLine cached;
            if (Routers.TryGetValue(routerName, out cached) && cached != null) return cached; // Unity null check
            if (RoutersScanned.Contains(routerName)) return null;
            RoutersScanned.Add(routerName);
            foreach (var line in Resources.FindObjectsOfTypeAll<DialogLine>())
            {
                if (line != null && line.name == routerName)
                {
                    Routers[routerName] = line;
                    return line;
                }
            }
            return null;
        }

        /// <summary>True when <paramref name="answer"/> carries weights this patch set (its first step
        /// names a PO report router line). Keyed on the target name, not the text, so it survives a
        /// reload of the scene.</summary>
        private static bool IsOurs(DialogAnswer answer)
        {
            if (answer == null || _useWeightsField == null || _weightsField == null) return false;
            if (!(bool)_useWeightsField.GetValue(answer)) return false;
            var steps = (EndLineStepWithWeights[])_weightsField.GetValue(answer);
            if (steps == null || steps.Length == 0) return false;
            var first = steps[0].Line.NextLine;
            return first != null && first.name != null && first.name.StartsWith(RouterPrefix, StringComparison.Ordinal);
        }

        private static bool RouterUsable(DialogLine router, string routerName)
        {
            var steps = router != null ? router.NextStepsWithWeights : null;
            if (router == null)
            {
                DebugOnce("router-missing-" + routerName, $"DialogLine '{routerName}' not loaded (CSFFModFramework absent or older than 2.15.1?); the report stays vanilla");
                return false;
            }
            if (!router.UseWeightsForNextStep || steps == null || steps.Length == 0)
            {
                WarnOnce("router-empty-" + routerName, $"'{routerName}' has no weighted steps; the report stays vanilla");
                return false;
            }
            foreach (var s in steps)
            {
                if (s.Line.NextStep != DialogStep.MoveOnTo || s.Line.NextLine == null)
                {
                    WarnOnce("router-unresolved-step-" + routerName, $"'{routerName}' has a weighted step with no resolved line; the report stays vanilla");
                    return false;
                }
            }
            return true;
        }

        private static void EnsureScene(DialogScene scene, string routerName)
        {
            if (!ResolveReflection()) return;
            var lines = scene.SceneLines;
            if (lines == null)
            {
                WarnOnce("scene-lines-null-" + scene.name, $"{scene.name}.SceneLines is null; no follow-up is wired");
                return;
            }

            bool enabled = Plugin.ExpeditionStories != null && Plugin.ExpeditionStories.Value;
            DialogLine router = enabled ? FindRouter(routerName) : null;
            bool usable = enabled && RouterUsable(router, routerName);

            int touched = 0;
            foreach (var line in lines)
            {
                if (line == null || line.PossibleAnswers == null) continue;
                foreach (var answer in line.PossibleAnswers)
                {
                    if (answer == null) continue;
                    bool ours = IsOurs(answer);
                    if (usable)
                    {
                        // A vanilla answer that already routes somewhere (weights of its own) is left alone.
                        if ((bool)_useWeightsField.GetValue(answer) && !ours) continue;
                        _useWeightsField.SetValue(answer, true);
                        _weightsField.SetValue(answer, (EndLineStepWithWeights[])router.NextStepsWithWeights.Clone());
                        touched++;
                    }
                    else if (ours)
                    {
                        // Vanilla's Continue has no weights: put exactly that back.
                        _useWeightsField.SetValue(answer, false);
                        _weightsField.SetValue(answer, new EndLineStepWithWeights[0]);
                        touched++;
                    }
                }
            }
            if (touched > 0)
                Logger.LogDebug($"[PartnerExpeditionStoryPatch] {(usable ? "wired" : "removed")} the follow-ups on {scene.name} ({touched} answer(s)).");
        }
    }
}
