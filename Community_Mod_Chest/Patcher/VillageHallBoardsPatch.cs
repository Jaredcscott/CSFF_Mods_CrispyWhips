using System;
using System.Collections.Generic;
using System.Text;
using CSFFModFramework.Util;
using HarmonyLib;
using TMPro;

namespace CommunityModChest.Patcher
{
    internal static class VillageHallBoardsPatch
    {
        // Live status readout appended under each board's notices (descriptive tier + number).
        // Friendship reads the NPC's player-side stat (Inn Keeper: cmcStatInnFriendship;
        // Miller/Weaver/Professor: the QuestChainSchedulePatch trust mirror). The Apothecary has
        // no trust stat, so that board shows quest progress only. The Town boards show Village
        // Renown + the village standing phase instead of a single NPC's values. The Town
        // Achievement Board (cmcBoardAchievements, Village_Master_Plan.md §10.9.2.2) is a fourth
        // shape - an earned-count summary plus ONE compact line per achievement, read
        // from the achievement GameStats rather than an NPC/Town status - handled by
        // AppendAchievementSections instead of the BoardStatuses table below.
        private sealed class BoardStatus
        {
            public string NpcName;            // sentence subject, e.g. "the Inn Keeper"
            public string FriendshipStatUid;  // null when the NPC has no tracked friendship
            public float FriendshipMax;
            public string QuestChainStatUid;  // null for the Town boards
            public int QuestChainMax;
            public bool IsTown;               // Town boards → Renown + phase instead of NPC values
        }

        private static readonly Dictionary<string, BoardStatus> BoardStatuses = new(StringComparer.Ordinal)
        {
            ["cmcBoardInnKeeper"] = new BoardStatus { NpcName = "the Inn Keeper", FriendshipStatUid = "cmcStatInnFriendship", FriendshipMax = 30f, QuestChainStatUid = "cmcStatInnQuestChain", QuestChainMax = 8 },
            ["cmcBoardMiller"] = new BoardStatus { NpcName = "the Miller", FriendshipStatUid = "cmcStatMillerTrustMirror", FriendshipMax = 100f, QuestChainStatUid = "cmcStatMillerQuestChain", QuestChainMax = 3 },
            ["cmcBoardWeaver"] = new BoardStatus { NpcName = "the Weaver", FriendshipStatUid = "cmcStatWeaverTrustMirror", FriendshipMax = 100f, QuestChainStatUid = "cmcStatWeaverQuestChain", QuestChainMax = 7 },
            ["cmcBoardApothecary"] = new BoardStatus { NpcName = "the Apothecary", FriendshipStatUid = null, FriendshipMax = 0f, QuestChainStatUid = "cmcStatApothQuestChain", QuestChainMax = 2 },
            ["cmcBoardProfessor"] = new BoardStatus { NpcName = "the Professor", FriendshipStatUid = "cmcStatProfessorTrustMirror", FriendshipMax = 100f, QuestChainStatUid = "cmcStatProfQuestChain", QuestChainMax = 4 },
            ["cmcBoardTown"] = new BoardStatus { IsTown = true },
            ["cmcBoardTownConstruction"] = new BoardStatus { IsTown = true },
        };

        private const string PhaseStatUid = "cmcStatVillagePhase";
        private const float ReputationMax = 100f;

        private struct DescriptionTextState
        {
            public bool AutoSizing;
            public float FontSize;
            public float FontSizeMin;
            public float FontSizeMax;
            public TextOverflowModes OverflowMode;
        }

        // Vanilla state per text object, captured only when a board first modifies it.
        // A text object absent from this dictionary has never been touched by us.
        private static readonly Dictionary<TextMeshProUGUI, DescriptionTextState> ModifiedTexts = new();

        private static readonly HashSet<string> BoardUids = new(StringComparer.Ordinal)
        {
            "cmcBoardTown",
            "cmcBoardTownConstruction",
            "cmcBoardInnKeeper",
            "cmcBoardMiller",
            "cmcBoardWeaver",
            "cmcBoardApothecary",
            "cmcBoardProfessor",
            "cmcBoardAchievements",
        };

        private const string AchievementsBoardUid = "cmcBoardAchievements";

        // Earned-latch stats (§10.9.2.3) — summary line counts these >= 0.5. Fixed order/labels
        // for readability only; the board's own DA text is the source of truth for wording.
        private static readonly string[] AchievementEarnedStatUids =
        {
            "cmcStatAchTraderFound",
            "cmcStatAchFullKit",
            "cmcStatAchBearSlain",
            "cmcStatAchHunter",
            "cmcStatAchExplorer",
            "cmcStatAchYearSurvived",
            "cmcStatAchAngler",
            "cmcStatAchSpelunker",
            "cmcStatAchShaman",
            "cmcStatAchNoiseMax",
            "cmcStatAchStinkyJar",
        };

        // Derived count stat for each of the six multi-part achievements, keyed by the earned latch
        // that gates that achievement's two board entries (the stat on their RequiredStatValues).
        // Read directly, never recomputed here: AchievementTrackerPatch owns the writes. The total
        // is read live from the count stat's own maximum (HiddenStat.GetMax), not copied here.
        private static readonly Dictionary<string, string> AchievementCountStatUids = new(StringComparer.Ordinal)
        {
            ["cmcStatAchFullKit"] = "cmcStatAchFullKitCount",
            ["cmcStatAchHunter"] = "cmcStatAchHunterCount",
            ["cmcStatAchAngler"] = "cmcStatAchAnglerCount",
            ["cmcStatAchShaman"] = "cmcStatAchShamanCount",
            ["cmcStatAchExplorer"] = "cmcStatAchExplorerCount",
            ["cmcStatAchSpelunker"] = "cmcStatAchSpelunkerCount",
        };

        // Last "lines|truncated" result LogBoardFit printed per board, so the line appears once per
        // change rather than on each of the five postfixes that fire while one board is open.
        private static readonly Dictionary<string, string> LastFitSignature = new(StringComparer.Ordinal);

        public static void Initialize(Harmony harmony)
        {
            harmony.Patch(
                AccessTools.Method(typeof(InspectionPopup), nameof(InspectionPopup.Setup), new[] { typeof(InGameCardBase) }),
                postfix: new HarmonyMethod(typeof(VillageHallBoardsPatch), nameof(RefreshBoardDescription_Postfix)));

            harmony.Patch(
                AccessTools.Method(typeof(InspectionPopup), nameof(InspectionPopup.RefreshInventory)),
                postfix: new HarmonyMethod(typeof(VillageHallBoardsPatch), nameof(RefreshBoardDescription_Postfix)));

            harmony.Patch(
                AccessTools.Method(typeof(InspectionPopup), nameof(InspectionPopup.RefreshGroupActions)),
                postfix: new HarmonyMethod(typeof(VillageHallBoardsPatch), nameof(RefreshBoardDescription_Postfix)));

            harmony.Patch(
                AccessTools.Method(typeof(InspectionPopup), nameof(InspectionPopup.RefreshDescription)),
                postfix: new HarmonyMethod(typeof(VillageHallBoardsPatch), nameof(RefreshBoardDescription_Postfix)));

            // Setup(SpecialActionSet) is the 5th writer of the shared DescriptionText object
            // (vanilla Time Options screen). It sets TitleSectionCard = null before returning,
            // so IsVillageBoard(null) is false and ApplyDescriptionSizing correctly takes the
            // restore branch here — same postfix, no new logic needed. Missing this patch left
            // Time Options' description permanently truncated/undersized after inspecting a
            // village board (critical-analysis 2026-08-03).
            harmony.Patch(
                AccessTools.Method(typeof(InspectionPopup), nameof(InspectionPopup.Setup), new[] { typeof(SpecialActionSet) }),
                postfix: new HarmonyMethod(typeof(VillageHallBoardsPatch), nameof(RefreshBoardDescription_Postfix)));

            Plugin.Logger.LogDebug("[VillageHallBoardsPatch] initialized.");
        }

        public static void RefreshBoardDescription_Postfix(InspectionPopup __instance)
        {
            var titleCard = __instance?.TitleSectionCard;
            bool isVillageBoard = IsVillageBoard(titleCard);

            UpdateActionsVisibility(__instance, isVillageBoard);
            ApplyDescriptionSizing(__instance, isVillageBoard);

            if (!isVillageBoard || __instance.DescriptionText == null)
            {
                return;
            }

            __instance.DescriptionText.text = BuildBoardDescription(titleCard);
            LogBoardFit(__instance.DescriptionText, titleCard.CardModel.UniqueID);
        }

        private static bool IsVillageBoard(InGameCardBase card)
        {
            return card != null
                && card.CardModel != null
                && BoardUids.Contains(card.CardModel.UniqueID);
        }

        private static string BuildBoardDescription(InGameCardBase card)
        {
            string baseDescription = card.CardModel.GetCardDescription(card) ?? string.Empty;

            var sections = new List<string>();
            bool isAchievementBoard = string.Equals(card.CardModel.UniqueID, AchievementsBoardUid, StringComparison.Ordinal);
            if (isAchievementBoard)
            {
                // One compact block instead of a paragraph per entry: see AppendAchievementSections.
                AppendAchievementSections(sections, card);
            }
            else
            {
                sections.AddRange(GetVisibleBoardLines(card));
                AppendStatusLines(sections, card.CardModel.UniqueID);
            }

            if (sections.Count == 0)
            {
                return baseDescription;
            }

            // The achievement board leaves its flavour description out. Measured live 2026-09-19
            // (walkthrough T2.236): description (2 lines) + summary + eleven entries needs 16 lines,
            // and the box holds 15 at its auto-size floor (lines=15 truncated=True fontSize=24), so
            // Stinky Jar, the last entry, was cut off. Summary plus list is 13.
            var builder = new StringBuilder(isAchievementBoard ? string.Empty : baseDescription.Trim());
            foreach (var section in sections)
            {
                if (builder.Length > 0 || !isAchievementBoard)
                {
                    builder.Append("\n\n");
                }
                builder.Append(section);
            }

            return builder.ToString();
        }

        private static void AppendStatusLines(List<string> sections, string boardUid)
        {
            if (!BoardStatuses.TryGetValue(boardUid, out var status))
            {
                return;
            }

            try
            {
                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null)
                {
                    return;
                }

                if (status.IsTown)
                {
                    // VillageReputationPatch.CurrentReputation() returns NaN (not the -1
                    // "unreadable" sentinel VillageClock.ReadStat uses elsewhere in this method)
                    // because 0 is now a legitimate, meaningful Reputation value in its own
                    // right (crime-net-of-civic-standing can legitimately sit at or near 0) —
                    // a `>= 0f` read-succeeded check would silently swallow this entire status
                    // line for any village with net-negative reputation.
                    float reputation = VillageReputationPatch.CurrentReputation();
                    if (!float.IsNaN(reputation))
                    {
                        sections.Add($"{ReputationTier(reputation)} ({(int)Math.Round(reputation)} / {(int)ReputationMax})");
                    }

                    float phase = VillageClock.ReadStat(gm, PhaseStatUid);
                    if (phase >= 0f)
                    {
                        sections.Add(PhaseTier(phase));
                    }

                    return;
                }

                if (status.FriendshipStatUid != null)
                {
                    float friendship = VillageClock.ReadStat(gm, status.FriendshipStatUid);
                    if (friendship >= 0f)
                    {
                        sections.Add($"{FriendshipTier(status.NpcName, friendship, status.FriendshipMax)} ({(int)Math.Round(friendship)} / {(int)status.FriendshipMax})");
                    }
                }

                if (status.QuestChainStatUid != null)
                {
                    float chain = VillageClock.ReadStat(gm, status.QuestChainStatUid);
                    if (chain >= 0f)
                    {
                        sections.Add($"{QuestTier(status.NpcName, chain, status.QuestChainMax)} ({(int)Math.Round(chain)} / {status.QuestChainMax})");
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogDebug($"[VillageHallBoardsPatch] Failed to build status lines for '{boardUid}': {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        /// <summary>Town Achievement Board prose. Every other board prints one paragraph per visible
        /// notice, and at 4-8 paragraphs that fits the popup's DescriptionText. This board has eleven
        /// always-visible entries plus a summary and six progress counters, which came to 19
        /// paragraphs in the same fixed-height text that <see cref="ApplyDescriptionSizing"/> puts in
        /// <c>TextOverflowModes.Truncate</c>, so the tail (the later entries AND the summary) was cut
        /// off with no error: the defect CMC 1.67.3 benched the board for
        /// (Documentation/Retrospectives/cmc-achievement-board-presentation.md). It now renders as two
        /// sections: the earned-count summary, then one line per achievement holding that entry's own
        /// localized ActionName (which already reads unclaimed or earned) plus, for a multi-part
        /// achievement not yet earned, its progress. The per-entry ActionDescription sentences are
        /// deliberately not printed on this board. Reads only; AchievementTrackerPatch and
        /// AchievementKillEffectsPatch own every write.</summary>
        private static void AppendAchievementSections(List<string> sections, InGameCardBase card)
        {
            try
            {
                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null)
                {
                    return;
                }

                int earned = 0;
                bool anyReadable = false;
                foreach (var statUid in AchievementEarnedStatUids)
                {
                    float value = VillageClock.ReadStat(gm, statUid);
                    if (value < 0f) continue; // unreadable this tick - don't miscount as "not earned"
                    anyReadable = true;
                    if (value >= 0.5f) earned++;
                }
                if (anyReadable)
                {
                    sections.Add($"Achievements earned: {earned} of {AchievementEarnedStatUids.Length}");
                }

                var lines = new List<string>();
                foreach (var action in GetVisibleBoardActions(card))
                {
                    string name = (action.ActionName != null ? action.ActionName.ToString() : string.Empty).Trim();
                    if (string.IsNullOrEmpty(name)) continue;
                    lines.Add(name + ProgressSuffix(gm, action));
                }
                if (lines.Count > 0)
                {
                    sections.Add(string.Join("\n", lines));
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogDebug($"[VillageHallBoardsPatch] Failed to build achievement board sections: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        /// <summary>" (n/total)" for a multi-part achievement that is not yet earned, otherwise empty.
        /// Which achievement an entry belongs to comes from the entry's own RequiredStatValues gate,
        /// so the board JSON stays the single source of that mapping.</summary>
        private static string ProgressSuffix(object gm, DismantleCardAction action)
        {
            var gates = action.RequiredStatValues;
            if (gates == null || gates.Length == 0 || gates[0].Stat == null)
            {
                return string.Empty;
            }

            string earnedStatUid = gates[0].Stat.UniqueID;
            if (string.IsNullOrEmpty(earnedStatUid) || !AchievementCountStatUids.TryGetValue(earnedStatUid, out var countStatUid))
            {
                return string.Empty;
            }

            if (VillageClock.ReadStat(gm, earnedStatUid) >= 0.5f)
            {
                return string.Empty; // earned: the entry's own name already says so
            }

            float count = VillageClock.ReadStat(gm, countStatUid);
            float total = HiddenStat.GetMax(countStatUid);
            if (count < 0f || total <= 0f)
            {
                return string.Empty; // unreadable this tick: omit rather than show a bogus 0
            }

            return $" ({(int)Math.Round(count)}/{(int)Math.Round(total)})";
        }

        private static string FriendshipTier(string npc, float value, float max)
        {
            string subject = Capitalize(npc);
            if (value <= 0f)
            {
                return $"You and {npc} are still strangers.";
            }

            float f = max > 0f ? value / max : 0f;
            if (f < 0.25f) return $"{subject} is beginning to warm to you.";
            if (f < 0.5f) return $"{subject} counts you a friendly face.";
            if (f < 0.8f) return $"{subject} considers you a good friend.";
            return $"{subject} trusts you deeply.";
        }

        private static string QuestTier(string npc, float chain, int max)
        {
            int n = (int)Math.Round(chain);
            if (n <= 0) return $"You have not yet completed any errands for {npc}.";
            if (n >= max) return $"You have completed every errand for {npc}.";
            return $"You are working through {npc}'s errands.";
        }

        private static string ReputationTier(float reputation)
        {
            if (reputation < 0f)
            {
                // Mirrors the negative (crime-dominant) side of the merged Reputation stat.
                // Unified against ReputationMax (100) the same way the positive branch below is,
                // rather than VillageCrimePatch's own separate 0-100 crime scale, since a net
                // reputation of -30 isn't necessarily "30 crime" (it could be 30 crime against 0
                // civic total, or 80 crime against 50 civic total) — the tier describes the NET
                // standing, not the raw crime ledger.
                float negF = -reputation / ReputationMax;
                if (negF < 0.25f) return "Rumors of trouble are starting to follow the village's name.";
                if (negF < 0.6f) return "The village is gaining an uneasy reputation.";
                return "The village is spoken of only in warnings.";
            }
            if (reputation <= 0f) return "The village is barely known beyond its own fences.";
            // Cutoffs unified to 25/50/75 to match CMC_BoardTown.json's own renown_low/mid/high/top
            // DA bands (0.8 previously disagreed with the JSON's 0.75 — pre-existing bug, fixed
            // here as part of touching this method for the merge).
            float f = reputation / ReputationMax;
            if (f < 0.25f) return "Word of the village is beginning to spread.";
            if (f < 0.5f) return "The village is earning a modest reputation.";
            if (f < 0.75f) return "The village is well regarded across the valley.";
            return "The village's renown is the pride of the valley.";
        }

        private static string PhaseTier(float phase)
        {
            return phase < 0.5f
                ? "The village has not yet been properly introduced."
                : "The village is established and open for business.";
        }

        private static string Capitalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        private static List<string> GetVisibleBoardLines(InGameCardBase card)
        {
            var visible = new List<string>();
            foreach (var action in GetVisibleBoardActions(card))
            {
                string line = (action.ActionDescription != null ? action.ActionDescription.ToString() : string.Empty).Trim();
                if (!string.IsNullOrEmpty(line))
                {
                    visible.Add(line);
                }
            }

            return visible;
        }

        private static List<DismantleCardAction> GetVisibleBoardActions(InGameCardBase card)
        {
            var visible = new List<DismantleCardAction>();
            var actions = card.DismantleActions;
            if (actions == null || actions.Length == 0)
            {
                return visible;
            }

            var alreadyDisplayed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var action in actions)
            {
                if (action == null || action.PerformUponInspection)
                {
                    continue;
                }

                string actionName = action.ActionName != null ? action.ActionName.DefaultText : string.Empty;
                if (alreadyDisplayed.Contains(actionName) || !CanAppear(action, card))
                {
                    continue;
                }

                alreadyDisplayed.Add(actionName);
                visible.Add(action);
            }

            return visible;
        }

        private static bool CanAppear(DismantleCardAction action, InGameCardBase card)
        {
            try
            {
                // AlwaysShow short-circuits DismantleCardAction.CanAppear() before it ever
                // consults RequiredStatValues (that only happens in StatsAreCorrect). Every
                // board status line is authored with AlwaysShow:true (informational, no
                // WillHaveAnEffect), so RequiredStatValues must be checked here explicitly or
                // mutually-exclusive status lines (e.g. "not yet begun" / "complete") render
                // simultaneously.
                if (!action.CanAppear(card, true, false, InGameNPCOrPlayer.PlayerAgent))
                {
                    return false;
                }

                return action.StatsAreCorrect(InGameNPCOrPlayer.PlayerAgent, null);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogDebug($"[VillageHallBoardsPatch] Failed to evaluate board action visibility on '{card?.CardModel?.UniqueID}': {ex.InnerException?.ToString() ?? ex.ToString()}");
                return false;
            }
        }

        /// <summary>Confirmation signal for the cmc-achievement-board-presentation retro: once a board's
        /// text is set, force TMP to lay it out and report whether <c>Truncate</c> cut anything. A board
        /// whose notices outgrow the fixed-height DescriptionText loses its TAIL silently, and this is the
        /// only place that is observable without someone counting lines on screen. Info when it fits,
        /// Warning when it does not, once per distinct result per board.</summary>
        private static void LogBoardFit(TextMeshProUGUI text, string boardUid)
        {
            try
            {
                text.ForceMeshUpdate(true, false);
                bool truncated = text.isTextTruncated;
                int lines = text.textInfo != null ? text.textInfo.lineCount : -1;
                if (lines == 0 && !string.IsNullOrEmpty(text.text))
                {
                    // Not laid out yet: the first postfix can run before the box has a height, and TMP
                    // then reports 0 lines with truncated=True for any text. Seen live 2026-09-19 as a
                    // 'lines=0 truncated=True' Warning ahead of the real 'lines=15' reading.
                    Plugin.Logger.LogDebug($"[VillageHallBoardsPatch] Board '{boardUid}' text not laid out yet; layout not measured.");
                    return;
                }

                string signature = $"{lines}|{truncated}";
                if (LastFitSignature.TryGetValue(boardUid, out var previous) && previous == signature)
                {
                    return;
                }

                LastFitSignature[boardUid] = signature;
                string message = $"[VillageHallBoardsPatch] Board '{boardUid}' text layout: lines={lines} truncated={truncated} fontSize={text.fontSize:0.#} (autoSizing={text.enableAutoSizing} min={text.fontSizeMin:0.#} max={text.fontSizeMax:0.#})";
                if (truncated)
                {
                    Plugin.Logger.LogWarning(message + " - notices past the last visible line are cut off.");
                }
                else
                {
                    Plugin.Logger.LogInfo(message);
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogDebug($"[VillageHallBoardsPatch] Could not measure board text layout for '{boardUid}': {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void UpdateActionsVisibility(InspectionPopup popup, bool isVillageBoard)
        {
            var optionsParent = popup?.DismantleOptionsParent;
            if (optionsParent == null)
            {
                return;
            }

            optionsParent.gameObject.SetActive(!isVillageBoard);
        }

        private static void ApplyDescriptionSizing(InspectionPopup popup, bool isVillageBoard)
        {
            var text = popup?.DescriptionText;
            if (text == null)
            {
                return;
            }

            if (isVillageBoard)
            {
                if (!ModifiedTexts.TryGetValue(text, out var vanilla))
                {
                    vanilla = new DescriptionTextState
                    {
                        AutoSizing = text.enableAutoSizing,
                        FontSize = text.fontSize,
                        FontSizeMin = text.fontSizeMin,
                        FontSizeMax = text.fontSizeMax,
                        OverflowMode = text.overflowMode,
                    };
                    ModifiedTexts[text] = vanilla;
                    Plugin.Logger.LogInfo(
                        $"[VillageHallBoardsPatch] Captured vanilla DescriptionText state: autoSizing={vanilla.AutoSizing} fontSize={vanilla.FontSize} min={vanilla.FontSizeMin} max={vanilla.FontSizeMax} overflow={vanilla.OverflowMode}");
                }

                // When vanilla auto-sizes, fontSize holds a computed value — the
                // prefab's intended ceiling is fontSizeMax, not fontSize.
                float baseSize = vanilla.AutoSizing ? vanilla.FontSizeMax : vanilla.FontSize;
                text.enableAutoSizing = true;
                text.fontSizeMax = baseSize;
                text.fontSizeMin = baseSize * 0.6f;
                text.overflowMode = TextOverflowModes.Truncate;
            }
            else if (ModifiedTexts.TryGetValue(text, out var vanilla))
            {
                text.enableAutoSizing = vanilla.AutoSizing;
                text.fontSizeMin = vanilla.FontSizeMin;
                text.fontSizeMax = vanilla.FontSizeMax;
                text.fontSize = vanilla.FontSize;
                text.overflowMode = vanilla.OverflowMode;
                ModifiedTexts.Remove(text);
            }
        }
    }
}