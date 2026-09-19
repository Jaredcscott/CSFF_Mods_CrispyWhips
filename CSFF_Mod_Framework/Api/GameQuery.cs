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
    private static FieldInfo _daySettingsField;
    private static FieldInfo _daysPerYearField;
    private static PropertyInfo _allCardsProp;
    private static FieldInfo _allCardsField;

    // ── Time ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Current in-game CLOCK hour as a float in [0, 24): 6.0 is 06:00, 19.25 is 19:15, 22.0 is
    /// 22:00. This is the hour the on-screen clock shows and the hour every vanilla time-of-day
    /// consumer compares against (<c>InGameTimeCondition</c> hour windows such as an NPCDuty's
    /// <c>ValidTimesOfDay</c>, GameStat <c>TimeOfDayMods</c>, <c>Season</c>, <c>WeatherSet</c>), so
    /// a window written here as "22 to 6" means the same night it means in vanilla data.
    /// Returns 0 before game init.
    ///
    /// <para>The value is the game's own <c>GameManager.HourOfTheDayValue(DayTimePoints, 0)</c>
    /// wrapped into [0, 24), i.e. <c>DaySettings.DayStartingHour</c> plus the hours elapsed since
    /// the day started. It steps once per DayTimePoint (a quarter hour at the shipped 96 points per
    /// day). Mini ticks are passed as 0 on purpose, exactly as vanilla's own TimeOfDayMods
    /// evaluation does: <c>HourOfTheDayValue</c> SUBTRACTS mini-tick time while mini ticks count UP
    /// within a point, so passing the live count makes the hour run backwards inside each point
    /// (6.00, 5.95, 5.90 ...). A real-time poller with a window edge on that boundary would flip
    /// in, out and in again, and one watching for the whole hour to change could see it change
    /// three times.</para>
    ///
    /// <para><b>Corrected 2026-09-17 (plan D6).</b> This used to return
    /// <c>(96 - DayTimePoints % 96) / 4</c>, the hours since the day STARTED, under a doc comment
    /// calling 0.0 "dawn". The day starts at <c>DayStartingHour</c> (04:00 in the shipped data), not
    /// at midnight, so every clock-hour window compared against it ran that many hours late.
    /// Measured on real saves: <c>DaytimeToHour</c> "22:00" is stored beside
    /// <c>CurrentDayTimePoints</c> 24 (old value 18.0), "06:00" beside 88 (old 2.0) and "02:45"
    /// beside 5 (old 22.75).</para>
    /// </summary>
    public static float HourOfDay
    {
        get
        {
            int dtp = DayTimePoints;
            if (dtp < 0) return 0f;

            if (!_vanillaClockUnavailable)
            {
                try
                {
                    if (TryVanillaClockHour(dtp, out float hour)) return hour;
                    return 0f; // no live GameManager this frame: same default as before game init
                }
                catch (Exception ex)
                {
                    // Reached when a game update renamed or removed a member TryVanillaClockHour
                    // binds at compile time (the JIT failure surfaces at the call above). Latched:
                    // the reflective fallback computes the same number, so nothing is lost by not
                    // retrying, and a per-tick caller is not left throwing every frame.
                    _vanillaClockUnavailable = true;
                    Log.Warn("[GameQuery] GameManager.HourOfTheDayValue is not callable on this game version; "
                           + $"HourOfDay now derives the clock hour from DaySettings by reflection. {Log.ExceptionText(ex)}");
                }
            }
            return FallbackClockHour(dtp);
        }
    }

    private static bool _vanillaClockUnavailable;
    private static bool _dayStartWarned;
    private static FieldInfo _dayStartingHourField;
    private static FieldInfo _dailyPointsField;

    // DayTimeSettings.DayStartingHour and DailyPoints as shipped in EA 0.67i (scene export recorded
    // in Documentation/Research/Animal_System_Vanilla.md section 6.1). Used ONLY when the live
    // DaySettings cannot be read, and never silently: see FallbackClockHour.
    private const int AssumedDayStartingHour = 4;
    private const int AssumedDailyPoints = 96;

    // Kept in its own non-inlined method so the compile-time GameManager members are bound when
    // THIS method is JIT-compiled, which happens at the guarded call in HourOfDay.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool TryVanillaClockHour(int dtp, out float hour)
    {
        hour = 0f;
        var gm = MBSingleton<GameManager>.Instance;
        // DaySettings is a struct; an uninitialised one has DailyPoints 0, and PointToHours
        // (24 / DailyPoints) would turn the whole expression into NaN.
        if (!gm || gm.DaySettings.DailyPoints <= 0) return false;
        hour = Mathf.Repeat(GameManager.HourOfTheDayValue(dtp, 0), 24f);
        return true;
    }

    /// <summary>
    /// The same clock hour computed by reflection, for a game version where the compile-time call
    /// is gone. Never returns the old dawn-relative number: if <c>DayStartingHour</c> cannot be
    /// read it warns once and assumes the shipped value.
    /// </summary>
    private static float FallbackClockHour(int dtp)
    {
        int dailyPoints = AssumedDailyPoints;
        int startHour = AssumedDayStartingHour;
        bool startRead = false;
        try
        {
            var gm = GetGM();
            var daySettings = gm == null ? null : _daySettingsField?.GetValue(gm);
            if (daySettings != null)
            {
                var type = daySettings.GetType();
                _dailyPointsField ??= type.GetField("DailyPoints", Flags);
                _dayStartingHourField ??= type.GetField("DayStartingHour", Flags);
                if (_dailyPointsField != null)
                {
                    int points = Convert.ToInt32(_dailyPointsField.GetValue(daySettings));
                    if (points > 0) dailyPoints = points;
                }
                if (_dayStartingHourField != null)
                {
                    startHour = Convert.ToInt32(_dayStartingHourField.GetValue(daySettings));
                    startRead = true;
                }
            }
        }
        catch (Exception ex) { Log.Debug($"[GameQuery] DaySettings read threw: {Log.ExceptionText(ex)}"); }

        if (!startRead && !_dayStartWarned)
        {
            _dayStartWarned = true;
            Log.Warn("[GameQuery] DaySettings.DayStartingHour could not be read; HourOfDay assumes the day starts at "
                   + $"{AssumedDayStartingHour}:00 (the shipped value). Clock-hour windows in mods are wrong by the difference "
                   + "if this game version starts its day at another hour.");
        }

        float elapsedHours = (dailyPoints - dtp) * (24f / dailyPoints);
        return Mathf.Repeat(startHour + elapsedHours, 24f);
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

    // ── Year ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// In-game days per year (from GameManager.DaySettings.DaysPerYear — a live gamemode
    /// setting, NOT a hardcoded constant; confirmed 120 in the shipped EA 0.66h data but a
    /// custom gamemode can change it). Returns the same 120 default before game init, so a
    /// day-length comparison (e.g. CurrentDay &gt;= DaysPerYear) can't false-trigger at boot
    /// when CurrentDay also defaults to 0.
    /// </summary>
    public static int DaysPerYear
    {
        get
        {
            const int fallback = 120;
            if (!TryResolve()) return fallback;
            var gm = GetGM();
            if (gm == null) return fallback;
            try
            {
                var daySettings = _daySettingsField?.GetValue(gm);
                if (daySettings == null) return fallback;
                _daysPerYearField ??= daySettings.GetType().GetField("DaysPerYear", Flags);
                if (_daysPerYearField != null) return Convert.ToInt32(_daysPerYearField.GetValue(daySettings));
            }
            catch (Exception ex) { Log.Debug($"[GameQuery] DaysPerYear read threw: {Log.ExceptionText(ex)}"); }
            return fallback;
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
    /// The current environment's full <c>EnvID.StringDictionnaryKey</c> (game's own spelling —
    /// double 'n'). Identical to <see cref="CurrentEnvironmentUniqueId"/> for a non-instanced
    /// environment, but additionally encodes the <c>ParentEnvs</c> chain
    /// (<c>mainUid_parentUid[=travelIndex]</c>) for an instanced one (Cabin/Cellar/Coop/Enclosure,
    /// mines, attics, ...). Reconstructing via <c>new EnvID(string)</c> restores that chain — the
    /// bare-UID form <see cref="CurrentEnvironmentUniqueId"/> loses it, which nulls the env when
    /// rebuilt via <c>new EnvID(CardData)</c> for an instanced destination (ctor guard). Use this
    /// (not the bare UID) whenever recording a travel-return target. See root CLAUDE.md
    /// § "GameManager.NextEnvironment travel invariant" and memory
    /// reference_gamemanager_nextenvironment_travel_invariant.
    /// </summary>
    public static string CurrentEnvironmentStringDictionaryKey
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
                return CardUtil.GetMemberValue(env, "StringDictionnaryKey") as string;
            }
            catch (Exception ex) { Log.Debug($"[GameQuery] CurrentEnvironmentStringDictionaryKey read threw: {Log.ExceptionText(ex)}"); }
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
        _daySettingsField  = _gmType.GetField("DaySettings", Flags);

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
