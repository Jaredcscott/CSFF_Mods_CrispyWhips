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
    /// The Keeper is never registered in WorldSettings.NPCAgents (confirmed the ONLY reader of
    /// that array — .decomp/GameManager.cs + .decomp/WorldSettings.cs). Instead:
    ///   - Boot (InitializeStatsAndActions postfix): if the Keeper already has NPC save data
    ///     (a prior session placed it), restore it directly — InGameNPC.Init's _FromData path
    ///     rebuilds CurrentEnvironment from the SAVED STRING KEY (the EnvID(string) ctor).
    ///   - Polled arrival check (TickEvents, not an Update loop): the first time the player's
    ///     own CurrentEnvironment matches cmcInnInterior, spawn the Keeper there reusing that
    ///     EnvID verbatim.
    /// This predates the 1.44.x instancing fix, when cmcInnInterior was InstancedEnvironment:true
    /// and vanilla's boot loop / LoadNPC single-arg `EnvID(CardData)` ctor genuinely could not
    /// place an NPC there (.decomp/EnvID.cs — that ctor rejects instanced cards outright). The
    /// interior is InstancedEnvironment:false now, so that limitation is gone, but the mechanism
    /// is kept: it is proven, and it is also what places the Keeper mid-session on first arrival.
    ///
    /// ENVIRONMENT RECONCILIATION (1.46.2). An NPC's environment persists as a STRING KEY, and
    /// for an instanced env that key carries the parent chain ("cmcInnInterior_cmcEnvVillage").
    /// Flipping cmcInnInterior to InstancedEnvironment:false in 1.44.x changed the key the player
    /// travels into to the bare "cmcInnInterior" — but did NOT rewrite keys already sitting in
    /// saves. A Keeper placed before that flip therefore restores into an orphaned env instance
    /// the player can no longer reach, AssignOrCreateNPCCards dutifully builds his board card
    /// THERE, and he is invisible forever — with zero errors logged. He was the only village NPC
    /// hit: the Professor/Apothecary/residents all get moved by their own schedulers, which
    /// rebuild the EnvID from the card each time and so self-corrected; the Keeper never moves.
    /// Both entry points below now reconcile his CurrentEnvironment against the canonical target
    /// (via vanilla GameManager.MoveNPC, which also relocates an already-created board card),
    /// which repairs affected saves on load and is inert once the keys agree.
    ///
    /// BOARD PRESENCE (1.46.4). That reconcile was necessary but not sufficient: an InGameNPC and
    /// its board card hold SEPARATE EnvIDs, and correcting only the NPC's leaves a save where the
    /// two disagree permanently — the reconcile then sees a matching NPC env and stands down every
    /// load while the card stays in the unreachable env. EnsureKeeperPresent below closes that by
    /// asserting the CARD's env (and its existence) whenever the player is standing in the Inn.
    /// </summary>
    internal static class InnKeeperSpawnPatch
    {
        private const string InnKeeperAgentUid = "cmcInnKeeperAgent";
        private const string InnInteriorUid = "cmcInnInterior";
        private const string RestockActionId = "InnKeeperMorningRestock";
        private const int RestockIntervalDays = 4;

        private static bool _initialized;
        private static bool _missingRefsWarned;
        private static string _lastLoggedEnvUid = "(unset)";

        // Board-presence check state (see EnsureKeeperPresent). The signature both suppresses
        // duplicate log lines on a 1s poll and gates repair attempts, so a stable-but-healthy
        // Keeper costs two reflection reads per second and nothing else.
        private static string _lastPresenceSignature;
        private static int _presenceRepairAttempts;
        private static bool _presenceRepairCapWarned;
        private const int MaxPresenceRepairAttempts = 5;

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
        private static Type _cardDataType;          // CardData (EnvID's ctor param)
        private static MethodInfo _getFromIdMethod; // UniqueIDScriptable.GetFromID(string)
        private static MethodInfo _createNpcMethod;  // GameManager.CreateNPC(NPCAgent, SavedNPCCharacterData, bool) — EA 0.66+ (was (NPCAgent) only pre-0.66)
        private static MethodInfo _initMethod;       // InGameNPC.Init(NPCAgentSaveDataByReference, EnvID)
        private static MethodInfo _getNpcDataMethod; // GameSaveDataByReference.GetNPCData(NPCAgent) -> NPCAgentSaveDataByReference
        private static MethodInfo _assignOrCreateCardsMethod; // GameManager.AssignOrCreateNPCCards() (private, no args)
        private static MethodInfo _toActionMethod;   // NPCAction.ToAction(InGameNPC, InGameCardBase) -> CardAction
        private static MethodInfo _performActionMethod; // GameManager.PerformAction(CardAction, InGameCardBase, bool, InGameNPCOrPlayer) (static)
        private static MethodInfo _moveNpcMethod;    // GameManager.MoveNPC(InGameNPC, EnvID) — relocates the NPC *and* its board card
        private static MethodInfo _matchesEnvMethod; // EnvID.MatchesEnv(EnvID) — full key compare, parent chain included
        private static MethodInfo _updatePlayerEnvMethod; // InGameCardBase.UpdatePlayerEnvironment(EnvID) — optional
        private static MethodInfo _updateVisibilityMethod; // InGameCardBase.UpdateVisibility() — optional
        private static ConstructorInfo _inGameNpcOrPlayerCtor; // InGameNPCOrPlayer(InGameNPC)
        private static ConstructorInfo _envIdFromCardCtor;     // EnvID(CardData, bool _IgnoreInstancedEnv)
        private static object _envIdEmpty;           // default(EnvID)
        private static object _canonicalInnEnv;      // EnvID(cmcInnInterior) — resolved once refs exist

        // Restock cadence state (in-memory; resets on process relaunch — acceptable for a
        // shop-stock timer, and self-heals an empty pantry immediately regardless).
        private static int _lastRestockDay = int.MinValue;
        private static bool _missingRestockActionWarned;

        // His Copper Chest config, resolved once from CopperChestPatch (the single owner of every
        // chest's UIDs, action IDs and caps). Its own accrual cadence/day-stamp is persistent and
        // lives in a GameStat, unlike _lastRestockDay above which is in-memory by design.
        private static CopperChestPatch.ChestConfig _chestConfig;

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
            _cardDataType = CardUtil.FindGameType("CardData");
            var uidType = CardUtil.FindGameType("UniqueIDScriptable");

            if (_gmType == null || _npcAgentType == null || _envIdType == null
                || _inGameNpcType == null || _saveDataByRefType == null || _gameSaveDataByRefType == null || uidType == null
                || _npcActionType == null || _inGameCardBaseType == null || _cardActionType == null || _inGameNpcOrPlayerType == null
                || _savedNpcCharacterDataType == null || _cardDataType == null)
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
            _moveNpcMethod = AccessTools.Method(_gmType, "MoveNPC", new[] { _inGameNpcType, _envIdType });
            _matchesEnvMethod = AccessTools.Method(_envIdType, "MatchesEnv", new[] { _envIdType });
            // Optional (not part of the ResolveTypes success contract) — used only to nudge a
            // repaired board card into refreshing itself; MoveNPC/AssignOrCreateNPCCards already
            // do this internally, so a signature change here degrades to a no-op, not a failure.
            _updatePlayerEnvMethod = AccessTools.Method(_inGameCardBaseType, "UpdatePlayerEnvironment", new[] { _envIdType });
            _updateVisibilityMethod = AccessTools.Method(_inGameCardBaseType, "UpdateVisibility", Type.EmptyTypes);
            _inGameNpcOrPlayerCtor = _inGameNpcOrPlayerType.GetConstructor(new[] { _inGameNpcType });
            _envIdFromCardCtor = _envIdType.GetConstructor(new[] { _cardDataType, typeof(bool) });
            _envIdEmpty = Activator.CreateInstance(_envIdType);

            return _getFromIdMethod != null && _createNpcMethod != null && _initMethod != null
                && _getNpcDataMethod != null && _assignOrCreateCardsMethod != null
                && _toActionMethod != null && _performActionMethod != null && _inGameNpcOrPlayerCtor != null
                && _moveNpcMethod != null && _matchesEnvMethod != null && _envIdFromCardCtor != null;
        }

        private static bool ResolveRefs()
        {
            if (_agent != null && _innInterior != null) return true;
            _agent ??= _getFromIdMethod.Invoke(null, new object[] { InnKeeperAgentUid });
            _innInterior ??= CardUtil.GetCardDataById(InnInteriorUid);
            return _agent != null && _innInterior != null;
        }

        // True if `model` (an NPCAgent instance) is our Inn Keeper. Checks both object identity
        // AND UniqueID string — a mod-loading path that ends up with two distinct NPCAgent
        // instances sharing the same UID (e.g. a third-party loader duplicating UniqueIDScriptable
        // registrations before the framework's ForeignInstanceReconciler runs) would make a
        // reference-only check silently and permanently fail to recognize an already-spawned or
        // newly-spawned Keeper. See root CLAUDE.md § Pikachu ModLoader Coexistence.
        private static bool IsInnKeeperAgent(object model) =>
            model != null && (ReferenceEquals(model, _agent) || CardUtil.GetCardUniqueId(model) == InnKeeperAgentUid);

        // Returns the live InGameNPC for our agent, or null if it hasn't been created yet.
        private static object FindLiveNpc(object gm)
        {
            if (Reflect.GetMember(gm, "AllNPCs") is not IEnumerable allNpcs) return null;
            foreach (var npc in allNpcs)
            {
                if (npc == null) continue;
                if (IsInnKeeperAgent(Reflect.GetMember(npc, "NPCModel"))) return npc;
            }
            return null;
        }

        private static bool AlreadySpawned(object gm) => FindLiveNpc(gm) != null;

        // The env the Keeper belongs in, built from the interior card itself rather than from
        // whatever string a past session serialized. cmcInnInterior is InstancedEnvironment:false,
        // so the single-card ctor yields the same bare key the player travels into; if that ever
        // flips back to true the ctor logs and returns a null MainEnvCard, which IsNull catches
        // here and reconciliation simply stands down (the arrival path still uses the player's
        // live EnvID, which is correct for an instanced env).
        private static object CanonicalInnEnv()
        {
            if (_canonicalInnEnv != null) return _canonicalInnEnv;
            var env = _envIdFromCardCtor.Invoke(new object[] { _innInterior, false });
            if (env == null || Reflect.GetMember(env, "IsNull") is true) return null;
            _canonicalInnEnv = env;
            return _canonicalInnEnv;
        }

        // Moves the Keeper to <paramref name="targetEnv"/> if he isn't already there, and reports
        // whether it moved him. MatchesEnv compares the full dictionary key (parent chain
        // included), so a legacy instanced key does NOT compare equal to the bare one — which is
        // exactly the stale-save case this exists for. GameManager.MoveNPC is the vanilla mover:
        // it reseats CurrentEnvironment, refreshes distance-to-player, and re-parents an
        // already-created AssociatedCard into the new env (the same call ProfessorSchedulePatch
        // uses for his daily commute). Inert once the keys agree, so it is safe to poll.
        private static bool ReconcileEnvironment(object gm, object npc, object targetEnv, string context)
        {
            if (targetEnv == null) return false;
            var npcEnv = Reflect.GetMember(npc, "CurrentEnvironment");
            if (npcEnv != null && _matchesEnvMethod.Invoke(npcEnv, new[] { targetEnv }) is true) return false;

            _moveNpcMethod.Invoke(gm, new[] { npc, targetEnv });
            Plugin.Logger.LogInfo($"[InnKeeperSpawnPatch] Inn Keeper's environment reconciled ({context}): '{EnvLabel(npcEnv)}' -> '{EnvLabel(targetEnv)}'.");
            return true;
        }

        // Unity-aware null test for a reflected UnityEngine.Object. A DESTROYED card still comes
        // back from reflection as a non-null boxed reference — Unity's null-masking `==` overload
        // only applies when the static type is UnityEngine.Object, which it never is here. Vanilla
        // tests `if (!npc.AssociatedCard)` (typed, so masked); a bare `!= null` on the reflected
        // value disagrees with vanilla about whether the card exists. See root CLAUDE.md
        // § Harmony Patching Pitfalls.
        private static object Alive(object maybeUnityObject)
        {
            if (maybeUnityObject is UnityEngine.Object uo) return uo == null ? null : maybeUnityObject;
            return maybeUnityObject;
        }

        private static bool EnvMatches(object env, object target) =>
            env != null && target != null && _matchesEnvMethod.Invoke(env, new[] { target }) is true;

        // BOARD-PRESENCE BACKSTOP (1.46.4). ReconcileEnvironment above fixes the NPC's own
        // CurrentEnvironment, but an InGameNPC and its board card carry SEPARATE environments, and
        // the two can diverge permanently:
        //
        //   * GameManager.MoveNPC early-returns when the target already matches the NPC's own env
        //     (.decomp/GameManager.cs), and only re-parents the board card INSIDE that guarded
        //     block. A session that reconciled the NPC while his AssociatedCard was still null —
        //     exactly what the boot-time reconcile does, since vanilla's AssignOrCreateNPCCards
        //     runs later in the same coroutine — corrects and SAVES the NPC's env while leaving the
        //     card wherever it was. On every later load the two keys disagree, ReconcileEnvironment
        //     sees a matching NPC env and stands down, and the Keeper is invisible forever with no
        //     error logged and no self-heal path.
        //   * AssignOrCreateNPCCards only creates a missing card when !IsInitializing
        //     (.decomp/GameManager.cs), so an NPC whose saved card failed to load can end up with
        //     no card at all if nothing calls it again after boot.
        //
        // The Keeper never leaves the Inn, so the player standing in cmcInnInterior means this env
        // is his by definition — which makes it safe to assert both his env AND his card's env
        // against it here and repair either. Blanking CurrentEnvironment before MoveNPC is what
        // gets past vanilla's early-return so the card relocation actually runs.
        private static void EnsureKeeperPresent(object gm, object npc, object playerEnv)
        {
            var npcEnv = Reflect.GetMember(npc, "CurrentEnvironment");
            var card = Alive(Reflect.GetMember(npc, "AssociatedCard"));
            var cardEnv = card == null ? null : Reflect.GetMember(card, "CardEnvironment");

            bool npcEnvOk = EnvMatches(npcEnv, playerEnv);
            bool cardEnvOk = card != null && EnvMatches(cardEnv, playerEnv);

            string signature = $"npcEnv='{EnvLabel(npcEnv)}' card={(card == null ? "MISSING" : "present")} cardEnv='{(card == null ? "n/a" : EnvLabel(cardEnv))}'";
            bool signatureChanged = signature != _lastPresenceSignature;
            if (signatureChanged)
            {
                _lastPresenceSignature = signature;
                Plugin.Logger.LogDebug($"[InnKeeperSpawnPatch] presence check (player in the Inn, env='{EnvLabel(playerEnv)}'): {signature}");
            }

            if (npcEnvOk && cardEnvOk) return; // he is here, with a card, in this env — nothing to do

            // Only act on a state we haven't already tried to repair, and give up after a few
            // rounds rather than thrashing MoveNPC/AssignOrCreateNPCCards once a second forever.
            if (!signatureChanged) return;
            if (_presenceRepairAttempts >= MaxPresenceRepairAttempts)
            {
                if (!_presenceRepairCapWarned)
                {
                    _presenceRepairCapWarned = true;
                    Plugin.Logger.LogWarning($"[InnKeeperSpawnPatch] gave up repairing the Inn Keeper's board placement after {MaxPresenceRepairAttempts} attempts — last state: {signature}");
                }
                return;
            }
            _presenceRepairAttempts++;

            if (!npcEnvOk || (card != null && !cardEnvOk))
            {
                Reflect.SetMember(npc, "CurrentEnvironment", _envIdEmpty);
                _moveNpcMethod.Invoke(gm, new[] { npc, playerEnv });
                Plugin.Logger.LogInfo($"[InnKeeperSpawnPatch] repaired the Inn Keeper's placement -> '{EnvLabel(playerEnv)}' (was {signature}).");
            }

            if (card == null)
            {
                // Mid-session, so IsInitializing is false and this really does create the card.
                _assignOrCreateCardsMethod.Invoke(gm, null);
                Plugin.Logger.LogInfo("[InnKeeperSpawnPatch] Inn Keeper had no board card — requested one via AssignOrCreateNPCCards.");
            }
            else
            {
                _updatePlayerEnvMethod?.Invoke(card, new[] { playerEnv });
                _updateVisibilityMethod?.Invoke(card, null);
            }
        }

        // Full dictionary key (parent chain included) for logging — the whole point of these
        // messages is telling 'cmcInnInterior' apart from 'cmcInnInterior_cmcEnvVillage', so the
        // EnvCard UID alone would hide the very difference being reported.
        private static string EnvLabel(object env)
        {
            if (env == null) return "(null)";
            return Reflect.GetMember(env, "StringDictionnaryKey") as string
                ?? CardUtil.GetCardUniqueId(Reflect.GetMember(env, "EnvCard"))
                ?? "(unknown)";
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
                Plugin.Logger.LogWarning("[InnKeeperSpawnPatch] SpawnAndReturn: CreateNPC invoked but GameManager.AllNPCs did not resolve — spawn cannot be confirmed.");
                return null;
            }
            object last = null;
            foreach (var npc in allNpcs)
                if (IsInnKeeperAgent(Reflect.GetMember(npc, "NPCModel")))
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

                // Init has just rebuilt CurrentEnvironment from the saved string key, which on a
                // pre-1.44.x save is the orphaned instanced key. Correct it HERE, before vanilla's
                // own AssignOrCreateNPCCards runs later in the same boot coroutine — otherwise his
                // board card is built into the unreachable env and only a later Inn visit fixes it.
                ReconcileEnvironment(__instance, npc, CanonicalInnEnv(), "save restore");

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

                if (!ResolveRefs())
                {
                    if (!_missingRefsWarned)
                    {
                        _missingRefsWarned = true;
                        Plugin.Logger.LogWarning($"[InnKeeperSpawnPatch] ResolveRefs failed (agent null={_agent == null}, innInterior null={_innInterior == null}) — '{InnKeeperAgentUid}'/'{InnInteriorUid}' did not resolve via GetFromID; Inn Keeper can never spawn until this succeeds.");
                    }
                    return;
                }

                var envCard = Reflect.GetMember(currentEnv, "EnvCard");
                string envUid = CardUtil.GetCardUniqueId(envCard);
                if (envUid != _lastLoggedEnvUid)
                {
                    _lastLoggedEnvUid = envUid;
                    Plugin.Logger.LogDebug($"[InnKeeperSpawnPatch] player env changed -> '{envUid ?? "(null)"}' (target='{InnInteriorUid}', refMatch={ReferenceEquals(envCard, _innInterior)})");
                }
                // Match by UID, not object reference — a duplicated CardData instance for the
                // same UID (see IsInnKeeperAgent above) would otherwise make this permanently and
                // silently false even while the player is standing inside the Inn.
                if (envUid != InnInteriorUid) return;

                // Already placed: the only thing left to check is that he's placed in the env the
                // player is actually standing in. The Keeper never leaves the Inn, so the player
                // being here means this EnvID is his by definition — which makes this the backstop
                // that repairs a stale saved env key even if the boot-time reconcile above didn't
                // run (mod added mid-save, refs not resolved yet at boot, key stale for any other
                // reason). MoveNPC carries his existing board card over; AssignOrCreateNPCCards
                // covers the case where he never got one. Only fires on an actual mismatch, so
                // this is not a per-second cost.
                var existing = FindLiveNpc(gm);
                if (existing != null)
                {
                    if (ReconcileEnvironment(gm, existing, currentEnv, "player arrival"))
                        _assignOrCreateCardsMethod.Invoke(gm, null);
                    // ReconcileEnvironment only inspects the NPC's own env, which can agree with
                    // the player's while his board card sits in a different one (or doesn't exist
                    // at all) — the actual "he is nowhere to be seen" state. Assert both.
                    EnsureKeeperPresent(gm, existing, currentEnv);
                    return;
                }

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

                // Copper Chest accrual (Village_Master_Plan.md §10.8.3.3) — a genuinely NEW weekly
                // tick, deliberately independent of the 4-day pantry restock below: the pantry is
                // the Inn's TRADE STOCK and the chest is the Keeper's PERSONAL savings, two
                // different pools on two different cadences (and neither is cmcInnCounter's till,
                // which InnPatch owns and this file never touches). Evaluated before the pantry's
                // own due-check so a not-yet-due pantry can't skip the chest.
                //
                // CopperChestPatch.TryAccrue owns the env gate (no-ops unless the player is
                // standing in cmcInnInterior), the persistent due-day stamp and both caps; this
                // file contributes only its own already-proven FireAgentAction. This is the single
                // caller for the Inn Keeper's chest — a second one anywhere would be R6's
                // double-drop.
                _chestConfig ??= CopperChestPatch.ForAgent(InnKeeperAgentUid);
                if (_chestConfig != null)
                    CopperChestPatch.TryAccrue(_chestConfig, npc, currentDay, FireAgentAction);

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
