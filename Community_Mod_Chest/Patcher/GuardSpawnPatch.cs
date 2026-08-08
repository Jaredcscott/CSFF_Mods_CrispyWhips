using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using HarmonyLib;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Village Guards — Wave 1 spawn chassis (Village_Master_Plan.md §10.8.1).
    ///
    /// Places the four Town Watch guards on the CMC village map, each at their own post,
    /// the first time the player stands in that post's environment. Cloned from the
    /// current <c>CottageResidentSpawnPatch</c> / <c>AshPartnerSpawnPatch</c> call sites:
    ///   • EA 0.66 signature <c>GameManager.CreateNPC(NPCAgent, SavedNPCCharacterData, bool)</c>
    ///     — the 1-arg overload no longer exists, and CMC 1.36.2 fixed every resident spawn
    ///     for exactly this (§10.8.0.1 rule 7).
    ///   • <c>CreateNPC → Init → AssignOrCreateNPCCards</c>, because vanilla only calls
    ///     AssignOrCreateNPCCards from its boot/load coroutine, so a mid-session NPC never
    ///     gets a board card unless the mod calls it (reference_npcagent_live_spawn_needs_assigncards).
    ///   • A postfix on <c>GameManager.InitializeStatsAndActions</c> re-creates any guard
    ///     that already has NPC save data, so a reload neither duplicates nor loses them.
    ///
    /// Idempotence comes from two independent layers: <see cref="FindLiveNpc"/> short-circuits
    /// before any work, and <c>CreateNPC</c> itself refuses a second InGameNPC for an
    /// NPCAgent that already has one (.decomp/GameManager.cs:3725-3733) — which is also why
    /// the four guards are four sibling NPCAgent assets rather than one shared asset spawned
    /// four times.
    ///
    /// Every spawn path is gated on <see cref="GuardDutyPatch.DutiesReady"/>: InGameNPC
    /// snapshots AgentDuties into DutiesDict once at init (.decomp/InGameNPC.cs:638-661),
    /// so a guard created one tick early would silently never patrol again.
    ///
    /// Per-guard personality (each guard's starting Suspicion) is carried JSON-side by
    /// <c>NPCAgent.Perks</c> → <c>SavedNPCCharacterData</c> (.decomp/SavedNPCCharacterData.cs:104-111,
    /// re-applied on load at :294-301), so nothing here has to apply an NPCCharacterPerk in C#.
    /// </summary>
    internal static class GuardSpawnPatch
    {
        private sealed class Guard
        {
            public string Label;        // log label
            public string AgentUid;
            public string SpawnEnvUid;  // post the guard first appears at
            public object Agent;        // NPCAgent, resolved lazily
        }

        private static readonly Guard[] Guards =
        {
            new Guard { Label = "Captain Reeve Sterling", AgentUid = "cmcGuardSterlingAgent", SpawnEnvUid = "cmcEnvVillage" },
            new Guard { Label = "Guard Nella Thorne",    AgentUid = "cmcGuardThorneAgent",  SpawnEnvUid = "cmcEnvVillagePath" },
            new Guard { Label = "Guard Old Corrin",      AgentUid = "cmcGuardCorrinAgent",  SpawnEnvUid = "cmcEnvVillageFarm" },
            new Guard { Label = "Guard Iris Vane",       AgentUid = "cmcGuardVaneAgent",    SpawnEnvUid = "cmcEnvVillage" },
        };

        private static bool _initialized;

        // Reflection handles, resolved once.
        private static Type _gmType;
        private static Type _npcAgentType;
        private static Type _envIdType;
        private static Type _inGameNpcType;
        private static Type _saveDataByRefType;         // NPCAgentSaveDataByReference (Init's 1st param)
        private static Type _gameSaveDataByRefType;     // GameSaveDataByReference (declares GetNPCData)
        private static Type _savedNpcCharacterDataType; // SavedNPCCharacterData (CreateNPC's 2nd param, EA 0.66+)
        private static MethodInfo _getFromIdMethod;
        private static MethodInfo _createNpcMethod;
        private static MethodInfo _initMethod;
        private static MethodInfo _getNpcDataMethod;
        private static MethodInfo _assignOrCreateCardsMethod;
        private static object _envIdEmpty;

        public static void Initialize(Harmony harmony)
        {
            if (_initialized) return;
            _initialized = true;

            if (!ResolveTypes())
            {
                Plugin.Logger.LogWarning("[GuardSpawnPatch] Required game types not found — village guards inactive.");
                return;
            }

            var initStatsMethod = AccessTools.Method(_gmType, "InitializeStatsAndActions");
            if (initStatsMethod == null)
            {
                Plugin.Logger.LogWarning("[GuardSpawnPatch] GameManager.InitializeStatsAndActions not found — guard reload restore inactive.");
            }
            else
            {
                harmony.Patch(initStatsMethod, postfix: new HarmonyMethod(typeof(GuardSpawnPatch), nameof(RestoreOnLoad_Postfix)));
            }

            TickEvents.Interval(1f, CheckAndSpawnOnArrival, "VillageGuardArrival");
            Plugin.Logger.LogDebug("[GuardSpawnPatch] initialized.");
        }

        private static bool ResolveTypes()
        {
            if (_gmType != null) return true;

            _gmType = CardUtil.FindGameType("GameManager");
            _npcAgentType = CardUtil.FindGameType("NPCAgent");
            _envIdType = CardUtil.FindGameType("EnvID");
            _inGameNpcType = CardUtil.FindGameType("InGameNPC");
            _saveDataByRefType = CardUtil.FindGameType("NPCAgentSaveDataByReference");
            _gameSaveDataByRefType = CardUtil.FindGameType("GameSaveDataByReference");
            _savedNpcCharacterDataType = CardUtil.FindGameType("SavedNPCCharacterData");
            var uidType = CardUtil.FindGameType("UniqueIDScriptable");

            if (_gmType == null || _npcAgentType == null || _envIdType == null || _inGameNpcType == null
                || _saveDataByRefType == null || _gameSaveDataByRefType == null || uidType == null
                || _savedNpcCharacterDataType == null)
                return false;

            _getFromIdMethod = uidType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "GetFromID" && !m.IsGenericMethodDefinition
                    && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
            _createNpcMethod = AccessTools.Method(_gmType, "CreateNPC", new[] { _npcAgentType, _savedNpcCharacterDataType, typeof(bool) });
            _initMethod = AccessTools.Method(_inGameNpcType, "Init", new[] { _saveDataByRefType, _envIdType });
            _getNpcDataMethod = AccessTools.Method(_gameSaveDataByRefType, "GetNPCData", new[] { _npcAgentType });
            _assignOrCreateCardsMethod = AccessTools.Method(_gmType, "AssignOrCreateNPCCards");
            _envIdEmpty = Activator.CreateInstance(_envIdType);

            return _getFromIdMethod != null && _createNpcMethod != null && _initMethod != null
                && _getNpcDataMethod != null && _assignOrCreateCardsMethod != null;
        }

        private static bool ResolveAgent(Guard guard)
        {
            guard.Agent ??= _getFromIdMethod.Invoke(null, new object[] { guard.AgentUid });
            return guard.Agent != null;
        }

        private static object FindLiveNpc(object gm, Guard guard)
        {
            if (Reflect.GetMember(gm, "AllNPCs") is not IEnumerable allNpcs) return null;
            foreach (var npc in allNpcs)
            {
                if (npc == null) continue;
                if (ReferenceEquals(Reflect.GetMember(npc, "NPCModel"), guard.Agent)) return npc;
            }
            return null;
        }

        // The 2nd/3rd CreateNPC params are unused inside CreateNPC's own body beyond
        // SetCharacterData(null) — null/false match the vanilla LoadNPC/boot-coroutine
        // precedent for a first-time spawn with no saved character data. Passing null here
        // is what makes InGameNPC.Init build the character data itself from NPCAgent.Perks
        // (CreateCharacterData → new SavedNPCCharacterData(NPCModel, null, ...)), which is
        // how each guard picks up their own NPCCharacterPerk bundle.
        private static object SpawnAndReturn(object gm, Guard guard)
        {
            _createNpcMethod.Invoke(gm, new object[] { guard.Agent, null, false });
            var npc = FindLiveNpc(gm, guard);
            if (npc == null)
                Plugin.Logger.LogWarning($"[GuardSpawnPatch] SpawnAndReturn: CreateNPC succeeded but the post-spawn AllNPCs identity lookup found no matching NPCModel for {guard.Label} — spawn effectively failed silently.");
            return npc;
        }

        private static void RestoreOnLoad_Postfix(object __instance)
        {
            try
            {
                if (!ResolveTypes()) return;

                var saveData = Reflect.GetMember(__instance, "CurrentSaveData");
                if (saveData == null) return;

                // Build duties synchronously before ANY restore — the 2s build poll has
                // not necessarily run yet at this point in the load, and a guard restored
                // with an empty AgentDuties never patrols again for the session.
                GuardDutyPatch.EnsureBuiltNow();
                if (!GuardDutyPatch.DutiesReady)
                {
                    Plugin.Logger.LogWarning("[GuardSpawnPatch] Guard duties not built at load time — deferring guard restore to the arrival poll.");
                    return;
                }

                foreach (var guard in Guards)
                {
                    if (!ResolveAgent(guard)) continue;
                    if (FindLiveNpc(__instance, guard) != null) continue;

                    var npcSaveData = _getNpcDataMethod.Invoke(saveData, new object[] { guard.Agent });
                    if (npcSaveData == null) continue; // never placed yet — the arrival poll handles first placement

                    var npc = SpawnAndReturn(__instance, guard);
                    if (npc == null) continue;
                    _initMethod.Invoke(npc, new object[] { npcSaveData, _envIdEmpty });
                    Plugin.Logger.LogInfo($"[GuardSpawnPatch] {guard.Label} restored from save.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[GuardSpawnPatch] RestoreOnLoad_Postfix failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // Places each guard the first time the player stands at that guard's post, and
        // picks up any restore the postfix had to defer because duties were not built yet.
        // A guard with existing NPC save data is RESTORED (Init from save data, which
        // rebuilds their environment and stats from the save) rather than re-created from
        // scratch — a fresh Init(null, env) would silently reset their position and their
        // Suspicion value back to the perk default.
        private static void CheckAndSpawnOnArrival()
        {
            try
            {
                if (!ResolveTypes()) return;
                if (!GuardDutyPatch.DutiesReady) return; // §10.8.0.1 rule 6

                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;

                var currentEnv = Reflect.GetMember(gm, "CurrentEnvironment");
                if (currentEnv == null) return;
                if (Reflect.GetMember(currentEnv, "IsNull") is true) return;

                var currentEnvUid = GameQuery.CurrentEnvironmentUniqueId;
                var saveData = Reflect.GetMember(gm, "CurrentSaveData");

                foreach (var guard in Guards)
                {
                    if (!ResolveAgent(guard)) continue;
                    if (FindLiveNpc(gm, guard) != null) continue;

                    // Restore takes priority and is env-independent — the saved data carries
                    // the guard's own environment key.
                    var npcSaveData = saveData == null
                        ? null
                        : _getNpcDataMethod.Invoke(saveData, new object[] { guard.Agent });
                    if (npcSaveData != null)
                    {
                        var restored = SpawnAndReturn(gm, guard);
                        if (restored == null) continue;
                        _initMethod.Invoke(restored, new object[] { npcSaveData, _envIdEmpty });
                        _assignOrCreateCardsMethod.Invoke(gm, null);
                        Plugin.Logger.LogInfo($"[GuardSpawnPatch] {guard.Label} restored from save (deferred past the load postfix).");
                        continue;
                    }

                    // First placement. Spawns land on the CURRENT board only, so a guard can
                    // only take up their post while the player is standing at it
                    // (reference_spawn_targets_current_board).
                    if (currentEnvUid != guard.SpawnEnvUid) continue;

                    // A guard serving out a season after the player bested them does not take up
                    // a post for the first time (§10.8.6). Deliberately NOT applied to the restore
                    // branch above: a guard who was merely routed is still standing on the board,
                    // and skipping their restore would make them vanish on the next reload. Their
                    // downed marker comes back with their save data and keeps them out of the
                    // chase on its own; GuardOutcomePatch clears it when the season is up.
                    if (GuardOutcomePatch.IsRespawnSuppressed(guard.AgentUid)) continue;

                    var npc = SpawnAndReturn(gm, guard);
                    if (npc == null) continue;
                    _initMethod.Invoke(npc, new object[] { null, currentEnv });
                    _assignOrCreateCardsMethod.Invoke(gm, null);

                    Plugin.Logger.LogInfo($"[GuardSpawnPatch] {guard.Label} took up their post at {guard.SpawnEnvUid}.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[GuardSpawnPatch] CheckAndSpawnOnArrival failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }
    }
}
