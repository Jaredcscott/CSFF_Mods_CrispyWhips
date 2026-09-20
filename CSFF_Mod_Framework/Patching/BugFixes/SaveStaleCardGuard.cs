namespace CSFFModFramework.Patching.BugFixes;

/// <summary>
/// Drops broken entries from <c>GameManager.AllCards</c> immediately before the game writes a
/// save, so one stale card reference can no longer turn an autosave into a permanent
/// "I can't do two things at once..." lock.
///
/// <para><strong>The failure</strong> (EA 0.67i decompile; the method bodies are unchanged in
/// 0.68). <c>GameLoad.SaveGameByReference</c> (<c>.decomp/GameLoad.cs</c> lines 719-767) walks
/// <c>GM.AllCards</c>, where <c>GM</c> is <c>MBSingleton&lt;GameManager&gt;.Instance</c> (line 607),
/// and dereferences <c>GM.AllCards[l].CardModel.CardType</c> with no null check. The day, week,
/// season and per-tick autosaves run synchronously inside <c>GameManager.ActionRoutine</c>
/// (<c>.decomp/GameManager.cs</c> lines 5136-5180), which has no try/finally, so a throw there ends
/// the action before <c>RootAction = null</c> (line 5222). <c>GameManager.PerformingAction</c> is
/// <c>RootAction != null</c> (line 704), so every later click is refused until the game restarts.
/// Player report 2026-09-19 (Dory22), after a day passed in a modded village.</para>
///
/// <para><strong>How an entry goes stale.</strong> Vanilla only ever removes a card through
/// <c>GameManager.RemoveCard</c>, which takes it out of <c>AllCards</c> (line 9739) before calling
/// <c>InGameCardBase.DestroyCard</c> (line 9983). Code that calls DestroyCard directly skips that
/// step; <c>CardUtil.TryRemoveCard</c> did until framework 2.26.2, and any third-party mod still can.
/// The left-behind entry then passes through states that each corrupt the save differently:
/// <list type="number">
/// <item>Destroyed flag set, <c>CardModel</c> still set: the save can write the removed card back,
/// so it reappears on reload.</item>
/// <item>With card pooling on, <c>ResetCard</c> (<c>.decomp/InGameCardBase.cs</c> line 10095)
/// returns the object to the pool and sets <c>CardModel = null</c>: the save throws (the reported
/// freeze).</item>
/// <item>The pooled object is reused for a NEW card (<c>SetModel</c> clears the Destroyed flag):
/// the same live card is now listed twice and the save writes it twice, a duplicate on reload.</item>
/// <item>With pooling off, <c>Object.Destroy</c> leaves a destroyed (Unity-null) object listed.</item>
/// </list>
/// This prefix removes all four shapes, plus a plain null. None of them is a live card that the
/// save should have written: vanilla never lists a destroyed card, and the first listing of a
/// duplicated card is kept, so the card is still saved exactly once.</para>
///
/// <para>Deliberately NOT touched: an entry that is the current environment, hand, weather or event
/// card. The save loop skips those before reading <c>CardModel</c> (line 721) and saves them
/// separately (lines 696-710), and other game code relies on them being listed.</para>
///
/// <para>The prefix returns void, so it can never skip the save, and it never throws: any failure
/// is logged and the save runs on the list as it was.</para>
/// </summary>
internal static class SaveStaleCardGuard
{
    // Per-entry lines are capped so a pathological list cannot flood the log; the summary line
    // always carries the full count.
    private const int MaxEntryLogs = 10;

    // Reused across saves (the save runs on the main thread only), so a clean save allocates nothing.
    private static readonly HashSet<InGameCardBase> _seen = new(ReferenceComparer.Instance);
    private static readonly List<int> _pruneIndices = new();
    private static readonly List<string> _pruneReasons = new();

    public static void ApplyPatch(Harmony harmony)
    {
        try
        {
            var original = AccessTools.Method(typeof(GameLoad), nameof(GameLoad.SaveGameByReference));
            if (original == null)
            {
                Util.Log.Warn("SaveStaleCardGuard: GameLoad.SaveGameByReference not found; broken card entries will not be pruned before saving.");
                return;
            }

            var prefix = new HarmonyMethod(AccessTools.Method(typeof(SaveStaleCardGuard), nameof(Prefix)));
            harmony.Patch(original, prefix: prefix);
            Util.Log.Debug("SaveStaleCardGuard: patched GameLoad.SaveGameByReference.");
        }
        catch (Exception ex)
        {
            Util.Log.Warn($"SaveStaleCardGuard: patch setup failed: {Util.Log.ExceptionText(ex)}");
        }
    }

    // void, never bool: this prefix cannot skip the original save.
    private static void Prefix()
    {
        try
        {
            var gm = MBSingleton<GameManager>.Instance;
            if (gm == null) return;
            var allCards = gm.AllCards;
            if (allCards == null || allCards.Count == 0) return;

            int before = allCards.Count;
            int pruned = PruneStaleEntries(gm, allCards);
            if (pruned > 0)
                Util.Log.Warn($"SaveStaleCardGuard: removed {pruned} broken card entr{(pruned == 1 ? "y" : "ies")} "
                    + $"from GameManager.AllCards ({before} -> {allCards.Count}) before saving. Each is a card some code "
                    + "removed without the game's own RemoveCard; without this the save would have frozen every action "
                    + "(\"I can't do two things at once...\"), resurrected the card, or saved it twice.");
        }
        catch (Exception ex)
        {
            Util.Log.Warn($"SaveStaleCardGuard: prune failed; saving with GameManager.AllCards unchanged: {Util.Log.ExceptionText(ex)}");
        }
    }

    private static int PruneStaleEntries(GameManager gm, List<InGameCardBase> allCards)
    {
        _seen.Clear();
        _pruneIndices.Clear();
        _pruneReasons.Clear();

        // Classify front to back, so the FIRST listing of a duplicated card is the one kept.
        for (int i = 0; i < allCards.Count; i++)
        {
            var card = allCards[i];
            string reason;
            if ((object)card == null) reason = "null reference";
            else if (IsSpecialCard(gm, card)) continue;
            else if (card == null) reason = "destroyed object";   // Unity ==: the native object is gone
            else if ((object)card.CardModel == null) reason = "no CardModel (reset after removal)";
            else if (card.Destroyed) reason = "card already destroyed";
            else if (!_seen.Add(card)) reason = "listed twice";
            else continue;

            _pruneIndices.Add(i);
            _pruneReasons.Add(reason);
        }

        // Remove back to front so the recorded indices stay valid.
        for (int k = _pruneIndices.Count - 1; k >= 0; k--)
        {
            int index = _pruneIndices[k];
            if (k < MaxEntryLogs)
                Util.Log.Warn($"SaveStaleCardGuard: dropped GameManager.AllCards[{index}] before saving ({_pruneReasons[k]}): {Describe(allCards[index])}");
            allCards.RemoveAt(index);
        }

        int pruned = _pruneIndices.Count;
        _seen.Clear();
        return pruned;
    }

    private static bool IsSpecialCard(GameManager gm, InGameCardBase card)
        => ReferenceEquals(card, gm.CurrentEnvironmentCard)
        || ReferenceEquals(card, gm.CurrentHandCard)
        || ReferenceEquals(card, gm.CurrentWeatherCard)
        || ReferenceEquals(card, gm.CurrentEventCard);

    // Whatever identity is still readable: the UniqueID while CardModel survives, the GameObject
    // name while the native object does (DestroyCard appends " (CLEARED)"), and the instance id,
    // which is a managed field and survives both.
    private static string Describe(InGameCardBase card)
    {
        if ((object)card == null) return "null";
        try
        {
            var model = card.CardModel;
            string uid = (object)model != null ? model.UniqueID : null;
            string goName = card != null ? card.name : null;
            return $"uid={uid ?? "?"} object='{goName ?? "?"}' instanceId={card.GetInstanceID()}";
        }
        catch (Exception ex)
        {
            Util.Log.Debug($"SaveStaleCardGuard: could not describe a pruned entry: {ex.GetType().Name} {ex.Message}");
            return "unreadable";
        }
    }

    // Reference identity: two listings of one object must collide even if Unity's Equals
    // override would consult the native side.
    private sealed class ReferenceComparer : IEqualityComparer<InGameCardBase>
    {
        public static readonly ReferenceComparer Instance = new();
        public bool Equals(InGameCardBase x, InGameCardBase y) => ReferenceEquals(x, y);
        public int GetHashCode(InGameCardBase obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
