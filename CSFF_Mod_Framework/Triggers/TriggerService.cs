using CSFFModFramework.Data;
using CSFFModFramework.Util;

namespace CSFFModFramework.Triggers;

/// <summary>
/// Runtime service that evaluates mod-defined spawn triggers on each in-game day rollover.
/// Each trigger specifies a card UID, spawn chance, frequency (in DTP units), and a MaxOnBoard cap.
/// When the frequency elapses, rolls the chance, checks the board count, and calls GiveCard if all pass.
///
/// <para>Polled via <c>Plugin.Update()</c>. Mirrors the pattern of <see cref="Wildlife.WildlifeRaidService"/>.</para>
/// </summary>
internal static class TriggerService
{
    private const BindingFlags Flags =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static bool _ready;
    private static bool _setupAttempted;
    private static int _lastDayTimePoints = int.MinValue;

    private static Type _gameManagerType;
    private static PropertyInfo _gmInstanceProp;
    private static PropertyInfo _dtpProp;
    private static FieldInfo _dtpField;
    private static MethodInfo _giveCardMethod;
    private static MethodInfo _getFromIdGeneric;
    private static Type _cardDataType;

    private static readonly Dictionary<(Type, string), MemberInfo> _memberCache = new();

    public static void Init()
    {
        _ready = false;
        _setupAttempted = false;
        _lastDayTimePoints = int.MinValue;
        _memberCache.Clear();
    }

    /// <summary>Called from Plugin.Update(). No-op until the game is fully initialized.</summary>
    public static void PollUpdate()
    {
        if (TriggerLoader.LoadedTriggers.Count == 0) return;

        if (!_ready)
        {
            if (!TryCompleteOneTimeSetup()) return;
        }

        var dtp = ReadDayTimePoints();
        if (dtp < 0) return;

        if (_lastDayTimePoints == int.MinValue) { _lastDayTimePoints = dtp; return; }

        // DTP counts DOWN (96→0) then wraps back to ~96. A jump of >50 upward = day rollover.
        if (dtp > _lastDayTimePoints + 50)
        {
            try { OnDayRollover(); }
            catch (Exception ex) { Log.Warn($"[TriggerService] day-rollover processing failed: {Log.ExceptionText(ex)}"); }
        }

        _lastDayTimePoints = dtp;
    }

    // ─────────────────────────────────────────────────────────────── setup ──

    private static bool TryCompleteOneTimeSetup()
    {
        if (_setupAttempted) return false;
        if (GameRegistry.Count == 0) return false;
        _setupAttempted = true;

        ResolveTypes();

        _ready = _gameManagerType != null && _gmInstanceProp != null && _giveCardMethod != null;
        if (!_ready)
            Log.Warn("[TriggerService] could not resolve GameManager or GiveCard — triggers inactive");
        else
            Log.Info($"[TriggerService] initialized with {TriggerLoader.LoadedTriggers.Count} trigger(s)");

        return _ready;
    }

    private static void ResolveTypes()
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (_gameManagerType == null) _gameManagerType = asm.GetType("GameManager", false);
            if (_cardDataType    == null) _cardDataType    = asm.GetType("CardData",     false);
            if (_gameManagerType != null && _cardDataType != null) break;
        }

        if (_gameManagerType == null) { Log.Warn("[TriggerService] GameManager type not found"); return; }

        // Instance is on MBSingleton<T> base — requires FlattenHierarchy or base-type walk.
        _gmInstanceProp = _gameManagerType.GetProperty("Instance",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy);
        if (_gmInstanceProp == null)
        {
            for (var t = _gameManagerType.BaseType; t != null; t = t.BaseType)
            {
                _gmInstanceProp = t.GetProperty("Instance",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy);
                if (_gmInstanceProp != null) break;
            }
        }

        // DayTimePoints — try several known field/property names (per CLAUDE.md §Runtime Stat Change Hook).
        foreach (var name in new[] { "DayTimePoints", "CurrentDayTimePoints", "DaytimePoints" })
        {
            _dtpProp  = _gameManagerType.GetProperty(name, Flags);
            if (_dtpProp  != null) break;
            _dtpField = _gameManagerType.GetField(name, Flags);
            if (_dtpField != null) break;
        }

        // GiveCard(CardData, ...) — first overload whose first param is CardData.
        if (_cardDataType != null)
        {
            _giveCardMethod = _gameManagerType.GetMethods(Flags)
                .FirstOrDefault(m => m.Name == "GiveCard"
                    && m.GetParameters() is { Length: >= 1 } ps
                    && ps[0].ParameterType == _cardDataType);
        }

        // GetFromID<T> generic on UniqueIDScriptable for typed card lookup.
        _getFromIdGeneric = typeof(UniqueIDScriptable)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == "GetFromID" && m.IsGenericMethodDefinition);
    }

    // ─────────────────────────────────────────────────────── day-tick logic ──

    private static void OnDayRollover()
    {
        foreach (var def in TriggerLoader.LoadedTriggers)
        {
            def.DaysAccumulated += 1f;

            float daysNeeded = def.DaysBetweenFires;
            if (def.DaysAccumulated < daysNeeded) continue;

            def.DaysAccumulated -= daysNeeded;

            // Roll spawn chance (SpawnChancePercent is 0-100).
            float roll = UnityEngine.Random.Range(0, 100);
            if (roll >= def.SpawnChancePercent)
            {
                Log.Debug($"[TriggerService] '{def.UniqueID}': chance roll failed ({roll:F0} >= {def.SpawnChancePercent})");
                continue;
            }

            // Check MaxOnBoard cap.
            if (def.MaxOnBoard > 0 && CountOnBoard(def) >= def.MaxOnBoard)
            {
                Log.Debug($"[TriggerService] '{def.UniqueID}': MaxOnBoard={def.MaxOnBoard} reached — skipping");
                continue;
            }

            if (TrySpawn(def))
                Log.Debug($"[TriggerService] '{def.UniqueID}': spawned '{def.TriggerCardUID}' (roll {roll:F0} < {def.SpawnChancePercent})");
        }
    }

    // ─────────────────────────────────────────────────────────────── spawn ──

    private static bool TrySpawn(TriggerDefinition def)
    {
        if (_giveCardMethod == null || _gmInstanceProp == null) return false;

        var gm = _gmInstanceProp.GetValue(null, null);
        if (gm == null)
        {
            Log.Warn($"[TriggerService] '{def.UniqueID}': GameManager.Instance is null — skipping spawn");
            return false;
        }

        var cardData = ResolveCardData(def.TriggerCardUID);
        if (cardData == null)
        {
            Log.Warn($"[TriggerService] '{def.UniqueID}': TriggerCardUID '{def.TriggerCardUID}' not found in registry — trigger inactive");
            return false;
        }

        try
        {
            var ps   = _giveCardMethod.GetParameters();
            var args = new object[ps.Length];
            args[0] = cardData;
            for (int i = 1; i < ps.Length; i++)
            {
                var pt = ps[i].ParameterType;
                args[i] = pt.IsValueType ? Activator.CreateInstance(pt) : null;
            }
            _giveCardMethod.Invoke(gm, args);
            return true;
        }
        catch (Exception ex)
        {
            var inner = ex is TargetInvocationException tie ? tie.InnerException ?? ex : ex;
            Log.Warn($"[TriggerService] '{def.UniqueID}': GiveCard threw: {Log.ExceptionText(inner)}");
            return false;
        }
    }

    private static object ResolveCardData(string uid)
    {
        if (_getFromIdGeneric != null && _cardDataType != null)
        {
            try
            {
                var typed = _getFromIdGeneric.MakeGenericMethod(_cardDataType);
                var result = typed.Invoke(null, new object[] { uid });
                if (result != null) return result;
            }
            catch (Exception ex)
            {
                Log.Debug($"[TriggerService] GetFromID<CardData>('{uid}') failed: {ex.GetType().Name}");
            }
        }
        return GameRegistry.GetByUid(uid);
    }

    // ──────────────────────────────────────────────────── board-count query ──

    private static int CountOnBoard(TriggerDefinition def)
    {
        if (_gmInstanceProp == null) return 0;
        var gm = _gmInstanceProp.GetValue(null, null);
        if (gm == null) return 0;

        if (GetMemberValue(gm, "AllCards") is not IList allCards) return 0;

        int count = 0;
        foreach (var card in allCards)
        {
            if (card == null) continue;
            if (!IsInPlayerEnv(card)) continue;

            var model = GetMemberValue(card, "CardModel") as UniqueIDScriptable;
            if (model == null) continue;

            var cardUID = model.UniqueID;
            if (cardUID == def.TriggerCardUID) { count++; continue; }
            if (def.MaxOnBoardUIDs.Count > 0 && def.MaxOnBoardUIDs.Contains(cardUID)) count++;
        }
        return count;
    }

    private static bool IsInPlayerEnv(object card)
    {
        var env = GetMemberValue(card, "CardEnvironment");
        if (env == null) return false;
        return GetMemberValue(env, "MatchesPlayerEnv") is true;
    }

    // ─────────────────────────────────────────────────────────── DTP reader ──

    private static int ReadDayTimePoints()
    {
        if (_gmInstanceProp == null) return -1;
        var gm = _gmInstanceProp.GetValue(null, null);
        if (gm == null) return -1;
        try
        {
            if (_dtpProp  != null) return Convert.ToInt32(_dtpProp.GetValue(gm, null));
            if (_dtpField != null) return Convert.ToInt32(_dtpField.GetValue(gm));
        }
        catch (Exception ex) { Log.Debug($"[TriggerService] ReadDayTimePoints: {ex.GetType().Name}"); }
        return -1;
    }

    // ─────────────────────────────────────────────────── reflection helpers ──

    private static object GetMemberValue(object target, string name)
    {
        if (target == null) return null;
        var t   = target.GetType();
        var key = (t, name);
        if (!_memberCache.TryGetValue(key, out var member))
        {
            member = (MemberInfo)t.GetProperty(name, Flags) ?? t.GetField(name, Flags);
            _memberCache[key] = member;
        }
        if (member is PropertyInfo pi && pi.CanRead) { try { return pi.GetValue(target, null); } catch { } }
        if (member is FieldInfo   fi)                { try { return fi.GetValue(target);        } catch { } }
        return null;
    }
}
