using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using HarmonyLib;
using UnityEngine;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Village Professor — Phase P1 spawn + Phase P2 Foraging Arc scheduler
    /// (Documentation/Design/Village_Professor_Plan.md).
    ///
    /// Spawn/residency (P1, unchanged): structural copy of InnKeeperSpawnPatch.cs, targeting
    /// cmcAcademyInterior. See the P0/P1 write-up in the plan for the instanced-env citation
    /// trail — already owner-verified in-game.
    ///
    /// Foraging Arc (P2, added 2026-07-15): a single TickEvents.Interval poll owns ALL NPC
    /// movement and ALL persisted/gating state via GameManager.MoveNPC (a plain synchronous
    /// method, never a coroutine) and a set of custom NPCStats. Actual NEW-CARD CREATION
    /// (forage drops, specialty-item rolls) is delegated to native JSON AgentActions on
    /// Agent_Professor.json (DroppedCards + DropCardsInsideInventory) — the same mechanism
    /// Agent_TraderGeneral.json already ships — because there is no safe way to reflectively
    /// invoke the coroutine-chained GameManager.AddCard machinery from mod C# (see the P2
    /// correction note in the plan's §3c). This C# scheduler therefore only ever: moves the
    /// NPC (outdoor wander + Academy/Inn homing) and flips NPCStat values that the JSON
    /// AgentActions gate on (RequiredEnvironment + RequiredNPCStatValues) — it never creates a
    /// card. Because C# is the only caller of MoveNPC for this NPC (Agent_Professor.json never
    /// sets MoveToEnvironment on any AgentAction), the two systems can never race over the same
    /// move.
    ///
    /// Portrait tracking (added 2026-07-16): SyncPortrait keeps his card art and dialog/trade
    /// portrait matched to his current location (Academy / outdoors / Inn) — see its doc.
    /// </summary>
    internal static class ProfessorSchedulePatch
    {
        private const string ProfessorAgentUid = "cmcProfessorAgent";
        private const string AcademyInteriorUid = "cmcAcademyInterior";
        private const string InnInteriorUid = "cmcInnInterior";

        // Academy and Inn both sit on the Village tile (ConditionalDrops in WorldMap/MapNodes.json)
        // — their shared "front door" for outdoor tile-walking purposes.
        private const string VillageEnvUid = "cmcEnvVillage";
        private const int TicksPerTile = NpcTileWalker.DefaultTicksPerTile;

        private static readonly string[] OutdoorNodeUids =
        {
            "cmcEnvVillagePath", "cmcEnvVillageFarm", "cmcEnvVillage", "cmcEnvForagingForest",
        };

        // Forage AgentAction ID per outdoor node (parallel to OutdoorNodeUids). These actions
        // are RepeatOptions:OnlyWhenTriggered in Agent_Professor.json so native CheckForActions
        // never fires them — the scheduler fires them from C# (FireAgentAction) with the
        // Professor's own card as the receiving container, the only path proven to route
        // DropCardsInsideInventory into an NPC's satchel rather than onto the ground (mirrors
        // InnKeeperSpawnPatch; see memory reference_npc_inventory_card_creation).
        private static readonly string[] ForageActionIds =
        {
            "ProfessorForage_VillagePath", "ProfessorForage_VillageFarm",
            "ProfessorForage_Village", "ProfessorForage_ForagingForest",
        };

        private const string PhaseStatUid = "cmcStatProfessorPhase";
        private const float PhaseForaging = 0f;
        private const float PhaseResident = 1f;

        // Narrative/enhancement stats (all declared in NPCStat/*.json and listed in
        // Agent_Professor.json AgentStats; dialog gates on them via explicit-agent
        // RequiredNPCStatValues — see the enhancement-pack note in the plan doc).
        private const string LastNodeStatUid = "cmcStatProfLastNode";          // 1..4 = OutdoorNodeUids index + 1
        private const string GradCountStatUid = "cmcStatProfGradCount";        // 0..6 graduated courses
        private const string BridgeNewsStatUid = "cmcStatProfBridgeNews";      // 0 unbuilt, 1 built+unheard, 2 heard
        private const string SpecimenReadyStatUid = "cmcStatProfSpecimenReady";    // 1 = commission offerable
        private const string SpecimenNextDayStatUid = "cmcStatProfSpecimenNextDay"; // day it re-arms (0 = no timer)
        private const string RiverClearingEnvUid = "3c7144d78a55ebb45b0fdf1598cf6a04"; // vanilla River Clearing CT8
        private const string RiverBridgeImpUid = "cmcimpriverbridge";
        private const int SpecimenIntervalDays = 7;

        // Phase P4 — event-triggered tasks (Village_Master_Plan.md §10.2 Phase P4). Each task is
        // a one-shot commission BP; the day-gated one needs its day-count check bridged into an
        // NPCStat the same way BridgeNews bridges CardUtil.IsImprovementBuilt. The subject-gated
        // task reuses the existing cmcStatProfEligibleHerbalism eligibility stat directly — no new
        // bridge needed since course graduation is already mirrored there.
        private const string FieldSamplesReadyStatUid = "cmcStatProfFieldSamplesReady"; // one-shot day latch
        private const int FieldSamplesUnlockDay = 10;

        private const int ThresholdCount = 10; // satchel full -> head to the Academy
        private const int WatermarkCount = 2;  // stock depleted -> head back out foraging
        private const int WanderIntervalTicks = 24; // ~a quarter day between outdoor moves
        private const int SpecialtyCap = 2;    // at most this many specialty items held at once

        // Portrait tracking — the Professor's card art (and dialog/trade portrait, which the
        // game reads live off NPCModel.AgentImage) follows where the scheduler moved him.
        private const int PortraitAcademy = 1;
        private const int PortraitOutdoors = 2;
        private const int PortraitInn = 3;
        private const string AcademySpriteName = "Professor_Indoors_Academy";
        private const string OutdoorsSpriteName = "Professor_Outdoors";
        private const string InnSpriteName = "Professor_Indoors_Inn";

        private sealed class CourseSpec
        {
            public string PerkUid;
            public string StatUid;
            public HashSet<string> PoolUids;
        }

        // Mirrors Documentation/Design/Village_Professor_Plan.md §3d's curated item table.
        // Keep in sync with the matching AgentActions block in Agent_Professor.json — this
        // list is used only to COUNT currently-held specialty items for the cap check; the
        // actual drop tables live in JSON (cross-mod soft-dependency degrades there, silently,
        // via normal WarpData resolution — nothing to duplicate here).
        private static readonly CourseSpec[] Courses =
        {
            new CourseSpec
            {
                PerkUid = "cmcperkgradmetallurgy", StatUid = "cmcStatProfEligibleMetallurgy",
                PoolUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "advanced_copper_tools_copper_tea_kettle", "advanced_copper_tools_metal_lantern",
                    "advanced_copper_tools_copper_oil_flask", "advanced_copper_tools_copper_watering_can",
                    "advanced_copper_tools_copper_cauldron", "act_tin_solder",
                    "water_sawmill_copper_gear_small", "water_sawmill_copper_rivet",
                }
            },
            new CourseSpec
            {
                PerkUid = "cmcperkgradherbalism", StatUid = "cmcStatProfEligibleHerbalism",
                PoolUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "555bacff1fadd9a418c7324a8fdc568d", "herbs_fungi_herb_pipe",
                    "herbs_fungi_drying_tray", "herbs_fungi_dried_reishi",
                    "herbs_fungi_flower_garland", "advanced_copper_tools_focus_tea",
                    "potterysculptureherbpaste",
                }
            },
            new CourseSpec
            {
                PerkUid = "cmcperkgradmedicine", StatUid = "cmcStatProfEligibleMedicine",
                PoolUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "herbs_fungi_plantain_poultice", "herbs_fungi_dried_ginseng", "sh_lanolin_salve",
                    "advanced_copper_tools_calming_tea", "advanced_copper_tools_warming_tea",
                    "83f75985c19c9ba46a282072a84a05a4",
                }
            },
            new CourseSpec
            {
                PerkUid = "cmcperkgradfishing", StatUid = "cmcStatProfEligibleFishing",
                PoolUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "cmcfishingnet", "CMCIronFishingRodFittings", "CMCIronFishingRod",
                    "e3a3984267c59214cbf0ab931a9e7f23",
                }
            },
            new CourseSpec
            {
                PerkUid = "cmcperkgradarchitecture", StatUid = "cmcStatProfEligibleArchitecture",
                PoolUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "advanced_copper_tools_wheelbarrow", "advanced_copper_tools_large_saw",
                    "advanced_copper_tools_copper_nails", "act_iron_nail",
                    "potterysculpturestonetiles", "water_sawmill_iron_wrench",
                }
            },
            new CourseSpec
            {
                PerkUid = "cmcperkgradarmorer", StatUid = "cmcStatProfEligibleArmorer",
                PoolUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "combatandarmorquiltedcap", "combatandarmorquiltedvest",
                    "combatandarmorbonelamellar", "combatandarmorwoodenshield",
                    "advanced_copper_tools_copper_helmet", "advanced_copper_tools_copper_gauntlets",
                    "advanced_copper_tools_wearable_metal_pan",
                }
            },
        };

        // Specialty-stock AgentAction ID per course (parallel to Courses). Fired from C# for the
        // same inventory-routing reason as ForageActionIds — keep in sync with Courses order.
        private static readonly string[] SpecialtyActionIds =
        {
            "ProfessorSpecialty_Metallurgy", "ProfessorSpecialty_Herbalism",
            "ProfessorSpecialty_Medicine", "ProfessorSpecialty_Fishing",
            "ProfessorSpecialty_Architecture", "ProfessorSpecialty_Armorer",
        };

        private static bool _initialized;

        // His Copper Chest config, resolved once from CopperChestPatch (the single owner of every
        // chest's UIDs, action IDs and caps). Its accrual cadence is a persistent GameStat, not one
        // of this file's in-memory tick throttles.
        private static CopperChestPatch.ChestConfig _chestConfig;

        // Resolved once game data is loaded.
        private static object _agent;           // NPCAgent
        private static object _academyInterior;  // CardData
        private static object _innInterior;      // CardData
        private static object[] _outdoorNodes;   // CardData[4]
        private static object _phaseStat;        // NPCStat
        private static readonly Dictionary<string, object> _npcStatCache = new(StringComparer.OrdinalIgnoreCase);

        // Captured once per session (instanced envs — see the P0 citation trail).
        private static object _academyEnvId;
        private static object _innEnvId;
        private static bool _academyCaptured;
        private static bool _innCaptured;

        // Scheduler bookkeeping (not persisted — cheap to recompute, no save-format risk).
        private static int _lastWanderTotalTick = int.MinValue;
        private static string _wanderTargetUid; // outdoor uid he's currently walking to, or null when idle
        private static int _lastForageTotalTick = int.MinValue; // throttles C#-fired forage
        private static int _lastSpecialtyDay = int.MinValue;     // throttles specialty stock to 1/day
        private static int _lastLoggedHeld = -1;                 // diagnostic: satchel-count change tracker
        private static bool _fireActionUnavailableWarned;
        private static int _lastPortrait; // 0 = not yet synced this run

        // Commission diagnostics (temporary — owner reported "commissions only work at the Inn";
        // logs on change so a single play session captures the state transition without spam).
        private static string _lastLoggedCommissionEnv;
        private static bool? _lastLoggedHasVisibleCommissions;

        // Commission popup diagnostics (temporary — owner reported 2026-08-05: the Commissions
        // popup shows a "?" placeholder slot and a duplicated "Specimen Request" label/switch
        // button when the Professor has 2+ commissions simultaneously ready). Fired once per
        // CommissionDiag log line (see LogCommissionDiagnostics) to dump each commission's
        // resolved BlueprintModelState + CommissionIsVisible(i) — the two gates that decide
        // whether BlueprintConstructionPopup.SetupActions shows a switch-blueprint button for it.
        private static MethodInfo _commissionIsVisibleMethod; // InGameNPC.CommissionIsVisible(int)

        // Once-true caches (graduations and the bridge cannot revert mid-run). Reset per run
        // in RestoreOnLoad_Postfix so a different save loaded in the same session cannot
        // inherit stale positives.
        private static readonly bool[] _hasCourseCache = new bool[Courses.Length];
        private static bool _bridgeSeenBuilt;

        // Reflection handles, resolved once.
        private static Type _gmType;
        private static Type _npcAgentType;
        private static Type _envIdType;
        private static Type _inGameNpcType;
        private static Type _npcStatType;
        private static Type _cardDataType;
        private static Type _saveDataByRefType;     // NPCAgentSaveDataByReference (Init's 1st param)
        private static Type _gameSaveDataByRefType; // GameSaveDataByReference (declares GetNPCData; type of GameManager.CurrentSaveData)
        private static Type _savedNpcCharacterDataType; // SavedNPCCharacterData (CreateNPC's 2nd param, EA 0.66+)
        private static MethodInfo _getFromIdMethod; // UniqueIDScriptable.GetFromID(string)
        private static MethodInfo _createNpcMethod;  // GameManager.CreateNPC(NPCAgent, SavedNPCCharacterData, bool) — EA 0.66+ (was (NPCAgent) only pre-0.66)
        private static MethodInfo _initMethod;       // InGameNPC.Init(NPCAgentSaveDataByReference, EnvID)
        private static MethodInfo _getNpcDataMethod; // GameSaveDataByReference.GetNPCData(NPCAgent) -> NPCAgentSaveDataByReference
        private static MethodInfo _assignOrCreateCardsMethod; // GameManager.AssignOrCreateNPCCards() (private, no args)
        private static MethodInfo _moveNpcMethod;    // GameManager.MoveNPC(InGameNPC, EnvID)
        private static MethodInfo _getStatValueMethod; // InGameNPC.GetStatValue(NPCStat)
        private static MethodInfo _setStatValueMethod; // InGameNPCStat.SetStatValue(float) — resolved lazily (per-instance type)
        private static MethodInfo _setCardImageMethod; // CardData.SetCardImage(Sprite)
        private static MethodInfo _refreshVisualsMethod; // CardGraphics.RefreshCookingStatus() — resolved lazily (per-instance type)
        private static bool _refreshVisualsFailureLogged;
        private static ConstructorInfo _envIdFromCardCtor; // EnvID(CardData, bool)
        private static object _envIdEmpty;           // default(EnvID)

        // Forage/specialty firing (mirrors InnKeeperSpawnPatch.FireAgentAction) — the proven
        // path that lands DropCardsInsideInventory drops in the NPC's own satchel.
        private static Type _npcActionType;          // NPCAction (declares ToAction)
        private static Type _inGameCardBaseType;     // InGameCardBase
        private static Type _inGameNpcOrPlayerType;  // InGameNPCOrPlayer (struct)
        private static MethodInfo _toActionMethod;      // NPCAction.ToAction(InGameNPC, InGameCardBase) -> CardAction
        private static MethodInfo _performActionMethod; // GameManager.PerformAction(CardAction, InGameCardBase, bool, InGameNPCOrPlayer) (static)
        private static ConstructorInfo _inGameNpcOrPlayerCtor; // InGameNPCOrPlayer(InGameNPC)

        public static void Initialize(Harmony harmony)
        {
            if (_initialized) return;
            _initialized = true;

            if (!ResolveTypes())
            {
                Plugin.Logger.LogWarning("[ProfessorSchedulePatch] Required game types not found — Professor spawn inactive.");
                return;
            }

            var initStatsMethod = AccessTools.Method(_gmType, "InitializeStatsAndActions");
            if (initStatsMethod == null)
            {
                Plugin.Logger.LogWarning("[ProfessorSchedulePatch] GameManager.InitializeStatsAndActions not found — Professor reload restore inactive.");
            }
            else
            {
                harmony.Patch(initStatsMethod, postfix: new HarmonyMethod(typeof(ProfessorSchedulePatch), nameof(RestoreOnLoad_Postfix)));
            }

            TickEvents.Interval(1f, CheckAndSpawnOnArrival, "ProfessorArrivalCheck");
            TickEvents.Interval(1f, RunScheduler, "ProfessorSchedule");

            Plugin.Logger.LogDebug("[ProfessorSchedulePatch] initialized.");
        }

        private static bool ResolveTypes()
        {
            if (_gmType != null) return true;

            _gmType = CardUtil.FindGameType("GameManager");
            _npcAgentType = CardUtil.FindGameType("NPCAgent");
            _envIdType = CardUtil.FindGameType("EnvID");
            _inGameNpcType = CardUtil.FindGameType("InGameNPC");
            _npcStatType = CardUtil.FindGameType("NPCStat");
            _cardDataType = CardUtil.FindGameType("CardData");
            _saveDataByRefType = CardUtil.FindGameType("NPCAgentSaveDataByReference");
            _gameSaveDataByRefType = CardUtil.FindGameType("GameSaveDataByReference");
            _savedNpcCharacterDataType = CardUtil.FindGameType("SavedNPCCharacterData");
            var uidType = CardUtil.FindGameType("UniqueIDScriptable");

            if (_gmType == null || _npcAgentType == null || _envIdType == null || _inGameNpcType == null
                || _npcStatType == null || _cardDataType == null
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
            _moveNpcMethod = AccessTools.Method(_gmType, "MoveNPC", new[] { _inGameNpcType, _envIdType });
            _getStatValueMethod = AccessTools.Method(_inGameNpcType, "GetStatValue", new[] { _npcStatType });
            // Optional — portrait tracking degrades gracefully if absent (art just stays static).
            _setCardImageMethod = AccessTools.Method(_cardDataType, "SetCardImage", new[] { typeof(Sprite) });
            // Optional — commission-popup diagnostics degrade gracefully (logged once) if absent.
            _commissionIsVisibleMethod = AccessTools.Method(_inGameNpcType, "CommissionIsVisible", new[] { typeof(int) });
            _envIdFromCardCtor = _envIdType.GetConstructor(new[] { _cardDataType, typeof(bool) });
            _envIdEmpty = Activator.CreateInstance(_envIdType);

            // Optional — forage/specialty stock degrades gracefully (logged once) if absent.
            _npcActionType = CardUtil.FindGameType("NPCAction");
            _inGameCardBaseType = CardUtil.FindGameType("InGameCardBase");
            _inGameNpcOrPlayerType = CardUtil.FindGameType("InGameNPCOrPlayer");
            if (_npcActionType != null && _inGameCardBaseType != null && _inGameNpcOrPlayerType != null)
            {
                _toActionMethod = AccessTools.Method(_npcActionType, "ToAction", new[] { _inGameNpcType, _inGameCardBaseType });
                _performActionMethod = _gmType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "PerformAction" && m.GetParameters().Length == 4);
                _inGameNpcOrPlayerCtor = _inGameNpcOrPlayerType.GetConstructor(new[] { _inGameNpcType });
            }

            return _getFromIdMethod != null && _createNpcMethod != null && _initMethod != null
                && _getNpcDataMethod != null && _assignOrCreateCardsMethod != null
                && _moveNpcMethod != null && _getStatValueMethod != null && _envIdFromCardCtor != null;
        }

        private static bool _refsUnresolvedWarned;

        private static bool ResolveRefs()
        {
            if (_agent != null && _academyInterior != null && _innInterior != null
                && _outdoorNodes != null && _phaseStat != null) return true;

            _agent ??= _getFromIdMethod.Invoke(null, new object[] { ProfessorAgentUid });
            _academyInterior ??= CardUtil.GetCardDataById(AcademyInteriorUid);
            _innInterior ??= CardUtil.GetCardDataById(InnInteriorUid);
            _phaseStat ??= _getFromIdMethod.Invoke(null, new object[] { PhaseStatUid });

            if (_outdoorNodes == null)
            {
                var nodes = new object[OutdoorNodeUids.Length];
                bool allResolved = true;
                for (int i = 0; i < OutdoorNodeUids.Length; i++)
                {
                    nodes[i] = CardUtil.GetCardDataById(OutdoorNodeUids[i]);
                    if (nodes[i] == null) allResolved = false;
                }
                if (allResolved) _outdoorNodes = nodes;
            }

            bool resolved = _agent != null && _academyInterior != null && _innInterior != null
                && _outdoorNodes != null && _phaseStat != null;

            if (!resolved && !_refsUnresolvedWarned)
            {
                _refsUnresolvedWarned = true;
                Plugin.Logger.LogWarning($"[ProfessorSchedulePatch] ResolveRefs failed (agent null={_agent == null}, academyInterior null={_academyInterior == null}, innInterior null={_innInterior == null}, outdoorNodes null={_outdoorNodes == null}, phaseStat null={_phaseStat == null}) — the Professor can never spawn/schedule until this succeeds.");
            }

            return resolved;
        }

        private static object ResolveNpcStat(string uid)
        {
            if (_npcStatCache.TryGetValue(uid, out var cached)) return cached;
            var stat = _getFromIdMethod.Invoke(null, new object[] { uid });
            if (stat != null) _npcStatCache[uid] = stat;
            return stat;
        }

        // ── Spawn / residency (P1, unchanged behavior) ───────────────────────────

        private static bool AlreadySpawned(object gm)
        {
            if (Reflect.GetMember(gm, "AllNPCs") is not IEnumerable allNpcs) return false;
            foreach (var npc in allNpcs)
            {
                if (npc == null) continue;
                if (ReferenceEquals(Reflect.GetMember(npc, "NPCModel"), _agent)) return true;
            }
            return false;
        }

        private static object FindProfessorNpc(object gm)
        {
            if (Reflect.GetMember(gm, "AllNPCs") is not IEnumerable allNpcs) return null;
            foreach (var npc in allNpcs)
            {
                if (npc == null) continue;
                if (ReferenceEquals(Reflect.GetMember(npc, "NPCModel"), _agent)) return npc;
            }
            return null;
        }

        // Invokes the private GameManager.CreateNPC(NPCAgent, SavedNPCCharacterData, bool) (idempotent —
        // no-ops if AllNPCs already holds this agent) and returns the InGameNPC it just added. The 2nd/3rd
        // params are unused inside CreateNPC's own body (confirmed via decompile) — null/false match the
        // vanilla LoadNPC/boot-coroutine precedent for a first-time spawn with no saved character data.
        private static object SpawnAndReturn(object gm)
        {
            _createNpcMethod.Invoke(gm, new object[] { _agent, null, false });
            if (Reflect.GetMember(gm, "AllNPCs") is not IEnumerable allNpcs)
            {
                Plugin.Logger.LogWarning("[ProfessorSchedulePatch] SpawnAndReturn: CreateNPC invoked but GameManager.AllNPCs did not resolve — spawn cannot be confirmed.");
                return null;
            }
            object last = null;
            foreach (var npc in allNpcs)
                if (ReferenceEquals(Reflect.GetMember(npc, "NPCModel"), _agent))
                    last = npc;
            if (last == null)
                Plugin.Logger.LogWarning("[ProfessorSchedulePatch] SpawnAndReturn: CreateNPC succeeded but the post-spawn AllNPCs identity lookup found no matching NPCModel — Professor spawn effectively failed silently.");
            return last;
        }

        private static void RestoreOnLoad_Postfix(object __instance)
        {
            try
            {
                // New run booting — drop the once-true caches so a different save loaded in
                // the same session re-derives them from ITS own state.
                Array.Clear(_hasCourseCache, 0, _hasCourseCache.Length);
                _bridgeSeenBuilt = false;
                _lastPortrait = 0;
                _lastForageTotalTick = int.MinValue;
                _lastSpecialtyDay = int.MinValue;
                _lastLoggedHeld = -1;
                _wanderTargetUid = null;
                NpcTileWalker.ResetAll();

                if (!ResolveRefs()) return;
                if (AlreadySpawned(__instance)) return;

                var saveData = Reflect.GetMember(__instance, "CurrentSaveData");
                if (saveData == null) return;
                var npcSaveData = _getNpcDataMethod.Invoke(saveData, new object[] { _agent });
                if (npcSaveData == null) return; // never placed yet — the arrival check handles first placement

                var npc = SpawnAndReturn(__instance);
                if (npc == null) return;
                _initMethod.Invoke(npc, new object[] { npcSaveData, _envIdEmpty });
                Plugin.Logger.LogInfo("[ProfessorSchedulePatch] Professor restored from save.");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[ProfessorSchedulePatch] RestoreOnLoad_Postfix failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
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
                if (!ReferenceEquals(envCard, _academyInterior)) return;
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
                Plugin.Logger.LogInfo("[ProfessorSchedulePatch] Professor spawned inside the Village Academy.");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[ProfessorSchedulePatch] CheckAndSpawnOnArrival failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // ── Foraging Arc scheduler (P2) ──────────────────────────────────────────

        private static void RunScheduler()
        {
            try
            {
                if (!ResolveTypes() || !ResolveRefs()) return;

                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;

                CaptureEnvIds(gm);

                var npc = FindProfessorNpc(gm);
                if (npc == null) return; // not spawned yet this session

                var associatedCard = Reflect.GetMember(npc, "AssociatedCard");
                if (associatedCard == null) return;

                // Copper Chest accrual (Village_Master_Plan.md §10.8.3.3) — a genuinely NEW weekly
                // tick. His ForageActionIds mechanism is per-node forage into his SATCHEL and is a
                // different concern entirely; this neither replaces nor reads it, and it never
                // touches cmcAcademyLectern's tuition account (AcademyPatch owns that). Placed
                // ahead of the PR-1 phase gate below on purpose: the chest is an economic system
                // that fills from the moment he exists, like every other resident's, rather than a
                // schedule behaviour the phase gate is meant to hold back.
                //
                // CopperChestPatch.TryAccrue owns the env gate (no-ops unless the player is
                // standing in cmcAcademyInterior), the persistent due-day stamp and both caps. This
                // is the single caller for the Professor's chest — a second one would be R6's
                // double-drop.
                _chestConfig ??= CopperChestPatch.ForAgent(ProfessorAgentUid);
                if (_chestConfig != null)
                    CopperChestPatch.TryAccrue(_chestConfig, npc, GameQuery.CurrentDay, FireAgentAction);

                // PR-1 village phase gate (Event Timeline plan §2): before the Inn Keeper's intro
                // the Professor talks only to redirect — no wander, no forage, no specialty stock,
                // no specimen re-arm. He stays at the Academy (where he spawns) with indoor art.
                if (!VillageClock.PhaseAtLeast(1f)) return;

                float phase = GetNpcStatValue(npc, _phaseStat);
                int heldCount = Inventory.Cards(associatedCard).Count;
                bool isNight = IsNightHour(GameQuery.HourOfDay);

                if (heldCount != _lastLoggedHeld)
                {
                    _lastLoggedHeld = heldCount;
                    Plugin.Logger.LogDebug($"[ProfessorSchedulePatch] satchel={heldCount}, phase={(phase < 0.5f ? "Foraging" : "Resident")}, withPlayer={SharesPlayerEnv(npc)}.");
                }

                SyncCourseEligibility(npc, associatedCard, phase);
                SyncNarrativeStats(npc);
                LogCommissionDiagnostics(gm, npc, associatedCard);

                if (phase < 0.5f)
                    RunForagingPhase(gm, npc, associatedCard, heldCount, isNight);
                else
                    RunResidentPhase(gm, npc, associatedCard, heldCount, isNight);

                // After movement so a move this tick shows its art this tick (MoveNPC is
                // synchronous — CurrentEnvironment is already the destination here).
                SyncPortrait(npc);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[ProfessorSchedulePatch] RunScheduler failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void CaptureEnvIds(object gm)
        {
            if (_academyCaptured && _innCaptured) return;

            var currentEnv = Reflect.GetMember(gm, "CurrentEnvironment");
            if (currentEnv == null) return;
            if (Reflect.GetMember(currentEnv, "IsNull") is true) return;
            var envCard = Reflect.GetMember(currentEnv, "EnvCard");

            if (!_academyCaptured && ReferenceEquals(envCard, _academyInterior))
            {
                _academyEnvId = currentEnv;
                _academyCaptured = true;
                Plugin.Logger.LogInfo("[ProfessorSchedulePatch] Captured Academy EnvID for the Foraging Arc scheduler.");
            }
            else if (!_innCaptured && ReferenceEquals(envCard, _innInterior))
            {
                _innEnvId = currentEnv;
                _innCaptured = true;
                Plugin.Logger.LogInfo("[ProfessorSchedulePatch] Captured Inn EnvID for the Foraging Arc scheduler.");
            }
        }

        private static void RunForagingPhase(object gm, object npc, object associatedCard, int heldCount, bool isNight)
        {
            bool withPlayer = SharesPlayerEnv(npc);

            if (heldCount >= ThresholdCount && _academyCaptured)
            {
                SetNpcStatValue(npc, _phaseStat, PhaseResident);
                Plugin.Logger.LogInfo("[ProfessorSchedulePatch] Professor's satchel is full — heading to the Academy.");
                // Don't teleport him out from under the player mid-visit — he'll head over the
                // next tick the player isn't standing with him.
                if (!withPlayer)
                {
                    _wanderTargetUid = null; // abandon any wander-in-progress — he's committed to the Academy/Inn now
                    WalkTowards(gm, npc, VillageEnvUid, isNight && _innCaptured ? _innEnvId : _academyEnvId);
                }
                return;
            }

            // While the player is with him, stay put (no wandering off, no portrait flip) and
            // forage in place at whatever node he's on.
            if (withPlayer)
            {
                FireForageThrottled(npc, associatedCard, CurrentOutdoorNodeIndex(npc));
                return;
            }

            if (isNight)
            {
                _wanderTargetUid = null; // abandon any wander-in-progress — he's heading in for the night
                if (_innCaptured) WalkTowards(gm, npc, VillageEnvUid, _innEnvId);
                return;
            }

            // Already walking toward a previously-picked wander target — keep stepping toward it
            // (one outdoor tile at a time) and forage only once he's actually arrived.
            if (_wanderTargetUid != null)
            {
                var targetCard = CardUtil.GetCardDataById(_wanderTargetUid);
                WalkTowards(gm, npc, _wanderTargetUid, BuildOutdoorEnvId(targetCard));

                int arrivedIndex = CurrentOutdoorNodeIndex(npc);
                if (arrivedIndex >= 0 && string.Equals(OutdoorNodeUids[arrivedIndex], _wanderTargetUid, StringComparison.OrdinalIgnoreCase))
                {
                    _wanderTargetUid = null;

                    // "Where were you today?" dialog flavor — 1-based index into OutdoorNodeUids
                    // (1=VillagePath, 2=VillageFarm, 3=Village, 4=ForagingForest).
                    SetNpcStatValue(npc, ResolveNpcStat(LastNodeStatUid), arrivedIndex + 1);

                    // Forage the node he just arrived at — fired from C# (not native CheckForActions)
                    // so DropCardsInsideInventory lands in his own satchel, not on the ground.
                    FireForageThrottled(npc, associatedCard, arrivedIndex);
                }
                return;
            }

            int totalTick = CurrentTotalTick();
            if (_lastWanderTotalTick != int.MinValue && totalTick - _lastWanderTotalTick < WanderIntervalTicks)
                return;
            _lastWanderTotalTick = totalTick;

            int nodeIndex = UnityEngine.Random.Range(0, _outdoorNodes.Length);
            _wanderTargetUid = OutdoorNodeUids[nodeIndex];
        }

        private static void RunResidentPhase(object gm, object npc, object associatedCard, int heldCount, bool isNight)
        {
            bool withPlayer = SharesPlayerEnv(npc);

            if (isNight)
            {
                if (_innCaptured && !withPlayer) WalkTowards(gm, npc, VillageEnvUid, _innEnvId);
                return;
            }

            if (_academyCaptured && !withPlayer) WalkTowards(gm, npc, VillageEnvUid, _academyEnvId);

            // Rarer specialty stock for graduated courses, once per day, only at the Academy.
            if (IsAtAcademy(npc)) FireSpecialtyStock(npc, associatedCard);

            if (heldCount <= WatermarkCount)
            {
                SetNpcStatValue(npc, _phaseStat, PhaseForaging);
                // Backdate so the very next Foraging-phase tick wanders immediately instead of
                // waiting out a full WanderIntervalTicks window standing at the Academy.
                _lastWanderTotalTick = CurrentTotalTick() - WanderIntervalTicks;
                Plugin.Logger.LogInfo("[ProfessorSchedulePatch] Professor's stock is low — heading back out to forage.");
            }
        }

        // Graduations cannot revert mid-run — cache each course's HasCourse once true.
        private static bool HasCourseCached(int courseIndex)
        {
            if (!_hasCourseCache[courseIndex] && AcademyCourseService.HasCourse(Courses[courseIndex].PerkUid))
                _hasCourseCache[courseIndex] = true;
            return _hasCourseCache[courseIndex];
        }

        private static void SyncCourseEligibility(object npc, object associatedCard, float phase)
        {
            bool resident = phase >= 0.5f;
            List<object> heldCards = null;

            for (int i = 0; i < Courses.Length; i++)
            {
                var course = Courses[i];
                bool eligible = false;
                if (resident && HasCourseCached(i))
                {
                    heldCards ??= Inventory.Cards(associatedCard);
                    int held = 0;
                    foreach (var card in heldCards)
                    {
                        var uid = CardUtil.GetCardUniqueId(card);
                        if (uid != null && course.PoolUids.Contains(uid)) held++;
                    }
                    eligible = held < SpecialtyCap;
                }

                var stat = ResolveNpcStat(course.StatUid);
                if (stat == null) continue;

                float target = eligible ? 1f : 0f;
                if (GetNpcStatValue(npc, stat) != target)
                    SetNpcStatValue(npc, stat, target);
            }
        }

        /// <summary>
        /// Keeps the narrative/enhancement NPCStats current: graduated-course count (gates
        /// studies dialog + the two milestone gifts), the river-bridge news flag, and the
        /// weekly re-arm of the specimen commission. All values live in the Professor's own
        /// NPC stats, so they persist through the game's native NPC-stat save path.
        /// </summary>
        private static void SyncNarrativeStats(object npc)
        {
            // Graduated-course count (0..6).
            int grads = 0;
            for (int i = 0; i < Courses.Length; i++)
                if (HasCourseCached(i)) grads++;
            var gradStat = ResolveNpcStat(GradCountStatUid);
            if (gradStat != null && GetNpcStatValue(npc, gradStat) != grads)
                SetNpcStatValue(npc, gradStat, grads);

            // River-bridge news: 0 = not built, 1 = built + unheard, 2 = player heard the
            // line (written by the dialog line's own NPCStatModifications — never by C#).
            var bridgeStat = ResolveNpcStat(BridgeNewsStatUid);
            if (bridgeStat != null && GetNpcStatValue(npc, bridgeStat) < 0.5f)
            {
                if (!_bridgeSeenBuilt)
                    _bridgeSeenBuilt = CardUtil.IsImprovementBuilt(RiverClearingEnvUid, RiverBridgeImpUid);
                if (_bridgeSeenBuilt)
                    SetNpcStatValue(npc, bridgeStat, 1f);
            }

            // Field Samples task (P4): one-shot latch, never reset, once day 10 arrives. The
            // completed/claimed guard lives entirely in the blueprint's own NPCStatModifications
            // (cmcStatProfFieldSamplesClaimed) — this scheduler only ever flips Ready 0 -> 1.
            var fieldSamplesReadyStat = ResolveNpcStat(FieldSamplesReadyStatUid);
            if (fieldSamplesReadyStat != null && GetNpcStatValue(npc, fieldSamplesReadyStat) < 0.5f
                && GameQuery.CurrentDay >= FieldSamplesUnlockDay)
            {
                SetNpcStatValue(npc, fieldSamplesReadyStat, 1f);
            }

            // Weekly specimen-commission re-arm: completion (BlueprintNPCStatModifications)
            // drops Ready 1 -> 0; the first tick after that starts a NextDay timer; when the
            // day arrives the commission re-arms and the timer clears.
            var readyStat = ResolveNpcStat(SpecimenReadyStatUid);
            var nextDayStat = ResolveNpcStat(SpecimenNextDayStatUid);
            if (readyStat == null || nextDayStat == null) return;
            if (GetNpcStatValue(npc, readyStat) >= 0.5f) return;

            float nextDay = GetNpcStatValue(npc, nextDayStat);
            int today = GameQuery.CurrentDay;
            if (nextDay < 0.5f)
            {
                SetNpcStatValue(npc, nextDayStat, today + SpecimenIntervalDays);
            }
            else if (today >= nextDay)
            {
                SetNpcStatValue(npc, readyStat, 1f);
                SetNpcStatValue(npc, nextDayStat, 0f);
            }
        }

        // Human-readable env label for diagnostics: the EnvCard's UniqueID, or a fallback token
        // for null/empty/unresolved EnvIDs.
        private static string EnvLabel(object envId)
        {
            if (envId == null) return "null";
            if (Reflect.GetMember(envId, "IsNull") is true) return "IsNull";
            var envCard = Reflect.GetMember(envId, "EnvCard");
            if (envCard == null) return "EnvCard=null";
            return CardUtil.GetCardUniqueId(envCard) ?? (Reflect.GetMember(envCard, "name") as string) ?? "?";
        }

        /// <summary>
        /// Temporary diagnostic (owner report 2026-07-30: "the Professor appears to only accept
        /// commissions in the Inn"). Logs, on change only, the NPC's own tracked environment vs.
        /// where his visible board card AND his separate invisible CommissionsAssociatedCard
        /// actually sit (these are two distinct native cards — see InGameNPC.CommissionsAssociatedCard
        /// / GameManager.MoveNPC — and only vanilla's own MoveNPC keeps them in sync when he
        /// walks), plus HasVisibleCommissions and the raw stat values every commission BP's
        /// BlueprintNPCStatConditions reads. Demote to LogDebug once the divergence (if any) is
        /// confirmed and fixed.
        /// </summary>
        private static void LogCommissionDiagnostics(object gm, object npc, object associatedCard)
        {
            string envLabel = EnvLabel(Reflect.GetMember(npc, "CurrentEnvironment"));
            bool hasVisible = Reflect.GetMember(npc, "HasVisibleCommissions") is true;

            if (envLabel == _lastLoggedCommissionEnv && hasVisible == _lastLoggedHasVisibleCommissions) return;
            _lastLoggedCommissionEnv = envLabel;
            _lastLoggedHasVisibleCommissions = hasVisible;

            var commissionsCard = Reflect.GetMember(npc, "CommissionsAssociatedCard");
            string commissionsCardEnv = commissionsCard != null ? EnvLabel(Reflect.GetMember(commissionsCard, "CardEnvironment")) : "null";
            string associatedCardEnv = EnvLabel(Reflect.GetMember(associatedCard, "CardEnvironment"));

            float phaseVal = GetNpcStatValue(npc, _phaseStat);
            float specimenReady = GetNpcStatValue(npc, ResolveNpcStat(SpecimenReadyStatUid));
            float fieldReady = GetNpcStatValue(npc, ResolveNpcStat(FieldSamplesReadyStatUid));
            float fieldClaimed = GetNpcStatValue(npc, ResolveNpcStat("cmcStatProfFieldSamplesClaimed"));
            float herbEligible = GetNpcStatValue(npc, ResolveNpcStat("cmcStatProfEligibleHerbalism"));
            float herbClaimed = GetNpcStatValue(npc, ResolveNpcStat("cmcStatProfAppliedHerbalismClaimed"));

            Plugin.Logger.LogDebug("[ProfessorSchedulePatch][CommissionDiag] "
                + $"npcEnv={envLabel} associatedCardEnv={associatedCardEnv} "
                + $"commissionsCard={(commissionsCard != null ? "present" : "MISSING")} commissionsCardEnv={commissionsCardEnv} "
                + $"HasVisibleCommissions={hasVisible} phase={phaseVal} "
                + $"specimenReady={specimenReady} fieldReady={fieldReady} fieldClaimed={fieldClaimed} "
                + $"herbEligible={herbEligible} herbClaimed={herbClaimed}");

            LogCommissionPopupState(gm, npc, commissionsCard);
        }

        /// <summary>
        /// Dumps, per commission slot on Agent_Professor.json's CommissionsBpWarpData, the two
        /// gates BlueprintConstructionPopup.SetupActions ANDs together to decide whether that
        /// slot's switch-blueprint button renders (owner report 2026-08-05: a "?" placeholder
        /// slot plus a duplicated "Specimen Request" switch button in the Commissions popup).
        /// Also dumps CommissionsAssociatedCard.CardModel.ContainedBlueprintCards.Length and
        /// SelectedContainedBlueprint so a mismatch against CommissionsBp.Length (3 expected)
        /// is visible directly. Demote to LogDebug once the glitch is confirmed and fixed.
        /// </summary>
        private static void LogCommissionPopupState(object gm, object npc, object commissionsCard)
        {
            if (Reflect.GetMember(_agent, "CommissionsBp") is not Array commissionsBp) return;

            var blueprintStates = Reflect.GetMember(gm, "BlueprintModelStates") as System.Collections.IDictionary;
            int selected = commissionsCard != null ? Convert.ToInt32(Reflect.GetMember(commissionsCard, "SelectedContainedBlueprint") ?? -1) : -1;
            var containedCards = commissionsCard != null ? Reflect.GetMember(Reflect.GetMember(commissionsCard, "CardModel"), "ContainedBlueprintCards") as Array : null;

            var sb = new System.Text.StringBuilder();
            sb.Append("[ProfessorSchedulePatch][CommissionPopupDiag] ")
              .Append($"CommissionsBp.Length={commissionsBp.Length} ContainedBlueprintCards.Length={(containedCards?.Length.ToString() ?? "null")} SelectedContainedBlueprint={selected} ");

            for (int i = 0; i < commissionsBp.Length; i++)
            {
                var bpCard = commissionsBp.GetValue(i);
                string uid = bpCard != null ? CardUtil.GetCardUniqueId(bpCard) : "null";
                bool visible = false;
                if (bpCard != null && _commissionIsVisibleMethod != null)
                {
                    try { visible = (bool)_commissionIsVisibleMethod.Invoke(npc, new object[] { i }); }
                    catch (Exception ex) { Plugin.Logger.LogDebug($"[ProfessorSchedulePatch] CommissionIsVisible({i}) failed: {ex.InnerException?.ToString() ?? ex.ToString()}"); }
                }
                string state = "MISSING-KEY";
                if (bpCard != null && blueprintStates != null && blueprintStates.Contains(bpCard))
                    state = blueprintStates[bpCard]?.ToString() ?? "null";

                sb.Append($"[{i}]uid={uid} visible={visible} state={state} ");
            }

            Plugin.Logger.LogDebug(sb.ToString());
        }

        /// <summary>
        /// Points the Professor's art at wherever he currently is: Academy interior →
        /// Professor_Indoors, the 4 outdoor forage nodes → Professor_Outdoors, Inn interior →
        /// Professor_Inn (Academy art until that PNG exists). Swaps NPCModel.AgentImage (the
        /// dialog + trading popups read it live on open), restamps both model cards (their
        /// image was copied from AgentImage once at CreateModelCard), and refreshes any live
        /// card graphics so a card already on the player's board updates the same tick.
        /// </summary>
        private static void SyncPortrait(object npc)
        {
            if (_setCardImageMethod == null) return;

            var currentEnv = Reflect.GetMember(npc, "CurrentEnvironment");
            if (currentEnv == null || Reflect.GetMember(currentEnv, "IsNull") is true) return;
            var envCard = Reflect.GetMember(currentEnv, "EnvCard");
            if (envCard == null) return;

            int portrait;
            if (ReferenceEquals(envCard, _academyInterior)) portrait = PortraitAcademy;
            else if (ReferenceEquals(envCard, _innInterior)) portrait = PortraitInn;
            else if (Array.IndexOf(_outdoorNodes, envCard) >= 0) portrait = PortraitOutdoors;
            else return; // somewhere unexpected — leave the current art alone

            if (portrait == _lastPortrait) return;

            Plugin.Logger.LogDebug($"[ProfessorSchedulePatch] SyncPortrait: switching to portrait {portrait}.");
            var sprite = ResolvePortraitSprite(portrait);
            if (sprite == null) return;

            Reflect.SetMember(_agent, "AgentImage", sprite);
            ApplyCardImage(Reflect.GetMember(npc, "ModelCard"), sprite);
            ApplyCardImage(Reflect.GetMember(npc, "CommissionsModelCard"), sprite);
            RefreshCardVisuals(Reflect.GetMember(npc, "AssociatedCard"));
            RefreshCardVisuals(Reflect.GetMember(npc, "CommissionsAssociatedCard"));
            _lastPortrait = portrait;
        }

        private static Sprite ResolvePortraitSprite(int portrait)
        {
            string name = portrait switch
            {
                PortraitOutdoors => OutdoorsSpriteName,
                PortraitInn => InnSpriteName,
                _ => AcademySpriteName,
            };
            var sprite = GameContent.Find<Sprite>(name);
            if (sprite == null && portrait == PortraitInn)
                sprite = GameContent.Find<Sprite>(AcademySpriteName); // fallback if Inn art not found
            return sprite;
        }

        private static void ApplyCardImage(object cardData, Sprite sprite)
        {
            if (cardData == null) return;
            try { _setCardImageMethod.Invoke(cardData, new object[] { sprite }); }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[ProfessorSchedulePatch] ApplyCardImage failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void RefreshCardVisuals(object inGameCard)
        {
            if (inGameCard == null) return;
            var visuals = Reflect.GetMember(inGameCard, "CardVisuals");
            if (visuals == null) return;
            _refreshVisualsMethod ??= visuals.GetType().GetMethod("RefreshCookingStatus",
                BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            // Destroyed-but-not-null Unity objects throw inside — harmless to swallow here;
            // the next real render reads CurrentArt (and thus the new image) anyway.
            try { _refreshVisualsMethod?.Invoke(visuals, null); }
            catch (Exception ex)
            {
                if (!_refreshVisualsFailureLogged)
                {
                    _refreshVisualsFailureLogged = true;
                    Plugin.Logger.LogDebug($"[ProfessorSchedulePatch] RefreshCardVisuals swallowed (expected on a destroyed card): {ex.InnerException?.ToString() ?? ex.ToString()}");
                }
            }
        }

        private static bool IsNightHour(float hour) => hour >= 22f || hour < 3f;

        private static int CurrentTotalTick()
        {
            int dtp = GameQuery.DayTimePoints;
            if (dtp < 0) return 0;
            return GameQuery.CurrentDay * 96 + (96 - dtp);
        }

        private static object BuildOutdoorEnvId(object cardData)
        {
            if (cardData == null) return null;
            try { return _envIdFromCardCtor.Invoke(new object[] { cardData, false }); }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[ProfessorSchedulePatch] BuildOutdoorEnvId failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
                return null;
            }
        }

        private static void MoveNpcTo(object gm, object npc, object envId)
        {
            if (envId == null) return;
            try
            {
                _moveNpcMethod.Invoke(gm, new object[] { npc, envId });
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[ProfessorSchedulePatch] MoveNpcTo failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // ── Location helpers ─────────────────────────────────────────────────────

        // True when the Professor is standing in the player's current environment — used to
        // keep him from wandering off (or flipping his portrait) while the player is with him.
        private static bool SharesPlayerEnv(object npc)
        {
            var env = Reflect.GetMember(npc, "CurrentEnvironment");
            if (env == null) return false;
            return Reflect.GetMember(env, "MatchesPlayerEnv") is true;
        }

        // Index into OutdoorNodeUids/ForageActionIds of the node he's currently on, or -1.
        private static int CurrentOutdoorNodeIndex(object npc)
        {
            var env = Reflect.GetMember(npc, "CurrentEnvironment");
            if (env == null || Reflect.GetMember(env, "IsNull") is true) return -1;
            var envCard = Reflect.GetMember(env, "EnvCard");
            return envCard == null ? -1 : Array.IndexOf(_outdoorNodes, envCard);
        }

        private static bool IsAtAcademy(object npc)
        {
            var env = Reflect.GetMember(npc, "CurrentEnvironment");
            if (env == null || Reflect.GetMember(env, "IsNull") is true) return false;
            return ReferenceEquals(Reflect.GetMember(env, "EnvCard"), _academyInterior);
        }

        private static bool IsAtInn(object npc)
        {
            var env = Reflect.GetMember(npc, "CurrentEnvironment");
            if (env == null || Reflect.GetMember(env, "IsNull") is true) return false;
            return ReferenceEquals(Reflect.GetMember(env, "EnvCard"), _innInterior);
        }

        // The outdoor tile the Professor is standing on right now, for tile-walking purposes —
        // either one of the 4 wander nodes, or the Village (the shared front door he's
        // conceptually "at" whenever he's inside the Academy or the Inn).
        private static string CurrentOutdoorUidOf(object npc)
        {
            int idx = CurrentOutdoorNodeIndex(npc);
            if (idx >= 0) return OutdoorNodeUids[idx];
            if (IsAtAcademy(npc) || IsAtInn(npc)) return VillageEnvUid;
            return null; // unknown / not yet spawned anywhere trackable
        }

        // Walks one outdoor tile per call toward destOutdoorUid, then takes the (possibly
        // interior) finalEnvId hop once that tile is reached. If he's already on
        // destOutdoorUid (including the Academy<->Inn case, which share the Village tile as
        // their front door — a same-tile hop needs no walking at all), the final hop fires
        // immediately, matching the previous one-shot behavior for that case.
        private static void WalkTowards(object gm, object npc, string destOutdoorUid, object finalEnvId)
        {
            string currentUid = CurrentOutdoorUidOf(npc);
            if (currentUid == null || string.Equals(currentUid, destOutdoorUid, StringComparison.OrdinalIgnoreCase))
            {
                MoveNpcTo(gm, npc, finalEnvId);
                return;
            }

            string nextUid = NpcTileWalker.AdvanceStep(npc, currentUid, destOutdoorUid, TicksPerTile, CurrentTotalTick());
            if (nextUid == null) return; // step not due yet, or (rarely) no path — wait rather than stall on a bad hop

            if (string.Equals(nextUid, destOutdoorUid, StringComparison.OrdinalIgnoreCase))
            {
                MoveNpcTo(gm, npc, finalEnvId);
                return;
            }

            var stepCard = CardUtil.GetCardDataById(nextUid);
            if (stepCard == null)
            {
                Plugin.Logger.LogWarning($"[ProfessorSchedulePatch] WalkTowards: intermediate tile '{nextUid}' has no CardData — jumping straight to the destination instead.");
                MoveNpcTo(gm, npc, finalEnvId);
                return;
            }
            MoveNpcTo(gm, npc, BuildOutdoorEnvId(stepCard));
        }

        // ── C#-fired forage / specialty stock (into his own satchel) ─────────────

        private static void FireForageThrottled(object npc, object associatedCard, int nodeIndex)
        {
            if (nodeIndex < 0 || nodeIndex >= ForageActionIds.Length) return;
            int totalTick = CurrentTotalTick();
            if (_lastForageTotalTick != int.MinValue && totalTick - _lastForageTotalTick < WanderIntervalTicks)
                return;
            _lastForageTotalTick = totalTick;
            FireAgentAction(npc, associatedCard, ForageActionIds[nodeIndex]);
        }

        private static void FireSpecialtyStock(object npc, object associatedCard)
        {
            int today = GameQuery.CurrentDay;
            if (_lastSpecialtyDay == today) return;
            _lastSpecialtyDay = today;

            List<object> heldCards = null;
            for (int i = 0; i < Courses.Length && i < SpecialtyActionIds.Length; i++)
            {
                if (!HasCourseCached(i)) continue;

                heldCards ??= Inventory.Cards(associatedCard);
                int held = 0;
                foreach (var card in heldCards)
                {
                    var uid = CardUtil.GetCardUniqueId(card);
                    if (uid != null && Courses[i].PoolUids.Contains(uid)) held++;
                }
                if (held >= SpecialtyCap) continue;

                FireAgentAction(npc, associatedCard, SpecialtyActionIds[i]);
            }
        }

        // Fires an AgentAction by ID via ToAction+PerformAction with the Professor's own card as
        // the receiving container — the path proven (InnKeeperSpawnPatch) to route
        // DropCardsInsideInventory into an NPC's satchel. Native CheckForActions never fires
        // these (they are RepeatOptions:OnlyWhenTriggered), so there is no double-firing.
        // Returns true when the action was found AND dispatched. The forage/specialty call sites
        // ignore the result (a missing forage action is already covered by the warning below);
        // CopperChestPatch.TryAccrue consumes it so a typo'd chest ActionID is reported once
        // instead of silently accruing nothing forever.
        private static bool FireAgentAction(object npc, object associatedCard, string actionId)
        {
            if (_toActionMethod == null || _performActionMethod == null || _inGameNpcOrPlayerCtor == null)
            {
                if (!_fireActionUnavailableWarned)
                {
                    _fireActionUnavailableWarned = true;
                    Plugin.Logger.LogWarning("[ProfessorSchedulePatch] PerformAction reflection unavailable — forage/specialty stock inactive.");
                }
                return false;
            }

            if (Reflect.GetMember(_agent, "AgentActions") is not Array agentActions) return false;
            object matched = null;
            foreach (var action in agentActions)
            {
                if (action != null && Reflect.GetMember(action, "ActionID") as string == actionId)
                {
                    matched = action;
                    break;
                }
            }
            if (matched == null) return false;

            try
            {
                var cardAction = _toActionMethod.Invoke(matched, new object[] { npc, associatedCard });
                var user = _inGameNpcOrPlayerCtor.Invoke(new object[] { npc });
                _performActionMethod.Invoke(null, new object[] { cardAction, associatedCard, true, user });
                Plugin.Logger.LogDebug($"[ProfessorSchedulePatch] Fired '{actionId}'.");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[ProfessorSchedulePatch] FireAgentAction('{actionId}') failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
                return false;
            }
        }

        private static bool _loggedNpcStatErr;

        private static float GetNpcStatValue(object npc, object stat)
        {
            if (stat == null) return 0f;
            try { return (float)_getStatValueMethod.Invoke(npc, new object[] { stat }); }
            catch (Exception ex)
            {
                if (!_loggedNpcStatErr) { _loggedNpcStatErr = true;
                    Plugin.Logger.LogWarning($"[ProfessorSchedulePatch] GetNpcStatValue failed, defaulting to 0: {ex.InnerException?.ToString() ?? ex.ToString()}"); }
                return 0f;
            }
        }

        private static void SetNpcStatValue(object npc, object stat, float value)
        {
            if (stat == null) return;
            try
            {
                if (Reflect.GetMember(npc, "NPCStatsDict") is not IDictionary dict) return;
                if (!dict.Contains(stat)) return;
                var inGameStat = dict[stat];
                if (inGameStat == null) return;
                _setStatValueMethod ??= inGameStat.GetType().GetMethod("SetStatValue",
                    BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(float) }, null);
                _setStatValueMethod?.Invoke(inGameStat, new object[] { value });
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[ProfessorSchedulePatch] SetNpcStatValue failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }
    }
}
