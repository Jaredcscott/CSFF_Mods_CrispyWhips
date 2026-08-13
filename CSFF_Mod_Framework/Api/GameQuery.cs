using CSFFModFramework.Util;

namespace CSFFModFramework.Api;

/// <summary>
/// Read-only query API for current game state: time, season, weather, moon phase, environment,
/// and cards in the player's current environment.
///
/// Available after <see cref="FrameworkEvents.GameDataReady"/>. All properties return safe
/// defaults (0, null, false) before the game manager is initialized.
/// </summary>
public static class GameQuery
{
    private const BindingFlags Flags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // ── Reflection cache (resolved once, lazily) ─────────────────────────────
    private static bool _resolved;
    private static Type _gmType;
    private static PropertyInfo _gmInstanceProp;
    private static PropertyInfo _dtpProp;
    private static FieldInfo _dtpField;
    private static PropertyInfo _currentDayProp;
    private static PropertyInfo _currentSeasonProp;
    private static FieldInfo _currentWeatherField;
    private static PropertyInfo _currentEnvProp;
    private static FieldInfo _currentEnvField;
    private static PropertyInfo _leavingEnvProp;
    private static PropertyInfo _envTransitionProp;
    private static FieldInfo _daysPerMoonField;
    private static PropertyInfo _allCardsProp;
    private static FieldInfo _allCardsField;

    // ── Time ─────────────────────────────────────────────────────────────────

    /// <summary>Current in-game hour as a float (0.0 = dawn, 23.99 = late night).</summary>
    public static float HourOfDay
    {
        get
        {
            int dtp = DayTimePoints;
            return dtp < 0 ? 0f : (96f - (dtp % 96f)) / 4f;
        }
    }

    /// <summary>
    /// Raw DayTimePoints from GameManager (0–96, counting DOWN each day).
    /// Returns -1 before game init.
    /// </summary>
    public static int DayTimePoints
    {
        get
        {
            if (!TryResolve()) return -1;
            var gm = GetGM();
            if (gm == null) return -1;
            try
            {
                if (_dtpProp != null) return Convert.ToInt32(_dtpProp.GetValue(gm, null));
                if (_dtpField != null) return Convert.ToInt32(_dtpField.GetValue(gm));
            }
            catch (Exception ex) { Log.Debug($"[GameQuery] DayTimePoints read threw: {Log.ExceptionText(ex)}"); }
            return -1;
        }
    }

    /// <summary>Current in-game day number. Returns 0 before game init.</summary>
    public static int CurrentDay
    {
        get
        {
            if (!TryResolve()) return 0;
            var gm = GetGM();
            if (gm == null) return 0;
            try { if (_currentDayProp != null) return Convert.ToInt32(_currentDayProp.GetValue(gm, null)); }
            catch (Exception ex) { Log.Debug($"[GameQuery] CurrentDay read threw: {Log.ExceptionText(ex)}"); }
            return 0;
        }
    }

    // ── Season ────────────────────────────────────────────────────────────────

    /// <summary>
    /// UniqueID of the current season ("Spring", "Summer", "Autumn", "Winter").
    /// Returns null before game init.
    /// </summary>
    public static string CurrentSeason
    {
        get
        {
            if (!TryResolve()) return null;
            var gm = GetGM();
            if (gm == null) return null;
            try
            {
                var season = _currentSeasonProp?.GetValue(gm, null);
                if (season == null) return null;
                // EA 0.65: GameManager.CurrentSeason is a plain `Season` class (decompile Season.cs) —
                // NOT a UniqueIDScriptable and NOT a UnityEngine.Object. Its season-name field is
                // `SeasonID` ("Spring"/"Summer"/"Autumn"/"Winter", per SeasonsSettings.cs). Read it FIRST,
                // or all three fallbacks below return null and every season-gated feature silently dies
                // (ConditionalDropService.GetSeasonIndex → 0 → SeasonRange never matches). See memory:
                // reference_gamequery_currentseason_null.
                var seasonId = CardUtil.GetMemberValue(season, "SeasonID") as string;
                if (!string.IsNullOrEmpty(seasonId)) return seasonId;
                if (season is UniqueIDScriptable uid) return uid.UniqueID;
                var uidStr = CardUtil.GetMemberValue(season, "UniqueID") as string;
                if (!string.IsNullOrEmpty(uidStr)) return uidStr;
                if (season is UnityEngine.Object uo) return uo.name;
            }
            catch (Exception ex) { Log.Debug($"[GameQuery] CurrentSeason read threw: {Log.ExceptionText(ex)}"); }
            return null;
        }
    }

    public static bool IsSpring => string.Equals(CurrentSeason, "Spring", StringComparison.OrdinalIgnoreCase);
    public static bool IsSummer => string.Equals(CurrentSeason, "Summer", StringComparison.OrdinalIgnoreCase);
    public static bool IsAutumn => string.Equals(CurrentSeason, "Autumn", StringComparison.OrdinalIgnoreCase);
    public static bool IsWinter => string.Equals(CurrentSeason, "Winter", StringComparison.OrdinalIgnoreCase);

    // ── Weather ───────────────────────────────────────────────────────────────

    /// <summary>UniqueID of the active weather card, or null if no weather / not initialized.</summary>
    public static string CurrentWeatherUniqueId
    {
        get
        {
            if (!TryResolve()) return null;
            var gm = GetGM();
            if (gm == null) return null;
            try
            {
                var weatherCard = _currentWeatherField?.GetValue(gm);
                if (weatherCard == null) return null;
                return CardUtil.GetCardUniqueId(weatherCard);
            }
            catch (Exception ex) { Log.Debug($"[GameQuery] CurrentWeatherUniqueId read threw: {Log.ExceptionText(ex)}"); }
            return null;
        }
    }

    /// <summary>True if a weather card is currently active.</summary>
    public static bool HasWeather => CurrentWeatherUniqueId != null;

    // ── Moon ──────────────────────────────────────────────────────────────────

    /// <summary>Days per lunar cycle (from GameManager.DaysPerMoon, default 30).</summary>
    public static int DaysPerMoon
    {
        get
        {
            if (!TryResolve()) return 30;
            var gm = GetGM();
            if (gm == null) return 30;
            try { if (_daysPerMoonField != null) return Convert.ToInt32(_daysPerMoonField.GetValue(gm)); }
            catch (Exception ex) { Log.Debug($"[GameQuery] DaysPerMoon read threw: {Log.ExceptionText(ex)}"); }
            return 30;
        }
    }

    /// <summary>Current moon phase index (0-based, resets every DaysPerMoon days).</summary>
    public static int MoonPhase => CurrentDay % Math.Max(1, DaysPerMoon);

    /// <summary>Moon phase as a 0–1 fraction of the full lunar cycle.</summary>
    public static float MoonPhaseNormalized
    {
        get
        {
            int dpm = DaysPerMoon;
            return dpm <= 0 ? 0f : (CurrentDay % dpm) / (float)dpm;
        }
    }

    // ── Environment ───────────────────────────────────────────────────────────

    /// <summary>UniqueID of the current environment's card, or null if unavailable.</summary>
    public static string CurrentEnvironmentUniqueId
    {
        get
        {
            if (!TryResolve()) return null;
            var gm = GetGM();
            if (gm == null) return null;
            try
            {
                var env = GetCurrentEnv(gm);
                if (env == null) return null;
                // EnvID (a struct) field name is "MainEnvCard" in EA 0.64f; try "EnvCard" as forward-compat fallback.
                var envCard = CardUtil.GetMemberValue(env, "MainEnvCard")
                           ?? CardUtil.GetMemberValue(env, "EnvCard");
                if (envCard is UniqueIDScriptable uid) return uid.UniqueID;
                return CardUtil.GetMemberValue(envCard, "UniqueID") as string;
            }
            catch (Exception ex) { Log.Debug($"[GameQuery] CurrentEnvironmentUniqueId read threw: {Log.ExceptionText(ex)}"); }
            return null;
        }
    }

    /// <summary>
    /// Reads GameManager.CurrentEnvironment (the <c>EnvID</c> struct). It is a field in EA 0.64f;
    /// the property form is tried as a forward-compat fallback. Returns the boxed struct or null.
    /// </summary>
    private static object GetCurrentEnv(object gm)
    {
        try
        {
            if (_currentEnvField != null) return _currentEnvField.GetValue(gm);
            if (_currentEnvProp != null) return _currentEnvProp.GetValue(gm, null);
        }
        catch (Exception ex) { Log.Debug($"[GameQuery] CurrentEnvironment read threw: {Log.ExceptionText(ex)}"); }
        return null;
    }

    /// <summary>
    /// True if the player is currently inside an instanced environment (cabin, mud hut, cellar,
    /// enclosure, mine, coop, etc.). Returns false when outdoors or before game init.
    /// </summary>
    public static bool IsInInstancedEnvironment
    {
        get
        {
            if (!TryResolve()) return false;
            var gm = GetGM();
            if (gm == null) return false;
            try
            {
                var env = GetCurrentEnv(gm);
                if (env == null) return false;
                var envCard = CardUtil.GetMemberValue(env, "MainEnvCard")
                           ?? CardUtil.GetMemberValue(env, "EnvCard");
                if (envCard == null) return false;
                var isInstanced = CardUtil.GetMemberValue(envCard, "InstancedEnvironment");
                return isInstanced is true;
            }
            catch (Exception ex) { Log.Debug($"[GameQuery] IsInInstancedEnvironment read threw: {Log.ExceptionText(ex)}"); }
            return false;
        }
    }

    /// <summary>
    /// Real vanilla/mod CardTag names marking an environment as cave or indoor (see
    /// Documentation/CSFF_Map_Travel_System.md § Environment Card Tags). There is no confirmed
    /// positive "outdoor" tag — callers test for absence of this set instead.
    /// </summary>
    private static readonly string[] IndoorOrCaveTags =
        { "tag_Cave", "tag_EnvCaveSystem", "tag_EnvIndoors", "tag_Env_BearCave", "tag_Env_WolfCave" };

    /// <summary>
    /// True if the current environment's card carries an indoor/cave biome tag. Complements
    /// <see cref="IsInInstancedEnvironment"/> — some caves and building interiors are plain
    /// (non-instanced) CT4/CT8 boards tagged tag_Cave/tag_EnvIndoors rather than pocket-dimension
    /// instances, so InstancedEnvironment alone misses them.
    /// </summary>
    public static bool IsInIndoorOrCaveEnvironment
    {
        get
        {
            if (!TryResolve()) return false;
            var gm = GetGM();
            if (gm == null) return false;
            try
            {
                var env = GetCurrentEnv(gm);
                if (env == null) return false;
                var envCard = CardUtil.GetMemberValue(env, "MainEnvCard")
                           ?? CardUtil.GetMemberValue(env, "EnvCard");
                if (envCard == null) return false;
                foreach (var tag in IndoorOrCaveTags)
                    if (HasTag(envCard, tag)) return true;
                return false;
            }
            catch (Exception ex) { Log.Debug($"[GameQuery] IsInIndoorOrCaveEnvironment read threw: {Log.ExceptionText(ex)}"); }
            return false;
        }
    }

    /// <summary>
    /// True only when the player is on a plain outdoor board — neither an instanced environment
    /// (cabin, mud hut, cellar, enclosure, mine, coop) nor a cave/indoor-tagged one. Use this for
    /// "outdoor only" gating (spawn triggers, foraging, etc.) instead of
    /// <see cref="IsInInstancedEnvironment"/> alone.
    /// </summary>
    public static bool IsOutdoors => !IsInInstancedEnvironment && !IsInIndoorOrCaveEnvironment;

    /// <summary>True while the player is transitioning between environments.</summary>
    public static bool IsTransitioning
    {
        get
        {
            if (!TryResolve()) return false;
            var gm = GetGM();
            if (gm == null) return false;
            try
            {
                bool leaving = _leavingEnvProp != null && Convert.ToBoolean(_leavingEnvProp.GetValue(gm, null));
                bool transit = _envTransitionProp != null && Convert.ToBoolean(_envTransitionProp.GetValue(gm, null));
                return leaving || transit;
            }
            catch (Exception ex) { Log.Debug($"[GameQuery] IsTransitioning read threw: {Log.ExceptionText(ex)}"); }
            return false;
        }
    }

    // ── Card queries ──────────────────────────────────────────────────────────

    /// <summary>
    /// All InGameCardBase instances in the player's current environment.
    /// Allocates a new list each call. Returns empty before game init.
    /// </summary>
    public static IReadOnlyList<object> CardsInPlayerEnv()
    {
        if (!TryResolve()) return Array.Empty<object>();
        var gm = GetGM();
        if (gm == null) return Array.Empty<object>();

        var results = new List<object>();
        try
        {
            IList allCards = _allCardsProp != null
                ? _allCardsProp.GetValue(gm, null) as IList
                : _allCardsField?.GetValue(gm) as IList;
            if (allCards == null) return results;

            foreach (var card in allCards)
            {
                if (card != null && IsInPlayerEnv(card))
                    results.Add(card);
            }
        }
        catch (Exception ex) { Log.Debug($"[GameQuery] CardsInPlayerEnv read threw: {Log.ExceptionText(ex)}"); }
        return results;
    }

    /// <summary>Cards in the player's current environment whose CardModel has the given tag (e.g. "tag_Fuel").</summary>
    public static IReadOnlyList<object> CardsInPlayerEnvWithTag(string tagName)
    {
        if (string.IsNullOrEmpty(tagName)) return Array.Empty<object>();
        var all = CardsInPlayerEnv();
        var results = new List<object>();
        foreach (var card in all)
        {
            var model = CardUtil.GetCardData(card);
            if (model != null && HasTag(model, tagName)) results.Add(card);
        }
        return results;
    }

    /// <summary>
    /// Cards in the player's current environment with a given CardType value
    /// (0=Item, 2=Structure, 7=Blueprint, 4=Buff).
    /// </summary>
    public static IReadOnlyList<object> CardsInPlayerEnvOfType(int cardType)
    {
        var all = CardsInPlayerEnv();
        var results = new List<object>();
        foreach (var card in all)
        {
            var model = CardUtil.GetCardData(card);
            if (model == null) continue;
            var ct = CardUtil.GetMemberValue(model, "CardType");
            if (ct != null && Convert.ToInt32(ct) == cardType) results.Add(card);
        }
        return results;
    }

    // ── Internal setup ────────────────────────────────────────────────────────

    private static bool TryResolve()
    {
        if (_resolved) return _gmType != null;
        _resolved = true;

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            _gmType = asm.GetType("GameManager", false);
            if (_gmType != null) break;
        }
        if (_gmType == null) return false;

        // Instance is on MBSingleton<T> base — requires FlattenHierarchy or base-type walk
        _gmInstanceProp = _gmType.GetProperty("Instance",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy);
        if (_gmInstanceProp == null)
        {
            for (var t = _gmType.BaseType; t != null; t = t.BaseType)
            {
                _gmInstanceProp = t.GetProperty("Instance",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy);
                if (_gmInstanceProp != null) break;
            }
        }

        // DayTimePoints — try multiple names for forward compat
        foreach (var name in new[] { "DayTimePoints", "CurrentDayTimePoints", "DaytimePoints" })
        {
            _dtpProp = _gmType.GetProperty(name, Flags);
            if (_dtpProp != null) break;
            _dtpField = _gmType.GetField(name, Flags);
            if (_dtpField != null) break;
        }

        _currentDayProp    = _gmType.GetProperty("CurrentDay", Flags);
        _currentSeasonProp = _gmType.GetProperty("CurrentSeason", Flags);
        _currentWeatherField = _gmType.GetField("CurrentWeatherCard", Flags);
        // GameManager.CurrentEnvironment is a FIELD (type EnvID), not a property, in EA 0.64f
        // (confirmed by Mono.Cecil dump). Resolving it as a property only returned null, which
        // silently broke every env-dependent query (run-start spawn, arrival detection). Resolve
        // both so a future version that promotes it to a property still works.
        _currentEnvField   = _gmType.GetField("CurrentEnvironment", Flags);
        _currentEnvProp    = _gmType.GetProperty("CurrentEnvironment", Flags);
        _leavingEnvProp    = _gmType.GetProperty("LeavingEnvironment", Flags);
        _envTransitionProp = _gmType.GetProperty("EnvironmentTransition", Flags);
        _daysPerMoonField  = _gmType.GetField("DaysPerMoon", Flags);

        foreach (var name in new[] { "AllCards", "AllInGameCards", "InGameCards" })
        {
            _allCardsProp = _gmType.GetProperty(name, Flags);
            if (_allCardsProp != null) break;
            _allCardsField = _gmType.GetField(name, Flags);
            if (_allCardsField != null) break;
        }

        return true;
    }

    private static object GetGM()
    {
        try { return _gmInstanceProp?.GetValue(null, null); }
        catch (Exception ex) { Log.Debug($"[GameQuery] GameManager.Instance read threw: {Log.ExceptionText(ex)}"); return null; }
    }

    private static bool IsInPlayerEnv(object card)
    {
        var env = CardUtil.GetMemberValue(card, "CardEnvironment");
        if (env == null) return false;
        var val = CardUtil.GetMemberValue(env, "MatchesPlayerEnv");
        return val is bool b && b;
    }

    private static bool HasTag(object cardData, string tagName)
    {
        try
        {
            var tagsField = cardData.GetType().GetField("CardTags", Flags);
            if (tagsField?.GetValue(cardData) is IList list)
                foreach (var t in list)
                    if (t is UnityEngine.Object uo && uo.name == tagName) return true;
        }
        catch (Exception ex) { Log.Debug($"[GameQuery] HasTag('{tagName}') read threw: {Log.ExceptionText(ex)}"); }
        return false;
    }

    // ── Time manipulation ─────────────────────────────────────────────────────

    /// <summary>
    /// Decrements DayTimePoints by <paramref name="dtpAmount"/> (one DTP = 15 in-game minutes),
    /// clamped so DTP never goes below zero. Use this when spawning arrival content that should
    /// cost the player some travel time — e.g., arriving at a clone env whose CT8 was deferred.
    /// Does nothing before game init or if DTP reflection is unavailable.
    /// </summary>
    internal static void SpendDayTimePoints(int dtpAmount)
    {
        if (dtpAmount <= 0) return;
        if (!TryResolve()) return;
        var gm = GetGM();
        if (gm == null) return;
        try
        {
            int current;
            if (_dtpProp != null)
            {
                current = Convert.ToInt32(_dtpProp.GetValue(gm, null));
                int next = Math.Max(0, current - dtpAmount);
                _dtpProp.SetValue(gm, Convert.ChangeType(next, _dtpProp.PropertyType));
                return;
            }
            if (_dtpField != null)
            {
                current = Convert.ToInt32(_dtpField.GetValue(gm));
                int next = Math.Max(0, current - dtpAmount);
                _dtpField.SetValue(gm, Convert.ChangeType(next, _dtpField.FieldType));
            }
        }
        catch (Exception ex) { Log.Debug($"[GameQuery] SpendDayTimePoints error: {ex.Message}"); }
    }
}
