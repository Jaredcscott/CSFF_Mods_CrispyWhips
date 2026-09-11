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
    ///     cmcWeaverCottageInterior — lightweight CT4 interiors, structurally identical to the
    ///     Apothecary's cabin interior: CT2 "Enter the Cottage" DA -> CT4 env -> CT8 backdrop +
    ///     CT2 exit door). Despite the "instanced interior" naming/chassis convention used
    ///     throughout this village content, these interiors — like the Academy/Inn interiors
    ///     below — are NOT flagged "InstancedEnvironment": true in their own JSON
    ///     (CardData/Environment/CMC_{Miller,Weaver}CottageInterior.json). That flag is exactly
    ///     what the game's own EnvID(CardData,EnvID,int) constructor checks before it needs
    ///     travel-path context (.decomp/EnvID.cs:214) — false here means the plain
    ///     EnvID(CardData,bool) constructor (BuildOutdoorEnvId, already used for every outdoor
    ///     tile below) produces a byte-for-byte equivalent destination with NO player visit
    ///     required (confirmed via .decomp/EnvDictKey.cs:24-46: with ParentEnvs always null for
    ///     a non-instanced card, the resulting key depends only on the card's own UniqueIDIndex).
    ///     A prior version of this file waited for the player to physically walk through each
    ///     door first ("EnvID capture") before it would ever route a resident home — since most
    ///     players have no reason to ever open a Miller/Weaver cottage door, that made the
    ///     nightly homecoming silently never fire for most playthroughs (the reported "I never
    ///     see them enter" bug). Fixed: the interior EnvID is now built directly, the same way
    ///     the Village/wander tiles always were.
    ///   - Every evening [18:00, 21:00): the Inn — guaranteed daily, not a roll (village-life ask:
    ///     "all NPCs spend time in the Inn in the evenings").
    ///   - Exactly one day per week (a fixed, per-resident day-of-week so Miller and Weaver don't
    ///     both show up the same day): an afternoon window [13:00, 17:00) at the Academy.
    ///   - Remaining daytime hours, [6:00, 18:00), on non-Academy days: "at work" — owned by
    ///     <see cref="CottageResidentWorkDutyPatch"/>'s real engine NPCDuty chassis (same pattern
    ///     as the Village Guards' patrol), NOT this file. Dest.Work's RunCommute case is
    ///     deliberately a no-op: during that window, the engine duty is the only caller of
    ///     MoveNPC for these two residents, so the two independent pollers can never race over
    ///     the same move. This file's only remaining job for that window is (a) keeping
    ///     <c>AcademyTodayStat</c> current — the one gate the engine duty needs but ValidTimesOfDay
    ///     cannot express on its own (no day-of-week support) — and (b) firing each resident's
    ///     once-a-day produce-stock action while they're actually standing at the Village. This
    ///     replaced a cosmetic per-day Wander roll to generic outdoor nodes that fired no
    ///     profession-specific action at all (owner request 2026-08-21 — "they don't enter the
    ///     mill/workshop").
    ///   The Inn/Academy legs use the same direct construction — no capture prerequisite either.
    ///
    /// Portrait sync (added alongside the guaranteed Inn/Academy schedule): each resident's card
    /// art follows Village (default) / Inn / Academy / their own Cottage interior the same way
    /// ProfessorSchedulePatch's and ApothecarySchedulePatch's SyncPortrait do — derived from the
    /// NPC's ACTUAL CurrentEnvironment, not the scheduled destination, so it never contradicts
    /// where they're really standing. The Cottage ("at home") leg was added 2026-08-26,
    /// and its two PNGs (CMC_Miller_Cottage / CMC_Weaver_Cottage) landed the same day in
    /// 1.68.10, so the leg renders its own art. ResolvePortraitSprite still falls back to
    /// the resident's Village photo if a Cottage sprite ever fails to resolve, which keeps
    /// that regression cosmetic rather than blanking the card: a present-but-blank file
    /// would render the resident white every night, strictly worse than art staying put.
    ///
    /// Never moves a resident out from under a player standing with them (SharesPlayerEnv guard,
    /// same as every other village NPC scheduler). Never creates cards — weekly Copper Chest
    /// restock stays owned by CottageResidentSpawnPatch; this file's own produce-stock action
    /// targets the resident's satchel on a daily cadence, a distinct mechanism by construction
    /// (different ActionID, different receiving card, different trigger), so the two patches
    /// cannot race or double-drop over anything.
    /// </summary>
    internal static class CottageResidentSchedulePatch
    {
        private const string VillageEnvUid = "cmcEnvVillage";
        private const string AcademyInteriorUid = "cmcAcademyInterior";
        private const string InnInteriorUid = "cmcInnInterior";

        private enum Dest { Village, Cottage, Inn, Academy, Work }
        private enum Portrait { None = 0, Village = 1, Inn = 2, Academy = 3, Cottage = 4 }

        private const float NightStartHour = 22f;
        private const float NightEndHour = 6f;
        private const float InnVisitStartHour = 18f;
        private const float InnVisitEndHour = 21f;
        private const float AcademyVisitStartHour = 13f;
        private const float AcademyVisitEndHour = 17f;

        private const int TicksPerTile = NpcTileWalker.DefaultTicksPerTile; // 45 min/tile, in DTP ticks

        private sealed class Resident
        {
            public string Name;
            public string AgentUid;
            public string CottageInteriorUid;
            public int AcademyDayOfWeek; // 0-6 — CurrentDay % 7 == this is their one Academy day
            public string VillageSpriteName;
            public string InnSpriteName;
            public string AcademySpriteName;
            public string CottageSpriteName; // "at home" leg; art shipped 1.68.10, Village fallback kept

            // "At work" leg — owned by CottageResidentWorkDutyPatch's engine duty, not this file
            // (see class doc). AcademyTodayStatUid is the gate that duty reads; ProduceActionId is
            // the AgentAction this file fires once/day while the resident is actually at work.
            public string AcademyTodayStatUid;
            public string ProduceActionId;

            public object Agent;                // NPCAgent, resolved lazily
            public object LiveNpc;              // cached InGameNPC, validated by FindLiveNpc before trusting it
            public object CottageInteriorCard;   // CardData (own cottage interior)
            public object CottageInteriorEnvId;  // built directly once CottageInteriorCard resolves (see ResolveRefs) — no player visit needed
            public object AcademyTodayStat;      // GameStat SO, resolved lazily
            public Portrait LastPortrait;        // 0 = not yet synced this run
            public int LastProduceDay = int.MinValue; // session-scoped throttle, like ProfessorSchedulePatch's _lastSpecialtyDay
        }

        private static readonly Resident[] Residents =
        {
            new Resident
            {
                Name = "Miller", AgentUid = "cmcMillerAgent", CottageInteriorUid = "cmcMillerCottageInterior",
                AcademyDayOfWeek = 1,
                VillageSpriteName = "CMC_Miller", InnSpriteName = "CMC_Miller_Inn", AcademySpriteName = "CMC_Miller_Academy",
                CottageSpriteName = "CMC_Miller_Cottage",
                AcademyTodayStatUid = "cmcStatMillerAcademyToday", ProduceActionId = "MillerProduceFlour",
            },
            new Resident
            {
                Name = "Weaver", AgentUid = "cmcWeaverAgent", CottageInteriorUid = "cmcWeaverCottageInterior",
                AcademyDayOfWeek = 4,
                VillageSpriteName = "CMC_Weaver", InnSpriteName = "CMC_Weaver_Inn", AcademySpriteName = "CMC_Weaver_Academy",
                CottageSpriteName = "CMC_Weaver_Cottage",
                AcademyTodayStatUid = "cmcStatWeaverAcademyToday", ProduceActionId = "WeaverProduceYarn",
            },
        };

        private static bool _initialized;

        private static object _villageEnvCard;   // CardData
        private static object _academyInteriorCard;
        private static object _innInteriorCard;

        // Academy/Inn destination EnvIDs (shared across both residents — same Academy/Inn everyone
        // else visits) — built once directly from their CardData in ResolveRefs; see the class
        // doc comment for why no player-visit capture is needed.
        private static object _academyEnvId;
        private static object _innEnvId;

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

        // Produce-stock firing (mirrors ProfessorSchedulePatch.FireAgentAction / this mod's own
        // CottageResidentSpawnPatch.FireAgentAction — each file that fires a named AgentAction
        // carries its own small copy of this reflection idiom rather than a shared helper, matching
        // the established convention). Optional — degrades gracefully if unresolvable: residents
        // still commute normally, they just never top up their own satchel while at work.
        private static Type _npcActionType;         // NPCAction (declares ToAction)
        private static Type _inGameCardBaseType;    // InGameCardBase
        private static Type _inGameNpcOrPlayerType; // InGameNPCOrPlayer (struct)
        private static MethodInfo _toActionMethod;      // NPCAction.ToAction(InGameNPC, InGameCardBase) -> CardAction
        private static MethodInfo _performActionMethod; // GameManager.PerformAction(CardAction, InGameCardBase, bool, InGameNPCOrPlayer) (static)
        private static ConstructorInfo _inGameNpcOrPlayerCtor; // InGameNPCOrPlayer(InGameNPC)
        private static bool _fireActionUnavailableWarned;

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

        // The destination EnvIDs (_academyEnvId/_innEnvId/resident.CottageInteriorEnvId) are built
        // directly from stable CardData references (ResolveRefs' ??= only ever computes them once)
        // and carry no per-session/per-visit state, so they do NOT need resetting on reload — only
        // the portrait/walk state that genuinely is session-scoped.
        private static void ResetSessionState_Postfix()
        {
            foreach (var resident in Residents)
            {
                resident.LastPortrait = Portrait.None;
                resident.LastProduceDay = int.MinValue;
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

            // Optional — produce-stock firing degrades gracefully if absent (see field doc above).
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

            return _getFromIdMethod != null && _moveNpcMethod != null && _envIdFromCardCtor != null;
        }

        private static bool ResolveRefs()
        {
            _villageEnvCard ??= CardUtil.GetCardDataById(VillageEnvUid);
            _academyInteriorCard ??= CardUtil.GetCardDataById(AcademyInteriorUid);
            _innInteriorCard ??= CardUtil.GetCardDataById(InnInteriorUid);

            // Built directly, exactly like every outdoor tile below — see the class doc comment
            // for why these interiors need no live player-visit capture (InstancedEnvironment is
            // false on all of them, confirmed in their own JSON).
            _academyEnvId ??= BuildOutdoorEnvId(_academyInteriorCard);
            _innEnvId ??= BuildOutdoorEnvId(_innInteriorCard);

            bool residentsOk = true;
            foreach (var resident in Residents)
            {
                resident.Agent ??= _getFromIdMethod.Invoke(null, new object[] { resident.AgentUid });
                resident.CottageInteriorCard ??= CardUtil.GetCardDataById(resident.CottageInteriorUid);
                resident.CottageInteriorEnvId ??= BuildOutdoorEnvId(resident.CottageInteriorCard);
                // Optional — see SetAcademyTodayStat; CottageResidentWorkDutyPatch's own build
                // poll is what actually gates on this stat existing, not this file.
                resident.AcademyTodayStat ??= _getFromIdMethod.Invoke(null, new object[] { resident.AcademyTodayStatUid });
                if (resident.Agent == null || resident.CottageInteriorCard == null) residentsOk = false;
            }

            return _villageEnvCard != null && _academyInteriorCard != null && _innInteriorCard != null && residentsOk;
        }

        private static void RunScheduler()
        {
            try
            {
                if (!ResolveTypes() || !ResolveRefs()) return;

                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;

                float hour = GameQuery.HourOfDay;
                int today = GameQuery.CurrentDay;

                bool quietVillage = QuietVillagePerkPatch.IsActive();

                foreach (var resident in Residents)
                {
                    var npc = FindLiveNpc(gm, resident);
                    if (npc == null) continue; // hasn't moved in yet — CottageResidentSpawnPatch owns that

                    // "Quiet Village" perk — skip the whole hour-clock/wander computation and just
                    // head home. CottageResidentWorkDutyPatch's own engine duty is suppressed
                    // separately (QuietVillagePerkPatch.SyncResidentWorkDuties) so it can't fight
                    // this over the same NPC during work hours.
                    if (quietVillage)
                    {
                        if (!SharesPlayerEnv(npc))
                            RunCommute(gm, npc, resident, Dest.Cottage);
                        SyncPortrait(npc, resident);
                        continue;
                    }

                    bool isAcademyDay = IsAcademyDay(today, resident);
                    var dest = ComputeDestination(hour, isAcademyDay);

                    // Written every tick for the WHOLE Academy day (not just the visit window) —
                    // this is the one gate CottageResidentWorkDutyPatch's engine duty needs but
                    // ValidTimesOfDay cannot express on its own (no day-of-week support). See
                    // class doc for why whole-day is the safe choice over a narrower window.
                    SetAcademyTodayStat(gm, resident, isAcademyDay);

                    // Never move him out from under a mid-visit player — but still let the
                    // portrait reflect wherever he's actually standing.
                    if (!SharesPlayerEnv(npc))
                        RunCommute(gm, npc, resident, dest);

                    // Only while actually AT work (not mid-commute) — CottageResidentWorkDutyPatch
                    // owns getting them there; this just fires the payoff once they've arrived.
                    if (dest == Dest.Work) FireProduceStockIfDue(npc, resident);

                    SyncPortrait(npc, resident);
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[CottageResidentSchedulePatch] RunScheduler failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // ── Daily schedule (pure function of the clock + day-of-week) ───────────────────────

        // Exactly one day per week, per resident — not a roll. Deliberately staggered
        // (Miller/Weaver AcademyDayOfWeek differ) so they don't both show up the same day.
        private static bool IsAcademyDay(int day, Resident resident) => Mod(day, 7) == resident.AcademyDayOfWeek;

        private static Dest ComputeDestination(float hour, bool isAcademyDay)
        {
            bool isNight = hour >= NightStartHour || hour < NightEndHour;
            if (isNight) return Dest.Cottage;

            // Guaranteed every evening, every resident, every day (village-life ask: "all NPCs
            // spend time in the Inn in the evenings") — checked before Academy/Work so it can
            // never be skipped by either.
            if (hour >= InnVisitStartHour && hour < InnVisitEndHour) return Dest.Inn;

            if (isAcademyDay && hour >= AcademyVisitStartHour && hour < AcademyVisitEndHour) return Dest.Academy;

            // Remaining daytime hours — "at work" (see class doc). Falls through here even on an
            // Academy day outside the 13:00-17:00 visit window; CottageResidentWorkDutyPatch's own
            // engine duty is unselectable all day on an Academy day regardless (AcademyTodayStat
            // gate), so this is a no-op then — a few cosmetic idle hours, never a conflict.
            return Dest.Work;
        }

        private static int Mod(int value, int modulus)
        {
            int m = value % modulus;
            return m < 0 ? m + modulus : m;
        }

        private static void RunCommute(object gm, object npc, Resident resident, Dest dest)
        {
            switch (dest)
            {
                case Dest.Cottage:
                    // No "already there" shortcut needed (mirrors ApothecarySchedulePatch's own
                    // cabin-interior homing) — WalkTowards' CurrentOutdoorUidOf/IsAtEnv checks
                    // already short-circuit once the resident has arrived.
                    WalkTowards(gm, npc, resident, VillageEnvUid, resident.CottageInteriorEnvId);
                    return;

                case Dest.Inn:
                    WalkTowards(gm, npc, resident, VillageEnvUid, _innEnvId);
                    return;

                case Dest.Academy:
                    WalkTowards(gm, npc, resident, VillageEnvUid, _academyEnvId);
                    return;

                case Dest.Work:
                    // No-op, deliberately: CottageResidentWorkDutyPatch's engine duty is the ONLY
                    // caller of MoveNPC for these two residents during work hours — see class doc
                    // for why this is what keeps the two independent pollers from ever racing.
                    return;

                default: // Dest.Village
                    WalkTowards(gm, npc, resident, VillageEnvUid, BuildOutdoorEnvId(_villageEnvCard));
                    return;
            }
        }

        // The outdoor tile this resident is standing on right now, for tile-walking purposes —
        // the Village (the shared front door for the Village itself, the Academy, the Inn, and
        // every resident's own cottage). No wander nodes to check anymore — daytime commuting
        // outside the Village is CottageResidentWorkDutyPatch's engine duty now (see class doc),
        // which paths via the engine's own WorldMap graph, not this file's tile walker.
        private static string CurrentOutdoorUidOf(object npc, Resident resident)
        {
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

        // ── AcademyToday stat + produce stock ─────────────────────────────────────

        // Read/write idiom copied from CottageResidentSpawnPatch's own move-in-day stat
        // (GetMoveInDay/SetMoveInDay/ResolveMoveInStatInstance) — the sanctioned "GameStat direct
        // C# write" exception (reference_gamestat_direct_csharp_write): SimpleCurrentValue is the
        // live per-player value, CurrentBaseValue is what a direct write actually has to touch.
        private static void SetAcademyTodayStat(object gm, Resident resident, bool isAcademyDay)
        {
            var inGameStat = ResolveAcademyTodayStatInstance(gm, resident);
            if (inGameStat == null) return;
            Reflect.SetMember(inGameStat, "CurrentBaseValue", isAcademyDay ? 1f : 0f);
        }

        private static object ResolveAcademyTodayStatInstance(object gm, Resident resident)
        {
            if (resident.AcademyTodayStat == null) return null;
            if (Reflect.GetMember(gm, "StatsDict") is not IDictionary statsDict) return null;
            if (!statsDict.Contains(resident.AcademyTodayStat)) return null;
            return statsDict[resident.AcademyTodayStat];
        }

        // Fires the resident's produce-stock AgentAction once per in-game day, only once they are
        // ACTUALLY standing at the Village (not merely scheduled to be — CottageResidentWorkDutyPatch's
        // engine duty may still be mid-pathfind). Distinct from the weekly Copper Chest accrual
        // CottageResidentSpawnPatch owns by construction: different ActionID, targets the
        // resident's own satchel (AssociatedCard) rather than the chest, different trigger — no
        // risk of the R6 double-drop that rule guards against.
        private static void FireProduceStockIfDue(object npc, Resident resident)
        {
            if (resident.ProduceActionId == null) return;
            int today = GameQuery.CurrentDay;
            if (resident.LastProduceDay == today) return;
            if (!IsAtEnv(npc, _villageEnvCard)) return; // still mid-commute — try again next tick

            var associatedCard = Reflect.GetMember(npc, "AssociatedCard");
            if (associatedCard == null) return;

            if (FireAgentAction(resident, npc, associatedCard, resident.ProduceActionId))
                resident.LastProduceDay = today;
        }

        // Fires a named AgentAction with `associatedCard` as the receiving container — same
        // ToAction+PerformAction idiom ProfessorSchedulePatch.FireAgentAction and
        // CottageResidentSpawnPatch.FireAgentAction each already carry their own copy of.
        private static bool FireAgentAction(Resident resident, object npc, object associatedCard, string actionId)
        {
            if (_toActionMethod == null || _performActionMethod == null || _inGameNpcOrPlayerCtor == null)
            {
                if (!_fireActionUnavailableWarned)
                {
                    _fireActionUnavailableWarned = true;
                    Plugin.Logger.LogWarning("[CottageResidentSchedulePatch] PerformAction reflection unavailable — produce-stock inactive.");
                }
                return false;
            }

            if (Reflect.GetMember(resident.Agent, "AgentActions") is not Array agentActions) return false;
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
                Plugin.Logger.LogDebug($"[CottageResidentSchedulePatch] {resident.Name} fired '{actionId}'.");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[CottageResidentSchedulePatch] FireAgentAction('{actionId}') failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
                return false;
            }
        }

        // ── NPC / location helpers ────────────────────────────────────────────────

        // Checks the cached reference first (O(1)) before falling back to a full AllNPCs
        // scan — this runs every 1s for both residents (Miller + Weaver), so once a resident
        // is spawned and stable this avoids re-scanning the whole NPC roster every tick.
        private static object FindLiveNpc(object gm, Resident resident)
        {
            if (Reflect.IsAlive(resident.LiveNpc) && ReferenceEquals(Reflect.GetMember(resident.LiveNpc, "NPCModel"), resident.Agent))
                return resident.LiveNpc;

            if (Reflect.GetMember(gm, "AllNPCs") is not IEnumerable allNpcs) return null;
            foreach (var npc in allNpcs)
            {
                if (npc == null) continue;
                if (ReferenceEquals(Reflect.GetMember(npc, "NPCModel"), resident.Agent))
                {
                    resident.LiveNpc = npc;
                    return npc;
                }
            }
            resident.LiveNpc = null;
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
        // ProfessorSchedulePatch/ApothecarySchedulePatch's own SyncPortrait). Inn, Academy and
        // the resident's OWN cottage interior each get their own art; wander nodes, the Village
        // tile, and anywhere unresolved all fall back to the default Village photo.
        //
        // Cottage is matched per-resident against resident.CottageInteriorCard (Miller's interior
        // must never stamp the Weaver's "at home" art and vice versa), which is why this takes the
        // resident rather than reading only file-level statics like the Inn/Academy cards do.
        private static Portrait ActualPortrait(object npc, Resident resident)
        {
            var env = Reflect.GetMember(npc, "CurrentEnvironment");
            if (env == null || Reflect.GetMember(env, "IsNull") is true) return Portrait.None;
            var envCard = Reflect.GetMember(env, "EnvCard");
            if (envCard == null) return Portrait.None;

            if (ReferenceEquals(envCard, _innInteriorCard)) return Portrait.Inn;
            if (ReferenceEquals(envCard, _academyInteriorCard)) return Portrait.Academy;
            if (resident.CottageInteriorCard != null
                && ReferenceEquals(envCard, resident.CottageInteriorCard)) return Portrait.Cottage;
            return Portrait.Village;
        }

        private static void SyncPortrait(object npc, Resident resident)
        {
            if (_setCardImageMethod == null) return;

            var portrait = ActualPortrait(npc, resident);
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
                Portrait.Cottage => resident.CottageSpriteName,
                _ => resident.VillageSpriteName,
            };
            var sprite = GameContent.Find<Sprite>(name);
            // Safety net, not a placeholder: both Cottage PNGs shipped in 1.68.10, so this
            // only fires if one ever fails to resolve. Reusing the Village photo keeps such
            // a regression cosmetic instead of blanking the card.
            if (sprite == null && portrait == Portrait.Cottage)
                sprite = GameContent.Find<Sprite>(resident.VillageSpriteName);
            return sprite;
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
