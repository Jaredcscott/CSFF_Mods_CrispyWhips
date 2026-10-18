using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace Utility_Belt;

/// <summary>
/// [Display] Show Carry Weight (plan B9). Hovering the item bar's weight bar shows a tooltip reading how much
/// you carry against the most you can carry, in numbers (plan Q2: a tooltip, no new layout).
/// </summary>
/// <remarks>
/// <para>R11 (2026-10-15, read from the code): vanilla shows the carry weight as numbers nowhere. The
/// <c>WeightBar</c> draws a fill and two alert icons; the Encumbrance stat's popup (<c>StatDetailsPopup.Setup</c>)
/// writes only the title, description, a slider and the current status name and description, and that
/// popup is reached only through a status icon (the stat is not in the detailed list), so not at all while
/// unencumbered.</para>
/// <para>Values: <c>GameManager.InGamePlayerWeight.CurrentValue(false)</c> against
/// <c>CurrentMinMaxValue.y</c>, the same pair the pickup check reads. The limit is always read live, so Extra
/// Carry Weight's raised limit shows (never a fixed 4000).</para>
/// <para>Hover: a child object "UB_CarryWeightTooltip" stretched over the bar, holding a transparent
/// raycast Image and a <see cref="CarryWeightTooltip"/> (a vanilla <c>TooltipProvider</c>). A pointer click on it bubbles to
/// the bar's own handlers, so nothing of the bar's is blocked. The child is created once per bar and turned
/// on or off with the option; the bar itself is never written.</para>
/// </remarks>
internal static class CarryWeight
{
    private const string ChildName = "UB_CarryWeightTooltip";

    private static readonly HashSet<string> _warned = new();
    private static GraphicsManager _gm;
    private static GameObject _child;
    private static float _nextLook;

    /// <summary>The tooltip text, or null when there is no run.</summary>
    internal static string Text()
    {
        var gm = MBSingleton<GameManager>.Instance;
        if (gm == null || gm.InGamePlayerWeight == null)
        {
            return null;
        }

        var stat = gm.InGamePlayerWeight;
        return OverlayText.Format(OverlayText.CarryWeightKey, OverlayText.CarryWeightDefault,
            Num(stat.CurrentValue(false)), Num(stat.CurrentMinMaxValue.y));
    }

    internal static void Tick()
    {
        var on = Plugin.ShowCarryWeight.Value;
        var gm = MBSingleton<GraphicsManager>.Instance;
        if (gm != _gm)
        {
            _gm = gm;
            _child = null; // the old scene's bar went with it
            _nextLook = 0f;
        }

        if (!on)
        {
            if (_child != null && _child.activeSelf)
            {
                _child.SetActive(false);
            }

            return;
        }

        if (_child != null)
        {
            if (!_child.activeSelf)
            {
                _child.SetActive(true);
            }

            return;
        }

        if (gm == null || Time.unscaledTime < _nextLook)
        {
            return;
        }

        _nextLook = Time.unscaledTime + 1f; // the bar can appear after the scene does: look once a second
        try
        {
            _child = Create();
        }
        catch (Exception ex)
        {
            WarnOnce("create:" + ex.GetType().Name, $"[UB] Carry weight tooltip could not be added ({ex.GetType().Name}: {ex.Message}); the weight bar is left as the game drew it.");
        }
    }

    private static GameObject Create()
    {
        // FindObjectOfType skips inactive objects, which is right: only the bar on screen gets the tooltip.
        var bar = UnityEngine.Object.FindObjectOfType<WeightBar>();
        if (bar == null)
        {
            return null;
        }

        var existing = bar.transform.Find(ChildName);
        if (existing != null)
        {
            return existing.gameObject;
        }

        if (bar.transform is not RectTransform)
        {
            WarnOnce("not-rect", "[UB] The weight bar has no RectTransform; the carry weight tooltip is not added.");
            return null;
        }

        var go = new GameObject(ChildName, typeof(RectTransform));
        go.layer = bar.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(bar.transform, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
        rt.SetAsLastSibling();

        var layout = go.AddComponent<LayoutElement>();
        layout.ignoreLayout = true; // never pushes the bar's own children around

        var img = go.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0f); // invisible, but a raycast target so the hover reaches us
        img.raycastTarget = true;

        go.AddComponent<CarryWeightTooltip>();
        return go;
    }

    private static string Num(float value)
    {
        return Mathf.RoundToInt(value).ToString(CultureInfo.InvariantCulture);
    }

    private static void WarnOnce(string cause, string message)
    {
        if (_warned.Add(cause))
        {
            Plugin.Logger.LogWarning(message);
        }
    }
}

/// <summary>The TooltipProvider on the bar; its text is rebuilt on hover and every frame while hovered.</summary>
internal sealed class CarryWeightTooltip : TooltipProvider
{
    public override void OnHoverEnter()
    {
        Refresh();
        base.OnHoverEnter();
    }

    private void Update()
    {
        if (IsHovered)
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        SetTooltip(CarryWeight.Text() ?? string.Empty, string.Empty, string.Empty);
    }
}
