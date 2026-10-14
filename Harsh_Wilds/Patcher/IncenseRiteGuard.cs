namespace HarshWilds.Patcher;

/// <summary>
/// Load guard for the athame's incense rows (plan section 26, "Incense"). Each rite on
/// <c>CardData/Item/hw_athame.json</c> has four extra DismantleActions, one per lit incense card of a sibling
/// mod (Herbs and Fungi's bundle, Community Mod Chest's three burners). Each names that card ONLY in a
/// <c>RequiredCardsOnBoard</c> condition. With the sibling mod absent the condition's <c>TriggerCard</c> does
/// not resolve (null), and the game would test a null card against the board on every inspection of the
/// athame; this guard removes any such action once at load, so with neither mod installed no incense row has
/// any effect and nothing reads a null card.
///
/// <para>It runs as a postfix on <c>GameManager.InitializeStatsAndActions</c>, like
/// <see cref="BanditTheftGuard"/>: after the framework resolved every mod object and before any card of the
/// run exists. A Harmony postfix rather than an event handler, so a save reload re-runs it without stacking
/// anything; removal is idempotent (an action already gone is not seen again).</para>
/// </summary>
internal static class IncenseRiteGuard
{
    internal const string AthameUid = "hw_athame";

    /// <summary>Marks an incense action: its LocalizationKey holds one of these. The static gate pins that
    /// every action carrying a foreign card has one.</summary>
    internal static readonly string[] SmokeKeyParts = { "_Bundle_", "_Burner_", "_Heather_", "_Juniper_" };

    private static readonly HashSet<string> Warned = new(StringComparer.Ordinal);

    internal static void ApplyPatch(Harmony harmony)
    {
        var target = AccessTools.Method(typeof(GameManager), "InitializeStatsAndActions");
        if (target == null)
        {
            Plugin.Logger.LogWarning("IncenseRiteGuard: GameManager.InitializeStatsAndActions not found; the athame's incense rows could not be verified.");
            return;
        }
        harmony.Patch(target, postfix: new HarmonyMethod(typeof(IncenseRiteGuard), nameof(InitializeStatsAndActions_Postfix)));
    }

    private static void InitializeStatsAndActions_Postfix()
    {
        var athame = UniqueIDScriptable.GetFromID<CardData>(AthameUid);
        if (athame == null)
        {
            WarnOnce("missing", $"IncenseRiteGuard: '{AthameUid}' is not loaded; nothing to verify.");
            return;
        }
        var actions = athame.DismantleActions;
        if (actions == null)
        {
            WarnOnce("no-actions", $"IncenseRiteGuard: '{AthameUid}' has no DismantleActions list.");
            return;
        }
        int removed = 0;
        for (int i = actions.Count - 1; i >= 0; i--)
        {
            var action = actions[i];
            if (action == null || !IsSmoke(action)) continue;
            if (!HasUnresolvedCondition(action)) continue;
            actions.RemoveAt(i);
            removed++;
        }
        if (removed > 0)
            Plugin.Logger.LogDebug($"IncenseRiteGuard: removed {removed} incense rite action(s) whose incense card is not installed.");
    }

    private static bool IsSmoke(DismantleCardAction action)
    {
        string key = action.ActionName.LocalizationKey ?? string.Empty;
        foreach (var part in SmokeKeyParts)
            if (key.Contains(part)) return true;
        return false;
    }

    /// <summary>True when the action's incense condition names a card that did not resolve (or is gone).</summary>
    internal static bool HasUnresolvedCondition(CardAction action)
    {
        var conds = action.RequiredCardsOnBoard;
        // A smoke action always carries one non-inverted incense condition. If the loader dropped an
        // unresolved entry instead of leaving it null, the action would show with no incense gate at all,
        // so a missing positive condition counts as unresolved too.
        if (conds == null) return true;
        bool hasIncense = false;
        for (int i = 0; i < conds.Length; i++)
        {
            if (conds[i].TriggerCard == null) return true;
            if (!conds[i].Inverted) hasIncense = true;
        }
        return !hasIncense;
    }

    private static void WarnOnce(string cause, string message)
    {
        if (Warned.Add(cause)) Plugin.Logger.LogWarning(message);
    }
}
