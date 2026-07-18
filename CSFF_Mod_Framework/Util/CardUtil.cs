namespace CSFFModFramework.Util;

/// <summary>
/// Shared utilities for runtime card identity, in-place card transformation, action name
/// extraction, and inventory list discovery. Consolidates patterns that were duplicated
/// across ACT, H&F, and WDI patchers.
/// </summary>
public static class CardUtil
{
    private static readonly BindingFlags All =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // ── Per-type caches ───────────────────────────────────────────────────────
    private static readonly Dictionary<Type, PropertyInfo>  _cardModelPropCache   = new();
    private static readonly Dictionary<Type, FieldInfo>     _inventoryFieldCache  = new();
    private static readonly Dictionary<Type, FieldInfo>     _actionNameFieldCache = new();
    private static readonly Dictionary<Type, FieldInfo>     _defaultTextFieldCache = new();
    private static FieldInfo _uidField;

    // ── Card identity ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the UniqueID string of an in-game card or CardData ScriptableObject.
    /// Fast path: if the object itself is UniqueIDScriptable (cast succeeds).
    /// Fallback: reads CardModel property then UniqueID field.
    /// Returns null on failure.
    /// </summary>
    public static string GetCardUniqueId(object card)
    {
        try
        {
            if (card == null) return null;
            if (card is UniqueIDScriptable s) return s.UniqueID;

            var cardData = GetCardData(card);
            if (cardData == null) return null;
            if (cardData is UniqueIDScriptable s2) return s2.UniqueID;

            _uidField ??= cardData.GetType().GetField("UniqueID", All);
            return _uidField?.GetValue(cardData) as string;
        }
        catch { return null; }
    }

    /// <summary>
    /// Gets the CardModel/CardData ScriptableObject from an InGameCardBase instance.
    /// Results are cached per card type.
    /// </summary>
    public static object GetCardData(object inGameCard)
    {
        try
        {
            if (inGameCard == null) return null;
            var t = inGameCard.GetType();
            if (!_cardModelPropCache.TryGetValue(t, out var prop))
            {
                prop = t.GetProperty("CardModel", All) ?? t.GetProperty("CardData", All);
                _cardModelPropCache[t] = prop;
            }
            return prop?.GetValue(inGameCard);
        }
        catch { return null; }
    }

    // ── Action name extraction ────────────────────────────────────────────────

    /// <summary>
    /// Extracts the display name from a CardAction or CardInteraction via
    /// ActionName.DefaultText. Caches field lookups per action type.
    /// Returns null on failure.
    /// </summary>
    public static string GetActionName(object action)
    {
        try
        {
            if (action == null) return null;
            var at = action.GetType();

            if (!_actionNameFieldCache.TryGetValue(at, out var anField))
            {
                anField = ReflectionHelpers.FindField(at, "ActionName");
                _actionNameFieldCache[at] = anField;
            }
            if (anField == null) return null;

            var nameObj = anField.GetValue(action);
            if (nameObj == null) return null;

            var nt = nameObj.GetType();
            if (!_defaultTextFieldCache.TryGetValue(nt, out var dtField))
            {
                dtField = ReflectionHelpers.FindField(nt, "DefaultText");
                _defaultTextFieldCache[nt] = dtField;
            }
            return dtField?.GetValue(nameObj) as string;
        }
        catch { return null; }
    }

    /// <summary>
    /// Returns the LocalizationKey from an action's ActionName LocalizedString,
    /// or null if not present. Useful as the tier-2 fallback in action matching.
    /// </summary>
    public static string GetActionLocalizationKey(object action)
    {
        try
        {
            if (action == null) return null;
            var at = action.GetType();
            if (!_actionNameFieldCache.TryGetValue(at, out var anField))
            {
                anField = ReflectionHelpers.FindField(at, "ActionName");
                _actionNameFieldCache[at] = anField;
            }
            var nameObj = anField?.GetValue(action);
            return ReflectionHelpers.FindField(nameObj?.GetType(), "LocalizationKey")?.GetValue(nameObj) as string;
        }
        catch { return null; }
    }

    // ── Inventory list discovery ──────────────────────────────────────────────

    private static readonly string[] _knownInventoryFieldNames =
    {
        "ContainedCards", "AllCardsInSlots", "CardsInInventory",
        "InventoryCards", "CardsInSlots", "AllCards",
        "InventorySlots", "Slots", "CardSlots"
    };

    /// <summary>
    /// Finds the IList field that holds inventory items on a card or InventorySlot.
    /// Pass 1 checks well-known field names; pass 2 scans for <c>List&lt;T&gt;</c> where
    /// T's name contains "Card" or "Slot". Results are cached per type.
    /// Returns null if no inventory list is found.
    /// </summary>
    public static IList GetInventoryList(object card)
    {
        try
        {
            if (card == null) return null;
            var t = card.GetType();
            if (!_inventoryFieldCache.TryGetValue(t, out var invField))
            {
                // Pass 1: known names
                foreach (var name in _knownInventoryFieldNames)
                {
                    var f = ReflectionHelpers.FindField(t, name);
                    if (f != null && typeof(IList).IsAssignableFrom(f.FieldType))
                    {
                        invField = f;
                        break;
                    }
                }

                // Pass 2: generic List<T> where T.Name contains "Card" or "Slot"
                if (invField == null)
                {
                    foreach (var f in t.GetFields(All))
                    {
                        if (!f.FieldType.IsGenericType) continue;
                        if (!typeof(IList).IsAssignableFrom(f.FieldType)) continue;
                        var args = f.FieldType.GetGenericArguments();
                        if (args.Length != 1) continue;
                        var elemName = args[0].Name;
                        if ((elemName.Contains("Card") || elemName.Contains("Slot"))
                            && !f.Name.Contains("Tag"))
                        {
                            invField = f;
                            break;
                        }
                    }
                }

                _inventoryFieldCache[t] = invField;
            }
            return invField?.GetValue(card) as IList;
        }
        catch { return null; }
    }

    // ── Member access (forwarded from ReflectionHelpers) ─────────────────────

    /// <summary>
    /// Gets a member value via property first, then field, walking the inheritance chain.
    /// Returns null silently on failure.
    /// </summary>
    public static object GetMemberValue(object instance, string name)
        => ReflectionHelpers.GetMemberValue(instance, name);

    /// <summary>
    /// Sets a member value via property setter (including non-public) then field,
    /// walking the inheritance chain. Returns true on success.
    /// </summary>
    public static bool SetMemberValue(object instance, string name, object value)
        => ReflectionHelpers.SetMemberValue(instance, name, value);

    /// <summary>
    /// Walks the inheritance chain to find a field by name (DeclaredOnly at each level).
    /// </summary>
    public static FieldInfo FindField(Type type, string name)
        => ReflectionHelpers.FindField(type, name);

    /// <summary>
    /// Walks the inheritance chain to find a property by name.
    /// </summary>
    public static PropertyInfo FindProperty(Type type, string name)
        => ReflectionHelpers.FindProperty(type, name);

    // ── Method resolution ────────────────────────────────────────────────────

    /// <summary>
    /// Finds a method on <paramref name="type"/> where the first N parameters have type names
    /// matching <paramref name="paramTypeNames"/> (simple name, full name, or suffix match).
    /// </summary>
    public static MethodInfo FindMethodBySignature(Type type, string methodName, params string[] paramTypeNames)
        => Reflection.ReflectionCache.FindMethodBySignature(type, methodName, paramTypeNames);

    /// <summary>
    /// Returns a bound MethodInfo for <c>UniqueIDScriptable.GetFromID&lt;CardData&gt;(string)</c>.
    /// Scans all assemblies once, then caches. Returns null if types are not found.
    /// </summary>
    public static MethodInfo GetCardDataFromIDMethod()
        => Reflection.ReflectionCache.GetCardDataFromIDMethod();

    /// <summary>
    /// Looks up a CardData by UniqueID using the game's own <c>UniqueIDScriptable.GetFromID</c>.
    /// Returns null if not found or if reflection is unavailable.
    /// </summary>
    public static object GetCardDataById(string uniqueId)
    {
        if (string.IsNullOrEmpty(uniqueId)) return null;
        try { return Reflection.ReflectionCache.GetCardDataFromIDMethod()?.Invoke(null, new object[] { uniqueId }); }
        catch { return null; }
    }

    // ── Type lookup ───────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves a game type by name across all loaded assemblies. Results are cached.
    /// Use for types not accessible at compile-time (e.g. InGameCardBase, GameManager).
    /// </summary>
    public static Type FindGameType(string typeName)
        => Reflection.ReflectionCache.FindType(typeName);

    // ── Cached field/property lookup ─────────────────────────────────────────

    private static readonly Dictionary<(Type, string), FieldInfo>    _fieldCache = new();
    private static readonly Dictionary<(Type, string), PropertyInfo> _propCache  = new();

    /// <summary>
    /// Finds a field by walking the inheritance chain, caching results per (Type, name).
    /// Prefer this over <see cref="FindField"/> for hot paths (e.g. per-frame or per-card loops).
    /// </summary>
    public static FieldInfo GetCachedField(Type type, string name)
    {
        if (type == null || name == null) return null;
        var key = (type, name);
        if (_fieldCache.TryGetValue(key, out var fi)) return fi;
        return _fieldCache[key] = ReflectionHelpers.FindField(type, name);
    }

    /// <summary>
    /// Finds a property by walking the inheritance chain, caching results per (Type, name).
    /// Prefer this over <see cref="FindProperty"/> for hot paths.
    /// </summary>
    public static PropertyInfo GetCachedProperty(Type type, string name)
    {
        if (type == null || name == null) return null;
        var key = (type, name);
        if (_propCache.TryGetValue(key, out var pi)) return pi;
        return _propCache[key] = ReflectionHelpers.FindProperty(type, name);
    }

    // ── Array append ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a new array with <paramref name="element"/> appended to the end.
    /// Replaces the common inline Array.CreateInstance / Array.Copy / SetValue pattern.
    /// </summary>
    public static T[] AppendToArray<T>(T[] array, T element)
        => ReflectionHelpers.AppendToArray(array, element);

    // ── GameManager singleton access ──────────────────────────────────────────

    private static Type         _gmType;
    private static PropertyInfo _gmInstanceProp;
    private static FieldInfo    _gmInstanceField;
    private static bool         _gmReflected;

    /// <summary>
    /// Returns the live GameManager singleton. Searches the MBSingleton&lt;T&gt; hierarchy
    /// for the static Instance property or field. Returns null before the game initializes.
    /// </summary>
    public static object GetGameManagerInstance()
    {
        if (!_gmReflected)
        {
            _gmReflected = true;
            _gmType = Reflection.ReflectionCache.FindType("GameManager");
            if (_gmType != null)
            {
                const BindingFlags StaticAll = BindingFlags.Static | BindingFlags.Public
                    | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
                _gmInstanceProp  = _gmType.GetProperty("Instance", StaticAll);
                _gmInstanceField = _gmInstanceProp == null
                    ? _gmType.GetField("Instance", StaticAll) : null;
            }
        }
        if (_gmType == null) return null;
        try
        {
            if (_gmInstanceProp  != null) return _gmInstanceProp.GetValue(null, null);
            if (_gmInstanceField != null) return _gmInstanceField.GetValue(null);
        }
        catch { }
        return null;
    }

    // ── World-map / gate condition state queries ──────────────────────────────
    //
    // Shared by ConnectionGateService, ConditionalDropService, and SealableGateService —
    // each independently reimplemented these before this consolidation (2026-07-02).

    /// <summary>
    /// Returns true if the current player character has the given perk UID equipped.
    /// Walks <c>GameManager.CurrentPlayerCharacter.CharacterPerks</c>. False on any
    /// missing link (no GameManager, no character, no perks) or reflection failure.
    /// </summary>
    public static bool IsPerkEquipped(string perkUID)
    {
        if (string.IsNullOrEmpty(perkUID)) return false;
        try
        {
            var gm = GetGameManagerInstance();
            if (gm == null) return false;
            var character = GetMemberValue(gm, "CurrentPlayerCharacter");
            if (character == null) return false;
            if (GetMemberValue(character, "CharacterPerks") is not IEnumerable perks) return false;
            foreach (var perk in perks)
            {
                if (perk == null) continue;
                if (perkUID.Equals(GetMemberValue(perk, "UniqueID") as string, StringComparison.Ordinal))
                    return true;
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Returns true if <paramref name="impUID"/> is present in the <c>CurrentlyBuiltImprovements</c>
    /// list of the <see cref="GameManager"/>.EnvironmentsData entry for <paramref name="envUID"/>.
    /// Matches the entry by <c>EnvironmentID</c>, <c>DictionaryKey</c>, or raw dictionary key,
    /// since different EnvID construction paths populate different fields. False on any
    /// missing link or reflection failure.
    /// </summary>
    public static bool IsImprovementBuilt(string envUID, string impUID)
    {
        if (string.IsNullOrEmpty(envUID) || string.IsNullOrEmpty(impUID)) return false;
        try
        {
            var gm = GetGameManagerInstance();
            if (gm == null) return false;
            var envDataField = GetCachedField(gm.GetType(), "EnvironmentsData");
            if (envDataField?.GetValue(gm) is not IDictionary envData) return false;

            foreach (DictionaryEntry entry in envData)
            {
                var value = entry.Value;
                if (value == null) continue;
                var vt = value.GetType();
                var envId   = GetCachedField(vt, "EnvironmentID")?.GetValue(value) as string;
                var dictKey = GetCachedField(vt, "DictionaryKey")?.GetValue(value) as string;
                bool isTarget =
                    envUID.Equals(envId,               StringComparison.Ordinal) ||
                    envUID.Equals(dictKey,             StringComparison.Ordinal) ||
                    envUID.Equals(entry.Key as string, StringComparison.Ordinal);
                if (!isTarget) continue;

                // An env can have multiple entries (EnvID constructed differently); scan all matches.
                if (GetCachedField(vt, "CurrentlyBuiltImprovements")?.GetValue(value) is IEnumerable built)
                    foreach (var uid in built)
                        if (impUID.Equals(uid as string, StringComparison.Ordinal)) return true;
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Adds <paramref name="impUID"/> to the <c>CurrentlyBuiltImprovements</c> list of the
    /// <c>GameManager.EnvironmentsData</c> entry for <paramref name="envUID"/>, creating that
    /// entry (via <c>GetEnvSaveData(EnvID, _CreateIfNull:true)</c>) if the env has never been
    /// visited. Idempotent — a UID already present is not duplicated. Mirrors the write-side of
    /// <see cref="IsImprovementBuilt"/>; both are consumed by <c>ExplorationPopup.SetupImprovements</c>
    /// (built-item ordering) and by <c>ConnectionGateService</c>/<c>ConditionalDropService</c>'s
    /// <c>ImprovementBuilt</c> gate condition. Returns false on any missing link or reflection failure.
    /// </summary>
    public static bool MarkImprovementBuilt(string envUID, string impUID)
    {
        if (string.IsNullOrEmpty(envUID) || string.IsNullOrEmpty(impUID)) return false;
        try
        {
            var gm = GetGameManagerInstance();
            if (gm == null) return false;

            var envDataField = GetCachedField(gm.GetType(), "EnvironmentsData");
            if (envDataField?.GetValue(gm) is IDictionary envData)
            {
                foreach (DictionaryEntry entry in envData)
                {
                    var value = entry.Value;
                    if (value == null) continue;
                    var vt = value.GetType();
                    var envId   = GetCachedField(vt, "EnvironmentID")?.GetValue(value) as string;
                    var dictKey = GetCachedField(vt, "DictionaryKey")?.GetValue(value) as string;
                    bool isTarget =
                        envUID.Equals(envId,               StringComparison.Ordinal) ||
                        envUID.Equals(dictKey,             StringComparison.Ordinal) ||
                        envUID.Equals(entry.Key as string, StringComparison.Ordinal);
                    if (!isTarget) continue;

                    var cbiField = GetCachedField(vt, "CurrentlyBuiltImprovements");
                    if (cbiField == null) return false;
                    if (cbiField.GetValue(value) is not IList built)
                    {
                        built = new List<string>();
                        cbiField.SetValue(value, built);
                    }
                    if (!built.Contains(impUID)) built.Add(impUID);
                    return true;
                }
            }

            // Env not yet visited (no EnvironmentsData entry) — create it so the mark persists.
            var envIdType = FindGameType("EnvID");
            var getEnvSaveData = gm.GetType().GetMethod("GetEnvSaveData", All);
            if (envIdType == null || getEnvSaveData == null) return false;

            var envId2 = Activator.CreateInstance(envIdType, new object[] { envUID });
            var envSaveObj = getEnvSaveData.Invoke(gm, new object[] { envId2, true });
            if (envSaveObj == null) return false;

            var svt = envSaveObj.GetType();
            var svtCbi = GetCachedField(svt, "CurrentlyBuiltImprovements");
            if (svtCbi == null) return false;
            if (svtCbi.GetValue(envSaveObj) is not IList newBuilt)
            {
                newBuilt = new List<string>();
                svtCbi.SetValue(envSaveObj, newBuilt);
            }
            if (!newBuilt.Contains(impUID)) newBuilt.Add(impUID);
            return true;
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Finds the player's live <c>CardUnlockConditions</c> entry for <paramref name="cardUID"/> in
    /// <c>GameManager.UnlockableCards</c> and calls its public <c>SetStartUnlocked()</c> — bypassing
    /// that card's <c>CardsOnBoard</c>/<c>TagsOnBoard</c>/etc. discovery gate for this run only (the
    /// JSON-authored gate is untouched, so players without the triggering condition still discover
    /// it normally). Returns false if the card has no matching entry or on reflection failure.
    /// </summary>
    public static bool ForceUnlockCard(string cardUID)
    {
        if (string.IsNullOrEmpty(cardUID)) return false;
        try
        {
            var gm = GetGameManagerInstance();
            if (gm == null) return false;
            if (GetMemberValue(gm, "UnlockableCards") is not IEnumerable unlockable) return false;

            foreach (var uc in unlockable)
            {
                if (uc == null) continue;
                var unlockedCard = GetMemberValue(uc, "UnlockedCard");
                if (unlockedCard == null) continue;
                if (!cardUID.Equals(GetCardUniqueId(unlockedCard), StringComparison.Ordinal)) continue;

                var setStartUnlocked = uc.GetType().GetMethod("SetStartUnlocked", All);
                if (setStartUnlocked == null) return false;
                setStartUnlocked.Invoke(uc, Array.Empty<object>());
                return true;
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Queues <paramref name="improvement"/> on <c>GameManager.AddImprovementToBuild</c> — the next
    /// time this CardData spawns as an <c>InGameCardBase</c> (via the normal unlock pipeline, e.g.
    /// after <see cref="ForceUnlockCard"/>), the engine's own <c>SpawnCard</c> immediately calls
    /// <c>CompleteImprovement()</c> on it (sets <c>BlueprintData.CurrentStage</c> to fully built)
    /// instead of leaving it at stage 0 awaiting materials. Returns false on reflection failure.
    /// </summary>
    public static bool QueueImprovementAutoComplete(object improvement)
    {
        if (improvement == null) return false;
        try
        {
            var gm = GetGameManagerInstance();
            if (gm == null) return false;
            var method = gm.GetType().GetMethod("AddImprovementToBuild", All);
            if (method == null) return false;
            method.Invoke(gm, new object[] { improvement });
            return true;
        }
        catch { return false; }
    }

    // ── Type conversion helpers ───────────────────────────────────────────────

    /// <summary>Converts any boxed numeric value to float. Returns 0 on null or failure.</summary>
    public static float ToFloat(object value)
    {
        if (value is float f)  return f;
        if (value is double d) return (float)d;
        if (value is int i)    return i;
        if (value is long l)   return l;
        if (value == null)     return 0f;
        try { return Convert.ToSingle(value); } catch { return 0f; }
    }

    /// <summary>Converts any boxed numeric value to int. Returns 0 on null or failure.</summary>
    public static int ToInt(object value)
    {
        if (value is int i)    return i;
        if (value is long l)   return (int)l;
        if (value is float f)  return (int)f;
        if (value is double d) return (int)d;
        if (value == null)     return 0;
        try { return Convert.ToInt32(value); } catch { return 0; }
    }

    /// <summary>Converts a boxed bool or numeric value to bool. Returns false on null or failure.</summary>
    public static bool ToBool(object value)
    {
        if (value is bool b) return b;
        if (value == null)   return false;
        try { return Convert.ToBoolean(value); } catch { return false; }
    }

    // ── High-level card transform ─────────────────────────────────────────────

    /// <summary>
    /// Performs a complete in-place card model swap: resolves <paramref name="targetUniqueId"/>
    /// to a CardData, calls <see cref="TrySetCardModel"/>, then <see cref="ReinitCard"/>.
    /// Returns true on success.
    /// For transforms that also need placement restore or runtime-state reset (durabilities,
    /// latches), call the lower-level methods after this returns.
    /// </summary>
    public static bool TransformCardInPlace(object card, string targetUniqueId)
    {
        if (card == null || string.IsNullOrEmpty(targetUniqueId)) return false;
        var targetData = GetCardDataById(targetUniqueId);
        if (targetData == null) return false;
        if (!TrySetCardModel(card, targetData)) return false;
        ReinitCard(card, targetData);
        return true;
    }

    // ── Batch AllData queries ─────────────────────────────────────────────────

    /// <summary>
    /// Returns all loaded CardData ScriptableObjects where <paramref name="predicate"/> returns true.
    /// Iterates the framework's SO cache — call only after LoadMainGameData fires.
    /// For UniqueID lookups, <see cref="FindCardsByUniqueIds"/> is faster (O(1) per ID).
    /// </summary>
    public static IEnumerable<object> FindCardsWhere(Func<object, bool> predicate)
    {
        var cardDataType = Reflection.ReflectionCache.FindType("CardData");
        if (cardDataType == null) yield break;
        foreach (var so in Data.Database.GetAllOfType(cardDataType))
        {
            if (so == null) continue;
            if (predicate == null || predicate(so))
                yield return so;
        }
    }

    /// <summary>
    /// Returns CardData objects whose UniqueID matches any element in <paramref name="ids"/>.
    /// Uses the game's own UniqueIDScriptable registry — O(1) per ID.
    /// </summary>
    public static IReadOnlyList<object> FindCardsByUniqueIds(IEnumerable<string> ids)
    {
        var result = new List<object>();
        foreach (var id in ids)
        {
            if (string.IsNullOrEmpty(id)) continue;
            var card = GetCardDataById(id);
            if (card != null) result.Add(card);
        }
        return result;
    }

    // ── Durability stat modifier ──────────────────────────────────────────────

    /// <summary>
    /// Adds <paramref name="delta"/> to a float durability field on an InGameCardBase instance.
    /// Three search paths: (1) flat <c>statName</c> property/field directly on the card,
    /// (2) DurabilityStats/CardDurabilities/Durabilities container → sub-stat.CurrentValue,
    /// stripping a leading "Current" prefix to find the sub-stat name.
    /// Returns true if any path succeeded.
    /// </summary>
    public static bool ModifyDurabilityStat(object card, string statName, float delta)
    {
        if (card == null || string.IsNullOrEmpty(statName)) return false;
        try
        {
            var cardType = card.GetType();

            // Path 1: flat property on the card (e.g. CurrentProgress, CurrentSpecial4)
            var directProp = ReflectionHelpers.FindProperty(cardType, statName);
            if (directProp?.CanRead == true && directProp.CanWrite)
            {
                directProp.SetValue(card, Convert.ToSingle(directProp.GetValue(card)) + delta);
                return true;
            }

            // Path 2: flat field on the card (float or double)
            var directField = ReflectionHelpers.FindField(cardType, statName);
            if (directField != null
                && (directField.FieldType == typeof(float) || directField.FieldType == typeof(double)))
            {
                directField.SetValue(card, Convert.ToSingle(directField.GetValue(card)) + delta);
                return true;
            }

            // Path 3: container → sub-stat → CurrentValue/FloatValue
            string innerName = statName.StartsWith("Current", StringComparison.Ordinal)
                ? statName.Substring("Current".Length) : statName;

            foreach (var containerName in _durabilityContainerNames)
            {
                var cField = ReflectionHelpers.FindField(cardType, containerName);
                if (cField == null) continue;
                var container = cField.GetValue(card);
                if (container == null) continue;
                if (TryModifySubStat(container, cField, card, innerName, delta))
                    return true;
            }
        }
        catch { }
        return false;
    }

    private static readonly string[] _durabilityContainerNames =
        { "DurabilityStats", "CardDurabilities", "Durabilities" };

    private static bool TryModifySubStat(object container, FieldInfo containerField,
        object card, string innerName, float delta)
    {
        var ct = container.GetType();
        var innerProp  = ReflectionHelpers.FindProperty(ct, innerName);
        var innerField = innerProp == null ? ReflectionHelpers.FindField(ct, innerName) : null;
        var innerObj   = innerProp != null ? innerProp.GetValue(container)
                                          : innerField?.GetValue(container);
        if (innerObj == null) return false;

        var it = innerObj.GetType();
        var cvProp = ReflectionHelpers.FindProperty(it, "CurrentValue")
                  ?? ReflectionHelpers.FindProperty(it, "FloatValue");
        if (cvProp?.CanRead == true && cvProp.CanWrite)
        {
            cvProp.SetValue(innerObj, Convert.ToSingle(cvProp.GetValue(innerObj)) + delta);
            WriteBackIfValueType(innerObj, it, innerField, container, containerField, card);
            return true;
        }

        var cvField = ReflectionHelpers.FindField(it, "CurrentValue")
                   ?? ReflectionHelpers.FindField(it, "FloatValue");
        if (cvField != null)
        {
            cvField.SetValue(innerObj, Convert.ToSingle(cvField.GetValue(innerObj)) + delta);
            WriteBackIfValueType(innerObj, it, innerField, container, containerField, card);
            return true;
        }
        return false;
    }

    private static void WriteBackIfValueType(object innerObj, Type innerType, FieldInfo innerField,
        object container, FieldInfo containerField, object card)
    {
        if (!innerType.IsValueType || innerField == null) return;
        innerField.SetValue(container, innerObj);
        if (containerField.FieldType.IsValueType)
            containerField.SetValue(card, container);
    }

    // ── In-place card model swap ──────────────────────────────────────────────

    // Per-type reflection for the model swap — invalidated when card type changes.
    private static Type        _lastCardType;
    private static PropertyInfo _modelSetProp;
    private static MethodInfo   _modelSetter;
    private static FieldInfo    _modelBackingField;
    private static MethodInfo   _setupCardSourceMethod;
    private static MethodInfo   _resetCardMethod;

    /// <summary>
    /// Sets the CardModel property on an InGameCardBase-derived instance using a
    /// 3-tier fallback strategy:
    /// <list type="number">
    ///   <item>Public/writable property setter.</item>
    ///   <item>Non-public property setter invoked via reflection.</item>
    ///   <item>Auto-property backing field <c>&lt;CardModel&gt;k__BackingField</c>.</item>
    /// </list>
    /// Returns true if any tier succeeded.
    /// </summary>
    public static bool TrySetCardModel(object card, object cardData)
    {
        if (card == null || cardData == null) return false;

        var ct = card.GetType();
        if (ct != _lastCardType)
        {
            _lastCardType = ct;
            _modelSetProp = ct.GetProperty("CardModel", All);
            _modelSetter  = _modelSetProp?.GetSetMethod(nonPublic: true);
            _modelBackingField = ReflectionHelpers.FindField(ct, "<CardModel>k__BackingField");
            _setupCardSourceMethod = ct.GetMethods(All).FirstOrDefault(m =>
                m.Name == "SetupCardSource" && m.GetParameters().Length >= 1
                && m.GetParameters()[0].ParameterType.IsInstanceOfType(cardData));
            _resetCardMethod = ct.GetMethod("ResetCard", All, null, Type.EmptyTypes, null);
        }

        // Path 1: writable property
        try
        {
            if (_modelSetProp?.CanWrite == true)
            {
                _modelSetProp.SetValue(card, cardData);
                return true;
            }
        }
        catch { }

        // Path 2: non-public setter
        try
        {
            if (_modelSetter != null)
            {
                _modelSetter.Invoke(card, new[] { cardData });
                return true;
            }
        }
        catch { }

        // Path 3: backing field
        try
        {
            if (_modelBackingField != null)
            {
                _modelBackingField.SetValue(card, cardData);
                return true;
            }
        }
        catch { }

        return false;
    }

    /// <summary>
    /// Reinitializes a card after an in-place CardModel swap by calling
    /// <c>SetupCardSource(cardData, ...)</c> (preferred) or <c>ResetCard()</c> (fallback).
    /// The card type must have been passed to <see cref="TrySetCardModel"/> first so
    /// the method cache is warm.
    /// </summary>
    public static void ReinitCard(object card, object cardData)
    {
        if (card == null) return;
        try
        {
            if (_setupCardSourceMethod != null)
            {
                var p = _setupCardSourceMethod.GetParameters();
                var args = new object[p.Length];
                args[0] = cardData;
                for (int i = 1; i < p.Length; i++)
                {
                    var pt = p[i].ParameterType;
                    args[i] = pt.IsValueType ? Activator.CreateInstance(pt) : null;
                }
                _setupCardSourceMethod.Invoke(card, args);
                return;
            }
            _resetCardMethod?.Invoke(card, null);
        }
        catch { }
    }

    // ── Absolute durability get/set (Tier 1) ─────────────────────────────────
    //
    // One unified path over the two runtime stat shapes:
    //   (a) EA 0.62b+: flat properties on InGameCardBase (CurrentProgress, CurrentSpecial1–4,
    //       CurrentFuel, CurrentUsageDurability, CurrentSpoilage)
    //   (b) older / container shape: DurabilityStats|CardDurabilities → <stat> → CurrentValue|FloatValue
    // Lifted from WDI FishpondPopulationPatch GetStat/SetStat (the proven implementation)
    // and CMC QualitySplitPatch. Accepts both JSON stat names ("SpoilageTime") and
    // runtime member names ("CurrentSpoilage").

    /// <summary>
    /// Maps a JSON-side stat name to the flat runtime member on InGameCardBase.
    /// Returns null for unknown names.
    /// </summary>
    private static string MapStatToRuntimeMember(string statName) => statName switch
    {
        "Progress"           => "CurrentProgress",
        "SpecialDurability1" => "CurrentSpecial1",
        "SpecialDurability2" => "CurrentSpecial2",
        "SpecialDurability3" => "CurrentSpecial3",
        "SpecialDurability4" => "CurrentSpecial4",
        "FuelCapacity"       => "CurrentFuel",
        "UsageDurability"    => "CurrentUsageDurability",
        "SpoilageTime"       => "CurrentSpoilage",
        _                    => statName != null && statName.StartsWith("Current", StringComparison.Ordinal)
                                    ? statName : null,
    };

    /// <summary>All eight JSON-side durability stat names, in SpecialDurability/Progress order.</summary>
    public static readonly string[] AllDurabilityStats =
    {
        "Progress", "SpecialDurability1", "SpecialDurability2", "SpecialDurability3",
        "SpecialDurability4", "FuelCapacity", "UsageDurability", "SpoilageTime",
    };

    /// <summary>
    /// Reads the current value of a durability stat on an InGameCardBase instance.
    /// <paramref name="statName"/> accepts JSON names ("SpoilageTime", "SpecialDurability4")
    /// or runtime names ("CurrentSpoilage"). Returns <see cref="float.NaN"/> on failure —
    /// always check with <see cref="float.IsNaN(float)"/>, not a sentinel comparison.
    /// </summary>
    public static float GetDurability(object card, string statName)
    {
        if (card == null || string.IsNullOrEmpty(statName)) return float.NaN;
        try
        {
            var cardType = card.GetType();

            // Path A: flat runtime property/field on the card.
            var directName = MapStatToRuntimeMember(statName);
            if (directName != null)
            {
                var dp = GetCachedProperty(cardType, directName);
                if (dp?.CanRead == true) return Convert.ToSingle(dp.GetValue(card));
                var df = GetCachedField(cardType, directName);
                if (df != null) return Convert.ToSingle(df.GetValue(card));
            }

            // Path B: container → sub-stat → CurrentValue/FloatValue.
            var innerName = statName.StartsWith("Current", StringComparison.Ordinal)
                ? statName.Substring("Current".Length) : statName;
            foreach (var containerName in _durabilityContainerNames)
            {
                var cField = GetCachedField(cardType, containerName);
                if (cField == null) continue;
                var stats = cField.GetValue(card);
                if (stats == null) continue;
                var dur = ReflectionHelpers.GetMemberValue(stats, innerName);
                if (dur == null) continue;
                var cv = ReflectionHelpers.GetMemberValue(dur, "CurrentValue")
                      ?? ReflectionHelpers.GetMemberValue(dur, "FloatValue");
                if (cv != null) return Convert.ToSingle(cv);
            }
        }
        catch { }
        return float.NaN;
    }

    /// <summary>
    /// Reads the maximum value of a durability stat on an InGameCardBase instance.
    /// Path 1: runtime durability container → sub-stat → MaxValue. Path 2: the card's
    /// CardModel JSON stat (CardData.&lt;stat&gt;.MaxValue). Accepts JSON names
    /// ("SpoilageTime") or runtime names ("CurrentSpoilage"). Returns
    /// <see cref="float.NaN"/> on failure — check with <see cref="float.IsNaN(float)"/>.
    /// </summary>
    public static float GetDurabilityMax(object card, string statName)
    {
        if (card == null || string.IsNullOrEmpty(statName)) return float.NaN;
        try
        {
            var innerName = statName.StartsWith("Current", StringComparison.Ordinal)
                ? statName.Substring("Current".Length) : statName;

            // Path 1: runtime container stat MaxValue.
            var cardType = card.GetType();
            foreach (var containerName in _durabilityContainerNames)
            {
                var cField = GetCachedField(cardType, containerName);
                if (cField == null) continue;
                var stats = cField.GetValue(card);
                if (stats == null) continue;
                var dur = ReflectionHelpers.GetMemberValue(stats, innerName);
                if (dur == null) continue;
                var mv = ReflectionHelpers.GetMemberValue(dur, "MaxValue");
                if (mv != null) return Convert.ToSingle(mv);
            }

            // Path 2: CardModel JSON-side stat MaxValue.
            var model = GetCardData(card);
            if (model != null)
            {
                var dur = ReflectionHelpers.GetMemberValue(model, innerName);
                var mv = dur == null ? null : ReflectionHelpers.GetMemberValue(dur, "MaxValue");
                if (mv != null) return Convert.ToSingle(mv);
            }
        }
        catch { }
        return float.NaN;
    }

    /// <summary>
    /// Writes an absolute value to a durability stat on an InGameCardBase instance.
    /// Write paths per layer: writable property → non-public setter → backing field →
    /// direct field, with value-type write-back at every container boundary.
    /// Returns true on success.
    /// </summary>
    public static bool SetDurability(object card, string statName, float value)
    {
        if (card == null || string.IsNullOrEmpty(statName)) return false;
        try
        {
            var cardType = card.GetType();

            // Path A: flat runtime property/field on the card.
            var directName = MapStatToRuntimeMember(statName);
            if (directName != null)
            {
                var dp = GetCachedProperty(cardType, directName);
                if (dp != null)
                {
                    var setter = dp.GetSetMethod(nonPublic: true);
                    if (setter != null) { setter.Invoke(card, new object[] { value }); return true; }
                }
                var df = GetCachedField(cardType, directName);
                if (df != null) { df.SetValue(card, value); return true; }
            }

            // Path B: container → sub-stat → CurrentValue/FloatValue with write-back.
            var innerName = statName.StartsWith("Current", StringComparison.Ordinal)
                ? statName.Substring("Current".Length) : statName;
            foreach (var containerName in _durabilityContainerNames)
            {
                var cField = GetCachedField(cardType, containerName);
                if (cField == null) continue;
                var stats = cField.GetValue(card);
                if (stats == null) continue;

                var statsType = stats.GetType();
                var durProp  = ReflectionHelpers.FindProperty(statsType, innerName);
                var durField = durProp == null ? ReflectionHelpers.FindField(statsType, innerName) : null;
                var dur = durProp != null ? durProp.GetValue(stats) : durField?.GetValue(stats);
                if (dur == null) continue;

                if (!WriteCurrentValue(dur, value)) continue;

                // Value-type write-back at each boundary (struct copies don't propagate).
                if (dur.GetType().IsValueType)
                {
                    if (durProp?.CanWrite == true) durProp.SetValue(stats, dur);
                    else durField?.SetValue(stats, dur);
                }
                if (statsType.IsValueType) cField.SetValue(card, stats);
                return true;
            }
        }
        catch { }
        return false;
    }

    private static bool WriteCurrentValue(object dur, float value)
    {
        var durType = dur.GetType();
        var cvProp = ReflectionHelpers.FindProperty(durType, "CurrentValue")
                  ?? ReflectionHelpers.FindProperty(durType, "FloatValue");
        if (cvProp != null)
        {
            var setter = cvProp.GetSetMethod(nonPublic: true);
            if (setter != null) { setter.Invoke(dur, new object[] { value }); return true; }
            // Auto-property with no setter at all — backing field, walking base types.
            var bf = ReflectionHelpers.FindField(durType, $"<{cvProp.Name}>k__BackingField");
            if (bf != null) { bf.SetValue(dur, value); return true; }
        }
        var cvField = ReflectionHelpers.FindField(durType, "FloatValue")
                   ?? ReflectionHelpers.FindField(durType, "CurrentValue");
        if (cvField != null) { cvField.SetValue(dur, value); return true; }
        return false;
    }

    // ── Stat-preserving in-place transform (Tier 1) ──────────────────────────

    /// <summary>
    /// Transforms a card in place (CardModel swap + reinit) while preserving the given
    /// durability stats across the transform — SetupCardSource resets them to the target
    /// JSON defaults, so they are captured before and restored after. Pass no stat names
    /// to preserve all eight (<see cref="AllDurabilityStats"/>). Formalizes the WDI
    /// fishpond capture/restore sequence. Returns true if the transform succeeded
    /// (stat restore is best-effort per stat).
    /// </summary>
    public static bool TransformInPlacePreservingStats(object card, string targetUniqueId, params string[] statsToPreserve)
    {
        if (card == null || string.IsNullOrEmpty(targetUniqueId)) return false;
        var stats = statsToPreserve is { Length: > 0 } ? statsToPreserve : AllDurabilityStats;

        var captured = new List<(string name, float value)>(stats.Length);
        foreach (var s in stats)
        {
            var v = GetDurability(card, s);
            if (!float.IsNaN(v)) captured.Add((s, v));
        }

        if (!TransformCardInPlace(card, targetUniqueId)) return false;

        foreach (var (name, value) in captured)
            SetDurability(card, name, value);
        return true;
    }

    // ── Card removal (Tier 1) ────────────────────────────────────────────────

    private static readonly Dictionary<Type, MethodInfo> _removeMethodCache = new();

    /// <summary>
    /// Removes a card via the game's own removal methods (RemoveFromGame → DestroyCard →
    /// DestroyCardFromInventory, whichever exists on this game version), with flexible
    /// 0–2 bool parameter signatures. Returns true if a method was found and invoked.
    ///
    /// <para>CAUTION: for cards inside another card's inventory these paths trigger
    /// OnDestroy callbacks that can relocate cards to adjacent containers (CLAUDE.md
    /// §Runtime Card Removal). For in-inventory cards prefer
    /// <see cref="Api.Inventory.Eject"/> or <see cref="RemoveCardCleanly"/>.</para>
    /// </summary>
    public static bool TryRemoveCard(object card)
    {
        if (card == null) return false;
        try
        {
            var cardType = card.GetType();
            if (!_removeMethodCache.TryGetValue(cardType, out var m))
            {
                foreach (var n in new[] { "RemoveFromGame", "DestroyCard", "DestroyCardFromInventory" })
                {
                    foreach (var cand in cardType.GetMethods(All).Where(x => x.Name == n))
                    {
                        var p = cand.GetParameters();
                        if (p.Length == 0 || (p.Length <= 2 && p.All(pp => pp.ParameterType == typeof(bool))))
                        {
                            m = cand;
                            break;
                        }
                    }
                    if (m != null) break;
                }
                _removeMethodCache[cardType] = m;
            }
            if (m == null)
            {
                Log.Warn($"CardUtil.TryRemoveCard: no removal method found on {cardType.Name}");
                return false;
            }

            var pms = m.GetParameters();
            if (pms.Length == 0) m.Invoke(card, null);
            else if (pms.Length == 1) m.Invoke(card, new object[] { true });
            else m.Invoke(card, new object[] { true, true });
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"CardUtil.TryRemoveCard failed: {ex.InnerException?.Message ?? ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Removes a card without triggering OnDestroy relocation: in-place CardModel swap
    /// to <paramref name="placeholderUniqueId"/> (an inert card the calling mod defines,
    /// e.g. a "consumed" stub) plus reinit. The safe removal path for cards inside
    /// another card's inventory. Returns true on success.
    /// </summary>
    public static bool RemoveCardCleanly(object card, string placeholderUniqueId)
        => TransformCardInPlace(card, placeholderUniqueId);
}
