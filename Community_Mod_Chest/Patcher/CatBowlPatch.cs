using System;
using System.Collections.Generic;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// E3, the Cat Bowl. A tamed cat (Ash or Shadow) on the board the player is on eats from a Cat Bowl on that same
    /// board when its Hunger (SpoilageTime, 0 to 576, one a tick) falls to <see cref="HungerThreshold"/> of full: one
    /// card is taken out of the bowl through <c>CardUtil.TryRemoveCard</c> (never Object.Destroy), the cat's Hunger
    /// is raised by <see cref="HungerRestore"/> (the same 288 a hand Feed gives) and its Care (SpecialDurability3)
    /// drops by <see cref="CareLossPerDrink"/> times <see cref="CareLossScale"/>, the cistern's bargain
    /// (<c>AshCatTickPatch.CareLossPerDrink</c>): self-serve is a convenience, never free.
    ///
    /// <para>What the bowl accepts is JSON (the card's InventoryFilter, the same sixteen meat and fish cards the cats'
    /// own Feed interaction takes); this class owns only the meal. Both the cat and the bowl are read from
    /// <c>GameQuery.CardsInPlayerEnv</c>, so a cat and a bowl on different boards never meet. The meal is skipped
    /// while the player is dragging a card (<c>GameManager.DraggedCard</c>), because the dragged card may be the one
    /// about to leave the bowl; if that property cannot be read the meal is skipped too, never guessed. The removal
    /// is a coroutine that finishes after this call returns, so the Hunger it just restored is what keeps the next
    /// tick from serving the same card twice. Registered once per process (the early return on
    /// <c>_initialized</c> is load-bearing, root CLAUDE.md "Per-Run Event Registration"). Every miss leaves a warn-once
    /// line keyed on its cause. The numbers are tunable placeholders.</para>
    /// </summary>
    internal static class CatBowlPatch
    {
        internal const string BowlUid = "cmcCatBowl";
        internal const string InnCatUid = "cmcInnCat";
        internal const string ShadowCatUid = "cmcShadowCatTamed";

        /// <summary>The cat's Hunger stat on both cats (SpoilageTime, counts down one a tick).</summary>
        internal const string HungerStatName = "SpoilageTime";

        /// <summary>The cat's own Care stat on both cats (SpecialDurability3, 0 to 100).</summary>
        internal const string CareStatName = "SpecialDurability3";

        /// <summary>TUNABLE PLACEHOLDER: a cat eats from the bowl when Hunger is at or below this share of full.</summary>
        internal const float HungerThreshold = 0.25f;

        /// <summary>TUNABLE PLACEHOLDER: Hunger a meal restores, the same as a hand Feed (the cat's Feed CI adds 288).</summary>
        internal const float HungerRestore = 288f;

        /// <summary>TUNABLE PLACEHOLDER: the share of a cistern drink's Care loss that one bowl meal costs.</summary>
        internal const float CareLossScale = 0.5f;

        private static bool _initialized;
        private static readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);
        private static PropertyInfo _draggedCardProp;
        private static bool _draggedCardResolved;

        private static void WarnOnce(string cause, string message)
        {
            if (_warned.Add(cause)) Plugin.Logger.LogWarning("[CatBowlPatch] " + message);
        }

        /// <summary>True when a cat at this Hunger is hungry enough to eat. NaN or a non-positive maximum never is.</summary>
        internal static bool IsHungry(float cur, float max)
        {
            if (float.IsNaN(cur) || float.IsNaN(max) || max <= 0f) return false;
            return (cur / max) <= HungerThreshold;
        }

        /// <summary>The Hunger after one meal: restored by <see cref="HungerRestore"/>, never past full.</summary>
        internal static float HungerAfterMeal(float cur, float max) => Math.Min(max, cur + HungerRestore);

        /// <summary>The Care lost to one meal: the cistern drink's loss times <see cref="CareLossScale"/>.</summary>
        internal static float CareLossPerMeal => AshCatTickPatch.CareLossPerDrink * CareLossScale;

        /// <summary>The Care after one meal, never below 0.</summary>
        internal static float CareAfterMeal(float care) => Math.Max(0f, care - CareLossPerMeal);

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            TickEvents.DtpTick += OnTick;
            Plugin.Logger.LogDebug("[CatBowlPatch] initialized.");
        }

        private static void OnTick()
        {
            try
            {
                var here = GameQuery.CardsInPlayerEnv();
                if (here == null || here.Count == 0) return;

                var cats = new List<object>();
                var bowls = new List<object>();
                foreach (var card in here)
                {
                    string uid = CardUtil.GetCardUniqueId(card);
                    if (uid == null) continue;
                    if (uid == InnCatUid || uid == ShadowCatUid) cats.Add(card);
                    else if (uid == BowlUid) bowls.Add(card);
                }
                if (cats.Count == 0 || bowls.Count == 0) return;

                bool dragging = false;
                bool dragChecked = false;
                foreach (var cat in cats)
                {
                    float cur = CardUtil.GetDurability(cat, HungerStatName);
                    float max = CardUtil.GetDurabilityMax(cat, HungerStatName);
                    if (float.IsNaN(cur) || float.IsNaN(max))
                    {
                        WarnOnce("hunger-unreadable-" + CardUtil.GetCardUniqueId(cat), "A cat's Hunger stat is unreadable, so it was not served.");
                        continue;
                    }
                    if (!IsHungry(cur, max)) continue;

                    if (!dragChecked)
                    {
                        dragChecked = true;
                        dragging = IsPlayerDragging();
                    }
                    if (dragging) return;

                    TryServe(cat, cur, max, bowls);
                }
            }
            catch (Exception ex)
            {
                WarnOnce("tick-exception", "OnTick failed: " + (ex.InnerException?.ToString() ?? ex.ToString()));
            }
        }

        // True while a card is being dragged. A property that cannot be read counts as dragging (skip), because the
        // dragged card may be the very one a meal would remove.
        private static bool IsPlayerDragging()
        {
            if (!_draggedCardResolved)
            {
                _draggedCardResolved = true;
                var gmType = CardUtil.FindGameType("GameManager");
                _draggedCardProp = gmType?.GetProperty("DraggedCard", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            }
            if (_draggedCardProp == null)
            {
                WarnOnce("dragged-card-unresolved", "GameManager.DraggedCard could not be resolved, so no cat is served from a bowl.");
                return true;
            }
            try
            {
                var dragged = _draggedCardProp.GetValue(null, null);
                return dragged is UnityEngine.Object uo ? uo != null : dragged != null;
            }
            catch (Exception ex)
            {
                WarnOnce("dragged-card-read", "GameManager.DraggedCard threw, so no cat is served from a bowl: " + (ex.InnerException?.Message ?? ex.Message));
                return true;
            }
        }

        private static void TryServe(object cat, float hungerNow, float hungerMax, List<object> bowls)
        {
            string catUid = CardUtil.GetCardUniqueId(cat);
            foreach (var bowl in bowls)
            {
                foreach (var food in Inventory.Cards(bowl))
                {
                    if (food == null) continue;
                    if (IsLocked(food)) continue;

                    if (!CardUtil.TryRemoveCard(food))
                    {
                        WarnOnce("remove-failed", "CardUtil.TryRemoveCard could not take a card out of the bowl, so " + catUid + " was not served.");
                        return;
                    }
                    if (!CardUtil.SetDurability(cat, HungerStatName, HungerAfterMeal(hungerNow, hungerMax)))
                        WarnOnce("hunger-write-failed", catUid + ": the Hunger stat could not be written after a meal left the bowl.");
                    float care = CardUtil.GetDurability(cat, CareStatName);
                    if (float.IsNaN(care)) WarnOnce("care-unreadable-" + catUid, catUid + ": the Care stat is unreadable, so a bowl meal cost no Care.");
                    else if (!CardUtil.SetDurability(cat, CareStatName, CareAfterMeal(care)))
                        WarnOnce("care-write-failed", catUid + ": the Care stat could not be written after a bowl meal.");
                    Plugin.Logger.LogDebug($"[CatBowlPatch] {catUid} ate {CardUtil.GetCardUniqueId(food)} from a Cat Bowl (Hunger {hungerNow:F0} to {HungerAfterMeal(hungerNow, hungerMax):F0}).");
                    return;
                }
            }
        }

        // CannotBeTransferred is the vanilla guard on the drag path only; a programmatic removal honours it itself.
        private static bool IsLocked(object card)
        {
            var model = Reflect.GetMember(card, "CardModel");
            return model != null && Reflect.GetBool(model, "CannotBeTransferred", false);
        }
    }
}
