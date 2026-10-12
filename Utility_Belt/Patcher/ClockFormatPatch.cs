namespace Utility_Belt.Patcher;

/// <summary>The main clock's display format ([Display] Clock Format).</summary>
internal enum ClockFormat
{
    TwentyFourHour,
    TwelveHour,
}

/// <summary>
/// [Display] Clock Format (plan U2). Vanilla's GraphicsManager.Update writes DayTimePoints.text as "HH:MM"
/// only when the tick changes. This postfix runs after it and, when TwelveHour is chosen, rewrites that
/// text as "h:MMa" or "h:MMp" once per change. Only the main clock is touched: HoursDisplay.HoursToTimeOfDay
/// (six callers, including save-slot times and the log) is never patched, so those stay 24-hour.
/// </summary>
/// <remarks>
/// The target is Update, a large caller, never the small HoursToTimeOfDay (CLAUDE.md "Harmony Patching
/// Pitfalls"). We remember the last source we rewrote and the result, recognise our own output by
/// equality, and put the source back if the option is turned off, so nothing else is touched.
/// </remarks>
internal static class ClockFormatPatch
{
    private static string _lastSource;
    private static string _lastResult;
    private static bool _warned;
    private static bool _warnedRich;

    internal static void ApplyPatch(Harmony harmony)
    {
        var update = AccessTools.Method(typeof(GraphicsManager), "Update");
        if (update == null)
        {
            Plugin.Logger.LogWarning("[UB] GraphicsManager.Update not found; Clock Format stays 24-hour.");
            return;
        }

        harmony.Patch(update, postfix: new HarmonyMethod(typeof(ClockFormatPatch), nameof(UpdatePostfix)));
    }

    private static void UpdatePostfix(GraphicsManager __instance)
    {
        try
        {
            var tmp = __instance.DayTimePoints;
            if (tmp == null)
            {
                return;
            }

            var twelve = Plugin.ClockFormatSetting.Value == ClockFormat.TwelveHour;
            var cur = tmp.text;

            if (_lastResult != null && cur == _lastResult)
            {
                // Our own output is on screen. Nothing to do unless the option went off: restore the source.
                if (!twelve)
                {
                    tmp.text = _lastSource;
                    _lastSource = null;
                    _lastResult = null;
                }
                return;
            }

            if (!twelve)
            {
                return;
            }

            if (!TryConvert(cur, out var result))
            {
                return;
            }

            result = FitToBox(tmp, cur, result);
            _lastSource = cur;
            _lastResult = result;
            tmp.text = result;
        }
        catch (Exception ex)
        {
            if (!_warned)
            {
                _warned = true;
                Plugin.Logger.LogWarning($"[UB] Clock Format failed ({ex.GetType().Name}: {ex.Message}); the clock stays as the game draws it.");
            }
        }
    }

    /// <summary>
    /// The converted text is a little wider than vanilla's "22:45". Measure both with the object's own
    /// preferred width (read only) and, when the result is wider, wrap it in a rich-text size tag so it
    /// takes no more room than the source did. fontSize, auto-size and the rect are never written, so there
    /// is no per-object state to capture or restore. Without rich text the compact string is used as is.
    /// </summary>
    private static string FitToBox(TMPro.TextMeshProUGUI tmp, string source, string result)
    {
        if (!tmp.richText)
        {
            if (!_warnedRich)
            {
                _warnedRich = true;
                Plugin.Logger.LogWarning("[UB] The clock text has rich text off; the 12-hour clock is shown without a fit-to-box size tag.");
            }
            return result;
        }

        var src = tmp.GetPreferredValues(source).x;
        var res = tmp.GetPreferredValues(result).x;
        if (res <= src || res <= 0f)
        {
            return result;
        }

        // A template may carry its own size tag (the Chinese 上午/下午 word is set smaller). TMP sizes an inner
        // <size=N%> against the base font size, not against an enclosing tag (measured 2026-10-10: a plain outer
        // wrap left 上午 larger than the digits), so Scale compounds every inner percentage by hand. Each guess is
        // re-measured and stepped down until it fits. Widths in the 1280x720 layout, 2026-10-10: "22:45" 133.1,
        // "10:45p" 163.1, Chinese "上午10:15" 253.1 and "<size=55%>上午</size>10:15" 199.1.
        var pct = (int)Math.Floor(100f * src / res);
        do
        {
            var sized = Scale(result, pct);
            if (tmp.GetPreferredValues(sized).x <= src)
            {
                return sized;
            }

            pct -= 3;
        }
        while (pct >= MinFitPercent);

        return Scale(result, MinFitPercent);
    }

    /// <summary>Wraps the text at pct percent and multiplies every inner size percentage by the same factor.</summary>
    internal static string Scale(string text, int pct)
    {
        var inner = InnerSize.Replace(text, m => "<size=" + (int.Parse(m.Groups[1].Value) * pct / 100) + "%>");
        return "<size=" + pct + "%>" + inner + "</size>";
    }

    private static readonly System.Text.RegularExpressions.Regex InnerSize =
        new System.Text.RegularExpressions.Regex(@"<size=(\d+)%>");

    // Below this the clock stops being readable; no shipped template comes near it.
    private const int MinFitPercent = 40;

    /// <summary>"HH:MM" to "h:MMa/p" through the localized templates. False when the text is not HH:MM.</summary>
    internal static bool TryConvert(string text, out string result)
    {
        result = null;
        if (text == null || text.Length != 5 || text[2] != ':'
            || !char.IsDigit(text[0]) || !char.IsDigit(text[1]) || !char.IsDigit(text[3]) || !char.IsDigit(text[4]))
        {
            return false;
        }

        var hour = (text[0] - '0') * 10 + (text[1] - '0');
        if (hour > 23)
        {
            return false;
        }

        var h12 = hour % 12 == 0 ? 12 : hour % 12;
        var minutes = text.Substring(3, 2);
        result = hour < 12
            ? OverlayText.Format(OverlayText.ClockAmKey, OverlayText.ClockAmDefault, h12, minutes)
            : OverlayText.Format(OverlayText.ClockPmKey, OverlayText.ClockPmDefault, h12, minutes);
        return true;
    }
}
