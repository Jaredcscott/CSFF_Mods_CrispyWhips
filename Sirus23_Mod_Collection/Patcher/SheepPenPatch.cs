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
    /// SCOPE LIMIT (matches WildlifeRaidService precedent, CSFFModFramework/Wildlife/
    /// WildlifeRaidService.cs IsInPlayerEnv): only cards in the environment the player is
    /// CURRENTLY standing in are evaluated. Tame sheep carry AlwaysUpdate: true (so their
    /// Wool/pregnancy stats keep progressing while the player is elsewhere - confirmed via
    /// the Wave-0 persistence spike), but "danger" events in this engine - bear raids,
    /// wildlife container raids, encounter guards - are consistently scoped to the player's
    /// current board, not simulated for every environment with AlwaysUpdate cards in it. A
    /// flock left at a distant, unvisited environment is not rolled against until the player
    /// is standing there again on a night the roll fires.
    ///
    /// SAFETY: a penned sheep (CurrentContainer == the Sheep Pen) is EXEMPT - checked before
    /// any roll. A Wolf companion present in the player's current environment suppresses the
    /// roll entirely for every unpenned sheep that night (mirrors the wolf's existing
    /// EncounterGuards/WolfGuard.json wildlife-suppression scoping: presence-in-current-env,
    /// not a "Guard Camp" action toggle - the wolf has no persistent "guarding" state to gate
    /// on, see CardInteractions on WolfCompanion.json).
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

        private static readonly HashSet<string> PennableUids = new(StringComparer.Ordinal)
        {
            TameSheepUid, MaleSheepUid, LactatingSheepUid,
        };

        // BALANCE PLACEHOLDER (see class doc comment) - not final-tuned. Combined per-sheep
        // nightly loss risk when unpenned and no wolf is present: 4% escape + 6% predation = 10%.
        private const float EscapeChance = 0.04f;
        private const float PredationChance = 0.06f;

        private static ManualLogSource Logger => Plugin.Logger;

        public static void Register()
        {
            TickEvents.DayRollover += OnDayRollover;
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
            if (CardUtil.GetMemberValue(gm, "AllCards") is not IEnumerable allCards) return;

            var atRisk = new List<object>();
            bool wolfPresent = false;

            foreach (var card in allCards)
            {
                if (card == null) continue;
                if (!IsInPlayerEnv(card)) continue;

                string uid = CardUtil.GetCardUniqueId(card);
                if (uid == null) continue;

                if (uid == WolfTickPatch.WolfId)
                {
                    wolfPresent = true;
                    continue;
                }
                if (!PennableUids.Contains(uid)) continue;
                if (IsPenned(card))
                {
                    Logger?.LogDebug($"[SheepPen] {uid} is penned - exempt from tonight's roll.");
                    continue;
                }
                atRisk.Add(card);
            }

            if (atRisk.Count == 0) return;

            if (wolfPresent)
            {
                Logger?.LogInfo($"[SheepPen] {atRisk.Count} unpenned sheep/ram(s) survived the night - the wolf companion stood guard.");
                return;
            }

            foreach (var sheep in atRisk)
                RollForSheep(sheep);
        }

        private static void RollForSheep(object sheep)
        {
            string uid = CardUtil.GetCardUniqueId(sheep) ?? "sheep";
            float roll = UnityEngine.Random.value;

            if (roll < PredationChance)
            {
                Logger?.LogInfo($"[SheepPen] {uid} was taken by a predator overnight (left unpenned).");
                SpawnService.Spawn(RemainsUid);
                CardUtil.TryRemoveCard(sheep);
            }
            else if (roll < PredationChance + EscapeChance)
            {
                Logger?.LogInfo($"[SheepPen] {uid} wandered off overnight (left unpenned).");
                CardUtil.TryRemoveCard(sheep);
            }
        }

        private static bool IsPenned(object card)
        {
            var container = CardUtil.GetMemberValue(card, "CurrentContainer");
            if (container == null) return false;
            return CardUtil.GetCardUniqueId(container) == PenUid;
        }

        private static bool IsInPlayerEnv(object card)
        {
            var env = CardUtil.GetMemberValue(card, "CardEnvironment");
            if (env == null) return false;
            return CardUtil.GetMemberValue(env, "MatchesPlayerEnv") is bool b && b;
        }
    }
}
