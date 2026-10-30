using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using HarmonyLib;
using UnityEngine;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Removes a CMC clone location card (CT8, e.g. <c>cmcLocVillage</c>) that sits on ANOTHER
    /// environment's board, from the saved boards at run start and after every environment change, and
    /// from the live board on arrival (CMC 1.69.26).
    ///
    /// <para><b>The reports.</b> Chiwei (Nexus 2026-08-19/20): "I couldn't leave the village map to the
    /// south; it would just take me back to the village", and "when I reach the map south of the
    /// village, I directly enter the village", after first describing the top-left location area
    /// showing "two different locations". TheFifthLorax (2026-09-16): "sent to the village area without
    /// completing the bridge ... On the village path it just keeps repeating the area when I try to go
    /// west."</para>
    ///
    /// <para><b>The one board shape that produces every one of those sentences.</b> A location card's
    /// travel buttons are plain data on that card: South on <c>cmcLocVillage</c> produces
    /// <c>cmcEnvPineTrail</c>, West on <c>cmcLocVillagePath</c> produces River Clearing
    /// (<c>WorldMap/MapNodes.json</c>; <c>WorldMapInjector.InjectTravelDA</c>). The clone CT8s are
    /// <c>AlwaysUpdate=false</c> (<c>CardCloneService.CloneCard</c>), so <c>GameManager.ChangeEnvironment</c>
    /// saves one into whichever environment's board it is on when the player leaves (.decomp
    /// GameManager.cs:11420-11427, inventory branch: the CT8 holds the improvement slots) and
    /// <c>LoadCardSet</c> puts it back on that board on every visit (:11489). So if the Village's CT8
    /// is ever saved onto Pine Trail's board:
    /// <list type="bullet">
    /// <item>arriving at Pine Trail (North from Highland Pines, East from Hunter's Crossing) shows the
    /// Village's location card: "I directly enter the village";</item>
    /// <item>the framework's env watch still spawns Pine Trail's own CT8 beside it
    /// (<c>WorldMapInjector.OnEnvWatchTick</c> looks for that UID only): "two different
    /// locations";</item>
    /// <item>South on the Village card produces Pine Trail, the environment the player is already in,
    /// which reloads the same board with the same Village card: "it would just take me back to the
    /// village", while North and East (other environments) work, exactly as reported.</item>
    /// </list>
    /// The same shape with <c>cmcLocVillagePath</c> saved onto River Clearing's board is TheFifthLorax's
    /// report: at River Clearing the Village Path card offers its North, East and South buttons into the
    /// village cluster with no bridge built (vanilla only gates River Clearing's own East button), and
    /// its West button produces River Clearing again, "it just keeps repeating the area". Vanilla never
    /// cleans this up: <c>CheckForMissingDefaultCardsInEnv</c> skips an environment that already has an
    /// explorable card (:11599), and the stray is one.</para>
    ///
    /// <para><b>How a card got there (inferred, not observed).</b> Chiwei's own words tie the loop to
    /// the same "two versions in the top left" overlap the Portal Hub produced (retro
    /// <c>portal-hub-env-overlap-2026-08-24</c>: stale <c>NextEnvironment</c> and duplicate per-run travel
    /// handlers, so one click ran several overlapping environment changes and saved boards under the
    /// wrong key; fixed framework 2.25.6/2.25.14/2.25.23), and to the stray vanilla road improvements
    /// (<see cref="StrayImprovementsPatch"/>), both of which move the player while an environment change
    /// is in flight. Those fixes stop NEW strays from those routes; none of them removes a stray a save
    /// already holds, which is why the loop survived every one of them ("the entire map structure in
    /// the save file has a permanent problem", Chiwei 2026-08-24). That retro rejected a save repair of
    /// the mis-keyed ENVIRONMENT entries as infeasible (a laundered key serializes like a real one); a
    /// clone CT8 on a foreign board is a different artifact and is exactly detectable, because each
    /// one's home is fixed by <c>MapNodes.json</c>.</para>
    ///
    /// <para><b>What it does.</b> (1) Saved boards: every <c>EnvironmentsData</c> entry's regular and
    /// inventory card lists lose any clone CT8 whose home is another environment, at
    /// <c>OnGMInitialized</c> (after <c>LoadCards</c> has filled <c>EnvironmentsData</c>) and after each
    /// environment change. (2) Live board: a clone CT8 the player is standing beside whose own
    /// <c>CardEnvironment</c> is not its home is removed with the game's own
    /// <c>RemoveCard(card, _NoDelay: true, _DoDrops: false, Null, RemoveOption.RemoveAll)</c>, the exact
    /// call <c>ChangeEnvironment</c> uses to clear a board (:11448). Judging by the card's own
    /// <c>CardEnvironment</c>, not <c>CurrentEnvironment</c>, keeps it safe during the first frames of a
    /// load, when <c>CurrentEnvironment</c> still reads the default start area. Nothing at home is ever
    /// touched, and the home board keeps (or gets, through the framework's env watch) its own card.
    /// Each removal logs one Info line naming the card, the board and the card's creation tick.</para>
    ///
    /// <para><b>Diagnostic for anything this does not explain.</b> A prefix on
    /// <c>GameManager.ChangeEnvironment</c> logs one Info line whenever the destination is the
    /// environment the player is already in (the loop's own signature) and lists every location card on
    /// the board at that moment with the environment it belongs to, plus the travel action's name. If a
    /// player still loops on this version and that line names no stray card, the cause is elsewhere and
    /// the line says what was clicked.</para>
    /// </summary>
    internal static class StrayLocationCardRepairPatch
    {
        private const string Tag = "[StrayLocationCardRepair]";
        private const BindingFlags InstanceAll = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static bool _initialized;
        private static Action _gmInitializedHandler;
        private static Type _gmType;

        // Live-board poll state.
        private static string _lastEnv;
        private static bool _sawTransition;
        private static int _idleTicks;
        private const int SafetyNetTicks = 15;

        // GameManager.RemoveCard(InGameCardBase, bool, bool, InGameNPCOrPlayer, RemoveOption, bool).
        private static bool _removeResolved;
        private static MethodInfo _removeCard;
        private static object[] _removeArgsTemplate;

        private static int _sameEnvLogged;
        private const int SameEnvLogCap = 20;

        private static readonly HashSet<string> _warned = new(StringComparer.Ordinal);

        public static void Initialize(Harmony harmony)
        {
            if (_initialized) return;
            _initialized = true;

            _gmType = CardUtil.FindGameType("GameManager");
            if (_gmType == null)
            {
                Plugin.Logger.LogWarning($"{Tag} GameManager type not found - stray village location cards will not be repaired.");
                return;
            }

            // Once per process: the delegate is combined here only (the _initialized guard), and
            // OnGMInitialized re-fires on every run start, which is exactly when a fresh save's boards
            // need the pass (root CLAUDE.md, Per-Run Event Registration).
            var onGmInitField = _gmType.GetField("OnGMInitialized", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (onGmInitField != null && onGmInitField.FieldType == typeof(Action))
            {
                _gmInitializedHandler = OnRunStart;
                var current = (Action)onGmInitField.GetValue(null);
                onGmInitField.SetValue(null, (Action)Delegate.Combine(current, _gmInitializedHandler));
            }
            else
            {
                Plugin.Logger.LogWarning($"{Tag} GameManager.OnGMInitialized not found - saved boards will only be repaired after the first environment change.");
            }

            var changeEnv = AccessTools.Method(_gmType, "ChangeEnvironment", Type.EmptyTypes);
            if (changeEnv != null)
                harmony.Patch(changeEnv, prefix: new HarmonyMethod(typeof(StrayLocationCardRepairPatch), nameof(ChangeEnvironment_Prefix)));
            else
                Plugin.Logger.LogWarning($"{Tag} GameManager.ChangeEnvironment() not found - the same-environment travel diagnostic is off.");

            TickEvents.Interval(1f, Poll, "StrayLocationCardRepair");
            Plugin.Logger.LogDebug($"{Tag} initialized.");
        }

        // ------------------------------------------------------------------ run start ---

        private static void OnRunStart()
        {
            _lastEnv = null;
            _sawTransition = false;
            _idleTicks = 0;
            try { RepairSavedBoards("run start"); }
            catch (Exception ex) { WarnOnce("runstart", $"run-start saved-board pass failed: {ex.InnerException?.ToString() ?? ex.ToString()}"); }
        }

        // ------------------------------------------------------------------ poll ---

        private static void Poll()
        {
            try
            {
                if (CardUtil.GetGameManagerInstance() == null) { _lastEnv = null; return; }
                if (GameQuery.IsTransitioning) { _sawTransition = true; return; }

                var env = GameQuery.CurrentEnvironmentUniqueId;
                if (string.IsNullOrEmpty(env)) return;

                bool changed = !string.Equals(env, _lastEnv, StringComparison.Ordinal);
                if (!changed && !_sawTransition && ++_idleTicks < SafetyNetTicks) return;
                _idleTicks = 0;
                bool afterTravel = changed || _sawTransition;
                _lastEnv = env;
                _sawTransition = false;

                RemoveLiveStrays(env);
                if (afterTravel) RepairSavedBoards("after travel");
            }
            catch (Exception ex)
            {
                WarnOnce("poll", $"poll failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // ------------------------------------------------------------------ saved boards ---

        /// <summary>Drops every clone CT8 saved on a board that is not its home. Returns the number removed.</summary>
        internal static int RepairSavedBoards(string phase)
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
                var keyField = CardUtil.GetCachedField(vt, "DictionaryKey");
                if (keyField == null) { WarnOnce("dictkey-field", $"{vt.Name}.DictionaryKey not found (game update?) - saved boards are not checked."); return removed; }
                var envKey = keyField.GetValue(value) as string;
                if (string.IsNullOrEmpty(envKey)) continue;

                removed += RemoveStraysFromList(value, vt, "AllRegularCards", envKey, phase);
                removed += RemoveStraysFromList(value, vt, "AllInventoryCards", envKey, phase);
            }

            if (removed > 0)
                Plugin.Logger.LogInfo($"{Tag} {phase}: removed {removed} village location card(s) saved on the wrong board.");
            return removed;
        }

        private static int RemoveStraysFromList(object envSave, Type vt, string listName, string envKey, string phase)
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
                if (!StrayLocationCardRules.IsStray(cardId, envKey)) continue;

                var tick = CardUtil.GetCachedField(card.GetType(), "CreatedOnTick")?.GetValue(card);
                cards.RemoveAt(i);
                removed++;
                Plugin.Logger.LogInfo($"{Tag} {phase}: removed saved '{StrayLocationCardRules.BareUid(cardId)}' from the board of '{envKey}' " +
                                      $"(its home is '{StrayLocationCardRules.HomeEnvOf(cardId)}'; list {listName}; CreatedOnTick={tick ?? "?"}).");
            }
            return removed;
        }

        // ------------------------------------------------------------------ live board ---

        private static void RemoveLiveStrays(string currentEnv)
        {
            foreach (var card in GameQuery.CardsInPlayerEnv())
            {
                var uid = CardUtil.GetCardUniqueId(card);
                if (StrayLocationCardRules.HomeEnvOf(uid) == null) continue;

                var cardEnv = CardEnvUid(card);
                if (cardEnv == null) { WarnOnce("cardenv", $"could not read the environment of live card '{uid}' - live stray check skipped for it."); continue; }
                if (!StrayLocationCardRules.IsStray(uid, cardEnv)) continue;

                bool ok = RemoveLiveCard(card);
                Plugin.Logger.LogInfo($"{Tag} removed live '{uid}' from the board of '{cardEnv}' (player in '{currentEnv}'; its home is " +
                                      $"'{StrayLocationCardRules.HomeEnvOf(uid)}'){(ok ? "" : " - REMOVAL FAILED, see the warning above")}.");
            }
        }

        /// <summary>UniqueID of the environment card in <c>card.CardEnvironment</c>; null when unreadable.</summary>
        private static string CardEnvUid(object card)
        {
            var envId = CardUtil.GetMemberValue(card, "CardEnvironment");
            if (envId == null) return null;
            var envCard = CardUtil.GetMemberValue(envId, "EnvCard");
            return envCard == null ? null : CardUtil.GetMemberValue(envCard, "UniqueID") as string;
        }

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

                object nullUser = null;
                var userType = ps[3].ParameterType;
                var nullProp = userType.GetProperty("Null", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                // InGameNPCOrPlayer.Null is a static property (.decomp/InGameNPCOrPlayer.cs:39); no field lookup.
                if (nullProp != null) nullUser = nullProp.GetValue(null, null);
                if (nullUser == null) { WarnOnce("remove-null-user", $"{userType.Name}.Null not found; live strays use CardUtil.TryRemoveCard."); return; }

                object removeAll;
                try { removeAll = Enum.Parse(ps[4].ParameterType, "RemoveAll"); }
                catch (Exception ex) { WarnOnce("remove-option", $"RemoveOption.RemoveAll not found ({ex.Message}); live strays use CardUtil.TryRemoveCard."); return; }

                _removeCard = m;
                // card, _NoDelay, _DoDrops, _User, _RemoveOption, _DontSpillLiquid - ChangeEnvironment's own board-clear call.
                _removeArgsTemplate = new object[] { null, true, false, nullUser, removeAll, false };
                return;
            }
            WarnOnce("remove-missing", "GameManager.RemoveCard(card, bool, bool, user, RemoveOption, bool) not found; live strays use CardUtil.TryRemoveCard.");
        }

        // ------------------------------------------------------------------ diagnostic ---

        /// <summary>
        /// Logs a travel whose destination is the environment the player is already in, with every
        /// location card on the board and the travel action's name. Runs when ChangeEnvironment is
        /// called, before the leaving board is saved, so the board listed is the one the click came from.
        /// </summary>
        private static void ChangeEnvironment_Prefix(object __instance)
        {
            try
            {
                if (_sameEnvLogged >= SameEnvLogCap) return;
                var cur = EnvUidOf(CardUtil.GetMemberValue(__instance, "CurrentEnvironment"));
                var next = EnvUidOf(CardUtil.GetMemberValue(__instance, "NextEnvironment"));
                if (cur == null || next == null || !string.Equals(cur, next, StringComparison.Ordinal)) return;

                _sameEnvLogged++;
                var sb = new StringBuilder();
                foreach (var card in GameQuery.CardsInPlayerEnvOfType(8))
                {
                    if (sb.Length > 0) sb.Append(", ");
                    var uid = CardUtil.GetCardUniqueId(card) ?? "?";
                    var env = CardEnvUid(card) ?? "?";
                    sb.Append(uid).Append('@').Append(env);
                    if (StrayLocationCardRules.IsStray(uid, env)) sb.Append(" (STRAY)");
                }
                var travel = CardUtil.GetMemberValue(__instance, "TravelAction");
                var name = travel == null ? null : CardUtil.GetMemberValue(CardUtil.GetMemberValue(travel, "ActionName"), "DefaultText") as string;
                Plugin.Logger.LogInfo($"{Tag} DIAG same-environment travel: '{cur}' -> '{next}' via '{name ?? "?"}'; location cards on the board: [{sb}].");
            }
            catch (Exception ex)
            {
                WarnOnce("diag", $"same-environment travel diagnostic failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static string EnvUidOf(object envId)
        {
            if (envId == null) return null;
            var envCard = CardUtil.GetMemberValue(envId, "EnvCard");
            return envCard == null ? null : CardUtil.GetMemberValue(envCard, "UniqueID") as string;
        }

        private static void WarnOnce(string cause, string message)
        {
            if (_warned.Add(cause)) Plugin.Logger.LogWarning($"{Tag} {message}");
        }
    }
}
