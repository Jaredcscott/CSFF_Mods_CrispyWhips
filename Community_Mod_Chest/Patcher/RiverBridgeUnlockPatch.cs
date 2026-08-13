using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CSFFModFramework.Util;
using HarmonyLib;

namespace CommunityModChest.Patcher;

/// <summary>
/// The River Bridge CT10 improvement (Imp_RiverBridge.json) only becomes visible in River
/// Clearing's Improvements tab once ITS OWN CardUnlockConditions gate (CardsOnBoard: "HasPlank")
/// evaluates true — until then <c>ExplorationPopup.SetupImprovements</c>'s
/// <c>GameManager.CardIsOnBoard(EnvImprovement)</c> check finds no spawned ghost card and hides
/// the slot entirely, regardless of the CT10 already being present in the CT8's
/// EnvironmentImprovements array (see Documentation/Retrospectives/_Archive/
/// river-bridge-improvement-menu-2026-07-16.md — same wiring, root-caused to this exact gate).
/// Nothing guarantees a returning player is holding a Plank the moment the engine's periodic
/// unlock scan runs, so satisfying the gate can be delayed indefinitely — most visibly on old
/// saves, where the bridge can stay invisible run after run even though the mod is installed
/// and working correctly. This patch bypasses that discovery gate with
/// <see cref="CardUtil.ForceUnlockCard"/> for EVERY player, every run start, so the slot always
/// appears the first time River Clearing is visited that run — construction materials are still
/// required as normal.
///
/// The Village Pathfinder perk ("you know of an old path... the bridge is already standing...
/// no construction required") layers an ADDITIONAL, perk-only shortcut on top of that universal
/// bypass: it also marks the bridge fully built so perk holders never have to gather materials.
/// </summary>
internal static class RiverBridgeUnlockPatch
{
    private const string PerkUid = "traitsperkvillagepath";
    private const string BridgeUid = "cmcimpriverbridge";

    // River Clearing CT4 UID — matches MapNodes.json ConnectionGates[].GateConditions[].EnvUID
    // for the "ImprovementBuilt" condition already gating travel to Village Path.
    private const string RiverClearingEnvUid = "2b19b942a09fdd148a43798e942a74eb";

    // Minimum CSFFModFramework version exposing CardUtil.ForceUnlockCard /
    // QueueImprovementAutoComplete / MarkImprovementBuilt. Older frameworks throw
    // MissingMethodException on every direct call below.
    private const string MinFrameworkVersion = "2.17.0";

    private static bool _subscribed;
    private static Action _gmInitializedHandler;

    public static void Initialize()
    {
        if (_subscribed) return;

        if (!RequiredCapabilitiesPresent()) return;

        var gmType = CardUtil.FindGameType("GameManager");
        if (gmType == null)
        {
            Plugin.Logger.LogWarning("[RiverBridgeUnlock] GameManager type not found — inactive.");
            return;
        }

        // Must run AFTER GameManager.LoadCards() has restored the saved EnvironmentsData.
        // Marking the bridge built earlier (e.g. an InitializeStatsAndActions postfix, which
        // runs before LoadCards) makes MarkImprovementBuilt's create-if-missing fallback
        // pre-create a phantom River Clearing env entry; LoadCards then reports it as
        // "already registered as an environment" and discards the saved home-base env data.
        // OnGMInitialized fires at the end of FinishInitializing, well after LoadCards, and
        // once per load — preserving the per-load re-apply of the runtime unlock below.
        var field = gmType.GetField("OnGMInitialized", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null || field.FieldType != typeof(Action))
        {
            Plugin.Logger.LogWarning("[RiverBridgeUnlock] GameManager.OnGMInitialized not found — inactive.");
            return;
        }

        _gmInitializedHandler = OnRunStart;
        var current = (Action)field.GetValue(null);
        field.SetValue(null, (Action)Delegate.Combine(current, _gmInitializedHandler));
        _subscribed = true;
    }

    /// <summary>
    /// Verifies the three CardUtil helpers this patch calls directly actually exist on the
    /// CardUtil type bound at runtime. CMC always compiles against the current framework
    /// source, but a player's installed framework DLL can be older (e.g. the public GitHub
    /// repo shipped 2.14.2 while this patch needs 2.17.0+) — calling a helper missing from
    /// that older assembly throws MissingMethodException. Probing here lets us fail loud with
    /// an actionable message instead of relying solely on the runtime catch in OnRunStart.
    /// </summary>
    private static bool RequiredCapabilitiesPresent()
    {
        string[] required = { "ForceUnlockCard", "QueueImprovementAutoComplete", "MarkImprovementBuilt" };
        var missing = new List<string>();
        foreach (var name in required)
            if (AccessTools.Method(typeof(CardUtil), name) == null) missing.Add(name);

        if (missing.Count == 0) return true;

        Plugin.Logger.LogError(
            $"[RiverBridgeUnlock] CSFFModFramework.Util.CardUtil is missing required helper(s): " +
            $"{string.Join(", ", missing)}. This feature needs framework >= {MinFrameworkVersion}. " +
            "Village Pathfinder will not pre-build the River Bridge and the HasPlank discovery gate " +
            "will not be bypassed until the framework is updated.");
        return false;
    }

    private static void OnRunStart()
    {
        try
        {
            // Bypass the HasPlank discovery gate for every player — the runtime
            // CardUnlockConditions instance is rebuilt fresh each load, so this must be
            // re-applied every run start regardless of whether the player currently has a
            // Plank on hand.
            bool unlocked = CardUtil.ForceUnlockCard(BridgeUid);

            bool queued = false;
            bool marked = false;
            bool completedOnBoard = false;
            if (CardUtil.IsPerkEquipped(PerkUid))
            {
                var bridge = CardUtil.GetCardDataById(BridgeUid);
                queued = bridge != null && CardUtil.QueueImprovementAutoComplete(bridge);
                marked = CardUtil.MarkImprovementBuilt(RiverClearingEnvUid, BridgeUid);

                // Failure Mode B: the auto-complete queue armed just above is consumed ONLY by
                // a future SpawnCard call (.decomp/GameManager.cs ~9020). If the player saved
                // and reloaded while STANDING AT River Clearing, the improvement card already
                // spawned onto the current board before this OnGMInitialized handler ran (it
                // fires after LoadCards — see the Initialize() comment above), so the queue
                // never catches it and it stays at BlueprintStage 0 for the rest of the
                // session. Scan the current board directly and complete it if found.
                completedOnBoard = CompleteAlreadySpawnedBridge();
            }

            Plugin.Logger.LogInfo(
                $"[RiverBridgeUnlock] River Bridge forceUnlocked={unlocked}, Village Pathfinder perk " +
                $"active={queued || marked || completedOnBoard} (autoCompleteQueued={queued} " +
                $"markedBuiltInSaveData={marked} completedAlreadySpawnedOnBoard={completedOnBoard}). " +
                "Bridge slot should appear the first time River Clearing is visited this run" +
                (queued || marked || completedOnBoard ? ", already constructed for the perk holder." : "."));
        }
        catch (MissingMemberException ex)
        {
            // MissingMethodException (thrown by a direct call whose target doesn't exist on
            // an old framework DLL) derives from MissingMemberException — catch both here.
            Plugin.Logger.LogError(
                $"[RiverBridgeUnlock] framework too old for this feature (need >= {MinFrameworkVersion}): " +
                $"{ex.InnerException?.ToString() ?? ex.ToString()}. Village Pathfinder will not pre-build " +
                "the River Bridge.");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[RiverBridgeUnlock] failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }

    /// <summary>
    /// Scans the current board (<c>GameManager.AllCards</c> — current-environment-scoped, see
    /// root CLAUDE.md "AllCards env-scoped") for an already-spawned <see cref="BridgeUid"/>
    /// improvement card and completes it directly via <c>InGameCardBase.CompleteImprovement()</c>
    /// (the same call the engine's own SpawnCard makes for queued improvements). Idempotent —
    /// CompleteImprovement just re-sets CurrentStage to the already-max value on a card that's
    /// already complete — but the stage check below avoids re-logging every run start once
    /// the bridge is built. Returns false (never throws) on any missing link.
    /// </summary>
    private static bool CompleteAlreadySpawnedBridge()
    {
        try
        {
            var gm = CardUtil.GetGameManagerInstance();
            if (gm == null) return false;
            if (CardUtil.GetMemberValue(gm, "AllCards") is not IEnumerable allCards) return false;

            bool completedAny = false;
            foreach (var card in allCards)
            {
                if (card == null) continue;
                if (!BridgeUid.Equals(CardUtil.GetCardUniqueId(card), StringComparison.Ordinal)) continue;

                var blueprintData = CardUtil.GetMemberValue(card, "BlueprintData");
                var blueprintSteps = CardUtil.GetMemberValue(card, "BlueprintSteps");
                var currentStage = blueprintData == null ? null : CardUtil.GetMemberValue(blueprintData, "CurrentStage");
                if (currentStage != null && blueprintSteps != null &&
                    Convert.ToInt32(currentStage) >= Convert.ToInt32(blueprintSteps))
                {
                    continue; // already complete
                }

                var completeMethod = card.GetType().GetMethod("CompleteImprovement",
                    BindingFlags.Instance | BindingFlags.Public);
                if (completeMethod == null)
                {
                    Plugin.Logger.LogDebug("[RiverBridgeUnlock] CompleteAlreadySpawnedBridge: " +
                        "InGameCardBase.CompleteImprovement not found — skipping.");
                    continue;
                }

                completeMethod.Invoke(card, Array.Empty<object>());
                completedAny = true;
            }
            return completedAny;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogDebug($"[RiverBridgeUnlock] CompleteAlreadySpawnedBridge: reflection scan threw: {ex}");
            return false;
        }
    }
}
