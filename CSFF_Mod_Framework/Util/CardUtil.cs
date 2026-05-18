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
