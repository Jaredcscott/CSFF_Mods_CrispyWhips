namespace PartnerOverhaul.Patcher
{
    /// <summary>
    /// PO-C1. Appends one answer, "Talk with &lt;name&gt;", to vanilla's Partner Talk hub line
    /// (DialogLine "Expression Hub" in scene "PartnerDialog_Expression"); choosing it changes scene
    /// into PO's own "PO_PartnerTalk" (ScriptableObject/DialogScene + DialogLine JSON, loaded by
    /// CSFFModFramework).
    ///
    /// Why C# and not a GameSourceModify patch (plan section 3.1, R1): the hub is a run-scoped UI
    /// ScriptableObject, absent from the framework's pre-menu object snapshot, so a GSM patch on it
    /// is skipped as unresolved. So the answer is added here, idempotently (check-before-add) at
    /// every run boot (GameManager.InitializeStatsAndActions postfix, the CMC StatTabInjectionPatch
    /// pattern) and again, as a safety net for a lazily loaded hub, whenever the hub's scene is
    /// shown (DialogsPopup.Show prefix).
    ///
    /// Soft dependency: when PO_PartnerTalk does not resolve (framework absent, or too old to fill
    /// dialog WarpData, below 2.15.1) the answer is NOT added, and one already added is removed, so
    /// the hub is exactly vanilla. The same removal runs when the config option is off.
    ///
    /// The name: DialogAnswerButton.Setup prefix rewrites OUR answer's text each time it is shown,
    /// from the CSV template PartnerOverhaul_Talk_Answer_Named ("Talk with {0}") and the name the
    /// player chose (PartnerNameInDialogPatch.ResolvePartnerName); it writes an empty-key
    /// LocalizedString, which LocalizedString.ToString returns verbatim, so the button and the
    /// dialog log both read the name. No name chosen: PartnerOverhaul_Talk_Answer.
    /// Calls no CSFFModFramework member.
    /// </summary>
    public static class PartnerTalkAnswerPatch
    {
        internal const string HubSceneName = "PartnerDialog_Expression";
        internal const string HubLineName = "Expression Hub";
        internal const string TalkSceneName = "PO_PartnerTalk";
        internal const string MoodRouterName = "PO_PartnerTalk_Moods";
        internal const string AnswerKey = "PartnerOverhaul_Talk_Answer";
        internal const string AnswerNamedKey = "PartnerOverhaul_Talk_Answer_Named";
        private const string AnswerDefault = "Talk with your partner";
        private const string AnswerNamedDefault = "Talk with {0}";
        private const string DonorAnswerText = "Step Back";

        private static BepInEx.Logging.ManualLogSource Logger => Plugin.Logger;
        private static readonly HashSet<string> Warned = new HashSet<string>();

        private static FieldInfo _newNextLineField;
        private static FieldInfo _useWeightsField;
        private static FieldInfo _weightsField;
        private static FieldInfo _actionField;
        private static MethodInfo _memberwiseClone;
        private static bool _reflectionResolved;
        private static bool _reflectionOk;

        // PO_PartnerTalk lookup: scanned at most once per run boot (reset in the boot postfix);
        // Talk can open many times a run and the scene, framework-created at menu, does not move.
        private static DialogScene _talkScene;
        private static bool _talkSceneScanned;
        private static DialogLine _moodRouter;
        private static bool _moodRouterScanned;

        public static void ApplyPatch(Harmony harmony)
        {
            var initStats = AccessTools.Method(typeof(GameManager), "InitializeStatsAndActions");
            if (initStats == null)
                Logger.LogWarning("[PartnerTalkAnswerPatch] GameManager.InitializeStatsAndActions not found; the Talk answer is added only when the Talk menu opens.");
            else
                harmony.Patch(initStats, postfix: new HarmonyMethod(typeof(PartnerTalkAnswerPatch), nameof(InitStats_Postfix)));

            harmony.Patch(
                AccessTools.Method(typeof(DialogsPopup), nameof(DialogsPopup.Show)),
                prefix: new HarmonyMethod(typeof(PartnerTalkAnswerPatch), nameof(Show_Prefix)));
            harmony.Patch(
                AccessTools.Method(typeof(DialogAnswerButton), nameof(DialogAnswerButton.Setup)),
                prefix: new HarmonyMethod(typeof(PartnerTalkAnswerPatch), nameof(Setup_Prefix)));
            Logger.LogDebug("PartnerTalkAnswerPatch applied.");
        }

        private static void WarnOnce(string cause, string detail)
        {
            if (Warned.Add(cause))
                Logger.LogWarning($"[PartnerTalkAnswerPatch] {cause}: {detail} (further occurrences suppressed)");
        }

        private static void DebugOnce(string cause, string detail)
        {
            if (Warned.Add(cause))
                Logger.LogDebug($"[PartnerTalkAnswerPatch] {cause}: {detail}");
        }

        private static bool ResolveReflection()
        {
            if (_reflectionResolved) return _reflectionOk;
            _reflectionResolved = true;
            const BindingFlags inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            _newNextLineField = typeof(DialogAnswer).GetField("NewNextLine", inst);
            _useWeightsField = typeof(DialogAnswer).GetField("UseWeightsForNextStep", inst);
            _weightsField = typeof(DialogAnswer).GetField("NextStepsWithWeights", inst);
            _actionField = typeof(DialogAnswer).GetField("ActionToPerform", inst);
            _memberwiseClone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_newNextLineField == null) WarnOnce("NewNextLine-field-missing", "DialogAnswer.NewNextLine not found; the Talk answer is not added");
            if (_useWeightsField == null) WarnOnce("UseWeightsForNextStep-field-missing", "DialogAnswer.UseWeightsForNextStep not found; the Talk answer is not added");
            if (_weightsField == null) WarnOnce("NextStepsWithWeights-field-missing", "DialogAnswer.NextStepsWithWeights not found; the Talk answer is not added");
            if (_actionField == null) WarnOnce("ActionToPerform-field-missing", "DialogAnswer.ActionToPerform not found; the Talk answer is not added");
            if (_memberwiseClone == null) WarnOnce("MemberwiseClone-missing", "object.MemberwiseClone not found; the Talk answer is not added");
            _reflectionOk = _newNextLineField != null && _useWeightsField != null && _weightsField != null
                            && _actionField != null && _memberwiseClone != null;
            return _reflectionOk;
        }

        private static void InitStats_Postfix()
        {
            try
            {
                _talkScene = null;
                _talkSceneScanned = false;
                _moodRouter = null;
                _moodRouterScanned = false;

                int hubs = 0;
                foreach (var line in Resources.FindObjectsOfTypeAll<DialogLine>())
                {
                    if (line == null || line.name != HubLineName) continue;
                    hubs++;
                    EnsureAnswer(line);
                }
                if (hubs == 0)
                    DebugOnce("hub-not-loaded-at-boot", $"DialogLine '{HubLineName}' not loaded at run boot; the answer is added when the Talk menu opens");
            }
            catch (Exception ex)
            {
                WarnOnce("InitStats_Postfix-exception", ex.ToString());
            }
        }

        private static void Show_Prefix(DialogScene _Scene)
        {
            try
            {
                if (_Scene == null || _Scene.name != HubSceneName) return;
                var lines = _Scene.SceneLines;
                if (lines == null)
                {
                    WarnOnce("hub-scene-lines-null", $"{HubSceneName}.SceneLines is null; the Talk answer is not added");
                    return;
                }
                bool found = false;
                foreach (var line in lines)
                {
                    if (line == null || line.name != HubLineName) continue;
                    found = true;
                    EnsureAnswer(line);
                }
                if (!found)
                    WarnOnce("hub-line-missing", $"{HubSceneName} has no line named '{HubLineName}' (renamed by a game update?); the Talk answer is not added");
            }
            catch (Exception ex)
            {
                WarnOnce("Show_Prefix-exception", ex.ToString());
            }
        }

        private static DialogScene FindTalkScene()
        {
            if (_talkScene != null) return _talkScene; // Unity null check: a destroyed scene rescans
            if (_talkSceneScanned) return null;
            _talkSceneScanned = true;
            foreach (var scene in Resources.FindObjectsOfTypeAll<DialogScene>())
            {
                if (scene != null && scene.name == TalkSceneName)
                {
                    _talkScene = scene;
                    break;
                }
            }
            return _talkScene;
        }

        private static DialogLine FindMoodRouter()
        {
            if (_moodRouter != null) return _moodRouter;
            if (_moodRouterScanned) return null;
            _moodRouterScanned = true;
            foreach (var line in Resources.FindObjectsOfTypeAll<DialogLine>())
            {
                if (line != null && line.name == MoodRouterName)
                {
                    _moodRouter = line;
                    break;
                }
            }
            return _moodRouter;
        }

        /// <summary>
        /// PO-C2. Gives the Talk answer the mood-weighted next step. The weights live in JSON on the
        /// never-shown line PO_PartnerTalk_Moods (one opener per mood, each excluded outside its band by
        /// AgentStatsWeightModifiers); the answer copies that list, so DialogAnswer.PickNextLine scores it
        /// against TalkingTo (the Partner) when the button is clicked. Not resolved or incomplete: the
        /// answer keeps its plain ChangeDialogScene target (the scene's first line), exactly as in PO-C1.
        /// </summary>
        private static void ApplyMoodWeights(DialogAnswer answer)
        {
            var router = FindMoodRouter();
            var steps = router != null ? router.NextStepsWithWeights : null;
            bool usable = router != null && router.UseWeightsForNextStep && steps != null && steps.Length > 0;
            if (usable)
            {
                foreach (var s in steps)
                {
                    if (s.Line.NextStep != DialogStep.MoveOnTo || s.Line.NextLine == null) { usable = false; break; }
                }
                if (!usable)
                    WarnOnce("mood-router-unresolved-step", $"'{MoodRouterName}' has a weighted step with no resolved line (CSFFModFramework older than 2.15.1?); the Talk answer opens the plain first line");
            }
            else if (router == null)
                DebugOnce("mood-router-missing", $"DialogLine '{MoodRouterName}' not loaded; the Talk answer opens the plain first line");
            else
                WarnOnce("mood-router-empty", $"'{MoodRouterName}' has no weighted steps; the Talk answer opens the plain first line");

            if (usable)
            {
                _useWeightsField.SetValue(answer, true);
                _weightsField.SetValue(answer, (EndLineStepWithWeights[])steps.Clone());
            }
            else
            {
                _useWeightsField.SetValue(answer, false);
                _weightsField.SetValue(answer, new EndLineStepWithWeights[0]);
            }
        }

        /// <summary>True when <paramref name="answer"/> is the one this patch built (it changes
        /// scene into PO_PartnerTalk). Keyed on the target, not the text, because Setup_Prefix
        /// rewrites the text with the Partner's name.</summary>
        private static bool IsOurAnswer(DialogAnswer answer)
        {
            if (answer == null || _newNextLineField == null) return false;
            var step = (EndLineStep)_newNextLineField.GetValue(answer);
            return step.NextStep == DialogStep.ChangeDialogScene
                   && step.NextScene != null
                   && step.NextScene.name == TalkSceneName;
        }

        private static void EnsureAnswer(DialogLine hub)
        {
            if (!ResolveReflection()) return;

            var answers = hub.PossibleAnswers ?? new DialogAnswer[0];
            int ours = Array.FindIndex(answers, IsOurAnswer);

            bool enabled = Plugin.PartnerTalkAnswer == null || Plugin.PartnerTalkAnswer.Value;
            DialogScene talk = enabled ? FindTalkScene() : null;
            bool talkUsable = talk != null && talk.DefaultStartingLine != null;
            if (enabled && talk == null)
                DebugOnce("talk-scene-missing", $"DialogScene '{TalkSceneName}' not loaded (CSFFModFramework absent?); the Talk hub stays vanilla");
            else if (enabled && !talkUsable)
                WarnOnce("talk-scene-no-lines", $"DialogScene '{TalkSceneName}' loaded with no resolved lines (CSFFModFramework older than 2.15.1?); the Talk hub stays vanilla");

            if (!enabled || !talkUsable)
            {
                if (ours >= 0)
                {
                    hub.PossibleAnswers = answers.Where(a => !IsOurAnswer(a)).ToArray();
                    Logger.LogDebug("[PartnerTalkAnswerPatch] removed the Talk answer from the hub.");
                }
                return;
            }

            if (ours >= 0)
            {
                // Already there: keep its target pointed at the live scene object.
                var existing = (EndLineStep)_newNextLineField.GetValue(answers[ours]);
                if (existing.NextScene != talk)
                {
                    existing.NextScene = talk;
                    _newNextLineField.SetValue(answers[ours], existing);
                }
                ApplyMoodWeights(answers[ours]);
                return;
            }

            if (answers.Length == 0)
            {
                WarnOnce("hub-no-answers", $"'{HubLineName}' has no answers to copy from; the Talk answer is not added");
                return;
            }

            // Clone vanilla's own no-effect Step Back answer so every serialized field (Conditions,
            // text settings) starts as the engine expects, then overwrite what makes it ours.
            var donor = answers.FirstOrDefault(a => a != null && a.Answer.DefaultText == DonorAnswerText) ?? answers[0];
            if (donor == null)
            {
                WarnOnce("hub-donor-null", $"'{HubLineName}' answer 0 is null; the Talk answer is not added");
                return;
            }
            var built = (DialogAnswer)_memberwiseClone.Invoke(donor, null);
            built.Answer = new LocalizedString { LocalizationKey = AnswerKey, DefaultText = AnswerDefault };
            _newNextLineField.SetValue(built, new EndLineStep
            {
                NextStep = DialogStep.ChangeDialogScene,
                NextScene = talk,
                NextLine = null,
                DontClearHistory = false
            });
            ApplyMoodWeights(built);
            _actionField.SetValue(built, new DialogAction()); // no effect on the answer itself; the PO lines carry the cost

            var list = answers.ToList();
            list.Add(built);
            hub.PossibleAnswers = list.ToArray();
            Logger.LogDebug($"[PartnerTalkAnswerPatch] added the Talk answer to '{HubLineName}' ({list.Count} answers).");
        }

        private static void Setup_Prefix(DialogAnswer _Answer)
        {
            try
            {
                if (!IsOurAnswer(_Answer)) return;
                var popup = MBSingleton<DialogsPopup>.Instance;
                string name = popup != null ? PartnerNameInDialogPatch.ResolvePartnerName(popup.TalkingTo) : null;

                string text = null;
                if (name != null)
                {
                    string template = new LocalizedString { LocalizationKey = AnswerNamedKey, DefaultText = AnswerNamedDefault }.ToString();
                    try { text = string.Format(template, name); }
                    catch (FormatException ex)
                    {
                        WarnOnce("named-template-bad", $"'{AnswerNamedKey}' is not a valid format string ({ex.Message}); using the unnamed text");
                    }
                }
                if (string.IsNullOrEmpty(text))
                    text = new LocalizedString { LocalizationKey = AnswerKey, DefaultText = AnswerDefault }.ToString();

                // Empty key: LocalizedString.ToString returns DefaultText as written.
                _Answer.Answer = new LocalizedString { LocalizationKey = "", DefaultText = text };
            }
            catch (Exception ex)
            {
                WarnOnce("Setup_Prefix-exception", ex.ToString());
            }
        }
    }
}
