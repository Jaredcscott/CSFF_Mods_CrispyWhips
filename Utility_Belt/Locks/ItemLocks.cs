using System.Globalization;
using UnityEngine;

namespace Utility_Belt.Locks;

/// <summary>
/// Item lock (plan B1 steps 1 and 2, UB host half): the slot switch and prune at each run start, the
/// [Hotkeys] Lock Toggle key, and the lock mark on locked cards.
/// </summary>
/// <remarks>
/// Run start: a GameManager.Awake postfix (Patcher/ItemLockPatch.cs) records whether the run is a new game
/// (CurrentSaveData.HasCardsData false, read before FinishInitializing replaces it with the first autosave),
/// then GameManager.OnGMInitialized, which fires once per run start after the board is built, loads the
/// slot's file and prunes it. A new game in a slot drops that slot's locks (a fresh character's starting
/// cards are made on tick 0 and would otherwise match a lock left by the slot's last game). A load keeps a
/// lock only while some card in the save still has its key: the live cards (GameManager.AllCards, which
/// holds the current board, carried and worn cards and everything inside them) plus every card recorded in
/// GameManager.EnvironmentsData and the run's save data, so a lock on a card in a chest at another place
/// survives. If that walk fails or finds no card at all, nothing is pruned.
/// </remarks>
internal static class ItemLocks
{
    internal const string MarkId = "Lock";

    private static readonly HashSet<string> _warned = new();
    private static GameManager _gm;
    private static bool _pendingNewGame;
    private static float _nextMarkPass;
    private static Sprite _lockSprite;

    /// <summary>Called by the GameManager.Awake postfix.</summary>
    internal static void OnGameManagerAwake(GameManager gm)
    {
        _gm = gm;
        try
        {
            var data = gm.CurrentSaveData;
            _pendingNewGame = data != null && !data.HasCardsData;
        }
        catch (Exception ex)
        {
            _pendingNewGame = false;
            WarnOnce("newgame:" + ex.GetType().Name, $"[UB] Item locks: could not tell a new game from a load ({ex.GetType().Name}: {ex.Message}); this run is treated as a load (locks are pruned, not cleared).");
        }
    }

    /// <summary>Subscribed to GameManager.OnGMInitialized once, from Plugin.Awake.</summary>
    internal static void OnRunStarted()
    {
        try
        {
            var gl = GameLoad.Instance;
            if (gl == null)
            {
                WarnOnce("gameload-null", "[UB] Item locks: GameLoad.Instance is null at run start; no locks this run.");
                LockStore.LoadSlot(LockStore.NoSlot);
                return;
            }

            var slot = gl.CurrentGameDataIndex;
            LockStore.LoadSlot(slot < 0 ? LockStore.NoSlot : slot);
            if (LockStore.Slot == LockStore.NoSlot)
            {
                return;
            }

            if (_pendingNewGame)
            {
                var had = LockStore.Count;
                LockStore.Clear();
                Plugin.Logger.LogDebug($"[UB] Item locks: new game in slot {slot}, {had} lock(s) from the slot's last game cleared.");
                return;
            }

            if (LockStore.Count == 0)
            {
                Plugin.Logger.LogDebug($"[UB] Item locks: slot {slot}, no locks.");
                return;
            }

            var gm = _gm != null ? _gm : MBSingleton<GameManager>.Instance;
            if (!CollectSaveKeys(gm, out var live))
            {
                return; // warned inside; keep every lock rather than guess
            }

            var before = LockStore.Count;
            var gone = LockStore.Prune(live);
            Plugin.Logger.LogDebug($"[UB] Item locks: slot {slot}, {before - gone} kept, {gone} pruned (their cards are gone), {live.Count} card key(s) in the save.");
        }
        catch (Exception ex)
        {
            WarnOnce("runstart:" + ex.GetType().Name, $"[UB] Item locks: run start failed ({ex.GetType().Name}: {ex.Message}); locks may not apply this run.");
        }
        finally
        {
            _pendingNewGame = false;
            _nextMarkPass = 0f;
        }
    }

    /// <summary>The (CardID, CreatedOnTick) key of every card in the run, live or saved.</summary>
    private static bool CollectSaveKeys(GameManager gm, out HashSet<string> keys)
    {
        keys = new HashSet<string>(StringComparer.Ordinal);
        if (gm == null)
        {
            WarnOnce("prune-gm-null", "[UB] Item locks: no GameManager at run start, so no lock is pruned this run.");
            return false;
        }

        if (gm.AllCards == null)
        {
            WarnOnce("prune-allcards-null", "[UB] Item locks: GameManager.AllCards is null at run start, so no lock is pruned this run.");
            return false;
        }

        if (gm.EnvironmentsData == null)
        {
            WarnOnce("prune-envdata-null", "[UB] Item locks: GameManager.EnvironmentsData is null at run start, so no lock is pruned this run.");
            return false;
        }

        foreach (var card in gm.AllCards)
        {
            if (card != null && card.CardModel != null)
            {
                keys.Add(LockStore.Key(card.CardModel.UniqueID, card.CreatedOnTick));
            }
        }

        foreach (var env in gm.EnvironmentsData.Values)
        {
            if (env == null)
            {
                continue;
            }

            AddAll(keys, env.GetRegularCards);
            AddAll(keys, env.GetInventoryCards);
            AddAll(keys, env.NestedInventoryCards);
        }

        var save = gm.CurrentSaveData;
        if (save != null)
        {
            AddAll(keys, save.CurrentCardsData);
            AddAll(keys, save.CurrentInventoryCards);
            AddAll(keys, save.CurrentNestedInventoryCards);
        }

        if (keys.Count == 0)
        {
            WarnOnce("prune-empty", "[UB] Item locks: found no card at all in the run, so no lock is pruned this run.");
            return false;
        }

        return true;
    }

    private static void AddAll<T>(HashSet<string> keys, List<T> list) where T : CardSaveDataByReference
    {
        if (list == null)
        {
            return;
        }

        foreach (var d in list)
        {
            Add(keys, d);
        }
    }

    private static void Add(HashSet<string> keys, CardSaveDataByReference d)
    {
        if (d == null)
        {
            return;
        }

        var id = UniqueIDScriptable.LoadID(d.CardID);
        if (!string.IsNullOrEmpty(id))
        {
            keys.Add(LockStore.Key(id, d.CreatedOnTick));
        }

        if (d is InventoryCardSaveDataByReference inv)
        {
            Add(keys, inv.LiquidContainedRef);
            if (inv.CardsInInventoryRef != null)
            {
                foreach (var slot in inv.CardsInInventoryRef)
                {
                    if (slot != null)
                    {
                        Add(keys, slot.RegularCardRef);
                    }
                }
            }
        }
    }

    /// <summary>Plugin.Update: the toggle key, then the mark pass.</summary>
    internal static void Tick()
    {
        var key = Plugin.LockToggleKey.Value;
        if (key != KeyCode.None && Input.GetKeyDown(key) && !GraphicsManager.PlayerIsTyping)
        {
            ToggleHovered();
        }

        if (Time.unscaledTime >= _nextMarkPass)
        {
            _nextMarkPass = Time.unscaledTime + 0.25f;
            RefreshMarks();
        }
    }

    private static void ToggleHovered()
    {
        try
        {
            if (LockStore.Slot == LockStore.NoSlot)
            {
                return; // main menu, or no run yet
            }

            var hovered = GameManager.HoveredCard;
            if (hovered == null || hovered.CardModel == null)
            {
                Plugin.Logger.LogDebug("[UB] Lock Toggle: no card is hovered.");
                return;
            }

            var gm = MBSingleton<GraphicsManager>.Instance;
            var open = gm != null ? gm.InspectedCard : null;
            if (open == null || !open.IsInventoryCard)
            {
                Plugin.Logger.LogDebug("[UB] Lock Toggle: no container is open.");
                return;
            }

            if (hovered.CurrentContainer != open)
            {
                Plugin.Logger.LogDebug("[UB] Lock Toggle: the hovered card is not inside the open container.");
                return;
            }

            var locked = LockStore.Toggle(hovered.CardModel.UniqueID, hovered.CreatedOnTick);
            Plugin.Logger.LogDebug($"[UB] Lock Toggle: {hovered.CardModel.name} created on tick {hovered.CreatedOnTick.ToString(CultureInfo.InvariantCulture)} is now {(locked ? "locked" : "unlocked")}.");
            _nextMarkPass = 0f; // redraw the marks on the next frame
        }
        catch (Exception ex)
        {
            WarnOnce("toggle:" + ex.GetType().Name, $"[UB] Lock Toggle failed ({ex.GetType().Name}: {ex.Message}).");
        }
    }

    private static void RefreshMarks()
    {
        if (LockStore.Count == 0 && !CardMark.AnyShown(MarkId))
        {
            return; // nothing locked, nothing drawn: no work
        }

        try
        {
            CardMark.BeginPass(MarkId);
            var gm = _gm;
            if (gm != null && gm.AllCards != null)
            {
                var sprite = LockSprite();
                foreach (var card in gm.AllCards)
                {
                    if (card == null || card.CardVisuals == null)
                    {
                        continue;
                    }

                    var show = card.CardModel != null && !card.Destroyed
                        && LockStore.Contains(card.CardModel.UniqueID, card.CreatedOnTick);
                    CardMark.Set(MarkId, card.CardVisuals, show, sprite, MarkCorner.TopLeft);
                }
            }

            CardMark.EndPass(MarkId);
        }
        catch (Exception ex)
        {
            WarnOnce("marks:" + ex.GetType().Name, $"[UB] Lock marks failed ({ex.GetType().Name}: {ex.Message}); locks still apply.");
        }
    }

    /// <summary>A 32x40 padlock drawn in code once: a dark shackle over a gold body with a keyhole.</summary>
    private static Sprite LockSprite()
    {
        if (_lockSprite != null)
        {
            return _lockSprite;
        }

        const int w = 32, h = 40;
        var px = new Color32[w * h];
        var clear = new Color32(0, 0, 0, 0);
        var ink = new Color32(30, 22, 14, 255);
        var gold = new Color32(236, 186, 58, 255);
        var steel = new Color32(150, 150, 160, 255);
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var c = clear;
                float fx = x + 0.5f, fy = y + 0.5f;

                // Shackle: a half ring centred over the body, with legs reaching into it.
                var dx = fx - 16f;
                var dy = fy - 25f;
                var d = Mathf.Sqrt(dx * dx + dy * dy);
                var inRing = (fy >= 25f && d >= 5f && d <= 11f) || (fy < 25f && fy >= 18f && Mathf.Abs(dx) >= 5f && Mathf.Abs(dx) <= 11f);
                if (inRing)
                {
                    var edge = (fy >= 25f) ? (d < 6.5f || d > 9.5f) : (Mathf.Abs(dx) < 6.5f || Mathf.Abs(dx) > 9.5f);
                    c = edge ? ink : steel;
                }

                // Body: a rounded gold block with an ink rim.
                if (y < 21 && x >= 2 && x < 30)
                {
                    var rim = y < 2 || y >= 19 || x < 4 || x >= 28;
                    c = rim ? ink : gold;

                    // Keyhole: a dot and a slot.
                    var kx = fx - 16f;
                    var ky = fy - 12.5f;
                    if (kx * kx + ky * ky <= 7f || (Mathf.Abs(kx) <= 1.2f && fy >= 5f && fy <= 12.5f))
                    {
                        c = ink;
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
            name = "UB_LockMark",
        };
        tex.SetPixels32(px);
        tex.Apply(false, false);
        _lockSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        _lockSprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
        _lockSprite.name = "UB_LockMark";
        return _lockSprite;
    }

    private static void WarnOnce(string cause, string message)
    {
        if (_warned.Add(cause))
        {
            Plugin.Logger.LogWarning(message);
        }
    }
}
