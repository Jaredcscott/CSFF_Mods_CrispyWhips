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
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private sealed class Pending
    {
        public string Uid;
        public Dictionary<string, float> Overrides;
        public int Remaining;
        public int ExpiresAtFrame;
    }

    private static readonly List<Pending> _pending = new();

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

        var gm = CardUtil.GetGameManagerInstance();
        if (gm == null)
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
            spawned = giveCard.Invoke(gm, args);
        }
        catch (Exception ex)
        {
            var inner = ex is TargetInvocationException tie ? tie.InnerException ?? ex : ex;
            Log.Error($"[SpawnService] GiveCard threw: {Log.ExceptionText(inner)}");
            return null;
        }

        if (spawned == null)
        {
            Log.Warn("[SpawnService] GiveCard returned null — stat overrides not applied.");
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

        var gmType = Reflection.ReflectionCache.FindType("GameManager");
        var cardDataType = Reflection.ReflectionCache.FindType("CardData");
        if (gmType == null)
        {
            Log.Warn("[SpawnService] GameManager type not found.");
            return null;
        }

        // Validate we resolved the real game GameManager (not ModCore.Games etc.).
        // If no GiveCard overload is present, fall back to an explicit Assembly-CSharp scan.
        if (!gmType.GetMethods(Flags).Any(m => m.Name == "GiveCard"))
        {
            gmType = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => { var n = a.GetName().Name; return n == "Assembly-CSharp" || n == "Assembly-CSharp-firstpass"; })
                .Select(a => a.GetType("GameManager", false))
                .FirstOrDefault(t => t != null);
            if (gmType == null)
            {
                Log.Warn("[SpawnService] GameManager not found in Assembly-CSharp — SpawnService inactive.");
                return null;
            }
        }

        // Exact CardData first param, then any 2-arg non-generic overload (CMC's proven order).
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
            Log.Warn("[SpawnService] GameManager.GiveCard not found — SpawnService inactive.");
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

    private static void GiveCard_Postfix(object __result)
    {
        if (__result == null) return;
        string uid = null;
        try
        {
            uid = CardUtil.GetCardUniqueId(__result);
            if (uid == null) return;

            if (_pending.Count > 0)
            {
                lock (_pending)
                {
                    int frame = Time.frameCount;
                    for (int i = _pending.Count - 1; i >= 0; i--)
                        if (frame > _pending[i].ExpiresAtFrame)
                            _pending.RemoveAt(i);

                    for (int i = 0; i < _pending.Count; i++)
                    {
                        var p = _pending[i];
                        if (!string.Equals(uid, p.Uid, StringComparison.OrdinalIgnoreCase)) continue;
                        ApplyOverrides(__result, p.Overrides);
                        if (--p.Remaining <= 0) _pending.RemoveAt(i);
                        break;
                    }
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
            try { ((Action<object, string>)d)(__result, uid); }
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
