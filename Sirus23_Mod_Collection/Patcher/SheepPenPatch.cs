using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Logging;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace Sirus23ModCollection.Patcher
{
    /// <summary>
    /// Night-time escape/predation roll for tame sheep, rams, and lactating sheep left
    /// OUTSIDE a Sheep Pen (<see cref="PenUid"/>). Evaluated once per in-game day rollover
    /// on <see cref="TickEvents.DayRollover"/> ("overnight" - the same framing
    /// CSFFModFramework's WildlifeRaidService uses for its own once-per-day danger roll).
    ///
    /// SCOPE: the player's current environment, AND (feature 27, B-lite) every other environment
    /// holding loose sheep. Tame sheep carry AlwaysUpdate: true, so a sheep left on the ground
    /// when the player travels stays in GameManager.AllCards as an InBackground row at its own
    /// environment, and survives a save and reload that way (W0-18 MEASURED,
    /// Documentation/Research/Sirus23_Mod_Collection/companions_and_transport.md). Until feature
    /// 27 those rows were skipped, so a flock the player had walked away from was never at risk.
    /// They are now grouped by CardEnvironment and rolled on the same night at the same chances
    /// (RollAwayFlocks), with the wolf and bell read per environment. A loss there leaves no
    /// Sheep Remains, tracks or orphan lamb, because a spawn lands on the current board only.
    ///
    /// SAFETY: a penned sheep (CurrentContainer == the Sheep Pen) is EXEMPT - checked before
    /// any roll. A Wolf companion present in the player's current environment REDUCES the
    /// predation roll for every unpenned sheep that night (6% to 1%) AND the escape roll (4% to 1%,
    /// plan N2: the wolf herds as well as guards; with a Sheep Bell the two multiply to 0.5%). Until 1.21.2 the wolf
    /// suppressed the roll entirely, which left the pen with no consequence to create for
    /// every wolf owner (T2.101, r33: five nights of "stood guard", zero losses); decided
    /// 2026-09-09. Wolf presence mirrors the wolf's EncounterGuards/WolfGuard.json scoping
    /// (an agent-only guard since 1.22.6): presence-in-current-env, not a "Guard Camp" action
    /// toggle - the wolf has no persistent "guarding" state to gate on, see CardInteractions
    /// on WolfCompanion.json.
    ///
    /// FENCE CONDITION (feature 21): the placed pen's SpoilageTime is its "Fence Condition",
    /// draining 1 per daytime point from 1920 (about 20 in-game days) and restored by half by
    /// the "Mend the Fence" Plank drag CI on SheepPen.json. A pen whose condition has reached
    /// zero protects nothing: its sheep stay inside it (they are not spilled out) but roll like
    /// loose ones (plan open question 10's default). A pen from a save made before the stat
    /// existed loads FULL, not at zero: such a pen was saved with no active durabilities, which
    /// CardSaveData stores as ActiveDurabilities = -1, and GetDurabilities then gives every
    /// stat the model now has its starting value (.decomp/CardSaveData.cs GetDurabilities).
    /// If the condition cannot be read, the pen is treated as intact (warned once), so a
    /// reflection miss never costs a player their flock.
    ///
    /// SHEEP BELL (feature 22): a Sheep Bell (<see cref="SheepBellUid"/>) anywhere in the
    /// evaluated environment halves the escape roll for every sheep at risk that night
    /// (plan open question 11: escape only, predation unchanged). Found in the same AllCards
    /// walk, behind the same IsInPlayerEnv test, so a bell left in another environment does
    /// nothing here.
    ///
    /// ORPHAN LAMB (feature 24): when the predator takes a Lactating Sheep, OrphanChance
    /// (plan open question 12: 50%) leaves her lamb beside the remains. Only the player's
    /// current environment spawns anything (an away loss spawns nothing), so SpawnService
    /// places the lamb on the right board. The lamb is spawned with its Growth (SpoilageTime) at the model's
    /// MaxValue: a spawned card can otherwise start at zero, and a lamb at zero Growth grows
    /// up on its next tick (memory reference_givecard_postfix_stat_init).
    ///
    /// PREDATOR TRACKS (feature 25): a night with at least one predator kill also leaves ONE
    /// Predator Tracks card (<see cref="PredatorTracksUid"/>) beside the remains. Its "Follow
    /// the Tracks" DismantleAction starts the vanilla wolf encounter with no NPC attached
    /// (Documentation/Research/Sirus23_Mod_Collection/pen_and_predators.md, W0-16 DECISION);
    /// since 1.22.6 (S23-B3) the companion guards name predator AGENTS only (AgentUids, empty
    /// EncounterUids), so they never touch an NPC-less start and the action is no longer hidden
    /// while the Wolf Companion is on the board. The tracks fade after about a day.
    ///
    /// HARSH WILDS RAIDS (feature 26, optional cross-mod): a Harsh Wilds black bear or eagle
    /// raid is an Event card that is queued, shown as a modal for zero game time and destroyed
    /// by its one button, so it is never on the board at DayRollover (research file
    /// pen_and_predators.md, W0-17 DECISION). Two ActionRouter AfterWrapped handlers, keyed on
    /// the raid cards' UIDs and the button's LocalizationKey, latch the day and the player's
    /// environment key when the button is pressed; a night evaluated in that environment on the
    /// raid's day or the day after doubles the predation chance (open question 13, a
    /// placeholder; the lynx is left out). The UIDs live only in this C# lookup, never in JSON,
    /// so without Harsh Wilds the handlers are never dispatched and nothing changes or logs.
    /// Registered once per process from Register (plugin Awake), never per run. The latch is
    /// in memory only: a reload loses it, which costs at most one raised night.
    ///
    /// VANILLA ENCLOSURE (feature 23): when the player is standing in a vanilla Enclosure room
    /// (<see cref="EnclosureEnvUids"/>), every sheep there is penned and the night is not rolled
    /// at all. Sheep left in the Enclosure while the player is elsewhere are skipped by the same
    /// test in RollAwayFlocks.
    ///
    /// BALANCE PLACEHOLDER: EscapeChance/PredationChance are conservative starting guesses
    /// for a mechanic that can permanently destroy the player's tamed livestock - NOT
    /// balanced/final values. Flag for playtest feedback before treating them as tuned.
    /// </summary>
    internal static class SheepPenPatch
    {
        private const string TameSheepUid = "sh_tame_sheep";
        private const string MaleSheepUid = "sh_male_sheep";
        private const string LactatingSheepUid = "sh_lactating_sheep";
        private const string PenUid = "sh_sheep_pen";
        private const string RemainsUid = "sh_sheep_remains";
        private const string SheepBellUid = "sh_sheep_bell";
        private const string LambUid = "sh_lamb";
        private const string PredatorTracksUid = "sh_predator_tracks";

        // Harsh Wilds raid events (optional mod; UIDs re-read 2026-10-09 from
        // HarshWilds/CardData/Event/hw_event_blackbear.json and hw_event_eagle.json). Keyed on
        // the event card's UID alone: each card has one button, so matching its localization
        // key as well narrowed nothing and would have stopped the latch silently on a key
        // rename (Fleet plan section 9, owner approved 2026-10-09). With Harsh Wilds absent the
        // card never exists and the handler never fires.
        private const string HwBlackBearEventUid = "hw_event_blackbear";
        private const string HwEagleEventUid = "hw_event_eagle";
        // BALANCE PLACEHOLDER (open question 13): predation multiplier the night after a raid.
        private const float RaidPredationMultiplier = 2f;
        private static int _raidDay = int.MinValue;
        private static string _raidEnvKey;
        private static string _raidName;
        // Lamb.json SpoilageTime ("Growth") MaxValue, used only if the model cannot be read.
        private const float LambGrowthFallback = 5760f;

        private static readonly HashSet<string> PennableUids = new(StringComparer.Ordinal)
        {
            TameSheepUid, MaleSheepUid, LactatingSheepUid,
        };

        // BALANCE PLACEHOLDER (see class doc comment) - not final-tuned. Combined per-sheep
        // nightly loss risk when unpenned: 4% escape + 6% predation = 10% with no wolf,
        // 1% escape + 1% predation = 2% with a wolf companion on the board. Penned: 0%.
        private const float EscapeChance = 0.04f;
        private const float PredationChance = 0.06f;
        private const float GuardedPredationChance = 0.01f;
        // BALANCE PLACEHOLDER (plan N2): with the wolf companion on the board the flock is herded,
        // so the escape roll drops 4% -> 1%. COMPOSES WITH THE BELL BY MULTIPLYING: a wolf and a
        // Sheep Bell together give 1% x 0.5 = 0.5%, because both are things the player built or
        // kept for this and stacking them should be worth more than either alone.
        private const float GuardedEscapeChance = 0.01f;
        // BALANCE PLACEHOLDER (open question 11): a Sheep Bell in the environment halves escape.
        private const float BellEscapeMultiplier = 0.5f;
        // BALANCE PLACEHOLDER (open question 12): chance a taken nursing ewe leaves her lamb.
        private const float OrphanChance = 0.5f;

        // A pen at or below this Fence Condition has worn through (an epsilon, not an exact
        // zero: a float drained by repeated rates can sit a hair above 0).
        private const float WornThroughCondition = 0.01f;
        private static bool _warnedConditionUnreadable;

        // Feature 23: the vanilla Enclosure rooms, as their own set (not IndoorOrCaveEnv, which
        // also covers cabins). b2b3c63ab86619d4ba3345d9dedad388 is the room Bp_Enclosure's
        // entrance opens (Env_MudHutConstructionStartingPointEnclosure, MEASURED live 2026-10-10,
        // Documentation/Research/Sirus23_Mod_Collection/pen_and_predators.md W0-15);
        // a98be0703900d5f45a89c3990c370138 is the extra-room card
        // (Env_MudHutConstructionLocationEnclosure, inferred from the export, not measured). The
        // legacy 193f3be30d3891547a95d0d7e8539ccb is left out: nothing live creates it.
        private static readonly HashSet<string> EnclosureEnvUids = new(StringComparer.Ordinal)
        {
            "b2b3c63ab86619d4ba3345d9dedad388",
            "a98be0703900d5f45a89c3990c370138",
        };
        private static bool _warnedEnvUnreadable;

        private enum PenState { Loose, Penned, WornPen }

        private static ManualLogSource Logger => Plugin.Logger;

        public static void Register()
        {
            TickEvents.DayRollover += OnDayRollover;

            // Once per process (plugin Awake): ActionRouter.Register is additive.
            ActionRouter.Register(new ActionHandler
            {
                Name = "SheepPenHarshWildsBearRaid",
                CardUid = HwBlackBearEventUid,
                Timing = ActionTiming.AfterWrapped,
                After = _ => LatchRaid("black bear"),
            });
            ActionRouter.Register(new ActionHandler
            {
                Name = "SheepPenHarshWildsEagleRaid",
                CardUid = HwEagleEventUid,
                Timing = ActionTiming.AfterWrapped,
                After = _ => LatchRaid("eagle"),
            });
        }

        private static void LatchRaid(string which)
        {
            _raidDay = GameQuery.CurrentDay;
            _raidEnvKey = GameQuery.CurrentEnvironmentStringDictionaryKey;
            _raidName = which;
            if (_raidEnvKey == null)
                Logger?.LogWarning($"[SheepPen] a Harsh Wilds {which} raid was seen but the current environment could not be read; it will not raise tonight's roll.");
            else
                Logger?.LogInfo($"[SheepPen] a Harsh Wilds {which} raid on day {_raidDay} in {_raidEnvKey}: unpenned sheep there face doubled predation tonight.");
        }

        /// <summary>True when a latched raid is from today or yesterday and in the given environment.</summary>
        private static bool RaidRaisesTonight(string envKey)
        {
            if (_raidEnvKey == null || envKey == null) return false;
            int age = GameQuery.CurrentDay - _raidDay;
            if (age < 0 || age > 1) return false;
            return string.Equals(_raidEnvKey, envKey, StringComparison.Ordinal);
        }

        private static void OnDayRollover()
        {
            try
            {
                EvaluateNight();
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[SheepPen] night roll failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void EvaluateNight()
        {
            var gm = CardUtil.GetGameManagerInstance();
            if (gm == null) return;

            // Feature 23: the vanilla Enclosure counts as a pen. Read at the moment of use,
            // never cached before a run (root CLAUDE.md Runtime Card State Caching (5)).
            string envUid = BareEnvUid(GameQuery.CurrentEnvironmentUniqueId);
            if (envUid == null)
            {
                if (!_warnedEnvUnreadable)
                {
                    _warnedEnvUnreadable = true;
                    Logger?.LogWarning("[SheepPen] could not read the current environment's UniqueID; the vanilla Enclosure will not protect its sheep until this is fixed.");
                }
            }
            bool playerInEnclosure = envUid != null && EnclosureEnvUids.Contains(envUid);
            if (playerInEnclosure)
                Logger?.LogDebug($"[SheepPen] the player is in the vanilla Enclosure ({envUid}) - its sheep are penned, no roll here tonight.");

            if (CardUtil.GetMemberValue(gm, "AllCards") is not IEnumerable allCards) return;

            var atRisk = new List<object>();
            var inWornPen = new HashSet<object>();
            bool wolfPresent = false;
            bool bellPresent = false;
            var away = new Dictionary<string, AwayFlock>(StringComparer.Ordinal);

            foreach (var card in allCards)
            {
                if (card == null) continue;
                if (!IsInPlayerEnv(card))
                {
                    CollectAway(card, away);
                    continue;
                }
                if (playerInEnclosure) continue;

                string uid = CardUtil.GetCardUniqueId(card);
                if (uid == null) continue;

                if (uid == WolfTickPatch.WolfId)
                {
                    wolfPresent = true;
                    continue;
                }
                if (uid == SheepBellUid)
                {
                    bellPresent = true;
                    continue;
                }
                if (!PennableUids.Contains(uid)) continue;
                var state = GetPenState(card);
                if (state == PenState.Penned)
                {
                    Logger?.LogDebug($"[SheepPen] {uid} is penned - exempt from tonight's roll.");
                    continue;
                }
                if (state == PenState.WornPen)
                {
                    Logger?.LogDebug($"[SheepPen] {uid} is in a worn-through Sheep Pen - rolls tonight like a loose sheep.");
                    inWornPen.Add(card);
                }
                atRisk.Add(card);
            }

            if (atRisk.Count > 0)
                RollCurrentEnvironment(atRisk, inWornPen, wolfPresent, bellPresent);

            if (away.Count > 0)
                RollAwayFlocks(gm as GameManager, away);
        }

        /// <summary>Per-sheep nightly escape chance: the wolf lowers the base (herding), the Sheep Bell then halves it.</summary>
        private static float EscapeChanceFor(bool wolf, bool bell)
        {
            float baseChance = wolf ? GuardedEscapeChance : EscapeChance;
            return bell ? baseChance * BellEscapeMultiplier : baseChance;
        }

        private static void RollCurrentEnvironment(List<object> atRisk, HashSet<object> inWornPen, bool wolfPresent, bool bellPresent)
        {
            bool raid = RaidRaisesTonight(GameQuery.CurrentEnvironmentStringDictionaryKey);
            float raidMultiplier = raid ? RaidPredationMultiplier : 1f;
            float predationChance = (wolfPresent ? GuardedPredationChance : PredationChance) * raidMultiplier;
            string raidText = raid ? $", doubled by a Harsh Wilds {_raidName} raid" : "";
            float escapeChance = EscapeChanceFor(wolfPresent, bellPresent);
            float unbelledEscape = EscapeChanceFor(wolfPresent, false);
            string escapeText = bellPresent
                ? $"{Percent(escapeChance)} escape ({Percent(unbelledEscape)} without the Sheep Bell, {Percent(EscapeChance)} with neither)"
                : wolfPresent
                    ? $"{Percent(escapeChance)} escape ({Percent(EscapeChance)} unguarded)"
                    : $"{Percent(escapeChance)} escape";
            if (inWornPen.Count > 0)
                Logger?.LogInfo($"[SheepPen] a Sheep Pen's fence is worn through - {inWornPen.Count} sheep/ram(s) inside it roll like loose ones tonight. Drag a Plank onto the pen to mend it.");
            if (wolfPresent)
                Logger?.LogInfo($"[SheepPen] the wolf companion stood guard - {atRisk.Count} unpenned sheep/ram(s) roll at {Percent(predationChance)} predation ({Percent(PredationChance * raidMultiplier)} unguarded){raidText} and {escapeText} tonight.");
            else
                Logger?.LogInfo($"[SheepPen] {atRisk.Count} unpenned sheep/ram(s) roll at {Percent(predationChance)} predation{raidText} and {escapeText} tonight.");

            int taken = 0;
            foreach (var sheep in atRisk)
                if (RollForSheep(sheep, predationChance, escapeChance, wolfPresent, inWornPen.Contains(sheep)))
                    taken++;

            if (taken > 0)
            {
                Logger?.LogInfo($"[SheepPen] the predator left tracks beside the remains ({taken} taken tonight) - Follow the Tracks on the {PredatorTracksUid} card to face it.");
                SpawnService.Spawn(PredatorTracksUid);
            }
        }

        /// <summary>Rolls one sheep; returns true when a predator took it.</summary>
        private static bool RollForSheep(object sheep, float predationChance, float escapeChance, bool guarded, bool wornPen)
        {
            string uid = CardUtil.GetCardUniqueId(sheep) ?? "sheep";
            float roll = UnityEngine.Random.value;
            string where = wornPen ? "in a worn-through pen" : "left unpenned";
            if (guarded) where += ", wolf on guard";

            if (roll < predationChance)
            {
                Logger?.LogInfo($"[SheepPen] {uid} was taken by a predator overnight ({where}).");
                SpawnService.Spawn(RemainsUid);
                if (uid == LactatingSheepUid)
                    TryLeaveOrphanLamb();
                CardUtil.TryRemoveCard(sheep);
                return true;
            }
            if (roll < predationChance + escapeChance)
            {
                Logger?.LogInfo($"[SheepPen] {uid} wandered off overnight ({where}).");
                CardUtil.TryRemoveCard(sheep);
            }
            return false;
        }

        private static void TryLeaveOrphanLamb()
        {
            if (UnityEngine.Random.value >= OrphanChance) return;

            float growth = LambGrowthFallback;
            if (CardUtil.GetCardDataById(LambUid) is CardData lamb && lamb.SpoilageTime != null && lamb.SpoilageTime.MaxValue > 0f)
                growth = lamb.SpoilageTime.MaxValue;
            else
                Logger?.LogWarning($"[SheepPen] could not read {LambUid}'s Growth maximum; the orphan lamb starts at {growth}.");

            Logger?.LogInfo($"[SheepPen] her lamb survived beside the remains - an orphan {LambUid} was left on the board (Growth {growth}).");
            SpawnService.Spawn(LambUid, new Dictionary<string, float> { { "SpoilageTime", growth } });
        }

        // ── Feature 27: flocks left loose at an environment the player has left ──────────────

        /// <summary>One environment the player is not in, holding background sheep.</summary>
        private sealed class AwayFlock
        {
            public EnvID Env;
            public string Key;
            public readonly List<object> Sheep = new();
            public readonly HashSet<object> InWornPen = new();
            public bool Wolf;
            public bool Bell;
        }

        private static readonly HashSet<string> _awayWarned = new(StringComparer.Ordinal);

        private static void WarnAwayOnce(string cause, string message)
        {
            if (_awayWarned.Add(cause))
                Logger?.LogWarning($"[SheepPen] {message}");
        }

        /// <summary>
        /// Files a card outside the player's environment under its own environment, when it is a
        /// sheep, the wolf or a bell the game keeps as a background row (an AlwaysUpdate card left
        /// on the ground when the player travelled: W0-18 MEASURED, companions_and_transport.md).
        /// </summary>
        private static void CollectAway(object card, Dictionary<string, AwayFlock> away)
        {
            string uid = CardUtil.GetCardUniqueId(card);
            if (uid == null) return;
            bool isSheep = PennableUids.Contains(uid);
            bool isWolf = uid == WolfTickPatch.WolfId;
            bool isBell = uid == SheepBellUid;
            if (!isSheep && !isWolf && !isBell) return;

            if (card is not InGameCardBase cb)
            {
                WarnAwayOnce("card-type", $"a {uid} card is a {card.GetType().Name}, not an InGameCardBase; sheep left at an environment you are away from are not rolled while this lasts.");
                return;
            }
            if (!cb.InBackground) return;

            EnvID env = cb.CardEnvironment;
            if (env.IsNull) return;
            string key = env.StringDictionnaryKey;
            if (string.IsNullOrEmpty(key))
            {
                WarnAwayOnce("env-key", $"a background {uid}'s environment has no dictionary key; it is not rolled while away.");
                return;
            }

            if (!away.TryGetValue(key, out var flock))
            {
                flock = new AwayFlock { Env = env, Key = key };
                away[key] = flock;
            }
            if (isWolf) { flock.Wolf = true; return; }
            if (isBell) { flock.Bell = true; return; }

            var state = GetPenState(card);
            if (state == PenState.Penned) return;
            if (state == PenState.WornPen) flock.InWornPen.Add(card);
            flock.Sheep.Add(card);
        }

        /// <summary>
        /// Rolls every flock left loose at an environment the player is not in, on the same night
        /// and at the same chances as the player's own. The guard test is per environment: a
        /// Wolf Companion told to "Stay Here" there is saved into that environment's board
        /// (CompanionStayPatch), so it is read from GameManager.EnvironmentsData, as is a Sheep
        /// Bell left there. A loss leaves no Sheep Remains, tracks or orphan lamb: a spawn lands
        /// on the player's current board only (root CLAUDE.md, Runtime Card Spawning), and the
        /// player finds the flock smaller on return. Nothing here is latched: a reload does not
        /// re-fire the day rollover (Gate.DaysRolledOver primes on its first reading of a run),
        /// and a removed sheep is gone from AllCards before the next save.
        /// </summary>
        private static void RollAwayFlocks(GameManager gm, Dictionary<string, AwayFlock> away)
        {
            foreach (var flock in away.Values)
            {
                if (flock.Sheep.Count == 0) continue;

                var envCard = flock.Env.EnvCard;
                string envName = envCard != null ? envCard.name : flock.Key;
                string envUid = envCard != null ? envCard.UniqueID : null;
                if (envUid != null && EnclosureEnvUids.Contains(envUid))
                {
                    Logger?.LogDebug($"[SheepPen] {flock.Sheep.Count} sheep left in the vanilla Enclosure ({envName}) are penned - no roll there tonight.");
                    continue;
                }

                bool wolf = flock.Wolf || SavedBoardHolds(gm, flock.Env, WolfTickPatch.WolfId);
                bool bell = flock.Bell || SavedBoardHolds(gm, flock.Env, SheepBellUid);
                bool raid = RaidRaisesTonight(flock.Key);
                float raidMultiplier = raid ? RaidPredationMultiplier : 1f;
                float predationChance = (wolf ? GuardedPredationChance : PredationChance) * raidMultiplier;
                float escapeChance = EscapeChanceFor(wolf, bell);
                string guardText = wolf ? $" (the wolf left there stands guard; escape {Percent(EscapeChanceFor(true, false))} unbelled vs {Percent(EscapeChance)} without it)" : "";
                string raidText = raid ? $", doubled by a Harsh Wilds {_raidName} raid" : "";
                string bellText = bell ? " (halved by the Sheep Bell there)" : "";

                Logger?.LogInfo($"[SheepPen] away at {envName}: {flock.Sheep.Count} loose sheep/ram(s) left there roll at {Percent(predationChance)} predation{guardText}{raidText} and {Percent(escapeChance)} escape{bellText} tonight.");

                int lost = 0;
                foreach (var sheep in flock.Sheep)
                    if (RollAwaySheep(sheep, envName, predationChance, escapeChance, wolf, flock.InWornPen.Contains(sheep)))
                        lost++;
                if (lost > 0)
                    Logger?.LogInfo($"[SheepPen] away at {envName}: {lost} lost tonight, with no remains left (you were not there).");
            }
        }

        /// <summary>Rolls one background sheep; returns true when it was lost either way.</summary>
        private static bool RollAwaySheep(object sheep, string envName, float predationChance, float escapeChance, bool guarded, bool wornPen)
        {
            string uid = CardUtil.GetCardUniqueId(sheep) ?? "sheep";
            float roll = UnityEngine.Random.value;
            string where = wornPen ? "in a worn-through pen" : "left unpenned";
            if (guarded) where += ", wolf on guard";

            string what;
            if (roll < predationChance) what = "was lost to a predator";
            else if (roll < predationChance + escapeChance) what = "strayed";
            else return false;

            bool removed = CardUtil.TryRemoveCard(sheep);
            if (removed)
                Logger?.LogInfo($"[SheepPen] {uid} left at {envName} {what} while you were away ({where}).");
            else
                Logger?.LogWarning($"[SheepPen] {uid} left at {envName} {what} while you were away, but the card could not be removed; it stays.");
            return removed;
        }

        /// <summary>
        /// True when the environment's saved board (cards the game stored there when the player
        /// left, such as a companion told to "Stay Here") holds a card with this UniqueID.
        /// </summary>
        private static bool SavedBoardHolds(GameManager gm, EnvID env, string uid)
        {
            if (gm == null)
            {
                WarnAwayOnce("gm-type", "the GameManager instance is not the game's GameManager type, so a wolf or bell left in another environment cannot be read; flocks there roll unguarded and without the bell.");
                return false;
            }
            var all = gm.EnvironmentsData;
            if (all == null) return false;
            if (!all.TryGetValue(env.DictionnaryKey, out var data) || data == null) return false;
            return SavedListHolds(data.GetRegularCards, uid) || SavedListHolds(data.GetInventoryCards, uid);
        }

        private static bool SavedListHolds<T>(List<T> cards, string uid) where T : CardSaveDataByReference
        {
            if (cards == null) return false;
            string prefix = uid + "(";
            foreach (var c in cards)
            {
                string id = c?.CardID;
                if (id == null) continue;
                if (id == uid || id.StartsWith(prefix, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>
        /// The bare env UID: strips a "(Name)" annotation and an instanced env's "_parent" chain
        /// (memory reference_envdictkey_names_annotated_matching), which a vanilla hex UID never contains.
        /// </summary>
        private static string BareEnvUid(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            int cut = raw.IndexOfAny(new[] { '(', '_' });
            return cut > 0 ? raw.Substring(0, cut) : raw;
        }

        private static string Percent(float chance) => $"{(int)Math.Round(chance * 100f)}%";

        private static PenState GetPenState(object card)
        {
            var container = CardUtil.GetMemberValue(card, "CurrentContainer");
            if (container == null) return PenState.Loose;
            if (CardUtil.GetCardUniqueId(container) != PenUid) return PenState.Loose;

            float condition = CardUtil.GetDurability(container, "SpoilageTime");
            if (float.IsNaN(condition))
            {
                if (!_warnedConditionUnreadable)
                {
                    _warnedConditionUnreadable = true;
                    Logger?.LogWarning("[SheepPen] could not read a Sheep Pen's Fence Condition (SpoilageTime); treating the pen as intact. A worn pen will keep protecting its sheep until this is fixed.");
                }
                return PenState.Penned;
            }
            return condition <= WornThroughCondition ? PenState.WornPen : PenState.Penned;
        }

        private static bool IsInPlayerEnv(object card)
        {
            var env = CardUtil.GetMemberValue(card, "CardEnvironment");
            if (env == null) return false;
            return CardUtil.GetMemberValue(env, "MatchesPlayerEnv") is bool b && b;
        }
    }
}
