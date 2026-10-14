using System;
using System.Collections.Generic;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Removes the second, unbuilt model card of one of this mod's environment improvements.
    ///
    /// <para>Vanilla's unlock pass places an improvement's model through a "Blueprint Unlocked" action that
    /// <c>GameManager.PerformObjectiveActions</c> does not wait for before it empties
    /// <c>CardsAboutToBeUnlocked</c>, so a second object check that lands before the first model card exists
    /// queues the same unlock again. One Stone Tile arriving in the Inn or the Jail Cell placed TWO Stone Tile
    /// Floor sites (harness T2.187, 2026-10-13); building one left the other standing.</para>
    ///
    /// <para>Only this mod's improvements are touched (UID prefix <c>cmcimp</c>), only when the player's own
    /// board holds more than one card of the same UID, and only a duplicate that is not built. The built one
    /// (or, failing that, the first) is kept.</para>
    /// </summary>
    internal static class EnvImprovementTwinGuard
    {
        private const string UidPrefix = "cmcimp";
        private const float PollSeconds = 2f;
        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            TickEvents.Interval(PollSeconds, Run, "EnvImprovementTwinGuard");
        }

        private static void Run()
        {
            try
            {
                if (GameQuery.IsTransitioning) return;
                var byUid = new Dictionary<string, List<object>>(StringComparer.OrdinalIgnoreCase);
                foreach (var card in GameQuery.CardsInPlayerEnv())
                {
                    string uid = CardUtil.GetCardUniqueId(card);
                    if (uid == null || !uid.StartsWith(UidPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                    var model = Reflect.GetMember(card, "CardModel");
                    if (model == null || Reflect.GetMember(model, "CardType")?.ToString() != "EnvImprovement") continue;
                    if (!byUid.TryGetValue(uid, out var list)) byUid[uid] = list = new List<object>();
                    list.Add(card);
                }

                foreach (var pair in byUid)
                {
                    var cards = pair.Value;
                    if (cards.Count < 2) continue;

                    object keeper = cards[0];
                    foreach (var c in cards)
                        if (Reflect.GetBool(c, "BlueprintComplete")) { keeper = c; break; }

                    foreach (var c in cards)
                    {
                        if (ReferenceEquals(c, keeper) || Reflect.GetBool(c, "BlueprintComplete")) continue;
                        if (CardUtil.TryRemoveCard(c))
                            Plugin.Logger.LogInfo($"[EnvImprovementTwinGuard] removed a duplicate unbuilt '{pair.Key}' site (vanilla placed it twice).");
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[EnvImprovementTwinGuard] Run failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }
    }
}
