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
}
