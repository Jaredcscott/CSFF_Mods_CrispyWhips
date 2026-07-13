using System.Reflection;
using CSFFModFramework.Data;
using CSFFModFramework.Discovery;
using CSFFModFramework.Util;

namespace CSFFModFramework.Injection;

/// <summary>
/// Hub Portal injection helper — provides <c>InjectIntoDefaultEnvCardDrops</c> for other
/// framework services to append cards into CT4 environment DefaultEnvCardDrops at load time.
///
/// <para>Portal travel is handled entirely through the portable Portal Kit + PortalService,
/// which gives each mod's CT4 InstancedEnvironment its own "Return to Portal" exit card
/// automatically. The <c>WorldMap/HubPortals.json</c> schema is no longer used.</para>
/// </summary>
internal static class HubPortalInjector
{
    private const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static bool      _typesResolved;
    private static FieldInfo _cobField;          // CardData.CardsOnBoard        (CardDrop[])
    private static FieldInfo _dedcField;         // CardData.DefaultEnvCardDrops (CardDrop[])
    private static Type      _cardDropType;
    private static FieldInfo _droppedCardField;  // CardDrop.DroppedCard
    private static FieldInfo _quantityField;     // CardDrop.Quantity (Vector2Int)

    // ─── public entry point ──────────────────────────────────────────────────

    /// <summary>
    /// Appends a CardDrop for <paramref name="cardUid"/> into <paramref name="envUid"/>'s
    /// <c>DefaultEnvCardDrops</c>. Used by <c>PortalService</c> to inject the hub exit card
    /// into each mod's CT4 hub.
    /// Returns false if types cannot be resolved or UIDs are not found.
    /// </summary>
    internal static bool InjectIntoDefaultEnvCardDrops(string envUid, string cardUid)
    {
        if (!TryResolveTypes()) return false;
        var env  = GameRegistry.GetByUid(envUid)  as CardData;
        var card = GameRegistry.GetByUid(cardUid) as CardData;
        if (env == null)  { Log.Warn($"HubPortalInjector: env '{envUid}' not found");   return false; }
        if (card == null) { Log.Warn($"HubPortalInjector: card '{cardUid}' not found"); return false; }
        return AppendCardDrop(_dedcField, env, card);
    }

    // ─── reflection helpers ──────────────────────────────────────────────────

    private static bool TryResolveTypes()
    {
        if (_typesResolved) return _cardDropType != null;
        _typesResolved = true;

        var cardDataType = AccessTools.TypeByName("CardData");
        if (cardDataType == null) return false;

        _cobField  = FindField(cardDataType, "CardsOnBoard");
        _dedcField = FindField(cardDataType, "DefaultEnvCardDrops");
        if (_cobField == null || _dedcField == null)
        {
            Log.Warn("HubPortalInjector: CardsOnBoard or DefaultEnvCardDrops field not found on CardData");
            return false;
        }

        // Derive CardDrop element type from either field's array type.
        _cardDropType = ElementType(_cobField.FieldType) ?? ElementType(_dedcField.FieldType);
        if (_cardDropType == null)
        {
            Log.Warn("HubPortalInjector: could not determine CardDrop element type");
            return false;
        }

        _droppedCardField = FindField(_cardDropType, "DroppedCard");
        _quantityField    = FindField(_cardDropType, "Quantity");
        return _droppedCardField != null;
    }

    private static bool AppendCardDrop(FieldInfo field, object target, CardData card)
    {
        if (field == null || _cardDropType == null || _droppedCardField == null) return false;

        var current = (field.GetValue(target) as Array) ?? Array.CreateInstance(_cardDropType, 0);

        // Idempotency: skip if this card is already in the collection.
        foreach (var existing in current)
        {
            if (existing != null && _droppedCardField.GetValue(existing) == (object)card)
                return false;
        }

        var drop = Activator.CreateInstance(_cardDropType);
        _droppedCardField.SetValue(drop, card);

        // Quantity (Vector2Int) — (1,1) so ProducedCardService.FixZeroQuantity doesn't touch it.
        if (_quantityField != null)
        {
            try { _quantityField.SetValue(drop, new UnityEngine.Vector2Int(1, 1)); }
            catch { /* leave at (0,0); FixZeroQuantity handles it */ }
        }

        var next = Array.CreateInstance(_cardDropType, current.Length + 1);
        Array.Copy(current, next, current.Length);
        next.SetValue(drop, current.Length);
        field.SetValue(target, next);
        return true;
    }

    private static FieldInfo FindField(Type type, string name)
    {
        for (var t = type; t != null; t = t.BaseType)
        {
            var f = t.GetField(name, BF);
            if (f != null) return f;
        }
        return null;
    }

    private static Type ElementType(Type collectionType)
        => collectionType.IsArray ? collectionType.GetElementType()
         : collectionType.IsGenericType ? collectionType.GetGenericArguments()[0]
         : null;
}
