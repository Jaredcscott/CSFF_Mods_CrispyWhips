using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using HarmonyLib;
using UnityEngine;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Village Apothecary — Phase 4/5 scheduler (Documentation/Design/Village_Apothecary_Plan.md),
    /// extended 2026-07-18 with the owner's exact daily commute timetable, a portrait system, and
    /// the gift-a-rare-herb-mixture -> 2-day potion brew side quest. Extended again to add a
    /// guaranteed weekly Academy visit and a guaranteed evening Inn visit (village-life ask), with
    /// matching Inn/Academy portrait states.
    ///
    /// The Apothecary herself is spawned and reload-restored by the generalized
    /// CottageResidentSpawnPatch (she is a cottage resident whose "cottage" is the
    /// cmcApothecaryCabin at the Foraging Forest). This patch owns everything that happens
    /// AFTER she has moved in:
    ///
    ///   1. Trade restock — unchanged from the original phase 4/5 build.
    ///   2. Daily commute (new) — a fully deterministic function of GameQuery.HourOfDay, so it
    ///      needs no persisted state and re-derives correctly after any reload:
    ///        [0:00, 6:00)   inside the cabin (cmcApothecaryCabinInterior, an instanced CT4 —
    ///                       same Enter/Exit shape as the Academy/Inn interiors)
    ///        [6:00, 9:00)   walking home->village, 45 min/tile through MossyClearing,
    ///                       HighGrove, PineTrail (reverse of the outbound path)
    ///        [9:00, 19:15)  staffing the stall at the Village (on her one weekly Academy day,
    ///                       [13:00, 17:00) of this window is spent at the Academy instead —
    ///                       village-life ask: "the academy at least one day a week")
    ///        [19:15, 21:00) the Inn, every evening — guaranteed daily, not a roll (village-life
    ///                       ask: "all NPCs spend time in the Inn in the evenings")
    ///        [21:00, 24:00) walking village->home, 45 min/tile through PineTrail, HighGrove,
    ///                       MossyClearing, arriving at the Foraging Forest cabin exterior
    ///                       (or standing outside once she's arrived, until midnight)
    ///      Before she's been gifted the stall kit she never leaves the Foraging Forest — the
    ///      indoor/outdoor half of the cycle (inside midnight-6am, outside the rest of the day)
    ///      still runs on its own, matching "when the Alchemist moves in she has an indoor image
    ///      where she is standing." Moving her into the instanced cabin/Academy/Inn interiors
    ///      requires the player to have physically visited each at least once this session (same
    ///      EnvID-capture prerequisite ProfessorSchedulePatch's Academy/Inn homing has) — until
    ///      then she waits outside/at the village instead.
    ///   3. Portrait sync (new) — her card + dialog/trade portrait follows the same states:
    ///      Village (CMC_Apothecary), in transit or standing outside the cabin (CMC_Alchemist_Forest),
    ///      inside the cabin (CMC_Alchemist_Cabin), brewing (CMC_Alchemist_Potions), her weekly
    ///      Academy visit (CMC_Alchemist_Academy), or her evening Inn visit (CMC_Alchemist_Inn).
    ///   4. Healing-potion brew (new) — "Give Herb Mixture" (Agent_Apothecary.json
    ///      DragAndDropActions) consumes a player-crafted cmcApothecaryHealingMixture (rare H&F
    ///      herbs incl. hemp) and nudges cmcStatApothecaryPotionCraftDay from 0 to a small
    ///      sentinel (JSON can't compute "today + 2" — only C# can, the same sanctioned exception
    ///      CottageResidentSpawnPatch's move-in timer uses). This scheduler arms that sentinel to
    ///      an absolute due day, freezes her commute/portrait at CMC_Alchemist_Potions for the
    ///      two days, then fires the ApothecaryBrewPotion AgentAction (same FireAgentAction path
    ///      as trade restock) to drop a cmcApothecaryHealingPotion into her own inventory and
    ///      resume the normal schedule.
    ///
    /// This patch never CREATES the NPC and never spawns any card, so it can never race the
    /// resident chassis over her placement.
    /// </summary>
    internal static class ApothecarySchedulePatch
    {
        private const string ApothecaryAgentUid = "cmcApothecaryAgent";
        private const string CabinInteriorUid   = "cmcApothecaryCabinInterior";
        private const string AcademyInteriorUid = "cmcAcademyInterior";
        private const string InnInteriorUid     = "cmcInnInterior";
        private const string GiftedStatUid      = "cmcStatApothecaryStallGifted";
        private const string PotionCraftStatUid = "cmcStatApothecaryPotionCraftDay";

        // Her Copper Chest config, resolved once from CopperChestPatch (the single owner of every
        // chest's UIDs, action IDs and caps). Null only if the config were ever removed there.
        private static CopperChestPatch.ChestConfig ChestConfig;

        private const string RestockActionId   = "ApothecaryRestock";
        private const string RestockActionHfId = "ApothecaryRestock_HF";
        private const string BrewActionId      = "ApothecaryBrewPotion";
        private const int    RestockIntervalDays = 4;
        private const int    PotionCraftDays     = 2;

        // Exactly one day per week — not a roll. Distinct from Miller (1) and Weaver (4) so the
        // village's three scheduled NPCs don't all show up at the Academy the same day.
        private const int AcademyDayOfWeek = 6;

        // Village <-> Foraging Forest commute path (5 nodes, 4 hops — matches the WorldMap's
        // actual node graph: Village -PineTrail-HighGrove-MossyClearing- Foraging Forest).
        private static readonly string[] CommuteNodeUids =
        {
            "cmcEnvVillage", "cmcEnvPineTrail", "cmcEnvHighGrove", "cmcEnvMossyClearing", "cmcEnvForagingForest"
        };
        private const int DestVillage        = 0;
        private const int DestForagingForest = 4;
        private const int DestInterior       = -1; // sentinel: inside the cabin, not an index into CommuteNodeUids
        private const int DestAcademy        = -2; // sentinel: at the Academy (weekly visit)
        private const int DestInn            = -3; // sentinel: at the Inn (evening visit)

        private const float TileTravelHours   = 0.75f; // 45 minutes per tile
        private const int TicksPerTile = NpcTileWalker.DefaultTicksPerTile; // same 45 min/tile, in DTP ticks
        private const float ReturnStartHour     = 6f;     // leaves the cabin, heading to the village
        private const float ReturnEndHour       = 9f;     // arrives at the village (4 tiles * 45 min)
        private const float AcademyVisitStartHour = 13f;  // weekly Academy visit window (only on AcademyDayOfWeek)
        private const float AcademyVisitEndHour   = 17f;
        private const float StallCloseHour      = 19.25f; // 7:15 PM — stops staffing the stall, heads for the Inn
        private const float InnVisitEndHour     = 21f;    // leaves the Inn, heading home
        private const float OutboundEndHour     = InnVisitEndHour + 3 * TileTravelHours; // arrives at the cabin exterior

        private enum ApPortrait { None = 0, Village = 1, Forest = 2, Cabin = 3, Potions = 4, Inn = 5, Academy = 6 }
        private const string VillageSpriteName = "CMC_Apothecary";
        private const string ForestSpriteName  = "CMC_Alchemist_Forest";
        private const string CabinSpriteName   = "CMC_Alchemist_Cabin";
        private const string PotionsSpriteName = "CMC_Alchemist_Potions";
        private const string InnSpriteName     = "CMC_Alchemist_Inn";
        private const string AcademySpriteName = "CMC_Alchemist_Academy";

        private static bool _initialized;

        // Resolved once game data is loaded.
        private static object _agent;            // NPCAgent
        private static object[] _commuteNodes;    // CardData[5], see CommuteNodeUids
        private static object _cabinInteriorCard; // CardData (cmcApothecaryCabinInterior, CT4)
        private static object _academyInteriorCard; // CardData (cmcAcademyInterior, CT4)
        private static object _innInteriorCard;      // CardData (cmcInnInterior, CT4)
        private static object _giftedStat;        // GameStat SO
        private static object _potionCraftStat;   // GameStat SO

        // Instanced-env EnvID capture (same prerequisite as ProfessorSchedulePatch's Academy/Inn
        // homing — an instanced env needs a live EnvID instance from an actual player visit
        // before C# can MoveNPC an NPC into it).
        private static object _cabinInteriorEnvId;
        private static bool _cabinInteriorCaptured;
        private static object _academyEnvId;
        private static object _innEnvId;
        private static bool _academyCaptured;
        private static bool _innCaptured;

        private static ApPortrait _lastPortrait = ApPortrait.None;

        // Restock cadence (in-memory; resets on relaunch — self-heals an empty shelf immediately).
        private static int _lastRestockDay = int.MinValue;
        private static bool _missingRestockActionWarned;

        // Reflection handles, resolved once.
        private static Type _gmType;
        private static Type _npcAgentType;
        private static Type _envIdType;
        private static Type _inGameNpcType;
        private static Type _cardDataType;
        private static Type _npcActionType;         // NPCAction (declares ToAction)
        private static Type _inGameCardBaseType;    // InGameCardBase
        private static Type _inGameNpcOrPlayerType; // InGameNPCOrPlayer (struct)
        private static MethodInfo _getFromIdMethod; // UniqueIDScriptable.GetFromID(string)
        private static MethodInfo _moveNpcMethod;   // GameManager.MoveNPC(InGameNPC, EnvID)
        private static MethodInfo _toActionMethod;      // NPCAction.ToAction(InGameNPC, InGameCardBase)
        private static MethodInfo _performActionMethod; // GameManager.PerformAction(CardAction, InGameCardBase, bool, InGameNPCOrPlayer)
        private static ConstructorInfo _inGameNpcOrPlayerCtor; // InGameNPCOrPlayer(InGameNPC)
        private static ConstructorInfo _envIdFromCardCtor;     // EnvID(CardData, bool)

        // Portrait application (optional — degrades gracefully if the game's SetCardImage/refresh
        // signatures shift; art just stays static rather than following her around).
        private static MethodInfo _setCardImageMethod;   // CardData.SetCardImage(Sprite)
        private static MethodInfo _refreshVisualsMethod; // CardGraphics.RefreshCookingStatus() — resolved lazily (per-instance type)
        private static bool _refreshVisualsFailureLogged;

        public static void Initialize(Harmony harmony)
        {
            if (_initialized) return;
            _initialized = true;

            if (!ResolveTypes())
            {
                Plugin.Logger.LogWarning("[ApothecarySchedulePatch] Required game types not found — Apothecary scheduler inactive.");
                return;
            }

            var initStatsMethod = AccessTools.Method(_gmType, "InitializeStatsAndActions");
            if (initStatsMethod != null)
                harmony.Patch(initStatsMethod, postfix: new HarmonyMethod(typeof(ApothecarySchedulePatch), nameof(ResetSessionState_Postfix)));

            TickEvents.Interval(1f, RunScheduler, "ApothecarySchedule");
            Plugin.Logger.LogDebug("[ApothecarySchedulePatch] initialized.");
        }

        // A new run/save may hand her a different live instanced-env instance (or none yet) and
        // her portrait sprite reference doesn't survive a reload — drop the in-memory caches so
        // this session re-derives both from scratch instead of skipping on a stale comparison.
        private static void ResetSessionState_Postfix()
        {
            _lastPortrait = ApPortrait.None;
            _cabinInteriorCaptured = false;
            _cabinInteriorEnvId = null;
            _academyCaptured = false;
            _innCaptured = false;
            _academyEnvId = null;
            _innEnvId = null;
            _lastRestockDay = int.MinValue;
            _missingRestockActionWarned = false;
            NpcTileWalker.ResetAll();
        }

        private static bool ResolveTypes()
        {
            if (_gmType != null) return true;

            _gmType = CardUtil.FindGameType("GameManager");
            _npcAgentType = CardUtil.FindGameType("NPCAgent");
            _envIdType = CardUtil.FindGameType("EnvID");
            _inGameNpcType = CardUtil.FindGameType("InGameNPC");
            _cardDataType = CardUtil.FindGameType("CardData");
            _npcActionType = CardUtil.FindGameType("NPCAction");
            _inGameCardBaseType = CardUtil.FindGameType("InGameCardBase");
            _inGameNpcOrPlayerType = CardUtil.FindGameType("InGameNPCOrPlayer");
            var uidType = CardUtil.FindGameType("UniqueIDScriptable");

            if (_gmType == null || _npcAgentType == null || _envIdType == null || _inGameNpcType == null
                || _cardDataType == null || _npcActionType == null || _inGameCardBaseType == null
                || _inGameNpcOrPlayerType == null || uidType == null)
                return false;

            _getFromIdMethod = uidType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "GetFromID" && !m.IsGenericMethodDefinition
                    && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
            _moveNpcMethod = AccessTools.Method(_gmType, "MoveNPC", new[] { _inGameNpcType, _envIdType });
            _toActionMethod = AccessTools.Method(_npcActionType, "ToAction", new[] { _inGameNpcType, _inGameCardBaseType });
            _performActionMethod = _gmType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "PerformAction" && m.GetParameters().Length == 4);
            _inGameNpcOrPlayerCtor = _inGameNpcOrPlayerType.GetConstructor(new[] { _inGameNpcType });
            _envIdFromCardCtor = _envIdType.GetConstructor(new[] { _cardDataType, typeof(bool) });

            // Optional — portrait tracking degrades gracefully if absent.
            _setCardImageMethod = AccessTools.Method(_cardDataType, "SetCardImage", new[] { typeof(Sprite) });

            return _getFromIdMethod != null && _moveNpcMethod != null && _toActionMethod != null
                && _performActionMethod != null && _inGameNpcOrPlayerCtor != null && _envIdFromCardCtor != null;
        }

        private static bool _refsUnresolvedWarned;

        private static bool ResolveRefs()
        {
            _agent ??= _getFromIdMethod.Invoke(null, new object[] { ApothecaryAgentUid });
            _giftedStat ??= _getFromIdMethod.Invoke(null, new object[] { GiftedStatUid });
            _potionCraftStat ??= _getFromIdMethod.Invoke(null, new object[] { PotionCraftStatUid });
            _cabinInteriorCard ??= CardUtil.GetCardDataById(CabinInteriorUid);
            // Academy/Inn are optional — graceful degradation matches the cabin interior's own
            // EnvID-capture prerequisite: if unresolved, the weekly/evening visits simply never
            // fire and she keeps to the stall/commute schedule she already had.
            _academyInteriorCard ??= CardUtil.GetCardDataById(AcademyInteriorUid);
            _innInteriorCard ??= CardUtil.GetCardDataById(InnInteriorUid);

            if (_commuteNodes == null)
            {
                var nodes = new object[CommuteNodeUids.Length];
                bool allResolved = true;
                for (int i = 0; i < CommuteNodeUids.Length; i++)
                {
                    nodes[i] = CardUtil.GetCardDataById(CommuteNodeUids[i]);
                    if (nodes[i] == null) allResolved = false;
                }
                if (allResolved) _commuteNodes = nodes;
            }

            bool resolved = _agent != null && _giftedStat != null && _potionCraftStat != null
                && _cabinInteriorCard != null && _commuteNodes != null;

            if (!resolved && !_refsUnresolvedWarned)
            {
                _refsUnresolvedWarned = true;
                Plugin.Logger.LogWarning($"[ApothecarySchedulePatch] ResolveRefs failed (agent null={_agent == null}, giftedStat null={_giftedStat == null}, potionCraftStat null={_potionCraftStat == null}, cabinInteriorCard null={_cabinInteriorCard == null}, commuteNodes null={_commuteNodes == null}) — the Apothecary can never spawn/schedule until this succeeds.");
            }

            return resolved;
        }

        private static void RunScheduler()
        {
            try
            {
                if (!ResolveTypes() || !ResolveRefs()) return;

                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;

                var npc = FindLiveNpc(gm);
                if (npc == null) return; // hasn't moved in yet (CottageResidentSpawnPatch owns her spawn)

                var associatedCard = Reflect.GetMember(npc, "AssociatedCard");
                if (associatedCard == null) return; // AssignOrCreateNPCCards hasn't finished yet

                // Trade restock — active from move-in, independent of the stall/commute phase.
                RestockIfNeeded(npc, associatedCard);

                // Copper Chest accrual (Village_Master_Plan.md §10.8.3.3). Deliberately PARALLEL to
                // the satchel restock above and NOT routed through CottageResidentSpawnPatch: her
                // Residents entry there leaves ChestAccrualAgentUid null precisely so this file is
                // the single caller for her chest, which is what keeps R6's double-drop impossible
                // across files as well as within one. CopperChestPatch.TryAccrue owns the env gate
                // (no-ops unless the player is standing in her cabin interior), the persistent
                // due-day stamp and both caps; this file contributes only its own proven
                // FireAgentAction, exactly as it does for the restock and brew actions.
                ChestConfig ??= CopperChestPatch.ForAgent(ApothecaryAgentUid);
                if (ChestConfig != null)
                    CopperChestPatch.TryAccrue(ChestConfig, npc, GameQuery.CurrentDay, FireAgentAction);

                CaptureCabinInteriorEnvId(gm);
                CaptureSharedEnvIds(gm);

                // Brewing overrides the whole schedule — she stays put with the Potions portrait
                // for the full 2 days regardless of what time or gift-phase it is.
                if (HandlePotionCraft(gm, npc, associatedCard))
                {
                    SyncPortrait(npc, ApPortrait.Potions);
                    return;
                }

                float gifted = GetGiftedFlag(gm);
                bool commuting = gifted >= 0.5f;
                bool isAcademyDay = GameQuery.CurrentDay % 7 == AcademyDayOfWeek;

                float hour = GameQuery.HourOfDay;
                int destIndex = ComputeDestIndex(hour, commuting, isAcademyDay);
                RunCommute(gm, npc, destIndex);
                // Derived from where she ACTUALLY is (not the scheduled destination) — she may not
                // have moved yet this tick (SharesPlayerEnv holds her back, or the cabin interior
                // isn't captured yet), so the portrait must reflect reality, not intent. Mirrors
                // ProfessorSchedulePatch.SyncPortrait's own envCard-based derivation.
                SyncPortrait(npc, ActualPortrait(npc));
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[ApothecarySchedulePatch] RunScheduler failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // ── Daily commute timetable (pure function of the clock — no persisted state) ────────

        private static int ComputeDestIndex(float hour, bool commuting, bool isAcademyDay)
        {
            if (hour < ReturnStartHour) return DestInterior; // 0:00-6:00 — inside the cabin

            if (!commuting) return DestForagingForest; // cabin-only phase: outside all day

            if (hour < ReturnEndHour)
            {
                int seg = Math.Min(3, (int)((hour - ReturnStartHour) / TileTravelHours));
                return 3 - seg; // home->village: MossyClearing(3) -> HighGrove(2) -> PineTrail(1) -> Village(0)
            }

            // Weekly Academy visit — carves a window out of her stall hours, one day/week only.
            if (isAcademyDay && hour >= AcademyVisitStartHour && hour < AcademyVisitEndHour) return DestAcademy;

            if (hour < StallCloseHour) return DestVillage; // 9:00-19:15 — staffing the stall
            if (hour < InnVisitEndHour) return DestInn;     // 19:15-21:00 — the Inn, every evening

            if (hour < OutboundEndHour)
            {
                int seg = Math.Min(3, (int)((hour - InnVisitEndHour) / TileTravelHours));
                return 1 + seg; // village->home: PineTrail(1) -> HighGrove(2) -> MossyClearing(3) -> Forest(4)
            }
            return DestForagingForest; // 23:15-24:00 — standing outside the cabin
        }

        // Reads her ACTUAL CurrentEnvironment.EnvCard — not the scheduled destination — so the
        // portrait never contradicts where she's really standing (see ProfessorSchedulePatch's own
        // SyncPortrait, which derives its state the same way for exactly this reason).
        private static ApPortrait ActualPortrait(object npc)
        {
            var env = Reflect.GetMember(npc, "CurrentEnvironment");
            if (env == null || Reflect.GetMember(env, "IsNull") is true) return ApPortrait.None;
            var envCard = Reflect.GetMember(env, "EnvCard");
            if (envCard == null) return ApPortrait.None;

            if (ReferenceEquals(envCard, _cabinInteriorCard)) return ApPortrait.Cabin;
            if (ReferenceEquals(envCard, _innInteriorCard)) return ApPortrait.Inn;
            if (ReferenceEquals(envCard, _academyInteriorCard)) return ApPortrait.Academy;
            if (ReferenceEquals(envCard, _commuteNodes[DestVillage])) return ApPortrait.Village;
            for (int i = 1; i <= DestForagingForest; i++)
                if (ReferenceEquals(envCard, _commuteNodes[i])) return ApPortrait.Forest;

            return ApPortrait.None; // somewhere unexpected — leave the current art alone
        }

        private static void RunCommute(object gm, object npc, int destIndex)
        {
            if (SharesPlayerEnv(npc)) return; // don't teleport her out from under a mid-visit player

            string forestUid = CommuteNodeUids[DestForagingForest];
            string villageUid = CommuteNodeUids[DestVillage];

            if (destIndex == DestInterior)
            {
                if (!_cabinInteriorCaptured)
                {
                    // Can't send her inside until the player has visited the interior at least
                    // once this session (instanced-env EnvID capture prerequisite) — wait outside.
                    WalkTowards(gm, npc, forestUid, BuildEnvId(_commuteNodes[DestForagingForest]));
                    return;
                }
                // Walk to the cabin's front door (the Foraging Forest tile) if she isn't there
                // yet, then take the single instantaneous "step inside" hop.
                WalkTowards(gm, npc, forestUid, _cabinInteriorEnvId);
                return;
            }

            if (destIndex == DestAcademy)
            {
                if (!_academyCaptured)
                {
                    WalkTowards(gm, npc, villageUid, BuildEnvId(_commuteNodes[DestVillage]));
                    return;
                }
                WalkTowards(gm, npc, villageUid, _academyEnvId);
                return;
            }

            if (destIndex == DestInn)
            {
                if (!_innCaptured)
                {
                    WalkTowards(gm, npc, villageUid, BuildEnvId(_commuteNodes[DestVillage]));
                    return;
                }
                WalkTowards(gm, npc, villageUid, _innEnvId);
                return;
            }

            WalkTowards(gm, npc, CommuteNodeUids[destIndex], BuildEnvId(_commuteNodes[destIndex]));
        }

        // The outdoor tile she's standing on right now, for tile-walking purposes — one of the 5
        // commute nodes, or the Foraging Forest (the cabin's front door) whenever she's inside it,
        // or the Village (the Academy/Inn's shared front door) whenever she's inside either.
        private static string CurrentOutdoorUidOf(object npc)
        {
            for (int i = 0; i < CommuteNodeUids.Length; i++)
                if (IsAtEnv(npc, _commuteNodes[i])) return CommuteNodeUids[i];
            if (IsAtEnv(npc, _cabinInteriorCard)) return CommuteNodeUids[DestForagingForest];
            if (IsAtEnv(npc, _academyInteriorCard) || IsAtEnv(npc, _innInteriorCard)) return CommuteNodeUids[DestVillage];
            return null; // unknown / not yet spawned anywhere trackable
        }

        private static void CaptureSharedEnvIds(object gm)
        {
            if (_academyCaptured && _innCaptured) return;

            var currentEnv = Reflect.GetMember(gm, "CurrentEnvironment");
            if (currentEnv == null || Reflect.GetMember(currentEnv, "IsNull") is true) return;
            var envCard = Reflect.GetMember(currentEnv, "EnvCard");

            if (!_academyCaptured && ReferenceEquals(envCard, _academyInteriorCard))
            {
                _academyEnvId = currentEnv;
                _academyCaptured = true;
                Plugin.Logger.LogInfo("[ApothecarySchedulePatch] Captured Academy EnvID for the Apothecary's weekly visit.");
            }
            else if (!_innCaptured && ReferenceEquals(envCard, _innInteriorCard))
            {
                _innEnvId = currentEnv;
                _innCaptured = true;
                Plugin.Logger.LogInfo("[ApothecarySchedulePatch] Captured Inn EnvID for the Apothecary's evening visit.");
            }
        }

        // Walks one outdoor tile per call toward destOutdoorUid, then takes the (possibly
        // interior) finalEnvId hop once that tile is reached — same pattern as
        // ProfessorSchedulePatch.WalkTowards. Preserves ComputeDestIndex's tuned clock schedule
        // (which node she SHOULD conceptually be at) while making the actual relocation
        // tile-stepped rather than a direct jump, so a big time-skip (e.g. sleeping through the
        // night) makes her visibly catch up tile by tile on subsequent ticks instead of
        // teleporting straight to the far end.
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
                Plugin.Logger.LogWarning($"[ApothecarySchedulePatch] WalkTowards: intermediate tile '{nextUid}' has no CardData — jumping straight to the destination instead.");
                MoveNpcTo(gm, npc, finalEnvId);
                return;
            }
            MoveNpcTo(gm, npc, BuildEnvId(stepCard));
        }

        private static int CurrentTotalTick()
        {
            int dtp = GameQuery.DayTimePoints;
            if (dtp < 0) return 0;
            return GameQuery.CurrentDay * 96 + (96 - dtp);
        }

        private static void CaptureCabinInteriorEnvId(object gm)
        {
            if (_cabinInteriorCaptured) return;

            var currentEnv = Reflect.GetMember(gm, "CurrentEnvironment");
            if (currentEnv == null || Reflect.GetMember(currentEnv, "IsNull") is true) return;
            if (!ReferenceEquals(Reflect.GetMember(currentEnv, "EnvCard"), _cabinInteriorCard)) return;

            _cabinInteriorEnvId = currentEnv;
            _cabinInteriorCaptured = true;
            Plugin.Logger.LogInfo("[ApothecarySchedulePatch] Captured the cabin interior EnvID for the commute scheduler.");
        }

        // ── Healing-potion brew (gift -> 2-day timer -> produce) ────────────────────────────

        // Returns true while she is (or just started) brewing — the caller freezes the normal
        // schedule/portrait for the rest of this tick when this is true.
        private static bool HandlePotionCraft(object gm, object npc, object associatedCard)
        {
            float craftDay = GetCraftDay(gm);
            if (craftDay <= 0.001f) return false; // idle — no mixture gifted

            if (craftDay < 1f)
            {
                // "Give Herb Mixture" just nudged the stat to a small sentinel (JSON can't
                // compute "today + N" — only C# can; same sanctioned exception
                // CottageResidentSpawnPatch's move-in timer uses). Arm it to an absolute due day.
                int dueDay = GameQuery.CurrentDay + PotionCraftDays;
                SetCraftDay(gm, dueDay);
                Plugin.Logger.LogInfo($"[ApothecarySchedulePatch] Herb mixture received — the Powerful Healing Potion will be ready on day {dueDay}.");
                return true;
            }

            if (GameQuery.CurrentDay >= craftDay)
            {
                if (!FireAgentAction(npc, associatedCard, BrewActionId))
                {
                    Plugin.Logger.LogWarning("[ApothecarySchedulePatch] Potion due but ApothecaryBrewPotion failed to fire — will retry next tick.");
                    return true; // keep the timer armed; try again rather than silently losing the potion
                }
                SetCraftDay(gm, 0f);
                Plugin.Logger.LogInfo("[ApothecarySchedulePatch] The Powerful Healing Potion is ready.");
                return false; // resume the normal schedule/portrait this same tick
            }

            return true; // still brewing
        }

        private static float GetCraftDay(object gm) => ReadLiveStat(gm, _potionCraftStat);

        private static void SetCraftDay(object gm, float value) => WriteLiveStat(gm, _potionCraftStat, value);

        // ── NPC / stat lookup ─────────────────────────────────────────────────────

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

        // Reads/writes a GameStat off the live InGameStat in GameManager.StatsDict (same idiom as
        // CottageResidentSpawnPatch's move-in timer). Returns -1 when unresolvable yet.
        private static float ReadLiveStat(object gm, object stat)
        {
            if (Reflect.GetMember(gm, "StatsDict") is not IDictionary statsDict) return -1f;
            if (!statsDict.Contains(stat)) return -1f;
            var inGameStat = statsDict[stat];
            if (inGameStat == null) return -1f;
            return Reflect.GetMember(inGameStat, "SimpleCurrentValue") is float f ? f : -1f;
        }

        private static void WriteLiveStat(object gm, object stat, float value)
        {
            if (Reflect.GetMember(gm, "StatsDict") is not IDictionary statsDict) return;
            if (!statsDict.Contains(stat)) return;
            var inGameStat = statsDict[stat];
            if (inGameStat == null) return;
            Reflect.SetMember(inGameStat, "CurrentBaseValue", value);
        }

        private static float GetGiftedFlag(object gm) => ReadLiveStat(gm, _giftedStat);

        // ── Location helpers ─────────────────────────────────────────────────────

        private static bool SharesPlayerEnv(object npc)
        {
            var env = Reflect.GetMember(npc, "CurrentEnvironment");
            if (env == null) return false;
            return Reflect.GetMember(env, "MatchesPlayerEnv") is true;
        }

        private static bool IsAtEnv(object npc, object envCard)
        {
            var env = Reflect.GetMember(npc, "CurrentEnvironment");
            if (env == null || Reflect.GetMember(env, "IsNull") is true) return false;
            return ReferenceEquals(Reflect.GetMember(env, "EnvCard"), envCard);
        }

        private static object BuildEnvId(object envCard)
        {
            if (envCard == null) return null;
            try { return _envIdFromCardCtor.Invoke(new object[] { envCard, false }); }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[ApothecarySchedulePatch] BuildEnvId failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
                return null;
            }
        }

        private static void MoveNpcTo(object gm, object npc, object envId)
        {
            if (envId == null) return;
            try { _moveNpcMethod.Invoke(gm, new object[] { npc, envId }); }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[ApothecarySchedulePatch] MoveNpcTo failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // ── Portrait sync ─────────────────────────────────────────────────────────

        private static void SyncPortrait(object npc, ApPortrait portrait)
        {
            if (_setCardImageMethod == null || portrait == ApPortrait.None || portrait == _lastPortrait) return;

            var sprite = ResolvePortraitSprite(portrait);
            if (sprite == null) return;

            Reflect.SetMember(_agent, "AgentImage", sprite);
            ApplyCardImage(Reflect.GetMember(npc, "ModelCard"), sprite);
            RefreshCardVisuals(Reflect.GetMember(npc, "AssociatedCard"));
            _lastPortrait = portrait;
        }

        private static Sprite ResolvePortraitSprite(ApPortrait portrait)
        {
            string name = portrait switch
            {
                ApPortrait.Forest => ForestSpriteName,
                ApPortrait.Cabin => CabinSpriteName,
                ApPortrait.Potions => PotionsSpriteName,
                ApPortrait.Inn => InnSpriteName,
                ApPortrait.Academy => AcademySpriteName,
                _ => VillageSpriteName,
            };
            return GameContent.Find<Sprite>(name);
        }

        private static void ApplyCardImage(object cardData, Sprite sprite)
        {
            if (cardData == null) return;
            try { _setCardImageMethod.Invoke(cardData, new object[] { sprite }); }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[ApothecarySchedulePatch] ApplyCardImage failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
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
                    Plugin.Logger.LogDebug($"[ApothecarySchedulePatch] RefreshCardVisuals swallowed (expected on a destroyed card): {ex.InnerException?.ToString() ?? ex.ToString()}");
                }
            }
        }

        // ── Trade restock ─────────────────────────────────────────────────────────

        private static void RestockIfNeeded(object npc, object associatedCard)
        {
            int currentDay = GameQuery.CurrentDay;
            int stockCount = Inventory.Cards(associatedCard).Count;

            if (_lastRestockDay == int.MinValue)
            {
                // First sighting this session — baseline the timer instead of restocking an
                // already-stocked shelf. An empty shelf still falls through and restocks now.
                _lastRestockDay = currentDay;
                if (stockCount > 0) return;
            }

            bool due = stockCount == 0 || currentDay - _lastRestockDay >= RestockIntervalDays;
            if (!due) return;

            if (!FireAgentAction(npc, associatedCard, RestockActionId) && !_missingRestockActionWarned)
            {
                _missingRestockActionWarned = true;
                Plugin.Logger.LogWarning($"[ApothecarySchedulePatch] '{RestockActionId}' not found on Agent_Apothecary.json — restock inactive.");
            }
            // Herbs & Fungi tinctures — resolves to nothing (no error) when H&F isn't installed.
            FireAgentAction(npc, associatedCard, RestockActionHfId);

            _lastRestockDay = currentDay;

            int newStock = Inventory.Cards(associatedCard).Count;
            Plugin.Logger.LogInfo($"[ApothecarySchedulePatch] Shelf restocked on day {currentDay} (stock {stockCount} -> {newStock}).");
        }

        // Fires an AgentAction by ID via ToAction+PerformAction with the Apothecary's own card as
        // the receiving container — the path proven (InnKeeperSpawnPatch) to route
        // DropCardsInsideInventory into an NPC's satchel. These actions are
        // RepeatOptions:OnlyWhenTriggered, so native CheckForActions never double-fires them.
        private static bool FireAgentAction(object npc, object associatedCard, string actionId)
        {
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
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[ApothecarySchedulePatch] FireAgentAction('{actionId}') failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
                return false;
            }
        }
    }
}
