using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Utility_Belt;

/// <summary>
/// [Hotkeys] Pin Blueprint and [General] Pinned Blueprints (plan B13). In the blueprint screen, hover a
/// blueprint and press the key to pin it: it is shown first in every tab and sub-tab that holds it and in
/// Search results, with a small pin mark. Press it again to unpin. Pins are one global list in the config
/// (plan Q5), so they hold across saves and restarts.
/// </summary>
/// <remarks>
/// <para>Hover: <c>InGameCardBase.OnHoverEnter</c> sets <c>GameManager.HoveredCard</c> for every card,
/// blueprint models in the screen included (read 2026-10-15, EA 0.69c), so no line-hover fallback is
/// needed. A blueprint model is a CardTypes.Blueprint card that is not a placed blueprint instance.</para>
/// <para>Order: postfixes on <c>BlueprintModelsScreen.ShowTab</c>, <c>ShowSubTab</c> and <c>SetupSearch</c> (all
/// private) re-sort <c>BlueprintsLine</c> with <c>CardLine.SortSlots</c>: pinned slots first, everything else in
/// the order vanilla just gave it (a stable sort on the current position, which is vanilla's
/// BlueprintSortingIndex or, in Search, its SortByTab). Then <c>Slots[i].Index = i</c>, as vanilla does after
/// its own sort. Active flags are left as vanilla set them, and <c>ShopSortingList</c> and
/// <c>IncludedCards</c> are never touched.</para>
/// <para>Safety: order is cosmetic. Research state is <c>CheckedBlueprints</c> and the model states, and
/// vanilla reassigns slot Index on every tab show, so a pin changes no research or save data.</para>
/// <para>Toggling re-shows the current tab through vanilla's own ShowTab, or, in Search, re-runs its SortByTab
/// comparison, so an unpinned blueprint goes straight back to its vanilla place.</para>
/// </remarks>
internal static class BlueprintPins
{
    internal const string MarkId = "Pin";

    private static readonly HashSet<string> _warned = new();
    private static readonly HashSet<string> _pins = new(StringComparer.Ordinal);
    private static string _pinsRaw;
    private static float _nextPass;
    private static Sprite _sprite;

    private static FieldInfo _lineField;
    private static FieldInfo _currentTabField;
    private static MethodInfo _showTab;
    private static MethodInfo _sortByTab;
    private static bool _resolved;

    internal static void ApplyPatch(Harmony harmony)
    {
        var post = new HarmonyMethod(typeof(BlueprintPins), nameof(AfterVanillaSort));
        foreach (var name in new[] { "ShowTab", "ShowSubTab", "SetupSearch" })
        {
            var m = AccessTools.Method(typeof(BlueprintModelsScreen), name);
            if (m == null)
            {
                Plugin.Logger.LogWarning($"[UB] BlueprintModelsScreen.{name} not found; pinned blueprints are not moved there.");
                continue;
            }

            harmony.Patch(m, postfix: post);
        }
    }

    private static void AfterVanillaSort(BlueprintModelsScreen __instance)
    {
        try
        {
            Reorder(__instance);
        }
        catch (Exception ex)
        {
            WarnOnce("reorder:" + ex.GetType().Name, $"[UB] Pinned blueprints: reorder failed ({ex.GetType().Name}: {ex.Message}); the blueprint screen keeps the game's order.");
        }
    }

    /// <summary>True when the blueprint's UniqueID is in the pinned list.</summary>
    internal static bool IsPinned(CardData model)
    {
        ParsePins();
        return model != null && _pins.Contains(model.UniqueID);
    }

    /// <summary>Plugin.Update: the toggle key, then the pin mark pass.</summary>
    internal static void Tick()
    {
        var key = Plugin.PinBlueprintKey.Value;
        if (key != KeyCode.None && Input.GetKeyDown(key) && !GraphicsManager.PlayerIsTyping)
        {
            try
            {
                ToggleHovered();
            }
            catch (Exception ex)
            {
                WarnOnce("toggle:" + ex.GetType().Name, $"[UB] Pin Blueprint failed ({ex.GetType().Name}: {ex.Message}).");
            }
        }

        if (Time.unscaledTime >= _nextPass)
        {
            _nextPass = Time.unscaledTime + 0.25f;
            RefreshMarks();
        }
    }

    private static void ToggleHovered()
    {
        var gm = MBSingleton<GraphicsManager>.Instance;
        var screen = gm != null ? gm.BlueprintModelsPopup : null;
        if (screen == null || !screen.gameObject.activeInHierarchy)
        {
            Plugin.Logger.LogDebug("[UB] Pin Blueprint: the blueprint screen is not open.");
            return;
        }

        var hovered = GameManager.HoveredCard;
        if (hovered == null || hovered.CardModel == null || hovered.CardModel.CardType != CardTypes.Blueprint || hovered.IsBlueprintInstance)
        {
            Plugin.Logger.LogDebug("[UB] Pin Blueprint: no blueprint is hovered.");
            return;
        }

        Toggle(hovered.CardModel.UniqueID);
        Reshow(screen);
    }

    /// <summary>Adds or removes <paramref name="uid"/> in the config list; returns true when it is now pinned.</summary>
    internal static bool Toggle(string uid)
    {
        ParsePins();
        var pinned = !_pins.Remove(uid);
        if (pinned)
        {
            _pins.Add(uid);
        }

        Plugin.PinnedBlueprints.Value = string.Join(",", _pins.OrderBy(x => x, StringComparer.Ordinal));
        _pinsRaw = Plugin.PinnedBlueprints.Value;
        _nextPass = 0f;
        Plugin.Logger.LogDebug($"[UB] Pin Blueprint: {uid} is now {(pinned ? "pinned" : "unpinned")}.");
        return pinned;
    }

    /// <summary>Re-runs vanilla's order for what the screen shows now, which the postfix then pins.</summary>
    internal static void Reshow(BlueprintModelsScreen screen)
    {
        if (!Resolve())
        {
            return;
        }

        var tab = (int)_currentTabField.GetValue(screen);
        if (tab == -1)
        {
            // Search: ShowTab(-1) while already in Search would switch to tab 0, so re-sort as SetupSearch does.
            var line = _lineField.GetValue(screen) as CardLine;
            if (line != null)
            {
                var cmp = (Comparison<DynamicLayoutSlot>)Delegate.CreateDelegate(typeof(Comparison<DynamicLayoutSlot>), screen, _sortByTab);
                line.SortSlots(cmp);
                Reorder(screen);
            }

            return;
        }

        _showTab.Invoke(screen, new object[] { tab });
    }

    /// <summary>Pinned slots first, the rest in their current (vanilla) order; then Index = position.</summary>
    internal static void Reorder(BlueprintModelsScreen screen)
    {
        if (screen == null || !Resolve())
        {
            return;
        }

        var line = _lineField.GetValue(screen) as CardLine;
        if (line == null || line.Slots == null || line.Slots.Count == 0)
        {
            return;
        }

        ParsePins();
        if (_pins.Count > 0)
        {
            var pos = new Dictionary<DynamicLayoutSlot, int>(line.Slots.Count);
            for (var i = 0; i < line.Slots.Count; i++)
            {
                pos[line.Slots[i]] = i;
            }

            line.SortSlots((a, b) =>
            {
                var pa = IsPinnedSlot(a);
                var pb = IsPinnedSlot(b);
                return pa != pb ? (pa ? -1 : 1) : pos[a].CompareTo(pos[b]);
            });
        }

        for (var i = 0; i < line.Slots.Count; i++)
        {
            line.Slots[i].Index = i;
        }
    }

    private static bool IsPinnedSlot(DynamicLayoutSlot slot)
    {
        var card = slot?.AssignedCard;
        return card != null && card.CardModel != null && _pins.Contains(card.CardModel.UniqueID);
    }

    private static bool Resolve()
    {
        if (_resolved)
        {
            return _lineField != null && _currentTabField != null && _showTab != null && _sortByTab != null;
        }

        _resolved = true;
        _lineField = AccessTools.Field(typeof(BlueprintModelsScreen), "BlueprintsLine");
        _currentTabField = AccessTools.Field(typeof(BlueprintModelsScreen), "CurrentTab");
        _showTab = AccessTools.Method(typeof(BlueprintModelsScreen), "ShowTab", new[] { typeof(int) });
        _sortByTab = AccessTools.Method(typeof(BlueprintModelsScreen), "SortByTab", new[] { typeof(DynamicLayoutSlot), typeof(DynamicLayoutSlot) });
        if (_lineField == null)
        {
            WarnOnce("no-line", "[UB] Pinned blueprints: BlueprintModelsScreen.BlueprintsLine not found; pins do nothing.");
        }

        if (_currentTabField == null)
        {
            WarnOnce("no-tab", "[UB] Pinned blueprints: BlueprintModelsScreen.CurrentTab not found; pins do nothing.");
        }

        if (_showTab == null)
        {
            WarnOnce("no-showtab", "[UB] Pinned blueprints: BlueprintModelsScreen.ShowTab(int) not found; pins do nothing.");
        }

        if (_sortByTab == null)
        {
            WarnOnce("no-sortbytab", "[UB] Pinned blueprints: BlueprintModelsScreen.SortByTab not found; pins do nothing.");
        }

        return _lineField != null && _currentTabField != null && _showTab != null && _sortByTab != null;
    }

    private static void ParsePins()
    {
        var raw = Plugin.PinnedBlueprints.Value ?? string.Empty;
        if (string.Equals(raw, _pinsRaw, StringComparison.Ordinal))
        {
            return;
        }

        _pinsRaw = raw;
        _pins.Clear();
        foreach (var part in raw.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            _pins.Add(part.Trim());
        }
    }

    private static void RefreshMarks()
    {
        var gm = MBSingleton<GraphicsManager>.Instance;
        var screen = gm != null ? gm.BlueprintModelsPopup : null;
        var open = screen != null && screen.gameObject.activeInHierarchy;
        ParsePins();
        if ((!open || _pins.Count == 0) && !CardMark.AnyShown(MarkId))
        {
            return;
        }

        try
        {
            CardMark.BeginPass(MarkId);
            if (open && _pins.Count > 0 && Resolve() && _lineField.GetValue(screen) is CardLine line && line.Slots != null)
            {
                var sprite = GetSprite();
                foreach (var slot in line.Slots)
                {
                    var card = slot?.AssignedCard;
                    if (card == null || card.CardVisuals == null)
                    {
                        continue;
                    }

                    CardMark.Set(MarkId, card.CardVisuals, IsPinnedSlot(slot), sprite, MarkCorner.TopLeft);
                }
            }

            CardMark.EndPass(MarkId);
        }
        catch (Exception ex)
        {
            WarnOnce("marks:" + ex.GetType().Name, $"[UB] Pin marks failed ({ex.GetType().Name}: {ex.Message}); pins still apply.");
        }
    }

    private static Sprite GetSprite()
    {
        if (_sprite != null)
        {
            return _sprite;
        }

        // A 32x40 push pin drawn in code once: a red round head with a highlight over a short steel needle.
        const int w = 32, h = 40;
        var px = new Color32[w * h];
        var clear = new Color32(0, 0, 0, 0);
        var ink = new Color32(40, 16, 16, 255);
        var red = new Color32(210, 50, 44, 255);
        var shine = new Color32(250, 170, 160, 255);
        var steel = new Color32(170, 170, 180, 255);
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var c = clear;
                float fx = x + 0.5f, fy = y + 0.5f;

                // Needle: a thin vertical line under the head.
                if (fy < 18f && Mathf.Abs(fx - 16f) <= 1.6f - fy * 0.05f)
                {
                    c = Mathf.Abs(fx - 16f) > 0.8f ? ink : steel;
                }

                // Head: a disc.
                float dx = fx - 16f, dy = fy - 27f;
                var d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d <= 11f)
                {
                    c = d > 9.4f ? ink : red;
                    if ((fx - 12.5f) * (fx - 12.5f) + (fy - 30.5f) * (fy - 30.5f) <= 6f)
                    {
                        c = shine;
                    }
                }

                px[y * w + x] = c;
            }
        }

        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontUnloadUnusedAsset,
            name = "UB_PinMark",
        };
        tex.SetPixels32(px);
        tex.Apply(false, false);
        _sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        _sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
        _sprite.name = "UB_PinMark";
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
