using CSFFModFramework.Util;

namespace CSFFModFramework.Api;

/// <summary>
/// Plain, current-language text with an English fallback (fleet plan section 10, W0.10 row 5).
/// <see cref="LocalizedStringBuilder"/> only builds a <c>LocalizedString</c> instance for a card
/// field; this reads a CSV row as a string, the way a log line, a UI label or an interpolated
/// message needs it. Same shape as QuickTransfer's own <c>OverlayText.Get</c>/<c>Format</c>
/// (<c>QuickTransfer/OverlayText.cs</c>), lifted here so content mods stop hand-rolling the same
/// <c>LocalizationManager.GetText</c> call, reflected or typed.
/// </summary>
public static class Localized
{
    private static bool _lookupFailedOnce;

    /// <summary>The current-language text for <paramref name="key"/>, or <paramref name="englishDefault"/>
    /// when the key is not loaded or the lookup fails. Never throws.</summary>
    public static string Text(string key, string englishDefault)
    {
        try
        {
            if (LocalizationManager.GetText(key, out var text) && !string.IsNullOrEmpty(text))
                return text;
        }
        catch (Exception ex)
        {
            // A game update could rename the type or method; say so once rather than per call.
            if (!_lookupFailedOnce)
            {
                _lookupFailedOnce = true;
                Log.Warn($"Api.Localized.Text: LocalizationManager.GetText failed ({ex.GetType().Name}: {ex.Message}); every localized lookup stays English from here on.");
            }
        }
        return englishDefault;
    }

    /// <summary>
    /// Formats the current-language template for <paramref name="key"/> with <paramref name="args"/>,
    /// falling back to formatting <paramref name="englishDefault"/> when a translation's placeholders
    /// are malformed (a CSV column can drift from the English template's {0}/{1}/... count without any
    /// build-time signal). The interpolated values themselves are never translated.
    /// </summary>
    public static string Format(string key, string englishDefault, params object[] args)
    {
        var template = Text(key, englishDefault);
        try
        {
            return string.Format(template, args);
        }
        catch (FormatException ex)
        {
            Log.Warn($"Api.Localized.Format: the localized template for '{key}' has malformed placeholders ({ex.Message}); using the English default instead.");
            return string.Format(englishDefault, args);
        }
    }
}
