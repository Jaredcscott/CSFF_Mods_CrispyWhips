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
    /// Phase 0 spike spawn hook (Documentation/Design plan "Convert Ash the Cat into a
    /// hands-free Partner companion"). Debug-only: spawns cmcAshPartnerAgent unconditionally
    /// the first time the player's environment is known, so the Follow/Wander duty-switch
    /// built by AshPartnerDutyPatch can be verified in-game. NOT the real Phase 3 quest-gated
    /// conversion trigger — that replaces this file once the spike is confirmed.
    ///
    /// Mirrors InnKeeperSpawnPatch's proven mid-session NPCAgent spawn idiom (CreateNPC ->
    /// Init -> AssignOrCreateNPCCards, since CreateNPC/Init alone never spawn a board card —
    /// see reference_npcagent_live_spawn_needs_assigncards) and its save/reload restore
    /// path (RestoreOnLoad_Postfix on GameManager.InitializeStatsAndActions), so a reload
    /// after the spike agent exists doesn't duplicate or lose him.
    /// </summary>
    internal static class AshPartnerSpawnPatch
    {
        private const string AgentUid = "cmcAshPartnerAgent";

        private static bool _initialized;

        private static object _agent; // NPCAgent

        private static Type _gmType;
        private static Type _npcAgentType;
        private static Type _envIdType;
        private static Type _inGameNpcType;
        private static Type _saveDataByRefType;     // NPCAgentSaveDataByReference
        private static Type _gameSaveDataByRefType;  // GameSaveDataByReference
        private static Type _savedNpcCharacterDataType; // SavedNPCCharacterData (CreateNPC's 2nd param, EA 0.66+)
        private static MethodInfo _getFromIdMethod;
        private static MethodInfo _createNpcMethod; // GameManager.CreateNPC(NPCAgent, SavedNPCCharacterData, bool) — EA 0.66+ (was (NPCAgent) only pre-0.66)
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
                Plugin.Logger.LogWarning("[AshPartnerSpawnPatch] Required game types not found — Ash Partner spike spawn inactive.");
                return;
            }

            var initStatsMethod = AccessTools.Method(_gmType, "InitializeStatsAndActions");
            if (initStatsMethod != null)
                harmony.Patch(initStatsMethod, postfix: new HarmonyMethod(typeof(AshPartnerSpawnPatch), nameof(RestoreOnLoad_Postfix)));

            TickEvents.Interval(3f, CheckAndSpawn, "AshPartnerSpikeSpawnCheck");
            Plugin.Logger.LogDebug("[AshPartnerSpawnPatch] initialized (Phase 0 spike).");
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

        private static bool ResolveAgent()
        {
            _agent ??= _getFromIdMethod.Invoke(null, new object[] { AgentUid });
            return _agent != null;
        }

        private static object FindLiveNpc(object gm)
        {
            if (Reflect.GetMember(gm, "AllNPCs") is not IEnumerable allNpcs) return null;
            foreach (var npc in allNpcs)
            {
                if (npc == null) continue;
                if (ReferenceEquals(Reflect.GetMember(npc, "NPCModel"), _agent)) return npc;
            }
            return null;
        }

        // The 2nd/3rd CreateNPC params are unused inside its own body (confirmed via decompile) —
        // null/false match the vanilla LoadNPC/boot-coroutine precedent for a first-time spawn with
        // no saved character data.
        private static object SpawnAndReturn(object gm)
        {
            _createNpcMethod.Invoke(gm, new object[] { _agent, null, false });
            if (Reflect.GetMember(gm, "AllNPCs") is not IEnumerable allNpcs)
            {
                Plugin.Logger.LogWarning("[AshPartnerSpawnPatch] SpawnAndReturn: CreateNPC invoked but GameManager.AllNPCs did not resolve — spawn cannot be confirmed.");
                return null;
            }
            object last = null;
            foreach (var npc in allNpcs)
                if (ReferenceEquals(Reflect.GetMember(npc, "NPCModel"), _agent))
                    last = npc;
            if (last == null)
                Plugin.Logger.LogWarning("[AshPartnerSpawnPatch] SpawnAndReturn: CreateNPC succeeded but the post-spawn AllNPCs identity lookup found no matching NPCModel — spawn effectively failed silently.");
            return last;
        }

        private static void RestoreOnLoad_Postfix(object __instance)
        {
            try
            {
                if (!ResolveAgent()) return;
                if (FindLiveNpc(__instance) != null) return;

                var saveData = Reflect.GetMember(__instance, "CurrentSaveData");
                if (saveData == null) return;
                var npcSaveData = _getNpcDataMethod.Invoke(saveData, new object[] { _agent });
                if (npcSaveData == null) return; // never spawned yet — CheckAndSpawn handles first placement

                var npc = SpawnAndReturn(__instance);
                if (npc == null) return;
                _initMethod.Invoke(npc, new object[] { npcSaveData, _envIdEmpty });
                Plugin.Logger.LogInfo("[AshPartnerSpawnPatch] Ash's Partner spike agent restored from save.");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[AshPartnerSpawnPatch] RestoreOnLoad_Postfix failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void CheckAndSpawn()
        {
            try
            {
                if (!ResolveTypes()) return;

                // Wait for AshPartnerDutyPatch to finish attaching AgentDuties — InGameNPC.Init
                // snapshots AgentDuties once; spawning before it's populated would leave this
                // NPC with no duties for the rest of the session (see class doc).
                if (!AshPartnerDutyPatch.DutiesReady) return;

                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;

                var currentEnv = Reflect.GetMember(gm, "CurrentEnvironment");
                if (currentEnv == null) return;
                if (Reflect.GetMember(currentEnv, "IsNull") is true) return;

                if (!ResolveAgent()) return;
                if (FindLiveNpc(gm) != null) return;

                var npc = SpawnAndReturn(gm);
                if (npc == null) return;
                _initMethod.Invoke(npc, new object[] { null, currentEnv });
                _assignOrCreateCardsMethod.Invoke(gm, null);

                Plugin.Logger.LogInfo("[AshPartnerSpawnPatch] Ash's Partner spike agent spawned. Use the [Debug] actions on his card to toggle Follow/Wander and cross an environment transition to verify movement.");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[AshPartnerSpawnPatch] CheckAndSpawn failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }
    }
}
