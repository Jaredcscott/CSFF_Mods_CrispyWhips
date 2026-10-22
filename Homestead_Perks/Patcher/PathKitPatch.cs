using System;
using System.Collections.Generic;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace HomesteadPerks.Patcher
{
    /// <summary>
    /// Path Kit spend guard (plan section 2, R2 measured 2026-10-09:
    /// Documentation/Research/HomesteadPerks/wave0-perk-runs.md section 1). A Build Path press
    /// spends one kit and completes an Imp_Path* card whether or not the current location offers
    /// that direction, and a repeat press in a built direction spends another kit and duplicates
    /// the card. This Cancel handler refuses both BEFORE the kit is consumed:
    ///  - the location card's EnvironmentImprovements has no entry for the pressed direction, or
    ///  - that direction's Imp_Path* card is already on the board AND complete.
    /// Registered once per process from Plugin.RegisterPatches (never from OnGMInitialized).
    /// Every non-throwing null exit logs once per cause and FAILS OPEN (the press proceeds), so a
    /// game-version drift never locks the kit.
    /// </summary>
    internal static class PathKitPatch
    {
        private const string PathKitId = "hsppathkit";
        private const string KeyPrefix = "HSP_PathKit_DA_";

        // Action LocalizationKey -> the Imp_Path* vanilla GUID that action builds
        // (the same GUIDs HSP_PathKit.json names in ProducedCards).
        private static readonly Dictionary<string, string> DirectionImprovement =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "HSP_PathKit_DA_North", "dd31ad4b8b070494294fde82c6523143" },
                { "HSP_PathKit_DA_South", "b1d3de19f5f9660428e4983bc90eace6" },
                { "HSP_PathKit_DA_East", "aceac961438f7cc48996a34de4fd8844" },
                { "HSP_PathKit_DA_West", "c0d621b8981ad3b499559262912ed7f8" },
            };

        private static readonly HashSet<string> Warned = new HashSet<string>();

        public static void Initialize()
        {
            ActionRouter.Register(new ActionHandler
            {
                Name = "PathKitSpendGuard",
                CardUid = PathKitId,
                ActionKeyPrefix = KeyPrefix,
                Timing = ActionTiming.Cancel,
                Before = ShouldCancel,
            });
            Plugin.Logger.LogDebug("[PathKitPatch] Path Kit spend guard registered.");
        }

        private static void WarnOnce(string cause, string detail)
        {
            if (Warned.Add(cause))
                Plugin.Logger.LogWarning($"[PathKitPatch] {cause}: {detail} (press allowed; logged once).");
        }

        /// <summary>
        /// Does one location's EnvironmentImprovements list offer the improvement? <paramref name="entryUids"/>
        /// holds each non-empty entry's Card.UniqueID, null where it could not be read. True when one names
        /// <paramref name="impUid"/>; null when none does but at least one was unreadable (the caller fails
        /// open); false when every entry read and none names it. Pure, so the gate can call it on the built DLL.
        /// </summary>
        internal static bool? RouteOffered(IEnumerable<string> entryUids, string impUid)
        {
            bool unreadable = false;
            foreach (var uid in entryUids)
            {
                if (uid == null) { unreadable = true; continue; }
                if (string.Equals(uid, impUid, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return unreadable ? (bool?)null : false;
        }

        // True = cancel the press.
        private static bool ShouldCancel(ActionContext ctx)
        {
            try
            {
                if (ctx.ActionKey == null || !DirectionImprovement.TryGetValue(ctx.ActionKey, out var impUid))
                    return false; // the informational DA or an unknown key: leave alone

                var cards = GameQuery.CardsInPlayerEnv();

                // Repeat press: that direction's improvement card is already on this board.
                foreach (var c in cards)
                {
                    if (!string.Equals(CardUtil.GetCardUniqueId(c), impUid, StringComparison.OrdinalIgnoreCase)) continue;
                    // An unbuilt model of the improvement is normal at a location with a route
                    // (measured 2026-10-09: BlueprintComplete false), and it is exactly what a press
                    // completes. Only a COMPLETED card means the road is already laid.
                    if (CardUtil.ToBool(CardUtil.GetMemberValue(c, "BlueprintComplete")))
                    {
                        Plugin.Logger.LogInfo($"[PathKitPatch] {ctx.ActionKey}: improvement already built here, press refused.");
                        return true;
                    }
                }

                // No route: the location card's buildable list must name the improvement.
                bool sawLocation = false;
                bool sawUnreadable = false;
                foreach (var c in cards)
                {
                    var data = CardUtil.GetCardData(c);
                    if (data == null) continue;
                    var ct = CardUtil.GetMemberValue(data, "CardType");
                    if (ct == null || Convert.ToInt32(ct) != 8) continue;
                    if (!(CardUtil.GetMemberValue(data, "EnvironmentImprovements") is Array arr)) continue;
                    sawLocation = true;
                    var uids = new List<string>();
                    foreach (var entry in arr)
                    {
                        if (entry == null) continue; // an empty slot names no route
                        var imp = CardUtil.GetMemberValue(entry, "Card");
                        uids.Add(imp == null ? null : CardUtil.GetMemberValue(imp, "UniqueID") as string);
                    }
                    var offered = RouteOffered(uids, impUid);
                    if (offered == true) return false; // a route exists
                    if (offered == null) sawUnreadable = true;
                }

                if (!sawLocation)
                {
                    WarnOnce("no-location-card", "no CT8 card with EnvironmentImprovements found in the player's environment");
                    return false;
                }

                // An entry whose Card or UniqueID did not read could be the very route pressed, so refusing
                // would lock the kit on a game-version drift: fail open, as every other null exit here does.
                if (sawUnreadable)
                {
                    WarnOnce("unreadable-improvement", "an EnvironmentImprovements entry's Card or UniqueID did not resolve, so the route check cannot tell");
                    return false;
                }

                Plugin.Logger.LogInfo($"[PathKitPatch] {ctx.ActionKey}: this location offers no such route, press refused.");
                return true;
            }
            catch (Exception ex)
            {
                WarnOnce("guard-threw", ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }
    }
}
