using System.Runtime.CompilerServices;

namespace Utility_Belt;

/// <summary>
/// Utility Belt's player-visible strings. Each has a <c>UtilityBelt_</c> CSV key in
/// <c>Localization/SimpEn.csv</c> / <c>SimpCn.csv</c> (read into the game's own
/// <c>LocalizationManager.CurrentTexts</c> by the framework's loader) and an English default used whenever
/// the key is not loaded (framework absent, CSV missing, a blank translation). This is UB's copy of Quick
/// Transfer's OverlayText pattern. Config descriptions are BepInEx-owned and not covered.
/// </summary>
/// <remarks>
/// Resolution goes through vanilla <c>LocalizationManager.GetText(key, out text)</c>, which reads only the
/// current language's dictionary. The game ships no En.csv, so in English mode that dictionary holds only
/// mod rows. The loader trims every CSV value, so spacing lives inside the template, not at its edges.
/// </remarks>
internal static class OverlayText
{
    // 12-hour clock (plan U2). {0} is the hour 1 to 12, {1} the two-digit minutes.
    public const string ClockAmKey = "UtilityBelt_Clock_AM";
    public const string ClockAmDefault = "{0}:{1}a";

    public const string ClockPmKey = "UtilityBelt_Clock_PM";
    public const string ClockPmDefault = "{0}:{1}p";

    // Capacity and warmth readouts (plan B3). Weight and liquid read {0} now / {1} capacity; warmth is the signed number.
    public const string ReadoutWeightKey = "UtilityBelt_Readout_Weight";
    public const string ReadoutWeightDefault = "Weight: {0} / {1}";

    public const string ReadoutLiquidKey = "UtilityBelt_Readout_Liquid";
    public const string ReadoutLiquidDefault = "Liquid: {0} / {1}";

    public const string ReadoutSleepWarmthKey = "UtilityBelt_Readout_SleepWarmth";
    public const string ReadoutSleepWarmthDefault = "Warmth while sleeping: {0}";

    // Character randomiser (plan U4): the button's label.
    public const string RandomiseKey = "UtilityBelt_Randomise_Button";
    public const string RandomiseDefault = "Randomise";

    // Store Products In (plan C2): {0} is the container's name.
    public const string StorePickedKey = "UtilityBelt_Store_Picked";
    public const string StorePickedDefault = "Repeat Action products go into {0}";

    public const string StoreClearedKey = "UtilityBelt_Store_Cleared";
    public const string StoreClearedDefault = "Repeat Action products stay where they land";

    public const string StoreFullKey = "UtilityBelt_Store_Full";
    public const string StoreFullDefault = "{0} is full: products stay where they land";

    public const string StoreBadTargetKey = "UtilityBelt_Store_BadTarget";
    public const string StoreBadTargetDefault = "{0} cannot take stored products";

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
            if (!_lookupFailedOnce)
            {
                _lookupFailedOnce = true;
                Plugin.Logger?.LogWarning($"[UB] Localization lookup failed ({ex.GetType().Name}: {ex.Message}); text stays English.");
            }
        }
        return englishDefault;
    }

    /// <summary>Formats the current-language template, falling back to the English one when its placeholders are malformed.</summary>
    public static string Format(string key, string englishDefault, params object[] args)
    {
        var template = Get(key, englishDefault);
        try
        {
            return string.Format(template, args);
        }
        catch (FormatException ex)
        {
            Plugin.Logger?.LogDebug($"[UB] Localized template for '{key}' has malformed placeholders ({ex.Message}); using the English default.");
            return string.Format(englishDefault, args);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool TryGameText(string key, out string text)
    {
        return LocalizationManager.GetText(key, out text);
    }
}
