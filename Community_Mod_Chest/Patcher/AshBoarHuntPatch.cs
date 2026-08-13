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
    /// Ash's "boar hunt" side beat.
    ///
    /// When cmcInnCat's SpecialDurability2 timer fires it transforms him into
    /// cmcAshTrackingBoar on the player's board. A tick poll detects this card, immediately
    /// replaces it with a lightweight cmcAshBoarTrail note card (so the player sees he left),
    /// and sets cmcStatAshBoarHuntSpot=1. Once the player travels to Hunter's Crossing the same
    /// poll spawns cmcAshTrackingBoar there and latches the stat to 2.
    ///
    /// The "Corner the Boar" DismantleAction on cmcAshTrackingBoar (visible only at Hunter's
    /// Crossing) triggers Combat_EncounterBoar. This patch then resolves the hunt when the
    /// player wins that encounter while cmcAshTrackingBoar is on the current board.
    /// </summary>
    internal static class AshBoarHuntPatch
    {
        private const string TrackingCardUid        = "cmcAshTrackingBoar";
        private const string BoarTrailCardUid       = "cmcAshBoarTrail";
        private const string InnCatUid              = "cmcInnCat";
        private const string HuntersCrossingEnvUid  = "cmcEnvHuntersCrossing";

        // Village_Master_Plan.md §10.5 K8 — one-shot epilogue flag for the Wooden Shield gate.
        private const string BoarHuntResolvedStatUid = "cmcStatInnBoarHuntResolved";
        // Tracks Ash's hunt movement state (0=home, 1=departed, 2=placed at HC).
        private const string BoarHuntSpotStatUid    = "cmcStatAshBoarHuntSpot";

        private const string VillageEnvUid           = "cmcEnvVillage";
        // CT13 invisible helper with active spoilage — used to cleanly remove CT2 trail card.
        private const string BeastTracksInvisibleUid = "7dcea42d5b79ed7419b43c414a99194d";

        // Combat_EncounterBoar GUID.
        private const string BoarEncounterUid = "919913fdcc78e6845940f420fd273bb3";
        private const string BoarCarcassUid    = "24a35218ec733854387c5a66ef3e6628"; // Carcass_BoarCarcass
        private const int EnemyDefeatedResult = 1; // EncounterResult.EnemyDefeated

        private static bool _initialized;
        private static MethodInfo _getFromIdMethod;
        private static object _boarHuntResolvedStat;
        private static object _boarHuntSpotStat;

        public static void Initialize(Harmony harmony)
        {
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
                    Plugin.Logger.LogWarning("[AshBoarHuntPatch] EncounterPopup type not found — boar hunt resolution disabled.");
                    return;
                }

                var applyResult = encounterPopupType
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == "ApplyEncounterResult" && m.GetParameters().Length == 0);

                if (applyResult == null)
                {
                    Plugin.Logger.LogWarning("[AshBoarHuntPatch] EncounterPopup.ApplyEncounterResult() not found — boar hunt resolution disabled.");
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

        // Moves Ash off the player's board to Hunter's Crossing when the hunt timer fires.
        private static void CheckAndMoveAsh()
        {
            try
            {
                if (!ResolveGetFromId()) return;
                _boarHuntSpotStat ??= _getFromIdMethod.Invoke(null, new object[] { BoarHuntSpotStatUid });

                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;

                float spotState = ReadStat(gm, _boarHuntSpotStat);
                if (spotState < 0f) return;

                if (spotState < 0.5f)
                {
                    // Cleanup trail left over from a resolved hunt cycle.
                    var trailCard = FindCardByUid(gm, BoarTrailCardUid);
                    if (trailCard != null)
                    {
                        // SetDurability writes via reflection and never runs through the game's
                        // own tick loop, so it alone never fires SpoilageTime.OnZero (Destroy) —
                        // the card just sits at 0 forever and this branch re-finds it every poll.
                        // Zero it first (seeds the carried-over value), then hand it to the CT13
                        // invisible helper whose OWN active SpoilageTime the real tick DOES
                        // evaluate, so its OnZero (ModType 3 = Destroy) actually removes the card —
                        // same pattern already used below to remove the tracking card at state 0.
                        CardUtil.SetDurability(trailCard, "SpoilageTime", 0f);
                        if (CardUtil.TransformCardInPlace(trailCard, BeastTracksInvisibleUid))
                            Plugin.Logger.LogInfo("[AshBoarHuntPatch] Ash's trail has faded from the village.");
                        else
                            Plugin.Logger.LogWarning("[AshBoarHuntPatch] Failed to clear Ash's trail from the village — it will remain on the board.");
                        return;
                    }

                    // State 0: watch for the tracking card appearing on the current board.
                    var trackingCard = FindCardByUid(gm, TrackingCardUid);
                    if (trackingCard == null) return;

                    // Remove the tracking card from wherever it appeared; trail will be placed at the village.
                    if (CardUtil.TransformCardInPlace(trackingCard, BeastTracksInvisibleUid))
                    {
                        WriteStat(gm, _boarHuntSpotStat, 1f);
                        Plugin.Logger.LogInfo("[AshBoarHuntPatch] Ash has slipped off toward Hunter's Crossing.");
                    }
                    else
                    {
                        Plugin.Logger.LogWarning("[AshBoarHuntPatch] Failed to remove tracking card from board — Ash stays put.");
                    }
                }
                else if (spotState < 1.5f)
                {
                    // State 1: place trail at the village board; spawn Ash at Hunter's Crossing when player arrives.
                    var currentEnv = GameQuery.CurrentEnvironmentUniqueId;

                    if (string.Equals(currentEnv, VillageEnvUid, StringComparison.Ordinal))
                    {
                        if (FindCardByUid(gm, BoarTrailCardUid) == null)
                        {
                            // SpawnService.Spawn returns null on success (GiveCard is void this game
                            // version) — verify by re-querying the board, not the return value.
                            SpawnService.Spawn(BoarTrailCardUid);
                            if (FindCardByUid(gm, BoarTrailCardUid) != null)
                                Plugin.Logger.LogInfo("[AshBoarHuntPatch] Ash's trail marks the village — he's gone to Hunter's Crossing.");
                            else
                                Plugin.Logger.LogWarning("[AshBoarHuntPatch] Trail failed to appear at the village after spawn — cmcAshBoarTrail may not be loaded.");
                        }
                        return;
                    }

                    if (!string.Equals(currentEnv, HuntersCrossingEnvUid, StringComparison.Ordinal)) return;

                    if (FindCardByUid(gm, TrackingCardUid) != null)
                    {
                        // Already present (e.g. after a save/reload while at HC) — just latch.
                        WriteStat(gm, _boarHuntSpotStat, 2f);
                        return;
                    }

                    // SpawnService.Spawn returns null on success (GiveCard is void this game
                    // version) — verify by re-querying the board, not the return value, so the
                    // stat latches on the spawning tick instead of relying on next-tick self-heal.
                    SpawnService.Spawn(TrackingCardUid);
                    if (FindCardByUid(gm, TrackingCardUid) != null)
                    {
                        WriteStat(gm, _boarHuntSpotStat, 2f);
                        Plugin.Logger.LogInfo("[AshBoarHuntPatch] Ash is at Hunter's Crossing — his quarry is close.");
                    }
                    else
                    {
                        Plugin.Logger.LogWarning("[AshBoarHuntPatch] Ash failed to appear at Hunter's Crossing after spawn — cmcAshTrackingBoar may not be loaded.");
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
            try
            {
                if (!IsBoarVictory(__instance)) return;

                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;

                var trackingCard = FindCardByUid(gm, TrackingCardUid);
                if (trackingCard == null) return; // Ash isn't at the current location.

                if (CardUtil.TransformCardInPlace(trackingCard, InnCatUid))
                {
                    // Ash's stats are 0 from the inactive cmcAshTrackingBoar — set non-zero to prevent instant Wanders Off.
                    CardUtil.SetDurability(trackingCard, "SpecialDurability2", 480f); // fresh 5-day hunt cycle
                    CardUtil.SetDurability(trackingCard, "SpoilageTime",       288f); // ~50% hunger
                    CardUtil.SetDurability(trackingCard, "UsageDurability",    144f); // ~50% thirst
                    CardUtil.SetDurability(trackingCard, "SpecialDurability3",  50f); // 50% care
                    Plugin.Logger.LogInfo("[AshBoarHuntPatch] The boar is down — Ash trots home.");
                }
                else
                    Plugin.Logger.LogWarning("[AshBoarHuntPatch] Boar defeated but transforming Ash back to cmcInnCat failed.");

                var carcass = SpawnService.Spawn(BoarCarcassUid);
                if (carcass != null)
                    Plugin.Logger.LogInfo("[AshBoarHuntPatch] Boar carcass spawned.");
                else
                    Plugin.Logger.LogDebug("[AshBoarHuntPatch] SpawnService returned null for carcass — vanilla encounter loot likely already dropped it.");

                MarkBoarHuntEpilogueResolved(gm);
                ResetHuntSpotStat(gm); // allow the next 5-day hunt cycle to move Ash again
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[AshBoarHuntPatch] ApplyEncounterResult_Postfix failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static bool IsBoarVictory(object popup)
        {
            var currentEncounter = Reflect.GetMember(popup, "CurrentEncounter");
            if (currentEncounter == null) return false;
            var resultValue = Reflect.GetMember(currentEncounter, "EncounterResult");
            if (resultValue == null || Convert.ToInt32(resultValue) != EnemyDefeatedResult) return false;
            var model = Reflect.GetMember(currentEncounter, "EncounterModel");
            if (model == null) return false;
            return Reflect.GetMember(model, "UniqueID") as string == BoarEncounterUid;
        }

        private static void MarkBoarHuntEpilogueResolved(object gm)
        {
            if (!ResolveGetFromId()) return;
            _boarHuntResolvedStat ??= _getFromIdMethod.Invoke(null, new object[] { BoarHuntResolvedStatUid });
            if (ReadStat(gm, _boarHuntResolvedStat) >= 0.5f) return; // already set
            if (WriteStat(gm, _boarHuntResolvedStat, 1f))
                Plugin.Logger.LogInfo("[AshBoarHuntPatch] Boar-hunt epilogue resolved — Wooden Shield revealed.");
        }

        private static void ResetHuntSpotStat(object gm)
        {
            if (!ResolveGetFromId()) return;
            _boarHuntSpotStat ??= _getFromIdMethod.Invoke(null, new object[] { BoarHuntSpotStatUid });
            WriteStat(gm, _boarHuntSpotStat, 0f);
        }

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
