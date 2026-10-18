namespace PartnerOverhaul.Patcher
{
    /// <summary>
    /// PO-X1. Appends two "Talk about..." answers to PO's topic list (DialogLine PO_PartnerTalk_Topics),
    /// one for the Community Mod Chest village ("cmcEnvVillage") and one for its Inn ("cmcInnInterior"),
    /// each gated on RequiredEnvironment for the resolved room card plus a PO heard-topic marker.
    ///
    /// Soft dependency (CLAUDE.md, memory project_soft_dep_doctrine): PO declares no dependency on CMC
    /// and reads no CMC stat. The answers are built here, not in JSON, because a JSON answer naming a CMC
    /// UID would leave an unresolved WarpData reference when CMC is absent. An answer is added only when
    /// its environment resolves by GetFromID, together with the PO line it leads to and the PO marker
    /// stat; a stale answer is removed again when the environment no longer resolves (CMC removed,
    /// Chinese or renamed UID). With no CMC the topic list is exactly the JSON file's.
    ///
    /// Runs at every run boot (GameManager.InitializeStatsAndActions postfix) and again when PO's talk
    /// scene is shown, idempotently (the PartnerTalkAnswerPatch pattern). Calls no CSFFModFramework member.
    /// </summary>
    public static class PartnerCmcTopicPatch
    {
        internal const string TalkSceneName = "PO_PartnerTalk";
        internal const string TopicsLineName = "PO_PartnerTalk_Topics";
        private const string DonorLineName = "PO_PartnerTalk_Topic_Garden";

        private sealed class Topic
        {
            public string EnvUid;
            public string LineName;
            public string StatUid;
            public string AnswerKey;
            public string AnswerDefault;
        }

        private static readonly Topic[] Topics =
        {
            new Topic { EnvUid = "cmcEnvVillage", LineName = "PO_PartnerTalk_Topic_Village", StatUid = "poStatTopicHeardVillage",
                        AnswerKey = "PartnerOverhaul_Talk_Topic_Village_Answer", AnswerDefault = "The village" },
            new Topic { EnvUid = "cmcInnInterior", LineName = "PO_PartnerTalk_Topic_Inn", StatUid = "poStatTopicHeardInn",
                        AnswerKey = "PartnerOverhaul_Talk_Topic_Inn_Answer", AnswerDefault = "The Inn" },
        };

        private static BepInEx.Logging.ManualLogSource Logger => Plugin.Logger;
        private static readonly HashSet<string> Warned = new HashSet<string>();

        private static FieldInfo _conditionsField;
        private static FieldInfo _newNextLineField;
        private static FieldInfo _answerField;
        private static MethodInfo _memberwiseClone;
        private static bool _reflectionResolved;
        private static bool _reflectionOk;

        public static void ApplyPatch(Harmony harmony)
        {
            var initStats = AccessTools.Method(typeof(GameManager), "InitializeStatsAndActions");
            if (initStats == null)
                Logger.LogWarning("[PartnerCmcTopicPatch] GameManager.InitializeStatsAndActions not found; the village and Inn topics are added only when the Talk scene opens.");
            else
                harmony.Patch(initStats, postfix: new HarmonyMethod(typeof(PartnerCmcTopicPatch), nameof(InitStats_Postfix)));

            harmony.Patch(
                AccessTools.Method(typeof(DialogsPopup), nameof(DialogsPopup.Show)),
                prefix: new HarmonyMethod(typeof(PartnerCmcTopicPatch), nameof(Show_Prefix)));
            Logger.LogDebug("PartnerCmcTopicPatch applied.");
        }

        private static void WarnOnce(string cause, string detail)
        {
            if (Warned.Add(cause))
                Logger.LogWarning($"[PartnerCmcTopicPatch] {cause}: {detail} (further occurrences suppressed)");
        }

        private static void DebugOnce(string cause, string detail)
        {
            if (Warned.Add(cause))
                Logger.LogDebug($"[PartnerCmcTopicPatch] {cause}: {detail}");
        }

        private static bool ResolveReflection()
        {
            if (_reflectionResolved) return _reflectionOk;
            _reflectionResolved = true;
            const BindingFlags inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            _conditionsField = typeof(DialogAnswer).GetField("Conditions", inst);
            _newNextLineField = typeof(DialogAnswer).GetField("NewNextLine", inst);
            _answerField = typeof(DialogAnswer).GetField("Answer", inst);
            _memberwiseClone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_conditionsField == null) WarnOnce("Conditions-field-missing", "DialogAnswer.Conditions not found; the village and Inn topics are not added");
            if (_newNextLineField == null) WarnOnce("NewNextLine-field-missing", "DialogAnswer.NewNextLine not found; the village and Inn topics are not added");
            if (_answerField == null) WarnOnce("Answer-field-missing", "DialogAnswer.Answer not found; the village and Inn topics are not added");
            if (_memberwiseClone == null) WarnOnce("MemberwiseClone-missing", "object.MemberwiseClone not found; the village and Inn topics are not added");
            _reflectionOk = _conditionsField != null && _newNextLineField != null && _answerField != null && _memberwiseClone != null;
            return _reflectionOk;
        }

        private static void InitStats_Postfix()
        {
            try
            {
                foreach (var line in Resources.FindObjectsOfTypeAll<DialogLine>())
                {
                    if (line == null || line.name != TopicsLineName) continue;
                    Ensure(line);
                }
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
                if (_Scene == null || _Scene.name != TalkSceneName) return;
                var lines = _Scene.SceneLines;
                if (lines == null) return;
                foreach (var line in lines)
                {
                    if (line == null || line.name != TopicsLineName) continue;
                    Ensure(line);
                }
            }
            catch (Exception ex)
            {
                WarnOnce("Show_Prefix-exception", ex.ToString());
            }
        }

        private static string NextLineName(DialogAnswer answer)
        {
            if (answer == null || _newNextLineField == null) return null;
            var step = (EndLineStep)_newNextLineField.GetValue(answer);
            return step.NextStep == DialogStep.MoveOnTo && step.NextLine != null ? step.NextLine.name : null;
        }

        private static DialogLine FindLine(string name)
        {
            foreach (var line in Resources.FindObjectsOfTypeAll<DialogLine>())
                if (line != null && line.name == name) return line;
            return null;
        }

        private static void Ensure(DialogLine hub)
        {
            if (!ResolveReflection()) return;
            var answers = (hub.PossibleAnswers ?? new DialogAnswer[0]).ToList();
            bool changed = false;

            foreach (var topic in Topics)
            {
                int ours = answers.FindIndex(a => NextLineName(a) == topic.LineName);
                var env = UniqueIDScriptable.GetFromID<CardData>(topic.EnvUid);
                var stat = env != null ? UniqueIDScriptable.GetFromID<NPCStat>(topic.StatUid) : null;
                var next = env != null ? FindLine(topic.LineName) : null;

                if (env == null || stat == null || next == null)
                {
                    if (env == null) DebugOnce(topic.EnvUid + "-absent", $"'{topic.EnvUid}' does not resolve (Community Mod Chest not installed); no topic added");
                    else if (stat == null) WarnOnce(topic.StatUid + "-missing", $"NPCStat '{topic.StatUid}' did not resolve; the '{topic.EnvUid}' topic is not added");
                    else WarnOnce(topic.LineName + "-missing", $"DialogLine '{topic.LineName}' not loaded; the '{topic.EnvUid}' topic is not added");
                    if (ours >= 0)
                    {
                        answers.RemoveAt(ours);
                        changed = true;
                        Logger.LogDebug($"[PartnerCmcTopicPatch] removed the '{topic.EnvUid}' topic.");
                    }
                    continue;
                }

                DialogAnswer built;
                if (ours >= 0)
                {
                    built = answers[ours];
                    var cond = (GeneralCondition)_conditionsField.GetValue(built);
                    if (cond.RequiredEnvironment == env) continue; // already live
                    cond.RequiredEnvironment = env;
                    _conditionsField.SetValue(built, cond);
                    continue;
                }

                var donor = answers.FirstOrDefault(a => NextLineName(a) == DonorLineName);
                if (donor == null)
                {
                    WarnOnce("donor-missing", $"'{TopicsLineName}' has no answer into '{DonorLineName}' to copy from; the '{topic.EnvUid}' topic is not added");
                    continue;
                }
                built = (DialogAnswer)_memberwiseClone.Invoke(donor, null);
                var conditions = (GeneralCondition)_conditionsField.GetValue(donor);
                conditions.RequiredEnvironment = env;
                conditions.RequiredCardsOnBoard = null; // the donor's Garden Plot gate does not apply
                conditions.RequiredNPCStatValues = new[]
                {
                    new NPCStatCondition { UseAssociatedAgent = true, TargetStat = stat, ConditionRange = new Vector2(0f, 0f) }
                };
                _conditionsField.SetValue(built, conditions);
                _answerField.SetValue(built, new LocalizedString { LocalizationKey = topic.AnswerKey, DefaultText = topic.AnswerDefault });
                _newNextLineField.SetValue(built, new EndLineStep { NextStep = DialogStep.MoveOnTo, NextLine = next });

                // Before the memory answers and Step back: after the last topic answer (the Snow group).
                int at = answers.FindLastIndex(a => (NextLineName(a) ?? "").StartsWith("PO_PartnerTalk_Topic_", StringComparison.Ordinal));
                answers.Insert(at >= 0 ? at + 1 : answers.Count, built);
                changed = true;
                Logger.LogDebug($"[PartnerCmcTopicPatch] added the '{topic.EnvUid}' topic to '{TopicsLineName}'.");
            }

            if (changed) hub.PossibleAnswers = answers.ToArray();
        }
    }
}
