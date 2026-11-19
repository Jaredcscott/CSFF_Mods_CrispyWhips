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
                    var model = ReadOrWarn(card, "CardModel");
                    if (model == null || ReadOrWarn(model, "CardType")?.ToString() != "EnvImprovement") continue;
                    if (!byUid.TryGetValue(uid, out var list)) byUid[uid] = list = new List<object>();
                    list.Add(card);
                }

                foreach (var pair in byUid)
                {
                    var cards = pair.Value;
                    if (cards.Count < 2) continue;

                    // Read every site's built state BEFORE choosing what to remove. A site whose state cannot be
                    // read counts as BUILT (kept), and one such site stands the whole group down: with a missing
                    // BlueprintComplete every site used to read "not built", so the guard kept the first and
                    // removed the rest, a built improvement among them (modwide A13).
                    var built = new bool[cards.Count];
                    bool allReadable = true;
                    for (int i = 0; i < cards.Count; i++)
                    {
                        if (TryReadBuilt(cards[i], out built[i])) continue;
                        built[i] = true;
                        allReadable = false;
                    }
                    if (!allReadable) continue;

                    int keeper = 0;
                    for (int i = 0; i < cards.Count; i++)
                        if (built[i]) { keeper = i; break; }

                    for (int i = 0; i < cards.Count; i++)
                    {
                        if (i == keeper || built[i]) continue;
                        if (CardUtil.TryRemoveCard(cards[i]))
                            Plugin.Logger.LogInfo($"[EnvImprovementTwinGuard] removed a duplicate unbuilt '{pair.Key}' site (vanilla placed it twice).");
                    }
                }
            }
            catch (Exception ex)
            {
                // Once per exception type: this runs every PollSeconds for the whole session.
                WarnOnce("run-exception:" + (ex.InnerException ?? ex).GetType().Name, $"[EnvImprovementTwinGuard] Run failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // One latch per cause (root CLAUDE.md Silent Catch Blocks; modwide A13, 2026-11-12).
        private static readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);

        private static void WarnOnce(string cause, string message)
        {
            if (_warned.Add(cause)) Plugin.Logger.LogWarning(message);
        }

        /// <summary>
        /// Whether this improvement site is built, read from <c>InGameCardBase.BlueprintComplete</c>. Returns
        /// false, with one warning naming the member, when it cannot be read (renamed or re-typed by a game
        /// update): the caller must then treat the site as built and remove nothing.
        /// </summary>
        private static bool TryReadBuilt(object card, out bool built)
        {
            built = false;
            if (Reflect.TryGetMember(card, "BlueprintComplete", out var value) && value is bool b)
            {
                built = b;
                return true;
            }
            WarnOnce("member-unresolved:BlueprintComplete", $"[EnvImprovementTwinGuard] {card?.GetType().Name}.BlueprintComplete did not resolve to a bool (renamed by a game update?); a built improvement cannot be told from an unbuilt one, so no duplicate site is removed until it does. Logged once.");
            return false;
        }

        // A CardModel or CardType that stops resolving makes every card read "not an improvement": the guard
        // then does nothing (safe), and says so once instead of going quiet.
        private static object ReadOrWarn(object instance, string member)
        {
            if (instance == null) return null;
            if (Reflect.TryGetMember(instance, member, out var value)) return value;
            string owner = instance.GetType().Name;
            WarnOnce("member-unresolved:" + owner + "." + member, $"[EnvImprovementTwinGuard] {owner}.{member} did not resolve or could not be read (renamed by a game update?); duplicate improvement sites are not detected until it does. Logged once.");
            return null;
        }
    }
}
