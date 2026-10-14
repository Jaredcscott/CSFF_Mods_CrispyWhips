namespace HarshWilds.Patcher;

/// <summary>
/// Fail-closed check on the one piece of Harsh Wilds that takes cards out of the player's hands: the
/// bandit's theft, <c>HarshWilds/SelfTriggeredAction/hw_tgr_bandit_theft.json</c>
/// (Documentation/Research/HarshWilds/bandits.md DECISION 4 and section 5, rows 4 to 8).
///
/// <para>The theft is one vanilla <c>ExtraDurabilityChange</c> that destroys up to
/// <see cref="TheftMax"/> cards from the hand, restricted to an allowlist of
/// <see cref="AllowlistSize"/> vanilla travel foods. The data is safe only while the allowlist
/// RESOLVED: <c>ExtraDurabilityChange.AppliesToAllCards</c> (<c>.decomp/ExtraDurabilityChange.cs</c>)
/// reads an empty <c>AffectedCards</c> with no <c>AffectedTags</c> as "every card", so an allowlist
/// whose entries all failed to resolve would turn a three-food theft into taking any three cards the
/// player holds: tools, quest items, the knife a recipe chain needs. The static gate
/// (<c>Development_Tools/Tests/HarshWilds-Bandits.Tests.ps1</c>) pins the JSON; this guard pins what
/// the game actually loaded. If anything about the resolved change differs from the contract, the
/// change is REMOVED from the action, so a lost fight still clears its marker
/// (<c>hw_bandit_robbed</c>, the action's own <c>StatModifications</c>) and takes nothing, and a
/// warning names the cause once.</para>
///
/// <para>It runs as a postfix on <c>GameManager.InitializeStatsAndActions</c>, like
/// <see cref="Switchboard"/>: after the framework has loaded and resolved every mod object (its
/// <c>LoadMainGameData</c> postfix, before the main menu) and before any self-triggered action of the
/// run can fire, including one whose marker a save recorded at 1. A Harmony postfix, not an event
/// handler, so a save reload re-runs the check without stacking anything; once disarmed the change
/// stays removed for the rest of the process, since the object is loaded once.</para>
/// </summary>
internal static class BanditTheftGuard
{
    internal const string TheftUid = "hw_tgr_bandit_theft";

    /// <summary>HW_BANDIT_THEFT_MAX: the most cards one lost fight can cost. Must equal the JSON's
    /// <c>RandomlyAffectedCards</c> (Absolute); the static gate checks the two agree.</summary>
    internal const int TheftMax = 3;

    /// <summary>Entries in the theft's <c>AffectedCardsWarpData</c> allowlist; the static gate checks
    /// the JSON list has exactly this many.</summary>
    internal const int AllowlistSize = 7;

    private static readonly HashSet<string> Warned = new(StringComparer.Ordinal);

    internal static void ApplyPatch(Harmony harmony)
    {
        var target = AccessTools.Method(typeof(GameManager), "InitializeStatsAndActions");
        if (target == null)
        {
            Plugin.Logger.LogWarning("BanditTheftGuard: GameManager.InitializeStatsAndActions not found; the bandit theft could not be verified.");
            return;
        }
        harmony.Patch(target, postfix: new HarmonyMethod(typeof(BanditTheftGuard), nameof(InitializeStatsAndActions_Postfix)));
    }

    private static void InitializeStatsAndActions_Postfix()
    {
        var sta = UniqueIDScriptable.GetFromID<SelfTriggeredAction>(TheftUid);
        if (sta == null)
        {
            // Nothing to disarm: with the object absent no theft can run at all.
            WarnOnce("missing", $"BanditTheftGuard: '{TheftUid}' is not loaded; a lost bandit fight takes nothing.");
            return;
        }
        if (sta.Actions == null || sta.Actions.Length == 0)
        {
            WarnOnce("no-actions", $"BanditTheftGuard: '{TheftUid}' has no actions; a lost bandit fight takes nothing.");
            return;
        }

        for (int i = 0; i < sta.Actions.Length; i++)
        {
            var action = sta.Actions[i];
            if (action == null) continue;
            var changes = action.ExtraDurabilityModifications;
            if (changes == null || changes.Length == 0) continue; // already disarmed, or never armed

            string fault = sta.Actions.Length != 1 ? $"{sta.Actions.Length} actions, expected 1" : Fault(changes);
            if (fault == null) continue;

            action.ExtraDurabilityModifications = Array.Empty<ExtraDurabilityChange>();
            WarnOnce(fault, $"BanditTheftGuard: '{TheftUid}' action {i} does not match its safety contract ({fault}); " +
                            "the theft is disabled for this session and a lost bandit fight takes nothing.");
        }
    }

    /// <summary>The first way the loaded change breaks the contract, or null when it holds.</summary>
    internal static string Fault(ExtraDurabilityChange[] changes)
    {
        if (changes.Length != 1) return $"{changes.Length} changes, expected 1";
        var c = changes[0];
        if (c == null) return "the change is null";
        if (c.AppliesTo != RemoteDurabilityChanges.AllCardsInHand) return $"AppliesTo {c.AppliesTo}, expected AllCardsInHand";
        if (c.IncludeEquippedCards) return "IncludeEquippedCards is true";
        if (c.CardChanges != RemoteCardStateChanges.Destroy) return $"CardChanges {c.CardChanges}, expected Destroy";
        if (c.RandomAmountType != RandomAffectCardsTypes.Absolute) return $"RandomAmountType {c.RandomAmountType}, expected Absolute";
        if (c.RandomlyAffectedCards < 1f || c.RandomlyAffectedCards > TheftMax)
            return $"RandomlyAffectedCards {c.RandomlyAffectedCards}, expected 1 to {TheftMax}";
        if (c.AffectedTags != null && c.AffectedTags.Length > 0) return $"{c.AffectedTags.Length} AffectedTags, expected none";
        if (c.AffectedCards == null) return "the allowlist is null";
        if (c.AffectedCards.Count != AllowlistSize) return $"the allowlist resolved {c.AffectedCards.Count} cards, expected {AllowlistSize}";

        foreach (var card in c.AffectedCards)
        {
            if (card == null) return "an allowlist entry did not resolve";
            string name = card.name;
            if (card.CardType != CardTypes.Item) return $"allowlist card {name} is {card.CardType}, expected Item";
            if (card.EquipmentTags != null && card.EquipmentTags.Length > 0) return $"allowlist card {name} can be equipped";
            if (card.InHandWhenEquipped) return $"allowlist card {name} is InHandWhenEquipped";
            if (card.MaxWeightCapacity > 0f || card.MaxLiquidCapacity > 0f || card.IsMiniInventory
                || (card.InventorySlots != null && card.InventorySlots.Length > 0))
                return $"allowlist card {name} is a container";
        }
        return null;
    }

    private static void WarnOnce(string cause, string message)
    {
        if (Warned.Add(cause)) Plugin.Logger.LogWarning(message);
    }
}
