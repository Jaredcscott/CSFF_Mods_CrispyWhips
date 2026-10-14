using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Repeat_Action.Patcher
{
    /// <summary>
    /// Feature 20: record a sequence of actions and play it back, repeated by the current count (plan section 20).
    ///
    /// Recording appends every capture that sets <c>last</c>, so a sequence shares the capture's skip rules (rest-like,
    /// dialog, encounter, blocklist). Playing runs <see cref="RunSteps"/> over the recorded steps, so every step passes the
    /// same checks a one-action run's iteration does, and a step that cannot be dispatched stops the play naming it.
    ///
    /// The library survives quitting the game through one managed config entry (Saved Sequences), as plain strings
    /// only, never a live reference. What a step needs to be rebuilt after a relaunch was measured by Wave 0 finding
    /// W0.9 (plan "## Wave 0 findings", "Third run"): a card action or stack action needs the card's UniqueID and the
    /// action's key; a drag-drop the two UniqueIDs and the key of the CardInteraction, found on the receiving card's or
    /// the dragged card's CardData; a group action the container's UniqueID and the index of its group button
    /// (CardData.GroupInventoryActions), plus each member's UniqueID. Three kinds cannot be rebuilt from strings and are
    /// kept for the session only: an action with no card (Rest, a time option), a liquid pour (the game builds a new
    /// action for every drag), and an action the card itself does not carry (a popup-owned runtime action; blueprint
    /// Build is not one of these, it is never captured at all).
    /// Every rebuild reads loaded CardData, never the board, so it does not matter where the player stands; which card
    /// a rebuilt step acts on is decided by the dispatch, through the same ResolveCard (InPlayerEnv) a single run uses.
    /// </summary>
    public static partial class ActionPatch
    {
        // Non-null only while recording. Appended to by every capture that sets `last` (CaptureAction, the group capture).
        private static List<Captured> recording;
        // The saved form of each recorded step, read at capture time (a group's container may be gone by the time
        // recording stops); null for a step that cannot be saved, with the reason beside it.
        private static List<SavedStep> recordingSaved;
        private static string recordingUnsaveable;

        private static readonly List<Sequence> library = new List<Sequence>();
        private static int selectedSequence = -1;

        // The most saved sequences kept; recording one more drops the oldest saved one (with a log line).
        internal const int MaxSavedSequences = 10;

        public static bool IsRecording => recording != null;

        private sealed class Sequence
        {
            public string Name;
            public List<Captured> Steps;      // live, or null until rebuilt from Saved on first play
            public List<SavedStep> Saved;     // null when a step cannot be saved: the sequence lasts for this session only
            public int StepCount => Steps != null ? Steps.Count : (Saved != null ? Saved.Count : 0);
        }

        private sealed class SavedStep
        {
            public FunnelKind Kind;
            public string ActionKey;
            public string ActionName;
            public string ReceivingUid;
            public string GivenUid;
            public string GroupContainerUid;
            public int GroupDefIndex = -1;
            public List<string> GroupUids;
            public bool StackLiquids;
        }

        // =====================================================================
        // RECORDING
        // =====================================================================

        /// <summary>The Record Sequence Key: start recording, or stop and keep what was recorded.</summary>
        public static void ToggleRecording()
        {
            if (recording == null)
            {
                if (isRepeating)
                {
                    Plugin.ShowNotification(Messages.Text("A repeat is already running"));
                    return;
                }
                recording = new List<Captured>();
                recordingSaved = new List<SavedStep>();
                recordingUnsaveable = null;
                Plugin.ShowNotification(Messages.Text("Recording (step {0})", 1));
                Logger.LogInfo("[Sequence] Recording started");
                return;
            }

            var steps = recording;
            var saved = recordingSaved;
            string unsaveable = recordingUnsaveable;
            recording = null;
            recordingSaved = null;
            recordingUnsaveable = null;
            if (steps.Count == 0)
            {
                Plugin.ShowNotification(Messages.Text("Recording cancelled (no steps)"));
                Logger.LogInfo("[Sequence] Recording stopped with no steps; nothing kept");
                return;
            }

            var seq = new Sequence
            {
                Name = UniqueSequenceName(steps[0].ActionDisplay ?? steps[0].ActionName),
                Steps = steps,
                Saved = unsaveable == null ? saved : null,
            };
            library.Add(seq);
            selectedSequence = library.Count - 1;
            if (seq.Saved != null)
            {
                TrimSavedSequences();
                PersistLibrary();
                Plugin.ShowNotification(Messages.Text("Sequence saved: {0} ({1} steps)", seq.Name, steps.Count));
                Logger.LogInfo($"[Sequence] Saved '{seq.Name}' ({steps.Count} steps): {DescribeSteps(steps)}");
            }
            else
            {
                Plugin.ShowNotification(Messages.Text("Sequence kept for this session: {0} ({1} steps)", seq.Name, steps.Count));
                Logger.LogInfo($"[Sequence] Kept '{seq.Name}' ({steps.Count} steps) for this session only, not saved: {unsaveable}. Steps: {DescribeSteps(steps)}");
            }
        }

        /// <summary>A new game starting (a load, a new run) ends a recording in progress: one sequence never spans two games.</summary>
        private static void CancelRecordingForNewGame()
        {
            if (recording == null) return;
            Logger.LogInfo($"[Sequence] A new game started while recording ({recording.Count} step(s)): the recording is dropped");
            recording = null;
            recordingSaved = null;
            recordingUnsaveable = null;
        }

        /// <summary>Called right after a capture sets `last`: while recording, the same object becomes the next step.</summary>
        private static void RecordStep(Captured cap)
        {
            if (recording == null || cap == null) return;
            recording.Add(cap);
            string why;
            var saved = ToSavedStep(cap, out why);
            recordingSaved.Add(saved);
            if (saved == null && recordingUnsaveable == null)
                recordingUnsaveable = $"step {recording.Count} ({cap.ActionName}): {why}";
            Plugin.ShowNotification(Messages.Text("Recording (step {0})", recording.Count + 1));
            Logger.LogInfo($"[Sequence] Recorded step {recording.Count}: '{cap.ActionName}' ({cap.Kind}){(saved == null ? " - cannot be saved: " + why : "")}");
        }

        /// <summary>
        /// A drag-drop's liquid flag is set by its capture prefix after CaptureAction returns, so the saved form is
        /// re-checked once the prefix has finished: a pour cannot be saved.
        /// </summary>
        private static void RecordLiquidFlag(Captured cap)
        {
            if (recording == null || cap == null || !cap.IsLiquidTransfer) return;
            int i = recording.LastIndexOf(cap);
            if (i < 0) return;
            recordingSaved[i] = null;
            if (recordingUnsaveable == null) recordingUnsaveable = $"step {i + 1} ({cap.ActionName}): a liquid pour";
            Logger.LogInfo($"[Sequence] Step {i + 1} '{cap.ActionName}' is a liquid pour and cannot be saved");
        }

        /// <summary>The saved form of a capture, or null (with the reason) for a kind that cannot be rebuilt from strings.</summary>
        private static SavedStep ToSavedStep(Captured cap, out string why)
        {
            why = null;
            var s = new SavedStep
            {
                Kind = cap.Kind,
                ActionKey = cap.ActionKey,
                ActionName = cap.ActionName,
                ReceivingUid = cap.ReceivingUid,
                GivenUid = cap.GivenUid,
                StackLiquids = cap.StackLiquids,
            };
            try
            {
                switch (cap.Kind)
                {
                    case FunnelKind.Action:
                    case FunnelKind.StackAction:
                    {
                        if (string.IsNullOrEmpty(cap.ReceivingUid)) { why = "an action with no card"; return null; }
                        var model = ModelOf(cap.ReceivingUid);
                        bool keyed = !string.IsNullOrEmpty(cap.ActionKey) && cap.ActionKey != LocalizedString.IgnoreKey;
                        if (model == null || FindInActions(ToArray(model.DismantleActions), new Captured { Action = cap.Action, ActionKey = cap.ActionKey, ActionName = cap.ActionName }, allowNameMatch: !keyed) == null)
                        {
                            why = "an action the card itself does not carry";
                            return null;
                        }
                        return s;
                    }
                    case FunnelKind.CardOnCard:
                    {
                        if (cap.IsLiquidTransfer) { why = "a liquid pour"; return null; }
                        if (string.IsNullOrEmpty(cap.ReceivingUid) || string.IsNullOrEmpty(cap.GivenUid)) { why = "a drag-drop with a card missing"; return null; }
                        if (FindInteraction(cap.ReceivingUid, cap.GivenUid, cap.ActionKey, cap.ActionName, cap.Action) == null)
                        {
                            why = "a drag-drop the game builds for each drag";
                            return null;
                        }
                        // The rebuild finds it without the captured object; it must come back as the same interaction.
                        if (!ReferenceEquals(FindInteraction(cap.ReceivingUid, cap.GivenUid, cap.ActionKey, cap.ActionName, null), cap.Action))
                        {
                            why = "a drag-drop whose name or key is shared by another interaction of the two cards";
                            return null;
                        }
                        return s;
                    }
                    case FunnelKind.GroupAction:
                    {
                        var first = cap.GroupCards != null && cap.GroupCards.Count > 0 ? cap.GroupCards[0] : null;
                        var container = ContainerOf(first);
                        var defs = container != null ? container.CardModel.GroupInventoryActions : null;
                        int index = -1;
                        if (defs != null)
                            for (int i = 0; i < defs.Length; i++)
                                if (ReferenceEquals(defs[i].Action, cap.GroupDef.Action)
                                    && string.Equals(defs[i].ActionName.LocalizationKey, cap.GroupDef.ActionName.LocalizationKey, StringComparison.Ordinal))
                                { index = i; break; }
                        if (index < 0) { why = "a group whose container could not be read"; return null; }
                        s.GroupContainerUid = UidOf(container);
                        s.GroupDefIndex = index;
                        s.GroupUids = new List<string>(cap.GroupUids);
                        return s;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"[Sequence] Reading the saved form of '{cap.ActionName}' failed: {ex}");
            }
            why = "unreadable";
            return null;
        }

        // =====================================================================
        // PLAYING AND CHOOSING
        // =====================================================================

        /// <summary>The Next Sequence Key: choose which recorded sequence the Play Sequence Key plays.</summary>
        public static void SelectNextSequence()
        {
            if (library.Count == 0)
            {
                Plugin.ShowNotification(Messages.Text("No sequences recorded"));
                return;
            }
            selectedSequence = (selectedSequence + 1) % library.Count;
            var seq = library[selectedSequence];
            Plugin.ShowNotification(Messages.Text("Sequence: {0} ({1} steps)", seq.Name, seq.StepCount));
            Logger.LogInfo($"[Sequence] Selected '{seq.Name}' ({seq.StepCount} steps, {(seq.Saved != null ? "saved" : "this session only")})");
        }

        /// <summary>The Play Sequence Key: play the selected sequence with the current count.</summary>
        public static IEnumerator PlaySelectedSequence(int count)
        {
            if (library.Count == 0 || selectedSequence < 0 || selectedSequence >= library.Count)
            {
                Plugin.ShowNotification(Messages.Text("No sequences recorded"));
                yield break;
            }
            var seq = library[selectedSequence];
            if (seq.Steps == null)
            {
                // Rebuilt once, on first play, from the saved strings: a step that cannot be rebuilt stops the play
                // naming it, before anything is dispatched, and the next play tries again.
                var built = new List<Captured>(seq.Saved.Count);
                for (int i = 0; i < seq.Saved.Count; i++)
                {
                    var c = RebuildStep(seq.Saved[i], out string why);
                    if (c == null)
                    {
                        string reason = Messages.En("step {0} of {1} ({2}): {3}", i + 1, seq.Saved.Count, seq.Saved[i].ActionName, Messages.En("cannot be rebuilt"));
                        string progress = count <= Plugin.UnlimitedCount ? "0" : $"0/{count}";
                        Plugin.ShowNotification(Messages.Text("Stopped - {0} ({1})", Messages.Shown(reason), progress));
                        Logger.LogInfo($"[Repeat] Stopped after {progress}: {reason} ({why})");
                        yield break;
                    }
                    built.Add(c);
                }
                seq.Steps = built;
                Logger.LogInfo($"[Sequence] Rebuilt '{seq.Name}' from the saved library: {DescribeSteps(built)}");
            }
            var run = RunSteps(seq.Steps, count, false, seq.Name);
            while (run.MoveNext()) yield return run.Current;
        }

        /// <summary>
        /// A step from its saved strings, every reference read from loaded CardData (W0.9's call sequence). Cards are
        /// left null: the dispatch resolves them by UniqueID in the player's environment, as it does for a single run.
        /// </summary>
        private static Captured RebuildStep(SavedStep s, out string why)
        {
            why = null;
            try
            {
                var cap = new Captured
                {
                    Kind = s.Kind,
                    ActionKey = s.ActionKey,
                    ActionName = s.ActionName,
                    ReceivingUid = s.ReceivingUid,
                    GivenUid = s.GivenUid,
                    StackLiquids = s.StackLiquids,
                };
                switch (s.Kind)
                {
                    case FunnelKind.Action:
                    case FunnelKind.StackAction:
                    {
                        var model = ModelOf(s.ReceivingUid);
                        if (model == null) { why = $"no card '{s.ReceivingUid}' is loaded"; return null; }
                        cap.Action = FindInActions(ToArray(model.DismantleActions), cap);
                        if (cap.Action == null) { why = $"'{s.ReceivingUid}' no longer has '{s.ActionName}'"; return null; }
                        break;
                    }
                    case FunnelKind.CardOnCard:
                    {
                        if (ModelOf(s.ReceivingUid) == null || ModelOf(s.GivenUid) == null) { why = $"'{s.ReceivingUid}' or '{s.GivenUid}' is not loaded"; return null; }
                        cap.Action = FindInteraction(s.ReceivingUid, s.GivenUid, s.ActionKey, s.ActionName, null);
                        if (cap.Action == null) { why = $"neither card carries '{s.ActionName}'"; return null; }
                        break;
                    }
                    case FunnelKind.GroupAction:
                    {
                        var container = ModelOf(s.GroupContainerUid);
                        var defs = container != null ? container.GroupInventoryActions : null;
                        if (defs == null || s.GroupDefIndex < 0 || s.GroupDefIndex >= defs.Length) { why = $"container '{s.GroupContainerUid}' has no group button {s.GroupDefIndex}"; return null; }
                        if (s.GroupUids == null || s.GroupUids.Count == 0) { why = "no group members saved"; return null; }
                        cap.GroupDef = defs[s.GroupDefIndex];
                        cap.GroupUids = new List<string>(s.GroupUids);
                        cap.GroupCards = new List<InGameCardBase>(s.GroupUids.Count);
                        for (int i = 0; i < s.GroupUids.Count; i++) cap.GroupCards.Add(null);
                        var member = ModelOf(s.GroupUids[0]);
                        if (member != null && member.DismantleActions != null)
                            foreach (var a in member.DismantleActions)
                                if (a != null && IsGroupCandidate(cap.GroupDef, a)) { cap.Action = a; break; }
                        break;
                    }
                    default:
                        why = "unknown action kind";
                        return null;
                }
                cap.ActionDisplay = s.Kind == FunnelKind.GroupAction
                    ? LocalizedText(cap.GroupDef.ActionName)
                    : LocalizedText(cap.Action.ActionName);
                if (string.IsNullOrEmpty(cap.ActionDisplay)) cap.ActionDisplay = s.ActionName;
                return cap;
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"[Sequence] Rebuilding '{s.ActionName}' threw: {ex}");
                why = $"{ex.GetType().Name} while rebuilding";
                return null;
            }
        }

        /// <summary>
        /// The CardInteraction a drag-drop step plays, from the two cards' CardData: the receiving card's own first,
        /// then the dragged card's (where the game keeps the inverted ones), by the captured object, then the
        /// LocalizationKey, then the name. Never a card other than these two.
        /// </summary>
        private static CardOnCardAction FindInteraction(string receivingUid, string givenUid, string key, string name, CardAction captured)
        {
            var models = new[] { ModelOf(receivingUid), ModelOf(givenUid) };
            bool useKey = !string.IsNullOrEmpty(key) && key != LocalizedString.IgnoreKey;
            for (int pass = 0; pass < 3; pass++)
            {
                foreach (var m in models)
                {
                    var cis = m != null ? m.CardInteractions : null;
                    if (cis == null) continue;
                    for (int i = 0; i < cis.Length; i++)
                    {
                        var ci = cis[i];
                        if (ci == null) continue;
                        if (pass == 0 && captured != null && ReferenceEquals(ci, captured)) return ci;
                        if (pass == 1 && useKey && string.Equals(ci.ActionName.LocalizationKey, key, StringComparison.Ordinal)) return ci;
                        if (pass == 2 && !useKey && string.Equals(ci.ActionName.DefaultText, name, StringComparison.OrdinalIgnoreCase)) return ci;
                    }
                }
            }
            return null;
        }

        private static CardData ModelOf(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return null;
            try { return UniqueIDScriptable.GetFromID<CardData>(uid); }
            catch (Exception ex)
            {
                Logger.LogDebug($"[Sequence] GetFromID<CardData>('{uid}') failed: {ex.GetType().Name}");
                return null;
            }
        }

        private static DismantleCardAction[] ToArray(List<DismantleCardAction> list) => list != null ? list.ToArray() : null;

        private static string DescribeSteps(List<Captured> steps)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < steps.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(i + 1).Append(". '").Append(steps[i].ActionName).Append("' (").Append(steps[i].Kind).Append(')');
            }
            return sb.ToString();
        }

        private static string UniqueSequenceName(string baseName)
        {
            if (string.IsNullOrEmpty(baseName)) baseName = "Sequence";
            for (int n = 1; ; n++)
            {
                string name = $"{baseName} {n}";
                bool taken = false;
                for (int i = 0; i < library.Count; i++)
                    if (string.Equals(library[i].Name, name, StringComparison.OrdinalIgnoreCase)) { taken = true; break; }
                if (!taken) return name;
            }
        }

        // =====================================================================
        // THE SAVED LIBRARY (one managed config entry)
        // =====================================================================
        // Format: sequences separated by ';', each "name=step|step|...", each step its fields separated by ',' in the
        // order Kind, ActionKey, ActionName, ReceivingUid, GivenUid, GroupContainerUid, GroupDefIndex, GroupUids (the
        // member UniqueIDs separated by '+'), StackLiquids (1 or 0). Numbers are written and read in the invariant culture. Every field is escaped with Uri.EscapeDataString, so none of the four
        // separators can appear inside one.

        private static void TrimSavedSequences()
        {
            int saved = 0;
            for (int i = 0; i < library.Count; i++) if (library[i].Saved != null) saved++;
            while (saved > MaxSavedSequences)
            {
                for (int i = 0; i < library.Count; i++)
                {
                    if (library[i].Saved == null) continue;
                    Logger.LogInfo($"[Sequence] More than {MaxSavedSequences} saved sequences: dropping the oldest, '{library[i].Name}'");
                    library.RemoveAt(i);
                    if (selectedSequence > i) selectedSequence--;
                    else if (selectedSequence == i) selectedSequence = library.Count - 1;
                    saved--;
                    break;
                }
            }
        }

        private static void PersistLibrary()
        {
            if (Plugin.SavedSequences == null) return;
            var sb = new StringBuilder();
            for (int i = 0; i < library.Count; i++)
            {
                var seq = library[i];
                if (seq.Saved == null) continue;
                if (sb.Length > 0) sb.Append(';');
                sb.Append(Esc(seq.Name)).Append('=');
                for (int j = 0; j < seq.Saved.Count; j++)
                {
                    if (j > 0) sb.Append('|');
                    var s = seq.Saved[j];
                    var members = new List<string>();
                    if (s.GroupUids != null) foreach (var u in s.GroupUids) members.Add(Esc(u));
                    sb.Append(Esc(s.Kind.ToString())).Append(',')
                      .Append(Esc(s.ActionKey)).Append(',')
                      .Append(Esc(s.ActionName)).Append(',')
                      .Append(Esc(s.ReceivingUid)).Append(',')
                      .Append(Esc(s.GivenUid)).Append(',')
                      .Append(Esc(s.GroupContainerUid)).Append(',')
                      .Append(s.GroupDefIndex.ToString(CultureInfo.InvariantCulture)).Append(',')
                      .Append(string.Join("+", members.ToArray())).Append(',')
                      .Append(s.StackLiquids ? '1' : '0');
                }
            }
            Plugin.SavedSequences.Value = sb.ToString();
        }

        /// <summary>Called once from Plugin.Awake: the saved sequences, rebuilt into live steps only when first played.</summary>
        internal static void LoadSavedSequences()
        {
            string raw = Plugin.SavedSequences != null ? Plugin.SavedSequences.Value : null;
            if (string.IsNullOrEmpty(raw)) return;
            int loaded = 0;
            foreach (var entry in raw.Split(';'))
            {
                if (entry.Length == 0) continue;
                try
                {
                    int eq = entry.IndexOf('=');
                    if (eq <= 0) throw new FormatException("no '='");
                    var seq = new Sequence { Name = Unesc(entry.Substring(0, eq)), Saved = new List<SavedStep>() };
                    foreach (var stepText in entry.Substring(eq + 1).Split('|'))
                    {
                        var f = stepText.Split(',');
                        if (f.Length != 9) throw new FormatException($"a step has {f.Length} fields, not 9");
                        var s = new SavedStep
                        {
                            Kind = (FunnelKind)Enum.Parse(typeof(FunnelKind), Unesc(f[0])),
                            ActionKey = NullIfEmpty(Unesc(f[1])),
                            ActionName = Unesc(f[2]),
                            ReceivingUid = NullIfEmpty(Unesc(f[3])),
                            GivenUid = NullIfEmpty(Unesc(f[4])),
                            GroupContainerUid = NullIfEmpty(Unesc(f[5])),
                            GroupDefIndex = int.Parse(f[6], NumberStyles.Integer, CultureInfo.InvariantCulture),
                            StackLiquids = f[8] == "1",
                        };
                        if (f[7].Length > 0)
                        {
                            s.GroupUids = new List<string>();
                            foreach (var u in f[7].Split('+')) s.GroupUids.Add(Unesc(u));
                        }
                        seq.Saved.Add(s);
                    }
                    library.Add(seq);
                    loaded++;
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"[Sequence] Skipping a saved sequence that could not be read ({ex.GetType().Name}: {ex.Message}); the others still load.");
                }
            }
            if (library.Count > 0) selectedSequence = library.Count - 1;
            Logger.LogDebug($"[Sequence] {loaded} saved sequence(s) loaded");
        }

        private static string Esc(string s) => Uri.EscapeDataString(s ?? "");
        private static string Unesc(string s) => Uri.UnescapeDataString(s ?? "");
        private static string NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;
    }
}
