using System;
using System.Collections.Generic;
using BepInEx.Logging;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using UnityEngine;

namespace Sirus23ModCollection.Patcher;

/// <summary>
/// The Recall Whistle (sh_recall_whistle, feature 34): its "Blow the Whistle" action calls back every companion
/// (wolf, fox, owl, hawk) that was left behind in ANOTHER environment and brings it onto the player's board.
///
/// A left-behind companion is not a live card. When the player leaves a room, GameManager.ChangeEnvironment saves
/// each card that is not IndependentFromEnv as one CardSaveDataByReference in that room's
/// GameManager.EnvironmentsData entry (GetRegularCards, the live list the save is written from) and removes the live
/// card. So a recall (research W0-24, Documentation/Research/Sirus23_Mod_Collection/community_companions_and_travel.md):
/// <list type="number">
/// <item>finds the record in every OTHER room's saved cards (the current room's entry is stale: its live board is
/// the truth), matched by the card's UniqueID and never by the stay flag, because the owl is also left behind with
/// the flag at 0 (by day, at a cave or building, and by its dawn roost, OwlRoostPatch);</item>
/// <item>removes the record (RemoveAt, then a recount of the same list proves it went), with no reload and no live
/// card to destroy, so there is nothing for Object.Destroy or RemoveCard to do;</item>
/// <item>only then spawns the companion on the current board (SpawnService.Spawn with the record's Hunger, Thirst,
/// Morale and Health, and the stay flag cleared, so it follows again);</item>
/// <item>and confirms within a few real seconds that a live card of that UID is on the player's board. If one is
/// missing, the record goes back where it was, so a companion is never lost and never doubled.</item>
/// </list>
///
/// The owl: by day it roosts in the nearest cave and OwlRoostPatch would send a recalled owl straight back at its
/// next poll, and it never comes into a cave or building. So the whistle calls the owl only at night and only
/// while the player is outdoors (OwlRoostPatch's own conditions); otherwise the owl is skipped and, when it was the
/// only one found, the player is told why. At night the whistle also brings a "Stay Here" owl, which the roost
/// never moves.
///
/// Registered once per process from Plugin.RegisterPatches (an ActionRouter handler and one real-time interval);
/// nothing here re-registers on OnGMInitialized.
/// </summary>
internal static class RecallWhistlePatch
{
    internal const string WhistleId = "sh_recall_whistle";
    private const string BlowActionKey = "sh_recall_whistle_DA0";

    private static readonly string[] CompanionUids =
    {
        WolfTickPatch.WolfId,
        WolfTickPatch.FoxId,
        WolfTickPatch.OwlId,
        WolfTickPatch.HawkId,
    };

    private const float PollSeconds = 1f;
    private const float VerifySeconds = 6f;   // how long a recalled companion has to show up before its record goes back

    private const string MsgAnswerKey = "sh_recall_whistle_Msg_Answer";
    private const string MsgNoneKey = "sh_recall_whistle_Msg_None";
    private const string MsgOwlKey = "sh_recall_whistle_Msg_Owl";

    private static ManualLogSource Logger => Plugin.Logger;

    private sealed class Recall
    {
        public string Uid;
        public CardSaveDataByReference Record;
        public EnvironmentSaveDataByReference Env;
        public int Index;
        public string EnvName;
    }

    // Recalls whose spawn is not yet confirmed, and how many live cards of each UID the board must then hold.
    private static readonly List<Recall> _pending = new();
    private static readonly Dictionary<string, int> _expectedLive = new(StringComparer.Ordinal);
    private static float _deadline;

    private static readonly HashSet<string> _warned = new(StringComparer.Ordinal);

    public static void Register()
    {
        ActionRouter.Register(new ActionHandler
        {
            Name = "RecallWhistle",
            CardUid = WhistleId,
            ActionKeyPrefix = BlowActionKey,
            ActionNamePrefix = "Blow the Whistle",
            Timing = ActionTiming.AfterWrapped,
            After = Blow,
        });
        TickEvents.Interval(PollSeconds, VerifyPending, "RecallWhistle");
    }

    private static void WarnOnce(string cause, string message)
    {
        if (_warned.Add(cause)) Logger?.LogWarning($"[RecallWhistle] {message} Logged once per cause.");
    }

    // ---- the action ------------------------------------------------------------------------------------------

    private static void Blow(ActionContext ctx)
    {
        try
        {
            if (CardUtil.GetGameManagerInstance() is not GameManager gm)
            {
                WarnOnce("no-gm", "no GameManager instance when the whistle was blown; nothing was recalled.");
                return;
            }
            if (gm.EnvironmentsData == null || gm.CurrentEnvironment.EnvCard == null)
            {
                WarnOnce("no-env", "the saved environments or the current environment could not be read; nothing was recalled.");
                return;
            }
            if (_pending.Count > 0)
            {
                Logger?.LogDebug("[RecallWhistle] a recall is still being confirmed; this blow waits for it.");
                return;
            }

            bool owlAllowed = CompanionStayPatch.IsNight() && !IndoorOrCaveEnv.Matches(gm.CurrentEnvironment.EnvCard);
            bool owlSkipped = false;

            // Collect first (each list highest index first, so removing in this order keeps the later indices valid),
            // then remove; the dictionary itself is never modified.
            var found = new List<Recall>();
            var current = gm.CurrentEnvironment.DictionnaryKey;
            foreach (var kv in gm.EnvironmentsData)
            {
                if (kv.Key.Equals(current) || kv.Value == null) continue;
                List<CardSaveDataByReference> list = kv.Value.GetRegularCards;
                if (list == null) continue;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    string uid = MatchCompanion(list[i]?.CardID);
                    if (uid == null) continue;
                    if (uid == WolfTickPatch.OwlId && !owlAllowed)
                    {
                        owlSkipped = true;
                        continue;
                    }
                    found.Add(new Recall { Uid = uid, Record = list[i], Env = kv.Value, Index = i, EnvName = EnvName(kv.Value) });
                }
            }

            if (found.Count == 0)
            {
                Notify(ctx.Card, owlSkipped ? MsgOwlKey : MsgNoneKey, owlSkipped
                    ? "Only your owl could hear it, and it will not come: it roosts by day and never comes into a cave or building."
                    : "Nothing answers the whistle.");
                Logger?.LogInfo(owlSkipped
                    ? "[RecallWhistle] Blown: only an owl was found elsewhere, and it is not called by day or into a cave or building."
                    : "[RecallWhistle] Blown: no companion is left behind in another place.");
                return;
            }

            var baseline = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var r in found)
                if (!baseline.ContainsKey(r.Uid)) baseline[r.Uid] = CountLive(r.Uid);

            int spawned = 0;
            foreach (var r in found)
            {
                if (!RemoveRecord(r)) continue;   // never spawn a companion whose record is still saved elsewhere
                SpawnService.Spawn(r.Uid, StatsFrom(r.Record));
                _pending.Add(r);
                _expectedLive[r.Uid] = (_expectedLive.TryGetValue(r.Uid, out int n) ? n : baseline[r.Uid]) + 1;
                spawned++;
                var rec = r.Record;
                Logger?.LogInfo($"[RecallWhistle] Recalled {r.Uid} from '{r.EnvName}': its saved record was removed there and it is spawned on your board (Hunger {rec.Spoilage:0}, Thirst {rec.Usage:0}, Morale {rec.ConsumableCharges:0}, Health {rec.Special1:0}).");
            }

            if (spawned > 0)
            {
                _deadline = Time.realtimeSinceStartup + VerifySeconds;
                Notify(ctx.Card, MsgAnswerKey, "An answering call: your companion is coming back to you.");
            }
        }
        catch (Exception ex)
        {
            WarnOnce("blow:" + ex.GetType().Name, $"the whistle threw {ex.GetType().Name}: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }

    private static string MatchCompanion(string cardId)
    {
        if (string.IsNullOrEmpty(cardId)) return null;
        foreach (var uid in CompanionUids)
        {
            // CardID is saved as "uid(Name)"; the "(" boundary keeps one UID from matching a longer one.
            if (cardId == uid || cardId.StartsWith(uid + "(", StringComparison.Ordinal)) return uid;
        }
        return null;
    }

    // The saved key reads "uid(Name)..." (UniqueIDScriptable.AddNamesToEnvKey), which names the room in the log.
    private static string EnvName(EnvironmentSaveDataByReference env) => env.DictionaryKey ?? "?";

    /// <summary>Removes the record from its room and proves it went by a recount of the same list.</summary>
    private static bool RemoveRecord(Recall r)
    {
        var list = r.Env.GetRegularCards;
        if (list == null || r.Index >= list.Count || !ReferenceEquals(list[r.Index], r.Record))
        {
            WarnOnce("moved", $"the saved record of {r.Uid} in '{r.EnvName}' moved before it could be removed; it was not recalled.");
            return false;
        }
        int before = list.Count;
        list.RemoveAt(r.Index);
        if (list.Count != before - 1 || list.Contains(r.Record))
        {
            WarnOnce("remove-failed", $"the saved record of {r.Uid} in '{r.EnvName}' could not be removed; not spawning a second one.");
            return false;
        }
        RecalculateWeight(r.Env);
        return true;
    }

    private static void RecalculateWeight(EnvironmentSaveDataByReference env)
    {
        try { env.CalculateWeight(); }
        catch (Exception ex) { Logger?.LogDebug($"[RecallWhistle] the room's saved weight was not recalculated: {ex.Message}"); }
    }

    // All four companions carry the same five stats: Hunger (Spoilage), Thirst (Usage), Morale (Progress), Health
    // (Special1) and the stay flag (Special2), which is cleared so the recalled companion follows again.
    private static Dictionary<string, float> StatsFrom(CardSaveDataByReference rec) => new(StringComparer.Ordinal)
    {
        ["SpoilageTime"] = rec.Spoilage,
        ["UsageDurability"] = rec.Usage,
        ["Progress"] = rec.ConsumableCharges,
        ["SpecialDurability1"] = rec.Special1,
        ["SpecialDurability2"] = 0f,
    };

    // Live cards of the UID on the player's own board (GameQuery.CardsInPlayerEnv pairs the AllCards scan with the
    // environment test), not carried or background cards of another room.
    private static int CountLive(string uid)
    {
        int n = 0;
        foreach (var c in GameQuery.CardsInPlayerEnv())
        {
            if (c is InGameCardBase cb && cb && !cb.Destroyed && CardUtil.GetCardUniqueId(cb) == uid) n++;
        }
        return n;
    }

    // ---- confirmation ----------------------------------------------------------------------------------------

    private static void VerifyPending()
    {
        if (_pending.Count == 0) return;
        try
        {
            if (CardUtil.GetGameManagerInstance() is not GameManager gm)
            {
                // The run ended (main menu): the records belonged to it, there is nothing to put back into.
                _pending.Clear();
                _expectedLive.Clear();
                return;
            }
            if (CardUtil.GetMemberValue(gm, "EnvironmentTransition") is true) return;

            var shortBy = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var kv in _expectedLive)
            {
                int missing = kv.Value - CountLive(kv.Key);
                if (missing > 0) shortBy[kv.Key] = missing;
            }
            if (shortBy.Count == 0)
            {
                Logger?.LogDebug($"[RecallWhistle] confirmed: {_pending.Count} recalled companion(s) are on the board.");
                _pending.Clear();
                _expectedLive.Clear();
                return;
            }
            if (Time.realtimeSinceStartup < _deadline) return;

            // Timed out: put back one record per companion that did not appear, at its old place, newest first.
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var r = _pending[i];
                if (!shortBy.TryGetValue(r.Uid, out int missing) || missing <= 0) continue;
                var list = r.Env.GetRegularCards;
                if (list == null) continue;
                list.Insert(Math.Min(r.Index, list.Count), r.Record);
                RecalculateWeight(r.Env);
                shortBy[r.Uid] = missing - 1;
                Logger?.LogWarning($"[RecallWhistle] {r.Uid} did not appear on the board within {VerifySeconds:0} s; its saved record was put back in '{r.EnvName}', so it is still there.");
            }
            _pending.Clear();
            _expectedLive.Clear();
        }
        catch (Exception ex)
        {
            WarnOnce("verify:" + ex.GetType().Name, $"the recall check threw {ex.GetType().Name}: {ex.InnerException?.ToString() ?? ex.ToString()}");
            _pending.Clear();
            _expectedLive.Clear();
        }
    }

    // ---- player-visible line -----------------------------------------------------------------------------------

    private static void Notify(object card, string key, string fallback)
    {
        try
        {
            if (card is not InGameCardBase cb || !cb) return;
            string text = LocalizationManager.GetText(key, out var t) && !string.IsNullOrEmpty(t) ? t : fallback;
            var gfx = MBSingleton<GraphicsManager>.Instance;
            if (gfx == null)
            {
                WarnOnce("no-gfx", "GraphicsManager is not available, so the whistle's result is not shown on the card.");
                return;
            }
            gfx.PlayCardNotification(cb, text);
        }
        catch (Exception ex)
        {
            WarnOnce("notify:" + ex.GetType().Name, $"the whistle's result line could not be shown: {ex.Message}");
        }
    }
}
