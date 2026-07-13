using CSFFModFramework.Util;

namespace CSFFModFramework.Api;

/// <summary>
/// Framework-owned card spawning with stat initialization (Centralization Tier 2, P4).
/// Replaces the per-mod GiveCard reflection + pending-flag GiveCard postfix chains
/// (WDI ×4, CMC remainder shard, ACT Grind All outputs, Sirus).
///
/// <para><b>Direct spawn</b> — resolve, spawn via the game's <c>GameManager.GiveCard</c>
/// (the only working spawn method, CLAUDE.md §Runtime Card Spawning), and apply stat
/// overrides on the returned card immediately (before any post-spawn time ticks):</para>
/// <code>
/// var card = SpawnService.Spawn("my_mod_item",
///     new Dictionary&lt;string,float&gt; { ["SpecialDurability4"] = 200f });
/// </code>
///
/// <para><b>Queued overrides</b> — for cards the GAME spawns (ProducedCards, OnFull,
/// perk kits), register the override before the game-side spawn happens; the framework's
/// single GiveCard postfix applies it to the next matching spawn(s):</para>
/// <code>
/// SpawnService.OnNextSpawn("my_mod_shard",
///     new Dictionary&lt;string,float&gt; { ["UsageDurability"] = 10f }, count: 3);
/// </code>
///
/// <para>Stat names accept JSON-side names ("SpoilageTime", "SpecialDurability4") or
/// runtime names ("CurrentSpoilage") — see <c>CardUtil.SetDurability</c>.</para>
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
    /// Raised from the framework's GiveCard postfix for EVERY card the game spawns
    /// (args: in-game card, its UniqueID). Subscribing applies the GiveCard patch
    /// lazily. Use to observe game-side spawns without writing your own postfix.
    /// </summary>
    public static event Action<object, string> CardSpawned
    {
        add { _cardSpawned += value; EnsurePatched(); }
        remove { _cardSpawned -= value; }
    }

    // ── Direct spawn ─────────────────────────────────────────────────────────

    /// <summary>
    /// Spawns a card by UniqueID and applies optional stat overrides. Returns the
    /// spawned in-game card, or null on failure (every failure path is logged —
    /// CLAUDE.md: always log at null checks in spawn chains).
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
    /// Spawns a card from a resolved CardData and applies optional stat overrides.
    /// Returns the spawned in-game card, or null on failure.
    /// </summary>
    public static object Spawn(object cardData, IDictionary<string, float> statOverrides = null)
    {
        if (cardData == null) return null;

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

    // GameManager.GiveCard(CardData _Data, bool _Complete) returns VOID in EA 0.64f, so a
    // postfix's __result is always null. We read the CardData ARGUMENT (__0) instead — it
    // carries the UniqueID, which is all the CardSpawned event needs. (Relying on __result
    // here was why WorldMapInjector.OnCloneEnvCardSpawned never fired and the Village Path
    // CT8 never spawned; see Retrospectives/village-path-east-travel.md.)
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

        var handler = _cardSpawned;
        if (handler == null) return;
        foreach (var d in handler.GetInvocationList())
        {
            try { ((Action<object, string>)d)(__0, uid); }
            catch (Exception ex) { Log.Warn($"[SpawnService] CardSpawned subscriber threw: {Log.ExceptionText(ex)}"); }
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
