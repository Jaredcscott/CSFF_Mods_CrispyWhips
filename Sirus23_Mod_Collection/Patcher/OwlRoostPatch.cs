using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Logging;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using UnityEngine;

namespace Sirus23ModCollection.Patcher;

/// <summary>
/// The Owl Companion roosts in the nearest cave by day and comes back to the player at night.
///
/// DAWN: an Owl Companion on the player's live board (not told to Stay Here, player not already in a cave or
/// building) is moved into the saved cards of the CLOSEST cave: the natural cave in
/// <see cref="RoostCaveUids"/> whose map node (x and z of its Coords) is nearest the player's own node. The move copies the idiom of the game's own <c>GameManager.MoveCard</c> for a
/// non-AlwaysUpdate card (save the card by reference into the target room's save data, then remove the live
/// card), because MoveCard itself only re-homes an AlwaysUpdate card (the owl is one) and saves nothing.
///
/// DUSK: with no owl on the board and the player outdoors, the owl's saved record is looked up in every other
/// room (the W0-24 route: <c>GameManager.EnvironmentsData</c>, one <c>CardSaveDataByReference</c> per left-behind
/// card), removed, and the owl spawned on the player's board with Hunger, Thirst, Morale and Health copied from the
/// record. The record is put back if the spawn does not produce a live owl within a few seconds, so the owl is
/// never lost and never doubled.
///
/// The owl follows the player only at night (<see cref="CompanionStayPatch"/>), so a roosting owl is simply a
/// daytime owl that is somewhere safe. A "Stay Here" owl (SpecialDurability2 at least 1) is never moved: the
/// player parked it. Everything runs from one process-lifetime interval registered once; its per-run state (the
/// retry latches, a recall in flight and the cached owl) is cleared when the poll first sees a new run's
/// GameManager, and every early return logs its cause once. The poll is cheap while nothing changes: the board
/// scan reruns only when the board's card count or the player's environment moved, or every 30 s.
/// </summary>
internal static class OwlRoostPatch
{
    // Natural caves only (no mines, tunnels or buildings): the places an owl would roost. UIDs from IndoorOrCaveEnv.
    private static readonly string[] RoostCaveUids =
    {
        "0d597c607faea644792819dd69735016", // Bear Cave
        "41d2fb7700db4614b8ae12b526d8ec24", // Great Cave (Old Hollow)
        "3ddd45aa9e1c28d48bec644a7bba37e0", // Cave (Pine Slopes)
        "75511a9fcdc45f647bc032eed359ef44", // Cave (River Pass)
        "45c5db6a28297e44b8eba819f391e0f8", // Secret Cave (Shady Thicket)
        "0b397533399cd9243ab2f3a77557f93b", // Still Cave
        "17d6cfe8ff886b643afee74dc18760a7", // Waterfall Caves
        "63bf07510e1a58d448cb36effe180a91", // Wolf Cave
        "e5ff4e632df90e643b4d968b4c55d6f8", // Flint Sett
    };

    private const float PollSeconds = 2f;
    private const int RecallEveryPolls = 5;          // the saved-record scan runs every 10 s at night, not every poll
    private const float VerifySeconds = 6f;          // how long a recalled owl has to show up before the record goes back
    private const float BackoffSeconds = 60f;        // after a failed send or recall

    private static ManualLogSource Logger => Plugin.Logger;

    private static readonly HashSet<string> _warned = new(StringComparer.Ordinal);
    private static int _poll;
    private static float _backoffUntil;

    // A recall in flight: the record we removed and where it came from, until a live owl is confirmed.
    private static CardSaveDataByReference _pendingRecord;
    private static IList<CardSaveDataByReference> _pendingList;
    private static int _pendingIndex;
    private static float _pendingDeadline;

    // The run these statics belong to (the GameManager's instance id). A new run gets a new GameManager,
    // and the first poll that sees it clears the per-run state above and below (audit row R8,
    // 2026-10-12). Keyed on the manager rather than GameManager.OnGMInitialized because that event fires
    // only at the end of FinishInitializing, after the board is live: a recall this poll started in the
    // new run before then would lose its record if the event then cleared it.
    private static int _runId;

    // Cheap board lookup (audit row R5, 2026-10-12). The full scan walks every card in AllCards with the
    // environment test, so it runs only when the board may have changed: the AllCards count or the
    // player's environment differs from the last scan, a rescan is due (every RescanEveryPolls polls,
    // so a one-for-one card swap within a poll cannot hide an owl for longer than that), or a caller is
    // about to act on "no owl here" (the night recall and the failed-recall put-back always rescan).
    private const int RescanEveryPolls = 15;          // 30 s
    private static InGameCardBase _owl;               // the owl the last scan found, or null
    private static int _scanCount = -1;
    private static EnvDictKey _scanEnv;
    private static int _pollsSinceScan;

    public static void Register()
    {
        TickEvents.Interval(PollSeconds, OnPoll, "OwlRoost");
    }

    private static void WarnOnce(string cause, string message)
    {
        if (_warned.Add(cause)) Logger?.LogWarning($"[OwlRoost] {message} Logged once per cause.");
    }

    private static void OnPoll()
    {
        try
        {
            if (CardUtil.GetGameManagerInstance() is not GameManager gm) return;   // main menu: no run
            if (CardUtil.GetMemberValue(gm, "EnvironmentTransition") is true) return;
            if (CardUtil.GetMemberValue(gm, "LeavingEnvironment") is true) return;
            if (gm.CurrentEnvironment.EnvCard == null) return;
            ResetIfNewRun(gm);

            var owl = FindOwlOnBoard(gm, false);

            if (_pendingRecord != null)
            {
                // A false "no owl" here would put the record back beside a live owl, so rescan first.
                if (owl == null && Time.realtimeSinceStartup >= _pendingDeadline) owl = FindOwlOnBoard(gm, true);
                VerifyRecall(owl);
                return;
            }
            if (Time.realtimeSinceStartup < _backoffUntil) return;

            bool night = CompanionStayPatch.IsNight();
            if (!night)
            {
                if (owl != null) SendToNearestCave(gm, owl);
                return;
            }

            if (owl != null) return;
            if (++_poll % RecallEveryPolls != 0) return;
            if (IndoorOrCaveEnv.Matches(gm.CurrentEnvironment.EnvCard)) return;   // the owl does not come into a cave or building
            if (FindOwlOnBoard(gm, true) != null) return;   // a false "no owl" here would recall a second one
            RecallOwl(gm);
        }
        catch (Exception ex)
        {
            WarnOnce("poll:" + ex.GetType().Name, $"the roost poll threw {ex.GetType().Name}: {ex.InnerException?.ToString() ?? ex.ToString()}");
            _backoffUntil = Time.realtimeSinceStartup + BackoffSeconds;
        }
    }

    private static void ResetIfNewRun(GameManager gm)
    {
        int id = gm.GetInstanceID();
        if (id == _runId) return;
        bool hadPending = _pendingRecord != null;
        _runId = id;
        _pendingRecord = null; _pendingList = null; _pendingIndex = 0; _pendingDeadline = 0f;
        _backoffUntil = 0f;
        _poll = 0;
        InvalidateOwlCache();
        if (hadPending)
            Logger?.LogDebug("[OwlRoost] A new run started while a recall from the previous run was in flight; that recall was dropped.");
    }

    private static void InvalidateOwlCache()
    {
        _owl = null;
        _scanCount = -1;
        _pollsSinceScan = 0;
    }

    // The owl on the player's own board only: an AllCards scan paired with the environment test (the board, not
    // carried or independent cards of another room), which GameQuery.CardsInPlayerEnv already applies. Without
    // force, the last scan's answer is reused while the board has not changed (see _owl above); a cached owl is
    // still checked to be alive, unremoved and an owl.
    private static InGameCardBase FindOwlOnBoard(GameManager gm, bool force)
    {
        int count = gm.AllCards != null ? gm.AllCards.Count : -1;
        var envKey = gm.CurrentEnvironment.DictionnaryKey;
        if (!force && ++_pollsSinceScan < RescanEveryPolls && count == _scanCount && envKey.Equals(_scanEnv))
        {
            if (_owl is null) return null;   // the last scan found none (a destroyed cached owl falls through to a rescan)
            if (_owl && !_owl.Destroyed && _owl.CardModel != null && _owl.CardModel.UniqueID == WolfTickPatch.OwlId)
                return _owl;
        }

        InGameCardBase found = null;
        foreach (var c in GameQuery.CardsInPlayerEnv())
        {
            if (CardUtil.GetCardUniqueId(c) == WolfTickPatch.OwlId && c is InGameCardBase cb && cb && !cb.Destroyed)
            {
                found = cb;
                break;
            }
        }
        _owl = found;
        _scanCount = count;
        _scanEnv = envKey;
        _pollsSinceScan = 0;
        return found;
    }

    // ---- DAWN: send to the nearest cave -------------------------------------------------------------------

    private static void SendToNearestCave(GameManager gm, InGameCardBase owl)
    {
        float stay = CardUtil.GetDurability(owl, "SpecialDurability2");
        if (!float.IsNaN(stay) && stay >= 1f) return;                            // told to Stay Here: leave it
        if (IndoorOrCaveEnv.Matches(gm.CurrentEnvironment.EnvCard)) return;     // already in a cave or building

        // Nearest by map position. WorldMapData.DistancesToPlayer reads -1 for every cave (they sit on another map
        // layer, w = 1, from the outdoor map at w = 0), so it cannot rank them; the x and z of each node's own
        // Coords can (the layer is ignored on purpose). Measured on rig 5, 2026-10-10: Bear Cave read -1.
        var world = gm.CurrentWorldMap;
        var here = world != null ? world.GetEnvData(gm.CurrentEnvironment) : null;
        if (here == null)
        {
            WarnOnce("no-here", "the player's environment has no map node, so the nearest cave cannot be found; the owl stays on the board by day.");
            _backoffUntil = Time.realtimeSinceStartup + BackoffSeconds;
            return;
        }

        EnvID best = default;
        string bestUid = null;
        float bestDist = float.MaxValue;
        foreach (var uid in RoostCaveUids)
        {
            if (CardUtil.GetCardDataById(uid) is not CardData caveCard) continue;
            var id = new EnvID(caveCard);
            var node = world.GetEnvData(id);
            if (node == null) continue;
            float dx = node.Coords.x - here.Coords.x, dz = node.Coords.z - here.Coords.z;
            float d = Mathf.Sqrt(dx * dx + dz * dz);
            if (d < bestDist)
            {
                bestDist = d; best = id; bestUid = uid;
            }
        }
        if (bestUid == null)
        {
            WarnOnce("no-cave", "none of the roost caves has a map node, so the owl stays on the board by day.");
            _backoffUntil = Time.realtimeSinceStartup + BackoffSeconds;
            return;
        }

        var envSave = gm.GetEnvSaveData(best, true);
        if (envSave == null)
        {
            WarnOnce("no-envsave", $"no saved-room entry could be created for cave {bestUid}; the owl stays on the board by day.");
            _backoffUntil = Time.realtimeSinceStartup + BackoffSeconds;
            return;
        }

        var originalEnv = owl.CardEnvironment;
        int before = envSave.GetRegularCards.Count;
        owl.SetNewEnvironment(best);   // as MoveCard does, so the saved record carries the cave
        var slot = new SlotInfo(MBSingleton<GraphicsManager>.Instance.CardToSlotType(owl.CardModel.CardType, InGameNPCOrPlayer.Null),
            envSave.AllCardsCount(false));
        owl.CreatedInSaveDataTick = gm.GetCreatedInSaveDataTick;
        owl.IgnoreBaseRow = true;
        var record = owl.SaveByRef();
        envSave.AddRegularCard(record, slot, true);

        if (envSave.GetRegularCards.Count != before + 1 || !CardUtil.TryRemoveCard(owl))
        {
            // Never leave a record AND a live owl: take the record back out and put the card back where it was.
            envSave.GetRegularCards.Remove(record);
            owl.SetNewEnvironment(originalEnv);
            WarnOnce("send-failed", $"the owl could not be moved into cave {bestUid}; it stays on the board.");
            _backoffUntil = Time.realtimeSinceStartup + BackoffSeconds;
            return;
        }
        InvalidateOwlCache();
        Logger?.LogInfo($"[OwlRoost] Dawn: the Owl Companion flew to the nearest cave ({bestUid}, distance {bestDist:0.#}) and will come back at dusk.");
    }

    // ---- DUSK: recall the saved owl ------------------------------------------------------------------------

    private static void RecallOwl(GameManager gm)
    {
        string uid = WolfTickPatch.OwlId;
        var current = gm.CurrentEnvironment.DictionnaryKey;
        foreach (var kv in gm.EnvironmentsData)
        {
            if (kv.Key.Equals(current) || kv.Value == null) continue;
            IList<CardSaveDataByReference> list = kv.Value.GetRegularCards;
            if (list == null) continue;
            for (int i = 0; i < list.Count; i++)
            {
                var rec = list[i];
                if (rec == null || string.IsNullOrEmpty(rec.CardID)) continue;
                if (!(rec.CardID == uid || rec.CardID.StartsWith(uid + "(", StringComparison.Ordinal))) continue;
                if (rec.Special2 >= 1f) continue;   // a "Stay Here" owl stays where the player left it

                int count = list.Count;
                list.RemoveAt(i);
                if (list.Count != count - 1)
                {
                    WarnOnce("remove-failed", "the saved owl record could not be removed; not spawning a second owl.");
                    _backoffUntil = Time.realtimeSinceStartup + BackoffSeconds;
                    return;
                }
                var stats = new Dictionary<string, float>
                {
                    ["SpoilageTime"] = rec.Spoilage,
                    ["UsageDurability"] = rec.Usage,
                    ["Progress"] = rec.ConsumableCharges,
                    ["SpecialDurability1"] = rec.Special1,
                    ["SpecialDurability2"] = 0f,
                };
                SpawnService.Spawn(uid, stats);
                InvalidateOwlCache();
                _pendingRecord = rec;
                _pendingList = list;
                _pendingIndex = i;
                _pendingDeadline = Time.realtimeSinceStartup + VerifySeconds;
                Logger?.LogInfo($"[OwlRoost] Dusk: the Owl Companion is coming back to you (Hunger {rec.Spoilage:0}, Thirst {rec.Usage:0}, Morale {rec.ConsumableCharges:0}).");
                return;
            }
        }
    }

    private static void VerifyRecall(InGameCardBase owl)
    {
        if (owl != null)
        {
            _pendingRecord = null; _pendingList = null;   // confirmed: a live owl is on the board, the record stays removed
            return;
        }
        if (Time.realtimeSinceStartup < _pendingDeadline) return;

        // The spawn produced no live owl: put the record back so the owl is not lost.
        int at = Math.Min(_pendingIndex, _pendingList.Count);
        _pendingList.Insert(at, _pendingRecord);
        _pendingRecord = null; _pendingList = null;
        WarnOnce("recall-failed", "the recalled owl did not appear on the board; its saved record was put back where it was.");
        _backoffUntil = Time.realtimeSinceStartup + BackoffSeconds;
    }
}
