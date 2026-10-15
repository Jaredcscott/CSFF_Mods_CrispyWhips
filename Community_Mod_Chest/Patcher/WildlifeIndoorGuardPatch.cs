using System;
using System.Collections.Generic;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using HarmonyLib;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Refuses a hostile wildlife agent's MOVE onto a protected CMC board. It refuses a move and
    /// suppresses nothing: no encounter is ever cancelled here (root CLAUDE.md "Encounter System
    /// Patching": never suppress an NPC encounter).
    ///
    /// First built 1.68.14 for the seven village interiors (reported 2026-08-28: a Sirus23 screenshot
    /// of a Bear standing at the Inn Counter and in the Academy). Every one of these agents' "approach
    /// the player" AgentActions sets <c>MoveToPlayerEnvironment: true</c>, which ends in
    /// <c>GameManager.MoveNPC(this, GM.CurrentEnvironment)</c> with no map or indoor check.
    ///
    /// Extended in round 2 (Documentation/Design/CMC_Round2_As_Built.md A1) to the seven
    /// outdoor village tiles and the Hunting Stand, because on EA 0.68b the JSON guards cannot stop an
    /// agent at all: every fight action of the eight agents below drops its encounter with
    /// EncounterNPC set to the agent itself, and CSFFModFramework's EncounterGuardPatch returns before
    /// any guard runs whenever that NPC is non-null. So EncounterGuards/CMC_VillageNoWildlife.json,
    /// CMC_InteriorsNoWildlife.json and HuntingStandGuard.json only ever see null-NPC starts (the
    /// player clicking an agent's card, or an encounter no agent started). They stay in place for
    /// exactly that path. What they cannot see, this sees: all 18 of those fight actions carry
    /// <c>Conditions.NotInBackground</c>, so they fire only once the agent stands on the player's
    /// board, and every route onto a board ends in MoveNPC (duty hops through
    /// <c>MoveDutyAction.PickDestinationAndMoveThere</c>, MoveToPlayerEnvironment agent actions,
    /// CardAction MoveNPC effects). Research: Documentation/Research/Community_Mod_Chest/
    /// engine_reads.md section W0.1.
    ///
    /// Two branches, checked in order:
    ///   1. <see cref="ProtectedSets"/>: each mirrors one EncounterGuards JSON's GuardEnvironmentUids
    ///      and carries its own refusal chance (1 = always). Development_Tools/Tests/
    ///      CMC-Round2.Tests.ps1 asserts each set equals its JSON's list, so a new protected set is a
    ///      new entry here plus its JSON pair, and the same check covers it.
    ///   2. The Hunting Stand: when the destination is the player's own board and a
    ///      <c>hunterscachehuntingstand</c> stands on it (an env-scoped read through
    ///      <c>GameQuery.CardsInPlayerEnv</c>, never a bare AllCards scan), refuse with
    ///      <see cref="HuntingStandRefuseChance"/>.
    ///
    /// A chance below 1 is rolled ONCE per (cause, agent, destination, in-game day) and the verdict
    /// holds for that day. A refused hop leaves the move duty incomplete
    /// (<c>MoveDutyAction.ActionComplete</c> is false until the agent stands at its target), so
    /// <c>InGameNPC.UpdateTick</c> runs <c>UpdateDutyAction</c>, and with it the same MoveNPC, again
    /// on every tick: a fresh roll per call would let a turned-back predator in a tick later and the
    /// chance would mean nothing.
    ///
    /// What this cannot stop (W0.1; the CHANGELOG says the same): a fight started with a non-null NPC
    /// that is elsewhere (<c>EncounterPopup.StartEncounter</c> initialises the fight before its own
    /// MoveNPC), the legacy Combat_Event* explore-event cards, an agent already standing inside a
    /// protected tile (saved there, or placed by Init or SpawnNPCS: a refusal does not evict), the
    /// player walking onto a tile an agent already occupies, and null-NPC starts of the encounters
    /// the village JSON guard does not list.
    ///
    /// Reflection-only, same convention as <c>PartnerIndoorFollowPatch</c>: CMC's Assembly-CSharp
    /// reference is the nstrip build (via HerbsAndFungi), which renames fields, so all game-side
    /// member access goes through <see cref="Reflect"/>/<see cref="CardUtil"/>. Every miss on that
    /// path warns once per cause (root CLAUDE.md "Silent Catch Blocks"): a guard that silently lets
    /// everything through looks exactly like a quiet night.
    /// </summary>
    internal static class WildlifeIndoorGuardPatch
    {
        private static readonly HashSet<string> HostileWildlifeUids = new(StringComparer.Ordinal)
        {
            "61dd2882b59388346b42628f5e76432b", // Bear
            "80cf123c1b0d049459ab4e3fb1b316ff", // Boar1
            "5957ab4b2d66bca44b6502b40eb30a9c", // Boar2
            "eb65ebb44e0cf304fa58b9af3ca9f311", // Forest Beast
            "dc995406694a50b4e8e7bfabd196b63e", // Moontouched
            "0cb137c5a8daaed47ba291779d8146f8", // Wolf
            "d1602c74669f2e642983a004355a3f81", // Primeval Wolf
            "c3ac5867e8021ce43a5094a2bb469935", // Wolf Pack
        };

        /// <summary>
        /// One set of destination env UIDs a hostile agent may not move into, with the chance a move
        /// there is refused (1 = always) and the EncounterGuards JSON whose GuardEnvironmentUids it
        /// mirrors. <c>Cause</c> names the set in log lines, warn-once keys and verdict keys.
        /// </summary>
        private sealed class ProtectedSet
        {
            public readonly string Cause;
            public readonly string MirrorsGuardFile;
            public readonly float RefuseChance;
            public readonly HashSet<string> EnvUids;

            public ProtectedSet(string cause, string mirrorsGuardFile, float refuseChance, params string[] envUids)
            {
                Cause = cause;
                MirrorsGuardFile = mirrorsGuardFile;
                RefuseChance = refuseChance;
                EnvUids = new HashSet<string>(envUids, StringComparer.OrdinalIgnoreCase);
            }
        }

        // Checked in order. Each entry's env list must equal its JSON's GuardEnvironmentUids; that is
        // asserted by Development_Tools/Tests/CMC-Round2.Tests.ps1, not by this comment.
        private static readonly ProtectedSet[] ProtectedSets =
        {
            new ProtectedSet("interior", "EncounterGuards/CMC_InteriorsNoWildlife.json", 1f,
                "cmcInnInterior",
                "cmcAcademyInterior",
                "cmcApothecaryCabinInterior",
                "cmcMillerCottageInterior",
                "cmcWeaverCottageInterior",
                "cmcVillageHallInterior",
                "cmcEnvJailCell"),
            new ProtectedSet("village", "EncounterGuards/CMC_VillageNoWildlife.json", 1f,
                "cmcEnvVillage",
                "cmcEnvVillagePath",
                "cmcEnvVillageFarm"),
            // Vanilla balance pass (1.69.17, plan Part II CMC-V-B5, owner tile 10): the village core stays
            // safe, the four hunting-ground tiles around it are only half guarded, like the Foraging Forest.
            // Keep 0.5 equal to CMC_VillageOutskirtsWildlife.json's SuppressChance / 100.
            new ProtectedSet("outskirts", "EncounterGuards/CMC_VillageOutskirtsWildlife.json", 0.5f,
                "cmcEnvHuntersCrossing",
                "cmcEnvPineTrail",
                "cmcEnvHighGrove",
                "cmcEnvDeerMeadow"),
            // Plan J6: the Foraging Forest is only half guarded (safer than the open woods, never as
            // safe as the village). 0.5 is tunable; keep it equal to the JSON's SuppressChance / 100.
            new ProtectedSet("forest", "EncounterGuards/CMC_ForagingForestWildlife.json", 0.5f,
                "cmcEnvForagingForest"),
        };

        private const string HuntingStandUid = "hunterscachehuntingstand";

        // The share of a hostile agent's approaches onto the player's board that a Hunting Stand there
        // turns back, decided once per agent per day. 0.25 (vanilla balance pass 1.69.17, plan Part II
        // CMC-V-B6; 0.45 before) matches the SuppressChance 25 that EncounterGuards/HuntingStandGuard.json
        // applies to the null-NPC path and the "~25%" in the stand's help text; change all three together.
        private const float HuntingStandRefuseChance = 0.25f;

        private static readonly HashSet<string> _warned = new(StringComparer.Ordinal);

        // Verdicts for the current in-game day, keyed "cause|agentUid|destinationKey". Cleared when
        // the day changes. A save reloaded on the same day keeps the day's verdicts, so reloading
        // does not re-roll the stand.
        private static readonly Dictionary<string, bool> _verdicts = new(StringComparer.Ordinal);
        private static int _verdictDay = int.MinValue;

        private static bool _initialized;

        public static void Initialize(Harmony harmony)
        {
            if (_initialized) return;
            _initialized = true;

            var gmType = CardUtil.FindGameType("GameManager");
            var inGameNpcType = CardUtil.FindGameType("InGameNPC");
            var envIdType = CardUtil.FindGameType("EnvID");
            if (gmType == null || inGameNpcType == null || envIdType == null)
            {
                Plugin.Logger.LogWarning("[WildlifeIndoorGuardPatch] Required game types not found, so the guard is inactive and predators can walk into the village and its buildings.");
                return;
            }

            var moveNpcMethod = AccessTools.Method(gmType, "MoveNPC", new[] { inGameNpcType, envIdType });
            if (moveNpcMethod == null)
            {
                Plugin.Logger.LogWarning("[WildlifeIndoorGuardPatch] GameManager.MoveNPC(InGameNPC, EnvID) not found, so the guard is inactive and predators can walk into the village and its buildings.");
                return;
            }

            harmony.Patch(moveNpcMethod, prefix: new HarmonyMethod(typeof(WildlifeIndoorGuardPatch), nameof(Prefix)));
            var sets = new List<string>();
            foreach (var set in ProtectedSets)
                sets.Add($"{set.Cause} ({set.EnvUids.Count} envs, chance {set.RefuseChance:0.##}, mirrors {set.MirrorsGuardFile})");
            Plugin.Logger.LogDebug($"[WildlifeIndoorGuardPatch] initialized: {string.Join("; ", sets)}; Hunting Stand chance {HuntingStandRefuseChance:0.##}.");
        }

        private static bool Prefix(object __0, object __1)
        {
            try
            {
                if (__0 == null)
                {
                    WarnOnce("npc-null", "[WildlifeIndoorGuardPatch] MoveNPC was called with a null NPC; that move is not guarded.");
                    return true;
                }

                var npcModel = Reflect.GetMember(__0, "NPCModel");
                if (npcModel == null)
                {
                    WarnOnce("npc-model", $"[WildlifeIndoorGuardPatch] {__0.GetType().Name}.NPCModel could not be read, so no wildlife move is guarded.");
                    return true;
                }

                string npcUid = CardUtil.GetCardUniqueId(npcModel);
                if (npcUid == null)
                {
                    WarnOnce("npc-uid", $"[WildlifeIndoorGuardPatch] The UniqueID of an NPC model ({npcModel.GetType().Name}) could not be read, so that NPC's moves are not guarded.");
                    return true;
                }
                if (!HostileWildlifeUids.Contains(npcUid)) return true;

                if (!Reflect.TryGetMember(__1, "EnvCard", out var envCard))
                {
                    WarnOnce("dest-envcard", $"[WildlifeIndoorGuardPatch] EnvID.EnvCard could not be read on the destination ({__1?.GetType().Name ?? "null"}), so no wildlife move is guarded.");
                    return true;
                }
                // An empty EnvID: vanilla MoveNPC does nothing with it either.
                if (envCard == null) return true;

                string destUid = CardUtil.GetCardUniqueId(envCard);
                if (destUid == null)
                {
                    WarnOnce("dest-uid", $"[WildlifeIndoorGuardPatch] The destination env card's UniqueID could not be read ({envCard.GetType().Name}), so no wildlife move is guarded.");
                    return true;
                }

                string destKey = null;
                if (Reflect.TryGetMember(__1, "StringDictionnaryKey", out var destKeyValue))
                    destKey = destKeyValue as string;
                if (string.IsNullOrEmpty(destKey))
                {
                    WarnOnce("dest-key", $"[WildlifeIndoorGuardPatch] EnvID.StringDictionnaryKey could not be read for '{destUid}'; verdicts are keyed by the env UID instead, so instances of one env share a verdict.");
                    destKey = destUid;
                }

                // Already standing there: vanilla MoveNPC is a no-op, so there is nothing to refuse and
                // no day's roll to spend on it.
                if (!Reflect.TryGetMember(__0, "CurrentEnvironment", out var npcEnv) || npcEnv == null
                    || !Reflect.TryGetMember(npcEnv, "StringDictionnaryKey", out var npcKeyValue))
                {
                    WarnOnce("npc-env", "[WildlifeIndoorGuardPatch] InGameNPC.CurrentEnvironment's key could not be read, so a move onto the board an agent already stands on is judged like an arrival.");
                }
                else if (npcKeyValue is string npcKey && string.Equals(npcKey, destKey, StringComparison.Ordinal))
                {
                    return true;
                }

                foreach (var set in ProtectedSets)
                {
                    if (!set.EnvUids.Contains(destUid)) continue;
                    if (Refuse(set.Cause, set.RefuseChance, npcUid, destUid, destKey)) return false;
                }

                if (!(Reflect.GetMember(__1, "MatchesPlayerEnv") is bool toPlayerBoard))
                {
                    WarnOnce("dest-matchesplayerenv", "[WildlifeIndoorGuardPatch] EnvID.MatchesPlayerEnv could not be read, so a Hunting Stand turns no predator back.");
                    return true;
                }
                if (!toPlayerBoard) return true;
                if (!HuntingStandOnPlayerBoard()) return true;

                return !Refuse("stand", HuntingStandRefuseChance, npcUid, destUid, destKey);
            }
            catch (Exception ex)
            {
                WarnOnce("exception-" + ex.GetType().Name, $"[WildlifeIndoorGuardPatch] The guard threw, so this move was allowed: {ex.InnerException?.ToString() ?? ex.ToString()}");
                return true;
            }
        }

        /// <summary>
        /// True when a Hunting Stand is on the player's board. Env-scoped by
        /// <c>GameQuery.CardsInPlayerEnv</c> (CardEnvironment then MatchesPlayerEnv per card), the same
        /// read the framework uses for HuntingStandGuard.json's GuardCardUids.
        /// </summary>
        private static bool HuntingStandOnPlayerBoard()
        {
            var cards = GameQuery.CardsInPlayerEnv();
            if (cards.Count == 0)
            {
                WarnOnce("board-empty", "[WildlifeIndoorGuardPatch] GameQuery.CardsInPlayerEnv() returned no cards for the player's own board, so a Hunting Stand there was not seen (expected for a moment while a board is rebuilt on arrival; anything more means the env-scoped read is broken).");
                return false;
            }

            int named = 0;
            foreach (var card in cards)
            {
                string uid = CardUtil.GetCardUniqueId(card);
                if (uid == null) continue;
                named++;
                if (string.Equals(uid, HuntingStandUid, StringComparison.OrdinalIgnoreCase)) return true;
            }
            if (named == 0)
                WarnOnce("board-uids", $"[WildlifeIndoorGuardPatch] None of the {cards.Count} cards on the player's board gave a UniqueID, so a Hunting Stand there was not seen.");
            return false;
        }

        /// <summary>
        /// The verdict for one move. A chance of 1 always refuses; a lower chance is rolled once per
        /// (cause, agent, destination, day) and repeated for every retry that day. The first verdict of
        /// each key per day is logged at Info; the per-tick retries are silent.
        /// </summary>
        private static bool Refuse(string cause, float chance, string npcUid, string destUid, string destKey)
        {
            if (chance <= 0f) return false;

            int day = GameQuery.CurrentDay;
            if (day <= 0)
            {
                WarnOnce("day", "[WildlifeIndoorGuardPatch] GameQuery.CurrentDay read 0, so a chance below 1 is rolled on every move instead of once a day (a turned-back predator then gets in within a few ticks).");
                return chance >= 1f || UnityEngine.Random.value < chance;
            }

            if (day != _verdictDay)
            {
                _verdicts.Clear();
                _verdictDay = day;
            }

            string key = cause + "|" + npcUid + "|" + destKey;
            if (_verdicts.TryGetValue(key, out bool known)) return known;

            bool refuse = chance >= 1f || UnityEngine.Random.value < chance;
            _verdicts[key] = refuse;
            if (chance >= 1f)
                Plugin.Logger.LogInfo($"[WildlifeIndoorGuardPatch] Blocked '{npcUid}' from entering {cause} env '{destUid}' (day {day}).");
            else
                Plugin.Logger.LogInfo($"[WildlifeIndoorGuardPatch] {cause} roll {(refuse ? "refused" : "allowed")} '{npcUid}' into '{destUid}' for day {day} (chance {chance:0.##}).");
            return refuse;
        }

        private static void WarnOnce(string cause, string message)
        {
            if (_warned.Add(cause)) Plugin.Logger.LogWarning(message);
        }
    }
}
