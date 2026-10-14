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
    /// One parameter set per bonded cat's away-trip (round 2 E4). A <see cref="AshBoarHuntPatch"/> route names the
    /// cat, where his trail is left, his quarry and his reward; the class runs every route in <see cref="Routes"/>.
    /// The first set is Ash's boar hunt exactly as it shipped; the second is Shadow's night hunt.
    /// </summary>
    internal sealed class CatHuntRoute
    {
        /// <summary>The cat's name, for log lines only (never written to a card: Ash is never renamed).</summary>
        public string Name;
        /// <summary>Prefix of every log line this route writes.</summary>
        public string LogTag;
        /// <summary>The cat card at home.</summary>
        public string CatUid;

        // ---- the trail tile ----
        /// <summary>The note card the player finds where the cat was last seen.</summary>
        public string TrailCardUid;
        /// <summary>The environment the trail is placed in. Null: the trail takes the cat's own place on the board
        /// he left from (the cat card is swapped in place for the trail and back).</summary>
        public string TrailEnvUid;

        // ---- the quarry ----
        /// <summary>The card that stands for the cat at his quarry (null: he is nowhere while away).</summary>
        public string AwayCardUid;
        /// <summary>The environment the quarry is in (null: the player never follows him).</summary>
        public string QuarryEnvUid;
        /// <summary>The encounter whose win ends the hunt (null: he comes home on his own when the trip is over).</summary>
        public string QuarryEncounterUid;

        // ---- the reward ----
        /// <summary>The cards one of which the hunt brings back (one is picked).</summary>
        public string[] RewardUids;
        /// <summary>Starting stats for each reward, same order (null entry: the card's own defaults).</summary>
        public Dictionary<string, float>[] RewardStats;

        // ---- state ----
        /// <summary>Hidden GameStat: 0 home, 1 away (and, for a quarry route, 2 placed at the quarry).</summary>
        public string SpotStatUid;
        /// <summary>Hidden one-shot GameStat set the first time the quarry falls (null: none).</summary>
        public string EpilogueStatUid;
        /// <summary>The cat's stats written when he comes back, in order (stat name, value).</summary>
        public KeyValuePair<string, float>[] ReturnStats;

        // ---- what sends him (a route sets exactly one of the two) ----
        /// <summary>True: the cat's own JSON timer turns him into <see cref="AwayCardUid"/> (Ash's SpecialDurability2).</summary>
        public bool SentByOwnTimer;
        /// <summary>Hidden per-cat clamped cooldown GameStat, drained by its own JSON rate (null: no cooldown route).</summary>
        public string CooldownStatUid;
        /// <summary>The value the cooldown is set to when he leaves (its JSON MinMaxValue.y).</summary>
        public float CooldownTicks;
        /// <summary>How long the trip lasts, in ticks drained off the cooldown since he left.</summary>
        public float TripTicks;
        /// <summary>He leaves only at or above this Care (SpecialDurability3).</summary>
        public float CareFloor;
        /// <summary>He leaves only between these clock hours (start inclusive, end exclusive, across midnight).</summary>
        public float NightStartHour;
        public float NightEndHour;

        // ---- log lines (Ash's are the shipped text, byte for byte) ----
        public string MsgTrailFaded, MsgTrailFadeFailed, MsgDeparted, MsgDepartFailed, MsgTrailPlaced, MsgTrailMissing,
                      MsgAtQuarry, MsgAtQuarryMissing, MsgResolved, MsgResolveFailed, MsgRewardSpawned, MsgRewardNull,
                      MsgEpilogue, MsgCameHome;
    }

    /// <summary>
    /// Away-trips for any bonded cat (round 2 E4), run from one parameter set per cat (<see cref="CatHuntRoute"/>).
    ///
    /// <para><b>Ash</b> (the first set, behaving exactly as it shipped): when cmcInnCat's SpecialDurability2 timer
    /// fires it transforms him into cmcAshTrackingBoar on the player's board. A tick poll detects this card,
    /// immediately replaces it with a lightweight cmcAshBoarTrail note card (so the player sees he left), and sets
    /// cmcStatAshBoarHuntSpot=1. Once the player travels to Hunter's Crossing the same poll spawns cmcAshTrackingBoar
    /// there and latches the stat to 2. The "Corner the Boar" DismantleAction on cmcAshTrackingBoar (visible only at
    /// Hunter's Crossing) triggers Combat_EncounterBoar. This patch then resolves the hunt when the player wins that
    /// encounter while cmcAshTrackingBoar is on the current board.</para>
    ///
    /// <para><b>Shadow</b> (the second set): a night trip toward the Foraging Forest. While Shadow is set down on the
    /// player's own board at night, with Care at <see cref="ShadowCareFloor"/> or more and his clamped per-cat
    /// cooldown (cmcStatShadowHuntCooldown) drained to 0, he slips off: his card is swapped in place for
    /// cmcShadowHuntTrail (which holds his Hunger, Thirst and Care, frozen), the cooldown is set to
    /// <see cref="ShadowCooldownTicks"/> and cmcStatShadowHuntSpot to 1. Once <see cref="ShadowTripTicks"/> of that
    /// cooldown have drained, the next poll that finds his trail on the player's board swaps it back into Shadow and
    /// lands his find (a Squirrel or Partridge carcass) beside him. A poll that does not find the trail (the player
    /// is elsewhere, or a just-entered board is still being rebuilt) does nothing and is retried, so nothing is
    /// latched before the work is done (root CLAUDE.md "Environment-entry logic must RETRY"). Every Shadow lookup is
    /// scoped to the player's board (<c>GameQuery.CardsInPlayerEnv</c>, memory reference_allcards_env_scoped). Every
    /// miss leaves a warn-once line keyed on its cause. The numbers are tunable placeholders.</para>
    /// </summary>
    internal static class AshBoarHuntPatch
    {
        private const string TrackingCardUid        = "cmcAshTrackingBoar";
        private const string BoarTrailCardUid       = "cmcAshBoarTrail";
        private const string InnCatUid              = "cmcInnCat";
        private const string HuntersCrossingEnvUid  = "cmcEnvHuntersCrossing";

        // Village_Master_Plan.md section 10.5 K8: one-shot epilogue flag for the Wooden Shield gate.
        private const string BoarHuntResolvedStatUid = "cmcStatInnBoarHuntResolved";
        // Tracks Ash's hunt movement state (0=home, 1=departed, 2=placed at HC).
        private const string BoarHuntSpotStatUid    = "cmcStatAshBoarHuntSpot";

        private const string VillageEnvUid           = "cmcEnvVillage";
        // CT13 invisible helper with active spoilage: used to cleanly remove CT2 trail card.
        private const string BeastTracksInvisibleUid = "7dcea42d5b79ed7419b43c414a99194d";

        // Combat_EncounterBoar GUID.
        private const string BoarEncounterUid = "919913fdcc78e6845940f420fd273bb3";
        private const string BoarCarcassUid    = "24a35218ec733854387c5a66ef3e6628"; // Carcass_BoarCarcass
        private const int EnemyDefeatedResult = 1; // EncounterResult.EnemyDefeated

        // ---- Shadow's night hunt ----
        internal const string ShadowCatUid             = "cmcShadowCatTamed";
        internal const string ShadowTrailCardUid       = "cmcShadowHuntTrail";
        internal const string ShadowHuntSpotStatUid    = "cmcStatShadowHuntSpot";
        internal const string ShadowHuntCooldownStatUid = "cmcStatShadowHuntCooldown";
        internal const string SquirrelCarcassUid       = "e6b341c0770197047a5ad6470476bbd4"; // Carcass_SquirrelCarcass
        internal const string PartridgeCarcassUid      = "f72ca8f6249870d4b9306c2d779c93a9"; // Carcass_PartridgeCarcass

        /// <summary>TUNABLE PLACEHOLDER: Shadow leaves only at or above this Care.</summary>
        internal const float ShadowCareFloor = 70f;
        /// <summary>TUNABLE PLACEHOLDER: ticks between two of Shadow's trips (three days); the cooldown stat's maximum.</summary>
        internal const float ShadowCooldownTicks = 288f;
        /// <summary>TUNABLE PLACEHOLDER: how long Shadow stays out, in ticks (six hours).</summary>
        internal const float ShadowTripTicks = 24f;
        /// <summary>TUNABLE PLACEHOLDER: the night window Shadow hunts in (his prowl hours, ShadowCatPatch).</summary>
        internal const float ShadowNightStartHour = 17f;
        internal const float ShadowNightEndHour = 6f;

        /// <summary>The cats' Hunger, Thirst and Care stats (both cats).</summary>
        internal const string HungerStatName = "SpoilageTime";
        internal const string ThirstStatName = "UsageDurability";
        internal const string CareStatName = "SpecialDurability3";

        /// <summary>TUNABLE PLACEHOLDER: the least Hunger, Thirst and Care Shadow comes home with (half his Hunger
        /// and Thirst, Ash's 50 Care after his hunt), so a value lost on the way never lands him at 0, where his own
        /// OnZero sends him off for good.</summary>
        internal const float ShadowReturnHungerFloor = 288f;
        internal const float ShadowReturnThirstFloor = 144f;
        internal const float ShadowReturnCareFloor = 50f;

        /// <summary>RNG hook for the find: -1 = unseeded, 0 or above = a replayable sequence. Written by harness scenarios.</summary>
        public static int RngSeed = -1;
        private static int _seededWith = int.MinValue;
        private static Random _rng;

        /// <summary>The first parameter set: Ash's boar hunt, as shipped.</summary>
        internal static readonly CatHuntRoute Ash = new CatHuntRoute
        {
            Name = "Ash",
            LogTag = "[AshBoarHuntPatch] ",
            CatUid = InnCatUid,
            TrailCardUid = BoarTrailCardUid,
            TrailEnvUid = VillageEnvUid,
            AwayCardUid = TrackingCardUid,
            QuarryEnvUid = HuntersCrossingEnvUid,
            QuarryEncounterUid = BoarEncounterUid,
            RewardUids = new[] { BoarCarcassUid },
            RewardStats = new Dictionary<string, float>[] { null },
            SpotStatUid = BoarHuntSpotStatUid,
            EpilogueStatUid = BoarHuntResolvedStatUid,
            ReturnStats = new[]
            {
                // Ash's stats are 0 from the inactive cmcAshTrackingBoar: set non-zero to prevent instant Wanders Off.
                new KeyValuePair<string, float>("SpecialDurability2", 0f),   // fresh 5-day hunt cycle (counts up to 480 since 1.69.12)
                new KeyValuePair<string, float>("SpoilageTime",       288f), // ~50% hunger
                new KeyValuePair<string, float>("UsageDurability",    144f), // ~50% thirst
                new KeyValuePair<string, float>("SpecialDurability3",  50f), // 50% care
            },
            SentByOwnTimer = true,
            MsgTrailFaded = "Ash's trail has faded from the village.",
            MsgTrailFadeFailed = "Failed to clear Ash's trail from the village \u2014 it will remain on the board.",
            MsgDeparted = "Ash has slipped off toward Hunter's Crossing.",
            MsgDepartFailed = "Failed to remove tracking card from board \u2014 Ash stays put.",
            MsgTrailPlaced = "Ash's trail marks the village \u2014 he's gone to Hunter's Crossing.",
            MsgTrailMissing = "Trail failed to appear at the village after spawn \u2014 cmcAshBoarTrail may not be loaded.",
            MsgAtQuarry = "Ash is at Hunter's Crossing \u2014 his quarry is close.",
            MsgAtQuarryMissing = "Ash failed to appear at Hunter's Crossing after spawn \u2014 cmcAshTrackingBoar may not be loaded.",
            MsgResolved = "The boar is down \u2014 Ash trots home.",
            MsgResolveFailed = "Boar defeated but transforming Ash back to cmcInnCat failed.",
            MsgRewardSpawned = "Boar carcass spawned.",
            MsgRewardNull = "SpawnService returned null for carcass \u2014 vanilla encounter loot likely already dropped it.",
            MsgEpilogue = "Boar-hunt epilogue resolved \u2014 Wooden Shield revealed.",
        };

        /// <summary>The second parameter set: Shadow's night hunt toward the Foraging Forest.</summary>
        internal static readonly CatHuntRoute Shadow = new CatHuntRoute
        {
            Name = "Shadow",
            LogTag = "[CatHunt] ",
            CatUid = ShadowCatUid,
            TrailCardUid = ShadowTrailCardUid,
            TrailEnvUid = null,          // his trail takes his place on the board he left
            AwayCardUid = null,          // he is nowhere while away
            QuarryEnvUid = null,         // the player never follows him into the Foraging Forest
            QuarryEncounterUid = null,   // he comes home on his own
            RewardUids = new[] { SquirrelCarcassUid, PartridgeCarcassUid },
            RewardStats = new[]
            {
                // the vanilla cards' own starting values (CardData/Carcass_*.json): fresh, at their default Quality
                new Dictionary<string, float> { { "SpoilageTime", 0f }, { "SpecialDurability2", 48f } },
                new Dictionary<string, float> { { "SpoilageTime", 0f }, { "SpecialDurability2", 62f } },
            },
            SpotStatUid = ShadowHuntSpotStatUid,
            EpilogueStatUid = null,
            ReturnStats = null,          // he comes home with the stats his trail held (see ReturnValue)
            SentByOwnTimer = false,
            CooldownStatUid = ShadowHuntCooldownStatUid,
            CooldownTicks = ShadowCooldownTicks,
            TripTicks = ShadowTripTicks,
            CareFloor = ShadowCareFloor,
            NightStartHour = ShadowNightStartHour,
            NightEndHour = ShadowNightEndHour,
            MsgDeparted = "Shadow has slipped off into the dark toward the Foraging Forest.",
            MsgDepartFailed = "Shadow could not be swapped for his trail, so he stays put.",
            MsgCameHome = "Shadow is back from his night hunt with a find.",
            MsgResolveFailed = "Shadow's trip is over but his trail could not be swapped back into him.",
        };

        /// <summary>Every route, Ash's first.</summary>
        internal static readonly CatHuntRoute[] Routes = { Ash, Shadow };

        private static bool _initialized;
        private static bool _tripsInitialized;
        private static MethodInfo _getFromIdMethod;
        // GameStat SOs by UniqueID, cached once resolved (a null lookup is retried on the next call).
        private static readonly Dictionary<string, object> _statSos = new Dictionary<string, object>(StringComparer.Ordinal);
        private static readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);

        private static void WarnOnce(CatHuntRoute route, string cause, string message)
        {
            if (_warned.Add(route.Name + "|" + cause)) Plugin.Logger.LogWarning(route.LogTag + message);
        }

        // ---- pure decisions (unit-checked in Development_Tools/Tests/CMC-Round2.Tests.ps1) ----

        /// <summary>True inside the night window [start, 24) or [0, end). NaN is never night.</summary>
        internal static bool IsNight(float hour, float start, float end)
        {
            if (float.IsNaN(hour)) return false;
            return hour >= start || hour < end;
        }

        /// <summary>True when a cooldown route's cat may leave now: Care at or above the floor, the cooldown fully
        /// drained, and night. An unreadable Care or cooldown (NaN, or a negative -1 sentinel) never sends him.</summary>
        internal static bool CanDepart(CatHuntRoute route, float care, float cooldown, float hour)
        {
            if (float.IsNaN(care) || float.IsNaN(cooldown) || cooldown < 0f) return false;
            if (care < route.CareFloor) return false;
            if (cooldown > 0f) return false;
            return IsNight(hour, route.NightStartHour, route.NightEndHour);
        }

        /// <summary>True once the trip is over: at least <c>TripTicks</c> have drained off the cooldown set when he
        /// left. An unreadable cooldown (NaN or negative) is never over.</summary>
        internal static bool ReturnReady(CatHuntRoute route, float cooldown)
        {
            if (float.IsNaN(cooldown) || cooldown < 0f) return false;
            return cooldown <= route.CooldownTicks - route.TripTicks;
        }

        /// <summary>A stat the cat comes home with: what his trail held, never below the floor nor above the max.</summary>
        internal static float ReturnValue(float carried, float floor, float max)
        {
            if (float.IsNaN(carried)) return floor;
            float v = Math.Max(carried, floor);
            return float.IsNaN(max) || max <= 0f ? v : Math.Min(max, v);
        }

        /// <summary>Which of the route's rewards a draw in [0, 1) picks, in equal shares.</summary>
        internal static int PickReward(CatHuntRoute route, double roll)
        {
            int n = route.RewardUids.Length;
            int i = (int)(roll * n);
            return i < 0 ? 0 : (i >= n ? n - 1 : i);
        }

        internal static double NextRoll()
        {
            if (_rng == null || _seededWith != RngSeed)
            {
                _rng = RngSeed >= 0 ? new Random(RngSeed) : new Random();
                _seededWith = RngSeed;
            }
            return _rng.NextDouble();
        }

        public static void Initialize(Harmony harmony)
        {
            InitializeTrips();

            if (_initialized) return;
            _initialized = true;

            try
            {
                Type encounterPopupType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try { encounterPopupType = asm.GetType("EncounterPopup", false); }
                    catch (Exception ex) { Plugin.Logger.LogDebug($"[AshBoarHuntPatch] GetType probe failed on '{asm.FullName}': {ex.Message}"); }
                    if (encounterPopupType != null) break;
                }

                if (encounterPopupType == null)
                {
                    Plugin.Logger.LogWarning("[AshBoarHuntPatch] EncounterPopup type not found \u2014 boar hunt resolution disabled.");
                    return;
                }

                var applyResult = encounterPopupType
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == "ApplyEncounterResult" && m.GetParameters().Length == 0);

                if (applyResult == null)
                {
                    Plugin.Logger.LogWarning("[AshBoarHuntPatch] EncounterPopup.ApplyEncounterResult() not found \u2014 boar hunt resolution disabled.");
                    return;
                }

                var postfix = typeof(AshBoarHuntPatch).GetMethod(nameof(ApplyEncounterResult_Postfix),
                    BindingFlags.Static | BindingFlags.NonPublic);
                harmony.Patch(applyResult, postfix: new HarmonyMethod(postfix));

                TickEvents.Interval(2f, CheckAndMoveAsh, "AshBoarHuntMovement");

                Plugin.Logger.LogDebug("[AshBoarHuntPatch] initialized.");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[AshBoarHuntPatch] Initialize failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // The cooldown routes need no encounter patch, so their poll is registered on its own, once per process, and
        // a missing EncounterPopup (which disables Ash's movement poll, as it always has) does not stop Shadow's trips.
        private static void InitializeTrips()
        {
            if (_tripsInitialized) return;
            _tripsInitialized = true;
            TickEvents.Interval(2f, CheckTrips, "CatHuntTrips");
        }

        // ================= quarry routes (Ash) =================

        // Moves Ash off the player's board to Hunter's Crossing when the hunt timer fires.
        private static void CheckAndMoveAsh()
        {
            foreach (var route in Routes)
                if (route.SentByOwnTimer) CheckAndMove(route);
        }

        private static void CheckAndMove(CatHuntRoute route)
        {
            try
            {
                if (!ResolveGetFromId()) return;
                var spotStat = StatSo(route.SpotStatUid);

                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;

                float spotState = ReadStat(gm, spotStat);
                if (spotState < 0f) return;

                if (spotState < 0.5f)
                {
                    // Cleanup trail left over from a resolved hunt cycle.
                    var trailCard = FindCardByUid(gm, route.TrailCardUid);
                    if (trailCard != null)
                    {
                        // SetDurability writes via reflection and never runs through the game's
                        // own tick loop, so it alone never fires SpoilageTime.OnZero (Destroy):
                        // the card just sits at 0 forever and this branch re-finds it every poll.
                        // Zero it first (seeds the carried-over value), then hand it to the CT13
                        // invisible helper whose OWN active SpoilageTime the real tick DOES
                        // evaluate, so its OnZero (ModType 3 = Destroy) actually removes the card,
                        // the same pattern already used below to remove the tracking card at state 0.
                        CardUtil.SetDurability(trailCard, "SpoilageTime", 0f);
                        if (CardUtil.TransformCardInPlace(trailCard, BeastTracksInvisibleUid))
                            Plugin.Logger.LogInfo(route.LogTag + route.MsgTrailFaded);
                        else
                            Plugin.Logger.LogWarning(route.LogTag + route.MsgTrailFadeFailed);
                        return;
                    }

                    // State 0: watch for the tracking card appearing on the current board.
                    var trackingCard = FindCardByUid(gm, route.AwayCardUid);
                    if (trackingCard == null) return;

                    // Remove the tracking card from wherever it appeared; trail will be placed at the village.
                    if (CardUtil.TransformCardInPlace(trackingCard, BeastTracksInvisibleUid))
                    {
                        WriteStat(gm, spotStat, 1f);
                        Plugin.Logger.LogInfo(route.LogTag + route.MsgDeparted);
                    }
                    else
                    {
                        Plugin.Logger.LogWarning(route.LogTag + route.MsgDepartFailed);
                    }
                }
                else if (spotState < 1.5f)
                {
                    // State 1: place trail at the village board; spawn Ash at Hunter's Crossing when player arrives.
                    var currentEnv = GameQuery.CurrentEnvironmentUniqueId;

                    if (string.Equals(currentEnv, route.TrailEnvUid, StringComparison.Ordinal))
                    {
                        if (FindCardByUid(gm, route.TrailCardUid) == null)
                        {
                            // SpawnService.Spawn returns null on success (GiveCard is void this game
                            // version): verify by re-querying the board, not the return value.
                            SpawnService.Spawn(route.TrailCardUid);
                            if (FindCardByUid(gm, route.TrailCardUid) != null)
                                Plugin.Logger.LogInfo(route.LogTag + route.MsgTrailPlaced);
                            else
                                Plugin.Logger.LogWarning(route.LogTag + route.MsgTrailMissing);
                        }
                        return;
                    }

                    if (!string.Equals(currentEnv, route.QuarryEnvUid, StringComparison.Ordinal)) return;

                    if (FindCardByUid(gm, route.AwayCardUid) != null)
                    {
                        // Already present (e.g. after a save/reload while at HC): just latch.
                        WriteStat(gm, spotStat, 2f);
                        return;
                    }

                    // SpawnService.Spawn returns null on success (GiveCard is void this game
                    // version): verify by re-querying the board, not the return value, so the
                    // stat latches on the spawning tick instead of relying on next-tick self-heal.
                    SpawnService.Spawn(route.AwayCardUid);
                    if (FindCardByUid(gm, route.AwayCardUid) != null)
                    {
                        WriteStat(gm, spotStat, 2f);
                        Plugin.Logger.LogInfo(route.LogTag + route.MsgAtQuarry);
                    }
                    else
                    {
                        Plugin.Logger.LogWarning(route.LogTag + route.MsgAtQuarryMissing);
                    }
                }
                // State 2: already placed, nothing to do.
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[AshBoarHuntPatch] CheckAndMoveAsh failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void ApplyEncounterResult_Postfix(object __instance)
        {
            foreach (var route in Routes)
                if (route.QuarryEncounterUid != null) ResolveQuarry(__instance, route);
        }

        private static void ResolveQuarry(object popup, CatHuntRoute route)
        {
            try
            {
                if (!IsQuarryVictory(popup, route.QuarryEncounterUid)) return;

                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;

                var trackingCard = FindCardByUid(gm, route.AwayCardUid);
                if (trackingCard == null) return; // Ash isn't at the current location.

                if (CardUtil.TransformCardInPlace(trackingCard, route.CatUid))
                {
                    foreach (var stat in route.ReturnStats)
                        CardUtil.SetDurability(trackingCard, stat.Key, stat.Value);
                    Plugin.Logger.LogInfo(route.LogTag + route.MsgResolved);
                }
                else
                    Plugin.Logger.LogWarning(route.LogTag + route.MsgResolveFailed);

                // SpawnService.Spawn returns null on SUCCESS (GiveCard is void; the card is placed as a side effect) and
                // logs its own Error on a failed spawn, so the reward line is logged after the call, not gated on a handle.
                SpawnService.Spawn(route.RewardUids[0], route.RewardStats[0]);
                Plugin.Logger.LogInfo(route.LogTag + route.MsgRewardSpawned);

                MarkBoarHuntEpilogueResolved(gm, route);
                ResetHuntSpotStat(gm, route); // allow the next 5-day hunt cycle to move Ash again
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[AshBoarHuntPatch] ApplyEncounterResult_Postfix failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static bool IsQuarryVictory(object popup, string encounterUid)
        {
            var currentEncounter = Reflect.GetMember(popup, "CurrentEncounter");
            if (currentEncounter == null) return false;
            var resultValue = Reflect.GetMember(currentEncounter, "EncounterResult");
            if (resultValue == null || Convert.ToInt32(resultValue) != EnemyDefeatedResult) return false;
            var model = Reflect.GetMember(currentEncounter, "EncounterModel");
            if (model == null) return false;
            return Reflect.GetMember(model, "UniqueID") as string == encounterUid;
        }

        private static void MarkBoarHuntEpilogueResolved(object gm, CatHuntRoute route)
        {
            if (route.EpilogueStatUid == null) return;
            if (!ResolveGetFromId()) return;
            var resolvedStat = StatSo(route.EpilogueStatUid);
            if (ReadStat(gm, resolvedStat) >= 0.5f) return; // already set
            if (WriteStat(gm, resolvedStat, 1f))
                Plugin.Logger.LogInfo(route.LogTag + route.MsgEpilogue);
        }

        private static void ResetHuntSpotStat(object gm, CatHuntRoute route)
        {
            if (!ResolveGetFromId()) return;
            WriteStat(gm, StatSo(route.SpotStatUid), 0f);
        }

        // ================= cooldown routes (Shadow) =================

        private static void CheckTrips()
        {
            // No run yet (main menu) or between runs: every stat read fails there, which is not broken.
            if (CardUtil.GetGameManagerInstance() == null) return;
            foreach (var route in Routes)
                if (route.CooldownStatUid != null) CheckTrip(route);
        }

        private static void CheckTrip(CatHuntRoute route)
        {
            try
            {
                float spot = HiddenStat.Get(route.SpotStatUid);
                if (spot < 0f)
                {
                    WarnOnce(route, "spot-unreadable", route.Name + "'s hunt state stat is unreadable, so his trip was not checked.");
                    return;
                }
                float cooldown = HiddenStat.Get(route.CooldownStatUid);
                if (cooldown < 0f)
                {
                    WarnOnce(route, "cooldown-unreadable", route.Name + "'s hunt cooldown stat is unreadable, so his trip was not checked.");
                    return;
                }

                if (spot < 0.5f) TryDepart(route, cooldown);
                else TryComeHome(route, cooldown);
            }
            catch (Exception ex)
            {
                WarnOnce(route, "trip-exception", "CheckTrip failed: " + (ex.InnerException?.ToString() ?? ex.ToString()));
            }
        }

        private static void TryDepart(CatHuntRoute route, float cooldown)
        {
            if (cooldown > 0f) return;
            float hour = GameQuery.HourOfDay;
            if (!IsNight(hour, route.NightStartHour, route.NightEndHour)) return;

            var cat = FindOnPlayerBoard(route.CatUid);
            if (cat == null) return;

            float care = CardUtil.GetDurability(cat, CareStatName);
            if (float.IsNaN(care))
            {
                WarnOnce(route, "care-unreadable", route.Name + "'s Care stat is unreadable, so he did not leave on a hunt.");
                return;
            }
            if (!CanDepart(route, care, cooldown, hour)) return;
            if (!IsSetDown(route, cat)) return;

            float hunger = CardUtil.GetDurability(cat, HungerStatName);
            float thirst = CardUtil.GetDurability(cat, ThirstStatName);

            if (!CardUtil.TransformCardInPlace(cat, route.TrailCardUid))
            {
                WarnOnce(route, "depart-transform-failed", route.MsgDepartFailed);
                return;
            }
            CardFinder.Invalidate();

            // The trail holds his stats, frozen (its own stats run at rate 0), until he comes back.
            bool held = CardUtil.SetDurability(cat, HungerStatName, hunger)
                      & CardUtil.SetDurability(cat, ThirstStatName, thirst)
                      & CardUtil.SetDurability(cat, CareStatName, care);
            if (!held) WarnOnce(route, "trail-stats-write-failed", route.Name + "'s stats could not all be written onto his trail; he comes home at the return floors.");

            if (!HiddenStat.Set(route.SpotStatUid, 1f))
            {
                // Undo: a trail with the state still at 0 would never be found again.
                WarnOnce(route, "spot-write-failed", route.Name + "'s hunt state could not be written, so he was put back and stays home.");
                if (CardUtil.TransformCardInPlace(cat, route.CatUid))
                {
                    CardFinder.Invalidate();
                    WriteReturnStats(route, cat, hunger, thirst, care);
                }
                else WarnOnce(route, "undo-transform-failed", route.Name + "'s trail could not be turned back into him after a failed state write.");
                return;
            }
            if (!HiddenStat.Set(route.CooldownStatUid, route.CooldownTicks))
                WarnOnce(route, "cooldown-write-failed", route.Name + "'s hunt cooldown could not be written, so this trip ends on the next check.");

            Plugin.Logger.LogInfo(route.LogTag + route.MsgDeparted);
        }

        private static void TryComeHome(CatHuntRoute route, float cooldown)
        {
            if (!ReturnReady(route, cooldown)) return;

            // Not here (the player is elsewhere, or this board is still being rebuilt on entry): retry next poll.
            var trail = FindOnPlayerBoard(route.TrailCardUid);
            if (trail == null) return;

            float hunger = CardUtil.GetDurability(trail, HungerStatName);
            float thirst = CardUtil.GetDurability(trail, ThirstStatName);
            float care = CardUtil.GetDurability(trail, CareStatName);

            if (!CardUtil.TransformCardInPlace(trail, route.CatUid))
            {
                WarnOnce(route, "return-transform-failed", route.MsgResolveFailed);
                return;
            }
            CardFinder.Invalidate();
            WriteReturnStats(route, trail, hunger, thirst, care);

            if (!HiddenStat.Set(route.SpotStatUid, 0f))
                WarnOnce(route, "spot-reset-failed", route.Name + "'s hunt state could not be reset after he came home, so he will not leave again.");

            int pick = PickReward(route, NextRoll());
            SpawnService.Spawn(route.RewardUids[pick], route.RewardStats[pick]);
            Plugin.Logger.LogInfo(route.LogTag + route.MsgCameHome + " (" + route.RewardUids[pick] + ")");
        }

        private static void WriteReturnStats(CatHuntRoute route, object cat, float hunger, float thirst, float care)
        {
            bool ok = CardUtil.SetDurability(cat, HungerStatName, ReturnValue(hunger, ShadowReturnHungerFloor, CardUtil.GetDurabilityMax(cat, HungerStatName)))
                    & CardUtil.SetDurability(cat, ThirstStatName, ReturnValue(thirst, ShadowReturnThirstFloor, CardUtil.GetDurabilityMax(cat, ThirstStatName)))
                    & CardUtil.SetDurability(cat, CareStatName, ReturnValue(care, ShadowReturnCareFloor, CardUtil.GetDurabilityMax(cat, CareStatName)));
            if (!ok) WarnOnce(route, "return-stats-write-failed", route.Name + "'s Hunger, Thirst or Care could not be written when he came home.");
        }

        // The first card with this UID on the player's own board (GameQuery.CardsInPlayerEnv pairs the UID test with the
        // card's environment, memory reference_allcards_env_scoped), never a carried or off-board one.
        private static object FindOnPlayerBoard(string uid)
        {
            foreach (var card in GameQuery.CardsInPlayerEnv())
                if (card != null && string.Equals(CardUtil.GetCardUniqueId(card), uid, StringComparison.Ordinal)) return card;
            return null;
        }

        // Set down on the board: in no container, in the board's Item or Base row, never in the hand or equipped. An
        // unreadable slot counts as not set down (he does not leave), so a carried cat is never swapped for a trail.
        private static bool IsSetDown(CatHuntRoute route, object card)
        {
            if (Reflect.GetMember(card, "CurrentContainer") is UnityEngine.Object container && container != null) return false;
            var slotInfo = Reflect.GetMember(card, "CurrentSlotInfo");
            if (slotInfo == null)
            {
                WarnOnce(route, "slot-unreadable", route.Name + "'s CurrentSlotInfo is unreadable, so he did not leave on a hunt.");
                return false;
            }
            var slotType = Reflect.GetMember(slotInfo, "SlotType");
            if (slotType == null)
            {
                WarnOnce(route, "slot-type-unreadable", route.Name + "'s SlotType is unreadable, so he did not leave on a hunt.");
                return false;
            }
            string name = slotType.ToString();
            return name == "Item" || name == "Base";
        }

        // ================= shared =================

        private static bool ResolveGetFromId()
        {
            if (_getFromIdMethod != null) return true;
            var uidType = CardUtil.FindGameType("UniqueIDScriptable");
            if (uidType == null) return false;
            _getFromIdMethod = uidType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "GetFromID" && !m.IsGenericMethodDefinition
                    && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
            return _getFromIdMethod != null;
        }

        private static object StatSo(string uid)
        {
            if (_statSos.TryGetValue(uid, out var so) && so != null) return so;
            so = _getFromIdMethod.Invoke(null, new object[] { uid });
            if (so != null) _statSos[uid] = so;
            return so;
        }

        private static float ReadStat(object gm, object statSo)
        {
            if (statSo == null) return -1f;
            if (Reflect.GetMember(gm, "StatsDict") is not IDictionary statsDict) return -1f;
            if (!statsDict.Contains(statSo)) return -1f;
            var inGameStat = statsDict[statSo];
            if (inGameStat == null) return -1f;
            return Reflect.GetMember(inGameStat, "SimpleCurrentValue") is float f ? f : -1f;
        }

        private static bool WriteStat(object gm, object statSo, float value)
        {
            if (statSo == null) return false;
            if (Reflect.GetMember(gm, "StatsDict") is not IDictionary statsDict) return false;
            if (!statsDict.Contains(statSo)) return false;
            var inGameStat = statsDict[statSo];
            if (inGameStat == null) return false;
            return Reflect.SetMember(inGameStat, "CurrentBaseValue", value);
        }

        private static object FindCardByUid(object gm, string uid)
        {
            if (Reflect.GetMember(gm, "AllCards") is not IEnumerable allCards) return null;
            foreach (var card in allCards)
            {
                if (card == null) continue;
                var model = Reflect.GetMember(card, "CardModel");
                if (model == null) continue;
                if (Reflect.GetMember(model, "UniqueID") as string == uid) return card;
            }
            return null;
        }
    }
}
