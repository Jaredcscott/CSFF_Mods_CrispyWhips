using System;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace FarTerritory.Patcher;

/// <summary>
/// Plan P21 (Documentation/Design/FarTerritory_As_Built.md section 21, Open question 15): when Forest Lore is
/// installed, its Standing Stones (<c>fl_standing_stones</c>) also stand on the grassland's Wildrye Plain.
///
/// <para>Why code and not JSON (W0-13, MEASURED): <c>DropInjections.json</c> only appends a drop to an existing
/// action's collection and never places a board card, and a foreign UID in a non-clone board's
/// <c>DefaultEnvCardDrops</c> would make WarpResolver invent a card when Forest Lore is absent. So the stones are
/// spawned from here, and only when <c>CardUtil.GetCardDataById</c> finds the card: with Forest Lore absent the
/// lookup is null and this class does nothing at all (no log line above Debug). Nothing of Forest Lore's is
/// copied; the card is Forest Lore's own and refers to nothing outside itself (its plan, Q8).</para>
///
/// <para>Shape (root CLAUDE.md "Runtime Card Spawning" and "Environment-entry logic must RETRY"): a spawn lands
/// on the CURRENT board only, so the check runs only while the player stands on Wildrye Plain; the board is
/// treated as NOT READY until Far Territory's own location card is on it, because
/// <c>ChangeEnvironment</c> sets the destination before its default cards exist; and "no stones yet" is
/// decided from an environment-scoped scan, never from a bare <c>AllCards</c> UID match. The board's saved
/// cards are the latch: once the stones are on the board they stay saved with it, so a revisit finds them and
/// spawns nothing. A short cool-down after a spawn stops a second one while the first is still being placed.</para>
/// </summary>
internal static class LandmarkSeed
{
    private const string StonesUid = "fl_standing_stones";
    private const string GrasslandEnvUid = "ftEnvWildryePlain";
    private const string GrasslandLocationUid = "ftLocWildryePlain";
    private const int CooldownChecks = 3;

    private static bool _initialized;
    private static bool _absentLogged;
    private static int _cooldown;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        TickEvents.Interval(5f, CheckAndSpawn, "FarTerritoryLandmarkSeed");
    }

    private static void CheckAndSpawn()
    {
        try
        {
            if (!string.Equals(GameQuery.CurrentEnvironmentUniqueId, GrasslandEnvUid, StringComparison.OrdinalIgnoreCase))
            {
                _cooldown = 0;
                return;
            }
            if (_cooldown > 0) { _cooldown--; return; }

            // Soft crossover: no Forest Lore, no card data, nothing to do.
            if (CardUtil.GetCardDataById(StonesUid) == null)
            {
                if (!_absentLogged)
                {
                    _absentLogged = true;
                    Plugin.ModLog.LogDebug($"[LandmarkSeed] '{StonesUid}' is not loaded (Forest Lore absent): Wildrye Plain keeps no landmark.");
                }
                return;
            }

            var cards = GameQuery.CardsInPlayerEnv();
            int locations = 0, stones = 0;
            foreach (var card in cards)
            {
                if (card == null) continue;
                string uid = CardUtil.GetCardUniqueId(card);
                if (uid == GrasslandLocationUid) locations++;
                else if (uid == StonesUid) stones++;
            }

            // Not ready: the board's own card has not appeared yet. Retry on the next check.
            if (locations == 0) return;
            if (stones > 0) return;

            SpawnService.Spawn(StonesUid);
            _cooldown = CooldownChecks;
            Plugin.ModLog.LogInfo($"[LandmarkSeed] Forest Lore's Standing Stones placed on Wildrye Plain.");
        }
        catch (Exception ex)
        {
            Plugin.ModLog.LogWarning($"[LandmarkSeed] check failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }
}
