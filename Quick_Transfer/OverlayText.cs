using System.Runtime.CompilerServices;

namespace Quick_Transfer;

/// <summary>
/// The on-screen overlay's player-visible strings. Each has a CSV key in
/// <c>Localization/SimpEn.csv</c> / <c>SimpCn.csv</c> (CSFFModFramework's LocalizationLoader reads the
/// file for the active language into the game's own <c>LocalizationManager.CurrentTexts</c>) and an
/// English default used whenever the key is not loaded: framework absent, CSV missing, or a
/// translation left blank. Config descriptions are BepInEx-owned and deliberately not covered.
/// </summary>
/// <remarks>
/// Resolution goes through vanilla <c>LocalizationManager.GetText(key, out text)</c>, which reads only
/// the current language's dictionary, so it follows the player's language on its own and never forces
/// one. The game ships no En.csv, so in English mode that dictionary holds ONLY mod rows; our SimpEn.csv
/// rows are what resolves there, and the defaults below are a second copy of the same English for when
/// they did not load. The loader trims every CSV value, so spacing between fragments lives in code.
/// </remarks>
internal static class OverlayText
{
    // {0} is the amount label: "All" / "Half" / a number.
    public const string AmountKey = "QuickTransfer_Overlay_Amount";
    public const string AmountDefault = "Quick Transfer: {0}";

    // The start notice of a Tag Filter batch (plan Feature 5). {0} is the amount label, {1} the tag's own
    // display name (CardTag.InGameName, which the game localizes itself, so it is never a row here).
    public const string AmountTagKey = "QuickTransfer_Overlay_AmountTag";
    public const string AmountTagDefault = "Quick Transfer: {0} ({1})";

    public const string AllKey = "QuickTransfer_Overlay_All";
    public const string AllDefault = "All";

    public const string HalfKey = "QuickTransfer_Overlay_Half";
    public const string HalfDefault = "Half";

    // The Ctrl+Shift label in AllButOne mode (plan Feature 6), shown while the combo is held.
    public const string AllButOneKey = "QuickTransfer_Overlay_AllButOne";
    public const string AllButOneDefault = "All but one";

    public const string ComboCtrlKey = "QuickTransfer_Overlay_ComboCtrl";
    public const string ComboCtrlDefault = "(Ctrl)";

    public const string ComboShiftKey = "QuickTransfer_Overlay_ComboShift";
    public const string ComboShiftDefault = "(Shift)";

    public const string ComboCtrlShiftKey = "QuickTransfer_Overlay_ComboCtrlShift";
    public const string ComboCtrlShiftDefault = "(Ctrl+Shift)";

    // {0} Shift preset, {1} Ctrl preset, {2} custom amount.
    public const string PresetsResetKey = "QuickTransfer_Overlay_PresetsReset";
    public const string PresetsResetDefault = "Presets reset: {0} / {1} / {2}";

    // A running batch stopped because the place its first card went is no longer where the next
    // card would go (popup closed, character window opened, another container opened).
    public const string StoppedKey = "QuickTransfer_Overlay_Stopped";
    public const string StoppedDefault = "Batch stopped: the destination changed";

    // The end-of-batch report (plan Feature 4). {0} cards that actually moved, {1} cards the click asked for.
    // Shown only when fewer moved than were asked for, never for a batch that was stopped or replaced.
    public const string MovedKey = "QuickTransfer_Overlay_Moved";
    public const string MovedDefault = "Moved {0} of {1}";

    // The same report for a whole-stack (All) batch, which has no count to compare against. {0} cards moved.
    public const string MovedAllKey = "QuickTransfer_Overlay_MovedAll";
    public const string MovedAllDefault = "Moved {0}";

    // Undo last batch (plan Feature 10). {0} cards the undo actually moved back.
    public const string UndidKey = "QuickTransfer_Overlay_Undid";
    public const string UndidDefault = "Undid {0}";

    // The Undo Key was pressed with no batch to reverse: none yet, the last one already undone or replaced, or the
    // container it went to closed or changed since.
    public const string NothingToUndoKey = "QuickTransfer_Overlay_NothingToUndo";
    public const string NothingToUndoDefault = "Nothing to undo";

    // Per-container settings (plan Features 11 and 12). {0} is the container's own in-game name.
    public const string ContainerOffKey = "QuickTransfer_Overlay_ContainerOff";
    public const string ContainerOffDefault = "{0}: bulk transfer off";

    public const string ContainerOnKey = "QuickTransfer_Overlay_ContainerOn";
    public const string ContainerOnDefault = "{0}: bulk transfer on";

    // {0} the amount label ("All" or a number), {1} the container's name.
    public const string RememberedKey = "QuickTransfer_Overlay_Remembered";
    public const string RememberedDefault = "Remembered {0} for {1}";

    public const string ForgotKey = "QuickTransfer_Overlay_Forgot";
    public const string ForgotDefault = "Forgot the remembered amount for {0}";

    private static bool _lookupFailedOnce;

    /// <summary>The current-language text for <paramref name="key"/>, or <paramref name="englishDefault"/> when it is not loaded.</summary>
    public static string Get(string key, string englishDefault)
    {
        try
        {
            if (TryGameText(key, out var text) && !string.IsNullOrEmpty(text))
                return text;
        }
        catch (Exception ex)
        {
            // A game update could rename the type or method. A missing member surfaces when
            // TryGameText is JIT-compiled, i.e. at this call, which is why the game call sits in
            // its own non-inlined method. The overlay must never cost the player a frame over a
            // label, so say so once and stay English.
            if (!_lookupFailedOnce)
            {
                _lookupFailedOnce = true;
                Plugin.Logger?.LogWarning($"[QT] Localization lookup failed ({ex.GetType().Name}: {ex.Message}); overlay stays English.");
            }
        }
        return englishDefault;
    }

    /// <summary>
    /// Formats the current-language template for <paramref name="key"/>, falling back to the English
    /// template when a translation's placeholders are malformed. The interpolated values themselves
    /// (counts) are never translated.
    /// </summary>
    public static string Format(string key, string englishDefault, params object[] args)
    {
        var template = Get(key, englishDefault);
        try
        {
            return string.Format(template, args);
        }
        catch (FormatException ex)
        {
            // A translation's placeholders (CSV column, not this file) can drift from the English
            // template's {0}/{1}/... count without any build-time signal - breadcrumb it so a
            // silently-wrong localized string is diagnosable instead of just "feels off".
            Plugin.Logger?.LogDebug($"[QT] Localized template for '{key}' has malformed placeholders ({ex.Message}); using the English default.");
            return string.Format(englishDefault, args);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool TryGameText(string key, out string text)
    {
        return LocalizationManager.GetText(key, out text);
    }
}
