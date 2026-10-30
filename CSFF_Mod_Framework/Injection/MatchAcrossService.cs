using CSFFModFramework.Data;
using CSFFModFramework.Loading;
using CSFFModFramework.Util;

namespace CSFFModFramework.Injection;

/// <summary>
/// Game side of the cross-slot matching rule (framework 2.29.0, fleet plan 6.1). The rule itself, and the parse of
/// <c>"MatchAcross"</c>, live in the pure <see cref="MatchAcrossRules"/>; finding
/// <c>Documentation/Research/Fleet/APE_Port/W0.4_cross_slot.md</c> (and its "As built" section).
///
/// <para>
/// LOAD (<see cref="LoadAll"/>, after GameSourceModifier and TagGroupService.Fill): reads <c>MatchAcross</c> from
/// each mod blueprint's parsed JSON tree (<see cref="JsonDataLoader.ParsedJsonByUniqueId"/>, no disk read) and from
/// its <c>.extras.json</c> sidecar (<see cref="ExtraDataStore"/>), resolves the slots against the loaded
/// <c>BlueprintStages</c>, and keeps one array of rules per blueprint <c>CardData</c>.
/// </para>
/// <para>
/// PLAY: one Harmony postfix on <c>BlueprintElement.CompatibleInGameCard</c>, the per-slot check every placement
/// path runs through (popup hover and drop, a drop on the board, AutoFill, <c>MoveCardToSlot</c>, the Build
/// button's ready state, NPC duties). The element is a struct with no link to its blueprint, so the blueprint is
/// taken from a context that prefixes on <c>InGameCardBase.CanReceiveInInventoryInstance(InGameCardBase, bool)</c>
/// and <c>InGameCardBase.DropInInventory</c> set (finalizers restore it), and otherwise from the slot's current
/// card, the candidate's own container, or the inspected card. The patches are installed only when some blueprint
/// declared a rule, and a blueprint with none pays one dictionary miss per check.
/// </para>
/// </summary>
internal static class MatchAcrossService
{
    private const string Tag = "[MatchAcross]";

    // Blueprint CardData -> its rules. Empty: nothing declared, and the patches are not installed.
    private static readonly Dictionary<CardData, MatchAcrossRule[]> _rules = new();
    private static bool _patchApplied;

    // Private fields of the BlueprintElement struct, read by ref (no boxing on the hot path).
    private static AccessTools.StructFieldRef<BlueprintElement, CardData> _requiredCard;
    private static AccessTools.StructFieldRef<BlueprintElement, CardTabGroup> _requiredTabGroup;

    // The blueprint card whose CanReceiveInInventoryInstance / DropInInventory is running (null outside them).
    private static InGameCardBase _context;
    // Counts refusals, so the DropInInventory finalizer can tell whether one happened inside its own call.
    private static int _refusals;
    // Reused per check; the game calls the check on the main thread only.
    private static readonly List<MatchAcrossPlaced> _placed = new();
    private static readonly HashSet<string> _loggedRefusals = new(StringComparer.Ordinal);
    private static readonly HashSet<string> _warned = new(StringComparer.Ordinal);

    internal static int BlueprintCount => _rules.Count;

    /// <summary>True when any mod blueprint JSON or sidecar carries the key (cheap: no parse).</summary>
    internal static bool AnyDeclared()
    {
        foreach (var kv in JsonDataLoader.ParsedJsonByUniqueId)
            if (kv.Value != null && kv.Value.ContainsKey(MatchAcrossRules.Key)) return true;
        foreach (var kv in ExtraDataStore.All)
            if (kv.Value != null && kv.Value.IndexOf("\"" + MatchAcrossRules.Key + "\"", StringComparison.Ordinal) >= 0) return true;
        return false;
    }

    /// <summary>Builds the per-blueprint rule table. Safe to call again: it starts from empty.</summary>
    internal static void LoadAll()
    {
        _rules.Clear();
        _loggedRefusals.Clear();
        if (!EnsureFieldAccess()) return;

        // Every UID that declares a rule, with where each declaration was read. A blueprint may carry the key in
        // its JSON and in its sidecar; both are read and their rules apply together.
        var sources = new List<(string Uid, object Value, string Source)>();
        foreach (var kv in JsonDataLoader.ParsedJsonByUniqueId)
            if (kv.Value != null && kv.Value.TryGetValue(MatchAcrossRules.Key, out var v))
                sources.Add((kv.Key, v, "blueprint JSON"));
        foreach (var kv in ExtraDataStore.All)
        {
            if (kv.Value == null || kv.Value.IndexOf("\"" + MatchAcrossRules.Key + "\"", StringComparison.Ordinal) < 0) continue;
            object root;
            try { root = MiniJson.Parse(kv.Value); }
            catch (Exception ex)
            {
                Log.Warn($"{Tag} {kv.Key}: .extras.json sidecar is not valid JSON ({ex.GetType().Name}: {ex.Message}); its {MatchAcrossRules.Key} is ignored.");
                continue;
            }
            if (root is Dictionary<string, object> obj && obj.TryGetValue(MatchAcrossRules.Key, out var sv))
                sources.Add((kv.Key, sv, ".extras.json sidecar"));
        }

        int issues = 0, notBlueprint = 0;
        var byCard = new Dictionary<CardData, List<MatchAcrossRule>>();
        foreach (var (uid, value, source) in sources)
        {
            var card = GameRegistry.GetByUid<CardData>(uid);
            var mod = JsonDataLoader.UniqueIdToModName.TryGetValue(uid, out var m) ? m : "?";
            if (card == null || card.BlueprintStages == null || card.BlueprintStages.Length == 0)
            {
                Log.Warn($"{Tag} [{mod}] {uid} carries {MatchAcrossRules.Key} ({source}) but is not a loaded blueprint with stages; ignored.");
                notBlueprint++;
                continue;
            }

            var stages = card.BlueprintStages;
            var counts = new int[stages.Length];
            for (int s = 0; s < stages.Length; s++) counts[s] = stages[s]?.RequiredElements?.Length ?? 0;

            var problems = new List<string>();
            var parsed = MatchAcrossRules.Parse(uid, value, counts, (s, e) => NamesOf(stages, s, e), problems, source);
            foreach (var p in problems) Log.Warn($"{Tag} [{mod}] {p}");
            issues += problems.Count;

            foreach (var rule in parsed)
                issues += CheckRule(mod, uid, stages, rule);

            if (parsed.Count == 0) continue;
            if (!byCard.TryGetValue(card, out var list)) byCard[card] = list = new List<MatchAcrossRule>();
            list.AddRange(parsed);
        }

        int ruleCount = 0;
        foreach (var kv in byCard)
        {
            _rules[kv.Key] = kv.Value.ToArray();
            ruleCount += kv.Value.Count;
            Log.Debug($"{Tag} {kv.Key.UniqueID}: {string.Join("; ", kv.Value.Select(r => r.ToString()))}");
        }

        if (issues > 0 || notBlueprint > 0)
            Log.Info($"{Tag} {ruleCount} rule(s) on {_rules.Count} blueprint(s) ({issues} issue(s), {notBlueprint} not a blueprint; each named above).");
        else
            Log.Info($"{Tag} {ruleCount} rule(s) on {_rules.Count} blueprint(s).");
    }

    private static string[] NamesOf(BlueprintStage[] stages, int s, int e)
    {
        var elems = stages[s]?.RequiredElements;
        if (elems == null || e < 0 || e >= elems.Length) return null;
        ref var el = ref elems[e];
        var card = _requiredCard(ref el);
        var group = _requiredTabGroup(ref el);
        return new[] { card ? card.UniqueID : null, group ? group.name : null };
    }

    // Load-time warnings about slots the rule cannot see as written. Returns the number of warnings.
    private static int CheckRule(string mod, string uid, BlueprintStage[] stages, MatchAcrossRule rule)
    {
        int warnings = 0;
        int lastStage = rule.Stages.Max();
        for (int i = 0; i < rule.Stages.Length; i++)
        {
            int s = rule.Stages[i], e = rule.Elements[i];
            ref var el = ref stages[s].RequiredElements[e];
            // A card placed in an earlier stage is destroyed when that stage completes; only an element that
            // remembers its cards (vanilla "Remember": true, and not kept) leaves a record a later stage can read.
            if (s < lastStage && !el.RememberCards)
            {
                Log.Warn($"{Tag} [{mod}] {uid}: {rule} lists slot {s}.{e}, an earlier stage than slot(s) it must match, but that element does not remember its cards (\"Remember\": true, not DontSpend); later stages cannot see what it held.");
                warnings++;
            }
            if (el.GetLiquidQuantity > 0)
            {
                Log.Warn($"{Tag} [{mod}] {uid}: {rule} lists slot {s}.{e}, which asks for a liquid; the rule compares the card offered (a liquid inside a container is checked on its own), so matching a liquid is not supported.");
                warnings++;
            }
        }
        return warnings;
    }

    private static bool EnsureFieldAccess()
    {
        if (_requiredCard != null && _requiredTabGroup != null) return true;
        try
        {
            _requiredCard = AccessTools.StructFieldRefAccess<BlueprintElement, CardData>("RequiredCard");
            _requiredTabGroup = AccessTools.StructFieldRefAccess<BlueprintElement, CardTabGroup>("RequiredTabGroup");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"{Tag} BlueprintElement.RequiredCard / RequiredTabGroup not found (a game update?); MatchAcross rules are off: {Log.ExceptionText(ex)}");
            _requiredCard = null;
            _requiredTabGroup = null;
            return false;
        }
    }

    /// <summary>Installs the patches once per process, and only when some blueprint declared a rule.</summary>
    internal static void ApplyPatch(Harmony harmony)
    {
        if (_patchApplied || _rules.Count == 0) return;
        _patchApplied = true;
        try
        {
            var check = AccessTools.Method(typeof(BlueprintElement), nameof(BlueprintElement.CompatibleInGameCard),
                new[] { typeof(InGameCardBase), typeof(InGameCardBase), typeof(bool) });
            var canReceive = AccessTools.Method(typeof(InGameCardBase), nameof(InGameCardBase.CanReceiveInInventoryInstance),
                new[] { typeof(InGameCardBase), typeof(bool) });
            var drop = AccessTools.Method(typeof(InGameCardBase), nameof(InGameCardBase.DropInInventory), new[] { typeof(InGameCardBase) });
            if (check == null)
            {
                Log.Warn($"{Tag} BlueprintElement.CompatibleInGameCard(InGameCardBase, InGameCardBase, bool) not found (a game update?); MatchAcross rules are off.");
                return;
            }
            harmony.Patch(check, postfix: new HarmonyMethod(typeof(MatchAcrossService), nameof(CompatibleInGameCard_Postfix)));

            // The 2-argument overload: the 1-argument one is a one-line forwarder the JIT may inline.
            if (canReceive != null)
                harmony.Patch(canReceive, prefix: new HarmonyMethod(typeof(MatchAcrossService), nameof(Context_Prefix)),
                    finalizer: new HarmonyMethod(typeof(MatchAcrossService), nameof(Context_Finalizer)));
            else
                Log.Warn($"{Tag} InGameCardBase.CanReceiveInInventoryInstance(InGameCardBase, bool) not found; a drop on a blueprint card on the board may skip the rule.");
            if (drop != null)
                harmony.Patch(drop, prefix: new HarmonyMethod(typeof(MatchAcrossService), nameof(Drop_Prefix)),
                    finalizer: new HarmonyMethod(typeof(MatchAcrossService), nameof(Drop_Finalizer)));
            else
                Log.Warn($"{Tag} InGameCardBase.DropInInventory(InGameCardBase) not found; a drop on a blueprint card on the board may skip the rule.");
            Log.Debug($"{Tag} patched BlueprintElement.CompatibleInGameCard, CanReceiveInInventoryInstance and DropInInventory.");
        }
        catch (Exception ex)
        {
            Log.Warn($"{Tag} failed to install the patches; MatchAcross rules are off: {Log.ExceptionText(ex)}");
        }
    }

    // ── Context ──────────────────────────────────────────────────────────────────────────────────────────

    private static void Context_Prefix(InGameCardBase __instance, out InGameCardBase __state)
    {
        __state = _context;
        _context = __instance;
    }

    private static void Context_Finalizer(InGameCardBase __state) => _context = __state;

    private struct DropState
    {
        public InGameCardBase Previous;
        public int Refusals;
    }

    private static void Drop_Prefix(InGameCardBase __instance, out DropState __state)
    {
        __state = new DropState { Previous = _context, Refusals = _refusals };
        _context = __instance;
    }

    // Vanilla DropInInventory: when the slot GetIndexForInventory picked refuses the card, its fallback loop sets
    // the index to -1 and then reads CardsInInventory[-1] for the first other element that accepts the card
    // (ArgumentOutOfRangeException before anything moves). A MatchAcross refusal makes that path reachable when one
    // card fits two elements, so a throw after a refusal inside this call is the refusal: nothing is placed.
    private static Exception Drop_Finalizer(Exception __exception, DropState __state, ref bool __result)
    {
        _context = __state.Previous;
        if (__exception is ArgumentOutOfRangeException && _refusals != __state.Refusals)
        {
            WarnOnce("drop-fallback", $"{Tag} a refused card fell into vanilla DropInInventory's fallback, which reads slot -1; treated as refused (nothing placed). Logged once.");
            __result = false;
            return null;
        }
        return __exception;
    }

    // ── The check ───────────────────────────────────────────────────────────────────────────────────────

    // Runs for every per-slot check the game makes, including every frame per visible blueprint card (the Build
    // button's ready state). A refused card, or no rule on the blueprint, leaves after a bool test, a count test,
    // a few reference reads and one dictionary miss.
    private static void CompatibleInGameCard_Postfix(ref BlueprintElement __instance, InGameCardBase _Card, InGameCardBase _CardsAlreadyInStack, bool _Debug, ref bool __result)
    {
        if (!__result || _rules.Count == 0) return;
        try
        {
            var bp = ResolveBlueprint(_Card, _CardsAlreadyInStack, out var rules);
            if (rules == null) return;

            var data = bp.BlueprintData;
            var stages = bp.CardModel.BlueprintStages;
            if (data == null || stages == null) return;
            int stage = data.CurrentStage;
            if (stage < 0 || stage >= stages.Length) return;
            var elems = stages[stage]?.RequiredElements;
            if (elems == null) return;

            foreach (var rule in rules)
            {
                int element = ListedElementOf(ref __instance, elems, stage, rule);
                if (element < 0) continue;

                var type = (DurabilitiesTypes)rule.DurabilityType;
                float candidate = _Card.GetCurrentDurability(type, false);
                CollectPlaced(bp, stages, stage, rule, type, _Card);
                if (MatchAcrossRules.Decide(rule, stage, element, candidate, _placed, out int conflict))
                    continue;

                __result = false;
                _refusals++;
                if (Log.Verbose && _Debug) LogRefusal(bp, stage, element, rule, _Card, candidate, conflict);
                return;
            }
        }
        catch (Exception ex)
        {
            WarnOnce("check:" + ex.GetType().Name, $"{Tag} the check threw (the card is accepted as vanilla decided; logged once per cause): {Log.ExceptionText(ex)}");
        }
    }

    // Which blueprint card the element belongs to, in order of certainty. The first source that names a card
    // decides; null rules means "no MatchAcross here".
    private static InGameCardBase ResolveBlueprint(InGameCardBase card, InGameCardBase alreadyInStack, out MatchAcrossRule[] rules)
    {
        rules = null;
        InGameCardBase bp;
        if ((object)_context != null) bp = _context;                     // inside CanReceiveInInventoryInstance / DropInInventory
        else if ((object)alreadyInStack != null) bp = alreadyInStack.CurrentContainer; // the slot's own card sits in it
        else
        {
            var own = card.CurrentContainer;                             // the candidate already placed (the Build check)
            if ((object)own != null && own.CardModel && own.CardModel.IsBlueprintCard) bp = own;
            else
            {
                var gm = MBSingleton<GraphicsManager>.Instance;           // the popup (empty slot, MoveCardToSlot, AutoFill)
                bp = gm ? gm.InspectedCard : null;
            }
        }
        if (!bp) return null;
        var model = bp.CardModel;
        if ((object)model == null || !_rules.TryGetValue(model, out rules)) { rules = null; return null; }
        return bp;
    }

    // The listed slot the checked element is, or -1. Elements have no identity of their own: match the required
    // card and tab group by reference against the live stage (BlueprintAlternates may have swapped a group since
    // load), and treat every element that looks the same as the one checked (two identical elements: if one is
    // listed, both are).
    private static int ListedElementOf(ref BlueprintElement checkedElement, BlueprintElement[] elems, int stage, MatchAcrossRule rule)
    {
        object card = _requiredCard(ref checkedElement);
        object group = _requiredTabGroup(ref checkedElement);
        for (int e = 0; e < elems.Length; e++)
        {
            ref var el = ref elems[e];
            if ((object)_requiredCard(ref el) != card || (object)_requiredTabGroup(ref el) != group) continue;
            if (rule.IsListed(stage, e)) return e;
        }
        return -1;
    }

    // Every card the rule must compare with: the cards in this stage's listed slots (all of each slot's stack),
    // and the cards remembered from earlier stages whose card fits a listed slot of that stage.
    private static void CollectPlaced(InGameCardBase bp, BlueprintStage[] stages, int stage, MatchAcrossRule rule, DurabilitiesTypes type, InGameCardBase candidate)
    {
        _placed.Clear();
        var slots = bp.CardsInInventory;
        for (int i = 0; i < rule.Stages.Length; i++)
        {
            if (rule.Stages[i] != stage) continue;
            int e = rule.Elements[i];
            if (slots == null || e >= slots.Count || slots[e] == null) continue;
            var all = slots[e].AllCards;
            if (all == null) continue;
            for (int k = 0; k < all.Count; k++)
            {
                var c = all[k];
                if ((object)c == null || ReferenceEquals(c, candidate) || !c || c.Destroyed) continue;
                _placed.Add(new MatchAcrossPlaced(stage, e, c.GetCurrentDurability(type, false)));
            }
        }

        var prev = bp.BlueprintData?.PrevStageCards;
        if (prev == null || prev.Count == 0) return;
        foreach (var used in prev)
        {
            if (used?.Ingredients == null || used.BlueprintStageIndex < 0 || used.BlueprintStageIndex >= stage) continue;
            int s = used.BlueprintStageIndex;
            if (!rule.StageHasListed(s)) continue;
            foreach (var saved in used.Ingredients)
            {
                if (saved == null || string.IsNullOrEmpty(saved.CardID)) continue;
                var model = UniqueIDScriptable.GetFromID<CardData>(saved.CardID);
                if (!model || !FitsListedSlot(stages, s, rule, model)) continue;
                _placed.Add(new MatchAcrossPlaced(s, -1, SavedValue(saved, type)));
            }
        }
    }

    private static bool FitsListedSlot(BlueprintStage[] stages, int s, MatchAcrossRule rule, CardData model)
    {
        var elems = stages[s]?.RequiredElements;
        if (elems == null) return false;
        for (int i = 0; i < rule.Stages.Length; i++)
        {
            if (rule.Stages[i] != s || rule.Elements[i] >= elems.Length) continue;
            if (elems[rule.Elements[i]].CompatibleCard(model, null)) return true;
        }
        return false;
    }

    // SimpleCardSaveData keeps Progress as ConsumableCharges (SaveCard).
    private static float SavedValue(SimpleCardSaveData saved, DurabilitiesTypes type) => type switch
    {
        DurabilitiesTypes.Spoilage => saved.Spoilage,
        DurabilitiesTypes.Usage => saved.Usage,
        DurabilitiesTypes.Fuel => saved.Fuel,
        DurabilitiesTypes.Progress => saved.ConsumableCharges,
        DurabilitiesTypes.Special1 => saved.Special1,
        DurabilitiesTypes.Special2 => saved.Special2,
        DurabilitiesTypes.Special3 => saved.Special3,
        DurabilitiesTypes.Special4 => saved.Special4,
        _ => 0f,
    };

    private static void LogRefusal(InGameCardBase bp, int stage, int element, MatchAcrossRule rule, InGameCardBase card, float candidate, int conflict)
    {
        var bpUid = bp.CardModel ? bp.CardModel.UniqueID : "?";
        var cardUid = card.CardModel ? card.CardModel.UniqueID : "?";
        var key = bpUid + "|" + stage + "." + element + "|" + cardUid + "|" + candidate;
        if (!_loggedRefusals.Add(key)) return;
        var other = conflict >= 0 && conflict < _placed.Count ? _placed[conflict] : default;
        var where = other.Element < 0 ? $"stage {other.Stage}" : $"slot {other.Stage}.{other.Element}";
        Log.Debug($"{Tag} {bpUid} slot {stage}.{element} refused {cardUid} ({rule.Stat} {candidate}): {where} holds {rule.Stat} {other.Value}.");
    }

    private static void WarnOnce(string key, string message)
    {
        if (_warned.Add(key)) Log.Warn(message);
    }
}
