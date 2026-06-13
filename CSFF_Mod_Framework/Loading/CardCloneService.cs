using CSFFModFramework.Data;
using CSFFModFramework.Reflection;
using CSFFModFramework.Util;

namespace CSFFModFramework.Loading;

/// <summary>
/// Clones a fully-loaded vanilla environment pair (CT4 world-map node card +
/// its CT8 explorable-location card) under new UniqueIDs. Used by the WorldMap
/// injection phase when a <c>MapNodes.json</c> entry declares
/// <c>CloneOfEnvironmentUID</c>.
///
/// <para><strong>Why clone instead of shipping JSON copies:</strong> vanilla JSON
/// exports reference tags, sounds, sprites, and improvement cards through
/// obfuscated Unity asset names (<c>LocalizedStaticText_6824</c> etc.) that do not
/// exist at runtime — copying them into mod JSON makes WarpResolver mint
/// brand-new SOs that never match the vanilla ones (CLAUDE.md §Obfuscated
/// WarpData Names). <c>Object.Instantiate</c> on the loaded SO deep-copies the
/// serialized data while keeping every UnityEngine.Object reference (CardTags,
/// EnvironmentImprovements, Ambience clips, tree drops, blueprint lists) pointing
/// at the live vanilla instances — so the clone meets the full vanilla minimum
/// definition of a map location by construction.</para>
///
/// <para>Only the identity fields change: UniqueID, SO name, and a fresh CardName
/// LocalizedString (never mutate the template's — CLAUDE.md §Runtime
/// DismantleAction Injection). The clone Env's DefaultEnvCardDrops entry that
/// spawned the template's location card is repointed at the cloned location card;
/// all other drops (trees, enrichers) keep their vanilla references.</para>
/// </summary>
internal static class CardCloneService
{
    private const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static FieldInfo _uidField;
    private static FieldInfo _envDropsField;     // CardData.DefaultEnvCardDrops
    private static FieldInfo _droppedCardField;  // drop element .DroppedCard
    private static FieldInfo _cardTypeField;     // CardData.CardType
    private static bool _fieldsResolved;

    /// <summary>
    /// Clones the environment pair behind <paramref name="templateEnvUid"/>.
    /// Returns false (with nulls) if the template, its location card, or any
    /// required reflection target cannot be resolved. Registers both clones in
    /// the game's UID registry and DataBase.AllData.
    /// </summary>
    public static bool TryCloneEnvironmentPair(
        string templateEnvUid, string newEnvUid, string newLocationUid,
        string displayName, string envNameKey, string locNameKey, string sourceMod,
        out CardData envClone, out CardData locationClone)
    {
        envClone = null;
        locationClone = null;

        if (!ResolveFields())
        {
            Log.Warn("CardCloneService: required CardData fields not found — clone-based map locations unavailable");
            return false;
        }

        var templateEnv = GameRegistry.GetByUid(templateEnvUid) as CardData;
        if (templateEnv == null)
        {
            Log.Warn($"CardCloneService: template environment '{templateEnvUid}' (mod {sourceMod}) not found — skipping");
            return false;
        }

        var templateLocation = FindLocationCard(templateEnv);
        if (templateLocation == null)
        {
            Log.Warn($"CardCloneService: template '{templateEnvUid}' has no CT8 location card in DefaultEnvCardDrops — skipping");
            return false;
        }

        locationClone = CloneCard(templateLocation, newLocationUid, displayName, locNameKey);
        if (locationClone == null) return false;

        envClone = CloneCard(templateEnv, newEnvUid, displayName, envNameKey);
        if (envClone == null) return false;

        RepointLocationDrop(envClone, templateLocation, locationClone);

        Register(locationClone, sourceMod);
        Register(envClone, sourceMod);

        Log.Debug($"CardCloneService: cloned '{templateEnv.name}' → env '{newEnvUid}' + location '{newLocationUid}' (\"{displayName}\", mod {sourceMod})");
        return true;
    }

    /// <summary>
    /// Resolves the CT8 explorable-location card paired with a CT4 environment card,
    /// using the same DefaultEnvCardDrops scan as cloning. Returns null when the env
    /// has no CT8 drop or the required fields can't be reflected. Lets the WorldMap
    /// injector resolve env→location UIDs at load time without a live WorldMapData.
    /// </summary>
    internal static CardData FindLocationCardFor(CardData env)
    {
        if (env == null || !ResolveFields()) return null;
        return FindLocationCard(env);
    }

    // --------------------------------------------------------------- steps ---

    private static bool ResolveFields()
    {
        if (_fieldsResolved) return _envDropsField != null && _uidField != null;
        _fieldsResolved = true;

        _uidField = AccessTools.Field(typeof(UniqueIDScriptable), "UniqueID")
                 ?? AccessTools.Field(typeof(UniqueIDScriptable), "uniqueID")
                 ?? AccessTools.Field(typeof(UniqueIDScriptable), "m_UniqueID");

        _envDropsField = typeof(CardData).GetField("DefaultEnvCardDrops", BF);
        _cardTypeField = typeof(CardData).GetField("CardType", BF);

        if (_envDropsField != null)
        {
            var arrType = _envDropsField.FieldType;
            var elemType = arrType.IsArray ? arrType.GetElementType()
                : arrType.IsGenericType ? arrType.GetGenericArguments()[0]
                : null;
            if (elemType != null)
                _droppedCardField = elemType.GetField("DroppedCard", BF);
        }

        return _envDropsField != null && _uidField != null && _droppedCardField != null;
    }

    /// <summary>First DefaultEnvCardDrops entry whose DroppedCard is a CT8 explorable location.</summary>
    private static CardData FindLocationCard(CardData envCard)
    {
        var drops = _envDropsField.GetValue(envCard) as IEnumerable;
        if (drops == null) return null;

        foreach (var drop in drops)
        {
            if (drop == null) continue;
            var dropped = _droppedCardField.GetValue(drop) as CardData;
            if (dropped == null) continue;
            if (GetCardTypeInt(dropped) == 8) return dropped;
        }
        return null;
    }

    private static int GetCardTypeInt(CardData card)
    {
        try
        {
            var ct = _cardTypeField?.GetValue(card);
            return ct != null ? Convert.ToInt32(ct) : -1;
        }
        catch { return -1; }
    }

    private static CardData CloneCard(CardData template, string newUid, string displayName, string nameKey)
    {
        try
        {
            var clone = UnityEngine.Object.Instantiate(template);
            clone.name = newUid;
            clone.hideFlags = UnityEngine.HideFlags.DontUnloadUnusedAsset;
            _uidField.SetValue(clone, newUid);

            // Fresh CardName of the same runtime type as the template's — the CSV row
            // for nameKey is authoritative at runtime; displayName is the fallback text.
            if (!string.IsNullOrEmpty(displayName))
            {
                var templateName = Api.Reflect.GetMember(clone, "CardName");
                var ls = Api.LocalizedStringBuilder.CreateLike(templateName, nameKey, displayName, newUid);
                if (ls == null || !Api.Reflect.SetMember(clone, "CardName", ls))
                    Log.Warn($"CardCloneService: could not set CardName on clone '{newUid}' — it will show the template's name");
            }

            // Mirror JsonDataLoader: give the game's Init() a chance to run its own
            // bookkeeping (it may self-register; TryRegister below is a no-op then).
            var init = ReflectionCache.GetMethod(typeof(CardData), "Init");
            if (init != null)
            {
                try { init.Invoke(clone, null); }
                catch { /* Init may fail before full resolution — that's OK */ }
            }

            return clone;
        }
        catch (Exception ex)
        {
            Log.Error($"CardCloneService: failed to clone '{template.name}' as '{newUid}': {Log.ExceptionText(ex)}");
            return null;
        }
    }

    /// <summary>
    /// In the clone Env's deep-copied DefaultEnvCardDrops, swap every reference to
    /// the template's location card for the cloned location card.
    /// </summary>
    private static void RepointLocationDrop(CardData envClone, CardData templateLocation, CardData locationClone)
    {
        var drops = _envDropsField.GetValue(envClone) as IEnumerable;
        if (drops == null) return;

        int repointed = 0;
        foreach (var drop in drops)
        {
            if (drop == null) continue;
            var dropped = _droppedCardField.GetValue(drop) as CardData;
            if (!ReferenceEquals(dropped, templateLocation)) continue;
            _droppedCardField.SetValue(drop, locationClone);
            repointed++;
        }

        if (repointed == 0)
            Log.Warn($"CardCloneService: no DefaultEnvCardDrops entry repointed on '{envClone.name}' — location card may spawn the template's location");
    }

    private static void Register(UniqueIDScriptable obj, string sourceMod)
    {
        if (!GameRegistry.TryRegister(obj))
        {
            // First-wins: if the UID is already taken by a DIFFERENT object, the clone
            // is orphaned — surface that loudly instead of silently splitting identity.
            var existing = GameRegistry.GetByUid(obj.UniqueID);
            if (!ReferenceEquals(existing, obj))
                Log.Warn($"CardCloneService: UID '{obj.UniqueID}' (mod {sourceMod}) already registered to another object — clone will not resolve");
        }
        GameRegistry.TryAddToAllData(obj);
    }
}
