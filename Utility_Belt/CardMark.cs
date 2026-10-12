using UnityEngine;
using UnityEngine.UI;

namespace Utility_Belt;

/// <summary>Which corner of a card a <see cref="CardMark"/> sits in.</summary>
internal enum MarkCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>
/// A small icon drawn on a card's graphics (B1's lock mark; Prompt 12 reuses it for B6's sealed mark in
/// another corner). Each mark is a child GameObject Utility Belt creates under the card's CardGraphics,
/// named "UB_Mark_&lt;id&gt;", holding one non-raycast Image.
/// </summary>
/// <remarks>
/// Capture and restore (CLAUDE.md "Harmony Patching Pitfalls", shared UI objects): a card's graphics are
/// reused (card pooling hands the same CardGraphics to another card), so nothing of the card's own is ever
/// written. The only state is our child object, recorded per CardGraphics per mark id; restoring is
/// deactivating it. A caller sweeps every live card with <see cref="BeginPass"/>, <see cref="Set"/> and
/// <see cref="EndPass"/>, and a mark whose card was not visited in a pass (pooled away, gone from the board)
/// is hidden at EndPass, so a recycled card never shows the last owner's mark.
/// </remarks>
internal static class CardMark
{
    private sealed class MarkSet
    {
        internal readonly Dictionary<CardGraphics, GameObject> Objects = new();
        internal readonly HashSet<CardGraphics> Visited = new();
    }

    private static readonly Dictionary<string, MarkSet> _sets = new(StringComparer.Ordinal);
    private static readonly HashSet<string> _warned = new();

    /// <summary>True when at least one mark of this id is showing.</summary>
    internal static bool AnyShown(string id)
    {
        if (!_sets.TryGetValue(id, out var set))
        {
            return false;
        }

        foreach (var kv in set.Objects)
        {
            if (kv.Value != null && kv.Value.activeSelf)
            {
                return true;
            }
        }

        return false;
    }

    internal static void BeginPass(string id)
    {
        Get(id).Visited.Clear();
    }

    /// <summary>
    /// Shows or hides mark <paramref name="id"/> on <paramref name="visuals"/>. Creates the mark the first time
    /// it is shown on that CardGraphics; never creates anything to hide.
    /// </summary>
    internal static void Set(string id, CardGraphics visuals, bool show, Sprite sprite, MarkCorner corner)
    {
        if (visuals == null)
        {
            return;
        }

        var set = Get(id);
        set.Visited.Add(visuals);
        set.Objects.TryGetValue(visuals, out var go);
        if (go == null)
        {
            if (!show)
            {
                return;
            }

            go = Create(id, visuals, sprite, corner);
            if (go == null)
            {
                return;
            }

            set.Objects[visuals] = go;
        }

        if (go.activeSelf != show)
        {
            go.SetActive(show);
        }

        if (show)
        {
            go.transform.SetAsLastSibling(); // drawn above the card's own children
        }
    }

    /// <summary>Hides every mark of this id whose card was not visited since BeginPass, and forgets destroyed ones.</summary>
    internal static void EndPass(string id)
    {
        var set = Get(id);
        List<CardGraphics> dead = null;
        foreach (var kv in set.Objects)
        {
            if (kv.Key == null || kv.Value == null)
            {
                (dead ??= new List<CardGraphics>()).Add(kv.Key);
                continue;
            }

            if (!set.Visited.Contains(kv.Key) && kv.Value.activeSelf)
            {
                kv.Value.SetActive(false);
            }
        }

        if (dead != null)
        {
            foreach (var k in dead)
            {
                set.Objects.Remove(k);
            }
        }
    }

    private static MarkSet Get(string id)
    {
        if (!_sets.TryGetValue(id, out var set))
        {
            set = new MarkSet();
            _sets[id] = set;
        }

        return set;
    }

    private static GameObject Create(string id, CardGraphics visuals, Sprite sprite, MarkCorner corner)
    {
        try
        {
            if (visuals.transform is not RectTransform)
            {
                WarnOnce("not-rect", "[UB] A card's CardGraphics has no RectTransform; card marks are not drawn.");
                return null;
            }

            var go = new GameObject("UB_Mark_" + id, typeof(RectTransform));
            go.layer = visuals.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(visuals.transform, false);

            // A fixed fraction of the card, so it scales with the card. The top corners sit just under the
            // title bar and the bottom ones just above the durability row, inside the art (measured on a
            // 1280x720 screenshot, 2026-10-10: the title takes the top ~17% of the card, the durability
            // row the bottom ~13%), so a mark never covers the card's name or its numbers.
            const float w = 0.24f, h = 0.18f, side = 0.05f, top = 0.19f, bottom = 0.15f;
            float x0 = corner is MarkCorner.TopLeft or MarkCorner.BottomLeft ? side : 1f - side - w;
            float y0 = corner is MarkCorner.BottomLeft or MarkCorner.BottomRight ? bottom : 1f - top - h;
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x0 + w, y0 + h);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;

            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false; // never steals a click or a hover from the card
            go.SetActive(false);
            return go;
        }
        catch (Exception ex)
        {
            WarnOnce("create:" + ex.GetType().Name, $"[UB] Could not draw a card mark ({ex.GetType().Name}: {ex.Message}); marks are not shown.");
            return null;
        }
    }

    private static void WarnOnce(string cause, string message)
    {
        if (_warned.Add(cause))
        {
            Plugin.Logger.LogWarning(message);
        }
    }
}
