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
    /// The Potter (CMC round 2, H6): a sixth resident in the lighter form, a lodger at the Inn with no
    /// cottage and no kiln of his own.
    ///
    /// Spawn and residency copy the Professor's chassis (ProfessorSchedulePatch.cs): the first time the
    /// player stands in the Inn once the village is introduced, GameManager.CreateNPC + InGameNPC.Init with
    /// the player's live EnvID, then AssignOrCreateNPCCards (a mid-session spawn has no other source of a
    /// board card); on every run boot a GameManager.InitializeStatsAndActions postfix restores him from the
    /// save's own NPC data.
    ///
    /// Schedule: he lodges at the Inn and, on the village market day (VillageSeasonsPatch writes
    /// cmcStatMarketDay to 96 on the first day of summer and it drains 1 a tick), works the market corner of
    /// the Village from StallOpenHour to StallCloseHour. With the Quiet Village perk nobody commutes, so he
    /// stays at the Inn. He never moves while he shares the player's room. Every destination EnvID is built
    /// at the moment of the move from its CardData (root CLAUDE.md "Runtime Card State Caching (5)": an EnvID
    /// cached before the run holds a dead UniqueIDIndex key and MoveNPC then moves nobody). The Inn's front
    /// door is the Village tile, so both legs are a single hop and no tile walk is needed.
    ///
    /// Stock: once a day while he is at the Inn and his satchel holds fewer than SatchelCap cards, the
    /// PotterProduce AgentAction is fired into his own satchel (ToAction + PerformAction, the Professor's
    /// FireAgentAction idiom). The day is marked done only once the action was dispatched.
    ///
    /// Board: his Village Hall board (cmcBoardPotter) arrives with the Hall's DefaultEnvCardDrops on a fresh
    /// save. A save whose Hall board was generated before he existed never re-reads that list
    /// (GameManager.CheckForMissingDefaultCardsInEnv only runs it for a missing Explorable), so this poll
    /// places the board when the player stands in the Hall, the board is loaded (not changing environment,
    /// the Hall's own exit card is on it) and no copy is there. The read is GameQuery.CardsInPlayerEnv, never
    /// a bare AllCards scan.
    ///
    /// Registration runs once per process under an _initialized guard (root CLAUDE.md "Per-Run Event
    /// Registration", pattern b). Every early return that is not the ordinary "not yet" is reported once per
    /// cause (root CLAUDE.md "Silent Catch Blocks").
    /// </summary>
    internal static class PotterSchedulePatch
    {
        private const string PotterAgentUid = "cmcPotterAgent";
        private const string InnInteriorUid = "cmcInnInterior";
        private const string VillageEnvUid = "cmcEnvVillage";
        private const string HallInteriorUid = "cmcVillageHallInterior";
        private const string HallExitUid = "cmcVillageHallExit";
        private const string BoardUid = "cmcBoardPotter";
        private const string ProduceActionId = "PotterProduce";

        // Tunable placeholders.
        private const float StallOpenHour = 8f;   // clock hour he sets out at the market corner
        private const float StallCloseHour = 18f; // clock hour he packs up and goes back to the Inn
        private const int SatchelCap = 12;        // no fresh stock while his satchel holds this many cards

        // Portraits (both PNGs ship in Resource/Picture; if one were missing the Stall picture falls back to
        // the Inn one, and a missing Inn picture leaves the card's art alone rather than blanking it).
        private const string InnSpriteName = "CMC_Potter_Inn";
        private const string StallSpriteName = "CMC_Potter_Stall";
        private const int PortraitInn = 1;
        private const int PortraitStall = 2;

        // C5: the CSV keys of his board's location line.
        private const string LegInnKey = "CMC_BoardLeg_Potter_Inn";
        private const string LegStallKey = "CMC_BoardLeg_Potter_Stall";

        private static bool _initialized;
        private static readonly HashSet<string> WarnedCauses = new(StringComparer.Ordinal);

        // Session state, reset on every run boot.
        private static object _liveNpc;
        private static int _lastPortrait;
        private static int _lastProduceDay = int.MinValue;
        private static int _missingCardPolls;
        private const int MissingCardPollsBeforeWarn = 30; // the scheduler polls once a second

        // Data references (stable ScriptableObjects, never EnvIDs).
        private static object _agent;        // NPCAgent
        private static object _innInterior;  // CardData
        private static object _villageCard;  // CardData

        // Reflection handles, resolved once.
        private static Type _gmType;
        private static Type _npcAgentType;
        private static Type _envIdType;
        private static Type _inGameNpcType;
        private static Type _cardDataType;
        private static Type _saveDataByRefType;
        private static Type _gameSaveDataByRefType;
        private static Type _savedNpcCharacterDataType;
        private static MethodInfo _getFromIdMethod;
        private static MethodInfo _createNpcMethod;
        private static MethodInfo _initMethod;
        private static MethodInfo _getNpcDataMethod;
        private static MethodInfo _assignOrCreateCardsMethod;
        private static MethodInfo _moveNpcMethod;
        private static MethodInfo _setCardImageMethod;
        private static MethodInfo _refreshVisualsMethod;
        private static ConstructorInfo _envIdFromCardCtor;
        private static object _envIdEmpty;
        private static MethodInfo _toActionMethod;
        private static MethodInfo _performActionMethod;
        private static ConstructorInfo _inGameNpcOrPlayerCtor;

        public static void Initialize(Harmony harmony)
        {
            if (_initialized) return;
            _initialized = true;

            if (!ResolveTypes())
            {
                Plugin.Logger.LogWarning("[PotterSchedulePatch] Required game types not found, so the Potter never moves in.");
                return;
            }

            var initStatsMethod = AccessTools.Method(_gmType, "InitializeStatsAndActions");
            if (initStatsMethod == null)
                Plugin.Logger.LogWarning("[PotterSchedulePatch] GameManager.InitializeStatsAndActions not found, so the Potter is not restored on a reload.");
            else
                harmony.Patch(initStatsMethod, postfix: new HarmonyMethod(typeof(PotterSchedulePatch), nameof(RestoreOnLoad_Postfix)));

            TickEvents.Interval(1f, CheckAndSpawnOnArrival, "PotterArrivalCheck");
            TickEvents.Interval(1f, RunScheduler, "PotterSchedule");
            TickEvents.Interval(5f, BackfillBoard, "PotterBoardBackfill");

            Plugin.Logger.LogDebug("[PotterSchedulePatch] initialized.");
        }

        private static void WarnOnce(string cause, string message)
        {
            if (WarnedCauses.Add(cause)) Plugin.Logger.LogWarning("[PotterSchedulePatch] " + message);
        }

        private static bool ResolveTypes()
        {
            if (_gmType != null) return true;

            _gmType = CardUtil.FindGameType("GameManager");
            _npcAgentType = CardUtil.FindGameType("NPCAgent");
            _envIdType = CardUtil.FindGameType("EnvID");
            _inGameNpcType = CardUtil.FindGameType("InGameNPC");
            _cardDataType = CardUtil.FindGameType("CardData");
            _saveDataByRefType = CardUtil.FindGameType("NPCAgentSaveDataByReference");
            _gameSaveDataByRefType = CardUtil.FindGameType("GameSaveDataByReference");
            _savedNpcCharacterDataType = CardUtil.FindGameType("SavedNPCCharacterData");
            var uidType = CardUtil.FindGameType("UniqueIDScriptable");

            if (_gmType == null || _npcAgentType == null || _envIdType == null || _inGameNpcType == null
                || _cardDataType == null || _saveDataByRefType == null || _gameSaveDataByRefType == null
                || _savedNpcCharacterDataType == null || uidType == null)
                return false;

            _getFromIdMethod = uidType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "GetFromID" && !m.IsGenericMethodDefinition
                    && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
            _createNpcMethod = AccessTools.Method(_gmType, "CreateNPC", new[] { _npcAgentType, _savedNpcCharacterDataType, typeof(bool) });
            _initMethod = AccessTools.Method(_inGameNpcType, "Init", new[] { _saveDataByRefType, _envIdType });
            _getNpcDataMethod = AccessTools.Method(_gameSaveDataByRefType, "GetNPCData", new[] { _npcAgentType });
            _assignOrCreateCardsMethod = AccessTools.Method(_gmType, "AssignOrCreateNPCCards");
            _moveNpcMethod = AccessTools.Method(_gmType, "MoveNPC", new[] { _inGameNpcType, _envIdType });
            _setCardImageMethod = AccessTools.Method(_cardDataType, "SetCardImage", new[] { typeof(Sprite) });
            _envIdFromCardCtor = _envIdType.GetConstructor(new[] { _cardDataType, typeof(bool) });
            _envIdEmpty = Activator.CreateInstance(_envIdType);

            // Optional: without these he still lodges and keeps his market day, he only never restocks.
            var npcActionType = CardUtil.FindGameType("NPCAction");
            var inGameCardBaseType = CardUtil.FindGameType("InGameCardBase");
            var inGameNpcOrPlayerType = CardUtil.FindGameType("InGameNPCOrPlayer");
            if (npcActionType != null && inGameCardBaseType != null && inGameNpcOrPlayerType != null)
            {
                _toActionMethod = AccessTools.Method(npcActionType, "ToAction", new[] { _inGameNpcType, inGameCardBaseType });
                _performActionMethod = _gmType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "PerformAction" && m.GetParameters().Length == 4);
                _inGameNpcOrPlayerCtor = inGameNpcOrPlayerType.GetConstructor(new[] { _inGameNpcType });
            }

            return _getFromIdMethod != null && _createNpcMethod != null && _initMethod != null
                && _getNpcDataMethod != null && _assignOrCreateCardsMethod != null
                && _moveNpcMethod != null && _envIdFromCardCtor != null;
        }

        private static bool ResolveRefs()
        {
            if (_agent != null && _innInterior != null && _villageCard != null) return true;

            _agent ??= _getFromIdMethod.Invoke(null, new object[] { PotterAgentUid });
            _innInterior ??= CardUtil.GetCardDataById(InnInteriorUid);
            _villageCard ??= CardUtil.GetCardDataById(VillageEnvUid);

            bool resolved = _agent != null && _innInterior != null && _villageCard != null;
            // Before a run boots the game data may not be loaded yet, so only a miss while a
            // GameManager exists is a real failure.
            if (!resolved && CardUtil.GetGameManagerInstance() != null)
                WarnOnce("refs", $"ResolveRefs failed in-game (agent null={_agent == null}, inn null={_innInterior == null}, village null={_villageCard == null}), so the Potter can never move in or move.");
            return resolved;
        }

        // -- Spawn and residency ---------------------------------------------------------------

        private static bool AlreadySpawned(object gm) => FindPotterNpc(gm) != null;

        private static object FindPotterNpc(object gm)
        {
            if (Reflect.IsAlive(_liveNpc) && ReferenceEquals(Reflect.GetMember(_liveNpc, "NPCModel"), _agent))
                return _liveNpc;

            if (Reflect.GetMember(gm, "AllNPCs") is not IEnumerable allNpcs)
            {
                WarnOnce("allnpcs", "GameManager.AllNPCs did not resolve, so the Potter cannot be found.");
                return null;
            }
            foreach (var npc in allNpcs)
            {
                if (npc == null) continue;
                if (ReferenceEquals(Reflect.GetMember(npc, "NPCModel"), _agent))
                {
                    _liveNpc = npc;
                    return npc;
                }
            }
            _liveNpc = null;
            return null;
        }

        // CreateNPC is idempotent (it no-ops when AllNPCs already holds this agent); the second and
        // third parameters are unused inside its body, as in ProfessorSchedulePatch.SpawnAndReturn.
        private static object SpawnAndReturn(object gm)
        {
            _createNpcMethod.Invoke(gm, new object[] { _agent, null, false });
            _liveNpc = null;
            var npc = FindPotterNpc(gm);
            if (npc == null)
                WarnOnce("spawn-lookup", "CreateNPC ran but AllNPCs holds no Potter afterwards, so the spawn failed.");
            return npc;
        }

        private static void RestoreOnLoad_Postfix(object __instance)
        {
            try
            {
                // A new run: drop every session cache so a different save re-derives them.
                _liveNpc = null;
                _lastPortrait = 0;
                _lastProduceDay = int.MinValue;

                if (!ResolveRefs()) return;
                if (AlreadySpawned(__instance)) return;

                var saveData = Reflect.GetMember(__instance, "CurrentSaveData");
                if (saveData == null) return;
                var npcSaveData = _getNpcDataMethod.Invoke(saveData, new object[] { _agent });
                if (npcSaveData == null) return; // never moved in yet: the arrival check places him

                var npc = SpawnAndReturn(__instance);
                if (npc == null) return;
                _initMethod.Invoke(npc, new object[] { npcSaveData, _envIdEmpty });
                Plugin.Logger.LogInfo("[PotterSchedulePatch] Potter restored from save.");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[PotterSchedulePatch] RestoreOnLoad_Postfix failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void CheckAndSpawnOnArrival()
        {
            try
            {
                if (!ResolveTypes()) return;
                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;
                if (!VillageClock.PhaseAtLeast(1f)) return; // he arrives once the village is introduced

                var currentEnv = Reflect.GetMember(gm, "CurrentEnvironment");
                if (currentEnv == null || Reflect.GetMember(currentEnv, "IsNull") is true) return;
                if (!ResolveRefs()) return;
                if (!ReferenceEquals(Reflect.GetMember(currentEnv, "EnvCard"), _innInterior)) return;
                if (AlreadySpawned(gm)) return;

                var npc = SpawnAndReturn(gm);
                if (npc == null) return;
                _initMethod.Invoke(npc, new object[] { null, currentEnv });
                // CreateNPC and Init build only the InGameNPC; vanilla gives an NPC its board card only
                // from the boot coroutine, so a mid-session spawn must ask for it here.
                _assignOrCreateCardsMethod.Invoke(gm, null);
                Plugin.Logger.LogInfo("[PotterSchedulePatch] The Potter has taken a room at the Inn.");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[PotterSchedulePatch] CheckAndSpawnOnArrival failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // -- Schedule --------------------------------------------------------------------------

        private static void RunScheduler()
        {
            try
            {
                if (!ResolveTypes()) return;
                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;
                if (!ResolveRefs()) return;

                var npc = FindPotterNpc(gm);
                if (npc == null) return; // not moved in yet

                var associatedCard = Reflect.GetMember(npc, "AssociatedCard");
                if (associatedCard == null)
                {
                    // The card is rebuilt on every environment change, so a 1 s poll can land in the gap and
                    // the next one finds it (harness T2.968, 2026-10-10: one miss on entering the Inn). Not
                    // ready is not broken: warn only when it stays missing for a whole sweep of polls.
                    if (++_missingCardPolls >= MissingCardPollsBeforeWarn)
                        WarnOnce("associated-card", $"the Potter has had no AssociatedCard for {_missingCardPolls} polls, so he cannot be stocked or shown.");
                    else
                        Plugin.Logger.LogDebug("[PotterSchedulePatch] no AssociatedCard yet; retrying next poll.");
                    return;
                }
                _missingCardPolls = 0;

                bool wantsStall = WantsStall();
                object destCard = wantsStall ? _villageCard : _innInterior;
                if (!IsAt(npc, destCard) && !SharesPlayerEnv(npc))
                    MoveNpcTo(gm, npc, EnvIdOf(destCard));

                if (IsAt(npc, _innInterior)) StockSatchel(npc, associatedCard);

                // After the move, so a move this tick shows its picture this tick (MoveNPC is synchronous).
                SyncPortrait(npc);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[PotterSchedulePatch] RunScheduler failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        /// <summary>True while it is the village market day, inside the market hours, and the village is not
        /// a Quiet Village. The market day is G2's cmcStatMarketDay (96 on the first day of summer, draining 1
        /// a tick); an unreadable stat reads -1 and so is never a market day.</summary>
        private static bool WantsStall()
        {
            if (QuietVillagePerkPatch.IsActive()) return false;
            if (!IsMarketDay()) return false;
            float hour = GameQuery.HourOfDay;
            return hour >= StallOpenHour && hour < StallCloseHour;
        }

        private static bool IsMarketDay() => HiddenStat.Get(VillageSeasonsPatch.MarketDayStatUid) > 0.5f;

        private static void StockSatchel(object npc, object associatedCard)
        {
            int today = GameQuery.CurrentDay;
            if (_lastProduceDay == today) return;
            if (Inventory.Cards(associatedCard).Count >= SatchelCap) return; // full: try again later today
            if (FireAgentAction(npc, associatedCard, ProduceActionId)) _lastProduceDay = today;
        }

        /// <summary>Where he is right now, as the CSV key of one sentence for his Notice Board, or null when he
        /// has not moved in or this cannot be read. Read-only status: it reads where he ACTUALLY stands and
        /// never moves him.</summary>
        internal static string CurrentLegLabel
        {
            get
            {
                try
                {
                    if (!ResolveTypes() || !ResolveRefs()) return null;
                    var gm = CardUtil.GetGameManagerInstance();
                    if (gm == null) return null;
                    var npc = FindPotterNpc(gm);
                    if (npc == null) return null;
                    if (IsAt(npc, _innInterior)) return LegInnKey;
                    if (IsAt(npc, _villageCard)) return LegStallKey;
                    return null; // somewhere unexpected: no line is better than a wrong one
                }
                catch (Exception ex)
                {
                    WarnOnce("leg", $"CurrentLegLabel failed, so his notice board shows no location line: {ex.InnerException?.ToString() ?? ex.ToString()}");
                    return null;
                }
            }
        }

        // -- The board backfill for a Hall generated before he existed --------------------------

        private static void BackfillBoard()
        {
            try
            {
                if (CardUtil.GetGameManagerInstance() == null) return;
                if (!string.Equals(GameQuery.CurrentEnvironmentUniqueId, HallInteriorUid, StringComparison.Ordinal)) return;
                if (GameQuery.IsTransitioning) return;

                var cards = GameQuery.CardsInPlayerEnv();
                if (cards == null || cards.Count == 0) return; // not ready
                bool hallLoaded = false;
                int boards = 0;
                foreach (var card in cards)
                {
                    string uid = CardUtil.GetCardUniqueId(card);
                    if (string.Equals(uid, HallExitUid, StringComparison.Ordinal)) hallLoaded = true;
                    else if (string.Equals(uid, BoardUid, StringComparison.Ordinal)) boards++;
                }
                if (!hallLoaded || boards > 0) return; // not ready, or already there

                SpawnService.Spawn(BoardUid);
                Plugin.Logger.LogInfo("[PotterSchedulePatch] The Potter's board was put up in the Village Hall.");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[PotterSchedulePatch] BackfillBoard failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // -- Location helpers ------------------------------------------------------------------

        private static bool IsAt(object npc, object envCard)
        {
            var env = Reflect.GetMember(npc, "CurrentEnvironment");
            if (env == null || Reflect.GetMember(env, "IsNull") is true) return false;
            return ReferenceEquals(Reflect.GetMember(env, "EnvCard"), envCard);
        }

        private static bool SharesPlayerEnv(object npc)
        {
            var env = Reflect.GetMember(npc, "CurrentEnvironment");
            if (env == null) return false;
            return Reflect.GetMember(env, "MatchesPlayerEnv") is true;
        }

        // Built at the moment of use, never cached (see the class doc).
        private static object EnvIdOf(object cardData)
        {
            if (cardData == null) return null;
            try { return _envIdFromCardCtor.Invoke(new object[] { cardData, false }); }
            catch (Exception ex)
            {
                WarnOnce("envid", $"building an EnvID failed, so the Potter cannot move: {ex.InnerException?.ToString() ?? ex.ToString()}");
                return null;
            }
        }

        private static void MoveNpcTo(object gm, object npc, object envId)
        {
            if (envId == null) return;
            try { _moveNpcMethod.Invoke(gm, new object[] { npc, envId }); }
            catch (Exception ex)
            {
                WarnOnce("move", $"MoveNPC failed, so the Potter stays where he is: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // -- Portrait --------------------------------------------------------------------------

        private static void SyncPortrait(object npc)
        {
            if (_setCardImageMethod == null)
            {
                WarnOnce("set-card-image", "CardData.SetCardImage not found, so the Potter's card keeps one picture.");
                return;
            }

            int portrait;
            if (IsAt(npc, _innInterior)) portrait = PortraitInn;
            else if (IsAt(npc, _villageCard)) portrait = PortraitStall;
            else return; // somewhere unexpected: leave the current art alone
            if (portrait == _lastPortrait) return;

            var sprite = ResolvePortraitSprite(portrait);
            if (sprite == null) return;

            Reflect.SetMember(_agent, "AgentImage", sprite);
            ApplyCardImage(Reflect.GetMember(npc, "ModelCard"), sprite);
            RefreshCardVisuals(Reflect.GetMember(npc, "AssociatedCard"));
            _lastPortrait = portrait;
        }

        private static Sprite ResolvePortraitSprite(int portrait)
        {
            var sprite = GameContent.Find<Sprite>(portrait == PortraitStall ? StallSpriteName : InnSpriteName);
            if (sprite == null && portrait == PortraitStall)
                sprite = GameContent.Find<Sprite>(InnSpriteName); // the Stall picture is not shipped yet
            return sprite;
        }

        private static void ApplyCardImage(object cardData, Sprite sprite)
        {
            if (cardData == null) return;
            try { _setCardImageMethod.Invoke(cardData, new object[] { sprite }); }
            catch (Exception ex)
            {
                WarnOnce("apply-image", $"ApplyCardImage failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void RefreshCardVisuals(object inGameCard)
        {
            if (inGameCard == null) return;
            var visuals = Reflect.GetMember(inGameCard, "CardVisuals");
            if (visuals == null) return;
            _refreshVisualsMethod ??= visuals.GetType().GetMethod("RefreshCookingStatus",
                BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            try { _refreshVisualsMethod?.Invoke(visuals, null); }
            catch (Exception ex)
            {
                // A destroyed card throws here; the next real render reads the new image anyway.
                Plugin.Logger.LogDebug($"[PotterSchedulePatch] RefreshCardVisuals swallowed (expected on a destroyed card): {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // -- Stock -----------------------------------------------------------------------------

        // Fires an AgentAction by ID with his own card as the receiving container, the path that routes
        // DropCardsInsideInventory into an NPC's satchel (ProfessorSchedulePatch.FireAgentAction). Native
        // CheckForActions never fires it (RepeatOptions 3, only when triggered), so it cannot double-fire.
        private static bool FireAgentAction(object npc, object associatedCard, string actionId)
        {
            if (_toActionMethod == null || _performActionMethod == null || _inGameNpcOrPlayerCtor == null)
            {
                WarnOnce("perform-action", "PerformAction reflection unavailable, so the Potter never restocks.");
                return false;
            }
            if (Reflect.GetMember(_agent, "AgentActions") is not Array agentActions)
            {
                WarnOnce("agent-actions", "NPCAgent.AgentActions did not resolve, so the Potter never restocks.");
                return false;
            }
            object matched = null;
            foreach (var action in agentActions)
            {
                if (action != null && Reflect.GetMember(action, "ActionID") as string == actionId) { matched = action; break; }
            }
            if (matched == null)
            {
                WarnOnce("action-" + actionId, $"no AgentAction '{actionId}' on the Potter, so he never restocks.");
                return false;
            }
            try
            {
                var cardAction = _toActionMethod.Invoke(matched, new object[] { npc, associatedCard });
                var user = _inGameNpcOrPlayerCtor.Invoke(new object[] { npc });
                _performActionMethod.Invoke(null, new object[] { cardAction, associatedCard, true, user });
                Plugin.Logger.LogDebug($"[PotterSchedulePatch] Fired '{actionId}'.");
                return true;
            }
            catch (Exception ex)
            {
                WarnOnce("fire-" + actionId, $"FireAgentAction('{actionId}') failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
                return false;
            }
        }
    }
}
