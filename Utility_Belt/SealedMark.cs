using UnityEngine;

namespace Utility_Belt;

/// <summary>
/// Sealed-container mark (plan B6): a small wax seal drawn on the card of a sealed storage pot or
/// fermentation bin, so a sealed one reads apart from an unsealed one on the board without opening either.
/// </summary>
/// <remarks>
/// Display only: no vanilla data changes. The set of sealed cards is the [Display] Sealed Cards list, not the
/// vanilla tag tag_ContainerSealed, because the tag does not mark the sealed state (R10, measured 2026-10-11:
/// it sits on the Sealed AND the Unsealed Storage Pot, item and placed, and on the Sealed Fermentation Bin
/// only). The mark is drawn by <see cref="CardMark"/> (the lock mark's helper) in the top right corner, the
/// lock sitting top left, and every pass hides the mark on a card that is no longer in the set or no longer
/// visited, so a recycled card never shows the last owner's mark.
/// </remarks>
internal static class SealedMark
{
    internal const string MarkId = "Sealed";

    /// <summary>ClayStoragePotSealed, ClayStoragePotPlacedSealed and FermentationBinSealed (EA 0.68b).</summary>
    internal const string DefaultSealedCards = "3af3dec775f09ad47a5f73ddd13ed228,a3713cbe65bca38419b8f4119a3c186c,647857cd2de994546971b1e6526893e3";

    private static readonly HashSet<string> _warned = new();
    private static readonly HashSet<string> _set = new(StringComparer.OrdinalIgnoreCase);
    private static string _setRaw;
    private static float _nextPass;
    private static Sprite _sprite;

    /// <summary>Plugin.Update: one pass four times a second while the option is on, or while a mark is still showing.</summary>
    internal static void Tick()
    {
        if (Time.unscaledTime < _nextPass)
        {
            return;
        }

        _nextPass = Time.unscaledTime + 0.25f;
        Refresh();
    }

    private static void Refresh()
    {
        var on = Plugin.ShowSealedMark.Value;
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
                    ParseSet(Plugin.SealedCards.Value);
                    var sprite = GetSprite();
                    foreach (var card in gm.AllCards)
                    {
                        if (card == null || card.CardVisuals == null)
                        {
                            continue;
                        }

                        var show = card.CardModel != null && !card.Destroyed && _set.Contains(card.CardModel.UniqueID);
                        CardMark.Set(MarkId, card.CardVisuals, show, sprite, MarkCorner.TopRight);
                    }
                }
            }

            CardMark.EndPass(MarkId); // hides every mark not visited this pass (all of them when the option is off)
        }
        catch (Exception ex)
        {
            WarnOnce("marks:" + ex.GetType().Name, $"[UB] Sealed marks failed ({ex.GetType().Name}: {ex.Message}); no sealed mark is drawn.");
        }
    }

    /// <summary>Re-parses the comma list only when its text changed.</summary>
    private static void ParseSet(string raw)
    {
        raw ??= string.Empty;
        if (string.Equals(raw, _setRaw, StringComparison.Ordinal))
        {
            return;
        }

        _setRaw = raw;
        _set.Clear();
        foreach (var part in raw.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            _set.Add(part.Trim());
        }

        Plugin.Logger.LogDebug($"[UB] Sealed marks: {_set.Count} card id(s) in the sealed set.");
    }

    private static Sprite GetSprite()
    {
        if (_sprite != null)
        {
            return _sprite;
        }

        // A 36x36 wax seal drawn in code once: a crimson disc with an ink rim, a darker inner ring, a pale
        // diamond stamp and two ribbon tails, so it reads apart from the lock's gold padlock at a glance.
        const int s = 36;
        var px = new Color32[s * s];
        var clear = new Color32(0, 0, 0, 0);
        var ink = new Color32(48, 14, 16, 255);
        var wax = new Color32(190, 38, 42, 255);
        var dark = new Color32(142, 24, 30, 255);
        var shine = new Color32(238, 132, 120, 255);
        var ribbon = new Color32(206, 170, 96, 255);
        for (var y = 0; y < s; y++)
        {
            for (var x = 0; x < s; x++)
            {
                var c = clear;
                float fx = x + 0.5f, fy = y + 0.5f;

                // Ribbon tails under the disc.
                var inTail = fy < 14f && ((fx >= 9f && fx < 16f) || (fx >= 20f && fx < 27f));
                if (inTail)
                {
                    var edge = fy < 1.5f || fx < 10.5f || (fx >= 14.5f && fx < 21.5f) || fx >= 25.5f;
                    c = edge ? ink : ribbon;
                }

                // Disc.
                float dx = fx - 18f, dy = fy - 21f;
                var d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d <= 14f)
                {
                    c = d > 12.2f ? ink : (d > 8.6f && d <= 10.2f ? dark : wax);

                    // Pale diamond stamp in the middle.
                    if (d <= 8.6f && Mathf.Abs(dx) + Mathf.Abs(dy) <= 5.2f)
                    {
                        c = Mathf.Abs(dx) + Mathf.Abs(dy) <= 3f ? dark : shine;
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
            name = "UB_SealedMark",
        };
        tex.SetPixels32(px);
        tex.Apply(false, false);
        _sprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
        _sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
        _sprite.name = "UB_SealedMark";
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
