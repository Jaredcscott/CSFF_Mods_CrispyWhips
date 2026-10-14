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
                foreach (var c in cards)
                {
                    var data = CardUtil.GetCardData(c);
                    if (data == null) continue;
                    var ct = CardUtil.GetMemberValue(data, "CardType");
                    if (ct == null || Convert.ToInt32(ct) != 8) continue;
                    if (!(CardUtil.GetMemberValue(data, "EnvironmentImprovements") is Array arr)) continue;
                    sawLocation = true;
                    foreach (var entry in arr)
                    {
                        var imp = entry == null ? null : CardUtil.GetMemberValue(entry, "Card");
                        var uid = imp == null ? null : CardUtil.GetMemberValue(imp, "UniqueID") as string;
                        if (string.Equals(uid, impUid, StringComparison.OrdinalIgnoreCase))
                            return false; // a route exists
                    }
                }

                if (!sawLocation)
                {
                    WarnOnce("no-location-card", "no CT8 card with EnvironmentImprovements found in the player's environment");
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
