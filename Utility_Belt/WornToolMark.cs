using UnityEngine;

namespace Utility_Belt;

/// <summary>
/// [Display] Show Worn Tool Mark (plan B11): a small cracked-blade mark in the bottom left corner of a tool
/// whose Usage has fallen below [Display] Worn Tool Threshold percent, so a tool about to break reads apart
/// at a glance. Vanilla shows the Usage number but never colours or flags it.
/// </summary>
/// <remarks>
/// <para>Which cards (plan Q4): an Item card (CardTypes.Item) whose CardData.UsageDurability is wear
/// (<see cref="IsWearStat"/>), anywhere on screen. No tool tag exists in vanilla, and "has an active Usage stat"
/// alone also caught the foods' Drying counter (0.1.4). The percentage is the game's own
/// <c>InGameCardBase.CurrentUsagePercent</c>.</para>
/// <para>Drawn exactly as the sealed mark (B6): a pass four times a second from Plugin.Update over
/// GameManager.AllCards, through <see cref="CardMark"/> in the free bottom left corner (lock top left, sealed
/// top right). With the option off and nothing drawn, no pass runs. Silent: nothing is logged per card,
/// which keeps it clear of the as-built's Considered row 11 (per-item warnings).</para>
/// </remarks>
internal static class WornToolMark
{
    internal const string MarkId = "Worn";

    private static readonly HashSet<string> _warned = new();
    private static float _nextPass;
    private static Sprite _sprite;

    internal static void Tick()
    {
        if (Time.unscaledTime < _nextPass)
        {
            return;
        }

        _nextPass = Time.unscaledTime + 0.25f;
        Refresh();
    }

    /// <summary>True when <paramref name="card"/> is a tool worn below <paramref name="thresholdPercent"/>.</summary>
    internal static bool IsWorn(InGameCardBase card, int thresholdPercent)
    {
        if (card == null || card.Destroyed)
        {
            return false;
        }

        var model = card.CardModel;
        if (model == null || model.CardType != CardTypes.Item || !IsWearStat(model.UsageDurability))
        {
            return false;
        }

        return card.CurrentUsagePercent * 100f < thresholdPercent;
    }

    /// <summary>
    /// True when a card's Usage stat is wear: active, with a maximum, acting when it runs out and not when it
    /// fills, and keeping the default Usage name. Vanilla reuses the Usage slot as a renamed counter on many items
    /// (EA 0.69c export, 2026-10-18): 62 foods count Drying UP to an OnFull action (Raw Meat starts at 0 of 24, so
    /// it read 0 percent and was marked), and wood, livestock and others rename it Fragrance, Thirst, Meat,
    /// Amount or Essence. Wear keeps the name: empty on tools, "Usage Durability" on clothing.
    /// </summary>
    internal static bool IsWearStat(DurabilityStat usage)
    {
        if (!usage || usage.Max <= 0f || !usage.HasActionOnZero || usage.HasActionOnFull)
        {
            return false;
        }

        var label = usage.CardStatName.DefaultText;
        return string.IsNullOrEmpty(label) || label == "Usage Durability";
    }

    private static void Refresh()
    {
        var on = Plugin.ShowWornToolMark.Value;
        if (!on && !CardMark.AnyShown(MarkId))
        {
            return; // option off and nothing drawn: no work
        }

        try
        {
            CardMark.BeginPass(MarkId);
            if (on)
            {
                var gm = MBSingleton<GameManager>.Instance;
                if (gm != null && gm.AllCards != null)
                {
                    var threshold = Plugin.WornToolThreshold.Value;
                    var sprite = GetSprite();
                    foreach (var card in gm.AllCards)
                    {
                        if (card == null || card.CardVisuals == null)
                        {
                            continue;
                        }

                        CardMark.Set(MarkId, card.CardVisuals, IsWorn(card, threshold), sprite, MarkCorner.BottomLeft);
                    }
                }
            }

            CardMark.EndPass(MarkId); // hides every mark not visited this pass (all of them when the option is off)
        }
        catch (Exception ex)
        {
            WarnOnce("marks:" + ex.GetType().Name, $"[UB] Worn tool marks failed ({ex.GetType().Name}: {ex.Message}); no worn tool mark is drawn.");
        }
    }

    private static Sprite GetSprite()
    {
        if (_sprite != null)
        {
            return _sprite;
        }

        // A 36x36 mark drawn in code once: an amber warning disc with an ink rim and a dark zig-zag crack
        // across it, so it reads apart from the gold padlock and the red wax seal.
        const int s = 36;
        var px = new Color32[s * s];
        var clear = new Color32(0, 0, 0, 0);
        var ink = new Color32(40, 26, 10, 255);
        var amber = new Color32(240, 150, 30, 255);
        var light = new Color32(255, 214, 120, 255);
        for (var y = 0; y < s; y++)
        {
            for (var x = 0; x < s; x++)
            {
                var c = clear;
                float fx = x + 0.5f, fy = y + 0.5f;
                float dx = fx - 18f, dy = fy - 18f;
                var d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d <= 16f)
                {
                    c = d > 14f ? ink : (d > 11.5f ? amber : light);

                    // A zig-zag crack from top left to bottom right: three straight legs.
                    float crack;
                    if (fx < 14f)
                    {
                        crack = Mathf.Abs(fy - (30f - (fx - 6f) * 1.2f));
                    }
                    else if (fx < 22f)
                    {
                        crack = Mathf.Abs(fy - (20.4f + (fx - 14f) * 0.6f));
                    }
                    else
                    {
                        crack = Mathf.Abs(fy - (25.2f - (fx - 22f) * 1.4f));
                    }

                    if (d <= 14f && crack <= 1.4f)
                    {
                        c = ink;
                    }
                }

                px[y * s + x] = c;
            }
        }

        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontUnloadUnusedAsset,
            name = "UB_WornMark",
        };
        tex.SetPixels32(px);
        tex.Apply(false, false);
        _sprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
        _sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
        _sprite.name = "UB_WornMark";
        return _sprite;
    }

    private static void WarnOnce(string cause, string message)
    {
        if (_warned.Add(cause))
        {
            Plugin.Logger.LogWarning(message);
        }
    }
}
