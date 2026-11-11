using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using UnityEngine;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Clears an uncleared seasonal road obstacle (the snow drifts, the autumn Fallen Pine, the spring
    /// Flooded Ford and the summer Bramble Thicket) once its season is over, from the live board and
    /// from every saved board (plan 1.4, Chiwei, Nexus c178551927: "a snowdrift will remain year-round
    /// if I don't deal with it").
    ///
    /// <para><b>Why it stayed.</b> Each obstacle is the challenge card of a Marker-model
    /// <c>SealableGates</c> entry in <c>WorldMap/MapNodes.json</c> with a Season <c>SealTrigger</c>. When
    /// the season ends the framework's gate opens the road (its unlocked closure reads
    /// <c>!SealTrigger</c>), but nothing removes the card the framework seeded, so an obstacle the player
    /// left alone kept standing beside an open road all year. No CMC doc calls that intended: the README
    /// says each blocker "stays open until its season returns" (a CLEARED one), and the cards say the
    /// road is blocked "in season".</para>
    ///
    /// <para><b>What it does.</b> Every second (cheap: it acts only after travel, a season change, or
    /// every 15 s as a safety net) it removes, when the current season is known and is not the
    /// obstacle's season: (1) any obstacle card on the live board, with the game's own
    /// <c>RemoveCard(..., RemoveOption.RemoveAll)</c> call that <c>ChangeEnvironment</c> uses to clear a
    /// board; (2) any obstacle card in a saved board's <c>AllRegularCards</c> / <c>AllInventoryCards</c>.
    /// The road is already open at that point, so nothing about travel changes.</para>
    ///
    /// <para><b>Next season (softlock guard).</b> The framework seeds a challenge card at most once per
    /// environment per session (<c>SealableGateService.GateState.SeedRequestedEnvUIDs</c>, cleared only on
    /// a run start or a reseal), because before this patch the uncleared card simply stayed. Removing the
    /// card without clearing that set would leave next season's road sealed with no card to clear in a
    /// session that is never reloaded. So the patch clears that gate's set first, through reflection, and
    /// removes NOTHING when the set cannot be reached (an older or newer framework): the old behaviour (a
    /// card that stays) is safe, a missing card is not. Next season the framework then seeds a fresh,
    /// full-strength obstacle as usual.</para>
    ///
    /// <para><b>Background cards (third pass).</b> An obstacle with actions is not an inert CT2 fixture, so the
    /// framework's <c>AlwaysUpdateService</c> forces <c>AlwaysUpdate</c> on it whatever its JSON says. Leaving its
    /// board then keeps it alive in <c>GameManager.AllCards</c> as an inactive background card
    /// (<c>InBackground</c> and <c>IndependentFromEnv</c> true) instead of writing it to that environment's saved
    /// board, so neither pass above can see it: the Fallen Pine left unchopped at Pine Trail sat there after the
    /// walk back to the Village (harness, Mothership rig 3, 2026-11-03, T2.2149). (3) removes such a card through
    /// the same <c>RemoveCard</c> call; the same probe removed it cleanly (AllCards 190 to 189, no stale row).</para>
    /// </summary>
    internal static class SeasonalObstacleCleanupPatch
    {
        private const string Tag = "[SeasonalObstacleCleanup]";
        private const BindingFlags InstanceAll = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static bool _initialized;
        private static string _lastEnv;
        private static string _lastSeason;
        private static bool _sawTransition;
        private static int _idleTicks;
        private const int SafetyNetTicks = 15;

        private static bool _removeResolved;
        private static MethodInfo _removeCard;
        private static object[] _removeArgsTemplate;

        private static readonly HashSet<string> _warned = new(StringComparer.Ordinal);

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            TickEvents.Interval(1f, Poll, "SeasonalObstacleCleanup");
            Plugin.Logger.LogDebug($"{Tag} initialized.");
        }

        private static void Poll()
        {
            try
            {
                if (CardUtil.GetGameManagerInstance() == null) { _lastEnv = null; _lastSeason = null; return; }
                if (GameQuery.IsTransitioning) { _sawTransition = true; return; }

                var env = GameQuery.CurrentEnvironmentUniqueId;
                var season = GameQuery.CurrentSeason;
                if (string.IsNullOrEmpty(env) || string.IsNullOrEmpty(season)) return;

                bool changed = !string.Equals(env, _lastEnv, StringComparison.Ordinal)
                               || !string.Equals(season, _lastSeason, StringComparison.OrdinalIgnoreCase);
                if (!changed && !_sawTransition && ++_idleTicks < SafetyNetTicks) return;
                _idleTicks = 0;
                bool savedBoardsMayDiffer = changed || _sawTransition;
                _lastEnv = env;
                _lastSeason = season;
                _sawTransition = false;

                RemoveLive(season);
                if (savedBoardsMayDiffer)
                {
                    RemoveSaved(season);
                    RemoveBackground(season);
                }
            }
            catch (Exception ex)
            {
                WarnOnce("poll", $"poll failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // ------------------------------------------------------------------ live board ---

        private static void RemoveLive(string season)
        {
            List<object> doomed = null;
            foreach (var card in GameQuery.CardsInPlayerEnv())
            {
                var uid = CardUtil.GetCardUniqueId(card);
                if (!SeasonalObstacleRules.IsOutOfSeason(uid, season)) continue;
                (doomed ??= new List<object>()).Add(card);
            }
            if (doomed == null) return;

            foreach (var card in doomed)
            {
                var uid = CardUtil.GetCardUniqueId(card);
                if (!AllowReseed(uid)) continue;
                bool ok = RemoveLiveCard(card);
                Plugin.Logger.LogInfo($"{Tag} {SeasonalObstacleRules.SeasonOf(uid)} is over ({season}): removed the uncleared '{uid}' from the board" +
                                      $"{(ok ? "" : " - REMOVAL FAILED, see the warning above")}.");
            }
        }

        // ------------------------------------------------------------------ saved boards ---

        internal static int RemoveSaved(string season)
        {
            var gm = CardUtil.GetGameManagerInstance();
            if (gm == null) return 0;
            var envDataField = CardUtil.GetCachedField(gm.GetType(), "EnvironmentsData");
            if (envDataField == null) { WarnOnce("envdata-field", "GameManager.EnvironmentsData not found (game update?) - saved boards are not checked."); return 0; }
            if (envDataField.GetValue(gm) is not IDictionary envData) return 0;

            int removed = 0;
            foreach (DictionaryEntry entry in envData)
            {
                var value = entry.Value;
                if (value == null) continue;
                var vt = value.GetType();
                var envKey = CardUtil.GetCachedField(vt, "DictionaryKey")?.GetValue(value) as string ?? "?";
                removed += RemoveFromList(value, vt, "AllRegularCards", envKey, season);
                removed += RemoveFromList(value, vt, "AllInventoryCards", envKey, season);
            }
            return removed;
        }

        private static int RemoveFromList(object envSave, Type vt, string listName, string envKey, string season)
        {
            var listField = CardUtil.GetCachedField(vt, listName);
            if (listField == null) { WarnOnce("list-field:" + listName, $"{vt.Name}.{listName} not found (game update?) - that list is not checked."); return 0; }
            if (listField.GetValue(envSave) is not IList cards || cards.Count == 0) return 0;

            int removed = 0;
            for (int i = cards.Count - 1; i >= 0; i--)
            {
                var card = cards[i];
                if (card == null) continue;
                var idField = CardUtil.GetCachedField(card.GetType(), "CardID");
                if (idField == null) { WarnOnce("cardid-field", $"{card.GetType().Name}.CardID not found (game update?) - saved boards are not checked."); return removed; }
                var cardId = idField.GetValue(card) as string;
                if (!SeasonalObstacleRules.IsOutOfSeason(cardId, season)) continue;
                var bare = SeasonalObstacleRules.BareUid(cardId);
                if (!AllowReseed(bare)) continue;
                cards.RemoveAt(i);
                removed++;
                Plugin.Logger.LogInfo($"{Tag} {SeasonalObstacleRules.SeasonOf(bare)} is over ({season}): removed the uncleared saved '{bare}' from the board of '{envKey}' (list {listName}).");
            }
            return removed;
        }

        // ------------------------------------------------------------------ background cards ---

        /// <summary>Removes an out-of-season obstacle the game keeps in AllCards off-board (see the class remarks).</summary>
        internal static int RemoveBackground(string season)
        {
            var gm = CardUtil.GetGameManagerInstance();
            if (gm == null) return 0;
            var allField = CardUtil.GetCachedField(gm.GetType(), "AllCards");
            if (allField == null) { WarnOnce("allcards-field", "GameManager.AllCards not found (game update?) - background cards are not checked."); return 0; }
            if (allField.GetValue(gm) is not IList all || all.Count == 0) return 0;

            List<object> doomed = null;
            foreach (var card in all)
            {
                if (card == null) continue;
                var bgProp = CardUtil.GetCachedProperty(card.GetType(), "InBackground");
                if (bgProp == null) { WarnOnce("inbackground-prop", $"{card.GetType().Name}.InBackground not found (game update?) - background cards are not checked."); return 0; }
                if (bgProp.GetValue(card, null) is not true) continue;
                if (!SeasonalObstacleRules.IsOutOfSeason(CardUtil.GetCardUniqueId(card), season)) continue;
                (doomed ??= new List<object>()).Add(card);
            }
            if (doomed == null) return 0;

            int removed = 0;
            foreach (var card in doomed)
            {
                var uid = CardUtil.GetCardUniqueId(card);
                if (!AllowReseed(uid)) continue;
                bool ok = RemoveLiveCard(card);
                if (ok) removed++;
                Plugin.Logger.LogInfo($"{Tag} {SeasonalObstacleRules.SeasonOf(uid)} is over ({season}): removed the uncleared background '{uid}' (kept off-board by AlwaysUpdate)" +
                                      $"{(ok ? "" : " - REMOVAL FAILED, see the warning above")}.");
            }
            return removed;
        }

        // ------------------------------------------------------------------ reseed guard ---

        private static bool _gatesResolved;
        private static IList _gates;
        private static FieldInfo _defField, _seedSetField, _challengeField;

        /// <summary>
        /// Clears the framework's per-session "already seeded" set for the gate whose challenge card is
        /// <paramref name="challengeUid"/>, so the gate can seed a fresh card next season. False (and the
        /// caller removes nothing) when that set cannot be reached.
        /// </summary>
        private static bool AllowReseed(string challengeUid)
        {
            try
            {
                if (!_gatesResolved)
                {
                    _gatesResolved = true;
                    var svc = typeof(GameQuery).Assembly.GetType("CSFFModFramework.Injection.SealableGateService");
                    _gates = svc?.GetField("_gates", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as IList;
                }
                if (_gates == null) { WarnOnce("gates", "the framework's SealableGateService gate list was not found - seasonal obstacles are left in place (old behaviour)."); return false; }

                bool found = false;
                foreach (var state in _gates)
                {
                    if (state == null) continue;
                    var st = state.GetType();
                    _defField ??= st.GetField("Def", InstanceAll);
                    _seedSetField ??= st.GetField("SeedRequestedEnvUIDs", InstanceAll);
                    var def = _defField?.GetValue(state);
                    if (def == null) continue;
                    _challengeField ??= def.GetType().GetField("ChallengeCardUID", InstanceAll);
                    var uid = _challengeField?.GetValue(def) as string
                              ?? def.GetType().GetProperty("ChallengeCardUID", InstanceAll)?.GetValue(def, null) as string;
                    if (!string.Equals(uid, challengeUid, StringComparison.Ordinal)) continue;
                    if (_seedSetField?.GetValue(state) is not HashSet<string> seeded) break;
                    seeded.Clear();
                    found = true;
                }
                if (!found) WarnOnce("gate:" + challengeUid, $"no reachable framework gate for '{challengeUid}' - it is left in place (old behaviour).");
                return found;
            }
            catch (Exception ex)
            {
                WarnOnce("reseed", $"reseed guard failed - seasonal obstacles are left in place: {ex.InnerException?.ToString() ?? ex.ToString()}");
                return false;
            }
        }

        // ------------------------------------------------------------------ removal ---

        private static bool RemoveLiveCard(object card)
        {
            var gm = CardUtil.GetGameManagerInstance() as MonoBehaviour;
            if (gm == null) return false;
            ResolveRemoveCard(gm.GetType());
            if (_removeCard != null && _removeCard.GetParameters()[0].ParameterType.IsInstanceOfType(card))
            {
                try
                {
                    var args = (object[])_removeArgsTemplate.Clone();
                    args[0] = card;
                    if (_removeCard.Invoke(gm, args) is IEnumerator routine)
                    {
                        gm.StartCoroutine(routine);
                        return true;
                    }
                    WarnOnce("remove-noroutine", "GameManager.RemoveCard returned no coroutine; falling back to CardUtil.TryRemoveCard.");
                }
                catch (Exception ex)
                {
                    WarnOnce("remove-threw", $"GameManager.RemoveCard threw; falling back to CardUtil.TryRemoveCard: {ex.InnerException?.ToString() ?? ex.ToString()}");
                }
            }
            return CardUtil.TryRemoveCard(card);
        }

        private static void ResolveRemoveCard(Type gmType)
        {
            if (_removeResolved) return;
            _removeResolved = true;
            foreach (var m in gmType.GetMethods(InstanceAll))
            {
                if (m.Name != "RemoveCard" || !typeof(IEnumerator).IsAssignableFrom(m.ReturnType)) continue;
                var ps = m.GetParameters();
                if (ps.Length != 6 || !ps[4].ParameterType.IsEnum) continue;

                // InGameNPCOrPlayer.Null is a static property (.decomp/InGameNPCOrPlayer.cs:39).
                var nullUser = ps[3].ParameterType.GetProperty("Null", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null, null);
                if (nullUser == null) { WarnOnce("remove-null-user", "InGameNPCOrPlayer.Null not found; using CardUtil.TryRemoveCard."); return; }

                object removeAll;
                try { removeAll = Enum.Parse(ps[4].ParameterType, "RemoveAll"); }
                catch (Exception ex) { WarnOnce("remove-option", $"RemoveOption.RemoveAll not found ({ex.Message}); using CardUtil.TryRemoveCard."); return; }

                _removeCard = m;
                _removeArgsTemplate = new object[] { null, true, false, nullUser, removeAll, false };
                return;
            }
            WarnOnce("remove-missing", "GameManager.RemoveCard(card, bool, bool, user, RemoveOption, bool) not found; using CardUtil.TryRemoveCard.");
        }

        private static void WarnOnce(string cause, string message)
        {
            if (_warned.Add(cause)) Plugin.Logger.LogWarning($"{Tag} {message}");
        }
    }
}
