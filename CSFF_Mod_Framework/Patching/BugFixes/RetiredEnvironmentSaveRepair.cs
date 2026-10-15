namespace CSFFModFramework.Patching.BugFixes;

/// <summary>
/// Lets a save that holds boards of mod environments that no longer exist load them into a real one
/// without losing a board. A mod that removes a location aliases the removed environment UID to the vanilla
/// (or other) environment the player should land in, in its <c>UidAliases.json</c>:
/// <code>{ "actTinCaveEnv": "17d6cfe8ff886b643afee74dc18760a7" }</code>
///
/// <para><strong>Why the alias alone is not enough.</strong> The alias
/// (<see cref="Loading.UidAliasService"/>) makes the removed UID resolve, so the save loads (the
/// <c>GameSaveDataByReference.HasCardsData</c> test on <c>CurrentEnvironmentCard.CardID</c> passes).
/// But every saved board of a removed environment now resolves to its target too, so two or more
/// boards share one key: <c>GameManager.LoadCards</c> (<c>.decomp/GameManager.cs</c>) keeps the first
/// and logs "is already registered as an environment" for the rest, or, when the duplicate is the
/// current environment, overwrites the real board with the removed one. Measured 2026-10-15 on a
/// rig with framework 2.28.4 and H&amp;F 1.14.9: the vanilla Mossy Clearing board was replaced by the
/// removed Oak Clearing's, and four "already registered" errors were logged. Saves made inside a
/// removed location still loaded with their cards there (the current board travels in
/// <c>CurrentCardsData</c>), so "loads as a fresh board" is NOT the failure; lost boards are.</para>
///
/// <para><strong>What this prefix does</strong>, before <c>LoadCards</c> reads the save: every saved
/// board whose key names a removed environment is folded into the target's board (its cards are
/// appended, nested-inventory indices offset to match) or, when the save holds no board for the
/// target, renamed to it. The current and previous environment keys are rewritten the same way.
/// Travel cards from the removed board are dropped, since they lead to places that no longer exist;
/// the target's own location and travel cards come back from its defaults when the game checks for
/// missing default cards at the end of loading. Cards whose own UID is gone (the removed
/// location's veins, walls and so on) are dropped by the game's own <c>LoadCard</c> as usual.</para>
///
/// <para>Only plain (non-instanced) environment keys are touched, and only UIDs that are no longer
/// live cards and whose alias target is a <c>CardTypes.Environment</c> card. Never throws: any
/// failure is logged and the game loads the save as it was.</para>
/// </summary>
internal static class RetiredEnvironmentSaveRepair
{
    private static FieldInfo _currentGameData;
    private static FieldInfo _allRegularCards;
    private static FieldInfo _allInventoryCards;

    public static void ApplyPatch(Harmony harmony)
    {
        try
        {
            var original = AccessTools.Method(typeof(GameManager), "LoadCards");
            _currentGameData = AccessTools.Field(typeof(GameManager), "CurrentGameData");
            _allRegularCards = AccessTools.Field(typeof(EnvironmentSaveDataByReference), "AllRegularCards");
            _allInventoryCards = AccessTools.Field(typeof(EnvironmentSaveDataByReference), "AllInventoryCards");
            if (original == null || _currentGameData == null || _allRegularCards == null || _allInventoryCards == null)
            {
                Util.Log.Warn("RetiredEnvironmentSaveRepair: GameManager.LoadCards or a save field was not found; saves made inside a removed mod location will not be repaired.");
                return;
            }
            harmony.Patch(original, prefix: new HarmonyMethod(AccessTools.Method(typeof(RetiredEnvironmentSaveRepair), nameof(Prefix))));
            Util.Log.Debug("RetiredEnvironmentSaveRepair: patched GameManager.LoadCards.");
        }
        catch (Exception ex)
        {
            Util.Log.Warn($"RetiredEnvironmentSaveRepair: patch setup failed: {Util.Log.ExceptionText(ex)}");
        }
    }

    /// <summary>The target environment UID when <paramref name="uid"/> is a removed environment aliased to a live one, else null.</summary>
    internal static string RetiredTarget(string uid)
    {
        if (string.IsNullOrEmpty(uid) || Loading.UidAliasService.Resolve(uid) == null) return null;
        // After UidAliasService.RegisterInGameRegistry the old UID resolves to the target object; a
        // live card keeps its own UID and is never treated as removed.
        if (UniqueIDScriptable.GetFromID(uid) is not CardData card || card.UniqueID == uid) return null;
        return card.CardType == CardTypes.Environment ? card.UniqueID : null;
    }

    // Main UID of a plain environment key ("uid" or "uid(Name)"); null for an instanced key, whose
    // parent chain is joined with '_'.
    private static string PlainKeyUid(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        var uid = UniqueIDScriptable.LoadID(key);
        return string.IsNullOrEmpty(uid) || uid.IndexOf('_') >= 0 ? null : uid;
    }

    private static void Prefix(GameManager __instance)
    {
        try
        {
            if (Loading.UidAliasService.Count == 0) return;
            if (_currentGameData.GetValue(__instance) is not GameSaveDataByReference data || data.EnvironmentsData == null) return;

            int folded = 0, renamed = 0, keys = 0;
            var names = new List<string>();

            string target;
            if ((target = RetiredTarget(PlainKeyUid(data.CurrentEnvironmentKey))) != null) { names.Add(PlainKeyUid(data.CurrentEnvironmentKey)); data.CurrentEnvironmentKey = target; keys++; }
            if ((target = RetiredTarget(PlainKeyUid(data.PrevEnvironmentKey))) != null) { data.PrevEnvironmentKey = target; keys++; }
            if (data.CurrentEnvironmentCard != null && (target = RetiredTarget(UniqueIDScriptable.LoadID(data.CurrentEnvironmentCard.CardID))) != null) { data.CurrentEnvironmentCard.CardID = target; keys++; }

            // The board the player saved on travels in CurrentCardsData/CurrentInventoryCards, not in
            // EnvironmentsData, so its removed terrain needs the same filter (measured 2026-10-15: a
            // save made inside hfEnvForagingPath kept its two __envlocal oaks beside Secret Grove's own).
            int currentDropped = 0;
            if (data.CurrentCardsData != null)
                currentDropped += data.CurrentCardsData.RemoveAll(c => c != null && OnRetiredBoard(c) && IsRemovedTerrain(c));
            if (data.CurrentInventoryCards != null)
                currentDropped += data.CurrentInventoryCards.RemoveAll(c => c != null && OnRetiredBoard(c) && IsRemovedTerrain(c) && !c.IsTravelCard);

            var list = data.EnvironmentsData;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var retired = list[i];
                var oldUid = PlainKeyUid(retired?.DictionaryKey);
                if ((target = RetiredTarget(oldUid)) == null) continue;
                if (!names.Contains(oldUid)) names.Add(oldUid);
                DropTravelCards(retired);

                var into = list.FirstOrDefault(e => e != null && !ReferenceEquals(e, retired) && PlainKeyUid(e.DictionaryKey) == target);
                if (into == null)
                {
                    retired.DictionaryKey = target;
                    renamed++;
                    continue;
                }
                Fold(retired, into);
                list.RemoveAt(i);
                folded++;
            }

            if (folded + renamed + keys + currentDropped > 0)
                Util.Log.Info($"RetiredEnvironmentSaveRepair: moved this save out of removed location(s) {string.Join(", ", names)}: {folded} board(s) merged, {renamed} renamed, {keys} key(s) rewritten, {currentDropped} removed-terrain card(s) dropped from the current board");
        }
        catch (Exception ex)
        {
            Util.Log.Warn($"RetiredEnvironmentSaveRepair: repair failed, loading the save as it was: {Util.Log.ExceptionText(ex)}");
        }
    }

    // A removed clone's own terrain: travel cards to places that no longer exist, and the
    // "<uid>__envlocal" copies the clone seeded in place of the source's defaults (trees, river).
    // The target's own defaults come back at the end of loading, so keeping the copies doubles
    // them (measured 2026-10-15: two Oak Trees and two Small Oak Trees on Secret Grove).
    // A card saved on a removed environment's board (its EnvironmentKey names an aliased removed env).
    private static bool OnRetiredBoard(CardSaveDataByReference c) =>
        RetiredTarget(PlainKeyUid(c.EnvironmentKey)) != null;

    private static bool IsRemovedTerrain(CardSaveDataByReference c) =>
        c == null || c.IsTravelCard
        || (c.CardID != null && UniqueIDScriptable.LoadID(c.CardID).EndsWith(Injection.StripAllContaminationCheck.EnvLocalSuffix, StringComparison.Ordinal));

    private static void DropTravelCards(EnvironmentSaveDataByReference env)
    {
        if (_allRegularCards.GetValue(env) is List<CardSaveDataByReference> regular)
            regular.RemoveAll(IsRemovedTerrain);
        if (_allInventoryCards.GetValue(env) is List<InventoryCardSaveDataByReference> inventory)
            inventory.RemoveAll(c => c != null && IsRemovedTerrain(c) && !c.IsTravelCard);
        env.AllPinnedCards?.Clear();
    }

    private static void Fold(EnvironmentSaveDataByReference from, EnvironmentSaveDataByReference into)
    {
        var intoRegular = _allRegularCards.GetValue(into) as List<CardSaveDataByReference>;
        var intoInventory = _allInventoryCards.GetValue(into) as List<InventoryCardSaveDataByReference>;
        if (intoRegular == null) _allRegularCards.SetValue(into, intoRegular = new List<CardSaveDataByReference>());
        if (intoInventory == null) _allInventoryCards.SetValue(into, intoInventory = new List<InventoryCardSaveDataByReference>());
        into.NestedInventoryCards ??= new List<InventoryCardSaveDataByReference>();

        // A slot points into NestedInventoryCards by index, so the moved cards' indices shift by the
        // length of the list they are appended to.
        int offset = into.NestedInventoryCards.Count;
        var fromInventory = _allInventoryCards.GetValue(from) as List<InventoryCardSaveDataByReference>;
        var fromNested = from.NestedInventoryCards;
        if (offset > 0)
        {
            ShiftSlots(fromInventory, offset);
            ShiftSlots(fromNested, offset);
        }

        if (_allRegularCards.GetValue(from) is List<CardSaveDataByReference> fromRegular) intoRegular.AddRange(fromRegular);
        if (fromInventory != null) intoInventory.AddRange(fromInventory);
        if (fromNested != null) into.NestedInventoryCards.AddRange(fromNested);
    }

    private static void ShiftSlots(List<InventoryCardSaveDataByReference> cards, int offset)
    {
        if (cards == null) return;
        foreach (var card in cards)
        {
            if (card?.CardsInInventoryRef == null) continue;
            foreach (var slot in card.CardsInInventoryRef)
                if (slot != null && slot.InventoryCardIndex >= 0) slot.InventoryCardIndex += offset;
        }
    }
}
