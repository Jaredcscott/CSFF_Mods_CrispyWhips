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
    /// Cottage residents — the Miller and the Weaver each move into their cottage one week
    /// after the player finishes building it (Village Construction Projects, resident phase;
    /// no interiors — the resident stands on the Village board beside their home).
    ///
    /// Mechanism, per resident:
    ///   1. Move-in timer: a hidden GameStat (cmcStatMillerMoveIn / cmcStatWeaverMoveIn)
    ///      stores the ABSOLUTE day the resident arrives; 0 = cottage not yet seen finished.
    ///      A 5s poll arms it to CurrentDay + 7 the first tick the built cottage card exists,
    ///      checked via a GameManager.AllCards scan (CardExistsAnywhere). NOTE: AllCards is
    ///      CURRENT-ENVIRONMENT-scoped, not global — GameManager.ChangeEnvironment serializes
    ///      the departed board's cards into EnvironmentsData, then Clear()s and rebuilds
    ///      AllCards from background-remaining cards only (.decomp/GameManager.cs
    ///      ChangeEnvironment, ~9609-9677) — so arming can only happen while the player is
    ///      standing at the Village. In practice that IS completion time: the player is on
    ///      the Village board when construction finishes and the poll fires within 5s. If a
    ///      session somehow ends inside that 5s window, the timer arms on the next Village
    ///      visit and the week counts from there instead. GameStats persist in the save
    ///      natively, so the armed timer survives reload. C# writes the stat because
    ///      "current day + 7" is state only C# can compute (authored JSON StatModifications
    ///      cannot read the calendar) — the sanctioned exception to the JSON-only-writes
    ///      rule (see reference_gamestat_direct_csharp_write).
    ///   2. Spawn: the proven InnKeeperSpawnPatch chassis (see its doc comment for why
    ///      WorldSettings.NPCAgents is unusable for mid-run spawns) — a 1s arrival poll
    ///      spawns the agent the first time the player stands at the Village on/after the
    ///      due day: CreateNPC + Init(null, live EnvID) + AssignOrCreateNPCCards (vanilla
    ///      never calls that after the boot coroutine, so the board card only appears if we
    ///      call it ourselves — reference_npcagent_live_spawn_needs_assigncards). The Village
    ///      is an outdoor, non-instanced env, unlike the Inn interior, but the live-EnvID
    ///      path is env-shape-agnostic so the chassis works unchanged.
    ///   3. Reload restore: postfix on GameManager.InitializeStatsAndActions re-creates any
    ///      resident whose NPC save data exists (InGameNPC.Init's _FromData path rebuilds the
    ///      environment from the saved string key).
    /// </summary>
    internal static class CottageResidentSpawnPatch
    {
        private const string VillageEnvUid = "cmcEnvVillage";
        private const string ForagingForestEnvUid = "cmcEnvForagingForest";
        private const int MoveInDelayDays = 7;
        // NOTE: the weekly accrual CADENCE now lives on CopperChestPatch (one interval shared by
        // all five chests) — this file no longer owns a restock interval of its own.

        private sealed class Resident
        {
            public string Name;         // log label
            public string AgentUid;
            public string CottageUid;
            public string MoveInStatUid;
            public string EnvUid;       // env this resident moves into (Village cottages vs. the forest cabin)

            // Copper Chest accrual (Village_Master_Plan.md §10.8.3.3). Set to the resident's own
            // AgentUid when THIS file drives their chest accrual; null when it does not — either
            // because they have no chest, or because another patcher owns their poll (the
            // Apothecary's accrual lives in ApothecarySchedulePatch). All the per-chest data
            // (chest UID, interior env, action IDs, day stat, caps) lives on
            // CopperChestPatch.ChestConfig, so no resident can silently pick up another's cap.
            //
            // Mutually exclusive with RestockActionId BY CONSTRUCTION — CheckRestock takes the
            // chest branch or the satchel branch, never both, which is what makes R6's "retarget,
            // don't duplicate" double-drop structurally impossible rather than merely intended.
            public string ChestAccrualAgentUid;

            public object Agent;        // NPCAgent, resolved lazily
            public object MoveInStat;   // GameStat SO, resolved lazily
        }

        private static readonly Resident[] Residents =
        {
            // Miller and Weaver: satchel restock RETIRED in favour of Copper Chest accrual
            // (§10.8.3.3). RestockActionId is deliberately null on both — leaving it set alongside
            // ChestAccrualAgentUid would be exactly the R6 double-drop bug (goods into the satchel
            // AND the chest every week). Their trade stock now comes from StartingInventory plus
            // whatever survives in the chest. The Weaver's old 'WeaverWeeklyRestock' AgentAction is
            // still shipped in Agent_Weaver.json but is now unfired, the same state the Miller's
            // retired action has been in since 1.38.0.
            new Resident { Name = "Miller", AgentUid = "cmcMillerAgent", CottageUid = "cmcCottageMiller", MoveInStatUid = "cmcStatMillerMoveIn", EnvUid = VillageEnvUid,
                           ChestAccrualAgentUid = "cmcMillerAgent" },
            new Resident { Name = "Weaver", AgentUid = "cmcWeaverAgent", CottageUid = "cmcCottageWeaver", MoveInStatUid = "cmcStatWeaverMoveIn", EnvUid = VillageEnvUid,
                           ChestAccrualAgentUid = "cmcWeaverAgent" },
            // Apothecary: she HAS a Copper Chest, but ApothecarySchedulePatch owns her poll and
            // therefore her chest accrual (§10.8.3.3). ChestAccrualAgentUid stays null here so this
            // file never fires a second, competing drop — that would be the R6 double-drop across
            // two files rather than within one.
            new Resident { Name = "Apothecary", AgentUid = "cmcApothecaryAgent", CottageUid = "cmcApothecaryCabin", MoveInStatUid = "cmcStatApothecaryMoveIn", EnvUid = ForagingForestEnvUid },
        };

        private static bool _initialized;

        // Reflection handles, resolved once.
        private static Type _gmType;
        private static Type _npcAgentType;
        private static Type _envIdType;
        private static Type _inGameNpcType;
        private static Type _saveDataByRefType;     // NPCAgentSaveDataByReference (Init's 1st param)
        private static Type _gameSaveDataByRefType; // GameSaveDataByReference (declares GetNPCData)
        private static Type _savedNpcCharacterDataType; // SavedNPCCharacterData (CreateNPC's 2nd param, EA 0.66+)
        private static MethodInfo _getFromIdMethod; // UniqueIDScriptable.GetFromID(string)
        private static MethodInfo _createNpcMethod; // GameManager.CreateNPC(NPCAgent, SavedNPCCharacterData, bool) — EA 0.66+ (was (NPCAgent) only pre-0.66)
        private static MethodInfo _initMethod;      // InGameNPC.Init(NPCAgentSaveDataByReference, EnvID)
        private static MethodInfo _getNpcDataMethod;          // GameSaveDataByReference.GetNPCData(NPCAgent)
        private static MethodInfo _assignOrCreateCardsMethod; // GameManager.AssignOrCreateNPCCards() (private, no args)
        private static object _envIdEmpty;          // default(EnvID)

        // AgentAction firing chassis (InnKeeperSpawnPatch idiom) — fires a named AgentAction with a
        // chosen receiving card. Passed to CopperChestPatch.TryAccrue as its drop mechanism.
        private static Type _npcActionType;         // NPCAction (declares ToAction)
        private static Type _inGameCardBaseType;    // InGameCardBase
        private static Type _cardActionType;        // CardAction
        private static Type _inGameNpcOrPlayerType; // InGameNPCOrPlayer (struct)
        private static MethodInfo _toActionMethod;   // NPCAction.ToAction(InGameNPC, InGameCardBase) -> CardAction
        private static MethodInfo _performActionMethod; // GameManager.PerformAction(CardAction, InGameCardBase, bool, InGameNPCOrPlayer) (static)
        private static ConstructorInfo _inGameNpcOrPlayerCtor; // InGameNPCOrPlayer(InGameNPC)

        public static void Initialize(Harmony harmony)
        {
            if (_initialized) return;
            _initialized = true;

            if (!ResolveTypes())
            {
                Plugin.Logger.LogWarning("[CottageResidentSpawnPatch] Required game types not found — cottage residents inactive.");
                return;
            }

            var initStatsMethod = AccessTools.Method(_gmType, "InitializeStatsAndActions");
            if (initStatsMethod == null)
            {
                Plugin.Logger.LogWarning("[CottageResidentSpawnPatch] GameManager.InitializeStatsAndActions not found — resident reload restore inactive.");
            }
            else
            {
                harmony.Patch(initStatsMethod, postfix: new HarmonyMethod(typeof(CottageResidentSpawnPatch), nameof(RestoreOnLoad_Postfix)));
            }

            TickEvents.Interval(5f, CheckMoveInTimers, "CottageResidentMoveIn");
            TickEvents.Interval(1f, CheckAndSpawnOnArrival, "CottageResidentArrival");
            TickEvents.Interval(30f, CheckRestock, "CottageResidentRestock");

            Plugin.Logger.LogDebug("[CottageResidentSpawnPatch] initialized.");
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
            _npcActionType = CardUtil.FindGameType("NPCAction");
            _inGameCardBaseType = CardUtil.FindGameType("InGameCardBase");
            _cardActionType = CardUtil.FindGameType("CardAction");
            _inGameNpcOrPlayerType = CardUtil.FindGameType("InGameNPCOrPlayer");
            _savedNpcCharacterDataType = CardUtil.FindGameType("SavedNPCCharacterData");
            var uidType = CardUtil.FindGameType("UniqueIDScriptable");

            if (_gmType == null || _npcAgentType == null || _envIdType == null
                || _inGameNpcType == null || _saveDataByRefType == null || _gameSaveDataByRefType == null || uidType == null
                || _npcActionType == null || _inGameCardBaseType == null || _cardActionType == null || _inGameNpcOrPlayerType == null
                || _savedNpcCharacterDataType == null)
                return false;

            _getFromIdMethod = uidType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "GetFromID" && !m.IsGenericMethodDefinition
                    && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
            _createNpcMethod = AccessTools.Method(_gmType, "CreateNPC", new[] { _npcAgentType, _savedNpcCharacterDataType, typeof(bool) });
            _initMethod = AccessTools.Method(_inGameNpcType, "Init", new[] { _saveDataByRefType, _envIdType });
            _getNpcDataMethod = AccessTools.Method(_gameSaveDataByRefType, "GetNPCData", new[] { _npcAgentType });
            _assignOrCreateCardsMethod = AccessTools.Method(_gmType, "AssignOrCreateNPCCards");
            _toActionMethod = AccessTools.Method(_npcActionType, "ToAction", new[] { _inGameNpcType, _inGameCardBaseType });
            _performActionMethod = _gmType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "PerformAction" && m.GetParameters().Length == 4);
            _inGameNpcOrPlayerCtor = _inGameNpcOrPlayerType.GetConstructor(new[] { _inGameNpcType });
            _envIdEmpty = Activator.CreateInstance(_envIdType);

            return _getFromIdMethod != null && _createNpcMethod != null && _initMethod != null
                && _getNpcDataMethod != null && _assignOrCreateCardsMethod != null
                && _toActionMethod != null && _performActionMethod != null && _inGameNpcOrPlayerCtor != null;
        }

        private static readonly HashSet<string> _agentUnresolvedWarned = new(StringComparer.Ordinal);

        private static bool ResolveAgent(Resident resident)
        {
            resident.Agent ??= _getFromIdMethod.Invoke(null, new object[] { resident.AgentUid });
            if (resident.Agent == null)
            {
                if (_agentUnresolvedWarned.Add(resident.AgentUid))
                    Plugin.Logger.LogWarning($"[CottageResidentSpawnPatch] Agent UID '{resident.AgentUid}' ({resident.Name}) did not resolve via GetFromID — this resident can never spawn until this is fixed.");
                return false;
            }
            return true;
        }

        // Move-in stat read/write — the InnKeeperDialogSchedulePatch idiom: read
        // SimpleCurrentValue, write CurrentBaseValue, both on the live InGameStat pulled from
        // GameManager.StatsDict. The read half is runtime-verified (LostCatPatch, InnKeeper
        // Phase 1 acid test); the CurrentBaseValue write half is authored-per-memory
        // (reference_gamestat_direct_csharp_write) but still awaits its own in-game
        // verification. Returns -1 when the stat isn't resolvable yet (pre-init).
        private static float GetMoveInDay(object gm, Resident resident)
        {
            var inGameStat = ResolveMoveInStatInstance(gm, resident);
            if (inGameStat == null) return -1f;
            return Reflect.GetMember(inGameStat, "SimpleCurrentValue") is float f ? f : -1f;
        }

        private static void SetMoveInDay(object gm, Resident resident, float value)
        {
            var inGameStat = ResolveMoveInStatInstance(gm, resident);
            if (inGameStat == null) return;
            Reflect.SetMember(inGameStat, "CurrentBaseValue", value);
        }

        private static object ResolveMoveInStatInstance(object gm, Resident resident)
        {
            resident.MoveInStat ??= _getFromIdMethod.Invoke(null, new object[] { resident.MoveInStatUid });
            if (resident.MoveInStat == null) return null;

            if (Reflect.GetMember(gm, "StatsDict") is not IDictionary statsDict) return null;
            if (!statsDict.Contains(resident.MoveInStat)) return null;
            return statsDict[resident.MoveInStat];
        }

        // Global built-state check over GameManager.AllCards (every environment, not just the
        // current board) — same idiom as InnKeeperDialogSchedulePatch.CardExistsAnywhere.
        private static bool CardExistsAnywhere(object gm, string uid)
        {
            if (Reflect.GetMember(gm, "AllCards") is not IEnumerable allCards) return false;
            foreach (var card in allCards)
            {
                if (card == null) continue;
                var model = Reflect.GetMember(card, "CardModel");
                if (model == null) continue;
                if (Reflect.GetMember(model, "UniqueID") as string == uid) return true;
            }
            return false;
        }

        // Returns the live InGameNPC for the resident's agent, or null if not created yet.
        private static object FindLiveNpc(object gm, Resident resident)
        {
            if (Reflect.GetMember(gm, "AllNPCs") is not IEnumerable allNpcs) return null;
            foreach (var npc in allNpcs)
            {
                if (npc == null) continue;
                if (ReferenceEquals(Reflect.GetMember(npc, "NPCModel"), resident.Agent)) return npc;
            }
            return null;
        }

        // Invokes GameManager.CreateNPC(NPCAgent, SavedNPCCharacterData, bool) (idempotent — no-ops if
        // AllNPCs already holds this agent) and returns the InGameNPC it added. The 2nd/3rd params are
        // unused inside CreateNPC's own body (confirmed via decompile) — null/false match the vanilla
        // LoadNPC/boot-coroutine precedent for a first-time spawn with no saved character data.
        private static object SpawnAndReturn(object gm, Resident resident)
        {
            _createNpcMethod.Invoke(gm, new object[] { resident.Agent, null, false });
            var npc = FindLiveNpc(gm, resident);
            if (npc == null)
                Plugin.Logger.LogWarning($"[CottageResidentSpawnPatch] SpawnAndReturn: CreateNPC succeeded but the post-spawn AllNPCs identity lookup found no matching NPCModel for {resident.Name} — spawn effectively failed silently.");
            return npc;
        }

        private static void RestoreOnLoad_Postfix(object __instance)
        {
            try
            {
                if (!ResolveTypes()) return;
                var saveData = Reflect.GetMember(__instance, "CurrentSaveData");
                if (saveData == null) return;

                foreach (var resident in Residents)
                {
                    if (!ResolveAgent(resident)) continue;
                    if (FindLiveNpc(__instance, resident) != null) continue;

                    var npcSaveData = _getNpcDataMethod.Invoke(saveData, new object[] { resident.Agent });
                    if (npcSaveData == null) continue; // never placed yet — the arrival check handles first placement

                    var npc = SpawnAndReturn(__instance, resident);
                    if (npc == null) continue;
                    _initMethod.Invoke(npc, new object[] { npcSaveData, _envIdEmpty });
                    Plugin.Logger.LogInfo($"[CottageResidentSpawnPatch] {resident.Name} restored from save.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[CottageResidentSpawnPatch] RestoreOnLoad_Postfix failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // Arms each resident's move-in day the first tick their finished cottage exists.
        private static void CheckMoveInTimers()
        {
            try
            {
                if (!ResolveTypes()) return;
                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;

                foreach (var resident in Residents)
                {
                    float dueDay = GetMoveInDay(gm, resident);
                    if (dueDay < 0f || dueDay >= 0.5f) continue; // unresolvable yet, or already armed

                    if (!CardExistsAnywhere(gm, resident.CottageUid)) continue; // cottage not built yet

                    int arrivalDay = GameQuery.CurrentDay + MoveInDelayDays;
                    SetMoveInDay(gm, resident, arrivalDay);
                    Plugin.Logger.LogInfo($"[CottageResidentSpawnPatch] {resident.Name}'s cottage finished on day {GameQuery.CurrentDay} — the {resident.Name} moves in on day {arrivalDay}.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[CottageResidentSpawnPatch] CheckMoveInTimers failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // Spawns a due resident the first time the player stands at the Village on/after
        // their move-in day.
        private static void CheckAndSpawnOnArrival()
        {
            try
            {
                if (!ResolveTypes()) return;
                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;

                var currentEnvUid = GameQuery.CurrentEnvironmentUniqueId;

                var currentEnv = Reflect.GetMember(gm, "CurrentEnvironment");
                if (currentEnv == null) return;
                if (Reflect.GetMember(currentEnv, "IsNull") is true) return;

                foreach (var resident in Residents)
                {
                    // Each resident moves into their own env (Village cottages vs. the forest
                    // cabin) — only place one while the player is standing in that env, so the
                    // spawn reuses the live CurrentEnvironment EnvID (same reason as the Village-
                    // only check this replaced: AllCards/board seeding is current-env scoped).
                    if (currentEnvUid != resident.EnvUid) continue;
                    if (!ResolveAgent(resident)) continue;
                    // Cheapest guard first: AllNPCs holds a handful of entries, so once the
                    // resident has moved in this short-circuits the per-second card scan below
                    // for the rest of the run.
                    if (FindLiveNpc(gm, resident) != null) continue;                // already moved in

                    float dueDay = GetMoveInDay(gm, resident);
                    if (dueDay < 0.5f) continue;                                    // timer not armed (or unresolvable)
                    if (GameQuery.CurrentDay < dueDay - 0.01f) continue;            // not due yet
                    if (!CardExistsAnywhere(gm, resident.CottageUid)) continue;     // save rolled back past the build

                    var npc = SpawnAndReturn(gm, resident);
                    if (npc == null) continue;
                    _initMethod.Invoke(npc, new object[] { null, currentEnv });

                    // CreateNPC/Init only build the InGameNPC data object — vanilla only calls
                    // AssignOrCreateNPCCards() from the boot/load coroutine, so nothing else
                    // ever gives a mid-session NPC its board card unless we call it here.
                    _assignOrCreateCardsMethod.Invoke(gm, null);
                    Plugin.Logger.LogInfo($"[CottageResidentSpawnPatch] The {resident.Name} has moved into the village (day {GameQuery.CurrentDay}, due day {dueDay:0}).");
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[CottageResidentSpawnPatch] CheckAndSpawnOnArrival failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // Weekly Copper Chest accrual for the residents this file owns (Miller, Weaver).
        //
        // <para>This used to also carry a SATCHEL restock branch, for residents whose weekly
        // AgentAction dropped into their own carried inventory. That branch is gone as of 1.46.0:
        // both cottage residents are now on Copper Chests (§10.8.3.3's "retarget, don't
        // duplicate" — R6), so it was unreachable, and the compiler said so. It is deliberately
        // deleted rather than left dormant: a dead `RestockActionId` field is an attractive
        // nuisance, and the obvious way to "fix" the resulting unused-field warning — re-pointing
        // a resident at their old satchel action — is precisely the double-drop R6 warns about.
        // A future chest-less resident gets the branch back from git history; a resident WITH a
        // chest must never have one.</para>
        private static void CheckRestock()
        {
            try
            {
                if (!ResolveTypes()) return;
                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;

                int currentDay = GameQuery.CurrentDay;

                foreach (var resident in Residents)
                {
                    // Null for any resident whose accrual another patcher owns (the Apothecary's
                    // lives in ApothecarySchedulePatch) — exactly one caller per chest, ever.
                    var chestConfig = CopperChestPatch.ForAgent(resident.ChestAccrualAgentUid);
                    if (chestConfig == null) continue;

                    if (!ResolveAgent(resident)) continue;
                    var npc = FindLiveNpc(gm, resident);
                    if (npc == null) continue; // hasn't moved in yet — an empty chest until they arrive

                    CopperChestPatch.TryAccrue(chestConfig, npc, currentDay, FireAgentAction);
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[CottageResidentSpawnPatch] CheckRestock failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // Fires a named AgentAction with `associatedCard` as the receiving container. Used for both
        // the satchel restock (receiving card = the NPC's own AssociatedCard) and the Copper Chest
        // accrual (receiving card = the CHEST) — that single substitution IS §10.8.3.3's retarget,
        // and it is why CopperChestPatch.TryAccrue takes this method as a delegate rather than
        // owning a spawn mechanism of its own.
        private static bool FireAgentAction(object npc, object associatedCard, string actionId)
        {
            var npcModel = Reflect.GetMember(npc, "NPCModel");
            var agentActions = Reflect.GetMember(npcModel, "AgentActions") as Array;
            object matchedAction = null;
            if (agentActions != null)
            {
                foreach (var action in agentActions)
                {
                    if (action != null && Reflect.GetMember(action, "ActionID") as string == actionId)
                    {
                        matchedAction = action;
                        break;
                    }
                }
            }
            if (matchedAction == null) return false;

            var cardAction = _toActionMethod.Invoke(matchedAction, new object[] { npc, associatedCard });
            var user = _inGameNpcOrPlayerCtor.Invoke(new object[] { npc });
            _performActionMethod.Invoke(null, new object[] { cardAction, associatedCard, true, user });
            return true;
        }
    }
}
