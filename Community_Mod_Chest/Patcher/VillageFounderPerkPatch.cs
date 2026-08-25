using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using HarmonyLib;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// "Village Founder" perk (cmcperkvillagefounder) — instantly completes every CURRENTLY
    /// SHIPPED village beat: Inn Keeper/Professor intro, all 4 village structures placed
    /// pre-built on their HOME boards (the three Village buildings on first visit to the
    /// Village, the Apothecary's Cabin on first visit to the Foraging Forest — deferred
    /// because spawning targets the current board only), all 4 single-shot NPC quests marked
    /// thanked (with their blueprint reveals), and all 3 cottage residents moved in
    /// immediately. Deliberately does NOT touch anything from the unbuilt layer (Town Hall
    /// Boards, Shadow the Cat, the 37-item reveal sweep, Trust/Renown-gated items — see
    /// Documentation/Plans/Community_Mod_Chest/Village_Master_Plan.md) since none of that exists as spawnable
    /// content yet. The Academy graduate-perk grant previously bundled here has moved to the
    /// standalone "Graduate" perk (<see cref="GraduatePerkPatch"/>, split 2026-07-23) —
    /// Village Founder and Graduate are independent traits now.
    ///
    /// Mechanism: gated on the perk being equipped, runs once per save via a one-shot
    /// "already applied" GameStat latch (cmcStatVillageFounderApplied). Uses the SAME proven
    /// idioms already shipped elsewhere in this mod — VillageClock's StatsDict
    /// SimpleCurrentValue/CurrentBaseValue read/write (reference_gamestat_direct_csharp_write)
    /// and CottageResidentSpawnPatch's CardExistsAnywhere/SpawnService chassis — rather than
    /// inventing any new mechanism.
    /// </summary>
    internal static class VillageFounderPerkPatch
    {
        private const string PerkUid = "cmcperkvillagefounder";
        private const string AppliedStatUid = "cmcStatVillageFounderApplied";
        private const string VillagePhaseStatUid = "cmcStatVillagePhase";
        private const string VillageEpochDayStatUid = "cmcStatVillageEpochDay";
        private const string VillageWeekStatUid = "cmcStatVillageWeek";
        private const string VillageEnvUid = "cmcEnvVillage";
        private const string ForagingForestEnvUid = "cmcEnvForagingForest";
        private const string VillagePlacedStatUid = "cmcStatFounderVillagePlaced";
        private const string ForestPlacedStatUid = "cmcStatFounderForestPlaced";

        private sealed class FounderStructure
        {
            public string Uid;
            public string EnvUid;
        }

        // Each structure belongs on the board of its HOME env — the three Village buildings
        // at cmcEnvVillage, the Apothecary's Cabin at cmcEnvForagingForest. SpawnService.Spawn
        // only targets the CURRENT board, so placement is deferred until the player first
        // stands in each home env (spawning at apply time dropped all four on the run's
        // starting board — confirmed in-game 2026-07-22), latched per env by a hidden
        // GameStat so a deliberately deconstructed building is not force-respawned later.
        private static readonly FounderStructure[] Structures =
        {
            new FounderStructure { Uid = "cmcCottageMiller", EnvUid = VillageEnvUid },
            new FounderStructure { Uid = "cmcCottageWeaver", EnvUid = VillageEnvUid },
            new FounderStructure { Uid = "cmcVillageHall", EnvUid = VillageEnvUid },
            new FounderStructure { Uid = "cmcApothecaryCabin", EnvUid = ForagingForestEnvUid },
        };

        private sealed class TrustBoost
        {
            public string AgentUid;
            public string NpcStatUid;
            public string LatchUid;
        }

        // Trust is a per-agent NPCStat (QuestChainSchedulePatch.TrustMirrors owns the same three
        // UIDs on the read side) — it can only be set on the live InGameNPC instance, which does
        // not exist until CottageResidentSpawnPatch's own move-in poll spawns the resident. Each
        // entry is applied once, latched, the first tick FindNPC resolves.
        private const float TrustBoostValue = 25f;
        private static readonly TrustBoost[] TrustBoosts =
        {
            new TrustBoost { AgentUid = "cmcMillerAgent", NpcStatUid = "cmcStatMillerTrust", LatchUid = "cmcStatFounderMillerTrustBoosted" },
            new TrustBoost { AgentUid = "cmcWeaverAgent", NpcStatUid = "cmcStatWeaverTrust", LatchUid = "cmcStatFounderWeaverTrustBoosted" },
            new TrustBoost { AgentUid = "cmcProfessorAgent", NpcStatUid = "cmcStatProfessorTrust", LatchUid = "cmcStatFounderProfessorTrustBoosted" },
        };

        // Stats confirmed to exist as GameStat/NPCStat JSON (Documentation reconciliation
        // pass, 2026-07-22). Village epoch/week are written separately below (derived from
        // CurrentDay, same as VillageClock.Tick does).
        private static readonly Dictionary<string, float> StatWrites = new(StringComparer.Ordinal)
        {
            [VillagePhaseStatUid] = 1f,
            ["cmcStatMillerQuestChain"] = 3f,
            ["cmcStatMillerQuestArmed"] = 0f,
            ["cmcStatWeaverQuestChain"] = 7f,
            ["cmcStatWeaverQuestArmed"] = 0f,
            ["cmcStatApothecaryHerbQuest"] = 3.5f,
            ["cmcStatApothecaryStallGifted"] = 1f,
            ["cmcStatQuestRenownMillerGrain"] = 1f,
            ["cmcStatQuestRenownWeaverFlax"] = 1f,
            ["cmcStatQuestRenownApothecaryHerbs"] = 1f,
            ["cmcStatQuestRenownProfSpecimen"] = 1f,
            ["cmcStatInnFriendship"] = 25f,
            // Miller/Weaver/Professor trust are NPCStats, not GameStats — they cannot be written
            // through WriteStat (gm.StatsDict holds GameStats only) and no live NPCAgent exists
            // yet at apply time anyway (residents move in asynchronously, see below). Boosted by
            // CheckTrustBoost once each resident is actually spawned — see TrustBoosts.
            // Move-in timers: due "yesterday" (CurrentDay - 1 substituted at apply time) so
            // CottageResidentSpawnPatch's own arrival poll spawns each resident the moment the
            // player next stands in their env — reuses its proven chassis unchanged.
            ["cmcStatMillerMoveIn"] = -1f,
            ["cmcStatWeaverMoveIn"] = -1f,
            ["cmcStatApothecaryMoveIn"] = -1f,
        };

        private static bool _initialized;
        private static MethodInfo _getFromIdMethod;
        private static MethodInfo _findNpcMethod;
        private static MethodInfo _hasStatMethod;
        private static MethodInfo _getStatMethod;
        private static MethodInfo _setStatValueFromEditorMethod;
        private static readonly Dictionary<string, object> _statCache = new(StringComparer.Ordinal);

        public static void Initialize(Harmony harmony)
        {
            if (_initialized) return;
            _initialized = true;

            TickEvents.Interval(3f, TryApply, "VillageFounderPerkApply");
            TickEvents.Interval(1f, CheckStructurePlacement, "VillageFounderStructures");
            TickEvents.Interval(5f, CheckTrustBoost, "VillageFounderTrustBoost");
            Plugin.Logger.LogDebug("[VillageFounderPerkPatch] initialized.");
        }

        private static void TryApply()
        {
            try
            {
                if (!CardUtil.IsPerkEquipped(PerkUid)) return;
                if (!ResolveGetFromId()) return;

                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;

                if (ReadStat(gm, AppliedStatUid) >= 0.5f) return; // already applied this save

                foreach (var kvp in StatWrites)
                {
                    float value = kvp.Value < 0f ? GameQuery.CurrentDay - 1 : kvp.Value;
                    if (!WriteStat(gm, kvp.Key, value))
                        Plugin.Logger.LogWarning($"[VillageFounderPerkPatch] Could not write stat '{kvp.Key}' — perk may apply incompletely.");
                }

                // Village clock epoch/week — back-dated so week-gated dialog reads as
                // "long since introduced" rather than day zero (same fields VillageClock.Tick
                // itself stamps on first observing phase >= 1).
                int backdatedEpoch = Math.Max(1, GameQuery.CurrentDay - 13); // +1 unset-sentinel offset, same as VillageClock
                WriteStat(gm, VillageEpochDayStatUid, backdatedEpoch);
                WriteStat(gm, VillageWeekStatUid, 2f);

                // Structures are NOT placed here — SpawnService.Spawn targets the current
                // board, and at apply time that is the run's starting location, not the
                // Village. CheckStructurePlacement (below) places each building the first
                // time the player stands in its home env.

                if (WriteStat(gm, AppliedStatUid, 1f))
                    Plugin.Logger.LogInfo("[VillageFounderPerkPatch] Village Founder perk applied — village fast-forwarded; buildings appear on first visit to the Village / Foraging Forest.");
                else
                    Plugin.Logger.LogWarning("[VillageFounderPerkPatch] AppliedStatUid write failed — will retry next tick; quest stats may be re-stamped until this resolves.");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[VillageFounderPerkPatch] TryApply failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // Places the founder structures on their HOME boards. Runs only while the player is
        // standing in a home env because SpawnService.Spawn targets the current board and
        // AllCards (the duplicate guard) is current-environment-scoped — both are only
        // correct for the env the player is in.
        private static void CheckStructurePlacement()
        {
            try
            {
                if (!CardUtil.IsPerkEquipped(PerkUid)) return;
                if (!ResolveGetFromId()) return;

                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;
                if (ReadStat(gm, AppliedStatUid) < 0.5f) return; // main fast-forward hasn't run yet

                var envUid = GameQuery.CurrentEnvironmentUniqueId;
                string latchUid;
                if (envUid == VillageEnvUid) latchUid = VillagePlacedStatUid;
                else if (envUid == ForagingForestEnvUid) latchUid = ForestPlacedStatUid;
                else return;

                if (ReadStat(gm, latchUid) >= 0.5f) return; // this env's buildings already placed

                bool allPlaced = true;
                foreach (var s in Structures)
                {
                    if (s.EnvUid != envUid) continue;
                    if (CardExistsAnywhere(gm, s.Uid)) continue;
                    if (SpawnService.Spawn(s.Uid) == null)
                    {
                        allPlaced = false;
                        Plugin.Logger.LogWarning($"[VillageFounderPerkPatch] Failed to spawn structure '{s.Uid}' — will retry next tick.");
                    }
                }

                if (allPlaced && WriteStat(gm, latchUid, 1f))
                    Plugin.Logger.LogInfo($"[VillageFounderPerkPatch] Village Founder buildings placed at '{envUid}'.");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[VillageFounderPerkPatch] CheckStructurePlacement failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // Boosts each resident's trust NPCStat to TrustBoostValue the first tick their NPCAgent
        // is actually live — cannot run any earlier since NPCStats are per-instance (see the
        // TrustBoosts field comment). QuestChainSchedulePatch.MirrorTrust copies the live value
        // into the player-facing GameStat mirror on its own 5s poll, so this only needs to touch
        // the NPCStat itself.
        private static void CheckTrustBoost()
        {
            try
            {
                if (!CardUtil.IsPerkEquipped(PerkUid)) return;
                if (!ResolveGetFromId() || !ResolveNpcStatMethods()) return;

                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;
                if (ReadStat(gm, AppliedStatUid) < 0.5f) return; // main fast-forward hasn't run yet

                foreach (var boost in TrustBoosts)
                {
                    if (ReadStat(gm, boost.LatchUid) >= 0.5f) continue; // already boosted

                    var agentSo = StatRef(boost.AgentUid);
                    var npcStatSo = StatRef(boost.NpcStatUid);
                    if (agentSo == null || npcStatSo == null) continue;

                    object inGameNpc;
                    try { inGameNpc = _findNpcMethod.Invoke(gm, new object[] { agentSo }); }
                    catch (Exception ex)
                    {
                        Plugin.Logger.LogDebug($"[VillageFounderPerkPatch] FindNPC invoke failed for agent={boost.AgentUid}: {ex.InnerException?.ToString() ?? ex.ToString()}");
                        continue;
                    }
                    if (inGameNpc == null) continue; // resident not spawned yet

                    bool hasStat;
                    try { hasStat = (bool)_hasStatMethod.Invoke(inGameNpc, new object[] { npcStatSo }); }
                    catch (Exception ex)
                    {
                        Plugin.Logger.LogDebug($"[VillageFounderPerkPatch] HasStat invoke failed for npcStat={boost.NpcStatUid}: {ex.InnerException?.ToString() ?? ex.ToString()}");
                        continue;
                    }
                    if (!hasStat) continue;

                    object inGameNpcStat;
                    try { inGameNpcStat = _getStatMethod.Invoke(inGameNpc, new object[] { npcStatSo }); }
                    catch (Exception ex)
                    {
                        Plugin.Logger.LogDebug($"[VillageFounderPerkPatch] GetStat invoke failed for npcStat={boost.NpcStatUid}: {ex.InnerException?.ToString() ?? ex.ToString()}");
                        continue;
                    }
                    if (inGameNpcStat == null) continue;

                    try { _setStatValueFromEditorMethod.Invoke(inGameNpcStat, new object[] { TrustBoostValue }); }
                    catch (Exception ex)
                    {
                        Plugin.Logger.LogWarning($"[VillageFounderPerkPatch] SetStatValueFromEditor invoke failed for npcStat={boost.NpcStatUid}: {ex.InnerException?.ToString() ?? ex.ToString()}");
                        continue;
                    }

                    if (WriteStat(gm, boost.LatchUid, 1f))
                        Plugin.Logger.LogInfo($"[VillageFounderPerkPatch] Boosted '{boost.NpcStatUid}' to {TrustBoostValue} for '{boost.AgentUid}'.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[VillageFounderPerkPatch] CheckTrustBoost failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // ── NPC trust access — identical idiom to QuestChainSchedulePatch.MirrorTrust ──

        private static bool ResolveNpcStatMethods()
        {
            if (_findNpcMethod != null && _hasStatMethod != null && _getStatMethod != null && _setStatValueFromEditorMethod != null) return true;

            var gmType = CardUtil.FindGameType("GameManager");
            var inGameNpcType = CardUtil.FindGameType("InGameNPC");
            var inGameNpcStatType = CardUtil.FindGameType("InGameNPCStat");
            if (gmType == null || inGameNpcType == null || inGameNpcStatType == null) return false;

            _findNpcMethod ??= gmType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "FindNPC" && m.GetParameters().Length == 1);
            _hasStatMethod ??= inGameNpcType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "HasStat" && m.GetParameters().Length == 1);
            _getStatMethod ??= inGameNpcType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "GetStat" && m.GetParameters().Length == 1);
            _setStatValueFromEditorMethod ??= inGameNpcStatType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "SetStatValueFromEditor" && m.GetParameters().Length == 1);

            return _findNpcMethod != null && _hasStatMethod != null && _getStatMethod != null && _setStatValueFromEditorMethod != null;
        }

        // ── GameStat access — identical idiom to VillageClock/CottageResidentSpawnPatch ──

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

        private static bool CardExistsAnywhere(object gm, string uid) => FindLiveCard(gm, uid) != null;

        // Returns the live in-game instance of the card with the given UID on the
        // CURRENT board, or null if not present there (AllCards is current-env-scoped).
        private static object FindLiveCard(object gm, string uid)
        {
            if (Reflect.GetMember(gm, "AllCards") is not IEnumerable allCards) return null;
            foreach (var card in allCards)
            {
                if (card == null) continue;
                if (CardUtil.GetCardUniqueId(card) == uid) return card;
            }
            return null;
        }
    }
}
