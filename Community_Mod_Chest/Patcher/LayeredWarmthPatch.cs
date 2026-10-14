using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// H4: a layered-outfit warmth bonus. Wearing three or more pieces of CMC's cold-weather apparel at once
    /// is warmer than the pieces alone.
    ///
    /// <para>A poll counts the worn cards that carry <see cref="ColdWeatherTagName"/> (referenced by NAME in each
    /// card's CardTagsWarpData; no ScriptableObject/CardTag file ships for it) and writes the count into the
    /// hidden stat <see cref="StatUid"/> (GameStat/CMC_LayeredWarmth.json, clamped 0 to 6). That stat's one
    /// status, for 3 to 6, carries the Temperature Perceived modifier, so the bonus is authored data and this
    /// class never writes Temperature Perceived itself and never touches a card's own passive effects (so no
    /// composition-point early-out is involved). CardUtil.IsPerkEquipped is not involved either.</para>
    ///
    /// <para>Read through <c>GameQuery.EquippedCards</c> by reflection (this mod compiles against a framework copy
    /// that predates it, as <c>GuardWantedReactionPatch.PlayerWearsDisguise</c> notes). Every non-throwing miss
    /// leaves its own warn-once line, so a renamed CardData field reads as a logged cause and never as "nothing
    /// is worn". Registration is once per process (the latch is load-bearing): the poll is a plain
    /// TickEvents.Interval, not an event the game re-fires per run, and the stat lives in the save.</para>
    /// </summary>
    internal static class LayeredWarmthPatch
    {
        internal const string ColdWeatherTagName = "tag_CMCColdWeatherApparel";
        internal const string StatUid = "cmcStatLayeredWarmth";

        /// <summary>Top of the stat's MinMaxValue. The count is clamped here too, so the write can never be
        /// refused or clamped by the engine into a different band.</summary>
        internal const int MaxCount = 6;

        /// <summary>TUNABLE PLACEHOLDER (H4): the warmth itself is the Temperature Perceived modifier on the
        /// stat's status (GameStat/CMC_LayeredWarmth.json, +1.0), and the band starts at 3 pieces (that status'
        /// ValueRange). Both live in the JSON; this note is the pointer.</summary>
        internal const int PiecesForBonus = 3;

        private const float PollSeconds = 5f;

        private static bool _initialized;
        private static PropertyInfo _equippedProp;
        private static readonly HashSet<string> _warned = new HashSet<string>();

        /// <summary>Pure: the value the stat should hold for a count of worn tagged pieces.</summary>
        internal static float StatValueFor(int taggedWorn)
        {
            if (taggedWorn < 0) return 0f;
            return taggedWorn > MaxCount ? MaxCount : taggedWorn;
        }

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            TickEvents.Interval(PollSeconds, Poll, "LayeredWarmth");
            Plugin.Logger.LogDebug("[LayeredWarmthPatch] initialized.");
        }

        private static void WarnOnce(string cause, string message)
        {
            if (_warned.Add(cause)) Plugin.Logger.LogWarning("[LayeredWarmthPatch] " + message);
        }

        private static void Poll()
        {
            try
            {
                // -1 means the stat is not readable yet (main menu, or a run still loading): no equipment read then.
                float current = HiddenStat.Get(StatUid);
                if (current < 0f) return;

                int worn = CountWornTagged();
                if (worn < 0) return; // unreadable: a warn-once line says why, and the stat keeps its last value
                float want = StatValueFor(worn);
                if (Math.Abs(current - want) < 0.001f) return;
                // Through the game's stat change, never HiddenStat.Set: a raw write never attaches the
                // 'Well Layered' status, so the +1 never applied (T2.965).
                if (HiddenStat.SetWithStatuses(StatUid, want))
                    Plugin.Logger.LogDebug($"[LayeredWarmthPatch] {worn} cold-weather piece(s) worn: stat {current:0.##} -> {want:0.##}.");
            }
            catch (Exception ex)
            {
                WarnOnce("exception", "Poll failed: " + (ex.InnerException?.ToString() ?? ex.ToString()));
            }
        }

        /// <summary>The number of worn cards carrying the tag, or -1 when the equipment cannot be read.</summary>
        private static int CountWornTagged()
        {
            _equippedProp ??= typeof(GameQuery).GetProperty("EquippedCards", BindingFlags.Public | BindingFlags.Static);
            if (_equippedProp == null)
            {
                WarnOnce("equippedcards-missing", "GameQuery.EquippedCards is not in the installed framework (needs a newer CSFFModFramework), so no layered warmth is ever read.");
                return -1;
            }
            if (_equippedProp.GetValue(null, null) is not IEnumerable equipped)
            {
                WarnOnce("equippedcards-unreadable", "GameQuery.EquippedCards returned nothing readable.");
                return -1;
            }
            int count = 0;
            foreach (object card in equipped)
            {
                if (card == null) continue;
                var model = CardUtil.GetCardData(card);
                if (model == null) { WarnOnce("model-null", "An equipped card has no CardData, so it cannot count toward layered warmth."); continue; }
                var tagsField = CardUtil.GetCachedField(model.GetType(), "CardTags");
                if (tagsField == null) { WarnOnce("cardtags-field-missing", "CardData.CardTags was not found, so no layered warmth can be read."); return -1; }
                if (tagsField.GetValue(model) is not IList tags) continue;
                foreach (var tag in tags)
                {
                    if (tag is UnityEngine.Object uo && uo != null && uo.name == ColdWeatherTagName) { count++; break; }
                }
            }
            return count;
        }
    }
}
