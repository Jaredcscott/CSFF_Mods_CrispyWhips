using TMPro;

namespace CSFFModFramework.Patching.BugFixes;

/// <summary>
/// Several NPC-interaction button labels (the Talk/Trade/Commissions row in
/// NPCInspectionPopup, and DialogsPopup answer buttons) ship with TextMeshPro
/// auto-sizing disabled, so any label wider than the button's authored width
/// clips past the border instead of shrinking to fit. This enables TMP
/// shrink-to-fit auto-sizing (fontSizeMax = the button's original authored
/// size, so text that already fits is visually unchanged) the first time each
/// TextMeshProUGUI is seen. TooltipButton.Setup underlies every IndexButton-
/// family button in the game (Talk-row interaction buttons, DismantleActionButton,
/// tab buttons, etc.), so this fixes the same overflow class everywhere that
/// hook is used, not just the two NPC popups called out below.
/// </summary>
internal static class NPCButtonTextFit
{
    private static readonly HashSet<int> _fitted = new();

    public static void ApplyPatch(Harmony harmony)
    {
        try
        {
            SafePatcher.TryPatch(harmony, "TooltipButton", "Setup",
                postfix: new HarmonyMethod(typeof(NPCButtonTextFit), nameof(TooltipButtonSetup_Postfix)));
            SafePatcher.TryPatch(harmony, "DialogAnswerButton", "Setup",
                postfix: new HarmonyMethod(typeof(NPCButtonTextFit), nameof(DialogAnswerButtonSetup_Postfix)));
            SafePatcher.TryPatch(harmony, "NPCInspectionPopup", "SetupActions",
                postfix: new HarmonyMethod(typeof(NPCButtonTextFit), nameof(NPCInspectionSetupActions_Postfix)));
        }
        catch (Exception ex)
        {
            Util.Log.Warn($"NPCButtonTextFit: patch setup failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }

    static void TooltipButtonSetup_Postfix(object __instance)
    {
        try
        {
            var field = Reflection.ReflectionCache.GetField(__instance.GetType(), "ButtonText");
            EnableShrinkToFit(field?.GetValue(__instance) as TextMeshProUGUI);
        }
        catch (Exception ex)
        {
            Util.Log.Debug($"[NPCButtonTextFit] TooltipButton.Setup postfix failed: {Util.Log.ExceptionText(ex)}");
        }
    }

    static void DialogAnswerButtonSetup_Postfix(object __instance)
    {
        try
        {
            var field = Reflection.ReflectionCache.GetField(__instance.GetType(), "AnswerText");
            EnableShrinkToFit(field?.GetValue(__instance) as TextMeshProUGUI);
        }
        catch (Exception ex)
        {
            Util.Log.Debug($"[NPCButtonTextFit] DialogAnswerButton.Setup postfix failed: {Util.Log.ExceptionText(ex)}");
        }
    }

    static void NPCInspectionSetupActions_Postfix(object __instance)
    {
        try
        {
            var type = __instance.GetType();
            var tradingField = Reflection.ReflectionCache.GetField(type, "TradingButtonObject");
            var commissionsField = Reflection.ReflectionCache.GetField(type, "CommissionsButtonObject");
            if (tradingField?.GetValue(__instance) is GameObject tradingObj)
                EnableShrinkToFit(tradingObj.GetComponentInChildren<TextMeshProUGUI>(true));
            if (commissionsField?.GetValue(__instance) is GameObject commissionsObj)
                EnableShrinkToFit(commissionsObj.GetComponentInChildren<TextMeshProUGUI>(true));
        }
        catch (Exception ex)
        {
            Util.Log.Debug($"[NPCButtonTextFit] NPCInspectionPopup.SetupActions postfix failed: {Util.Log.ExceptionText(ex)}");
        }
    }

    private static void EnableShrinkToFit(TextMeshProUGUI text)
    {
        if (!text) return;
        int id = text.GetInstanceID();
        if (_fitted.Contains(id)) return;
        _fitted.Add(id);
        if (text.enableAutoSizing) return;
        float original = text.fontSize;
        text.enableAutoSizing = true;
        text.fontSizeMax = original;
        text.fontSizeMin = Mathf.Max(8f, original * 0.5f);
    }
}
