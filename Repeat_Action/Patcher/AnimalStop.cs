using System;
using System.Collections.Generic;

namespace Repeat_Action.Patcher
{
    public static partial class ActionPatch
    {
        // =====================================================================
        // STOP WHEN AN ANIMAL ARRIVES (Nexus request c178705767; shipped 2.2.5, see RepeatAction/ROADMAP.md Plan Reconciliation Log 2026-11-05).
        // A wild animal that walks onto the player's board during a long repeat can leave again before the player stops
        // the run by hand. So between iterations the run looks at the agents on this board, and one on the watch list
        // that was not here at the previous look stops the run before its next iteration.
        //
        // This mod has no dependency on the framework (no [BepInDependency]; see UtilityBeltLockBridge.cs for the
        // reflection shape it uses for another mod), so the watch list is its own setting and an agent is read through
        // the game's own types only: GameManager.AllNPCs holds every agent in the world (.decomp/GameManager.cs), so each
        // one is held to the player's board by its own environment (InGameNPC.CurrentEnvironment.MatchesPlayerEnv, the
        // test .decomp/GeneralCondition.cs makes) and to a board card that is not in the background (a hiding agent's
        // card goes to the background: .decomp/InGameCardBase.cs WillBeInBG). The card's own CardEnvironment is NOT used:
        // GameManager.MoveNPC sets it only when the agent arrives in the player's environment, so an agent that left
        // keeps the player's environment on its card (CLAUDE.md "An AllCards UID scan is NOT the cards in this room").
        //
        // Written in C# 5 (no interpolated strings or expression bodies) so Development_Tools/Tests/
        // RepeatAction-AnimalArrivalStop.Tests.ps1 can compile this whole file with Windows PowerShell's Add-Type.
        // =====================================================================

        // Animals That Stop A Run, parsed: entries compared case-insensitively against an agent's UniqueID, its English
        // name (AgentName.DefaultText) and the name shown in the player's language. "*" is the generic rule: any agent not
        // allied with the player that cannot trade (every vanilla wild animal, but also the forest's monsters, so it is
        // opt-in). Re-parsed only when the setting's text changes, like Stop At Quantity.
        private static readonly HashSet<string> noAnimalNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static HashSet<string> animalNamesCache = noAnimalNames;
        private static bool animalAnyWildCache;
        private static string animalNamesSource;

        private static HashSet<string> AnimalNames
        {
            get
            {
                string raw = Plugin.AnimalsThatStopARun != null ? Plugin.AnimalsThatStopARun.Value : null;
                if (string.Equals(raw, animalNamesSource, StringComparison.Ordinal)) return animalNamesCache;
                animalNamesSource = raw;
                var parsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                bool anyWild = false;
                if (!string.IsNullOrEmpty(raw))
                {
                    foreach (var part in raw.Split(','))
                    {
                        string entry = part.Trim();
                        if (entry.Length == 0) continue;
                        if (entry == "*") { anyWild = true; continue; }
                        parsed.Add(entry);
                    }
                }
                animalNamesCache = parsed;
                animalAnyWildCache = anyWild;
                RunLog("Animals That Stop A Run: " + parsed.Count + " name(s)" + (anyWild ? " plus every wild agent (*)" : ""));
                return animalNamesCache;
            }
        }

        // The watched agents on the board at the previous look, so an animal already here never stops the run: only one
        // that arrives does (and one that leaves and comes back arrives again). Tracked whatever the setting, so turning
        // it on mid-run already knows what was here.
        private static readonly HashSet<InGameNPC> animalsHere = new HashSet<InGameNPC>();
        private static readonly List<InGameNPC> animalsSeenNow = new List<InGameNPC>();
        private static bool animalReadFailedWarned;

        /// <summary>At run start: what is on the board now is the baseline, never an arrival.</summary>
        private static void ResetAnimalWatch()
        {
            animalsHere.Clear();
            if (!CollectWatchedAnimalsHere(animalsSeenNow)) return;
            for (int i = 0; i < animalsSeenNow.Count; i++) animalsHere.Add(animalsSeenNow[i]);
            if (animalsSeenNow.Count > 0)
                RunLog("Stop When An Animal Arrives: " + animalsSeenNow.Count + " watched animal(s) already here at the start (" + AnimalNamesOf(animalsSeenNow) + "); only a new arrival stops the run");
        }

        /// <summary>
        /// Stop When An Animal Arrives: "{animal} arrived" when a watched agent is on the player's board now that was not at
        /// the previous look (and the setting is on), else null. <paramref name="iteration"/> and <paramref name="when"/>
        /// place the check in its log line.
        /// </summary>
        private static string CheckAnimalArrival(int iteration, string when)
        {
            if (!CollectWatchedAnimalsHere(animalsSeenNow))
            {
                RunLog("iteration " + iteration + ", " + when + ": the agents on this board cannot be read, so Stop When An Animal Arrives cannot fire");
                return null;
            }
            InGameNPC arrived = null;
            for (int i = 0; i < animalsSeenNow.Count; i++)
                if (!animalsHere.Contains(animalsSeenNow[i])) { arrived = animalsSeenNow[i]; break; }
            animalsHere.Clear();
            for (int i = 0; i < animalsSeenNow.Count; i++) animalsHere.Add(animalsSeenNow[i]);
            if (arrived == null) return null;
            string name = AnimalName(arrived);
            if (Plugin.StopWhenAnimalArrives == null || !Plugin.StopWhenAnimalArrives.Value)
            {
                RunLog("iteration " + iteration + ", " + when + ": '" + name + "' arrived on this board; Stop When An Animal Arrives is off, so the run carries on");
                return null;
            }
            RunLog("iteration " + iteration + ", " + when + ": '" + name + "' (" + AnimalUid(arrived) + ") arrived on this board (Stop When An Animal Arrives)");
            return Messages.En("{0} arrived", name);
        }

        /// <summary>
        /// Fills <paramref name="into"/> with the watched agents standing on the player's board, in AllNPCs order. False when
        /// the agent list cannot be read (warned once; the check then cannot fire).
        /// </summary>
        private static bool CollectWatchedAnimalsHere(List<InGameNPC> into)
        {
            into.Clear();
            try
            {
                var gm = GameManager.Instance;
                if (gm == null || gm.AllNPCs == null) return false;
                var names = AnimalNames;
                if (names.Count == 0 && !animalAnyWildCache) return true;
                for (int i = 0; i < gm.AllNPCs.Count; i++)
                {
                    var npc = gm.AllNPCs[i];
                    if (OnPlayerBoard(npc) && IsWatchedAnimal(npc, names)) into.Add(npc);
                }
                return true;
            }
            catch (Exception ex)
            {
                if (!animalReadFailedWarned)
                {
                    animalReadFailedWarned = true;
                    Logger.LogWarning("[Repeat] Could not read the agents on this board; Stop When An Animal Arrives cannot fire: " + ex.GetType().Name + ": " + ex.Message);
                }
                else Logger.LogDebug("[Repeat] reading the agents on this board failed again: " + ex.GetType().Name);
                into.Clear();
                return false;
            }
        }

        /// <summary>
        /// The agent stands on the player's board where the player can see it: in the player's environment by its own
        /// environment, with a live board card that is not in the background (a hiding agent's card is).
        /// </summary>
        private static bool OnPlayerBoard(InGameNPC npc)
        {
            if (npc == null || !npc) return false;
            var card = npc.AssociatedCard;
            if (!IsLive(card) || card.InBackground) return false;
            return npc.CurrentEnvironment.MatchesPlayerEnv;
        }

        private static bool IsWatchedAnimal(InGameNPC npc, HashSet<string> names)
        {
            var model = npc.NPCModel;
            if (model == null || !model || model.AlliedWithPlayer) return false;
            if (animalAnyWildCache && model.CannotTrade) return true;
            if (names.Count == 0) return false;
            return NameListed(names, model.UniqueID)
                || NameListed(names, model.AgentName.DefaultText)
                || NameListed(names, LocalizedText(model.AgentName))
                || NameListed(names, npc.GetName());
        }

        private static bool NameListed(HashSet<string> names, string value)
        {
            return !string.IsNullOrEmpty(value) && names.Contains(value.Trim());
        }

        /// <summary>The agent's name in the player's language, for the toast and the log.</summary>
        private static string AnimalName(InGameNPC npc)
        {
            string n = null;
            try { n = npc.GetName(); } catch (Exception ex) { Logger.LogDebug("[Repeat] reading an agent's name failed: " + ex.GetType().Name); }
            if (string.IsNullOrEmpty(n) && npc.NPCModel != null) n = npc.NPCModel.AgentName.DefaultText;
            return string.IsNullOrEmpty(n) ? "?" : n;
        }

        private static string AnimalUid(InGameNPC npc)
        {
            return npc.NPCModel != null ? npc.NPCModel.UniqueID : "?";
        }

        private static string AnimalNamesOf(List<InGameNPC> list)
        {
            var parts = new List<string>(list.Count);
            for (int i = 0; i < list.Count; i++) parts.Add(AnimalName(list[i]));
            return string.Join(", ", parts.ToArray());
        }
    }
}
