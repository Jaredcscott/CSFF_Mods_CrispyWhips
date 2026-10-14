using CSFFModFramework.Util;

namespace CSFFModFramework.Api;

/// <summary>
/// Accessors for a LIVE stat instance (Centralization Tier 3). Consolidates the
/// CurrentValue/MaxValue/UniqueID property-then-field fallback chains independently
/// duplicated in Community_Mod_Chest and SkillSpeedBoost (MorningBonusPatch), found by two
/// separate audits of each mod, neither aware of the other's code.
///
/// <para><b>Pass the live <c>InGameStat</c>, never the <c>GameStat</c> definition.</b> The game has
/// two types with near-identical names. <c>GameStat</c> is the ScriptableObject DEFINITION that
/// <c>UniqueIDScriptable.GetFromID</c> returns: it carries <c>BaseValue</c> and <c>MinMaxValue</c>
/// and has NO current value. <c>InGameStat</c> is the per-run MonoBehaviour that holds the value,
/// and it is reached through the definition: <c>GameManager.StatsDict[definition]</c>
/// (<c>Dictionary&lt;GameStat, InGameStat&gt;</c>). Working idiom:
/// <c>Community_Mod_Chest/Patcher/HiddenStat.cs</c> (<c>ResolveInstance</c>).</para>
///
/// <para>Handing a definition to a value accessor cannot work, and it used to fail in complete
/// silence (NaN / false, no log), which is how every C# trait effect in Community Mod Chest
/// shipped dead for months (Trait_Effect_Repair_Plan D1). Each accessor that fails on a
/// definition now says so: one warning per (definition type, member), then the SAME failure
/// value as before. <see cref="GetUniqueId"/> is the one accessor that is valid on a definition
/// and does not warn.</para>
///
/// <para>Distinct from <see cref="CardUtil"/>'s card-durability-stat accessors (those
/// target an InGameCardBase's SpecialDurability/Progress/etc. fields): this targets
/// the live stat itself (hunger, thirst, skill XP, custom mod stats, ...).</para>
///
/// <para><b>By UniqueID (2.28.0).</b> Most callers hold a stat's UniqueID, not a live instance:
/// <see cref="Resolve"/>, <see cref="Get"/>, <see cref="Set"/> and <see cref="Modify"/> do the
/// definition-to-live-instance hop for you (the idiom every consumer used to open-code), and
/// <see cref="ResolveNpc(InGameNPC, string)"/> does the same for an NPC's own stat. Reach for those
/// first; the object-taking accessors above remain for a caller that already holds the live
/// instance.</para>
/// </summary>
public static class StatAccess
{
    // (runtime type of the definition, StatAccess member) pairs already warned about.
    private static readonly HashSet<(Type, string)> _definitionWarned = new();

    /// <summary>
    /// Reads a live stat's current value. Tries SimpleCurrentValue, then CurrentValue,
    /// then CurrentBaseValue (widest fallback across observed runtime shapes).
    /// Returns <see cref="float.NaN"/> on failure (check with <see cref="float.IsNaN(float)"/>),
    /// including when handed a <c>GameStat</c> definition, which warns once.
    /// </summary>
    public static float GetCurrentValue(object stat)
    {
        if (stat == null) return float.NaN;
        if (IsDefinition(stat, nameof(GetCurrentValue), "NaN")) return float.NaN;
        if (Reflect.TryGetMember(stat, "SimpleCurrentValue", out var v) && v != null) return CardUtil.ToFloat(v);
        if (Reflect.TryGetMember(stat, "CurrentValue", out v) && v != null) return CardUtil.ToFloat(v);
        if (Reflect.TryGetMember(stat, "CurrentBaseValue", out v) && v != null) return CardUtil.ToFloat(v);
        return float.NaN;
    }

    /// <summary>
    /// Writes a live stat's current value directly (tries CurrentValue then
    /// CurrentBaseValue). Callers that need to route by ChangeStatValue's
    /// modification-type argument (GlobalModifier/AtBaseModifier/...) still own that
    /// dispatch themselves: it's patch-specific, not a general stat-access concern.
    /// Returns true on success; false when handed a <c>GameStat</c> definition, which warns once.
    /// <para>The game caches every stat condition verdict (a <c>RequiredStatValues</c> gate, a
    /// trigger range) per <c>InGameStat.ValueRevision</c> and re-evaluates only after
    /// <c>InvalidateConditionValues</c> bumps it. A raw field write does not, so without the call
    /// below every gate on the stat keeps answering from the old value (T4.23: the jail Dig stayed
    /// visible with Unguarded 0). <see cref="Reflect.SetMember"/> and
    /// <c>ReflectionHelpers.SetMemberValue</c> make that call for any write to an <c>InGameStat</c>
    /// value member (<see cref="InvalidateIfStatValue"/>), so this method and every mod's own
    /// <c>Reflect.SetMember(stat, "CurrentBaseValue", v)</c> are covered; a mod writing the field with
    /// its own <c>FieldInfo</c> must call <see cref="InvalidateConditionCache"/> itself.</para>
    /// </summary>
    public static bool SetCurrentValue(object stat, float value)
    {
        if (stat == null) return false;
        if (IsDefinition(stat, nameof(SetCurrentValue), "false and wrote nothing")) return false;
        return Reflect.SetMemberAny(stat, value, "CurrentValue", "CurrentBaseValue") != null;
    }

    // The InGameStat members a stat condition reads its value from.
    private static readonly HashSet<string> _statValueMembers = new(StringComparer.Ordinal)
    {
        "CurrentBaseValue", "GlobalModifiedValue", "AtBaseModifiedValue", "CurrentCompositeValue", "CurrentValue",
    };

    /// <summary>
    /// Called by the framework's reflection setters after a successful write: clears the stat's
    /// condition cache when <paramref name="instance"/> is a live <c>InGameStat</c> and
    /// <paramref name="member"/> holds part of its value. Any other write is ignored.
    /// </summary>
    public static void InvalidateIfStatValue(object instance, string member)
    {
        if (instance == null || member == null || !_statValueMembers.Contains(member)) return;
        if (instance.GetType().Name != "InGameStat") return;
        InvalidateConditionCache(instance);
    }

    private static Type _invalidateType;
    private static System.Reflection.MethodInfo _invalidateMethod;
    private static bool _invalidateMissingLogged;

    /// <summary>
    /// Calls the game's <c>InGameStat.InvalidateConditionValues()</c> so cached condition verdicts
    /// on this stat see a value written by reflection. A game build without that method has no
    /// such cache, so its absence is logged once at Debug and is not an error.
    /// </summary>
    public static void InvalidateConditionCache(object stat)
    {
        if (stat == null) return;
        var t = stat.GetType();
        if (t != _invalidateType)
        {
            _invalidateType = t;
            _invalidateMethod = t.GetMethod("InvalidateConditionValues",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        }
        if (_invalidateMethod == null)
        {
            if (!_invalidateMissingLogged)
            {
                _invalidateMissingLogged = true;
                Log.Debug($"StatAccess: {t.Name} has no InvalidateConditionValues() on this game build; no condition cache to clear.");
            }
            return;
        }
        try { _invalidateMethod.Invoke(stat, null); }
        catch (Exception ex) { Log.Warn($"StatAccess: InvalidateConditionValues on {t.Name} threw: {Log.ExceptionText(ex)}"); }
    }

    /// <summary>
    /// Adds <paramref name="delta"/> to a live stat's current value, clamped to
    /// [0, <see cref="GetMaxValue"/>] (uncapped above if the max can't be resolved).
    /// Returns the new value, or <see cref="float.NaN"/> if the current value couldn't be read,
    /// including when handed a <c>GameStat</c> definition, which warns once and writes nothing.
    /// </summary>
    public static float ModifyCurrentValue(object stat, float delta)
    {
        // Checked here, under this member's own name, so the one warning names the call the mod
        // actually made instead of the GetCurrentValue it fans out to.
        if (stat != null && IsDefinition(stat, nameof(ModifyCurrentValue), "NaN and wrote nothing")) return float.NaN;
        var current = GetCurrentValue(stat);
        if (float.IsNaN(current)) return float.NaN;
        var max = GetMaxValue(stat);
        var next = Mathf.Clamp(current + delta, 0f, float.IsNaN(max) ? float.MaxValue : max);
        SetCurrentValue(stat, next);
        return next;
    }

    /// <summary>
    /// Reads a live stat's maximum value. Tries the live CurrentMinMaxValue.y (Vector2),
    /// then MaxValue, then the StatModel/Stat member's MinMaxValue.y. Returns
    /// <see cref="float.NaN"/> on failure (callers commonly treat NaN as uncapped:
    /// <c>float.IsNaN(max) ? float.MaxValue : max</c>), including when handed a <c>GameStat</c>
    /// definition, which warns once. The live maximum moves with perk Min/MaxValueModifiers, so
    /// the definition's own <c>MinMaxValue</c> is deliberately not substituted for it.
    /// </summary>
    public static float GetMaxValue(object stat)
    {
        if (stat == null) return float.NaN;
        if (IsDefinition(stat, nameof(GetMaxValue), "NaN")) return float.NaN;

        if (Reflect.TryGetMember(stat, "CurrentMinMaxValue", out var minMax)
            && TryGetVectorY(minMax, out var liveMax) && liveMax > 0f)
            return liveMax;

        if (Reflect.TryGetMember(stat, "MaxValue", out var mv) && mv != null)
            return CardUtil.ToFloat(mv);

        var model = GetStatModel(stat);
        if (model != null && Reflect.TryGetMember(model, "MinMaxValue", out var modelMinMax)
            && TryGetVectorY(modelMinMax, out var modelMax) && modelMax > 0f)
            return modelMax;

        return float.NaN;
    }

    /// <summary>
    /// Resolves the JSON-side stat definition (<c>StatModel</c> or <c>Stat</c> member)
    /// referenced by a live stat instance. Returns null on failure, including when handed a
    /// <c>GameStat</c> definition (it has no such member: it IS the definition), which warns once.
    /// </summary>
    public static object GetStatModel(object stat)
    {
        if (stat == null) return null;
        if (IsDefinition(stat, nameof(GetStatModel), "null")) return null;
        return Reflect.GetMember(stat, "StatModel", "Stat");
    }

    /// <summary>
    /// Reads a stat's UniqueID: checked on the object itself first, then its
    /// StatModel/Stat member. Returns null on failure. Valid on a live stat AND on a
    /// <c>GameStat</c> definition (the definition owns the UniqueID), so this is the one
    /// accessor with no definition guard.
    /// </summary>
    public static string GetUniqueId(object stat)
    {
        if (stat == null) return null;
        if (Reflect.TryGetMember(stat, "UniqueID", out var uid) && uid is string s && !string.IsNullOrEmpty(s))
            return s;
        // A definition that reaches this line has an empty UniqueID and no StatModel to try:
        // return null without routing through GetStatModel's guard, which would warn about a
        // call the mod never made.
        if (stat is GameStat) return null;
        var model = GetStatModel(stat);
        return model != null && Reflect.TryGetMember(model, "UniqueID", out uid) ? uid as string : null;
    }

    // ───────────────────────────── By UniqueID (2.28.0) ─────────────────────────────
    #region By UniqueID

    // Per-run cache: stat UniqueID -> the live InGameStat of the CURRENT run. It is cleared at
    // GameManager.OnGMInitialized (every run start, so every save reload) and whenever the GameManager
    // is not the instance the cache was filled for, so a reference from a previous run is never handed
    // back: each run builds its own InGameStats, and a cached one would read and write a dead object.
    // Failures are never cached.
    private static readonly Dictionary<string, InGameStat> _liveByUid = new(StringComparer.Ordinal);
    private static GameManager _cacheOwner;
    private static bool _runResetSubscribed;

    // One warning per (cause, subject) for the life of the process. Every failure exit of a by-UID member
    // goes through WarnOnce, or is a call to a TryX helper whose own exits do; the shape gate
    // Development_Tools/Tests/Framework-StatAccessByUid.Tests.ps1 reads this region for exactly that.
    private static readonly HashSet<string> _byUidWarned = new(StringComparer.Ordinal);

    private static void WarnOnce(string cause, string subject, string message)
    {
        if (_byUidWarned.Add(cause + ":" + subject)) Log.Warn(message);
    }

    /// <summary>
    /// The live <c>InGameStat</c> of the stat with this UniqueID in the CURRENT run, or null. This is the
    /// hop every consumer used to open-code (<c>UniqueIDScriptable.GetFromID</c> for the
    /// <c>GameStat</c> definition, then <c>GameManager.StatsDict[definition]</c>). Works for a vanilla
    /// stat by GUID and for a mod stat by its UniqueID.
    ///
    /// <para>Cached per run, so a poll costs a dictionary read. The instance belongs to this run only:
    /// use it now, do not store it across a save reload (call again after one). Returns null, with one
    /// warning per stat and cause, when the UID is empty, no run is in progress, no <c>GameStat</c> has
    /// that UID, or the stat has no live instance this run. Never throws.</para>
    /// </summary>
    public static InGameStat Resolve(string statUid)
    {
        return TryResolve(statUid, out _, out var live) ? live : null;
    }

    /// <summary>
    /// The stat's current value (<c>InGameStat.SimpleCurrentValue</c>: base plus the active modifiers,
    /// clamped to the live range), by UniqueID. <see cref="float.NaN"/> on failure (check with
    /// <see cref="float.IsNaN(float)"/>), with the same one-warning-per-stat-and-cause reporting as
    /// <see cref="Resolve"/>.
    /// </summary>
    public static float Get(string statUid)
    {
        if (!TryResolve(statUid, out _, out var live)) return float.NaN;
        try
        {
            return live.SimpleCurrentValue;
        }
        catch (Exception ex)
        {
            WarnOnce("read-threw", statUid, $"StatAccess.Get('{statUid}'): reading the live stat threw {ex.GetType().Name}: {Log.ExceptionText(ex)}, so it returned NaN.");
            return float.NaN;
        }
    }

    /// <summary>
    /// Sets the stat so that <see cref="Get"/> reads <paramref name="value"/>, by UniqueID, through the
    /// game's own permanent stat change (the private <c>GameManager.ChangeStatValue</c> with
    /// <c>StatModification.Permanent</c>, started as a coroutine). So vanilla's rules apply, not ours: the
    /// result is clamped to the stat's live min/max (a value past the top lands ON the top), the stat's
    /// statuses update, and its listeners and triggers fire. The base value moves by (value minus the
    /// current value), which is what the test harness's own stat cheat does; a modifier that later ends
    /// (a perk, a card's passive effect) moves the reading by its amount, as it would for any stat.
    /// The base changes before this call returns, so <see cref="Get"/> right after reads the new value.
    /// Returns true when the change was applied (or the stat already read that value), false on failure,
    /// with one warning per stat and cause. Never throws.
    /// </summary>
    public static bool Set(string statUid, float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            WarnOnce("bad-value", statUid, $"StatAccess.Set('{statUid}'): the value is NaN or infinite, so nothing was written.");
            return false;
        }
        if (!TryResolve(statUid, out var gm, out var live)) return false;
        return TryChange(statUid, gm, live, value, true, nameof(Set));
    }

    /// <summary>
    /// Adds <paramref name="delta"/> to the stat, by UniqueID, through the same game-owned permanent
    /// change as <see cref="Set"/> (so the clamp is vanilla's min and max, not zero-and-max, and
    /// statuses and triggers fire). Returns the stat's new current value, or <see cref="float.NaN"/> on
    /// failure, with one warning per stat and cause. A delta of 0 changes nothing and returns the
    /// current value. Never throws.
    /// </summary>
    public static float Modify(string statUid, float delta)
    {
        if (float.IsNaN(delta) || float.IsInfinity(delta))
        {
            WarnOnce("bad-value", statUid, $"StatAccess.Modify('{statUid}'): the delta is NaN or infinite, so nothing was written.");
            return float.NaN;
        }
        if (!TryResolve(statUid, out var gm, out var live)) return float.NaN;
        if (!TryChange(statUid, gm, live, delta, false, nameof(Modify))) return float.NaN;
        return Get(statUid);
    }

    /// <summary>
    /// An NPC's own live stat by the stat's UniqueID (an <c>NPCStat</c>, for example the animals'
    /// Exists or Blood stat), or null. Resolves the definition, then asks the NPC through its own public
    /// <c>GetStat</c>. Returns null silently for an NPC that has not been initialised yet (the normal
    /// state before it is placed); null with one warning per stat for an empty or unknown UID, a UID
    /// that is not an <c>NPCStat</c>, no NPC, or an NPC whose model does not carry that stat. Not cached:
    /// the lookup is one dictionary read on the NPC. To write the value use the returned stat's own
    /// <c>SetStatValueFromEditor</c>, which is vanilla's permanent change.
    /// </summary>
    public static InGameNPCStat ResolveNpc(InGameNPC npc, string statUid)
    {
        if (string.IsNullOrEmpty(statUid))
        {
            WarnOnce("npc-empty-uid", "", "StatAccess.ResolveNpc was called with an empty NPC stat UniqueID, so it returned null.");
            return null;
        }
        var found = Data.GameRegistry.GetByUid(statUid);
        if (found == null)
        {
            WarnOnce("npc-unknown-uid", statUid, $"StatAccess.ResolveNpc: no object with UniqueID '{statUid}' is loaded, so it returned null.");
            return null;
        }
        if (found is not NPCStat def)
        {
            WarnOnce("npc-not-a-stat", statUid, $"StatAccess.ResolveNpc: UniqueID '{statUid}' is a {found.GetType().Name}, not an NPCStat, so it returned null.");
            return null;
        }
        return ResolveNpc(npc, def);
    }

    /// <summary>
    /// <see cref="ResolveNpc(InGameNPC, string)"/> for a caller that already holds the <c>NPCStat</c>
    /// definition (the Animals lifecycle and companion code do). Same results and reporting.
    /// </summary>
    public static InGameNPCStat ResolveNpc(InGameNPC npc, NPCStat stat)
    {
        if (stat == null)
        {
            WarnOnce("npc-stat-null", "", "StatAccess.ResolveNpc was called with a null NPCStat, so it returned null.");
            return null;
        }
        string uid = stat.UniqueID;
        if (npc == null)
        {
            WarnOnce("npc-null", uid, $"StatAccess.ResolveNpc('{uid}') was called with no NPC (null or destroyed), so it returned null.");
            return null;
        }
        try
        {
            if (!npc.Initialized)
            {
                // silent-ok: an NPC that has not run Init has no stats yet, the normal state before it is placed.
                return null;
            }
            var live = npc.GetStat(stat);
            if (live == null)
            {
                string who = npc.NPCModel != null ? npc.NPCModel.name : "no model";
                WarnOnce("npc-lacks-stat", uid + "@" + who, $"StatAccess.ResolveNpc: NPC '{who}' does not carry stat '{uid}' (its model's stat list does not hold that NPCStat), so it returned null.");
                return null;
            }
            return live;
        }
        catch (Exception ex)
        {
            WarnOnce("npc-resolve-threw", uid, $"StatAccess.ResolveNpc('{uid}') threw {ex.GetType().Name}: {Log.ExceptionText(ex)}, so it returned null.");
            return null;
        }
    }

    /// <summary>
    /// Shared by <see cref="Resolve"/>, <see cref="Get"/>, <see cref="Set"/> and <see cref="Modify"/>:
    /// the cached or freshly resolved live stat, plus the GameManager that owns it. Every false return
    /// has warned once for its cause.
    /// </summary>
    private static bool TryResolve(string statUid, out GameManager gm, out InGameStat live)
    {
        gm = null;
        live = null;
        if (string.IsNullOrEmpty(statUid))
        {
            WarnOnce("empty-uid", "", "StatAccess was called with an empty stat UniqueID, so it returned its failure value (null, NaN or false).");
            return false;
        }
        try
        {
            EnsureRunReset();

            // Unity's == on the MonoBehaviour static type: a destroyed manager, and no manager at all
            // (the main menu), both read null.
            gm = CardUtil.GetGameManagerInstance() as GameManager;
            if (gm == null)
            {
                WarnOnce("no-run", statUid, $"StatAccess: stat '{statUid}' was requested with no run in progress (no live GameManager), so it returned its failure value (null, NaN or false). Logged once per stat.");
                return false;
            }

            // A different GameManager means a different run: nothing cached for the old one is valid,
            // even if OnGMInitialized has not fired yet (it fires at the END of run start).
            if (!ReferenceEquals(_cacheOwner, gm))
            {
                _liveByUid.Clear();
                _cacheOwner = gm;
            }
            if (_liveByUid.TryGetValue(statUid, out var cached))
            {
                if (cached)
                {
                    live = cached;
                    return true;
                }
                _liveByUid.Remove(statUid);
            }

            var found = Data.GameRegistry.GetByUid(statUid);
            if (found == null)
            {
                WarnOnce("unknown-uid", statUid, $"StatAccess: no GameStat with UniqueID '{statUid}' is loaded, so it returned its failure value (null, NaN or false). Check the UID against the mod's GameStat JSON or the vanilla GUID table. Logged once per stat.");
                return false;
            }
            if (found is not GameStat def)
            {
                WarnOnce("not-a-stat", statUid, $"StatAccess: UniqueID '{statUid}' is a {found.GetType().Name}, not a GameStat, so it returned its failure value (null, NaN or false). Logged once per stat.");
                return false;
            }
            var statsDict = gm.StatsDict;
            if (statsDict == null)
            {
                WarnOnce("no-statsdict", statUid, $"StatAccess: GameManager.StatsDict is null, so stat '{statUid}' could not be resolved and it returned its failure value (null, NaN or false).");
                return false;
            }
            if (!statsDict.TryGetValue(def, out var inRun) || inRun == null)
            {
                WarnOnce("not-in-run", statUid, $"StatAccess: stat '{statUid}' is loaded but has no live instance in this run (GameManager.StatsDict does not hold it), so it returned its failure value (null, NaN or false). Logged once per stat.");
                return false;
            }
            _liveByUid[statUid] = inRun;
            live = inRun;
            return true;
        }
        catch (Exception ex)
        {
            WarnOnce("resolve-threw", statUid, $"StatAccess: resolving stat '{statUid}' threw {ex.GetType().Name}: {Log.ExceptionText(ex)}, so it returned its failure value (null, NaN or false).");
            return false;
        }
    }

    /// <summary>
    /// Applies a permanent change through vanilla's own <c>GameManager.ChangeStatValue</c>. When
    /// <paramref name="absolute"/> is true <paramref name="amount"/> is the TARGET value (the change is
    /// target minus the current value), otherwise it is the delta. Every false return has warned once.
    /// </summary>
    private static bool TryChange(string statUid, GameManager gm, InGameStat live, float amount, bool absolute, string caller)
    {
        try
        {
            // Vanilla ignores a permanent change to a content-filtered stat (the first line of
            // ChangeStatValue), so reporting success for one would be a lie.
            if (live.StatModel != null && live.StatModel.IsContentFiltered)
            {
                WarnOnce("content-filtered", statUid, $"StatAccess.{caller}('{statUid}'): the stat is hidden by a content filter, so vanilla ignores permanent changes to it and nothing was written.");
                return false;
            }
            float delta = absolute ? amount - live.SimpleCurrentValue : amount;
            if (delta == 0f) return true;
            if (!EnsureChangeMethod(out var method)) return false;

            // An IEnumerator method does nothing until it is started: a bare Invoke only builds it. Started
            // here, it runs to its first yield at once, and the base value is changed before that yield.
            var routine = method.Invoke(gm, new object[] { live, delta, StatModification.Permanent }) as IEnumerator;
            if (routine == null)
            {
                WarnOnce("change-null", statUid, $"StatAccess.{caller}('{statUid}'): GameManager.ChangeStatValue returned no coroutine, so nothing was written.");
                return false;
            }
            gm.StartCoroutine(routine);
            return true;
        }
        catch (Exception ex)
        {
            WarnOnce("change-threw", statUid, $"StatAccess.{caller}('{statUid}'): the stat change threw {ex.GetType().Name}: {Log.ExceptionText(ex)}, so it returned its failure value.");
            return false;
        }
    }

    private static bool _changeMethodResolved;
    private static MethodInfo _changeMethod;
    private static string _changeMethodCause;

    /// <summary>
    /// Resolves vanilla <c>GameManager.ChangeStatValue</c>, once per process. Decompile reference
    /// (EA 0.68b): <c>private IEnumerator ChangeStatValue(InGameStat _Stat, float _Value, StatModification
    /// _Modification)</c>, <c>.decomp/GameManager.cs</c>. It is private, hence <c>AccessTools.Method</c>,
    /// and it returns an <c>IEnumerator</c>, hence <c>StartCoroutine</c> (a bare <c>Invoke</c> would only
    /// build it). Pinned by Framework-ReflectedSignatures.Tests.ps1.
    /// </summary>
    private static bool EnsureChangeMethod(out MethodInfo method)
    {
        if (!_changeMethodResolved)
        {
            _changeMethodResolved = true;
            _changeMethodCause = ResolveChangeStatValue(typeof(GameManager), out _changeMethod);
        }
        method = _changeMethod;
        if (method != null) return true;
        WarnOnce("change-method", "", $"StatAccess: vanilla GameManager.ChangeStatValue could not be used ({_changeMethodCause}), so Set and Modify write nothing and return their failure value. A game update probably changed its signature: re-read .decomp/GameManager.cs.");
        return false;
    }

    /// <summary>
    /// Pure reflection over <paramref name="gameManagerType"/> with no Unity call. Returns null on
    /// success (<paramref name="method"/> set), or a short cause naming what did not match, in which
    /// case <paramref name="method"/> is null and the caller must not write.
    /// </summary>
    internal static string ResolveChangeStatValue(Type gameManagerType, out MethodInfo method)
    {
        method = null;
        if (gameManagerType == null) return "GameManager type unavailable";

        MethodInfo m;
        try { m = AccessTools.Method(gameManagerType, "ChangeStatValue", new[] { typeof(InGameStat), typeof(float), typeof(StatModification) }); }
        catch (Exception ex) { return $"the lookup threw {ex.GetType().Name}: {ex.Message}"; }
        if (m == null) return "no ChangeStatValue(InGameStat, float, StatModification) method";
        if (m.ReturnType != typeof(IEnumerator))
            return $"ChangeStatValue returns {m.ReturnType.Name}, not IEnumerator";

        method = m;
        // silent-ok: success is reported as a null cause (the CardUtil.ResolveApplyPerk convention); EnsureChangeMethod warns on a non-null one.
        return null;
    }

    /// <summary>
    /// Subscribes the per-run cache reset to <c>GameManager.OnGMInitialized</c>, once per process
    /// (root CLAUDE.md "Per-Run Event Registration", pattern b: the early return and the latch are both
    /// load-bearing). OnGMInitialized re-fires on EVERY run start, so the handler only clears state and
    /// registers nothing; the unsubscribe-before-subscribe keeps the subscription single even if the
    /// latch were ever lost.
    /// </summary>
    private static void EnsureRunReset()
    {
        if (_runResetSubscribed) return;
        try
        {
            GameManager.OnGMInitialized -= OnRunStart;
            GameManager.OnGMInitialized += OnRunStart;
            _runResetSubscribed = true;
        }
        catch (Exception ex)
        {
            WarnOnce("run-reset", "", $"StatAccess: could not subscribe the per-run cache reset to GameManager.OnGMInitialized ({ex.GetType().Name}: {ex.Message}). The cache still clears whenever the GameManager instance changes.");
        }
    }

    private static void OnRunStart()
    {
        _liveByUid.Clear();
        _cacheOwner = null;
    }

    #endregion

    /// <summary>
    /// True when <paramref name="stat"/> is a <c>GameStat</c> DEFINITION rather than the live
    /// <c>InGameStat</c>. The caller must then return its own failure value. Emits ONE warning
    /// per (definition runtime type, <paramref name="member"/>) for the life of the process,
    /// never one per call: these accessors sit on per-tick paths. Never throws.
    /// </summary>
    private static bool IsDefinition(object stat, string member, string failureText)
    {
        if (stat is not GameStat definition) return false;
        try
        {
            if (_definitionWarned.Add((stat.GetType(), member)))
            {
                string uid = null, name = null;
                try
                {
                    uid = definition.UniqueID;
                    if (definition) name = definition.name;
                }
                catch (Exception ex) { Log.Debug($"[StatAccess] definition identity unreadable: {ex.GetType().Name} {ex.Message}"); }

                Log.Warn($"StatAccess.{member} was handed a {stat.GetType().Name} DEFINITION "
                       + $"(UniqueID '{uid ?? "<unreadable>"}', name '{name ?? "<unreadable>"}') instead of the live InGameStat, "
                       + $"so it returned {failureText}. A definition from UniqueIDScriptable.GetFromID has no current value. "
                       + "Fix the caller: pass the live instance, which StatAccess.Resolve(uid) returns "
                       + "(or GameManager.StatsDict[definition]; idiom: Community_Mod_Chest/Patcher/HiddenStat.cs, ResolveInstance), "
                       + "or call StatAccess.Get / Set / Modify with the UniqueID. "
                       + $"Logged once per StatAccess member; further {member} calls with a definition fail silently.");
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"[StatAccess] definition guard for {member} threw while logging: {ex.GetType().Name} {ex.Message}");
        }
        return true;
    }

    private static bool TryGetVectorY(object vector, out float y)
    {
        y = 0f;
        if (vector == null) return false;
        if (!Reflect.TryGetMember(vector, "y", out var raw) || raw == null) return false;
        y = CardUtil.ToFloat(raw);
        return true;
    }
}
