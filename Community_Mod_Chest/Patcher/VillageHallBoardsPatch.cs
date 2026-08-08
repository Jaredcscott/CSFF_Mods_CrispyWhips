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
        // Renown + the village standing phase instead of a single NPC's values.
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

        private const string RenownStatUid = "cmcStatVillageRenown";
        private const string PhaseStatUid = "cmcStatVillagePhase";
        private const float RenownMax = 100f;

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
        };

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
            sections.AddRange(GetVisibleBoardLines(card));
            AppendStatusLines(sections, card.CardModel.UniqueID);

            if (sections.Count == 0)
            {
                return baseDescription;
            }

            var builder = new StringBuilder(baseDescription.Trim());
            foreach (var section in sections)
            {
                builder.Append("\n\n");
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
                    float renown = VillageClock.ReadStat(gm, RenownStatUid);
                    if (renown >= 0f)
                    {
                        sections.Add($"{RenownTier(renown)} ({(int)Math.Round(renown)} / {(int)RenownMax})");
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

        private static string RenownTier(float renown)
        {
            if (renown <= 0f) return "The village is barely known beyond its own fences.";
            float f = renown / RenownMax;
            if (f < 0.25f) return "Word of the village is beginning to spread.";
            if (f < 0.5f) return "The village is earning a modest reputation.";
            if (f < 0.8f) return "The village is well regarded across the valley.";
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
                string line = (action.ActionDescription != null ? action.ActionDescription.ToString() : string.Empty).Trim();
                if (!string.IsNullOrEmpty(line))
                {
                    visible.Add(line);
                }
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