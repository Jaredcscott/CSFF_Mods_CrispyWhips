using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CSFFModFramework.Injection;

/// <summary>
/// Gives a mod perk's starting items (<c>AddedCardsWarpData</c> and <c>EquippedCardsWarpData</c>)
/// the durability values the perk declares, instead of the card's defaults (framework 2.28.0).
///
/// <para><strong>Why it is needed.</strong> Vanilla spawns a perk's <c>AddedCards</c> through
/// <c>GameManager.GetStartingCardsFromArray(_FromPerk: true)</c> and its <c>EquippedCards</c> from a
/// loop in <c>InitializePlayerCharacter</c>; both call <c>AddCard</c> with a null
/// <c>TransferedDurabilities</c>, so each card starts at its own defaults. For most vanilla
/// materials that means quality 0 and type ID 0: Clay, Basket, Greenstone, Rustic Fishing Line,
/// Large Cloth and Small Leather all default to quality 0 (Clay and Greenstone plus a random offset),
/// a Plank's wood ID is 0 (no such plank; oak is 1), and a Small Leather's Skin Type is 0, below the
/// 200 every leather blueprint requires. Vanilla's own perks never hit this because they give
/// dedicated carrier cards (<c>Pk_5_1_8_SmithingGear_CopperNugget</c>: Metal Quality 50, Metal Type
/// 100) that transform into the normal card on spawn, carrying those values. Player report
/// (Chiwei, 2026-09-27): every item a vanilla perk carries starts at quality 50 or better, and the
/// mod perks handed out unusable ones.</para>
///
/// <para><strong>Authoring.</strong> In the perk's JSON, beside the array it describes:
/// <code>
/// "AddedCardsStats": [
///   { "Card": "57460207bbf77fa4fb6720aed5d84851", "SpecialDurability4": 1 },
///   { "Card": "669497ecaa30b634c86bea6b61f31e63", "SpecialDurability2": 50, "SpecialDurability4": 200 }
/// ],
/// "EquippedCardsStats": [ { "Card": "&lt;uid&gt;", "SpecialDurability2": 50 } ]
/// </code>
/// <c>Card</c> is the item's UniqueID (a vanilla GUID or a mod UID) exactly as it appears in the
/// matching <c>...WarpData</c> list; every copy of that card the perk gives gets the values. The
/// other keys are JSON-side stat names (<see cref="SpawnService.StatNames"/>). The keys are
/// deliberately not named <c>...WarpData</c>, so <c>WarpResolver</c> never walks them. Load-time
/// warnings name every entry that cannot apply: a card the perk does not give in that list, a stat
/// the card does not have active, a value outside the stat's range, or a card type this path does
/// not spawn.</para>
///
/// <para><strong>Runtime, AddedCards.</strong> A prefix on <c>GetStartingCardsFromArray</c>
/// recognises the perk by its <c>AddedCards</c> array reference, spawns each declared item itself
/// through <see cref="SpawnService.SpawnWithStats"/> (the same private <c>AddCard</c> call, with the
/// values in a <c>TransferedDurabilities</c>), and hands the original the array without those items.
/// An item whose spawn could not be started stays in the array, so vanilla still gives it at its
/// defaults.</para>
///
/// <para><strong>Runtime, EquippedCards.</strong> <c>InitializePlayerCharacter</c> is a plain method,
/// so a prefix takes the declared items out of each held perk's <c>EquippedCards</c> (only once the
/// equipment overload has resolved, so nothing is taken that cannot be given back), a postfix equips
/// them through <see cref="SpawnService.SpawnEquippedWithStats"/>, after vanilla has equipped the
/// starting clothes exactly as before, and a finalizer ALWAYS puts the perk's own array back, because
/// the perk is a shared asset that the next run reads again. Blueprint-only passes (save loads) are
/// left alone on both paths. On the AddedCards path declared items are spawned before the perk's other
/// cards rather than interleaved with them, which changes only board order. On the EquippedCards path
/// the order matters (a container listed before its contents must exist first), so the postfix takes
/// every card from the first declared one on and equips them in the perk's own order.</para>
/// </summary>
internal static class PerkStartingItemStats
{
    internal const string AddedKey = "AddedCardsStats";
    internal const string EquippedKey = "EquippedCardsStats";
    private const string CardKey = "Card";

    private sealed class Entry
    {
        public string PerkUid;
        public CharacterPerk Perk;
        public Dictionary<string, Dictionary<string, float>> Added;
        public Dictionary<string, Dictionary<string, float>> Equipped;
    }

    private static readonly List<Entry> _entries = new();

    // One InitializePlayerCharacter call's state, set by EquipPrefix and cleared by EquipFinalizer.
    private static readonly List<KeyValuePair<CharacterPerk, CardData[]>> _swapped = new();
    private static readonly List<(string perkUid, CardData card, Dictionary<string, float> stats)> _pendingEquip = new();

    internal static int PerkCount => _entries.Count;

    public static void LoadAll(IEnumerable allData)
    {
        _entries.Clear();
        if (allData == null) return;

        Dictionary<string, CharacterPerk> perks = null;
        int items = 0;
        foreach (var uid in Loading.JsonDataLoader.AllModUniqueIds)
        {
            if (!Loading.JsonDataLoader.ParsedJsonByUniqueId.TryGetValue(uid, out var parsed) || parsed == null) continue;
            bool hasAdded = parsed.TryGetValue(AddedKey, out var rawAdded) && rawAdded != null;
            bool hasEquipped = parsed.TryGetValue(EquippedKey, out var rawEquipped) && rawEquipped != null;
            if (!hasAdded && !hasEquipped) continue;

            if (perks == null)
            {
                perks = new Dictionary<string, CharacterPerk>(StringComparer.Ordinal);
                foreach (var obj in allData)
                    if (obj is CharacterPerk p && !string.IsNullOrEmpty(p.UniqueID))
                        perks[p.UniqueID] = p;
            }

            if (!perks.TryGetValue(uid, out var perk))
            {
                Log.Warn($"PerkStartingItemStats: '{uid}' carries {AddedKey}/{EquippedKey} but is not a CharacterPerk; ignored.");
                continue;
            }

            var entry = new Entry { PerkUid = uid, Perk = perk };
            if (hasAdded) entry.Added = ParseList(uid, AddedKey, rawAdded, perk.AddedCards, ref items);
            if (hasEquipped) entry.Equipped = ParseList(uid, EquippedKey, rawEquipped, perk.EquippedCards, ref items);
            if (entry.Added != null || entry.Equipped != null)
                _entries.Add(entry);
        }

        if (_entries.Count > 0)
            Log.Debug($"PerkStartingItemStats: {_entries.Count} perk(s) declare stats for {items} starting item(s).");
    }

    private static Dictionary<string, Dictionary<string, float>> ParseList(
        string perkUid, string key, object raw, CardData[] pool, ref int items)
    {
        if (raw is not IList list)
        {
            Log.Warn($"PerkStartingItemStats: perk '{perkUid}': {key} must be an array of objects; ignored.");
            return null;
        }

        var byCard = new Dictionary<string, Dictionary<string, float>>(StringComparer.Ordinal);
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] is not Dictionary<string, object> element)
            {
                Log.Warn($"PerkStartingItemStats: perk '{perkUid}' {key}[{i}] is not an object; skipped.");
                continue;
            }
            var parsedEntry = ParseEntry($"perk '{perkUid}' {key}[{i}]", key, pool, element);
            if (parsedEntry.cardUid == null) continue;
            byCard[parsedEntry.cardUid] = parsedEntry.stats;
            items++;
        }
        return byCard.Count > 0 ? byCard : null;
    }

    private static (string cardUid, Dictionary<string, float> stats) ParseEntry(
        string where, string key, CardData[] pool, Dictionary<string, object> element)
    {
        if (!element.TryGetValue(CardKey, out var cardRaw) || cardRaw is not string cardUid || cardUid.Length == 0)
        {
            Log.Warn($"PerkStartingItemStats: {where} has no \"{CardKey}\" UniqueID; skipped.");
            return (null, null);
        }

        CardData card = null;
        if (pool != null)
            foreach (var c in pool)
                if (c != null && string.Equals(c.UniqueID, cardUid, StringComparison.Ordinal)) { card = c; break; }
        if (card == null)
        {
            string array = key == AddedKey ? "AddedCards" : "EquippedCards";
            Log.Warn($"PerkStartingItemStats: {where}: '{cardUid}' is not among the perk's {array}; skipped.");
            return (null, null);
        }
        if (!IsSpawnedHere(card))
        {
            Log.Warn($"PerkStartingItemStats: {where}: '{cardUid}' is a {card.CardType} card; only Item cards take declared stats; skipped.");
            return (null, null);
        }

        var stats = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var kvp in element)
        {
            if (kvp.Key == CardKey) continue;
            if (Array.IndexOf(SpawnService.StatNames, kvp.Key) < 0)
            {
                Log.Warn($"PerkStartingItemStats: {where}: unknown stat '{kvp.Key}' ignored (expected one of {string.Join(", ", SpawnService.StatNames)}).");
                continue;
            }
            if (!TryNumber(kvp.Value, out float value))
            {
                Log.Warn($"PerkStartingItemStats: {where}: '{kvp.Key}' is not a number; ignored.");
                continue;
            }
            var stat = StatOf(card, kvp.Key);
            if (stat == null || !(bool)stat)
            {
                Log.Warn($"PerkStartingItemStats: {where}: '{cardUid}' has no active {kvp.Key}; the value would be ignored, so it is dropped.");
                continue;
            }
            if (value < 0f || value > stat.Max)
                Log.Warn($"PerkStartingItemStats: {where}: {kvp.Key} {value} is outside '{cardUid}''s range 0-{stat.Max}; the game clamps it.");
            stats[kvp.Key] = value;
        }

        if (stats.Count == 0)
        {
            Log.Warn($"PerkStartingItemStats: {where}: no usable stat for '{cardUid}'; skipped.");
            return (null, null);
        }
        return (cardUid, stats);
    }

    // Items only. Blueprints and improvements are unlocked rather than spawned, a Location (CT2) perk
    // card can be deferred to a perk spawning environment (PerkLocationCardsToSpawn), and a liquid's
    // quantity would be zeroed by this path.
    private static bool IsSpawnedHere(CardData card) => card.CardType == CardTypes.Item;

    private static DurabilityStat StatOf(CardData card, string name) => name switch
    {
        "SpoilageTime" => card.SpoilageTime,
        "UsageDurability" => card.UsageDurability,
        "FuelCapacity" => card.FuelCapacity,
        "Progress" => card.Progress,
        "SpecialDurability1" => card.SpecialDurability1,
        "SpecialDurability2" => card.SpecialDurability2,
        "SpecialDurability3" => card.SpecialDurability3,
        "SpecialDurability4" => card.SpecialDurability4,
        _ => null,
    };

    private static bool TryNumber(object raw, out float value)
    {
        switch (raw)
        {
            case double d: value = (float)d; return true;
            case long l: value = l; return true;
            case int n: value = n; return true;
            case float f: value = f; return true;
            default: value = 0f; return false;
        }
    }

    public static void ApplyPatch(Harmony harmony)
    {
        try
        {
            var original = AccessTools.Method(typeof(GameManager), "GetStartingCardsFromArray");
            if (original == null)
                Log.Warn("PerkStartingItemStats: GameManager.GetStartingCardsFromArray not found; perk AddedCards keep their default stats.");
            else
                harmony.Patch(original, prefix: new HarmonyMethod(AccessTools.Method(typeof(PerkStartingItemStats), nameof(Prefix))));

            var init = AccessTools.Method(typeof(GameManager), "InitializePlayerCharacter");
            if (init == null)
                Log.Warn("PerkStartingItemStats: GameManager.InitializePlayerCharacter not found; perk EquippedCards keep their default stats.");
            else
                harmony.Patch(init,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(PerkStartingItemStats), nameof(EquipPrefix))),
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(PerkStartingItemStats), nameof(EquipPostfix))),
                    finalizer: new HarmonyMethod(AccessTools.Method(typeof(PerkStartingItemStats), nameof(EquipFinalizer))));
            Log.Debug("PerkStartingItemStats: patched GetStartingCardsFromArray and InitializePlayerCharacter.");
        }
        catch (Exception ex)
        {
            Log.Warn($"PerkStartingItemStats: patch setup failed: {Log.ExceptionText(ex)}");
        }
    }

    // Parameter names match the original (CardData[] _Array, bool _OnlyBlueprintsMode, bool _FromPerk,
    // CardData _StartingEnv). Replacing _Array by ref changes only what the original iterates; the
    // perk's own AddedCards array is never modified.
    private static void Prefix(ref CardData[] _Array, bool _OnlyBlueprintsMode, bool _FromPerk)
    {
        if (_entries.Count == 0 || !_FromPerk || _OnlyBlueprintsMode || _Array == null) return;

        Entry match = null;
        foreach (var entry in _entries)
            if (entry.Added != null && entry.Perk != null && ReferenceEquals(entry.Perk.AddedCards, _Array)) { match = entry; break; }
        if (match == null) return;

        var kept = new List<CardData>(_Array.Length);
        int given = 0;
        foreach (var card in _Array)
        {
            if (card != null
                && match.Added.TryGetValue(card.UniqueID ?? "", out var stats)
                && SpawnService.SpawnWithStats(card, stats, Vector2Int.zero))
            {
                given++;
                continue;
            }
            kept.Add(card);
        }

        if (given == 0) return;
        _Array = kept.ToArray();
        Log.Info($"PerkStartingItemStats: perk '{match.PerkUid}' gave {given} starting item(s) their declared stats.");
    }

    // InitializePlayerCharacter(bool _OnlyBlueprintsMode, CardData _StartingEnv).
    private static void EquipPrefix(GameManager __instance, bool _OnlyBlueprintsMode)
    {
        _swapped.Clear();
        _pendingEquip.Clear();
        if (_OnlyBlueprintsMode || __instance == null) return;

        var held = __instance.CurrentPlayerCharacter != null ? __instance.CurrentPlayerCharacter.CharacterPerks : null;
        if (held == null || held.Count == 0) return;

        bool any = false;
        foreach (var entry in _entries)
            if (entry.Equipped != null && entry.Perk != null && held.Contains(entry.Perk)) { any = true; break; }
        if (!any || !SpawnService.CanSpawnEquippedWithStats) return;

        foreach (var entry in _entries)
        {
            var perk = entry.Perk;
            if (entry.Equipped == null || perk == null || perk.EquippedCards == null || !held.Contains(perk)) continue;
            if (_swapped.Exists(s => ReferenceEquals(s.Key, perk))) continue;

            // From the first declared item on, every card moves to the postfix IN ORDER (the undeclared ones with
            // null stats, i.e. vanilla's own null transfer). A container followed by its contents (H&F's Hand
            // Basket then 16 mushrooms) needs the container equipped first: taking out only the basket let
            // vanilla spawn the mushrooms with no basket to go into (T2.430).
            var original = perk.EquippedCards;
            var kept = new List<CardData>(original.Length);
            bool moving = false;
            foreach (var card in original)
            {
                Dictionary<string, float> stats = null;
                if (card != null && entry.Equipped.TryGetValue(card.UniqueID ?? "", out var declared))
                {
                    stats = declared;
                    moving = true;
                }
                if (moving && card != null)
                    _pendingEquip.Add((entry.PerkUid, card, stats));
                else
                    kept.Add(card);
            }
            if (kept.Count == original.Length) continue;
            _swapped.Add(new KeyValuePair<CharacterPerk, CardData[]>(perk, original));
            perk.EquippedCards = kept.ToArray();
        }
    }

    private static readonly HashSet<string> _warnedNotEquippable = new(StringComparer.Ordinal);

    private static void WarnNotEquippable(string perkUid, string cardUid)
    {
        if (!_warnedNotEquippable.Add(perkUid + "|" + cardUid)) return;
        Log.Warn($"PerkStartingItemStats: perk '{perkUid}': card '{cardUid}' is not equippable (no EquipmentTags) and landed on the board, not in the Equipment slot; list it under AddedCards instead.");
    }

    private static void EquipPostfix()
    {
        if (_pendingEquip.Count == 0) return;
        var givenByPerk = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (perkUid, card, stats) in _pendingEquip)
        {
            if (SpawnService.SpawnEquippedWithStats(card, stats))
            {
                // The equip slot is only honoured for a card with EquipmentTags (CardData.IsEquipment);
                // anything else spawned into SlotsTypes.Equipment lands on the board (slot Base). The
                // spawn is a coroutine, so the card is not placed yet: decide from the same predicate.
                if (!card.IsEquipment)
                {
                    WarnNotEquippable(perkUid, card.UniqueID);
                    continue;
                }
                if (stats == null) continue;
                givenByPerk.TryGetValue(perkUid, out int n);
                givenByPerk[perkUid] = n + 1;
            }
            else
            {
                Log.Warn($"PerkStartingItemStats: perk '{perkUid}' could not equip '{card.UniqueID}'.");
            }
        }
        foreach (var kvp in givenByPerk)
            Log.Info($"PerkStartingItemStats: perk '{kvp.Key}' equipped {kvp.Value} starting item(s) with their declared stats.");
    }

    // Runs after the postfix, and also when the original throws: the perk's EquippedCards array is
    // a shared asset, so it must never be left filtered.
    private static Exception EquipFinalizer(Exception __exception)
    {
        foreach (var kvp in _swapped)
            if (kvp.Key != null) kvp.Key.EquippedCards = kvp.Value;
        _swapped.Clear();
        _pendingEquip.Clear();
        return __exception;
    }
}
