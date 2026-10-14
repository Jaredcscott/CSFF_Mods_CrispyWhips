using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using CSFFModFramework.Util;
using UnityEngine;

namespace Sirus23ModCollection.Patcher;

/// <summary>
/// Feature 37 (a truffle hog, a no-op without Herbs and Fungi). A run-start pass, subscribed ONCE per process to
/// GameManager.OnGMInitialized (W0-27: Sirus's GameDataReady handler runs before H&amp;F's LoadMainGameData postfix on the
/// first load, so H&amp;F's truffle drops do not exist yet at that moment; OnGMInitialized fires from FinishInitializing,
/// after both, on every run start). It does two things, both only when herbs_fungi_truffle resolves:
/// <list type="number">
/// <item>appends ONE card-keyed CardOrTagIndividualDropChanceModifier (FerretTrapPatch.AppendCardModifier, the W0-20
/// shape) keyed on sh_truffle_hog to the DropChance.CardsModifiers of every CardDrop of herbs_fungi_truffle found by
/// scanning the DismantleActions of every loaded card (the "Dig up Mud" and "Dig up Dirt" drop and the "Truffle"
/// collection of "Dig for Truffles"; found by scan, not by UID list, because which locations carry them depends on
/// H&amp;F's localization-key matching);</item>
/// <item>adds the "Train to Root" CardInteraction to the vanilla BoarFreePiglet (drag Edible Acorns onto a piglet at
/// full Trust and it becomes the Truffle Hog). This is the HIDE: without H&amp;F nothing is added, so the training action
/// does not exist, no foreign UID is written in shipped JSON, and the hog (which needs the action to be made) cannot be
/// obtained.</item>
/// </list>
/// Idempotent and per-run safe: the subscription is made once (the early return and the latch are both load-bearing),
/// an existing modifier or action is left alone, and a reload appends nothing. Without H&amp;F it logs ONE Debug line per
/// process and nothing per dig (no handler is installed on any dig).
/// </summary>
internal static class TruffleHogPatch
{
    private const string HogUid = "sh_truffle_hog";
    private const string TruffleUid = "herbs_fungi_truffle";
    private const string PigletUid = "3a2695b0c1f80874d9237dc2155de4d9";
    private const string EdibleAcornsUid = "92de21bc827f1c74e8dbf8c72a51aa5a";
    private const string TrainActionName = "Train to Root";

    // INFERRED balance placeholders from W0-27: the 1 percent mud/dirt drop becomes about 3 percent; the 25 percent
    // "Dig for Truffles" drop becomes 40 percent. Flat, scaled down by the hog's Hunger (Special1).
    internal const float DirtDropBonus = 2f;
    internal const float DigForTrufflesBonus = 15f;
    private const string DigForTrufflesName = "Dig for Truffles";

    // Percent of the piglet's Trust bar (a fuel stat: FuelCapacity, max 11520) it needs to be trained.
    private const float TrainMinTrust = 0.95f;
    private const int TrainDaytimeCost = 4;

    private static bool _subscribed;
    private static bool _skipLogged;
    private static ManualLogSource Logger => Plugin.Logger;

    internal static void Register()
    {
        if (_subscribed) return;
        _subscribed = true;
        GameManager.OnGMInitialized += OnRunStart;
    }

    private static void OnRunStart()
    {
        try
        {
            var truffle = CardUtil.GetCardDataById(TruffleUid) as CardData;
            if (truffle == null)
            {
                if (!_skipLogged)
                {
                    _skipLogged = true;
                    Logger?.Log(LogLevel.Debug, $"[TruffleHog] {TruffleUid} does not resolve (Herbs and Fungi is absent or renamed it); the truffle hog is inert and its training action is not added.");
                }
                return;
            }

            var hog = CardUtil.GetCardDataById(HogUid) as CardData;
            if (hog == null)
            {
                Logger?.LogWarning($"[TruffleHog] {HogUid} is not loaded; the truffle hog is inert.");
                return;
            }

            int trained = AddTrainingAction(hog);
            ScanTruffleDrops(truffle, hog, out int added, out int already, out int drops);
            Logger?.LogInfo($"[TruffleHog] truffle drops found {drops}: appended {added}, already carried {already}; Train to Root on the piglet: {(trained > 0 ? "added" : trained == 0 ? "already there" : "not added")}.");
        }
        catch (Exception ex)
        {
            Logger?.LogWarning($"[TruffleHog] The hog gives no truffle bonus: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }

    private static FieldInfo _droppedCardsField;

    private static void ScanTruffleDrops(CardData truffle, CardData hog, out int added, out int already, out int drops)
    {
        added = already = drops = 0;
        _droppedCardsField ??= typeof(CardsDropCollection).GetField("DroppedCards", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (_droppedCardsField == null)
        {
            Logger?.LogWarning("[TruffleHog] CardsDropCollection.DroppedCards field not found (a game update may have renamed it); the hog gives no truffle bonus.");
            return;
        }

        var all = Resources.FindObjectsOfTypeAll<CardData>();
        foreach (var card in all)
        {
            if (card == null) continue;
            var actions = card.DismantleActions;
            if (actions == null) continue;
            foreach (var action in actions)
            {
                var produced = action?.ProducedCards;
                if (produced == null) continue;
                bool digForTruffles = string.Equals(action.ActionName.DefaultText, DigForTrufflesName, StringComparison.Ordinal);
                foreach (var collection in produced)
                {
                    if (collection == null) continue;
                    var dropped = _droppedCardsField.GetValue(collection) as CardDrop[];
                    if (dropped == null) continue;
                    for (int i = 0; i < dropped.Length; i++)
                    {
                        if (!ReferenceEquals(dropped[i].DroppedCard, truffle)) continue;
                        drops++;
                        // CardDrop and CardDropChance are structs inside an array: edit the element in place.
                        bool did = FerretTrapPatch.AppendCardModifier(ref dropped[i].DropChance, hog,
                            digForTruffles ? DigForTrufflesBonus : DirtDropBonus, DurabilitiesTypes.Special1);
                        if (did) added++; else already++;
                    }
                }
            }
        }
    }

    /// <summary>1 added, 0 already there, -1 could not be added.</summary>
    private static int AddTrainingAction(CardData hog)
    {
        var piglet = CardUtil.GetCardDataById(PigletUid) as CardData;
        var acorns = CardUtil.GetCardDataById(EdibleAcornsUid) as CardData;
        if (piglet == null || acorns == null)
        {
            Logger?.LogWarning($"[TruffleHog] Vanilla piglet or Edible Acorns not found (piglet {(piglet != null)}, acorns {(acorns != null)}); no training action added.");
            return -1;
        }

        var existing = piglet.CardInteractions ?? Array.Empty<CardOnCardAction>();
        foreach (var ci in existing)
        {
            if (ci != null && string.Equals(ci.ActionName.DefaultText, TrainActionName, StringComparison.Ordinal)) return 0;
        }

        var action = new CardOnCardAction();
        action.ActionName = new LocalizedString { ParentObjectID = HogUid, LocalizationKey = "SIR_TruffleHog_Train_ActionName", DefaultText = TrainActionName };
        action.ActionDescription = new LocalizedString { ParentObjectID = HogUid, LocalizationKey = "SIR_TruffleHog_Train_ActionDescription", DefaultText = "Show a fully trusting piglet an acorn and teach it to root for truffles." };
        action.VictorySettings = new VictoryCondition { Victory = false, SpecialEnding = false, VictoryMessage = new LocalizedString { ParentObjectID = "", LocalizationKey = "", DefaultText = "" } };
        action.CustomConfirmText = new LocalizedString { ParentObjectID = "", LocalizationKey = "", DefaultText = "" };
        action.UseMiniTicks = MiniTicksBehavior.DefaultBehavior;

        // Borrow the vanilla Feed action's tags and sounds (read-only, shared), as a drag action expects them.
        CardOnCardAction feed = existing.Length > 0 ? existing[0] : null;
        action.ActionTags = feed?.ActionTags ?? Array.Empty<ActionTag>();
        var soundsField = typeof(CardAction).GetField("ActionSounds", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        soundsField?.SetValue(action, feed != null && soundsField != null ? soundsField.GetValue(feed) : Array.Empty<AudioClip>());
        var costField = typeof(CardAction).GetField("DaytimeCost", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (costField == null)
        {
            Logger?.LogWarning("[TruffleHog] CardAction.DaytimeCost field not found; no training action added.");
            return -1;
        }
        costField.SetValue(action, TrainDaytimeCost);

        action.RequiredStatValues = Array.Empty<StatValueTrigger>();
        action.RequiredNPCStatValues = Array.Empty<NPCStatCondition>();
        action.RequiredCardsOnBoard = Array.Empty<CardOnBoardCondition>();
        action.RequiredTagsOnBoard = Array.Empty<TagOnBoardCondition>();
        action.RequiredAlreadyVisitedEnvironments = Array.Empty<CardData>();
        action.RequiredReceivingContainer = Array.Empty<CardData>();
        action.RequiredReceivingContainerTag = Array.Empty<CardTag>();
        action.CompatibleNPCDuties = Array.Empty<NPCDutyOrDutyTagRef>();
        action.ProducedCards = Array.Empty<CardsDropCollection>();

        // "Full Trust": the piglet's Trust is its fuel bar; require nearly all of it.
        var conditions = default(DurabilityConditions);
        conditions.RequiredFuelPercent = new OptionalRangeValueWithLiquidScaling(true, TrainMinTrust, 1f, new OptionalFloatValue(false, 0f));
        conditions.HideActionIfNotMet = false;
        action.RequiredReceivingDurabilities = conditions;

        action.CompatibleCards = new CardInteractionTrigger { TriggerCards = new[] { acorns }, TriggerTags = Array.Empty<CardTag>() };
        action.NOTCompatibleCards = new CardInteractionTrigger { TriggerCards = Array.Empty<CardData>(), TriggerTags = Array.Empty<CardTag>() };
        action.WorksInverted = false;
        action.WorksBothWays = false;
        action.CarryOverGivenCard = false;
        action.GivenCardChanges = StateChange(CardModifications.Destroy, null);
        action.ReceivingCardChanges = StateChange(CardModifications.Transform, hog);
        action.CreatedLiquidInGivenCard = default;
        action.RequiredGivenDurabilities = default;
        action.RequiredGivenContainer = Array.Empty<CardData>();
        action.RequiredGivenContainerTag = Array.Empty<CardTag>();
        action.RequiredGivenContainerDurabilities = default;
        action.RequiredGivenLiquidContent = default;

        var grown = new List<CardOnCardAction>(existing) { action };
        piglet.CardInteractions = grown.ToArray();
        return 1;
    }

    private static CardStateChange StateChange(CardModifications modType, CardData transformInto)
    {
        var change = new CardStateChange
        {
            ModType = modType,
            AddedFlavours = Array.Empty<FlavourAndIntensitySetup>(),
            AddedSpiceTags = Array.Empty<SpiceTag>(),
            StatInterpolatedDurabilityChanges = Array.Empty<StatInterpolatedDurabilityModifier>(),
            DurabilityInterpolatedTransfers = Array.Empty<DurabilityInterpolatedDurabilityModifier>(),
        };
        change.TransformInto = transformInto;
        return change;
    }
}
