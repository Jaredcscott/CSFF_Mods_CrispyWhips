using System.Reflection;
using CSFFModFramework.Util;

namespace HarshWilds.Patcher;

/// <summary>
/// The one notice line a Harsh Wilds action hook shows (the smithing burn, the foraging bites): the
/// game's own message popup carrying a localized row from the mod's CSVs. Both hooks run it AFTER the
/// player's action has finished, so it never interrupts the action itself. Every failure exit logs once
/// per cause and returns, never throws, and the hook's real effect (the wound, the bites) has already
/// been applied by then, so a missing popup costs only the line.
/// </summary>
internal static class HwNotice
{
    private static readonly HashSet<string> Warned = new(StringComparer.Ordinal);
    private static FieldInfo _textsField;

    internal static void Show(string localizationKey, string fallbackText)
    {
        try
        {
            string line = Lookup(localizationKey);
            if (string.IsNullOrWhiteSpace(line)) line = fallbackText;
            var gfx = GraphicsManager.Instance;
            var popup = gfx == null ? null : gfx.MessagePopup;
            if (popup == null)
            {
                WarnOnce("popup-null", "HwNotice: GraphicsManager.Instance.MessagePopup is not available, so the notice line is not shown.");
                return;
            }
            popup.Setup("", line.Trim(), null);
        }
        catch (Exception ex)
        {
            WarnOnce("show-threw", $"HwNotice: showing the notice threw: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }

    private static string Lookup(string key)
    {
        if (_textsField == null)
        {
            var locType = CardUtil.FindGameType("LocalizationManager");
            _textsField = locType?.GetField("CurrentTexts", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (_textsField == null)
            {
                WarnOnce("texts-field", "HwNotice: LocalizationManager.CurrentTexts was not found, so notice lines use their English default.");
                return null;
            }
        }
        return _textsField.GetValue(null) is Dictionary<string, string> texts && texts.TryGetValue(key, out var text) ? text : null;
    }

    private static void WarnOnce(string cause, string message)
    {
        if (Warned.Add(cause)) Plugin.Logger.LogWarning(message);
    }
}
