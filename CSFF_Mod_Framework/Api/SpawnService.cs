using CSFFModFramework.Util;

namespace CSFFModFramework.Api;

/// <summary>
/// Framework-owned card spawning (Centralization Tier 2, P4). Replaces the per-mod GiveCard
/// reflection chains (WDI, CMC remainder shard, Sirus).
///
/// <para><b>Direct spawn</b>: resolve and spawn through the game's <c>GameManager.GiveCard</c>
/// (CLAUDE.md, Runtime Card Spawning). <c>GiveCard</c> returns void on the live game, so
/// <c>Spawn</c> returns null even on success.</para>
///
/// <para><b>Stat overrides.</b> Since 2.28.0 <c>Spawn</c>'s <c>statOverrides</c> ARE applied:
/// such a spawn goes through vanilla's private <c>GameManager.AddCard</c> with a
/// <c>TransferedDurabilities</c> (<see cref="SpawnWithStats"/>), the path every vanilla drop
/// takes. <c>OnNextSpawn</c> and a card's JSON <c>SpawnStatDefaults</c> are still accepted and
/// dropped (a Debug line), because they target spawns the game makes itself, where no hook here
/// holds the new instance; for a vanilla drop use the drop's own <c>TransferRules</c>
/// (<c>SetValue</c> plus <c>*Value</c>) instead.</para>
///
/// <para><b>Spawn event</b>: <see cref="CardSpawned"/> fires for every card the game creates
/// during play, from vanilla's <c>GameManager.OnCardSpawned</c>.</para>
///
/// <para>Stat names accept JSON-side names ("SpoilageTime", "SpecialDurability4") or
/// runtime names ("CurrentSpoilage"); see <c>CardUtil.SetDurability</c>.</para>
/// </summary>
public static class SpawnService
{
    // GameManager.GiveCard is a STATIC method in EA 0.64f (Void GiveCard(CardData, bool)).
    // BindingFlags.Static is REQUIRED — without it GetMethods never returns GiveCard and the
    // resolver reports "inactive" even though the type and method both exist. (This was the
    // root cause of the Village Path CT8 not spawning; see Retrospectives/village-path-east-travel.md.)
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static
                                     | BindingFlags.Public | BindingFlags.NonPublic;

    private sealed class Pending
    {
        public string Uid;
        public Dictionary<string, float> Overrides;
        public int Remaining;
        public int ExpiresAtFrame;
    }

    private static readonly List<Pending> _pending = new();

    // Stat defaults registered by SpawnStatDefaultsLoader (from SpawnStatDefaults blocks
    // in mod card JSON). Applied to every spawn of the matching UID before pending overrides,
    // so explicit OnNextSpawn calls take priority.
    private static readonly Dictionary<string, Dictionary<string, float>> _statDefaults
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Register stat defaults for <paramref name="uid"/>. Called by
    /// <c>SpawnStatDefaultsLoader</c> during load; do not call at runtime.
    /// </summary>
    internal static void RegisterStatDefault(string uid, Dictionary<string, float> defaults)
    {
        if (string.IsNullOrEmpty(uid) || defaults == null || defaults.Count == 0) return;
        if (_statDefaults.TryGetValue(uid, out var existing))
            foreach (var kvp in defaults) existing[kvp.Key] = kvp.Value;
        else
            _statDefaults[uid] = new Dictionary<string, float>(defaults, StringComparer.Ordinal);
    }

    private static bool _resolveAttempted;
    private static MethodInfo _giveCardMethod;
    private static bool _patchAttempted;
    private static bool _patched;

    // ── Spawn event ──────────────────────────────────────────────────────────

    private static event Action<object, string> _cardSpawned;

    /// <summary>
    /// Raised for every card the game creates during play (args: the in-game card, its
    /// UniqueID), from vanilla's <c>GameManager.OnCardSpawned</c>, which <c>AddCard</c> invokes
    /// for drops, blueprint results, transforms and <c>GiveCard</c> spawns alike. Before 2.26.6
    /// this came from a <c>GiveCard</c> postfix, and vanilla calls <c>GiveCard</c> only from the
    /// cheat menu, so the event fired for mod spawns alone and passed the CardData template.
    /// </summary>
    public static event Action<object, string> CardSpawned
    {
        add { _cardSpawned += value; EnsureCardSpawnedHook(); }
        remove { _cardSpawned -= value; }
    }

    // ── Direct spawn ─────────────────────────────────────────────────────────

    /// <summary>
    /// Spawns a card by UniqueID onto the current board. On the live game this returns null
    /// even on success (<c>GiveCard</c> returns void). Since 2.28.0 <paramref name="statOverrides"/>
    /// ARE applied: a spawn that carries them goes through vanilla's own drop path
    /// (<see cref="SpawnWithStats"/>) instead of <c>GiveCard</c>. Every failure path is logged
    /// (CLAUDE.md: always log at null checks in spawn chains).
    /// </summary>
    public static object Spawn(string uid, IDictionary<string, float> statOverrides = null)
    {
        if (string.IsNullOrEmpty(uid)) return null;
        var cardData = CardUtil.GetCardDataById(uid);
        if (cardData == null)
        {
            Log.Error($"[SpawnService] CardData not found for '{uid}' — spawn aborted.");
            return null;
        }
        return Spawn(cardData, statOverrides);
    }

    /// <summary>
    /// Spawns a card from a resolved CardData. Same return value and override behaviour as
    /// the UniqueID overload.
    /// </summary>
    public static object Spawn(object cardData, IDictionary<string, float> statOverrides = null)
    {
        if (cardData == null) return null;

        // 2.28.0: overrides go through the vanilla drop path, which applies them to the new card
        // (GiveCard has no durability parameter and returns no instance). On any failure there
        // the card is still spawned below through GiveCard, at its defaults, with a Warn.
        if (statOverrides != null && statOverrides.Count > 0 && cardData is CardData typedData)
        {
            var gmForTick = MBSingleton<GameManager>.Instance;
            var tick = gmForTick != null ? new Vector2Int(gmForTick.CurrentTickInfo.z, 0) : Vector2Int.zero;
            if (SpawnWithStats(typedData, statOverrides, tick))
                return null;
            Log.Warn($"[SpawnService] '{typedData.UniqueID}' spawned at its default stats: the stat overrides could not be applied.");
            statOverrides = null;
        }

        var giveCard = ResolveGiveCard();
        if (giveCard == null)
        {
            Log.Error("[SpawnService] GameManager.GiveCard not resolved — spawn aborted.");
            return null;
        }

        // GiveCard is static in EA 0.64f, so the instance is ignored by Invoke. Still try to
        // resolve it (harmless, and forward-compatible if a future version makes it instance).
        var gm = CardUtil.GetGameManagerInstance();
        if (gm == null && !giveCard.IsStatic)
        {
            Log.Error("[SpawnService] GameManager.Instance is null — spawn aborted.");
            return null;
        }

        object spawned;
        try
        {
            var ps = giveCard.GetParameters();
            var args = new object[ps.Length];
            args[0] = cardData;
            for (int i = 1; i < ps.Length; i++)
            {
                var pt = ps[i].ParameterType;
                // bool params default to true — matches the proven mod-side GiveCard(data, true)
                // calls (CMC shard spawn, ACT Grind All, WDI sluice outputs).
                args[i] = pt == typeof(bool) ? true
                        : pt.IsValueType ? Activator.CreateInstance(pt) : null;
            }
            spawned = giveCard.Invoke(giveCard.IsStatic ? null : gm, args);
        }
        catch (Exception ex)
        {
            var inner = ex is TargetInvocationException tie ? tie.InnerException ?? ex : ex;
            Log.Error($"[SpawnService] GiveCard threw: {Log.ExceptionText(inner)}");
            return null;
        }

        // EA 0.64f GiveCard returns void: the card is placed on the board as a side effect and
        // Invoke returns null. That is SUCCESS, not failure — do not treat null as an error.
        // We have no spawned-instance handle, so stat overrides can't be applied to it here
        // (known gap — see retrospective). Callers that spawn purely for the side effect
        // (e.g. WorldMapInjector's CT8 travel target) ignore the return value.
        bool returnsVoid = giveCard.ReturnType == typeof(void);
        if (spawned == null)
        {
            if (!returnsVoid)
            {
                Log.Warn("[SpawnService] GiveCard returned null — card may not have spawned.");
                return null;
            }
            if (statOverrides != null && statOverrides.Count > 0)
                Log.Warn("[SpawnService] GiveCard returns void in this game version — stat overrides "
                       + "were NOT applied to the spawned card (no return instance). Apply via the "
                       + "CardSpawned event or a pre/post ID-diff if overrides are required.");
            return null;
        }

        ApplyOverrides(spawned, statOverrides);
        return spawned;
    }

    // ── Spawn with explicit stats (the vanilla drop path) ────────────────────

    /// <summary>The JSON-side stat names <see cref="SpawnWithStats"/> accepts, in
    /// <c>TransferedDurabilities</c> slot order.</summary>
    internal static readonly string[] StatNames =
    {
        "SpoilageTime", "UsageDurability", "FuelCapacity", "Progress",
        "SpecialDurability1", "SpecialDurability2", "SpecialDurability3", "SpecialDurability4",
    };

    private static MethodInfo _addCardMethod;
    private static object _specialDropNone;
    private static bool _addCardResolveAttempted;

    /// <summary>
    /// Spawns <paramref name="data"/> on the current board with the named durability values, through
    /// the same private <c>GameManager.AddCard</c> coroutine vanilla uses for every drop. The values
    /// travel in a <c>TransferedDurabilities</c> whose named slots are active and every other slot
    /// inactive; <c>InGameCardBase.Init</c> first generates the card's normal starting values and
    /// then overwrites only the active slots (clamped to the stat's max, snapped to its valid values),
    /// so every stat not named keeps its default, random offset included. A name for a stat the card
    /// does not have active is ignored by the engine. Stat names are the JSON-side
    /// <see cref="StatNames"/>. Returns false, with a Warn, when the spawn could not be started;
    /// the caller then decides whether to fall back to a plain spawn. Liquids are refused, because
    /// this path would set their liquid quantity to 0.
    /// </summary>
    internal static bool SpawnWithStats(CardData data, IDictionary<string, float> stats, Vector2Int tick)
    {
        if (data == null) return false;
        if (data.CardType == CardTypes.Liquid)
        {
            Log.Warn($"[SpawnService] SpawnWithStats refuses liquid '{data.UniqueID}'.");
            return false;
        }

        var gm = MBSingleton<GameManager>.Instance;
        if (gm == null)
        {
            Log.Warn($"[SpawnService] SpawnWithStats('{data.UniqueID}'): GameManager.Instance is null.");
            return false;
        }

        var addCard = ResolveAddCard();
        if (addCard == null) return false;

        var transfer = BuildTransfer(data, stats);

        try
        {
            // Mirrors the argument list GameManager.GiveCard and GetStartingCardsFromArray pass,
            // with the transfer in place of their null.
            var args = new object[]
            {
                data, null, true, _specialDropNone, transfer, null, null, null, true,
                SpawningLiquid.DefaultLiquid, tick, default(EnvDictKey), InGameNPCOrPlayer.Null,
                null, true, null, null,
            };
            if (addCard.Invoke(gm, args) is not IEnumerator routine)
            {
                Log.Warn($"[SpawnService] SpawnWithStats('{data.UniqueID}'): AddCard returned no coroutine.");
                return false;
            }
            // AddCard is an iterator: invoking it only builds the state machine, so it must be
            // started or nothing spawns (CLAUDE.md, Runtime Card Removal: coroutine rule).
            gm.StartCoroutine(routine);
            return true;
        }
        catch (Exception ex)
        {
            var inner = ex is TargetInvocationException tie ? tie.InnerException ?? ex : ex;
            Log.Warn($"[SpawnService] SpawnWithStats('{data.UniqueID}') threw: {Log.ExceptionText(inner)}");
            return false;
        }
    }

    private static OptionalFloatValue Inactive() => new OptionalFloatValue(_Active: false, 0f);

    // Named slots active with their value, every other slot inactive (Init then keeps that stat's
    // own starting value). Liquid 0 is only read for a liquid card, which both callers refuse.
    private static TransferedDurabilities BuildTransfer(CardData data, IDictionary<string, float> stats)
    {
        var transfer = new TransferedDurabilities
        {
            Spoilage = Inactive(), Usage = Inactive(), Fuel = Inactive(), ConsumableCharges = Inactive(),
            Special1 = Inactive(), Special2 = Inactive(), Special3 = Inactive(), Special4 = Inactive(),
            Liquid = 0f,
        };
        if (stats == null) return transfer;
        foreach (var kvp in stats)
        {
            var value = new OptionalFloatValue(_Active: true, kvp.Value);
            switch (kvp.Key)
            {
                case "SpoilageTime": transfer.Spoilage = value; break;
                case "UsageDurability": transfer.Usage = value; break;
                case "FuelCapacity": transfer.Fuel = value; break;
                case "Progress": transfer.ConsumableCharges = value; break;
                case "SpecialDurability1": transfer.Special1 = value; break;
                case "SpecialDurability2": transfer.Special2 = value; break;
                case "SpecialDurability3": transfer.Special3 = value; break;
                case "SpecialDurability4": transfer.Special4 = value; break;
                default:
                    Log.Warn($"[SpawnService] '{data.UniqueID}': unknown stat '{kvp.Key}' ignored "
                           + $"(expected one of {string.Join(", ", StatNames)}).");
                    break;
            }
        }
        return transfer;
    }

    // ── Spawn into the player's equipment with explicit stats ────────────────

    private static MethodInfo _addCardSlotMethod;
    private static bool _addCardSlotResolveAttempted;

    /// <summary>True when <see cref="SpawnEquippedWithStats"/> can run: the private slot overload of
    /// <c>GameManager.AddCard</c> resolved. Callers that must hand an item back to vanilla on failure
    /// check this BEFORE taking the item away.</summary>
    internal static bool CanSpawnEquippedWithStats => ResolveAddCardSlot() != null;

    /// <summary>
    /// Spawns <paramref name="data"/> into the player's equipment with the named durability values,
    /// through the private <c>GameManager.AddCard</c> slot overload with exactly the arguments
    /// vanilla's own perk <c>EquippedCards</c> loop passes in <c>InitializePlayerCharacter</c>
    /// (<c>SlotInfo(Equipment, -2)</c>, current env, default inventory), plus the transfer in place
    /// of its null. The drop overload <see cref="SpawnWithStats"/> uses cannot do this: its special
    /// slot is only read when there is a source card. Liquids are refused, as there.
    /// </summary>
    internal static bool SpawnEquippedWithStats(CardData data, IDictionary<string, float> stats)
    {
        if (data == null) return false;
        if (data.CardType == CardTypes.Liquid)
        {
            Log.Warn($"[SpawnService] SpawnEquippedWithStats refuses liquid '{data.UniqueID}'.");
            return false;
        }
        var gm = MBSingleton<GameManager>.Instance;
        if (gm == null)
        {
            Log.Warn($"[SpawnService] SpawnEquippedWithStats('{data.UniqueID}'): GameManager.Instance is null.");
            return false;
        }
        var addCard = ResolveAddCardSlot();
        if (addCard == null) return false;

        try
        {
            var ps = addCard.GetParameters();
            var args = new object[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                var p = ps[i];
                switch (p.Name)
                {
                    case "_Data": args[i] = data; break;
                    case "_Slot": args[i] = new SlotInfo(SlotsTypes.Equipment, -2); break;
                    case "_FromEnvironment": args[i] = EnvID.Empty; break;
                    case "_InCurrentEnv": args[i] = true; break;
                    // Null stats = vanilla's own null transfer (the card's default stats).
                    case "_TransferedDurabilities": args[i] = stats == null ? null : BuildTransfer(data, stats); break;
                    case "_FromPosition": args[i] = Vector3.zero; break;
                    case "_UseDefaultInventory": args[i] = true; break;
                    case "_WithLiquid": args[i] = SpawningLiquid.DefaultLiquid; break;
                    case "_MoveView": args[i] = false; break;
                    case "_Pinned": args[i] = false; break;
                    case "_Tick": args[i] = Vector2Int.zero; break;
                    case "_TravelTargetKey": args[i] = default(EnvDictKey); break;
                    case "_Ownership": args[i] = CardOwnershipData.Null; break;
                    case "_User": args[i] = InGameNPCOrPlayer.Null; break;
                    default: args[i] = DefaultFor(p); break;
                }
            }
            if (addCard.Invoke(gm, args) is not IEnumerator routine)
            {
                Log.Warn($"[SpawnService] SpawnEquippedWithStats('{data.UniqueID}'): AddCard returned no coroutine.");
                return false;
            }
            gm.StartCoroutine(routine);
            return true;
        }
        catch (Exception ex)
        {
            var inner = ex is TargetInvocationException tie ? tie.InnerException ?? ex : ex;
            Log.Warn($"[SpawnService] SpawnEquippedWithStats('{data.UniqueID}') threw: {Log.ExceptionText(inner)}");
            return false;
        }
    }

    // Vanilla passes null (or its declared default) for every argument not named above.
    private static object DefaultFor(ParameterInfo p)
    {
        if (p.HasDefaultValue)
        {
            var v = p.DefaultValue;
            if (v != null && p.ParameterType.IsEnum && v.GetType() != p.ParameterType)
                return Enum.ToObject(p.ParameterType, v);
            if (v == null && p.ParameterType.IsValueType)
                return Activator.CreateInstance(p.ParameterType);
            return v;
        }
        return p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null;
    }

    // GameManager.AddCard(CardData _Data, SlotInfo _Slot, EnvID _FromEnvironment, ...,
    // TransferedDurabilities _TransferedDurabilities, ...): the 29-parameter overload vanilla calls
    // for StartingClothes and perk EquippedCards. Matched by parameter NAME as well as type, so a
    // reordering fails to resolve (and is logged) instead of silently passing values into the
    // wrong slots.
    private static readonly string[] SlotOverloadRequiredNames =
    {
        "_Data", "_Slot", "_FromEnvironment", "_InCurrentEnv", "_TransferedDurabilities", "_FromPosition",
        "_UseDefaultInventory", "_WithLiquid", "_MoveView", "_Pinned", "_Tick", "_TravelTargetKey",
        "_Ownership", "_User",
    };

    private static MethodInfo ResolveAddCardSlot()
    {
        if (_addCardSlotResolveAttempted) return _addCardSlotMethod;
        _addCardSlotResolveAttempted = true;
        try
        {
            _addCardSlotMethod = typeof(GameManager)
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .FirstOrDefault(m =>
                {
                    if (m.Name != "AddCard") return false;
                    var ps = m.GetParameters();
                    if (ps.Length < SlotOverloadRequiredNames.Length
                        || ps[0].ParameterType != typeof(CardData)
                        || ps[1].ParameterType != typeof(SlotInfo)) return false;
                    var names = ps.Select(p => p.Name).ToList();
                    if (SlotOverloadRequiredNames.Any(n => !names.Contains(n))) return false;
                    return ps[names.IndexOf("_TransferedDurabilities")].ParameterType == typeof(TransferedDurabilities)
                        && ps[names.IndexOf("_Slot")].ParameterType == typeof(SlotInfo);
                });
            if (_addCardSlotMethod == null)
                Log.Warn("[SpawnService] GameManager.AddCard (slot overload) not found; equipped items with stats are given at default stats.");
        }
        catch (Exception ex)
        {
            Log.Warn($"[SpawnService] AddCard slot overload resolution failed: {Log.ExceptionText(ex)}");
            _addCardSlotMethod = null;
        }
        return _addCardSlotMethod;
    }

    // GameManager.AddCard(CardData, InGameCardBase, bool, SpecialDrop, TransferedDurabilities,
    // BlueprintSaveData, List<FlavourAndIntensityNumber>, List<SpiceSaveData>, bool, SpawningLiquid,
    // Vector2Int, EnvDictKey, InGameNPCOrPlayer, SlotInfo, bool, NPCTrackingInfo,
    // List<InGameCardBase>): private, and SpecialDrop is a private nested enum, so it is resolved
    // by reflection. Two other overloads exist (the save-load one and a 29-parameter one); this is
    // the only one whose 5th parameter is a TransferedDurabilities and that has 17 parameters.
    private static MethodInfo ResolveAddCard()
    {
        if (_addCardResolveAttempted) return _addCardMethod;
        _addCardResolveAttempted = true;
        try
        {
            var specialDropType = typeof(GameManager).GetNestedType("SpecialDrop", BindingFlags.NonPublic | BindingFlags.Public);
            if (specialDropType == null)
            {
                Log.Warn("[SpawnService] GameManager.SpecialDrop not found; spawns with stats fall back to default stats.");
                return null;
            }
            _specialDropNone = Enum.ToObject(specialDropType, 0);

            _addCardMethod = typeof(GameManager)
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .FirstOrDefault(m =>
                {
                    if (m.Name != "AddCard") return false;
                    var ps = m.GetParameters();
                    return ps.Length == 17
                        && ps[0].ParameterType == typeof(CardData)
                        && ps[3].ParameterType == specialDropType
                        && ps[4].ParameterType == typeof(TransferedDurabilities)
                        && ps[10].ParameterType == typeof(Vector2Int);
                });
            if (_addCardMethod == null)
                Log.Warn("[SpawnService] GameManager.AddCard (17-parameter drop overload) not found; spawns with stats fall back to default stats.");
        }
        catch (Exception ex)
        {
            Log.Warn($"[SpawnService] AddCard resolution failed: {Log.ExceptionText(ex)}");
            _addCardMethod = null;
        }
        return _addCardMethod;
    }

    // ── Queued overrides for game-side spawns ────────────────────────────────

    /// <summary>
    /// Queues stat overrides for the next <paramref name="count"/> spawns of
    /// <paramref name="uid"/> (ProducedCards, OnFull, perk kits — any GiveCard path).
    /// Entries expire after <paramref name="ttlFrames"/> rendered frames so a spawn
    /// that never happens cannot leak onto an unrelated later spawn.
    /// </summary>
    public static void OnNextSpawn(string uid, IDictionary<string, float> statOverrides,
        int count = 1, int ttlFrames = 600)
    {
        if (string.IsNullOrEmpty(uid) || statOverrides == null || statOverrides.Count == 0 || count <= 0)
            return;
        EnsurePatched();
        if (!_patched)
        {
            Log.Error($"[SpawnService] GiveCard postfix unavailable — OnNextSpawn('{uid}') will never apply.");
            return;
        }
        lock (_pending)
        {
            _pending.Add(new Pending
            {
                Uid = uid,
                Overrides = new Dictionary<string, float>(statOverrides, StringComparer.Ordinal),
                Remaining = count,
                ExpiresAtFrame = Time.frameCount + Math.Max(1, ttlFrames),
            });
        }
    }

    // ── GiveCard resolution + patch ──────────────────────────────────────────

    private static MethodInfo ResolveGiveCard()
    {
        if (_resolveAttempted) return _giveCardMethod;
        _resolveAttempted = true;

        // IMPORTANT: Do NOT use ReflectionCache.FindType or AccessTools.TypeByName here.
        // Both scan all loaded assemblies by type name and may return a mod's shadowing
        // "GameManager" class (e.g., ModCore 3.3.1) that has either no GiveCard at all or a
        // GiveCard with incompatible parameter types. The only safe approach is to resolve
        // both GameManager and CardData directly from Assembly-CSharp, guaranteeing they come
        // from the same assembly and match the game's actual runtime types.
        var asmCSharp = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
        if (asmCSharp == null)
        {
            // Last-ditch: try firstpass (some game versions split the assembly).
            asmCSharp = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp-firstpass");
        }
        if (asmCSharp == null)
        {
            Log.Warn("[SpawnService] Assembly-CSharp not found — SpawnService inactive.");
            return null;
        }

        Type gmType = null;
        Type cardDataType = null;
        try
        {
            foreach (var t in asmCSharp.GetTypes())
            {
                if (t == null) continue;
                if (gmType == null && t.Name == "GameManager") gmType = t;
                if (cardDataType == null && t.Name == "CardData") cardDataType = t;
                if (gmType != null && cardDataType != null) break;
            }
        }
        catch (ReflectionTypeLoadException rtle)
        {
            foreach (var t in rtle.Types ?? Array.Empty<Type>())
            {
                if (t == null) continue;
                if (gmType == null && t.Name == "GameManager") gmType = t;
                if (cardDataType == null && t.Name == "CardData") cardDataType = t;
            }
        }

        Log.Debug($"[SpawnService] Assembly-CSharp scan: GameManager={gmType?.FullName ?? "null"}, CardData={cardDataType?.FullName ?? "null"}");

        if (gmType == null)
        {
            Log.Warn("[SpawnService] GameManager not found in Assembly-CSharp — SpawnService inactive.");
            return null;
        }

        // Exact CardData first param (preferred), then name-contains fallback for resilience.
        if (cardDataType != null)
        {
            _giveCardMethod = gmType.GetMethods(Flags).FirstOrDefault(m =>
                m.Name == "GiveCard" && !m.IsGenericMethod
                && m.GetParameters().Length >= 1
                && m.GetParameters()[0].ParameterType == cardDataType);
        }
        _giveCardMethod ??= gmType.GetMethods(Flags)
            .Where(m => m.Name == "GiveCard" && !m.IsGenericMethod)
            .OrderBy(m => m.GetParameters().Length)
            .FirstOrDefault(m => m.GetParameters().Length >= 1
                && m.GetParameters()[0].ParameterType.Name.Contains("CardData"));

        if (_giveCardMethod == null)
            Log.Warn($"[SpawnService] GameManager.GiveCard not found in Assembly-CSharp ({gmType.FullName}) — SpawnService inactive.");
        else
            Log.Debug($"[SpawnService] GiveCard resolved: {_giveCardMethod.DeclaringType?.FullName}.{_giveCardMethod.Name}({string.Join(", ", _giveCardMethod.GetParameters().Select(p => p.ParameterType.Name))})");
        return _giveCardMethod;
    }

    private static bool _cardSpawnedHooked;

    // Vanilla raises GameManager.OnCardSpawned from AddCard for every card it creates during
    // play (.decomp/GameManager.cs). It is a static delegate that vanilla only Combines and
    // Removes its own handlers on, never resets, so one subscription lasts the process.
    private static void EnsureCardSpawnedHook()
    {
        if (_cardSpawnedHooked) return;
        _cardSpawnedHooked = true;
        try
        {
            GameManager.OnCardSpawned += OnVanillaCardSpawned;
            Log.Debug("[SpawnService] subscribed to GameManager.OnCardSpawned.");
        }
        catch (Exception ex)
        {
            Log.Warn($"[SpawnService] could not subscribe to GameManager.OnCardSpawned; CardSpawned will not fire: {Log.ExceptionText(ex)}");
        }
    }

    private static void OnVanillaCardSpawned(InGameCardBase card)
    {
        var subscribers = _cardSpawned;
        if (subscribers == null || card == null) return;
        string uid;
        try { uid = CardUtil.GetCardUniqueId(card); }
        catch (Exception ex)
        {
            Log.Debug($"[SpawnService] CardSpawned: UniqueID read threw: {ex.GetType().Name} {ex.Message}");
            return;
        }
        if (uid == null) return;
        foreach (var d in subscribers.GetInvocationList())
        {
            try { ((Action<object, string>)d)(card, uid); }
            catch (Exception ex) { Log.Warn($"[SpawnService] CardSpawned subscriber threw: {Log.ExceptionText(ex)}"); }
        }
    }

    private static void EnsurePatched()
    {
        if (_patchAttempted) return;
        _patchAttempted = true;

        var giveCard = ResolveGiveCard();
        if (giveCard == null) return;
        try
        {
            var postfix = typeof(SpawnService).GetMethod(nameof(GiveCard_Postfix),
                BindingFlags.Static | BindingFlags.NonPublic);
            Plugin.Harmony.Patch(giveCard, postfix: new HarmonyMethod(postfix));
            _patched = true;
            Log.Debug("[SpawnService] GiveCard postfix applied.");
        }
        catch (Exception ex)
        {
            Log.Warn($"[SpawnService] GiveCard patch failed: {Log.ExceptionText(ex)}");
        }
    }

    // GameManager.GiveCard(CardData _Data, bool _Complete) returns VOID, so a postfix's
    // __result is always null and there is no spawned instance to apply overrides to. Since
    // 2.26.6 this postfix only expires stale OnNextSpawn entries; CardSpawned is raised from
    // vanilla's GameManager.OnCardSpawned instead (EnsureCardSpawnedHook).
    private static void GiveCard_Postfix(object __0)
    {
        if (__0 == null) return;
        string uid = null;
        try
        {
            uid = CardUtil.GetCardUniqueId(__0);
            if (uid == null) return;

            // Override application requires the SPAWNED in-game instance, which a void GiveCard
            // does not return — and __0 is the shared CardData template (mutating it would
            // corrupt every instance), so overrides are NOT applied here. We still expire stale
            // pending entries so they cannot leak, and warn once if overrides were expected.
            if (_pending.Count > 0 || _statDefaults.Count > 0)
            {
                lock (_pending)
                {
                    int frame = Time.frameCount;
                    for (int i = _pending.Count - 1; i >= 0; i--)
                        if (frame > _pending[i].ExpiresAtFrame)
                            _pending.RemoveAt(i);

                    bool hasPendingForUid = _pending.Any(p =>
                        string.Equals(uid, p.Uid, StringComparison.OrdinalIgnoreCase));
                    if (hasPendingForUid || _statDefaults.ContainsKey(uid))
                        Log.Debug($"[SpawnService] '{uid}' spawned, but stat overrides cannot be applied "
                                + "via the void GiveCard postfix in this game version (known gap).");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[SpawnService] GiveCard postfix error: {Log.ExceptionText(ex)}");
        }
    }

    private static void ApplyOverrides(object card, IDictionary<string, float> overrides)
    {
        if (overrides == null || overrides.Count == 0) return;
        foreach (var kvp in overrides)
        {
            if (!CardUtil.SetDurability(card, kvp.Key, kvp.Value))
                Log.Warn($"[SpawnService] SetDurability('{kvp.Key}', {kvp.Value}) failed on spawned "
                       + $"'{CardUtil.GetCardUniqueId(card) ?? "?"}'.");
        }
    }
}
