using System;
using System.Reflection;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher;

/// <summary>
/// The Village Pathfinder perk ("you know of an old path... the bridge is already standing...
/// no construction required") only ever bypassed the WorldMap travel gate — MapNodes.json's
/// ConnectionGates for cmcEnvVillagePath already OR the ImprovementBuilt and PerkEquipped
/// conditions, so a perk holder could already travel without the bridge. But the River Bridge
/// CT10 itself (Imp_RiverBridge.json) had no mechanism backing the perk's own flavor text: a
/// perk holder visiting River Clearing still saw it as an unbuilt, gathering-required slot.
/// This patch marks it built (and pre-completed) for perk holders only — players without the
/// perk are unaffected and still discover/build it normally via the HasPlank CardsOnBoard gate.
/// </summary>
internal static class VillagePathfinderBridgePatch
{
    private const string PerkUid = "traitsperkvillagepath";
    private const string BridgeUid = "cmcimpriverbridge";

    // River Clearing CT4 UID — matches MapNodes.json ConnectionGates[].GateConditions[].EnvUID
    // for the "ImprovementBuilt" condition already gating travel to Village Path.
    private const string RiverClearingEnvUid = "2b19b942a09fdd148a43798e942a74eb";

    private static bool _subscribed;
    private static Action _gmInitializedHandler;

    public static void Initialize()
    {
        if (_subscribed) return;

        var gmType = CardUtil.FindGameType("GameManager");
        if (gmType == null)
        {
            Plugin.Logger.LogWarning("[VillagePathfinderBridge] GameManager type not found — inactive.");
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
            Plugin.Logger.LogWarning("[VillagePathfinderBridge] GameManager.OnGMInitialized not found — inactive.");
            return;
        }

        _gmInitializedHandler = OnRunStart;
        var current = (Action)field.GetValue(null);
        field.SetValue(null, (Action)Delegate.Combine(current, _gmInitializedHandler));
        _subscribed = true;
    }

    private static void OnRunStart()
    {
        try
        {
            if (!CardUtil.IsPerkEquipped(PerkUid)) return;

            // ForceUnlockCard's effect lives on a runtime CardUnlockConditions instance that is
            // rebuilt fresh each load, so it must be re-applied every run start while the perk
            // is active — do not skip based on the (persistent) built flag.
            bool unlocked = CardUtil.ForceUnlockCard(BridgeUid);
            var bridge = CardUtil.GetCardDataById(BridgeUid);
            bool queued = bridge != null && CardUtil.QueueImprovementAutoComplete(bridge);
            bool marked = CardUtil.MarkImprovementBuilt(RiverClearingEnvUid, BridgeUid);

            Plugin.Logger.LogInfo(
                $"[VillagePathfinderBridge] Village Pathfinder perk active — River Bridge forceUnlocked={unlocked} " +
                $"autoCompleteQueued={queued} markedBuiltInSaveData={marked}. Bridge should appear already " +
                "constructed the first time River Clearing is visited.");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[VillagePathfinderBridge] failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }
}
