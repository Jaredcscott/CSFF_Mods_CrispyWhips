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
    /// Village Inn Keeper — Phase 1 acid test (Documentation/Design/Village_InnKeeper_Plan.md).
    ///
    /// Phase 0 finding: vanilla's boot-time WorldSettings.NPCAgents/StartingEnv spawn loop can
    /// NEVER place an NPC into an instanced environment. It builds the placement EnvID via the
    /// single-arg `EnvID(CardData)` constructor, which rejects both non-Environment cards
    /// (CardType != Environment) AND InstancedEnvironment:true cards outright
    /// (.decomp/EnvID.cs:189-216) — and that constructor is exactly what the boot loop
    /// (.decomp/GameManager.cs:2338-2346) and the save-reload path (LoadNPC,
    /// .decomp/GameManager.cs:3563-3567) both call. cmcInnInterior is CT4 +
    /// InstancedEnvironment:true (CardData/Environment/CMC_InnInterior.json), so this fails
    /// regardless of which card is chosen as StartingEnv — a structural limitation, not a
    /// card-choice problem. The only place vanilla ever builds a VALID EnvID for an instanced
    /// env is the 3-arg `EnvID(CardData, EnvID _FromEnv, int _WithIndex)` constructor, used by
    /// CardAction.TravelDestination (.decomp/CardAction.cs:896-929) the moment the player clicks
    /// "Enter the Inn" — it needs to know where the player was standing, which doesn't exist at
    /// game-boot time.
    ///
    /// So the Keeper is never registered in WorldSettings.NPCAgents (confirmed the ONLY reader
    /// of that array — .decomp/GameManager.cs + .decomp/WorldSettings.cs). Instead:
    ///   - Boot (InitializeStatsAndActions postfix): if the Keeper already has NPC save data
    ///     (a prior session placed it), restore it directly — InGameNPC.Init's _FromData path
    ///     rebuilds CurrentEnvironment from the SAVED STRING KEY (the EnvID(string) ctor),
    ///     which bypasses the CardType/instanced guards entirely (.decomp/InGameNPC.cs:364-380).
    ///   - Polled arrival check (TickEvents, not an Update loop): the first time the player's
    ///     own CurrentEnvironment matches cmcInnInterior, spawn the Keeper there reusing that
    ///     EnvID verbatim — it already carries the correct instanced-env parent chain.
    /// </summary>
    internal static class InnKeeperSpawnPatch
    {
        private const string InnKeeperAgentUid = "cmcInnKeeperAgent";
        private const string InnInteriorUid = "cmcInnInterior";
        private const string RestockActionId = "InnKeeperMorningRestock";
        private const int RestockIntervalDays = 4;

        private static bool _initialized;

        // Resolved once game data is loaded.
        private static object _agent;       // NPCAgent
        private static object _innInterior; // CardData

        // Reflection handles, resolved once.
        private static Type _gmType;
        private static Type _npcAgentType;
        private static Type _envIdType;
        private static Type _inGameNpcType;
        private static Type _saveDataByRefType;     // NPCAgentSaveDataByReference (Init's 1st param)
        private static Type _gameSaveDataByRefType; // GameSaveDataByReference (declares GetNPCData; type of GameManager.CurrentSaveData)
        private static Type _npcActionType;         // NPCAction (declares ToAction)
        private static Type _inGameCardBaseType;    // InGameCardBase
        private static Type _cardActionType;        // CardAction
        private static Type _inGameNpcOrPlayerType; // InGameNPCOrPlayer (struct)
        private static Type _savedNpcCharacterDataType; // SavedNPCCharacterData (CreateNPC's 2nd param, EA 0.66+)
        private static MethodInfo _getFromIdMethod; // UniqueIDScriptable.GetFromID(string)
        private static MethodInfo _createNpcMethod;  // GameManager.CreateNPC(NPCAgent, SavedNPCCharacterData, bool) — EA 0.66+ (was (NPCAgent) only pre-0.66)
        private static MethodInfo _initMethod;       // InGameNPC.Init(NPCAgentSaveDataByReference, EnvID)
        private static MethodInfo _getNpcDataMethod; // GameSaveDataByReference.GetNPCData(NPCAgent) -> NPCAgentSaveDataByReference
        private static MethodInfo _assignOrCreateCardsMethod; // GameManager.AssignOrCreateNPCCards() (private, no args)
        private static MethodInfo _toActionMethod;   // NPCAction.ToAction(InGameNPC, InGameCardBase) -> CardAction
        private static MethodInfo _performActionMethod; // GameManager.PerformAction(CardAction, InGameCardBase, bool, InGameNPCOrPlayer) (static)
        private static ConstructorInfo _inGameNpcOrPlayerCtor; // InGameNPCOrPlayer(InGameNPC)
        private static object _envIdEmpty;           // default(EnvID)

        // Restock cadence state (in-memory; resets on process relaunch — acceptable for a
        // shop-stock timer, and self-heals an empty pantry immediately regardless).
        private static int _lastRestockDay = int.MinValue;
        private static bool _missingRestockActionWarned;

        public static void Initialize(Harmony harmony)
        {
            if (_initialized) return;
            _initialized = true;

            if (!ResolveTypes())
            {
                Plugin.Logger.LogWarning("[InnKeeperSpawnPatch] Required game types not found — Inn Keeper spawn inactive.");
                return;
            }

            var initStatsMethod = AccessTools.Method(_gmType, "InitializeStatsAndActions");
            if (initStatsMethod == null)
            {
                Plugin.Logger.LogWarning("[InnKeeperSpawnPatch] GameManager.InitializeStatsAndActions not found — Inn Keeper reload restore inactive.");
            }
            else
            {
                harmony.Patch(initStatsMethod, postfix: new HarmonyMethod(typeof(InnKeeperSpawnPatch), nameof(RestoreOnLoad_Postfix)));
            }

            TickEvents.Interval(1f, CheckAndSpawnOnArrival, "InnKeeperArrivalCheck");
            TickEvents.Interval(30f, CheckRestock, "InnKeeperRestockCheck");

            Plugin.Logger.LogDebug("[InnKeeperSpawnPatch] initialized.");
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

        private static bool ResolveRefs()
        {
            if (_agent != null && _innInterior != null) return true;
            _agent ??= _getFromIdMethod.Invoke(null, new object[] { InnKeeperAgentUid });
            _innInterior ??= CardUtil.GetCardDataById(InnInteriorUid);
            return _agent != null && _innInterior != null;
        }

        // Returns the live InGameNPC for our agent, or null if it hasn't been created yet.
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

        private static bool AlreadySpawned(object gm) => FindLiveNpc(gm) != null;

        // Invokes the private GameManager.CreateNPC(NPCAgent, SavedNPCCharacterData, bool) (idempotent —
        // no-ops if AllNPCs already holds this agent) and returns the InGameNPC it just added. The 2nd/3rd
        // params are unused inside CreateNPC's own body (confirmed via decompile) — null/false match the
        // vanilla LoadNPC/boot-coroutine precedent for a first-time spawn with no saved character data.
        private static object SpawnAndReturn(object gm)
        {
            _createNpcMethod.Invoke(gm, new object[] { _agent, null, false });
            if (Reflect.GetMember(gm, "AllNPCs") is not IEnumerable allNpcs)
            {
                Plugin.Logger.LogWarning("[InnKeeperSpawnPatch] SpawnAndReturn: CreateNPC invoked but GameManager.AllNPCs did not resolve — spawn cannot be confirmed.");
                return null;
            }
            object last = null;
            foreach (var npc in allNpcs)
                if (ReferenceEquals(Reflect.GetMember(npc, "NPCModel"), _agent))
                    last = npc;
            if (last == null)
                Plugin.Logger.LogWarning("[InnKeeperSpawnPatch] SpawnAndReturn: CreateNPC succeeded but the post-spawn AllNPCs identity lookup found no matching NPCModel — Inn Keeper spawn effectively failed silently.");
            return last;
        }

        private static void RestoreOnLoad_Postfix(object __instance)
        {
            try
            {
                if (!ResolveRefs()) return;
                if (AlreadySpawned(__instance)) return;

                var saveData = Reflect.GetMember(__instance, "CurrentSaveData");
                if (saveData == null) return;
                var npcSaveData = _getNpcDataMethod.Invoke(saveData, new object[] { _agent });
                if (npcSaveData == null) return; // never placed yet — the arrival check handles first placement

                var npc = SpawnAndReturn(__instance);
                if (npc == null) return;
                _initMethod.Invoke(npc, new object[] { npcSaveData, _envIdEmpty });
                Plugin.Logger.LogInfo("[InnKeeperSpawnPatch] Inn Keeper restored from save.");
                ForceRestock(__instance, npc);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[InnKeeperSpawnPatch] RestoreOnLoad_Postfix failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void CheckAndSpawnOnArrival()
        {
            try
            {
                if (!ResolveTypes()) return;

                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;

                var currentEnv = Reflect.GetMember(gm, "CurrentEnvironment");
                if (currentEnv == null) return;
                if (Reflect.GetMember(currentEnv, "IsNull") is true) return;

                if (!ResolveRefs()) return;

                var envCard = Reflect.GetMember(currentEnv, "EnvCard");
                if (!ReferenceEquals(envCard, _innInterior)) return;
                if (AlreadySpawned(gm)) return;

                var npc = SpawnAndReturn(gm);
                if (npc == null) return;
                _initMethod.Invoke(npc, new object[] { null, currentEnv });

                // CreateNPC/Init only build the InGameNPC data object (AllNPCs entry) — they never
                // spawn a board card. Vanilla only calls AssignOrCreateNPCCards() from the boot/load
                // coroutine (.decomp/GameManager.cs:2375,2527); this arrival check runs mid-session,
                // long after boot, so nothing else will ever give this NPC an AssociatedCard unless
                // we call it ourselves here.
                _assignOrCreateCardsMethod.Invoke(gm, null);
                Plugin.Logger.LogInfo("[InnKeeperSpawnPatch] Inn Keeper spawned inside the Village Inn.");

                // Force the pantry to stock immediately rather than waiting for the next 30s
                // CheckRestock poll — a player who opens Trade in the first half-minute after
                // first meeting the Keeper would otherwise see an empty popup (StartingInventory's
                // ItemWarpData resolution is unproven for NPCInventoryElement; DropCardsInsideInventory
                // is the confirmed-working mechanism, see reference_npc_inventory_card_creation).
                ForceRestock(gm, npc);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[InnKeeperSpawnPatch] CheckAndSpawnOnArrival failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // Pantry restock, driven entirely from C# rather than the JSON action's own Conditions
        // (Agent_InnKeeper.json's InnKeeperMorningRestock is RepeatOptions:OnlyWhenTriggered —
        // vanilla's own CheckForActions loop forces it permanently not-ready since we never set
        // InGameNPC.CurrentActionRequest; only this method ever fires it). Reasons for owning the
        // cadence here instead of via a second declarative NPCAction + counter stat: (1) no native
        // GeneralCondition supports "every N days" (InGameTimeCondition only compares against a
        // fixed day/hour, no modulo), and (2) two AgentActions evaluated in the same CheckForActions
        // pass previously caused a sibling-action race on this exact NPC system (see
        // reference_npcaction_move_before_drop_race) — a single C#-owned trigger has no sibling to
        // race against.
        private static void CheckRestock()
        {
            try
            {
                if (!ResolveTypes()) return;

                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;
                if (!ResolveRefs()) return;

                var npc = FindLiveNpc(gm);
                if (npc == null) return;
                var associatedCard = Reflect.GetMember(npc, "AssociatedCard");
                if (associatedCard == null) return; // AssignOrCreateNPCCards hasn't finished yet

                int currentDay = Reflect.GetInt(gm, "CurrentDay");
                int stockCount = (Reflect.GetMember(associatedCard, "CardsInInventory") as ICollection)?.Count ?? 0;

                if (_lastRestockDay == int.MinValue)
                {
                    // First check this session — just take a baseline, don't force a restock onto
                    // an already-stocked pantry every time the mod initializes. An empty pantry
                    // (the self-heal case) still falls through and restocks immediately below.
                    _lastRestockDay = currentDay;
                    if (stockCount > 0) return;
                }

                bool due = stockCount == 0 || currentDay - _lastRestockDay >= RestockIntervalDays;
                if (!due) return;

                PerformRestock(gm, npc, associatedCard, currentDay, stockCount);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[InnKeeperSpawnPatch] CheckRestock failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // Called immediately after the Keeper's board card is created (arrival spawn or reload
        // restore) so the pantry is never observed empty just because CheckRestock's own 30s poll
        // hasn't fired yet. Safe to call unconditionally — PerformRestock's own drop mechanism
        // (DropCardsInsideInventory) simply adds more stock on top of whatever StartingInventory
        // already resolved, if anything.
        private static void ForceRestock(object gm, object npc)
        {
            try
            {
                var associatedCard = Reflect.GetMember(npc, "AssociatedCard");
                if (associatedCard == null) return;
                int currentDay = Reflect.GetInt(gm, "CurrentDay");
                int stockCount = (Reflect.GetMember(associatedCard, "CardsInInventory") as ICollection)?.Count ?? 0;
                PerformRestock(gm, npc, associatedCard, currentDay, stockCount);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[InnKeeperSpawnPatch] ForceRestock failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void PerformRestock(object gm, object npc, object associatedCard, int currentDay, int stockCountBefore)
        {
            if (!FireAgentAction(npc, associatedCard, RestockActionId) && !_missingRestockActionWarned)
            {
                _missingRestockActionWarned = true;
                Plugin.Logger.LogWarning($"[InnKeeperSpawnPatch] '{RestockActionId}' not found on Agent_InnKeeper.json AgentActions — restock inactive.");
            }

            string seasonalActionId = SeasonalRestockActionId();
            if (seasonalActionId != null)
                FireAgentAction(npc, associatedCard, seasonalActionId);

            _lastRestockDay = currentDay;

            int newStockCount = (Reflect.GetMember(associatedCard, "CardsInInventory") as ICollection)?.Count ?? 0;
            Plugin.Logger.LogInfo($"[InnKeeperSpawnPatch] Pantry restocked on day {currentDay} (stock {stockCountBefore} -> {newStockCount}).");
        }

        // Seasonal pantry variety (Documentation/Design/Village_InnKeeper_Plan.md § Phase 7): each
        // restock also fires whichever "InnKeeperSeasonalRestock_<Season>" AgentAction matches
        // GameQuery.CurrentSeason, if one is shipped. Returns null (no-op) if the season string is
        // unavailable — the base pantry restock above still runs regardless.
        private static string SeasonalRestockActionId()
        {
            string season = GameQuery.CurrentSeason;
            return string.IsNullOrEmpty(season) ? null : $"InnKeeperSeasonalRestock_{season}";
        }

        // Looks up an AgentAction by ActionID on the Keeper's own NPCAgent and fires it via the
        // same ToAction+PerformAction path CheckRestock/ForceRestock already use. Returns false if
        // the action isn't found (harmless — not every seasonal action needs to exist).
        private static bool FireAgentAction(object npc, object associatedCard, string actionId)
        {
            var agentActions = Reflect.GetMember(_agent, "AgentActions") as Array;
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
