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
        private const int RestockIntervalDays = 7;

        private sealed class Resident
        {
            public string Name;         // log label
            public string AgentUid;
            public string CottageUid;
            public string MoveInStatUid;
            public string EnvUid;       // env this resident moves into (Village cottages vs. the forest cabin)
            public string RestockActionId; // AgentActions[].ActionID fired weekly into the SATCHEL, or null

            // Copper Chest accrual (Village_Master_Plan.md §10.8.3.3). Mutually exclusive with
            // RestockActionId BY CONSTRUCTION — CheckRestock takes one branch or the other, never
            // both, which is what makes R6's "retarget, don't duplicate" double-drop structurally
            // impossible rather than merely intended.
            public string ChestUid;             // CT2 chest card UniqueID, or null for no chest
            public string ChestEnvUid;          // interior env the chest lives in (accrual only runs while the player is standing in it)
            public string ChestCurrencyActionId;// AgentActions[].ActionID dropping currency into the chest
            public string ChestGoodsActionId;   // AgentActions[].ActionID dropping goods into the chest
            public string ChestRestockDayStat;  // hidden GameStat holding the absolute day of the last chest accrual

            public object Agent;        // NPCAgent, resolved lazily
            public object MoveInStat;   // GameStat SO, resolved lazily
            public int LastRestockDay = int.MinValue;
        }

        private static readonly Resident[] Residents =
        {
            // Miller: satchel restock RETIRED in favour of Copper Chest accrual (§10.8.3.3).
            // RestockActionId is deliberately null — leaving it set alongside the chest fields
            // would be exactly the R6 double-drop bug (goods into the satchel AND the chest every
            // week). His trade stock now comes from StartingInventory plus whatever survives in
            // the chest.
            new Resident { Name = "Miller", AgentUid = "cmcMillerAgent", CottageUid = "cmcCottageMiller", MoveInStatUid = "cmcStatMillerMoveIn", EnvUid = VillageEnvUid,
                           ChestUid = CopperChestPatch.ChestUid, ChestEnvUid = CopperChestPatch.InteriorEnvUid,
                           ChestCurrencyActionId = "MillerChestCurrency", ChestGoodsActionId = "MillerChestGoods",
                           ChestRestockDayStat = "cmcStatMillerChestRestockDay" },
            new Resident { Name = "Weaver", AgentUid = "cmcWeaverAgent", CottageUid = "cmcCottageWeaver", MoveInStatUid = "cmcStatWeaverMoveIn", EnvUid = VillageEnvUid, RestockActionId = "WeaverWeeklyRestock" },
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

        // Restock chassis (InnKeeperSpawnPatch idiom) — fires a named AgentAction on the
        // resident's own NPC/board card once every RestockIntervalDays.
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

        private static bool ResolveAgent(Resident resident)
        {
            resident.Agent ??= _getFromIdMethod.Invoke(null, new object[] { resident.AgentUid });
            return resident.Agent != null;
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

        // Weekly stock restock (Miller/Weaver trade goods) — same chassis as
        // InnKeeperSpawnPatch.CheckRestock: fires the resident's own AgentAction on their
        // live NPC/board card once every RestockIntervalDays. Only residents with a
        // RestockActionId participate (the Apothecary uses its own schedule patch).
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
                    if (!string.IsNullOrEmpty(resident.ChestUid))
                    {
                        CheckChestRestock(gm, resident, currentDay);
                        continue;
                    }

                    if (string.IsNullOrEmpty(resident.RestockActionId)) continue;
                    if (!ResolveAgent(resident)) continue;

                    var npc = FindLiveNpc(gm, resident);
                    if (npc == null) continue; // not moved in yet

                    var associatedCard = Reflect.GetMember(npc, "AssociatedCard");
                    if (associatedCard == null) continue; // AssignOrCreateNPCCards hasn't finished yet

                    int stockCount = (Reflect.GetMember(associatedCard, "CardsInInventory") as ICollection)?.Count ?? 0;

                    if (resident.LastRestockDay == int.MinValue)
                    {
                        // First check this session — just take a baseline, don't force a restock
                        // onto an already-stocked shop every time the mod initializes. An empty
                        // shop (the self-heal case) still falls through and restocks immediately.
                        resident.LastRestockDay = currentDay;
                        if (stockCount > 0) continue;
                    }

                    bool due = stockCount == 0 || currentDay - resident.LastRestockDay >= RestockIntervalDays;
                    if (!due) continue;

                    if (!FireAgentAction(npc, associatedCard, resident.RestockActionId))
                    {
                        Plugin.Logger.LogWarning($"[CottageResidentSpawnPatch] '{resident.RestockActionId}' not found on {resident.AgentUid}'s AgentActions — restock inactive for {resident.Name}.");
                    }
                    resident.LastRestockDay = currentDay;
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[CottageResidentSpawnPatch] CheckRestock failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // Copper Chest accrual (Village_Master_Plan.md §10.8.3.3) — the same FireAgentAction
        // chassis as the satchel restock above, with the CHEST passed as the receiving card
        // instead of the NPC's AssociatedCard. That single substitution IS the retarget; there is
        // no second drop mechanism anywhere (R6).
        //
        // Why this is gated on the player's location, unlike the satchel restock: an NPC's satchel
        // follows the NPC and is reachable from anywhere, but the chest is a board-bound card in an
        // INSTANCED environment. CardFinder scans live scene objects, and GameManager.ChangeEnvironment
        // serialises every non-current environment's cards out of the scene
        // (memory: reference_allcards_env_scoped), so CardFinder.Find returns null for the chest
        // unless the player is standing inside the cottage at that instant. Firing the drop from
        // the location-independent 30 s poll would therefore silently no-op almost every week.
        // Instead the accrual is deferred to a visit and latched into a persistent GameStat, the
        // same defer-poll-latch shape as reference_spawn_targets_current_board.
        //
        // One week's worth per qualifying visit, deliberately NOT a catch-up for every missed week:
        // the caps below already bound the total, so banking owed weeks would add state for no
        // reachable outcome.
        private static void CheckChestRestock(object gm, Resident resident, int currentDay)
        {
            if (GameQuery.CurrentEnvironmentUniqueId != resident.ChestEnvUid) return;

            var chest = CardFinder.Find(resident.ChestUid);
            if (chest == null) return; // interior entered but the chest hasn't spawned yet

            if (!ResolveAgent(resident)) return;
            var npc = FindLiveNpc(gm, resident);
            if (npc == null) return; // hasn't moved in yet — an empty chest until he arrives

            float lastDay = HiddenStat.Get(resident.ChestRestockDayStat);
            if (lastDay < 0f) return;                                              // stat not readable yet
            if (lastDay > 0.5f && currentDay - lastDay < RestockIntervalDays) return; // not due

            // Each half pauses independently once its own ceiling is reached (§10.8.3.3: "the
            // weekly currency drop pauses; goods accrual may continue up to its own separate cap").
            bool currencyDue = CopperChestPatch.CurrentWealth(chest) < CopperChestPatch.CurrencyCap;
            bool goodsDue = CopperChestPatch.GoodsCount(chest) < CopperChestPatch.GoodsCap;

            if (currencyDue && !FireAgentAction(npc, chest, resident.ChestCurrencyActionId))
                Plugin.Logger.LogWarning($"[CottageResidentSpawnPatch] '{resident.ChestCurrencyActionId}' not found on {resident.AgentUid}'s AgentActions — chest currency accrual inactive.");
            if (goodsDue && !FireAgentAction(npc, chest, resident.ChestGoodsActionId))
                Plugin.Logger.LogWarning($"[CottageResidentSpawnPatch] '{resident.ChestGoodsActionId}' not found on {resident.AgentUid}'s AgentActions — chest goods accrual inactive.");

            // Stamped even when both halves are capped, so a full chest doesn't re-check every
            // 30 s for the rest of the visit. Floored at 1 so day 0 can't read back as "never".
            HiddenStat.Set(resident.ChestRestockDayStat, Math.Max(1, currentDay));

            if (currencyDue || goodsDue)
                Plugin.Logger.LogInfo($"[CottageResidentSpawnPatch] {resident.Name} added to his Copper Chest (day {currentDay}; currency {(currencyDue ? "yes" : "capped")}, goods {(goodsDue ? "yes" : "capped")}).");
        }

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
