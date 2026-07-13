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
                catch { /* Init may fail before full resolution — that's OK */ }
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
                catch { /* best-effort — a collision index is better than a crash */ }
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
                try { fi.SetValue(newDrop, fi.GetValue(template)); } catch { }
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

        if (appended > 0)
            Log.Info($"CardCloneService.AppendExtraDrops: appended {appended} extra drop(s) to '{envClone.name}' ([{string.Join(", ", extraDropUids)}])");
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
        catch { return card; }
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
            Log.Info($"CardCloneService.StripNonLocationDrops: '{envClone.name}' — kept {toKeep.Count}, stripped {stripped} of {originalCount} inherited drops (kept CT8 '{keepLocationUid}')");
    }

    private static int GetCollectionCount(object collection)
    {
        if (collection is Array a) return a.Length;
        if (collection is ICollection c) return c.Count;
        return -1;
    }

    // Cache reflected fields by (declaring-or-derived Type, fieldName) — base-walking, public+nonpublic.
    private static readonly Dictionary<(Type, string), FieldInfo> _fieldCache = new();

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

        // Only CardData fields that are CardAction[]/List<CardAction-derived>. SelfTriggeredActions is
        // a separate ScriptableObject collection, not a CardData action array, so it is not scanned.
        string[] actionFields =
        {
            "OnStatsChangeActions", "CardInteractions",
            "DismantleActions", "AlternateDismantleActions", "OnInteractActions",
        };

        int totalRemoved = 0;
        foreach (var fieldName in actionFields)
        {
            try { totalRemoved += StripActionsInField(cloneCard, fieldName, uidSet); }
            catch (Exception ex)
            {
                Log.Warn($"CardCloneService.StripActionsProducingUids: '{cloneCard.name}' field '{fieldName}' failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        if (totalRemoved > 0)
            Log.Info($"CardCloneService: stripped {totalRemoved} re-spawn action(s) on '{cloneCard.name}' producing StripLegacyBoardUIDs ([{string.Join(", ", stripUids)}]) — prevents inherited cards (e.g. Exit/Flint Vein) reappearing every entry");
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
