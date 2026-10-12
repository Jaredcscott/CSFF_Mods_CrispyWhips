namespace Utility_Belt;

/// <summary>
/// The item-lock seam other mods call by reflection (plan B1 step 2): Quick Transfer's and Repeat Action's
/// bridges resolve <c>Utility_Belt.LockApi.IsLocked(object)</c> from the loaded Utility Belt assembly and skip
/// a card it reports locked. It never throws and logs once per cause on a failed read.
/// </summary>
public static class LockApi
{
    private static readonly HashSet<string> _warned = new();

    /// <summary>
    /// True when <paramref name="card"/> is a live in-game card (InGameCardBase) whose (CardID, CreatedOnTick)
    /// key is locked in the current save slot. False for anything else, including null, a destroyed card, a
    /// card with no model, or before a run has started.
    /// </summary>
    public static bool IsLocked(object card)
    {
        try
        {
            if (card == null)
            {
                return false;
            }

            if (card is not InGameCardBase c)
            {
                WarnOnce("not-a-card:" + card.GetType().FullName, $"[UB] LockApi.IsLocked was handed a {card.GetType().FullName}, not an InGameCardBase; it reads as unlocked.");
                return false;
            }

            if (c == null || c.CardModel == null)
            {
                return false; // destroyed or model-less: no lock to honour
            }

            return Locks.LockStore.Contains(c.CardModel.UniqueID, c.CreatedOnTick);
        }
        catch (Exception ex)
        {
            WarnOnce("throw:" + ex.GetType().Name, $"[UB] LockApi.IsLocked failed ({ex.GetType().Name}: {ex.Message}); the card reads as unlocked.");
            return false;
        }
    }

    private static void WarnOnce(string cause, string message)
    {
        if (_warned.Add(cause))
        {
            Plugin.Logger?.LogWarning(message);
        }
    }
}
