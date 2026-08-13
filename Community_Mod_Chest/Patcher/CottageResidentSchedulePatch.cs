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
    /// Miller/Weaver daily schedule — added on top of the existing CottageResidentSpawnPatch
    /// (which still owns their move-in timer, spawn, reload restore, and weekly trade restock).
    /// This patch owns everything that happens AFTER a resident has moved in: where they stand
    /// through the day/night cycle.
    ///
    /// Mechanism, per resident, entirely a deterministic function of (CurrentDay, GameQuery.HourOfDay)
    /// — no persisted state at all, so it re-derives correctly after any reload (same idiom as
    /// ApothecarySchedulePatch's clock-driven commute):
    ///
    ///   - Night [22:00, 6:00): home, inside their own cottage (cmcMillerCottageInterior /
    ///     cmcWeaverCottageInterior — new lightweight instanced interiors, structurally identical
    ///     to the Apothecary's cabin interior: CT2 "Enter the Cottage" DA -> CT4 instanced env ->
    ///     CT8 backdrop + CT2 exit door). Moving a resident into an instanced env needs a live
    ///     EnvID captured from an actual player visit first (same prerequisite Professor/Apothecary
    ///     already have for the Academy/Inn/Cabin) — until the player has entered that resident's
    ///     cottage at least once this session, they simply stand outside at the Village overnight
    ///     instead of vanishing (graceful degradation, not a broken state).
    ///   - Every evening [18:00, 21:00): the Inn — guaranteed daily, not a roll (village-life ask:
    ///     "all NPCs spend time in the Inn in the evenings").
    ///   - Exactly one day per week (a fixed, per-resident day-of-week so Miller and Weaver don't
    ///     both show up the same day): an afternoon window [13:00, 17:00) at the Academy.
    ///   - Remaining daytime hours on non-Academy days: a per-resident, per-day deterministic hash
    ///     (day + a fixed per-resident seed, no Unity Random involved) occasionally sends them
    ///     wandering [10:00, 16:00) to one of the outdoor map nodes (Village Path/Farm, Foraging
    ///     Forest, Pine Trail, Highland Pines, Moss-Grown Clearing) instead of standing at the Village.
    ///   Same EnvID-capture prerequisite applies to the Inn/Academy legs; before capture the
    ///   resident just stays at the Village.
    ///
    /// Portrait sync (added alongside the guaranteed Inn/Academy schedule): each resident's card
    /// art follows Village (default) / Inn / Academy the same way ProfessorSchedulePatch's and
    /// ApothecarySchedulePatch's SyncPortrait do — derived from the NPC's ACTUAL CurrentEnvironment,
    /// not the scheduled destination, so it never contradicts where they're really standing.
    ///
    /// Never moves a resident out from under a player standing with them (SharesPlayerEnv guard,
    /// same as every other village NPC scheduler). Never creates cards — restock stays owned by
    /// CottageResidentSpawnPatch, so the two patches cannot race over anything.
    /// </summary>
    internal static class CottageResidentSchedulePatch
    {
        private const string VillageEnvUid = "cmcEnvVillage";
        private const string AcademyInteriorUid = "cmcAcademyInterior";
        private const string InnInteriorUid = "cmcInnInterior";

        // Shared outdoor map nodes a wandering resident may visit (the Village itself is home,
        // not a wander destination).
        private static readonly string[] WanderNodeUids =
        {
            "cmcEnvVillagePath", "cmcEnvVillageFarm", "cmcEnvForagingForest",
            "cmcEnvPineTrail", "cmcEnvHighGrove", "cmcEnvMossyClearing",
        };

        private enum Dest { Village, Cottage, Inn, Academy, Wander }
        private enum Portrait { None = 0, Village = 1, Inn = 2, Academy = 3 }

        private const float NightStartHour = 22f;
        private const float NightEndHour = 6f;
        private const float WanderStartHour = 10f;
        private const float WanderEndHour = 16f;
        private const float InnVisitStartHour = 18f;
        private const float InnVisitEndHour = 21f;
        private const float AcademyVisitStartHour = 13f;
        private const float AcademyVisitEndHour = 17f;
        private const int WanderChancePercent = 40; // chance a non-Academy day includes a wander leg

        private const int TicksPerTile = NpcTileWalker.DefaultTicksPerTile; // 45 min/tile, in DTP ticks

        private sealed class Resident
        {
            public string Name;
            public string AgentUid;
            public string CottageInteriorUid;
            public int Seed; // arbitrary distinct per-resident mixing constant, not a save value
            public int AcademyDayOfWeek; // 0-6 — CurrentDay % 7 == this is their one Academy day
            public string VillageSpriteName;
            public string InnSpriteName;
            public string AcademySpriteName;

            public object Agent;                // NPCAgent, resolved lazily
            public object CottageInteriorCard;   // CardData (own instanced interior)
            public object CottageInteriorEnvId;  // captured live EnvID, or null until visited
            public bool CottageInteriorCaptured;
            public Portrait LastPortrait;        // 0 = not yet synced this run
        }

        private static readonly Resident[] Residents =
        {
            new Resident
            {
                Name = "Miller", AgentUid = "cmcMillerAgent", CottageInteriorUid = "cmcMillerCottageInterior",
                Seed = unchecked((int)0x9E3779B1), AcademyDayOfWeek = 1,
                VillageSpriteName = "CMC_Miller", InnSpriteName = "CMC_Miller_Inn", AcademySpriteName = "CMC_Miller_Academy",
            },
            new Resident
            {
                Name = "Weaver", AgentUid = "cmcWeaverAgent", CottageInteriorUid = "cmcWeaverCottageInterior",
                Seed = unchecked((int)0x517CC1B7), AcademyDayOfWeek = 4,
                VillageSpriteName = "CMC_Weaver", InnSpriteName = "CMC_Weaver_Inn", AcademySpriteName = "CMC_Weaver_Academy",
            },
        };

        private static bool _initialized;

        private static object _villageEnvCard;   // CardData
        private static object[] _wanderNodes;    // CardData[6], parallel to WanderNodeUids
        private static object _academyInteriorCard;
        private static object _innInteriorCard;

        // Instanced-env EnvID capture (shared across both residents — same Academy/Inn everyone
        // else visits).
        private static object _academyEnvId;
        private static object _innEnvId;
        private static bool _academyCaptured;
        private static bool _innCaptured;

        // Reflection handles, resolved once.
        private static Type _gmType;
        private static Type _envIdType;
        private static Type _inGameNpcType;
        private static Type _cardDataType;
        private static MethodInfo _getFromIdMethod; // UniqueIDScriptable.GetFromID(string)
        private static MethodInfo _moveNpcMethod;   // GameManager.MoveNPC(InGameNPC, EnvID)
        private static ConstructorInfo _envIdFromCardCtor; // EnvID(CardData, bool)

        // Portrait application (optional — degrades gracefully if the game's SetCardImage/refresh
        // signatures shift; art just stays static rather than following a resident around).
        private static MethodInfo _setCardImageMethod;   // CardData.SetCardImage(Sprite)
        private static MethodInfo _refreshVisualsMethod; // CardGraphics.RefreshCookingStatus() — resolved lazily (per-instance type)
        private static bool _refreshVisualsFailureLogged;

        public static void Initialize(Harmony harmony)
        {
            if (_initialized) return;
            _initialized = true;

            if (!ResolveTypes())
            {
                Plugin.Logger.LogWarning("[CottageResidentSchedulePatch] Required game types not found — Miller/Weaver daily schedule inactive.");
                return;
            }

            var initStatsMethod = AccessTools.Method(_gmType, "InitializeStatsAndActions");
            if (initStatsMethod != null)
                harmony.Patch(initStatsMethod, postfix: new HarmonyMethod(typeof(CottageResidentSchedulePatch), nameof(ResetSessionState_Postfix)));

            TickEvents.Interval(1f, RunScheduler, "CottageResidentSchedule");
            Plugin.Logger.LogDebug("[CottageResidentSchedulePatch] initialized.");
        }

        // A new run/save may hand out different live instanced-env instances (or none yet) and
        // in-memory capture state doesn't survive a reload.
        private static void ResetSessionState_Postfix()
        {
            _academyCaptured = false;
            _innCaptured = false;
            _academyEnvId = null;
            _innEnvId = null;
            foreach (var resident in Residents)
            {
                resident.CottageInteriorCaptured = false;
                resident.CottageInteriorEnvId = null;
                resident.LastPortrait = Portrait.None;
            }
            NpcTileWalker.ResetAll();
        }

        private static bool ResolveTypes()
        {
            if (_gmType != null) return true;

            _gmType = CardUtil.FindGameType("GameManager");
            _envIdType = CardUtil.FindGameType("EnvID");
            _inGameNpcType = CardUtil.FindGameType("InGameNPC");
            _cardDataType = CardUtil.FindGameType("CardData");
            var uidType = CardUtil.FindGameType("UniqueIDScriptable");

            if (_gmType == null || _envIdType == null || _inGameNpcType == null || _cardDataType == null || uidType == null)
                return false;

            _getFromIdMethod = uidType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "GetFromID" && !m.IsGenericMethodDefinition
                    && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
            _moveNpcMethod = AccessTools.Method(_gmType, "MoveNPC", new[] { _inGameNpcType, _envIdType });
            _envIdFromCardCtor = _envIdType.GetConstructor(new[] { _cardDataType, typeof(bool) });

            // Optional — portrait tracking degrades gracefully if absent.
            _setCardImageMethod = AccessTools.Method(_cardDataType, "SetCardImage", new[] { typeof(Sprite) });

            return _getFromIdMethod != null && _moveNpcMethod != null && _envIdFromCardCtor != null;
        }

        private static bool ResolveRefs()
        {
            _villageEnvCard ??= CardUtil.GetCardDataById(VillageEnvUid);
            _academyInteriorCard ??= CardUtil.GetCardDataById(AcademyInteriorUid);
            _innInteriorCard ??= CardUtil.GetCardDataById(InnInteriorUid);

            if (_wanderNodes == null)
            {
                var nodes = new object[WanderNodeUids.Length];
                bool allResolved = true;
                for (int i = 0; i < WanderNodeUids.Length; i++)
                {
                    nodes[i] = CardUtil.GetCardDataById(WanderNodeUids[i]);
                    if (nodes[i] == null) allResolved = false;
                }
                if (allResolved) _wanderNodes = nodes;
            }

            bool residentsOk = true;
            foreach (var resident in Residents)
            {
                resident.Agent ??= _getFromIdMethod.Invoke(null, new object[] { resident.AgentUid });
                resident.CottageInteriorCard ??= CardUtil.GetCardDataById(resident.CottageInteriorUid);
                if (resident.Agent == null || resident.CottageInteriorCard == null) residentsOk = false;
            }

            return _villageEnvCard != null && _academyInteriorCard != null && _innInteriorCard != null
                && _wanderNodes != null && residentsOk;
        }

        private static void RunScheduler()
        {
            try
            {
                if (!ResolveTypes() || !ResolveRefs()) return;

                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;

                CaptureSharedEnvIds(gm);

                float hour = GameQuery.HourOfDay;
                int today = GameQuery.CurrentDay;

                foreach (var resident in Residents)
                {
                    var npc = FindLiveNpc(gm, resident);
                    if (npc == null) continue; // hasn't moved in yet — CottageResidentSpawnPatch owns that

                    CaptureCottageInteriorEnvId(gm, resident, npc);

                    bool isAcademyDay = IsAcademyDay(today, resident);
                    bool isWanderDay = !isAcademyDay && RollWander(today, resident.Seed);
                    int wanderIndex = _wanderNodes.Length > 0 ? Mod(DayHash(today, resident.Seed + 1), _wanderNodes.Length) : 0;
                    var dest = ComputeDestination(hour, isAcademyDay, isWanderDay);

                    // Never move him out from under a mid-visit player — but still let the
                    // portrait reflect wherever he's actually standing.
                    if (!SharesPlayerEnv(npc))
                        RunCommute(gm, npc, resident, dest, wanderIndex);

                    SyncPortrait(npc, resident);
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[CottageResidentSchedulePatch] RunScheduler failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // ── Daily schedule (pure function of the clock + a deterministic per-day roll) ───────

        // Exactly one day per week, per resident — not a roll. Deliberately staggered
        // (Miller/Weaver AcademyDayOfWeek differ) so they don't both show up the same day.
        private static bool IsAcademyDay(int day, Resident resident) => Mod(day, 7) == resident.AcademyDayOfWeek;

        // Flavor-only variety on non-Academy days — never overrides the guaranteed evening Inn
        // visit or the weekly Academy day, both of which are checked first in ComputeDestination.
        private static bool RollWander(int day, int seed) => Mod(DayHash(day, seed), 100) < WanderChancePercent;

        private static Dest ComputeDestination(float hour, bool isAcademyDay, bool isWanderDay)
        {
            bool isNight = hour >= NightStartHour || hour < NightEndHour;
            if (isNight) return Dest.Cottage;

            // Guaranteed every evening, every resident, every day (village-life ask: "all NPCs
            // spend time in the Inn in the evenings") — checked before Academy/Wander so it can
            // never be skipped by either.
            if (hour >= InnVisitStartHour && hour < InnVisitEndHour) return Dest.Inn;

            if (isAcademyDay && hour >= AcademyVisitStartHour && hour < AcademyVisitEndHour) return Dest.Academy;

            if (!isAcademyDay && isWanderDay && hour >= WanderStartHour && hour < WanderEndHour) return Dest.Wander;

            return Dest.Village;
        }

        // Simple deterministic integer hash (Murmur3-style finalizer) — no Unity Random, no
        // persisted state, identical result every time for the same (day, seed) pair.
        private static int DayHash(int day, int seed)
        {
            unchecked
            {
                int h = (day * 397) ^ seed;
                h = (h ^ (int)((uint)h >> 15)) * -862048943; // 0xCC9E2D51 as signed
                h = (h ^ (int)((uint)h >> 13)) * -2048144789; // 0x85EBCA77 as signed
                h ^= (int)((uint)h >> 16);
                return h;
            }
        }

        private static int Mod(int value, int modulus)
        {
            int m = value % modulus;
            return m < 0 ? m + modulus : m;
        }

        private static void RunCommute(object gm, object npc, Resident resident, Dest dest, int wanderIndex)
        {
            switch (dest)
            {
                case Dest.Cottage:
                    if (!resident.CottageInteriorCaptured)
                    {
                        WalkTowards(gm, npc, resident, VillageEnvUid, BuildOutdoorEnvId(_villageEnvCard));
                        return;
                    }
                    // Instanced env — no cheap "already there" comparison, mirrors
                    // ApothecarySchedulePatch's own cabin-interior homing.
                    WalkTowards(gm, npc, resident, VillageEnvUid, resident.CottageInteriorEnvId);
                    return;

                case Dest.Inn:
                    if (!_innCaptured)
                    {
                        WalkTowards(gm, npc, resident, VillageEnvUid, BuildOutdoorEnvId(_villageEnvCard));
                        return;
                    }
                    WalkTowards(gm, npc, resident, VillageEnvUid, _innEnvId);
                    return;

                case Dest.Academy:
                    if (!_academyCaptured)
                    {
                        WalkTowards(gm, npc, resident, VillageEnvUid, BuildOutdoorEnvId(_villageEnvCard));
                        return;
                    }
                    WalkTowards(gm, npc, resident, VillageEnvUid, _academyEnvId);
                    return;

                case Dest.Wander:
                    string wanderUid = WanderNodeUids[wanderIndex];
                    WalkTowards(gm, npc, resident, wanderUid, BuildOutdoorEnvId(_wanderNodes[wanderIndex]));
                    return;

                default: // Dest.Village
                    WalkTowards(gm, npc, resident, VillageEnvUid, BuildOutdoorEnvId(_villageEnvCard));
                    return;
            }
        }

        // The outdoor tile this resident is standing on right now, for tile-walking purposes —
        // one of the wander nodes, or the Village (the shared front door for the Village itself,
        // the Academy, the Inn, and every resident's own cottage).
        private static string CurrentOutdoorUidOf(object npc, Resident resident)
        {
            for (int i = 0; i < WanderNodeUids.Length; i++)
                if (IsAtEnv(npc, _wanderNodes[i])) return WanderNodeUids[i];
            if (IsAtEnv(npc, _villageEnvCard) || IsAtEnv(npc, _academyInteriorCard) || IsAtEnv(npc, _innInteriorCard)
                || IsAtEnv(npc, resident.CottageInteriorCard))
                return VillageEnvUid;
            return null; // unknown / not yet spawned anywhere trackable
        }

        // Walks one outdoor tile per call toward destOutdoorUid, then takes the (possibly
        // interior) finalEnvId hop once that tile is reached — same pattern as
        // ProfessorSchedulePatch.WalkTowards / ApothecarySchedulePatch.WalkTowards.
        private static void WalkTowards(object gm, object npc, Resident resident, string destOutdoorUid, object finalEnvId)
        {
            string currentUid = CurrentOutdoorUidOf(npc, resident);
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
                Plugin.Logger.LogWarning($"[CottageResidentSchedulePatch] WalkTowards: intermediate tile '{nextUid}' has no CardData for {resident.Name} — jumping straight to the destination instead.");
                MoveNpcTo(gm, npc, finalEnvId);
                return;
            }
            MoveNpcTo(gm, npc, BuildOutdoorEnvId(stepCard));
        }

        private static int CurrentTotalTick()
        {
            int dtp = GameQuery.DayTimePoints;
            if (dtp < 0) return 0;
            return GameQuery.CurrentDay * 96 + (96 - dtp);
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
                Plugin.Logger.LogInfo("[CottageResidentSchedulePatch] Captured Academy EnvID for the Miller/Weaver schedule.");
            }
            else if (!_innCaptured && ReferenceEquals(envCard, _innInteriorCard))
            {
                _innEnvId = currentEnv;
                _innCaptured = true;
                Plugin.Logger.LogInfo("[CottageResidentSchedulePatch] Captured Inn EnvID for the Miller/Weaver schedule.");
            }
        }

        private static void CaptureCottageInteriorEnvId(object gm, Resident resident, object npc)
        {
            if (resident.CottageInteriorCaptured) return;

            var currentEnv = Reflect.GetMember(gm, "CurrentEnvironment");
            if (currentEnv == null || Reflect.GetMember(currentEnv, "IsNull") is true) return;
            if (!ReferenceEquals(Reflect.GetMember(currentEnv, "EnvCard"), resident.CottageInteriorCard)) return;

            resident.CottageInteriorEnvId = currentEnv;
            resident.CottageInteriorCaptured = true;
            Plugin.Logger.LogInfo($"[CottageResidentSchedulePatch] Captured {resident.Name}'s cottage interior EnvID.");
        }

        // ── Public queries ────────────────────────────────────────────────────────

        /// <summary>
        /// True when the named resident's NPC is currently standing inside their OWN cottage
        /// interior. The Copper Chest theft roll (CopperChestPatch, §10.8.3.6) uses this for its
        /// "instant detection, no roll — the owner is right there" case; exposed here rather than
        /// duplicated so there is only one definition of "home" for these residents.
        ///
        /// <para>Reads the NPC's ACTUAL CurrentEnvironment, not the scheduled destination, for the
        /// same reason SyncPortrait does: a resident mid-commute has not arrived yet, and treating
        /// the schedule as ground truth would catch a burglar for a Miller who is still two tiles
        /// away. Returns false whenever anything is unresolvable — a broken query must not
        /// manufacture a crime (feedback_subsystem_graceful_degradation).</para>
        /// </summary>
        public static bool IsResidentHome(string agentUid)
        {
            try
            {
                if (string.IsNullOrEmpty(agentUid)) return false;
                if (!ResolveTypes() || !ResolveRefs()) return false;

                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return false;

                foreach (var resident in Residents)
                {
                    if (!string.Equals(resident.AgentUid, agentUid, StringComparison.OrdinalIgnoreCase)) continue;
                    var npc = FindLiveNpc(gm, resident);
                    if (npc == null) return false; // hasn't moved in — nobody home by definition
                    return IsAtEnv(npc, resident.CottageInteriorCard);
                }
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[CottageResidentSchedulePatch] IsResidentHome('{agentUid}') failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
                return false;
            }
        }

        // ── NPC / location helpers ────────────────────────────────────────────────

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

        private static object BuildOutdoorEnvId(object cardData)
        {
            if (cardData == null) return null;
            try { return _envIdFromCardCtor.Invoke(new object[] { cardData, false }); }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[CottageResidentSchedulePatch] BuildOutdoorEnvId failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
                return null;
            }
        }

        private static void MoveNpcTo(object gm, object npc, object envId)
        {
            if (envId == null) return;
            try { _moveNpcMethod.Invoke(gm, new object[] { npc, envId }); }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[CottageResidentSchedulePatch] MoveNpcTo failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // ── Portrait sync ─────────────────────────────────────────────────────────

        // Reads the resident's ACTUAL CurrentEnvironment.EnvCard — not the scheduled destination
        // — so the portrait never contradicts where they're really standing (same reasoning as
        // ProfessorSchedulePatch/ApothecarySchedulePatch's own SyncPortrait). Cottage, wander
        // nodes, the Village tile, and anywhere unresolved all fall back to the default Village
        // photo — only Inn and Academy get their own art.
        private static Portrait ActualPortrait(object npc)
        {
            var env = Reflect.GetMember(npc, "CurrentEnvironment");
            if (env == null || Reflect.GetMember(env, "IsNull") is true) return Portrait.None;
            var envCard = Reflect.GetMember(env, "EnvCard");
            if (envCard == null) return Portrait.None;

            if (ReferenceEquals(envCard, _innInteriorCard)) return Portrait.Inn;
            if (ReferenceEquals(envCard, _academyInteriorCard)) return Portrait.Academy;
            return Portrait.Village;
        }

        private static void SyncPortrait(object npc, Resident resident)
        {
            if (_setCardImageMethod == null) return;

            var portrait = ActualPortrait(npc);
            if (portrait == Portrait.None || portrait == resident.LastPortrait) return;

            var sprite = ResolvePortraitSprite(resident, portrait);
            if (sprite == null) return;

            Reflect.SetMember(resident.Agent, "AgentImage", sprite);
            ApplyCardImage(Reflect.GetMember(npc, "ModelCard"), sprite);
            RefreshCardVisuals(Reflect.GetMember(npc, "AssociatedCard"));
            resident.LastPortrait = portrait;
        }

        private static Sprite ResolvePortraitSprite(Resident resident, Portrait portrait)
        {
            string name = portrait switch
            {
                Portrait.Inn => resident.InnSpriteName,
                Portrait.Academy => resident.AcademySpriteName,
                _ => resident.VillageSpriteName,
            };
            return GameContent.Find<Sprite>(name);
        }

        private static void ApplyCardImage(object cardData, Sprite sprite)
        {
            if (cardData == null) return;
            try { _setCardImageMethod.Invoke(cardData, new object[] { sprite }); }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[CottageResidentSchedulePatch] ApplyCardImage failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
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
                    Plugin.Logger.LogDebug($"[CottageResidentSchedulePatch] RefreshCardVisuals swallowed (expected on a destroyed card): {ex.InnerException?.ToString() ?? ex.ToString()}");
                }
            }
        }
    }
}
