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
    private const string FoodTagName = "tag_HumanFood";
    private const string RottenRemainsUID = "25a487b16088c2046a51935973ba6a90";
    private const string StressStatUID = "3b79a4c6d7e151044a1c56fbbd401d78";

    /// <summary>Vanilla container UIDs that gain the NotSafe tag at load time.</summary>
    private static readonly string[] VanillaOpenStorage =
    {
        "fc102f9646c86fc4d85f25f05713376b", // BasketPlaced
        "ae80b3304fa930748941abc6edc5c884", // HandBasket
        "487c5e8616abfec4198cdf0883135212", // Shelf
        "deb4eeab547ddd64bb6cbb9659f94066", // Sack
        "a2eabda942140a84fafc371513d4d886", // LeatherSack
        "9fc4843f7d5c97044952b9c14902f431", // RusticBarrelLocation
        "4e2b3e00c88f8d14cb52a614584a66d5", // DryingRack
    };

    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static bool Enabled { get; set; } = false;
    public static float DailyChance { get; set; } = 0.35f;
    public static float StressPenalty { get; set; } = 2f;

    private static bool _ready;
    private static bool _loadCompleteProcessed;
    private static int _lastDayTimePoints = int.MinValue;
    private static Type _inGameCardBaseType;
    private static Type _gameManagerType;
    private static PropertyInfo _gameManagerInstanceProp;
    private static object _notSafeTagAsset;
    // Cached DayTimePoints accessor — resolved once in TryCompleteOneTimeSetup to avoid per-frame reflection.
    private static PropertyInfo _dtpProp;
    private static FieldInfo _dtpField;

    /// <summary>Call once at framework Awake — registers a one-shot hook that fires after data load.</summary>
    public static void Init()
    {
        // Runner: a Unity coroutine-host MonoBehaviour owned by the framework's Plugin.
        // We piggy-back on the Plugin's Update() via <see cref="PollUpdate"/>; all init happens there.
        _ready = false;
        _loadCompleteProcessed = false;
    }

    /// <summary>Call from Plugin.Update(). Cheap no-op until ready; then checks day rollover.</summary>
    public static void PollUpdate()
    {
        if (!Enabled) return;
        if (!_ready)
        {
            if (!TryCompleteOneTimeSetup()) return;
        }

        var dtp = ReadDayTimePoints();
        if (dtp < 0) return;
        if (_lastDayTimePoints == int.MinValue) { _lastDayTimePoints = dtp; return; }

        // Day rollover: DayTimePoints wraps each day in the range [0..96]. A day rollover
        // is any drop from last > current (e.g. 2 → 1 → 0 → wraps to ~96 next day;
        // "fresh morning" is when it drops toward 0 then jumps back up).
        if (dtp > _lastDayTimePoints + 50)
        {
            // Wrap detected.
            try { TryRaid(); }
            catch (Exception ex) { Log.Warn($"[WildlifeRaid] raid attempt failed: {ex.Message}"); }
        }
        _lastDayTimePoints = dtp;
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
        Log.Info($"[WildlifeRaid] initialized — enabled={Enabled} chance={DailyChance:F2}");
        return true;
    }

    private static void ResolveTypes()
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (_inGameCardBaseType == null) _inGameCardBaseType = asm.GetType("InGameCardBase", false);
            if (_gameManagerType == null) _gameManagerType = asm.GetType("GameManager", false);
            if (_inGameCardBaseType != null && _gameManagerType != null) break;
        }
        if (_gameManagerType != null)
        {
            _gameManagerInstanceProp = _gameManagerType.GetProperty("Instance",
                BindingFlags.Static | BindingFlags.Public);

            // Cache the DayTimePoints accessor once so PollUpdate doesn't do reflection every frame.
            foreach (var name in new[] { "DayTimePoints", "CurrentDayTimePoints", "DaytimePoints" })
            {
                _dtpProp = _gameManagerType.GetProperty(name, Flags);
                if (_dtpProp != null) break;
                _dtpField = _gameManagerType.GetField(name, Flags);
                if (_dtpField != null) break;
            }
        }
    }

    private static void ResolveTagAsset()
    {
        if (Database.AllScriptableObjectDict.TryGetValue(NotSafeTagName, out var so))
            _notSafeTagAsset = so;
        else
            Log.Warn($"[WildlifeRaid] vanilla CardTag '{NotSafeTagName}' not found — tag injection skipped.");
    }

    private static void InjectTagOnVanillaContainers()
    {
        if (_notSafeTagAsset == null) return;
        int injected = 0;

        foreach (var uid in VanillaOpenStorage)
        {
            var target = GameRegistry.GetByUid(uid);
            if (target == null) continue;

            var tagsField = target.GetType().GetField("CardTags", Flags);
            if (tagsField == null) continue;
            if (!(tagsField.GetValue(target) is IList list)) continue;

            // Skip if already present (idempotent — survives reloads).
            bool already = false;
            foreach (var t in list)
            {
                if (t is UnityEngine.Object uo && uo.name == NotSafeTagName) { already = true; break; }
            }
            if (already) continue;

            try { list.Add(_notSafeTagAsset); injected++; }
            catch (Exception ex) { Log.Debug($"[WildlifeRaid] tag inject failed on {uid}: {ex.Message}"); }
        }
        if (injected > 0) Log.Info($"[WildlifeRaid] injected NotSafeFromAnimals on {injected} vanilla container(s).");
    }

    // ------------------------------------------------------------ tick ------

    private static int ReadDayTimePoints()
    {
        if (_gameManagerInstanceProp == null) return -1;
        var gm = _gameManagerInstanceProp.GetValue(null, null);
        if (gm == null) return -1;
        try
        {
            if (_dtpProp != null) return Convert.ToInt32(_dtpProp.GetValue(gm, null));
            if (_dtpField != null) return Convert.ToInt32(_dtpField.GetValue(gm));
        }
        catch { }
        return -1;
    }

    // ----------------------------------------------------------- raid ------

    private static void TryRaid()
    {
        if (UnityEngine.Random.value > DailyChance) return;
        if (_inGameCardBaseType == null) return;

        var allCards = UnityEngine.Object.FindObjectsOfType(_inGameCardBaseType);
        if (allCards == null || allCards.Length == 0) return;

        var candidates = new List<object>();
        foreach (var c in allCards)
        {
            if (c == null) continue;
            if (!IsInPlayerEnv(c)) continue;
            var model = GetMemberValue(c, "CardModel");
            if (model == null) continue;
            if (!HasTag(model, NotSafeTagName)) continue;
            if (CountFoodIn(c) <= 0) continue;
            candidates.Add(c);
        }
        if (candidates.Count == 0)
        {
            Log.Debug("[WildlifeRaid] roll succeeded but no eligible containers in player env.");
            return;
        }

        var container = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        var foodCard = PickRandomFoodInside(container);
        if (foodCard == null) return;

        var containerName = GetCardDisplayName(container) ?? "a container";
        var foodName = GetCardDisplayName(foodCard) ?? "some food";

        if (TransformToRotten(foodCard))
        {
            Log.Info($"[WildlifeRaid] Wildlife raided {containerName} overnight — {foodName} was spoiled.");
            ApplyStress(StressPenalty);
        }
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
        catch { }
        return false;
    }

    private static int CountFoodIn(object container)
    {
        int total = 0;
        IterateInnerCards(container, card =>
        {
            var model = GetMemberValue(card, "CardModel");
            if (model != null && HasTag(model, FoodTagName)) total++;
        });
        return total;
    }

    private static object PickRandomFoodInside(object container)
    {
        var foods = new List<object>();
        IterateInnerCards(container, card =>
        {
            var model = GetMemberValue(card, "CardModel");
            if (model != null && HasTag(model, FoodTagName)) foods.Add(card);
        });
        if (foods.Count == 0) return null;
        return foods[UnityEngine.Random.Range(0, foods.Count)];
    }

    private static void IterateInnerCards(object container, Action<object> visit)
    {
        if (container == null) return;
        // InGameCardBase.InventorySlots — list of InventorySlot; each slot has AllCards or ContainedCards.
        var slotsMember = GetMemberValue(container, "InventorySlots");
        if (!(slotsMember is IList slots)) return;
        foreach (var slot in slots)
        {
            if (slot == null) continue;
            foreach (var name in new[] { "AllCards", "ContainedCards" })
            {
                var inner = GetMemberValue(slot, name);
                if (!(inner is IList innerList)) continue;
                foreach (var card in innerList)
                {
                    if (card == null) continue;
                    visit(card);
                }
                break;
            }
        }
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

        var cardType = card.GetType();
        try
        {
            var cardModelProp = cardType.GetProperty("CardModel", Flags);
            if (cardModelProp != null && cardModelProp.CanWrite)
            {
                cardModelProp.SetValue(card, rotten);
            }
            else
            {
                var cardModelField = cardType.GetField("CardModel", Flags)
                    ?? cardType.GetField("<CardModel>k__BackingField", Flags);
                if (cardModelField == null) return false;
                cardModelField.SetValue(card, rotten);
            }

            // Preferred: SetupCardSource(CardData, ...) for a full reinit.
            var setup = cardType.GetMethods(Flags)
                .FirstOrDefault(m => m.Name == "SetupCardSource" && m.GetParameters().Length >= 1);
            if (setup != null)
            {
                var p = setup.GetParameters();
                var args = new object[p.Length];
                args[0] = rotten;
                for (int i = 1; i < p.Length; i++)
                {
                    var pt = p[i].ParameterType;
                    args[i] = pt.IsValueType ? Activator.CreateInstance(pt) : null;
                }
                try { setup.Invoke(card, args); return true; }
                catch (Exception ex) { Log.Debug($"[WildlifeRaid] SetupCardSource failed: {ex.Message}"); }
            }

            // Fallback: ResetCard() — repaints visuals/stats from CardModel.
            var reset = cardType.GetMethod("ResetCard", Flags);
            reset?.Invoke(card, null);
            return true;
        }
        catch (Exception ex)
        {
            Log.Debug($"[WildlifeRaid] transform failed: {ex.Message}");
            return false;
        }
    }

    private static void ApplyStress(float amount)
    {
        if (amount <= 0f) return;
        if (_gameManagerType == null || _gameManagerInstanceProp == null) return;
        var gm = _gameManagerInstanceProp.GetValue(null, null);
        if (gm == null) return;

        // Resolve the stat. Prefer GameManager.GetStat(UID) or FindStat; fall back to game registry.
        object statObj = null;
        foreach (var name in new[] { "GetStat", "FindStat" })
        {
            var m = _gameManagerType.GetMethod(name, Flags);
            if (m != null && m.GetParameters().Length == 1)
            {
                try { statObj = m.Invoke(gm, new object[] { StressStatUID }); if (statObj != null) break; }
                catch { }
            }
        }
        if (statObj == null && GameRegistry.GetByUid(StressStatUID) != null)
        {
            // The asset lookup gives us the GameStat SO — runtime "current" stat lives on InGameStat
            // or similar. If GetStat wasn't present, we conservatively skip rather than write to the
            // asset (which would persist across saves).
            Log.Debug("[WildlifeRaid] stress application skipped: no runtime GetStat method found.");
            return;
        }
        if (statObj == null) return;

        var curProp = statObj.GetType().GetProperty("CurrentValue", Flags);
        if (curProp == null || !curProp.CanWrite) return;
        try
        {
            float cur = Convert.ToSingle(curProp.GetValue(statObj, null));
            float max = cur;
            var maxProp = statObj.GetType().GetProperty("MaxValue", Flags);
            if (maxProp != null) { try { max = Convert.ToSingle(maxProp.GetValue(statObj, null)); } catch { } }
            float next = Math.Min(cur + amount, max);
            curProp.SetValue(statObj, Convert.ChangeType(next, curProp.PropertyType), null);
        }
        catch (Exception ex) { Log.Debug($"[WildlifeRaid] stress write failed: {ex.Message}"); }
    }

    // ----------------------------------------------------------- helpers ---

    private static bool IsInPlayerEnv(object card)
    {
        var env = GetMemberValue(card, "CardEnvironment");
        if (env == null) return false;
        var prop = env.GetType().GetProperty("MatchesPlayerEnv", BindingFlags.Instance | BindingFlags.Public);
        if (prop == null) return false;
        try { return (bool)prop.GetValue(env, null); } catch { return false; }
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

    private static object GetMemberValue(object target, string name)
    {
        if (target == null) return null;
        var t = target.GetType();
        var prop = t.GetProperty(name, Flags);
        if (prop != null && prop.CanRead) { try { return prop.GetValue(target, null); } catch { } }
        var field = t.GetField(name, Flags);
        if (field != null) { try { return field.GetValue(target); } catch { } }
        return null;
    }
}
