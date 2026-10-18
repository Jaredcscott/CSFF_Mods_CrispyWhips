namespace PartnerOverhaul.Patcher
{
    /// <summary>
    /// PO-C0. Vanilla titles a Talk window, and speaks each log line, with <c>NPCModel.AgentName</c>
    /// ("Partner"), not the name the player chose (<c>InGameNPC.GetName()</c>). Two patches:
    /// a postfix on <c>DialogsPopup.Show</c> sets the title when the scene sets none, and a prefix on
    /// <c>DialogLine.ToLogMessage</c> swaps the default speaker. Both are scoped to NPCs whose model
    /// is <c>Agent_Partner</c>, so traders, the Hag and mod NPCs keep their own titles, and both fall
    /// back to vanilla when GetName() is empty or only repeats the model name. No new strings.
    /// </summary>
    public static class PartnerNameInDialogPatch
    {
        private const string PartnerModelName = "Agent_Partner";
        private static BepInEx.Logging.ManualLogSource Logger => Plugin.Logger;
        private static readonly HashSet<string> Warned = new HashSet<string>();
        private static FieldInfo _titleField;
        private static bool _titleFieldResolved;

        public static void ApplyPatch(Harmony harmony)
        {
            harmony.Patch(
                AccessTools.Method(typeof(DialogsPopup), nameof(DialogsPopup.Show)),
                postfix: new HarmonyMethod(typeof(PartnerNameInDialogPatch), nameof(Show_Postfix)));
            harmony.Patch(
                AccessTools.Method(typeof(DialogLine), nameof(DialogLine.ToLogMessage)),
                prefix: new HarmonyMethod(typeof(PartnerNameInDialogPatch), nameof(ToLogMessage_Prefix)));
            Logger.LogDebug("PartnerNameInDialogPatch applied.");
        }

        private static void WarnOnce(string cause, string detail)
        {
            if (Warned.Add(cause))
                Logger.LogWarning($"[PartnerNameInDialogPatch] {cause}: {detail} (further occurrences suppressed)");
        }

        /// <summary>The chosen name when <paramref name="npc"/> is a Partner whose chosen name differs
        /// from the model name; null means "leave vanilla's text alone".</summary>
        internal static string ResolvePartnerName(InGameNPC npc)
        {
            if (npc == null) return null;
            var model = npc.NPCModel;
            if (model == null) return null;
            if (model.name != PartnerModelName) return null;
            string chosen = npc.GetName();
            if (string.IsNullOrEmpty(chosen) || chosen == model.AgentName) return null;
            return chosen;
        }

        private static void Show_Postfix(DialogsPopup __instance, InGameNPC _TalkingTo, string _Title)
        {
            try
            {
                if (!string.IsNullOrEmpty(_Title)) return; // the scene sets a title: keep it
                string name = ResolvePartnerName(_TalkingTo);
                if (name == null) return;

                if (!_titleFieldResolved)
                {
                    _titleFieldResolved = true;
                    _titleField = AccessTools.Field(typeof(DialogsPopup), "DialogTitle");
                    if (_titleField == null)
                        WarnOnce("DialogTitle-field-missing", "DialogsPopup.DialogTitle not found; the Talk window keeps vanilla's title");
                }
                if (_titleField == null) return;
                var text = _titleField.GetValue(__instance) as TMPro.TextMeshProUGUI;
                if (text == null)
                {
                    WarnOnce("DialogTitle-null", "DialogsPopup.DialogTitle is null or not a TextMeshProUGUI; vanilla title kept");
                    return;
                }
                text.text = name;
            }
            catch (Exception ex)
            {
                WarnOnce("Show_Postfix-exception", ex.ToString());
            }
        }

        private static void ToLogMessage_Prefix(ref string _DefaultSpeakerName)
        {
            try
            {
                var popup = MBSingleton<DialogsPopup>.Instance;
                if (popup == null) return; // not in a Talk window: nothing to rename
                var npc = popup.TalkingTo;
                string name = ResolvePartnerName(npc);
                if (name == null) return;
                // Only replace the speaker vanilla itself passed for this NPC.
                if (_DefaultSpeakerName == npc.NPCModel.AgentName)
                    _DefaultSpeakerName = name;
            }
            catch (Exception ex)
            {
                WarnOnce("ToLogMessage_Prefix-exception", ex.ToString());
            }
        }
    }
}
