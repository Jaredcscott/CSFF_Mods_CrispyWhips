using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Quest-chain chassis (Documentation/Plans/Community_Mod_Chest/Village_Master_Plan.md §3.4/§10.5 §5.1) — the
    /// single mechanism reused across all 5 villagers' 26 main-chain reveals. Each NPC gets three
    /// hidden player GameStats: cmcStat&lt;NPC&gt;QuestChain (how many quests thanked so far),
    /// cmcStat&lt;NPC&gt;QuestArmed (0/1 — gates the next Offer DialogLine), and
    /// cmcStat&lt;NPC&gt;LastQuestWeek (the Village Clock week the last quest was thanked).
    ///
    /// This patch owns exactly the one thing JSON cannot express: "one village-week has passed
    /// since the last completion, so arm the next offer." Everything else — the offer/delivery/
    /// thanks flow itself, the chain +1, the blueprint FullUnlock — is authored JSON (DialogLine
    /// ActionToPerform.StatModifications + NPCAgent DragAndDropActions), same idiom as the
    /// already-shipped Miller grain / Weaver flax / Apothecary herb quests.
    ///
    /// Piggybacks a single new TickEvents.Interval (CLAUDE.md allows this — the existing polls
    /// belong to unrelated subsystems (Inn dialog scheduling, Professor wander, Apothecary
    /// commute) and folding 5 NPCs' arming logic into any one of them would be a layering
    /// violation, not a poll-count savings); still only ONE new poll total for all 5 NPCs, not 5.
    ///
    /// Also stamps cmcStat&lt;NPC&gt;LastQuestWeek the moment it observes the chain counter
    /// increase (Thanks lines bump the chain declaratively in JSON; DialogLine StatModifications
    /// can only apply a fixed ValueModifier, so it cannot copy the live Village Clock week itself
    /// — this poll does that half of the job by diffing against the last-seen chain value).
    ///
    /// Also mirrors Miller/Weaver/Professor/Apothecary's trust NPCStat into a player-side GameStat
    /// each poll (GameManager.FindNPC(NPCAgent) → InGameNPC.GetStatValue(NPCStat)) — blueprint
    /// StatValues gates can only read player GameStats, never NPCStats, so the T3/T4 trust-threshold
    /// reveals gate on the mirror instead of the raw NPCStat. (Inn Keeper is excluded — his trust
    /// equivalent, cmcStatInnFriendship, is already a plain player GameStat with no mirror needed.)
    /// </summary>
    internal static class QuestChainSchedulePatch
    {
        private const int WeeksBetweenQuests = 1;

        private sealed class ChainNpc
        {
            public string Name;
            public string ChainStatUid;
            public string ArmedStatUid;
            public string LastWeekStatUid;
            public int MaxChain; // stop arming once every main-chain quest is thanked
            public float LastSeenChain = -1f;
        }

        private static readonly ChainNpc[] Npcs =
        {
            new ChainNpc { Name = "Inn", ChainStatUid = "cmcStatInnQuestChain", ArmedStatUid = "cmcStatInnQuestArmed", LastWeekStatUid = "cmcStatInnLastQuestWeek", MaxChain = 8 },
            new ChainNpc { Name = "Miller", ChainStatUid = "cmcStatMillerQuestChain", ArmedStatUid = "cmcStatMillerQuestArmed", LastWeekStatUid = "cmcStatMillerLastQuestWeek", MaxChain = 3 },
            new ChainNpc { Name = "Weaver", ChainStatUid = "cmcStatWeaverQuestChain", ArmedStatUid = "cmcStatWeaverQuestArmed", LastWeekStatUid = "cmcStatWeaverLastQuestWeek", MaxChain = 7 },
            new ChainNpc { Name = "Apothecary", ChainStatUid = "cmcStatApothQuestChain", ArmedStatUid = "cmcStatApothQuestArmed", LastWeekStatUid = "cmcStatApothLastQuestWeek", MaxChain = 2 },
            new ChainNpc { Name = "Professor", ChainStatUid = "cmcStatProfQuestChain", ArmedStatUid = "cmcStatProfQuestArmed", LastWeekStatUid = "cmcStatProfLastQuestWeek", MaxChain = 4 },
        };

        // NPC trust NPCStats (Miller/Weaver/Professor/Apothecary — Inn Keeper's trust equivalent
        // is already a player GameStat, cmcStatInnFriendship) mirrored into player GameStats so
        // blueprint StatValues gates (T3/T4, which key on trust) can read them — StatValues only
        // reads player GameStats, never NPCStats (Village_Master_Plan.md §3.4 "NPC trust values
        // must be mirrored into player GameStats for blueprint gates").
        //
        // Apothecary added 2026-08 alongside CopperChestPatch's N19 (selling into a chest bumps
        // the seller's trust) — she previously had no Trust NPCStat at all; CopperChestPatch.cs
        // (NPCStat/CMC_ApothecaryTrust.json, GameStat/CMC_ApothecaryTrustMirror.json) is the
        // ONLY writer of cmcStatApothecaryTrust today, same as the other three were before her.
        private sealed class TrustMirror
        {
            public string AgentUid;
            public string NpcStatUid;
            public string PlayerStatUid;
        }

        private static readonly TrustMirror[] TrustMirrors =
        {
            new TrustMirror { AgentUid = "cmcMillerAgent", NpcStatUid = "cmcStatMillerTrust", PlayerStatUid = "cmcStatMillerTrustMirror" },
            new TrustMirror { AgentUid = "cmcWeaverAgent", NpcStatUid = "cmcStatWeaverTrust", PlayerStatUid = "cmcStatWeaverTrustMirror" },
            new TrustMirror { AgentUid = "cmcProfessorAgent", NpcStatUid = "cmcStatProfessorTrust", PlayerStatUid = "cmcStatProfessorTrustMirror" },
            new TrustMirror { AgentUid = "cmcApothecaryAgent", NpcStatUid = "cmcStatApothecaryTrust", PlayerStatUid = "cmcStatApothecaryTrustMirror" },
            new TrustMirror { AgentUid = "cmcPotterAgent", NpcStatUid = "cmcStatPotterTrust", PlayerStatUid = "cmcStatPotterTrustMirror" },
        };

        private static bool _initialized;
        private static MethodInfo _getFromIdMethod;
        private static MethodInfo _findNpcMethod;
        private static MethodInfo _hasStatMethod;
        private static MethodInfo _getStatValueMethod;
        private static readonly Dictionary<string, object> _statCache = new(StringComparer.Ordinal);

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            TickEvents.Interval(5f, RunSchedule, "QuestChainSchedule");
            Plugin.Logger.LogDebug("[QuestChainSchedulePatch] initialized.");
        }

        private static void RunSchedule()
        {
            try
            {
                if (!ResolveGetFromId()) return;
                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;

                // Ahead of the chain gates below: since 1.69.0 Chat and trade raise trust from the
                // day a resident moves in, so a mirror held behind "week >= 1" left the Hall boards
                // and the trust-gated blueprints stale for the whole first week. MirrorTrust skips
                // an NPC that has not spawned, which covers "before the intro" on its own.
                foreach (var mirror in TrustMirrors)
                    MirrorTrust(gm, mirror);

                // Ahead of the chain gates too: the Weaver's Sirus23 yarn errand (C10) is shown by this flag
                // alone, and it is kept in step with the load order on every poll, in both directions.
                SyncSirusPresence(gm);
                SyncDurosPresence(gm); // J5: shows the Market Stall's Collect as Coins only while DurosCoinage's coin resolves

                if (!VillageClock.PhaseAtLeast(1f)) return; // no chains before the intro

                int week = VillageClock.WeeksSinceIntro(gm);
                if (week < 1) return;

                foreach (var npc in Npcs)
                    CheckArm(gm, npc, week);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[QuestChainSchedulePatch] RunSchedule failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // C10: the hidden presence flag that shows the Weaver's wool yarn errand. It is 1 only while Sirus23's
        // Wool Yarn card resolves, so the offer, the hand-over and the thank-you never appear without that mod,
        // and a save made with Sirus23 and loaded without it goes back to 0 on the next poll. This rides the
        // poll registered once in Initialize (no new registration, so nothing multiplies on a save reload).
        // Both early returns are expected states (the stat is not in this run yet, or the value is already
        // right), and a missing Sirus23 is a normal state that logs nothing above Debug.
        private const string SirusYarnUid = "sh_yarn";
        private const string SirusPresenceStatUid = "cmcStatSirusYarnPresent";
        private static float _lastSirusPresenceWanted = -1f;

        private static void SyncSirusPresence(object gm)
        {
            float current = ReadStat(gm, SirusPresenceStatUid);
            if (current < 0f) return; // the flag is not in this run's stat list yet: try again next poll

            float wanted = StatRef(SirusYarnUid) != null ? 1f : 0f;
            if (Math.Abs(current - wanted) < 0.01f) return; // already correct
            if (!WriteStat(gm, SirusPresenceStatUid, wanted)) return;

            if (Math.Abs(_lastSirusPresenceWanted - wanted) > 0.01f)
            {
                _lastSirusPresenceWanted = wanted;
                Plugin.Logger.LogDebug($"[QuestChainSchedulePatch] Sirus23 Wool Yarn {(wanted > 0.5f ? "resolves" : "does not resolve")}: the Weaver's yarn errand flag set to {wanted}.");
            }
        }

        // J5: the same shape for DurosCoinage. The hidden flag is 1 only while its Metal Coin (CurrencyValue.CoinUid)
        // resolves, so the stalls' "Collect as Coins" button never shows without that mod, and a save made with it
        // and loaded without it goes back to 0 on the next poll. It rides the poll registered once in Initialize.
        private const string DurosPresenceStatUid = "cmcStatDurosPresent";
        private static float _lastDurosPresenceWanted = -1f;

        private static void SyncDurosPresence(object gm)
        {
            float current = ReadStat(gm, DurosPresenceStatUid);
            if (current < 0f) return; // the flag is not in this run's stat list yet: try again next poll

            float wanted = StatRef(CurrencyValue.CoinUid) != null ? 1f : 0f;
            if (Math.Abs(current - wanted) < 0.01f) return; // the flag already matches
            if (!WriteStat(gm, DurosPresenceStatUid, wanted)) return;

            if (Math.Abs(_lastDurosPresenceWanted - wanted) > 0.01f)
            {
                _lastDurosPresenceWanted = wanted;
                Plugin.Logger.LogDebug($"[QuestChainSchedulePatch] DurosCoinage's coin {(wanted > 0.5f ? "resolves" : "does not resolve")}: the Market Stall coin button flag set to {wanted}.");
            }
        }

        private static void CheckArm(object gm, ChainNpc npc, int currentWeek)
        {
            float chain = ReadStat(gm, npc.ChainStatUid);
            if (chain < 0f) return; // stat not found yet

            // Chain just advanced (a Thanks line fired since we last polled) — stamp the week the
            // JSON side can't write itself, so the NEXT offer arms exactly one week from now.
            if (npc.LastSeenChain >= 0f && chain > npc.LastSeenChain)
            {
                WriteStat(gm, npc.LastWeekStatUid, currentWeek);
                Plugin.Logger.LogInfo($"[QuestChainSchedulePatch] {npc.Name}'s quest chain advanced to {chain} — last-quest week stamped at {currentWeek}.");
            }
            npc.LastSeenChain = chain;

            if (chain >= npc.MaxChain) return; // whole main chain thanked — nothing left to arm

            float armed = ReadStat(gm, npc.ArmedStatUid);
            if (armed < 0f) return;       // stat not found yet

            float lastWeek = ReadStat(gm, npc.LastWeekStatUid);
            if (lastWeek < 0f) return;

            // Offer already live (armed = 1) or item already delivered (armed = 0.5). Pin the
            // last-quest week to now so that when the player accepts the offer — which drops armed
            // to 0, the "accepted, awaiting delivery" state the drag/hand-over action requires — the
            // re-arm gate below does NOT overwrite that 0 back to 1 on the very next 5 s poll and
            // hide the hand-over action. Without this pin the accepted state loses an unwinnable
            // race against the poller (root cause of the "can't hand the item over" reports).
            if (armed >= 0.5f)
            {
                if (currentWeek > lastWeek)
                    WriteStat(gm, npc.LastWeekStatUid, currentWeek);
                return;
            }

            // armed < 0.5: idle between quests (post-thanks) or accepted-awaiting-delivery. Re-arm
            // only once the inter-quest gap has elapsed since the last arm/thanks week.
            if (currentWeek < lastWeek + WeeksBetweenQuests) return;

            if (WriteStat(gm, npc.ArmedStatUid, 1f))
            {
                WriteStat(gm, npc.LastWeekStatUid, currentWeek); // pin so an immediate accept isn't re-armed
                Plugin.Logger.LogInfo($"[QuestChainSchedulePatch] {npc.Name}'s next quest armed (week {currentWeek}, chain {chain}).");
            }
        }

        // ── NPC trust mirror — InGameNPC.GetStatValue via GameManager.FindNPC(NPCAgent) ──

        private static void MirrorTrust(object gm, TrustMirror mirror)
        {
            if (!ResolveNpcStatMethods()) return;

            var agentSo = StatRef(mirror.AgentUid);
            var npcStatSo = StatRef(mirror.NpcStatUid);
            if (agentSo == null || npcStatSo == null) return;

            object inGameNpc;
            try { inGameNpc = _findNpcMethod.Invoke(gm, new object[] { agentSo }); }
            catch (Exception ex)
            {
                Plugin.Logger.LogDebug($"[QuestChainSchedulePatch] FindNPC invoke failed for agent={mirror.AgentUid}: {ex.InnerException?.ToString() ?? ex.ToString()}");
                return;
            }
            if (inGameNpc == null) return; // NPC not spawned yet (resident hasn't moved in)

            bool hasStat;
            try { hasStat = (bool)_hasStatMethod.Invoke(inGameNpc, new object[] { npcStatSo }); }
            catch (Exception ex)
            {
                Plugin.Logger.LogDebug($"[QuestChainSchedulePatch] HasStat invoke failed for npcStat={mirror.NpcStatUid}: {ex.InnerException?.ToString() ?? ex.ToString()}");
                return;
            }
            if (!hasStat) return;

            float value;
            try { value = (float)_getStatValueMethod.Invoke(inGameNpc, new object[] { npcStatSo }); }
            catch (Exception ex)
            {
                Plugin.Logger.LogDebug($"[QuestChainSchedulePatch] GetStatValue invoke failed for npcStat={mirror.NpcStatUid}: {ex.InnerException?.ToString() ?? ex.ToString()}");
                return;
            }

            float mirrored = ReadStat(gm, mirror.PlayerStatUid);
            if (mirrored >= 0f && Math.Abs(mirrored - value) < 0.01f) return; // no change
            WriteStat(gm, mirror.PlayerStatUid, value);
        }

        private static bool ResolveNpcStatMethods()
        {
            if (_findNpcMethod != null && _hasStatMethod != null && _getStatValueMethod != null) return true;

            var gmType = CardUtil.FindGameType("GameManager");
            var inGameNpcType = CardUtil.FindGameType("InGameNPC");
            if (gmType == null || inGameNpcType == null) return false;

            _findNpcMethod ??= gmType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "FindNPC" && m.GetParameters().Length == 1);
            _hasStatMethod ??= inGameNpcType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "HasStat" && m.GetParameters().Length == 1);
            _getStatValueMethod ??= inGameNpcType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "GetStatValue" && m.GetParameters().Length == 1);

            return _findNpcMethod != null && _hasStatMethod != null && _getStatValueMethod != null;
        }

        // ── GameStat access — identical idiom to VillageClock/VillageFounderPerkPatch ──

        private static bool ResolveGetFromId()
        {
            if (_getFromIdMethod != null) return true;
            var uidType = CardUtil.FindGameType("UniqueIDScriptable");
            if (uidType == null) return false;
            _getFromIdMethod = uidType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "GetFromID" && !m.IsGenericMethodDefinition
                    && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
            return _getFromIdMethod != null;
        }

        private static object StatRef(string uid)
        {
            if (_statCache.TryGetValue(uid, out var cached)) return cached;
            var stat = _getFromIdMethod.Invoke(null, new object[] { uid });
            if (stat != null) _statCache[uid] = stat;
            return stat;
        }

        private static float ReadStat(object gm, string uid)
        {
            var stat = StatRef(uid);
            if (stat == null) return -1f;
            if (Reflect.GetMember(gm, "StatsDict") is not IDictionary statsDict) return -1f;
            if (!statsDict.Contains(stat)) return -1f;
            var inGameStat = statsDict[stat];
            if (inGameStat == null) return -1f;
            return Reflect.GetMember(inGameStat, "SimpleCurrentValue") is float f ? f : -1f;
        }

        private static bool WriteStat(object gm, string uid, float value)
        {
            var stat = StatRef(uid);
            if (stat == null) return false;
            if (Reflect.GetMember(gm, "StatsDict") is not IDictionary statsDict) return false;
            if (!statsDict.Contains(stat)) return false;
            var inGameStat = statsDict[stat];
            if (inGameStat == null) return false;
            return Reflect.SetMember(inGameStat, "CurrentBaseValue", value);
        }
    }
}
