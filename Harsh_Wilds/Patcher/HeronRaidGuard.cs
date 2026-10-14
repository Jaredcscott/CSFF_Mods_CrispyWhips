namespace HarshWilds.Patcher;

/// <summary>
/// Fail-closed check on the heron's one button, <c>HarshWilds/CardData/Event/hw_event_heron.json</c>
/// (Documentation/Design/Harsh_Wilds_Content_Update_As_Built.md section 8), in the shape of
/// <see cref="BanditTheftGuard"/>.
///
/// <para>The button lowers the Minnow and Perch of one Water-Driven Infrastructure fishpond through a
/// vanilla <c>ExtraDurabilityChange</c> whose target list names the two pond UniqueIDs. That list is
/// safe only while it RESOLVED: <c>ExtraDurabilityChange.AppliesToAllCards</c>
/// (<c>.decomp/ExtraDurabilityChange.cs</c>) reads an empty <c>AffectedCards</c> with no
/// <c>AffectedTags</c> as "every card", so with Water-Driven Infrastructure NOT installed (a normal
/// install: the list resolves to nothing) the change would reach every card in the location and
/// lower the fish stats of whichever of them carries those stats. The static gate
/// (<c>Development_Tools/Tests/HarshWilds-Heron.Tests.ps1</c>) pins the JSON; this guard pins what the
/// game actually loaded. If the resolved change is anything but the contract, the change is REMOVED
/// from the action, so the button still clears its card and takes nothing, and one Info line names the
/// cause once. Without Water-Driven Infrastructure the roll's two weight rows also match no card, so
/// the heron never comes at all; the guard covers a card given by a cheat or another mod.</para>
///
/// <para>It runs as a postfix on <c>GameManager.InitializeStatsAndActions</c>, after the framework has
/// loaded and resolved every mod object and before any self-triggered action of the run can fire. A
/// Harmony postfix, not an event handler, so a save reload re-runs the check without stacking
/// anything; once disarmed the change stays removed for the rest of the process.</para>
/// </summary>
internal static class HeronRaidGuard
{
    internal const string EventUid = "hw_event_heron";

    /// <summary>The only cards the heron may change: Water-Driven Infrastructure's filled and stocked
    /// fishponds. The winter pond regrows nothing and is left out on purpose.</summary>
    internal static readonly string[] PondUids =
    {
        "water_sawmill_fishpond_filled",
        "water_sawmill_fishpond_stocked",
    };

    private static readonly HashSet<string> Logged = new(StringComparer.Ordinal);

    internal static void ApplyPatch(Harmony harmony)
    {
        var target = AccessTools.Method(typeof(GameManager), "InitializeStatsAndActions");
        if (target == null)
        {
            Plugin.Logger.LogWarning("HeronRaidGuard: GameManager.InitializeStatsAndActions not found; the heron's take could not be verified.");
            return;
        }
        harmony.Patch(target, postfix: new HarmonyMethod(typeof(HeronRaidGuard), nameof(InitializeStatsAndActions_Postfix)));
    }

    private static void InitializeStatsAndActions_Postfix()
    {
        var card = UniqueIDScriptable.GetFromID<CardData>(EventUid);
        if (card == null)
        {
            // With the card absent no heron button can run at all.
            LogOnce("missing", $"HeronRaidGuard: '{EventUid}' is not loaded; the heron takes nothing.");
            return;
        }
        var actions = card.DismantleActions;
        if (actions == null || actions.Count == 0)
        {
            LogOnce("no-actions", $"HeronRaidGuard: '{EventUid}' has no buttons; the heron takes nothing.");
            return;
        }

        for (int i = 0; i < actions.Count; i++)
        {
            var action = actions[i];
            if (action == null) continue;
            var changes = action.ExtraDurabilityModifications;
            if (changes == null || changes.Length == 0) continue; // already disarmed, or never armed

            string fault = actions.Count != 1 ? $"{actions.Count} buttons, expected 1" : Fault(changes);
            if (fault == null) continue;

            action.ExtraDurabilityModifications = Array.Empty<ExtraDurabilityChange>();
            string note = fault.StartsWith("the target list resolved 0 ", StringComparison.Ordinal)
                ? " (Water-Driven Infrastructure is not installed, which is a normal install)"
                : string.Empty;
            LogOnce(fault, $"HeronRaidGuard: '{EventUid}' button {i} does not match its contract ({fault}){note}; " +
                           "the heron's take is disabled for this session and the button takes nothing.");
        }
    }

    /// <summary>The first way the loaded change breaks the contract, or null when it holds.</summary>
    internal static string Fault(ExtraDurabilityChange[] changes)
    {
        if (changes.Length != 1) return $"{changes.Length} changes, expected 1";
        var c = changes[0];
        if (c == null) return "the change is null";
        if (c.AffectedCards == null || c.AffectedCards.Count == 0)
            return "the target list resolved 0 of " + PondUids.Length + " ponds";
        if (c.AffectedCards.Count != PondUids.Length)
            return $"the target list resolved {c.AffectedCards.Count} of {PondUids.Length} ponds";
        foreach (var uid in PondUids)
        {
            bool found = false;
            foreach (var card in c.AffectedCards)
            {
                if (card != null && string.Equals(card.UniqueID, uid, StringComparison.Ordinal)) { found = true; break; }
            }
            if (!found) return $"the target list lacks {uid}";
        }
        foreach (var card in c.AffectedCards)
        {
            if (card == null) return "a target entry did not resolve";
            if (card.CardType != CardTypes.Location) return $"target card {card.name} is {card.CardType}, expected Location";
        }
        if (c.AffectedTags != null && c.AffectedTags.Length > 0) return $"{c.AffectedTags.Length} AffectedTags, expected none";
        if (c.AppliesTo != RemoteDurabilityChanges.AllCardsInLocation) return $"AppliesTo {c.AppliesTo}, expected AllCardsInLocation";
        if (c.LookInInventories) return "LookInInventories is true";
        if (c.IncludeEquippedCards) return "IncludeEquippedCards is true";
        if (c.CardChanges != RemoteCardStateChanges.ChangeDurabilities) return $"CardChanges {c.CardChanges}, expected ChangeDurabilities";
        if (c.RandomAmountType != RandomAffectCardsTypes.Absolute) return $"RandomAmountType {c.RandomAmountType}, expected Absolute";
        if (c.RandomlyAffectedCards < 1f || c.RandomlyAffectedCards > 1f) return $"RandomlyAffectedCards {c.RandomlyAffectedCards}, expected 1";
        if (!IsZero(c.SpoilageChange) || !IsZero(c.UsageChange) || !IsZero(c.FuelChange) || !IsZero(c.ChargesChange)
            || !IsZero(c.LiquidChange) || !IsZero(c.Special1Change) || !IsZero(c.Special4Change))
            return "a stat other than Special2 and Special3 changes";
        if (c.Special2Change.x > 0f || c.Special2Change.y > 0f || c.Special3Change.x > 0f || c.Special3Change.y > 0f)
            return "Special2 or Special3 would rise";
        return null;
    }

    private static bool IsZero(UnityEngine.Vector2 v) => v.x == 0f && v.y == 0f;

    private static void LogOnce(string cause, string message)
    {
        if (Logged.Add(cause)) Plugin.Logger.LogInfo(message);
    }
}
