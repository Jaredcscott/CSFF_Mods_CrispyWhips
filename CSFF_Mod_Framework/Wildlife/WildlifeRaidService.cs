using System.Collections;
using System.Reflection;
using CSFFModFramework.Data;
using CSFFModFramework.Util;

namespace CSFFModFramework.Wildlife;

/// <summary>
/// At each in-game day rollover, rolls a chance to raid one on-board container tagged
/// <c>tag_NotSafeFromAnimals</c>. A random food card inside is transformed to RottenRemains
/// (in-place, not destroyed — see CLAUDE.md in-place transform pattern). Player takes a small
/// stress hit.
///
/// The tag is defined in vanilla (<c>ScriptableObjectJsonDataWithWarpLitAllInOne/CardTag/
/// tag_NotSafeFromAnimals.json</c>) but never applied to vanilla cards and not referenced by
/// Assembly-CSharp. We inject it at load time onto open-storage containers (Basket, Shelf,
/// Sack, etc.) so the feature works on existing saves.
///
/// Opt-in: governed by <c>WildlifeRaidsEnabled</c> config (default false).
/// </summary>
internal static class WildlifeRaidService
{
    private const string NotSafeTagName = "tag_NotSafeFromAnimals";
    private const string NotSafeBearTagName = "tag_NotSafeFromBears";
    private const string FoodTagName = "tag_HumanFood";
    private const string RottenRemainsUID = "25a487b16088c2046a51935973ba6a90";
    private const string StressStatUID = "3b79a4c6d7e151044a1c56fbbd401d78";

    /// <summary>
    /// Fallback container UIDs that gain the NotSafe tag at load time (EA 0.65 values).
    /// The live list comes from <see cref="Api.VanillaIds.OpenStorage"/> (regenerated per
    /// game version); this array only covers a missing/empty embedded registry.
    /// </summary>
    private static readonly string[] FallbackOpenStorage =
    {
        "fc102f9646c86fc4d85f25f05713376b", // BasketPlaced
        "ae80b3304fa930748941abc6edc5c884", // HandBasket
        "487c5e8616abfec4198cdf0883135212", // Shelf
        "1c62d1f5116b7014e9cc4f7615ecc33c", // ClothSack (Sack removed EA 0.65)
        "a2eabda942140a84fafc371513d4d886", // LeatherSack
        "9fc4843f7d5c97044952b9c14902f431", // RusticBarrelLocation
        "4e2b3e00c88f8d14cb52a614584a66d5", // DryingRack
    };

    private static IReadOnlyList<string> OpenStorageUids
    {
        get
        {
            var fromRegistry = Api.VanillaIds.OpenStorage;
            return fromRegistry.Count > 0 ? fromRegistry : FallbackOpenStorage;
        }
    }

    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static bool Enabled { get; set; } = false;
    public static float DailyChance { get; set; } = 0.35f;
    public static float BearRaidChance { get; set; } = 0.5f;
    public static float StressPenalty { get; set; } = 2f;

    /// <summary>
    /// Plan feature 20 (opt-in, default 0 = off): after this many consecutive in-game days with a
    /// successful raid, the next raid is a <c>Combat_EncounterBear</c> fight instead of
    /// <see cref="RaidOnce"/>. The count lives for the session only and resets on every
    /// <c>GameManager.OnGMInitialized</c> (a save reload or menu round trip), plan Open question 12.
    /// </summary>
    public static int BearRaidEscalationDays { get; set; } = 0;

    // Combat_EncounterBear, the Encounter every vanilla brown bear fight starts (see
    // WildlifeRaidPatch.FallbackBearEncounterUID for the history of this constant).
    internal const string EscalationEncounterUID = "4ce4b386231d17741aa540b11fd8e832";

    private static int _consecutiveRaidDays;
    private static bool _escalationPending;
    private static bool _runHooked;

    /// <summary>
    /// Set immediately before the escalation's throwaway action starts and cleared by the first
    /// <c>EncounterPopup.StartEncounter</c> after it (WildlifeRaidPatch) or when the action ends.
    /// While set, that postfix treats the start as the escalation and never chains a second raid
    /// from it through <see cref="OnBearEncounter"/>.
    /// </summary>
    internal static bool EscalationStartInFlight { get; private set; }

    private static bool _ready;
    private static bool _loadCompleteProcessed;
    private static int _lastDayTimePoints = int.MinValue;
    private static Type _gameManagerType;
    private static PropertyInfo _gameManagerInstanceProp;
    private static object _notSafeTagAsset;
    private static object _notSafeBearTagAsset;

    /// <summary>Call once at framework Awake — registers a one-shot hook that fires after data load.</summary>
    public static void Init()
    {
        // Runner: a Unity coroutine-host MonoBehaviour owned by the framework's Plugin.
        // We piggy-back on the Plugin's Update() via <see cref="PollUpdate"/>; all init happens there.
        _ready = false;
        _loadCompleteProcessed = false;

        // Per-run reset. Init runs once per process (Plugin.Awake) and the latch keeps it to one
        // subscription, so a reload never stacks a second handler (root CLAUDE.md "Per-Run Event
        // Registration", pattern b); the handler itself only clears per-run state.
        if (!_runHooked)
        {
            _runHooked = true;
            GameManager.OnGMInitialized += OnRunStart;
        }
    }

    private static void OnRunStart()
    {
        if (_consecutiveRaidDays > 0 || _escalationPending)
            Log.Debug($"[WildlifeRaid] run start: raid-day streak {_consecutiveRaidDays} and pending bear escalation={_escalationPending} reset.");
        _consecutiveRaidDays = 0;
        _escalationPending = false;
        EscalationStartInFlight = false;
    }

    /// <summary>Call from Plugin.Update(). Cheap no-op until ready; then checks day rollover.</summary>
    public static void PollUpdate()
    {
        if (!Enabled) return;
        if (!_ready)
        {
            if (!TryCompleteOneTimeSetup()) return;
        }

        // Day rollover: GameManager.CurrentDay advancing. Detection shared with mods via Api.Gate.
        if (Api.Gate.OncePerDayRollover(ref _lastDayTimePoints))
        {
            try { TryRaid(); }
            catch (Exception ex) { Log.Warn($"[WildlifeRaid] raid attempt failed: {Log.ExceptionText(ex)}"); }
        }

        // The rollover is seen mid-action (CurrentDay advances inside SpendDaytimePoints, which runs
        // in the Sleep or Rest action), so the bear waits here for the first idle frame (W0-10).
        if (_escalationPending)
        {
            try { TryStartEscalation(); }
            catch (Exception ex)
            {
                _escalationPending = false;
                EscalationStartInFlight = false;
                Log.Warn($"[WildlifeRaid] bear escalation start failed: {Log.ExceptionText(ex)}");
            }
        }
    }

    // ------------------------------------------------------------ setup ------

    private static bool TryCompleteOneTimeSetup()
    {
        // Wait until the game's UID registry is reachable — populated once GameLoad runs.
        if (GameRegistry.Count == 0) return false;
        if (_loadCompleteProcessed) return _ready;
        _loadCompleteProcessed = true;

        ResolveTypes();
        ResolveTagAsset();
        InjectTagOnVanillaContainers();
        _ready = true;
        Log.Debug($"[WildlifeRaid] initialized - enabled={Enabled} chance={DailyChance:F2}");
        return true;
    }

    private static void ResolveTypes()
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (_gameManagerType == null) _gameManagerType = asm.GetType("GameManager", false);
            if (_gameManagerType != null) break;
        }
        if (_gameManagerType != null)
        {
            // GameManager.Instance is defined on MBSingleton<T>, not GameManager directly.
            // FlattenHierarchy + base-type walk is required; without it the prop is always null.
            _gameManagerInstanceProp = _gameManagerType.GetProperty("Instance",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy);
            if (_gameManagerInstanceProp == null)
            {
                for (var t = _gameManagerType.BaseType; t != null; t = t.BaseType)
                {
                    _gameManagerInstanceProp = t.GetProperty("Instance",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy);
                    if (_gameManagerInstanceProp != null) break;
                }
            }

        }
    }

    private static void ResolveTagAsset()
    {
        if (Database.AllScriptableObjectDict.TryGetValue(NotSafeTagName, out var so))
            _notSafeTagAsset = so;
        else
            Log.Warn($"[WildlifeRaid] vanilla CardTag '{NotSafeTagName}' not found — tag injection skipped.");

        if (Database.AllScriptableObjectDict.TryGetValue(NotSafeBearTagName, out var soB))
            _notSafeBearTagAsset = soB;
        else
            Log.Warn($"[WildlifeRaid] vanilla CardTag '{NotSafeBearTagName}' not found — bear tag injection skipped.");
    }

    private static void InjectTagOnVanillaContainers()
    {
        int injected = 0;
        foreach (var uid in OpenStorageUids)
        {
            var target = GameRegistry.GetByUid(uid);
            if (target == null) continue;
            var tagsField = target.GetType().GetField("CardTags", Flags);
            if (tagsField == null) continue;

            injected += TryInjectTag(target, tagsField, _notSafeTagAsset, NotSafeTagName, uid);
            injected += TryInjectTag(target, tagsField, _notSafeBearTagAsset, NotSafeBearTagName, uid);
        }
        if (injected > 0) Log.Debug($"[WildlifeRaid] injected NotSafe tags on {injected} vanilla container slot(s).");
    }

    // CardData.CardTags is a CardTag[] (.decomp/CardData.cs). An array passes `is IList`, but its Add always throws
    // NotSupportedException, so until this was fixed every injection failed into a Debug line and no vanilla
    // container was ever raidable (harness T2.273, 2026-10-05). The tag goes in as a new, longer array assigned
    // back to the field.
    private static int TryInjectTag(object target, FieldInfo tagsField, object tagAsset, string tagName, string uid)
    {
        if (tagAsset == null) return 0;
        var elemType = tagsField.FieldType.IsArray ? tagsField.FieldType.GetElementType() : null;
        if (elemType == null || !elemType.IsInstanceOfType(tagAsset))
        {
            Log.Warn($"[WildlifeRaid] cannot add '{tagName}' to {uid}: CardTags is {tagsField.FieldType.Name}, not an array of the tag's type");
            return 0;
        }
        var current = tagsField.GetValue(target) as Array;
        int count = current?.Length ?? 0;
        for (int i = 0; i < count; i++)
        {
            if (current.GetValue(i) is UnityEngine.Object uo && uo && uo.name == tagName) return 0; // already present
        }
        var grown = Array.CreateInstance(elemType, count + 1);
        if (count > 0) Array.Copy(current, grown, count);
        grown.SetValue(tagAsset, count);
        try { tagsField.SetValue(target, grown); return 1; }
        catch (Exception ex) { Log.Warn($"[WildlifeRaid] inject '{tagName}' failed on {uid}: {Log.ExceptionText(ex)}"); return 0; }
    }

    // ----------------------------------------------------------- raid ------

    /// <summary>Called by the encounter patch when a bear combat encounter starts.</summary>
    public static void OnBearEncounter()
    {
        if (!_ready) return;
        if (UnityEngine.Random.value > BearRaidChance) return;
        RaidOnce(bearRaid: true);
    }

    private static void TryRaid()
    {
        // A bear that has not found its idle frame yet still owns this streak: no raid on top of it.
        if (_escalationPending) return;

        if (UnityEngine.Random.value > DailyChance)
        {
            BreakStreak("no raid roll");
            return;
        }

        if (BearRaidEscalationDays > 0 && _consecutiveRaidDays >= BearRaidEscalationDays)
        {
            // The bear comes for food, so it needs something a raid could have hit today.
            if (FindRaidCandidates(bearRaid: false).Count == 0)
            {
                BreakStreak("no open container with food in your location");
                return;
            }
            _escalationPending = true;
            Log.Info($"[WildlifeRaid] {_consecutiveRaidDays} day(s) of raids in a row: a brown bear is coming for the food.");
            return;
        }

        if (RaidOnce(bearRaid: false)) _consecutiveRaidDays++;
        else BreakStreak("raid found nothing to spoil");
    }

    private static void BreakStreak(string why)
    {
        if (_consecutiveRaidDays > 0)
            Log.Debug($"[WildlifeRaid] raid-day streak {_consecutiveRaidDays} broken: {why}.");
        _consecutiveRaidDays = 0;
    }

    // GameManager.EncounterToStart is private. A value already there would win over the
    // escalation's drop (the drop is ignored while it is set, GameManager.cs 5160-5178) and that
    // other fight would then be read as the escalation, so the start waits for it to clear.
    private static readonly FieldInfo _encounterToStartField = AccessTools.Field(typeof(GameManager), "EncounterToStart");
    private static readonly HashSet<string> _escalationWarned = new();

    private static void WarnEscalationOnce(string cause, string message)
    {
        if (_escalationWarned.Add(cause)) Log.Warn($"[WildlifeRaid] bear escalation: {message}.");
    }

    /// <summary>
    /// Starts the escalation bear at W0-10's safe point: no action in flight, state SELECT, no
    /// fight, no dialog, not initializing or catching up. The start is the vanilla form (the
    /// encounter-duty, chase and save-resume starts all do this): a throwaway CardAction whose
    /// only drop names the encounter, run through GameManager.PerformAction so it becomes the
    /// root action and holds RootAction for the whole fight. A direct EncounterPopup.StartEncounter
    /// call leaves RootAction null and lets an action run beside the fight (W0-10 runtime leg B).
    /// EncounterNPC is left null, so every null-NPC encounter guard applies to it.
    /// </summary>
    private static void TryStartEscalation()
    {
        var gm = MBSingleton<GameManager>.Instance;
        if (!gm) return;
        if (GameManager.PerformingAction) return;
        if (GameManager.CurrentState != GameStates.SELECT) return;
        if (gm.IsInitializing || gm.IsCatchingUp) return;
        var encounterPopup = MBSingleton<EncounterPopup>.Instance;
        if (encounterPopup && encounterPopup.OngoingEncounter) return;
        var dialogs = MBSingleton<DialogsPopup>.Instance;
        if (dialogs && dialogs.IsInDialog) return;
        if (_encounterToStartField == null)
            WarnEscalationOnce("field", "GameManager.EncounterToStart not found; starting without the pending-encounter check");
        else if (_encounterToStartField.GetValue(gm) is UnityEngine.Object pending && pending) return;

        if (!(GameRegistry.GetByUid(EscalationEncounterUID) is Encounter encounter) || !encounter)
        {
            _escalationPending = false;
            WarnEscalationOnce("encounter", $"Encounter '{EscalationEncounterUID}' (Combat_EncounterBear) not found; no bear started");
            return;
        }

        var action = new CardAction(new LocalizedString
        {
            LocalizationKey = "IGNOREKEY",
            DefaultText = "Bear raid escalation",
        }, default(LocalizedString));
        action.ProducedCards = new CardsDropCollection[1];
        action.ProducedCards[0] = new CardsDropCollection
        {
            CollectionName = "BearRaidEscalation",
            CollectionWeight = 1,
        };
        action.ProducedCards[0].DroppedEncounter = encounter;
        action.ProducedCards[0].EncounterNPC = null;

        _escalationPending = false;
        EscalationStartInFlight = true;
        Log.Debug("[WildlifeRaid] idle frame reached: starting the bear escalation action.");
        gm.StartCoroutine(RunEscalation(action));
    }

    private static IEnumerator RunEscalation(CardAction action)
    {
        yield return GameManager.PerformAction(action, null, false, InGameNPCOrPlayer.PlayerAgent);
        if (EscalationStartInFlight)
        {
            EscalationStartInFlight = false;
            Log.Info($"[WildlifeRaid] bear escalation action ended without a fight starting; the {_consecutiveRaidDays}-day streak is kept.");
        }
    }

    /// <summary>
    /// Called by WildlifeRaidPatch's StartEncounter postfix for the first start after the
    /// escalation action began. A start an encounter guard suppressed keeps the streak, so a
    /// guarded place (CMC's village) starts nothing, every day; a real start resets it.
    /// </summary>
    internal static void OnEscalationStartObserved(bool suppressed)
    {
        EscalationStartInFlight = false;
        if (suppressed)
        {
            Log.Info($"[WildlifeRaid] bear escalation stopped by an encounter guard; the {_consecutiveRaidDays}-day streak is kept.");
            return;
        }
        Log.Info($"[WildlifeRaid] bear escalation started after {_consecutiveRaidDays} day(s) of raids; streak reset.");
        _consecutiveRaidDays = 0;
    }

    private static List<object> FindRaidCandidates(bool bearRaid)
    {
        var candidates = new List<object>();
        var allCards = CollectAllCards();
        foreach (var c in allCards)
        {
            if (c == null) continue;
            if (!IsInPlayerEnv(c)) continue;
            var model = GetMemberValue(c, "CardModel");
            if (model == null) continue;
            // Bears can breach any open container (NotSafeFromAnimals OR NotSafeFromBears).
            // Regular animals only raid NotSafeFromAnimals containers.
            bool tagMatch = HasTag(model, NotSafeTagName)
                || (bearRaid && HasTag(model, NotSafeBearTagName));
            if (!tagMatch) continue;
            if (CountFoodIn(c) <= 0) continue;
            candidates.Add(c);
        }
        return candidates;
    }

    private static bool RaidOnce(bool bearRaid)
    {
        var candidates = FindRaidCandidates(bearRaid);
        if (candidates.Count == 0)
        {
            Log.Debug($"[WildlifeRaid] raid roll succeeded (bear={bearRaid}) but no eligible containers in player env.");
            return false;
        }

        var container = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        var foodCard = PickRandomFoodInside(container);
        if (foodCard == null) return false;

        var containerName = GetCardDisplayName(container) ?? "a container";
        var foodName = GetCardDisplayName(foodCard) ?? "some food";

        if (TransformToRotten(foodCard))
        {
            string raider = bearRaid ? "A bear" : "Wildlife";
            string timing = bearRaid ? "during the encounter" : "overnight";
            Log.Info($"[WildlifeRaid] {raider} raided {containerName} {timing} — {foodName} was spoiled.");
            ApplyStress(StressPenalty);
            return true;
        }
        return false;
    }

    private static bool HasTag(object cardDataModel, string tagName)
    {
        try
        {
            var tagsField = cardDataModel.GetType().GetField("CardTags", Flags);
            if (tagsField == null) return false;
            if (!(tagsField.GetValue(cardDataModel) is IList list)) return false;
            foreach (var t in list)
            {
                if (t is UnityEngine.Object uo && uo.name == tagName) return true;
            }
        }
        catch (Exception ex) { Log.Debug($"[WildlifeRaid] HasTag reflection failed ({ex.GetType().Name})"); }
        return false;
    }

    private static int CountFoodIn(object container)
        => Api.Inventory.Count(container, FoodTagName);

    private static object PickRandomFoodInside(object container)
    {
        var foods = Api.Inventory.Find(container, FoodTagName);
        if (foods.Count == 0) return null;
        return foods[UnityEngine.Random.Range(0, foods.Count)];
    }

    // ----------------------------------------------------------- effects ---

    private static bool TransformToRotten(object card)
    {
        var rotten = GameRegistry.GetByUid(RottenRemainsUID);
        if (rotten == null)
        {
            Log.Debug("[WildlifeRaid] RottenRemains UID not resolvable — skipping transform.");
            return false;
        }

        // CardUtil.TransformCardInPlace goes through SetModel, so visuals, liquid info and the
        // board's per-model buckets follow the new model. Before 2.26.6 this swapped CardModel
        // raw, then invoked ResetCard with no arguments; on EA 0.68 ResetCard takes a float and
        // returns an IEnumerator, so the call threw and left a half-transformed card.
        return CardUtil.TransformCardInPlace(card, RottenRemainsUID);
    }

    // GameManager.ChangeStatValue(InGameStat, float, StatModification) is the private coroutine
    // vanilla itself starts for a permanent stat change (stat transfers, EA 0.68b GameManager.cs
    // 6914-6915): it clamps, updates statuses and fires the stat's triggers. Before 2.26.8 this
    // looked for GameManager.GetStat / FindStat, which do not exist, so the raid's stress penalty
    // was never applied and nothing said so.
    private static readonly MethodInfo _changeStatValue = AccessTools.Method(typeof(GameManager), "ChangeStatValue",
        new[] { typeof(InGameStat), typeof(float), typeof(StatModification) });
    private static readonly HashSet<string> _stressWarned = new();

    private static void ApplyStress(float amount)
    {
        if (amount <= 0f) return;
        var gm = MBSingleton<GameManager>.Instance;
        if (!gm) { WarnStressOnce("gm", "GameManager.Instance is null"); return; }
        if (_changeStatValue == null || _changeStatValue.ReturnType != typeof(IEnumerator))
        {
            WarnStressOnce("method", "GameManager.ChangeStatValue(InGameStat, float, StatModification) returning IEnumerator not found");
            return;
        }
        if (!(GameRegistry.GetByUid(StressStatUID) is GameStat stressDef))
        {
            WarnStressOnce("def", $"GameStat '{StressStatUID}' (Stress) not found");
            return;
        }
        if (gm.StatsDict == null || !gm.StatsDict.TryGetValue(stressDef, out var stress) || !stress)
        {
            WarnStressOnce("instance", "no live InGameStat for Stress in GameManager.StatsDict");
            return;
        }
        try
        {
            // An IEnumerator method does nothing until it is started: a bare Invoke only builds it.
            var routine = (IEnumerator)_changeStatValue.Invoke(gm, new object[] { stress, amount, StatModification.Permanent });
            gm.StartCoroutine(routine);
        }
        catch (Exception ex) { WarnStressOnce("invoke", $"ChangeStatValue failed: {Log.ExceptionText(ex)}"); }
    }

    private static void WarnStressOnce(string cause, string message)
    {
        if (_stressWarned.Add(cause)) Log.Warn($"[WildlifeRaid] raid stress penalty not applied: {message}.");
    }

    private static List<object> CollectAllCards()
    {
        var cards = new List<object>();
        if (_gameManagerInstanceProp == null) return cards;

        try
        {
            var gm = _gameManagerInstanceProp.GetValue(null, null);
            if (gm == null) return cards;

            var allCards = GetMemberValue(gm, "AllCards") as IList;
            if (allCards == null)
            {
                Log.Debug("[WildlifeRaid] GameManager.AllCards unavailable; raid scan skipped");
                return cards;
            }

            foreach (var card in allCards)
            {
                if (card != null) cards.Add(card);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[WildlifeRaid] failed to read GameManager.AllCards: {Log.ExceptionText(ex)}");
        }

        return cards;
    }

    // ----------------------------------------------------------- helpers ---

    private static bool IsInPlayerEnv(object card)
    {
        var env = GetMemberValue(card, "CardEnvironment");
        if (env == null) return false;
        var val = GetMemberValue(env, "MatchesPlayerEnv");
        return val is bool b && b;
    }

    private static string GetCardDisplayName(object card)
    {
        var model = GetMemberValue(card, "CardModel");
        if (model == null) return null;
        var name = GetMemberValue(model, "CardName");
        if (name == null) return null;
        var def = GetMemberValue(name, "DefaultText") as string;
        return !string.IsNullOrEmpty(def) ? def : null;
    }

    private static readonly Dictionary<(Type, string), MemberInfo> _memberCache = new();

    private static object GetMemberValue(object target, string name)
    {
        if (target == null) return null;
        var t = target.GetType();
        var key = (t, name);
        if (!_memberCache.TryGetValue(key, out var member))
        {
            member = (MemberInfo)t.GetProperty(name, Flags) ?? t.GetField(name, Flags);
            _memberCache[key] = member;
        }
        if (member is PropertyInfo pi && pi.CanRead) { try { return pi.GetValue(target, null); } catch (Exception ex) { Log.Debug($"[WildlifeRaid] '{name}' property read threw: {Log.ExceptionText(ex)}"); } }
        if (member is FieldInfo fi) { try { return fi.GetValue(target); } catch (Exception ex) { Log.Debug($"[WildlifeRaid] '{name}' field read threw: {Log.ExceptionText(ex)}"); } }
        return null;
    }
}
