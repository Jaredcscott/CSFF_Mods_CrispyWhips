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
            catch { }
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
            catch { }
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
                if (season is UniqueIDScriptable uid) return uid.UniqueID;
                var uidStr = CardUtil.GetMemberValue(season, "UniqueID") as string;
                if (!string.IsNullOrEmpty(uidStr)) return uidStr;
                if (season is UnityEngine.Object uo) return uo.name;
            }
            catch { }
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
            catch { }
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
            catch { }
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
                var env = _currentEnvProp?.GetValue(gm, null);
                if (env == null) return null;
                var envCard = CardUtil.GetMemberValue(env, "EnvCard");
                if (envCard is UniqueIDScriptable uid) return uid.UniqueID;
                return CardUtil.GetMemberValue(envCard, "UniqueID") as string;
            }
            catch { }
            return null;
        }
    }

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
            catch { }
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
        catch { }
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
        catch { return null; }
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
        catch { }
        return false;
    }
}
