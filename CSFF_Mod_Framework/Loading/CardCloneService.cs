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
/// spawned the template's location card is repointed at the cloned location card.
/// Every other drop whose card is AlwaysUpdate (all vanilla trees, Pond, the river cards) is
/// swapped for a <c>&lt;uid&gt;__envlocal</c> variant by <see cref="NeutralizeFollowerDrops"/>;
/// the rest keep their vanilla references. Because of that swap, the location card's inherited
/// "create X if missing" actions must be taught about the variants:
/// <see cref="GuardCreatorsAgainstEnvLocalDrops"/>.</para>
/// </summary>
internal static class CardCloneService
{
    private const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static FieldInfo _uidField;
    private static FieldInfo _envDropsField;      // CardData.DefaultEnvCardDrops
    private static FieldInfo _droppedCardField;   // drop element .DroppedCard
    private static FieldInfo _cardTypeField;      // CardData.CardType
    private static FieldInfo _alwaysUpdateField;  // CardData.AlwaysUpdate
    // UniqueIDScriptable.AllUniqueObjectsAsInts — public static List<UniqueIDScriptable>.
    // Used to assign post-sort UniqueIDIndex values to clones so they never collide
    // with vanilla CT4 cards (EnvironmentsData is keyed by UniqueIDIndex via EnvDictKey).
    private static FieldInfo _allUniqueObjectsAsIntsField;
    private static bool _fieldsResolved;

    // D5: AlwaysUpdate=false substitutes for AlwaysUpdate=true ExtraDrop cards, keyed by the
    // ORIGINAL card UID. One variant per original is reused across every env that drops it.
    private static readonly Dictionary<string, CardData> _envLocalVariants = new(StringComparer.OrdinalIgnoreCase);

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

        // D5 extension: after RepointLocationDrop the CT8 entry already points to locationClone
        // (AlwaysUpdate=false). Any remaining inherited drops whose DroppedCard has AlwaysUpdate=true
        // (e.g. Exit card, Sacred Spring, Flint Vein from Env_WaterfallCaves) would follow the
        // player out of the cave on env-leave. Substitute AlwaysUpdate=false variants for them.
        NeutralizeFollowerDrops(envClone, sourceMod);

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

        _allUniqueObjectsAsIntsField = typeof(UniqueIDScriptable)
            .GetField("AllUniqueObjectsAsInts", BindingFlags.Static | BindingFlags.Public);

        _envDropsField = typeof(CardData).GetField("DefaultEnvCardDrops", BF);
        _cardTypeField = typeof(CardData).GetField("CardType", BF);
        _alwaysUpdateField = typeof(CardData).GetField("AlwaysUpdate", BF);

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
        catch (Exception ex) { Log.Debug($"CardCloneService.GetCardTypeInt: CardType read failed for '{card?.name}': {ex.GetType().Name} {ex.Message}"); return -1; }
    }

    private static CardData CloneCard(CardData template, string newUid, string displayName, string nameKey)
    {
        try
        {
            var clone = UnityEngine.Object.Instantiate(template);
            clone.name = newUid;
            clone.hideFlags = UnityEngine.HideFlags.DontUnloadUnusedAsset;
            _uidField.SetValue(clone, newUid);

            // ROOT-CAUSE FIX (travel softlock): force AlwaysUpdate=false on every cloned env node.
            // Vanilla CT8 explorable cards (ThicketOak_GreenTangle, ClearingPine_PineMeadows, ...)
            // ship AlwaysUpdate=true, so Object.Instantiate copies that onto the location clone.
            // For a CT4/CT8, CardData.IndependentFromEnv == AlwaysUpdate (CardData.cs:1275-1289), and
            // GameManager.ChangeEnvironment routes IndependentFromEnv cards down the "remain in BG /
            // re-home to the new env" branch (GameManager.cs:9920-9931) INSTEAD of the regular
            // "save to leaving env + RemoveCard" branch (9937-9962). Result: the clone location card
            // "follows" the player onto the destination board, where its injected travel DA points
            // back at the current env → an inescapable Env→Env loop (player stuck). Setting
            // AlwaysUpdate=false makes the clone a regular board card: removed on env-leave, restored
            // fresh on re-entry — exactly like a vanilla DefaultEnvCardDrops location card, so it can
            // never appear at the wrong env. Env nodes have no durability/spoilage that needs ticking.
            // (decompile-verified EA 0.64f: AllCards re-home loop GameManager.cs:9858-10013.)
            _alwaysUpdateField?.SetValue(clone, false);

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
                catch (Exception ex) { Log.Debug($"CardCloneService.CloneCard: Init() failed for clone '{newUid}' before full resolution — that's OK: {ex.GetType().Name} {ex.Message}"); }
            }

            // ROOT-CAUSE FIX (void board on portal travel): assign a unique UniqueIDIndex
            // beyond the vanilla sort range. Object.Instantiate copies the template's
            // UniqueIDIndex — for CT4 envs, those are the LOWEST indices (0,1,2,...) since
            // SortUniqueObjectList() puts CT4 cards first. EnvironmentsData is keyed by
            // EnvDictKey which uses ONLY UniqueIDIndex (not UID string). Any clone CT4 whose
            // index collides with a vanilla CT4 causes ChangeEnvironment() to overwrite the
            // clone env's EnvironmentsData entry the moment the player visits/leaves the
            // matching vanilla env — resulting in a void board on the next portal travel to
            // the clone env. RegisterID() (called above via Init()) appends this clone to
            // AllUniqueObjectsAsInts at position Count-1; use that position as the index.
            if (_allUniqueObjectsAsIntsField != null)
            {
                try
                {
                    var allAsInts = _allUniqueObjectsAsIntsField.GetValue(null) as IList;
                    if (allAsInts != null)
                        clone.UniqueIDIndex = allAsInts.Count - 1;
                }
                catch (Exception ex) { Log.Debug($"CardCloneService.CloneCard: UniqueIDIndex assignment failed for clone '{newUid}' — best-effort, a collision index is better than a crash: {ex.GetType().Name} {ex.Message}"); }
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
    /// Uses indexed iteration with write-back to handle value-type (struct) array elements —
    /// a plain foreach gives a boxed copy, so SetValue on the box would leave the original
    /// array slot unchanged (classic C# boxing pitfall).
    /// </summary>
    private static void RepointLocationDrop(CardData envClone, CardData templateLocation, CardData locationClone)
    {
        var dropCollection = _envDropsField.GetValue(envClone);
        if (dropCollection == null) return;

        int repointed = 0;

        if (dropCollection is Array arr)
        {
            for (int i = 0; i < arr.Length; i++)
            {
                var drop = arr.GetValue(i);
                if (drop == null) continue;
                var dropped = _droppedCardField.GetValue(drop) as CardData;
                if (!ReferenceEquals(dropped, templateLocation)) continue;
                _droppedCardField.SetValue(drop, locationClone);
                arr.SetValue(drop, i); // write back; no-op for ref types, essential for structs
                repointed++;
            }
        }
        else if (dropCollection is IList list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var drop = list[i];
                if (drop == null) continue;
                var dropped = _droppedCardField.GetValue(drop) as CardData;
                if (!ReferenceEquals(dropped, templateLocation)) continue;
                _droppedCardField.SetValue(drop, locationClone);
                list[i] = drop; // write back; no-op for ref types, essential for structs
                repointed++;
            }
        }

        if (repointed == 0)
            Log.Warn($"CardCloneService: no DefaultEnvCardDrops entry repointed on '{envClone.name}' " +
                     $"(templateLoc='{templateLocation?.name}', drops={GetCollectionCount(dropCollection)}) " +
                     "— location card may spawn the template's location");
        else
            Log.Debug($"CardCloneService: repointed {repointed} DefaultEnvCardDrops entry/entries on '{envClone.name}' → '{locationClone.name}'");
    }

    /// <summary>
    /// D5 extension. Iterates all DefaultEnvCardDrops entries on <paramref name="envClone"/>
    /// (after <see cref="RepointLocationDrop"/> has already swapped the CT8 entry to the
    /// AlwaysUpdate=false clone location card) and substitutes an env-local
    /// (AlwaysUpdate=false) clone for every remaining entry whose DroppedCard has
    /// AlwaysUpdate=true. Without this substitution, inherited template drops such as
    /// Exit / Sacred Spring / Flint Vein from Env_WaterfallCaves would be seeded into the
    /// clone env's EnvironmentsData and then follow the player out on env-leave because
    /// CardData.IndependentFromEnv == AlwaysUpdate for those card types.
    /// Reuses <see cref="GetEnvLocalVariant"/> so a single <c>__envlocal</c> clone is
    /// shared across all mod envs that inherit the same vanilla template.
    /// </summary>
    private static void NeutralizeFollowerDrops(CardData envClone, string sourceMod)
    {
        var dropCollection = _envDropsField.GetValue(envClone);
        if (dropCollection == null || _alwaysUpdateField == null) return;

        int substituted = 0;

        if (dropCollection is Array arr)
        {
            for (int i = 0; i < arr.Length; i++)
            {
                var drop = arr.GetValue(i);
                if (drop == null) continue;
                var dropped = _droppedCardField.GetValue(drop) as CardData;
                if (dropped == null) continue;
                var variant = GetEnvLocalVariant(dropped, sourceMod);
                if (ReferenceEquals(variant, dropped)) continue; // already AlwaysUpdate=false
                _droppedCardField.SetValue(drop, variant);
                arr.SetValue(drop, i); // write back; no-op for ref types, required for structs
                substituted++;
            }
        }
        else if (dropCollection is IList list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var drop = list[i];
                if (drop == null) continue;
                var dropped = _droppedCardField.GetValue(drop) as CardData;
                if (dropped == null) continue;
                var variant = GetEnvLocalVariant(dropped, sourceMod);
                if (ReferenceEquals(variant, dropped)) continue;
                _droppedCardField.SetValue(drop, variant);
                list[i] = drop; // write back; no-op for ref types, required for structs
                substituted++;
            }
        }

        if (substituted > 0)
            Log.Debug($"CardCloneService: neutralized {substituted} AlwaysUpdate=true DefaultEnvCardDrops entry/entries on '{envClone.name}' " +
                      "— substituted env-local variants so they stay in the env instead of following the player");
    }

    /// <summary>
    /// Appends <paramref name="extraDropUids"/> as additional DefaultEnvCardDrops entries on
    /// <paramref name="envClone"/>. Each UID is resolved via the game registry; unresolvable UIDs
    /// are logged and skipped. The first existing drop entry is used as a structural clone template
    /// so new drops inherit Quantity=(1,1) and other element-level defaults.
    /// Call AFTER <see cref="TryCloneEnvironmentPair"/> so DefaultEnvCardDrops already contains
    /// the CT8 location drop entry.
    /// </summary>
    internal static void AppendExtraDrops(CardData envClone, IReadOnlyList<string> extraDropUids)
    {
        if (extraDropUids == null || extraDropUids.Count == 0) return;
        if (!ResolveFields()) return;

        var dropCollection = _envDropsField.GetValue(envClone);
        if (dropCollection == null) return;

        // Use the first existing drop element as a structural template (inherits Quantity, etc.).
        object template = null;
        foreach (var d in (IEnumerable)dropCollection)
        {
            if (d != null) { template = d; break; }
        }
        if (template == null)
        {
            Log.Warn($"CardCloneService.AppendExtraDrops: '{envClone.name}' has no drop template (DefaultEnvCardDrops is empty after strip) — extra drops not appended: [{string.Join(", ", extraDropUids)}]");
            return;
        }
        var dropElemType = template.GetType();

        int appended = 0;
        foreach (var uid in extraDropUids)
        {
            if (string.IsNullOrEmpty(uid)) continue;
            var card = GameRegistry.GetByUid(uid) as CardData;
            if (card == null)
            {
                Log.Warn($"CardCloneService.AppendExtraDrops: UID '{uid}' not found — skipping");
                continue;
            }

            // D5 fix: a DefaultEnvCardDrops entry must not follow the player out of the env.
            // If the referenced card is AlwaysUpdate=true it is IndependentFromEnv → it would
            // re-home onto the player on env change; substitute an AlwaysUpdate=false clone.
            card = GetEnvLocalVariant(card, sourceMod: "CSFFMFW(D5)");

            // Shallow-copy the template element and swap DroppedCard.
            var newDrop = Activator.CreateInstance(dropElemType);
            foreach (var fi in dropElemType.GetFields(BF))
                try { fi.SetValue(newDrop, fi.GetValue(template)); }
                catch (Exception ex) { Log.Debug($"CardCloneService.AppendExtraDrops: field copy failed for '{fi.Name}' on '{envClone.name}': {ex.GetType().Name} {ex.Message}"); }
            _droppedCardField.SetValue(newDrop, card);

            // Append to the collection (handles Array or IList).
            if (dropCollection is Array arr)
            {
                var elemType = arr.GetType().GetElementType();
                var newArr = Array.CreateInstance(elemType, arr.Length + 1);
                Array.Copy(arr, newArr, arr.Length);
                newArr.SetValue(newDrop, arr.Length);
                dropCollection = newArr;
            }
            else if (dropCollection is IList list)
            {
                list.Add(newDrop);
            }
            appended++;
        }

        // Write back the final array (IList is mutated in-place by Add calls above).
        if (dropCollection is Array finalArr)
            _envDropsField.SetValue(envClone, finalArr);

        // Per-clone-node detail: Debug, not Info (CLAUDE.md § Mod Logging Norms — per-item loops).
        // WorldMapInjector's "prepared N node(s)" line is the Info-level aggregate; every FAILURE
        // path in this method still logs at Warn, so a real problem is not silenced by this.
        if (appended > 0)
            Log.Debug($"CardCloneService.AppendExtraDrops: appended {appended} extra drop(s) to '{envClone.name}' ([{string.Join(", ", extraDropUids)}])");
    }

    /// <summary>
    /// D5 fix. Returns <paramref name="card"/> unchanged when it is already env-local
    /// (AlwaysUpdate=false). When it is AlwaysUpdate=true — and therefore
    /// <c>IndependentFromEnv</c> (CardData.cs IndependentFromEnv getter), which makes
    /// <c>ChangeEnvironment</c> re-home it onto the player (bucket 2) instead of leaving it in
    /// the env (vanilla <c>CopperVein_</c> following the player out of the cave is the shipped
    /// example) — returns a cached AlwaysUpdate=false clone so the card behaves as a normal
    /// env-local drop (bucket 4: saved to the env, restored on re-entry). One variant per
    /// original UID, reused across every env that drops it; falls back to the original if the
    /// clone fails.
    /// </summary>
    private static CardData GetEnvLocalVariant(CardData card, string sourceMod)
    {
        if (card == null || _alwaysUpdateField == null) return card;

        bool alwaysUpdate;
        // `is true` unboxes-and-compares; `== true` would fail on the boxed bool from reflection.
        try { alwaysUpdate = _alwaysUpdateField.GetValue(card) is true; }
        catch (Exception ex) { Log.Debug($"CardCloneService.GetEnvLocalVariant: AlwaysUpdate read failed for '{card.UniqueID}': {ex.GetType().Name} {ex.Message}"); return card; }
        if (!alwaysUpdate) return card; // already env-local — drop it directly

        if (_envLocalVariants.TryGetValue(card.UniqueID, out var cached) && cached != null)
            return cached;

        var variantUid = card.UniqueID + "__envlocal";

        // Already created this session (re-prepare / shared across mods)? Reuse the registered one.
        if (GameRegistry.GetByUid(variantUid) is CardData existing)
        {
            _envLocalVariants[card.UniqueID] = existing;
            return existing;
        }

        // CloneCard forces AlwaysUpdate=false (the RC-12 path). displayName=null keeps the
        // template's CardName — the variant IS the same card, only non-following.
        var clone = CloneCard(card, variantUid, displayName: null, nameKey: null);
        if (clone == null)
        {
            Log.Warn($"CardCloneService.AppendExtraDrops: could not clone AlwaysUpdate=true ExtraDrop '{card.UniqueID}' " +
                     $"to an env-local variant — using the original (it may follow the player out of the env)");
            return card;
        }

        Register(clone, sourceMod);
        _envLocalVariants[card.UniqueID] = clone;
        Log.Debug($"CardCloneService: ExtraDrop '{card.UniqueID}' is AlwaysUpdate=true (would follow the player) — " +
                  $"substituted env-local clone '{variantUid}'");
        return clone;
    }

    /// <summary>
    /// Removes all DefaultEnvCardDrops entries from <paramref name="envClone"/> EXCEPT the
    /// entry whose DroppedCard.UniqueID or .name equals <paramref name="keepLocationUid"/>.
    /// Called when MapNodes.json declares <c>StripAllInheritedDrops:true</c>, after
    /// <see cref="TryCloneEnvironmentPair"/> (which already neutralizes AlwaysUpdate=true
    /// followers as env-local variants), so that inherited path/exit cards from the template
    /// are fully discarded before <see cref="AppendExtraDrops"/> runs.
    /// </summary>
    internal static void StripNonLocationDrops(CardData envClone, string keepLocationUid)
    {
        if (envClone == null || string.IsNullOrEmpty(keepLocationUid)) return;
        if (!ResolveFields()) return;

        var dropCollection = _envDropsField.GetValue(envClone);
        if (dropCollection == null) return;

        var toKeep = new List<object>();
        foreach (var drop in (IEnumerable)dropCollection)
        {
            if (drop == null) continue;
            var dropped = _droppedCardField.GetValue(drop) as CardData;
            if (dropped == null) continue;
            if (string.Equals(dropped.UniqueID, keepLocationUid, StringComparison.OrdinalIgnoreCase)
             || string.Equals(dropped.name,     keepLocationUid, StringComparison.OrdinalIgnoreCase))
                toKeep.Add(drop);
        }

        int originalCount = GetCollectionCount(dropCollection);

        if (dropCollection is Array arr)
        {
            var elemType = arr.GetType().GetElementType();
            var newArr = Array.CreateInstance(elemType, toKeep.Count);
            for (int i = 0; i < toKeep.Count; i++) newArr.SetValue(toKeep[i], i);
            _envDropsField.SetValue(envClone, newArr);
        }
        else if (dropCollection is IList list)
        {
            list.Clear();
            foreach (var d in toKeep) list.Add(d);
        }

        int stripped = originalCount - toKeep.Count;
        if (toKeep.Count == 0)
            Log.Warn($"CardCloneService.StripNonLocationDrops: '{envClone.name}' — keepLocationUid '{keepLocationUid}' matched ZERO entries out of {originalCount} — DefaultEnvCardDrops will be empty (AppendExtraDrops will silently fail)");
        else
            // Per-clone-node detail: Debug. The ZERO-match case above stays at Warn.
            Log.Debug($"CardCloneService.StripNonLocationDrops: '{envClone.name}' — kept {toKeep.Count}, stripped {stripped} of {originalCount} inherited drops (kept CT8 '{keepLocationUid}')");
    }

    private static int GetCollectionCount(object collection)
    {
        if (collection is Array a) return a.Length;
        if (collection is ICollection c) return c.Count;
        return -1;
    }

    // Cache reflected fields by (declaring-or-derived Type, fieldName) — base-walking, public+nonpublic.
    private static readonly Dictionary<(Type, string), FieldInfo> _fieldCache = new();

    // CardData fields that hold CardAction[]/List<CardAction-derived> on a location card. Shared by
    // the two passes that rewrite a clone location card's inherited actions (StripActionsProducingUids
    // and GuardCreatorsAgainstEnvLocalDrops). SelfTriggeredActions is a separate ScriptableObject
    // collection, not a CardData action array, so it is not scanned.
    private static readonly string[] LocationActionFields =
    {
        "OnStatsChangeActions", "CardInteractions",
        "DismantleActions", "AlternateDismantleActions", "OnInteractActions",
    };

    private static FieldInfo GetFieldDeep(Type type, string name)
    {
        var key = (type, name);
        if (_fieldCache.TryGetValue(key, out var cached)) return cached;
        FieldInfo found = null;
        for (var t = type; t != null; t = t.BaseType)
        {
            found = t.GetField(name, BF);
            if (found != null) break;
        }
        _fieldCache[key] = found;
        return found;
    }

    /// <summary>
    /// Removes <c>CardAction</c>s from a cloned location card whose <c>ProducedCards</c> would
    /// (re)spawn any UID in <paramref name="stripUids"/>. Vanilla explorable CT8s carry
    /// "create X if missing" maintenance actions — e.g. <c>Caves_WaterfallCaves</c> has two
    /// <c>OnStatsChangeActions</c> ("Create a Cave Exit if it is missing" → <c>d65ae569…</c>,
    /// "Create a Flint Vein if missing" → <c>977cc7d7…</c>) that re-spawn template board cards on
    /// EVERY env entry. <c>Object.Instantiate</c> copies them onto the clone, so a mod that strips
    /// those cards from <c>DefaultEnvCardDrops</c> (<see cref="StripNonLocationDrops"/> +
    /// <c>StripLegacyBoardUIDs</c>) STILL sees them reappear at runtime (decompile + Player.log
    /// verified: "[Capture] SKIPPED system action: Create a Cave Exit if it is missing"). Worse, the
    /// action firing mid env-transition (the clone CT8 is placed by <c>LoadCardSet</c>, which fires its
    /// <c>OnStatsChange</c> actions) is the prime suspect for the portal-entry crash (NRE in
    /// <c>GameManager.CalculateEnvironmentWeight</c>). Scrubbing the actions whose <c>ProducedCards</c>
    /// reference a stripped UID removes both the board clutter and the mid-transition spawn.
    ///
    /// <para>Scoped: only invoked for nodes that declare <c>StripLegacyBoardUIDs</c> (ACT mining caves
    /// today). Scans <c>OnStatsChangeActions</c>, <c>CardInteractions</c>, <c>DismantleActions</c>,
    /// <c>AlternateDismantleActions</c>, and <c>OnInteractActions</c>. An action is removed only if at
    /// least one of its <c>ProducedCards</c> drops a stripUid; actions that produce nothing (e.g. the
    /// CT8's "Clean" interaction) are preserved. Fully defensive — any reflection failure on one field
    /// degrades to a warning and leaves that field untouched.</para>
    /// </summary>
    internal static void StripActionsProducingUids(CardData cloneCard, IReadOnlyList<string> stripUids)
    {
        if (cloneCard == null || stripUids == null || stripUids.Count == 0) return;
        var uidSet = new HashSet<string>(stripUids, StringComparer.OrdinalIgnoreCase);

        int totalRemoved = 0;
        foreach (var fieldName in LocationActionFields)
        {
            try { totalRemoved += StripActionsInField(cloneCard, fieldName, uidSet); }
            catch (Exception ex)
            {
                Log.Warn($"CardCloneService.StripActionsProducingUids: '{cloneCard.name}' field '{fieldName}' failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Per-clone-node detail: Debug. Field-level failures above stay at Warn.
        if (totalRemoved > 0)
            Log.Debug($"CardCloneService: stripped {totalRemoved} re-spawn action(s) on '{cloneCard.name}' producing StripLegacyBoardUIDs ([{string.Join(", ", stripUids)}]) — prevents inherited cards (e.g. Exit/Flint Vein) reappearing every entry");
    }

    private static int StripActionsInField(CardData card, string fieldName, HashSet<string> uidSet)
    {
        var field = GetFieldDeep(card.GetType(), fieldName);
        if (field == null) return 0;
        var collection = field.GetValue(card);
        if (collection == null) return 0;

        var keep = new List<object>();
        int removed = 0;
        foreach (var action in (IEnumerable)collection)
        {
            if (action != null && ActionProducesAnyUid(action, uidSet)) { removed++; continue; }
            keep.Add(action);
        }
        if (removed == 0) return 0;

        var fieldType = field.FieldType;
        if (fieldType.IsArray)
        {
            var elem = fieldType.GetElementType();
            var newArr = Array.CreateInstance(elem, keep.Count);
            for (int i = 0; i < keep.Count; i++) newArr.SetValue(keep[i], i);
            field.SetValue(card, newArr);
        }
        else if (collection is IList list)
        {
            list.Clear();
            foreach (var a in keep) list.Add(a);
        }
        return removed;
    }

    /// <summary>True if any of <paramref name="action"/>'s ProducedCards drops a card whose UID is in the set.</summary>
    private static bool ActionProducesAnyUid(object action, HashSet<string> uidSet)
    {
        var producedField = GetFieldDeep(action.GetType(), "ProducedCards"); // CardsDropCollection[]
        if (producedField?.GetValue(action) is not IEnumerable collections) return false;

        foreach (var coll in collections)
        {
            if (coll == null) continue;
            var droppedField = GetFieldDeep(coll.GetType(), "DroppedCards"); // private CardDrop[]
            if (droppedField?.GetValue(coll) is not IEnumerable drops) continue;
            foreach (var drop in drops)
            {
                if (drop == null) continue;
                var dropType = drop.GetType();
                var dropped = GetFieldDeep(dropType, "DroppedCard")?.GetValue(drop) as CardData; // resolved SO
                if (dropped != null && uidSet.Contains(dropped.UniqueID)) return true;
                // Pre-WarpResolver / unresolved safety: also match the string warp ref if present.
                if (GetFieldDeep(dropType, "DroppedCardWarpData")?.GetValue(drop) is string w
                    && !string.IsNullOrEmpty(w) && uidSet.Contains(w)) return true;
            }
        }
        return false;
    }

    private const string EnvLocalSuffix = "__envlocal";

    /// <summary>
    /// Makes a clone location card's inherited "create X if missing" actions see the env-local
    /// variants its own environment seeds, so they stop planting a vanilla copy beside them.
    ///
    /// <para><strong>The defect (retro worldmap-clone-duplicate-terrain, 2026-09-26):</strong>
    /// vanilla explorable cards carry <c>OnStatsChangeActions</c> such as "Create a Pond if it is
    /// missing" and "Create Birch Tree", gated by an INVERTED <c>RequiredCardsOnBoard</c> entry on the
    /// vanilla card and fired every tick by the <c>Counter</c> stat. <c>Object.Instantiate</c> copies
    /// them onto the clone. <see cref="NeutralizeFollowerDrops"/> then makes the clone env seed
    /// <c>&lt;uid&gt;__envlocal</c> instead of every AlwaysUpdate default drop (all eight vanilla
    /// trees, Pond, the river cards). <c>GameManager.CardIsOnBoard</c> matches <c>CardModel</c> BY
    /// REFERENCE (<c>.decomp/GameManager.cs</c> 13723, 13780; scan mode 12694, 12746), so the env-local
    /// copy never satisfies "Pond is on the board", the inverted condition passes, and the action adds
    /// a vanilla Pond beside the clone's own. Two identical Ponds, capped at two because the vanilla
    /// copy then satisfies its own condition. Tag-gated creators ("Create Large Tree" on
    /// <c>RequiredTagsOnBoard</c>) are NOT affected: <c>TagInCards</c> matches <c>HasTag</c>, which
    /// the env-local copy shares.</para>
    ///
    /// <para><strong>The fix:</strong> for every inverted condition whose trigger card this env seeds
    /// as an env-local variant, APPEND a matching inverted condition on the variant, so the action
    /// fires only when neither form is on the board. The vanilla condition stays: a seasonal transform
    /// (Pond to the vanilla PondFrozen and back) turns the env-local copy into the vanilla card, and
    /// the original condition is what keeps that case from planting a second one. The action's
    /// products that ARE the guarded card are repointed to the variant, so a re-planted tree or pond
    /// is the same env-local form the env seeds. Non-inverted conditions, tag conditions and every
    /// other product are left exactly as inherited.</para>
    ///
    /// <para>Scoped to clone location cards: it only ever touches <paramref name="locationClone"/>,
    /// whose action arrays <c>Object.Instantiate</c> deep-copied (no vanilla card shares them), and
    /// only for variants present in <paramref name="envClone"/>'s own drops. Idempotent, so a
    /// re-prepare in the same process adds nothing. Must run AFTER <see cref="StripNonLocationDrops"/>,
    /// <see cref="AppendExtraDrops"/> and <see cref="StripActionsProducingUids"/>: the first two decide
    /// which variants the env really seeds, and the third matches products by their VANILLA UniqueID,
    /// so repointing a product first would stop it stripping the actions it is meant to strip.</para>
    /// </summary>
    /// <returns>One entry per guarded action ("'Create a Pond if it is missing' guards Pond"),
    /// empty when nothing on this card needed it.</returns>
    internal static List<string> GuardCreatorsAgainstEnvLocalDrops(CardData envClone, CardData locationClone)
    {
        var guarded = new List<string>();
        if (envClone == null || locationClone == null || !ResolveFields()) return guarded;

        // Vanilla UniqueID -> the env-local variant this env's FINAL DefaultEnvCardDrops seeds.
        var variants = new Dictionary<string, CardData>(StringComparer.Ordinal);
        if (_envDropsField.GetValue(envClone) is IEnumerable drops)
        {
            foreach (var drop in drops)
            {
                if (drop == null) continue;
                var dropped = _droppedCardField.GetValue(drop) as CardData;
                if (dropped == null) continue;
                var uid = dropped.UniqueID;
                if (string.IsNullOrEmpty(uid) || !uid.EndsWith(EnvLocalSuffix, StringComparison.Ordinal)) continue;
                variants[uid.Substring(0, uid.Length - EnvLocalSuffix.Length)] = dropped;
            }
        }
        if (variants.Count == 0) return guarded;

        var strayRules = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var fieldName in LocationActionFields)
        {
            try
            {
                var field = GetFieldDeep(locationClone.GetType(), fieldName);
                if (field?.GetValue(locationClone) is not IEnumerable actions) continue;
                foreach (var item in actions)
                {
                    if (item is not CardAction action) continue;
                    var summary = GuardCreatorAction(action, variants, strayRules);
                    if (summary != null) guarded.Add(summary);
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"CardCloneService.GuardCreatorsAgainstEnvLocalDrops: '{locationClone.name}' field '{fieldName}' failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
        if (!string.IsNullOrEmpty(locationClone.UniqueID))
        {
            if (strayRules.Count > 0) _strayProductRules[locationClone.UniqueID] = strayRules;
            else _strayProductRules.Remove(locationClone.UniqueID);
        }
        return guarded;
    }

    // Clone location card UID -> (product UID -> env-local variant UIDs). One entry per product of a
    // guarded creator that is a DIFFERENT card from everything it guards, so the repoint cannot
    // cover it: Clay Shoal's "Create a River if it is missing" is gated on RiverBog (which the env
    // seeds as RiverBog__envlocal) and produces vanilla River. Before the guard it planted a River
    // beside the bog; a River standing next to one of the listed variants can only have come from
    // there, and it is not a twin of any declared drop, so the UID-family heal cannot see it.
    private static readonly Dictionary<string, Dictionary<string, HashSet<string>>> _strayProductRules =
        new(StringComparer.Ordinal);

    /// <summary>
    /// For the live trim's existing-save heal: products of this clone location card's guarded
    /// creators that the guard could not repoint, each with the env-local variant UIDs whose
    /// presence on the board proves a copy of it was planted by the pre-guard action. Null when the
    /// card has none. Only UniqueOnBoard products are listed.
    /// </summary>
    internal static IReadOnlyDictionary<string, HashSet<string>> GetStrayProductRules(string locationUid)
    {
        if (string.IsNullOrEmpty(locationUid)) return null;
        return _strayProductRules.TryGetValue(locationUid, out var rules) ? rules : null;
    }

    /// <summary>Appends the env-local twin of each guarded inverted condition on one action,
    /// repoints the matching products, and records stray-product rules for the ones it cannot
    /// repoint. Returns a one-line summary when twins were added, else null. Safe to run twice:
    /// already-present twins are not re-added, and the rules are rebuilt the same either way.</summary>
    private static string GuardCreatorAction(CardAction action, Dictionary<string, CardData> variants,
                                             Dictionary<string, HashSet<string>> strayRules)
    {
        var conditions = action.RequiredCardsOnBoard;
        if (conditions == null || conditions.Length == 0) return null;

        List<CardOnBoardCondition> twins = null;
        HashSet<string> guardedUids = null;
        List<string> guardedNames = null;
        foreach (var condition in conditions)
        {
            if (!condition.Inverted || condition.TriggerCard == null) continue;
            var triggerUid = condition.TriggerCard.UniqueID;
            if (string.IsNullOrEmpty(triggerUid) || !variants.TryGetValue(triggerUid, out var variant)) continue;
            if (guardedUids != null && guardedUids.Contains(triggerUid)) continue; // one twin per card
            (guardedUids ??= new HashSet<string>(StringComparer.Ordinal)).Add(triggerUid);
            if (HasInvertedCondition(conditions, variant)) continue; // already guarded (re-prepare)

            // CardOnBoardCondition is a struct: the copy keeps OnlyInHand / NotInHand /
            // ExcludeInventories / OnlyEquipped exactly as the vanilla entry has them.
            var twin = condition;
            twin.TriggerCard = variant;
            (twins ??= new List<CardOnBoardCondition>()).Add(twin);
            (guardedNames ??= new List<string>()).Add(condition.TriggerCard.name);
        }
        if (guardedUids == null) return null;

        if (twins != null)
        {
            var merged = new CardOnBoardCondition[conditions.Length + twins.Count];
            Array.Copy(conditions, merged, conditions.Length);
            for (int i = 0; i < twins.Count; i++) merged[conditions.Length + i] = twins[i];
            action.RequiredCardsOnBoard = merged;
        }

        int repointed = RepointProducedCardsToVariants(action, guardedUids, variants, strayRules, out var strayNames);
        if (twins == null) return null;
        return $"'{action.ActionName.DefaultText}' guards {string.Join("/", guardedNames)}" +
               (repointed > 0 ? $", {repointed} product(s) now env-local" : "") +
               (strayNames != null ? $", stray {string.Join("/", strayNames)} healed on arrival" : "");
    }

    private static bool HasInvertedCondition(CardOnBoardCondition[] conditions, CardData card)
    {
        foreach (var c in conditions)
            if (c.Inverted && ReferenceEquals(c.TriggerCard, card)) return true;
        return false;
    }

    /// <summary>Repoints each <c>ProducedCards</c> drop whose card is one of
    /// <paramref name="uids"/> to that card's env-local variant. Every OTHER UniqueOnBoard product
    /// that has no env-local variant here is recorded in <paramref name="strayRules"/> against the
    /// guarded variants (see <see cref="GetStrayProductRules"/>), and named in
    /// <paramref name="strayNames"/> (null when there are none).</summary>
    private static int RepointProducedCardsToVariants(CardAction action, HashSet<string> uids, Dictionary<string, CardData> variants,
                                                      Dictionary<string, HashSet<string>> strayRules, out List<string> strayNames)
    {
        strayNames = null;
        if (action.ProducedCards == null) return 0;
        int repointed = 0;
        foreach (var collection in action.ProducedCards)
        {
            if (collection == null) continue;
            // CardsDropCollection.DroppedCards is a PRIVATE CardDrop[] (.decomp/CardsDropCollection.cs).
            if (GetFieldDeep(collection.GetType(), "DroppedCards")?.GetValue(collection) is not CardDrop[] cardDrops) continue;
            for (int i = 0; i < cardDrops.Length; i++)
            {
                var dropped = cardDrops[i].DroppedCard;
                if (dropped == null || string.IsNullOrEmpty(dropped.UniqueID)) continue;
                if (uids.Contains(dropped.UniqueID))
                {
                    // CardDrop is a struct: assign through the array slot, never through a copy.
                    cardDrops[i].DroppedCard = variants[dropped.UniqueID];
                    repointed++;
                    continue;
                }
                // Not repointable: a different card from everything guarded (Clay Shoal's River,
                // gated on RiverBog). Already-env-local products (a re-prepare) and cards this env
                // seeds its own variant of are handled elsewhere, so they are never stray.
                if (strayRules == null || !dropped.UniqueOnBoard) continue;
                if (dropped.UniqueID.EndsWith(EnvLocalSuffix, StringComparison.Ordinal) || variants.ContainsKey(dropped.UniqueID)) continue;
                if (!strayRules.TryGetValue(dropped.UniqueID, out var gates))
                    strayRules[dropped.UniqueID] = gates = new HashSet<string>(StringComparer.Ordinal);
                foreach (var guardedUid in uids) gates.Add(variants[guardedUid].UniqueID);
                (strayNames ??= new List<string>()).Add(dropped.name);
            }
        }
        return repointed;
    }

    private static void Register(UniqueIDScriptable obj, string sourceMod)
    {
        if (!GameRegistry.TryRegister(obj))
        {
            // First-wins: if the UID is already taken by a DIFFERENT object, the clone is
            // orphaned. Do NOT add it to AllData — an AllData entry whose UID resolves to a
            // different instance splits reference identity (crafted items rejected by blueprint
            // slots, blueprint research reset on load — CLAUDE.md §Pikachu ModLoader Coexistence).
            var existing = GameRegistry.GetByUid(obj.UniqueID);
            if (!ReferenceEquals(existing, obj))
            {
                Log.Warn($"CardCloneService: UID '{obj.UniqueID}' (mod {sourceMod}) already registered to another object — clone discarded (not added to AllData)");
                return;
            }
            // else: re-register of the SAME instance — fall through so AllData definitely has it.
        }
        GameRegistry.TryAddToAllData(obj);
    }
}
