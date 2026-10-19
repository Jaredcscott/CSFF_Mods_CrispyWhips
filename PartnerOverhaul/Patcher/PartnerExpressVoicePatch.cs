namespace PartnerOverhaul.Patcher
{
    /// <summary>
    /// PO-C8 (plan section 3.2, decision Q-T6). The Partner starts a conversation itself only through nine
    /// vanilla PartnerDuty_Express* duties; each opens a vanilla scene whose line is narration ("Your
    /// partner approaches you and expresses the urgent need for food."). This patch keeps that line and its
    /// ActionToPerform exactly as vanilla wrote them and turns the line's Continue answer into a weighted
    /// pick over PO spoken follow-ups (PO_PartnerExpress_&lt;Kind&gt;_&lt;Tier&gt;_&lt;Voice&gt;), chosen by the PO-C3
    /// personality voice (exact poStatVoice* markers, then the fuzzy stat fingerprints) and a PO-C2 mood band
    /// (Stressed for the four needs, Happy for the three warm remarks). The weights are authored in JSON on
    /// a never-shown router line per kind (PO_PartnerExpress_Router_&lt;Kind&gt;), the shape of PO-C12.
    ///
    /// The follow-ups carry NO ActionToPerform and the router steps never set OverrideAction, so vanilla's
    /// stat changes happen once, on the vanilla line. Each follow-up's one answer is given, at wire time, a
    /// copy of the vanilla Continue's own next step, so after the follow-up the dialog goes where vanilla's
    /// Continue went.
    ///
    /// Deliberate injection into vanilla data (CLAUDE.md "Vanilla Data Protection"), scoped to the nine
    /// lines named below and found by object name: the pass stops unless every one of them is loaded exactly
    /// once. Of the nine, Express Blame (owner tile 2 A, 2026-10-15: Blame stays as vanilla wrote it) and
    /// Express Ultimatum (its answers decide whether the Partner leaves) have no Continue answer and are
    /// never written. Before the first write to a Continue answer its two fields are snapshotted; with the
    /// option off (or a router that does not resolve) every snapshotted answer gets its original values
    /// back. Idempotent: run at every run boot (GameManager.InitializeStatsAndActions postfix) and whenever
    /// an Express scene is shown (DialogsPopup.Show prefix), the same values are written again.
    ///
    /// Behind the PO-C6 config "Partner Starts Conversations" (default OFF, owner tile 4 A). Calls no
    /// CSFFModFramework member.
    /// </summary>
    public static class PartnerExpressVoicePatch
    {
        internal const string FollowUpPrefix = "PO_PartnerExpress_";
        internal const string RouterPrefix = "PO_PartnerExpress_Router_";

        // The nine vanilla Express narration lines, by object name (export ScriptableObjectObjectName/DialogLine.txt).
        internal static readonly string[] NineExpressLines =
        {
            "Express Affection",
            "Express Attraction",
            "Express Blame",
            "Express Gratitude",
            "Express Need Attention",
            "Express Need Food",
            "Express Need Trust",
            "Express Need Warmth",
            "Express Ultimatum",
        };

        // The nine vanilla Express scenes the duties open (export DialogScene/PartnerDialog_Express*.json).
        internal static readonly string[] NineExpressScenes =
        {
            "PartnerDialog_ExpressAffection",
            "PartnerDialog_ExpressAttraction",
            "PartnerDialog_ExpressBlame",
            "PartnerDialog_ExpressGratitude",
            "PartnerDialog_ExpressNeedAttention",
            "PartnerDialog_ExpressNeedFood",
            "PartnerDialog_ExpressNeedTrust",
            "PartnerDialog_ExpressNeedWarmth",
            "PartnerDialog_ExpressUltimatum",
        };

        // Vanilla line -> PO router line, for the seven lines that end on one plain Continue.
        private static readonly string[][] VoicedLines =
        {
            new[] { "Express Need Food", "PO_PartnerExpress_Router_NeedFood" },
            new[] { "Express Need Warmth", "PO_PartnerExpress_Router_NeedWarmth" },
            new[] { "Express Need Attention", "PO_PartnerExpress_Router_NeedAttention" },
            new[] { "Express Need Trust", "PO_PartnerExpress_Router_NeedTrust" },
            new[] { "Express Affection", "PO_PartnerExpress_Router_Affection" },
            new[] { "Express Attraction", "PO_PartnerExpress_Router_Attraction" },
            new[] { "Express Gratitude", "PO_PartnerExpress_Router_Gratitude" },
        };

        private sealed class Snapshot
        {
            public DialogAnswer Answer;
            public string LineName;
            public bool UseWeights;
            public EndLineStepWithWeights[] Weights;
        }

        private sealed class RefEq : IEqualityComparer<DialogAnswer>
        {
            public bool Equals(DialogAnswer a, DialogAnswer b) => ReferenceEquals(a, b);
            public int GetHashCode(DialogAnswer a) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(a);
        }

        private static BepInEx.Logging.ManualLogSource Logger => Plugin.Logger;
        private static readonly HashSet<string> Warned = new HashSet<string>();
        private static readonly Dictionary<DialogAnswer, Snapshot> Snapshots = new Dictionary<DialogAnswer, Snapshot>(new RefEq());

        private static FieldInfo _useWeightsField;
        private static FieldInfo _weightsField;
        private static FieldInfo _newNextLineField;
        private static bool _reflectionResolved;
        private static bool _reflectionOk;

        public static void ApplyPatch(Harmony harmony)
        {
            var initStats = AccessTools.Method(typeof(GameManager), "InitializeStatsAndActions");
            if (initStats == null)
                Logger.LogWarning("[PartnerExpressVoicePatch] GameManager.InitializeStatsAndActions not found; the Express follow-ups are wired only when an Express scene opens.");
            else
                harmony.Patch(initStats, postfix: new HarmonyMethod(typeof(PartnerExpressVoicePatch), nameof(InitStats_Postfix)));

            var show = AccessTools.Method(typeof(DialogsPopup), nameof(DialogsPopup.Show));
            if (show == null)
            {
                Logger.LogWarning("[PartnerExpressVoicePatch] DialogsPopup.Show not found; Express follow-ups are not applied.");
                return;
            }
            harmony.Patch(show, prefix: new HarmonyMethod(typeof(PartnerExpressVoicePatch), nameof(Show_Prefix)));
            Logger.LogDebug("PartnerExpressVoicePatch applied.");
        }

        private static void WarnOnce(string cause, string detail)
        {
            if (Warned.Add(cause))
                Logger.LogWarning($"[PartnerExpressVoicePatch] {cause}: {detail} (further occurrences suppressed)");
        }

        private static void DebugOnce(string cause, string detail)
        {
            if (Warned.Add(cause))
                Logger.LogDebug($"[PartnerExpressVoicePatch] {cause}: {detail}");
        }

        private static bool ResolveReflection()
        {
            if (_reflectionResolved) return _reflectionOk;
            _reflectionResolved = true;
            const BindingFlags inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            _useWeightsField = typeof(DialogAnswer).GetField("UseWeightsForNextStep", inst);
            _weightsField = typeof(DialogAnswer).GetField("NextStepsWithWeights", inst);
            _newNextLineField = typeof(DialogAnswer).GetField("NewNextLine", inst);
            if (_useWeightsField == null) WarnOnce("UseWeightsForNextStep-field-missing", "DialogAnswer.UseWeightsForNextStep not found; the Express dialogs stay vanilla");
            if (_weightsField == null) WarnOnce("NextStepsWithWeights-field-missing", "DialogAnswer.NextStepsWithWeights not found; the Express dialogs stay vanilla");
            if (_newNextLineField == null) WarnOnce("NewNextLine-field-missing", "DialogAnswer.NewNextLine not found; the Express dialogs stay vanilla");
            _reflectionOk = _useWeightsField != null && _weightsField != null && _newNextLineField != null;
            return _reflectionOk;
        }

        private static bool IsExpressScene(string sceneName) => Array.IndexOf(NineExpressScenes, sceneName) >= 0;

        private static void InitStats_Postfix()
        {
            try { ApplyPass("boot"); }
            catch (Exception ex) { WarnOnce("InitStats_Postfix-exception", ex.ToString()); }
        }

        private static void Show_Prefix(DialogScene _Scene)
        {
            try
            {
                if (_Scene == null || !IsExpressScene(_Scene.name)) return;
                ApplyPass(_Scene.name);
            }
            catch (Exception ex)
            {
                WarnOnce("Show_Prefix-exception", ex.ToString());
            }
        }

        /// <summary>Puts every Continue answer this patch changed back to its snapshotted vanilla values.</summary>
        private static void RestoreAll(string why)
        {
            if (Snapshots.Count == 0) return;
            int restored = 0;
            foreach (var snap in Snapshots.Values.ToList())
            {
                _useWeightsField.SetValue(snap.Answer, snap.UseWeights);
                _weightsField.SetValue(snap.Answer, snap.Weights);
                restored++;
            }
            Snapshots.Clear();
            Logger.LogDebug($"[PartnerExpressVoicePatch] restored {restored} vanilla Express Continue answer(s) ({why}).");
        }

        private static void Restore(DialogAnswer answer, string why)
        {
            Snapshot snap;
            if (answer == null || !Snapshots.TryGetValue(answer, out snap)) return;
            _useWeightsField.SetValue(snap.Answer, snap.UseWeights);
            _weightsField.SetValue(snap.Answer, snap.Weights);
            Snapshots.Remove(answer);
            Logger.LogDebug($"[PartnerExpressVoicePatch] restored the vanilla Continue on '{snap.LineName}' ({why}).");
        }

        private static void ApplyPass(string trigger)
        {
            if (!ResolveReflection()) return;

            bool enabled = Plugin.PartnerStartsConversations != null && Plugin.PartnerStartsConversations.Value;
            if (!enabled)
            {
                RestoreAll("option off");
                return;
            }

            // One scan: the nine vanilla lines (each exactly once) and the PO lines the routers need.
            var vanilla = new Dictionary<string, List<DialogLine>>();
            var po = new Dictionary<string, DialogLine>();
            foreach (var line in Resources.FindObjectsOfTypeAll<DialogLine>())
            {
                if (line == null || line.name == null) continue;
                if (Array.IndexOf(NineExpressLines, line.name) >= 0)
                {
                    List<DialogLine> list;
                    if (!vanilla.TryGetValue(line.name, out list)) vanilla[line.name] = list = new List<DialogLine>();
                    list.Add(line);
                }
                else if (line.name.StartsWith(FollowUpPrefix, StringComparison.Ordinal))
                {
                    po[line.name] = line;
                }
            }

            int found = vanilla.Values.Sum(l => l.Count);
            if (found == 0)
            {
                DebugOnce("express-lines-not-loaded-" + trigger, $"no Express line loaded at {trigger}; the follow-ups are wired when an Express scene opens");
                return;
            }
            if (found != NineExpressLines.Length || vanilla.Count != NineExpressLines.Length)
            {
                var missing = NineExpressLines.Where(n => !vanilla.ContainsKey(n)).ToArray();
                var doubled = vanilla.Where(kv => kv.Value.Count > 1).Select(kv => kv.Key).ToArray();
                WarnOnce("express-line-count-" + found,
                    $"found {found} Express lines, wanted exactly {NineExpressLines.Length} (missing: [{string.Join(", ", missing)}], more than once: [{string.Join(", ", doubled)}]); this pass changes nothing and puts back any answer it changed (game update?)");
                RestoreAll("line count not nine");
                return;
            }

            int wired = 0;
            foreach (var pair in VoicedLines)
            {
                var line = vanilla[pair[0]][0];
                if (EnsureLine(line, pair[1], po)) wired++;
            }
            if (wired > 0)
                Logger.LogDebug($"[PartnerExpressVoicePatch] wired {wired} Express Continue answer(s) at {trigger}.");
        }

        private static bool RouterUsable(DialogLine router, string routerName, Dictionary<string, DialogLine> po)
        {
            if (router == null)
            {
                DebugOnce("router-missing-" + routerName, $"DialogLine '{routerName}' not loaded (CSFFModFramework absent or older than 2.15.1?); that Express stays vanilla");
                return false;
            }
            var steps = router.NextStepsWithWeights;
            if (!router.UseWeightsForNextStep || steps == null || steps.Length == 0)
            {
                WarnOnce("router-empty-" + routerName, $"'{routerName}' has no weighted steps; that Express stays vanilla");
                return false;
            }
            foreach (var s in steps)
            {
                if (s.Line.NextStep != DialogStep.MoveOnTo || s.Line.NextLine == null)
                {
                    WarnOnce("router-unresolved-step-" + routerName, $"'{routerName}' has a weighted step with no resolved line; that Express stays vanilla");
                    return false;
                }
                if (s.OverrideAction)
                {
                    // An override would replace the vanilla Continue's own action.
                    WarnOnce("router-override-action-" + routerName, $"'{routerName}' has a step that overrides the answer's action; that Express stays vanilla");
                    return false;
                }
                var target = s.Line.NextLine;
                if (target.name == null || !target.name.StartsWith(FollowUpPrefix, StringComparison.Ordinal) || target.PossibleAnswers == null || target.PossibleAnswers.Length != 1 || target.PossibleAnswers[0] == null)
                {
                    WarnOnce("router-bad-follow-up-" + routerName, $"'{routerName}' routes to '{target.name}', which is not a PO follow-up with exactly one answer; that Express stays vanilla");
                    return false;
                }
            }
            return true;
        }

        private static bool EnsureLine(DialogLine line, string routerName, Dictionary<string, DialogLine> po)
        {
            var answers = line.PossibleAnswers;
            if (answers == null || answers.Length != 1 || answers[0] == null)
            {
                WarnOnce("not-one-answer-" + line.name, $"'{line.name}' does not end on exactly one answer; it stays vanilla (game update?)");
                return false;
            }
            var answer = answers[0];

            DialogLine router;
            po.TryGetValue(routerName, out router);
            if (!RouterUsable(router, routerName, po))
            {
                Restore(answer, "router unusable");
                return false;
            }

            if (!Snapshots.ContainsKey(answer))
            {
                // A vanilla Continue that already routes by weights is not the plain Continue this was built for.
                if ((bool)_useWeightsField.GetValue(answer))
                {
                    WarnOnce("continue-already-weighted-" + line.name, $"'{line.name}' Continue already uses weights; it stays vanilla (game update?)");
                    return false;
                }
                // Snapshot BEFORE the first write.
                Snapshots[answer] = new Snapshot
                {
                    Answer = answer,
                    LineName = line.name,
                    UseWeights = (bool)_useWeightsField.GetValue(answer),
                    Weights = (EndLineStepWithWeights[])_weightsField.GetValue(answer),
                };
            }

            // Each follow-up continues where vanilla's Continue went (its own NewNextLine is never written).
            object vanillaNext = _newNextLineField.GetValue(answer);
            foreach (var s in router.NextStepsWithWeights)
            {
                var followAnswer = s.Line.NextLine.PossibleAnswers[0];
                _newNextLineField.SetValue(followAnswer, vanillaNext);
            }

            _useWeightsField.SetValue(answer, true);
            _weightsField.SetValue(answer, (EndLineStepWithWeights[])router.NextStepsWithWeights.Clone());
            return true;
        }
    }
}
