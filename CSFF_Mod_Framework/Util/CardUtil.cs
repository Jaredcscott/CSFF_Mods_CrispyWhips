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
        catch (Exception ex) { Log.Debug($"CardUtil.GetCardUniqueId: reflection read threw: {ex}"); return null; }
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
        catch (Exception ex) { Log.Debug($"CardUtil.GetCardData: CardModel/CardData property read threw: {ex}"); return null; }
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
        catch (Exception ex) { Log.Debug($"CardUtil.GetActionName: ActionName/DefaultText field read threw: {ex}"); return null; }
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
        catch (Exception ex) { Log.Debug($"CardUtil.GetActionLocalizationKey: ActionName/LocalizationKey field read threw: {ex}"); return null; }
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
        catch (Exception ex) { Log.Debug($"CardUtil.GetInventoryList: inventory field discovery threw for {card.GetType().Name}: {ex}"); return null; }
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
        catch (Exception ex) { Log.Debug($"CardUtil.GetCardDataById('{uniqueId}'): GetFromID invoke threw: {ex}"); return null; }
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

    // GetMemberValue and GetCachedField return null both when a member holds null (normal: no
    // character yet, an env never visited) and when the member does not exist at all (a game update
    // renamed it). The public queries below failed closed on the second with no line at any
    // verbosity before 2.26.8, and about ten content mods call them. These two wrappers keep the
    // first case silent and warn once per (type, member) on the second.
    private static readonly HashSet<(Type, string)> _missingMemberWarned = new();

    private static void WarnMissingMember(Type type, string name, string caller)
    {
        if (type != null && _missingMemberWarned.Add((type, name)))
            Log.Warn($"CardUtil.{caller}: {type.Name} has no member '{name}' (a game update may have renamed it), so {caller} fails closed.");
    }

    private static object RequiredMember(object instance, string name, string caller)
    {
        var value = GetMemberValue(instance, name);
        if (value == null && instance != null)
        {
            var t = instance.GetType();
            if (GetCachedProperty(t, name) == null && GetCachedField(t, name) == null)
                WarnMissingMember(t, name, caller);
        }
        return value;
    }

    private static FieldInfo RequiredField(Type type, string name, string caller)
    {
        var field = GetCachedField(type, name);
        if (field == null) WarnMissingMember(type, name, caller);
        return field;
    }

    private static MethodInfo RequiredMethod(Type type, string name, string caller)
    {
        var method = type?.GetMethod(name, All);
        if (method == null) WarnMissingMember(type, name, caller);
        return method;
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
            // FindType (not FindTypeInAssemblyCSharp) previously scanned every loaded assembly
            // and could silently resolve a third-party mod's shadowing "GameManager" type (e.g.
            // ModCore's own) when one is installed -- every downstream GetField/GetMethod/
            // GetProperty lookup against it then fails with no indication why. See root
            // CLAUDE.md "Runtime Card Spawning" and ReflectionCache.FindTypeInAssemblyCSharp's
            // own doc comment.
            _gmType = Reflection.ReflectionCache.FindTypeInAssemblyCSharp("GameManager");
            Log.Info($"CardUtil.GetGameManagerInstance: resolved GameManager type = {_gmType?.AssemblyQualifiedName ?? "NULL"}");
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
        catch (Exception ex) { Log.Debug($"CardUtil.GetGameManagerInstance: reflection read threw: {ex}"); }
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
            var character = RequiredMember(gm, "CurrentPlayerCharacter", nameof(IsPerkEquipped));
            if (character == null) return false;
            return PerkListHolds(RequiredMember(character, "CharacterPerks", nameof(IsPerkEquipped)), perkUID);
        }
        catch (Exception ex) { Log.Debug($"CardUtil.IsPerkEquipped({perkUID}): {ex}"); }
        return false;
    }

    /// <summary>
    /// True when a perk list (anything enumerable whose entries carry a <c>UniqueID</c>) holds
    /// <paramref name="perkUID"/>. False for a null or non-enumerable list. Shared by
    /// <see cref="IsPerkEquipped"/>, <see cref="IsPerkHeld"/> and <see cref="GrantInRunPerk"/>.
    /// </summary>
    private static bool PerkListHolds(object list, string perkUID)
    {
        if (list is not IEnumerable perks) return false;
        foreach (var perk in perks)
        {
            if (perk == null) continue;
            if (perkUID.Equals(GetMemberValue(perk, "UniqueID") as string, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Returns true if the player holds the perk by EITHER route: it is in the character's
    /// <c>CharacterPerks</c> (picked at character creation, what <see cref="IsPerkEquipped"/> reads)
    /// or in <c>GameManager.InRunAddedPerks</c> (granted during the run, by a CardAction's
    /// <c>AddedInRunPerksWarpData</c> or by <see cref="GrantInRunPerk"/>). An in-run grant never enters
    /// <c>CharacterPerks</c>, so <see cref="IsPerkEquipped"/> answers false for it, permanently and
    /// with no log line; this is the question to ask about a perk that may have been granted in-run.
    /// The framework's declarative <c>PerkEquipped</c> / <c>HasPerk</c> conditions use it (2.28.0).
    /// False on any missing link (no GameManager) or reflection failure; a renamed member warns once.
    /// </summary>
    public static bool IsPerkHeld(string perkUID)
    {
        if (string.IsNullOrEmpty(perkUID)) return false;
        try
        {
            var gm = GetGameManagerInstance();
            if (gm == null) return false;
            // No character yet is normal (a value of null, no warning) and must not hide an in-run entry.
            var character = RequiredMember(gm, "CurrentPlayerCharacter", nameof(IsPerkHeld));
            if (character != null
                && PerkListHolds(RequiredMember(character, "CharacterPerks", nameof(IsPerkHeld)), perkUID))
                return true;
            return PerkListHolds(RequiredMember(gm, "InRunAddedPerks", nameof(IsPerkHeld)), perkUID);
        }
        catch (Exception ex) { Log.Debug($"CardUtil.IsPerkHeld({perkUID}): {ex}"); }
        return false;
    }

    // ── In-run perk grant ─────────────────────────────────────────────────────

    // Every early return on the grant path is a non-throwing miss (a null lookup, a signature that
    // changed), so each CAUSE warns once instead of the grant silently doing nothing.
    private static readonly HashSet<string> _perkGrantWarnedCauses = new();

    private static bool WarnPerkGrantOnce(string cause, string message)
    {
        if (_perkGrantWarnedCauses.Add(cause)) Log.Warn(message);
        return false;
    }

    // GameManager.ApplyPerk, resolved once per process (see GrantInRunPerk).
    private static bool       _applyPerkResolved;
    private static MethodInfo _applyPerk;

    /// <summary>
    /// Resolves vanilla <c>GameManager.ApplyPerk</c>. Returns null on success, or a short cause naming
    /// what did not match, in which case <paramref name="method"/> is null and the caller must not
    /// grant.
    ///
    /// <para>Decompile reference (EA 0.68b): <c>private void ApplyPerk(CharacterPerk _Perk, bool
    /// _ApplyStartingStatMods, List&lt;GameStat&gt; _AlreadyModifiedStats)</c>, <c>.decomp/GameManager.cs</c>
    /// line 3633. It is PRIVATE, which is why the lookup is <c>AccessTools.Method</c>: a
    /// <c>GetMethod</c> or <c>GetMethods</c> call restricted to <c>BindingFlags.Public</c> returns null
    /// and the grant then skips the apply with no log line (the defect CMC GraduatePerkPatch and
    /// Sirus23 CompanionHuntPatch shipped until 1.69.3 / 1.22.0). It returns <c>void</c>, so a bare
    /// <c>Invoke</c> runs the whole body; a return type of <c>IEnumerator</c> would instead need
    /// <c>StartCoroutine</c> (CLAUDE.md "Runtime Card Removal"), and the check below refuses that rather
    /// than invoke a method whose body would never run.</para>
    ///
    /// <para>Pure reflection over <paramref name="gameManagerType"/> with no Unity call, so the
    /// workspace gate drives it against a replica of the decompiled signature.</para>
    /// </summary>
    internal static string ResolveApplyPerk(Type gameManagerType, out MethodInfo method)
    {
        method = null;
        if (gameManagerType == null) return "GameManager type unavailable";

        MethodInfo m;
        try { m = AccessTools.Method(gameManagerType, "ApplyPerk"); }
        catch (Exception ex) { return $"the lookup threw {ex.GetType().Name}: {ex.Message}"; }
        if (m == null) return "no ApplyPerk method";
        if (m.ReturnType != typeof(void))
            return $"ApplyPerk returns {m.ReturnType.Name}, not void (it would need StartCoroutine)";

        var p = m.GetParameters();
        if (p.Length != 3) return $"ApplyPerk takes {p.Length} parameters, not 3";
        if (p[0].ParameterType.Name != "CharacterPerk")
            return $"ApplyPerk parameter 0 is {p[0].ParameterType.Name}, not CharacterPerk";
        if (p[1].ParameterType != typeof(bool))
            return $"ApplyPerk parameter 1 is {p[1].ParameterType.Name}, not bool";
        if (p[2].ParameterType.IsValueType)
            return $"ApplyPerk parameter 2 is {p[2].ParameterType.Name}, not a reference type";

        method = m;
        return null;
    }

    /// <summary>
    /// The grant itself, after every lookup succeeded: dedup on the list, append, then apply. Returns
    /// null when the perk is held in <paramref name="inRunList"/> afterwards (already there, or granted
    /// and applied now) and a short cause when it is not. A failed apply takes the entry back out of
    /// the list, so a later call retries instead of mistaking an unapplied perk for a held one.
    ///
    /// <para>Mirrors vanilla's own grant (<c>.decomp/GameManager.cs</c> line 5321: <c>if
    /// (!InRunAddedPerks.Contains(p)) { InRunAddedPerks.Add(p); ApplyPerk(p, _ApplyStartingStatMods:
    /// true, null); }</c>). The dedup is by UniqueID, a superset of vanilla's reference test.</para>
    /// </summary>
    internal static string ApplyInRunGrant(object gm, MethodInfo applyPerk, IList inRunList, object perk, string perkUid)
    {
        if (PerkListHolds(inRunList, perkUid)) return null;
        if (applyPerk == null) return "ApplyPerk was not resolved";

        inRunList.Add(perk);
        try
        {
            applyPerk.Invoke(gm, new object[] { perk, true, null });
            return null;
        }
        catch (Exception ex)
        {
            inRunList.Remove(perk);
            return $"ApplyPerk threw: {Log.ExceptionText(ex.InnerException ?? ex)}";
        }
    }

    /// <summary>
    /// Grants a <c>CharacterPerk</c> to the running game, the way the game's own
    /// <c>AddedInRunPerksWarpData</c> CardAction path does: the perk joins
    /// <c>GameManager.InRunAddedPerks</c> (which the game saves and re-applies on load) and its
    /// modifiers are applied now through vanilla's private <c>ApplyPerk(perk, true, null)</c>. Returns
    /// true when the perk is held in-run afterwards (granted now, or already granted: the call is
    /// idempotent) and false when it is not, with one warning per failing cause (no run in progress,
    /// unknown UID, a changed game signature).
    ///
    /// <para>Ship a perk meant only for runtime grant with <c>"CharacterPerkPerkGroup": "None"</c>, so
    /// it never appears at character creation. The grant is invisible to <see cref="IsPerkEquipped"/>
    /// by design of the game; ask <see cref="IsPerkHeld"/>.</para>
    ///
    /// <para>The dedup is on <c>InRunAddedPerks</c> only, as vanilla's is: a perk the character already
    /// holds from creation would be applied a second time (its passive modifiers stacking), so a caller
    /// that may target one tests <see cref="IsPerkHeld"/> first.</para>
    /// </summary>
    public static bool GrantInRunPerk(string perkUid)
    {
        if (string.IsNullOrEmpty(perkUid))
            return WarnPerkGrantOnce("empty-uid", "CardUtil.GrantInRunPerk: called with an empty perk UniqueID, so nothing was granted.");
        try
        {
            // Unity's == on the MonoBehaviour static type, so a destroyed GameManager reads null.
            var gm = GetGameManagerInstance() as MonoBehaviour;
            if (gm == null)
                return WarnPerkGrantOnce("no-gm", $"CardUtil.GrantInRunPerk('{perkUid}'): there is no live GameManager (no run in progress), so nothing was granted.");

            var perk = Data.GameRegistry.GetByUid<CharacterPerk>(perkUid);
            if (perk == null)
                return WarnPerkGrantOnce("perk:" + perkUid, $"CardUtil.GrantInRunPerk: no CharacterPerk with UniqueID '{perkUid}' is loaded, so nothing was granted.");

            if (RequiredMember(gm, "InRunAddedPerks", nameof(GrantInRunPerk)) is not IList inRunList)
                return WarnPerkGrantOnce("list", $"CardUtil.GrantInRunPerk('{perkUid}'): GameManager.InRunAddedPerks is missing or is not a list, so nothing was granted.");

            // Resolution is by type, so one attempt per process is final.
            if (!_applyPerkResolved)
            {
                _applyPerkResolved = true;
                var cause = ResolveApplyPerk(typeof(GameManager), out _applyPerk);
                if (cause != null)
                    WarnPerkGrantOnce("resolve",
                        $"CardUtil.GrantInRunPerk: vanilla GameManager.ApplyPerk could not be resolved ({cause}). "
                        + "A perk granted without it would sit in InRunAddedPerks unapplied until the next load, so no in-run perk is granted on this game version.");
            }

            var failure = ApplyInRunGrant(gm, _applyPerk, inRunList, perk, perkUid);
            if (failure != null)
            {
                // The one-time resolve warning above already named an unresolved ApplyPerk.
                if (_applyPerk != null)
                    WarnPerkGrantOnce("apply:" + perkUid, $"CardUtil.GrantInRunPerk('{perkUid}'): {failure}. The perk was not granted.");
                return false;
            }

            Log.Debug($"CardUtil.GrantInRunPerk: '{perkUid}' is held in-run.");
            return true;
        }
        catch (Exception ex)
        {
            return WarnPerkGrantOnce("threw:" + perkUid, $"CardUtil.GrantInRunPerk('{perkUid}') failed: {Log.ExceptionText(ex)}");
        }
    }

    /// <summary>
    /// Returns true if <paramref name="impUID"/> is present in the <c>CurrentlyBuiltImprovements</c>
    /// list of the <see cref="GameManager"/>.EnvironmentsData entry for <paramref name="envUID"/>.
    /// Matches the entry by <c>EnvironmentID</c>, <c>DictionaryKey</c>, or raw dictionary key,
    /// since different EnvID construction paths populate different fields. False on any
    /// missing link or reflection failure.
    /// </summary>
    /// <summary>
    /// True when a bare env UID (or composite env key) matches an <c>EnvironmentsData</c>
    /// entry's <c>DictionaryKey</c>. Entries that have been through a leave/save cycle carry
    /// the names-annotated key form (e.g. <c>"2b19b942…(Env_River_ClearingOak_RiverClearing)"</c>
    /// — <c>GameManager</c> re-adds the entry via <c>AddNamesToEnvKey</c> when the player leaves
    /// the env), so a raw ordinal compare silently stops matching at that point. Normalize with
    /// the engine's own <c>UniqueIDScriptable.RemoveNamesFromEnvKey</c> before comparing.
    /// </summary>
    public static bool EnvKeyMatchesUid(string envUid, string dictKey)
    {
        if (string.IsNullOrEmpty(envUid) || string.IsNullOrEmpty(dictKey)) return false;
        if (envUid.Equals(dictKey, StringComparison.Ordinal)) return true;
        try
        {
            return envUid.Equals(UniqueIDScriptable.RemoveNamesFromEnvKey(dictKey),
                StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            Log.Debug($"CardUtil.EnvKeyMatchesUid('{envUid}', '{dictKey}'): {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// True when the CT10 improvement <paramref name="impUID"/> is COMPLETE in environment
    /// <paramref name="envUID"/>. While the player is standing in the queried env, the live
    /// improvement card's <c>BlueprintData.CurrentStage &gt;= BlueprintSteps</c> is authoritative
    /// (2.23.0): the persisted <c>CurrentlyBuiltImprovements</c> list means "present / being
    /// built" — vanilla registers it at construction START and re-syncs it only at
    /// <c>InGameCardBase.Init()</c> — so reading only the list either opened
    /// ImprovementBuilt-gated connections before construction finished, or (when registration
    /// hadn't fired for the flow) kept them locked until the player left and re-entered the env
    /// (Documentation/Retrospectives/river-bridge.md). Falls back to the persisted list whenever
    /// the queried env is not the player's current env or no live instance is on the board.
    /// Save-safe: improvements are exempt from <c>BlueprintSaveData</c>'s stage clamp
    /// (<c>_Clamp</c> is <c>CardType != EnvImprovement</c> at every save site), so a completed
    /// stage survives reload intact.
    /// </summary>
    public static bool IsImprovementBuilt(string envUID, string impUID)
    {
        if (string.IsNullOrEmpty(envUID) || string.IsNullOrEmpty(impUID)) return false;
        try
        {
            var live = LiveImprovementCompleteOnCurrentBoard(envUID, impUID);
            if (live.HasValue) return live.Value;

            var gm = GetGameManagerInstance();
            if (gm == null) return false;
            var envDataField = RequiredField(gm.GetType(), "EnvironmentsData", nameof(IsImprovementBuilt));
            if (envDataField?.GetValue(gm) is not IDictionary envData) return false;

            foreach (DictionaryEntry entry in envData)
            {
                var value = entry.Value;
                if (value == null) continue;
                var vt = value.GetType();
                // EnvironmentID is an optional legacy key: EnvironmentSaveDataByReference has none on
                // EA 0.68b, so it must not warn. DictionaryKey is the live one.
                var envId   = GetCachedField(vt, "EnvironmentID")?.GetValue(value) as string;
                var dictKey = RequiredField(vt, "DictionaryKey", nameof(IsImprovementBuilt))?.GetValue(value) as string;
                bool isTarget =
                    envUID.Equals(envId,               StringComparison.Ordinal) ||
                    EnvKeyMatchesUid(envUID, dictKey)                            ||
                    envUID.Equals(entry.Key as string, StringComparison.Ordinal);
                if (!isTarget) continue;

                // An env can have multiple entries (EnvID constructed differently); scan all matches.
                if (RequiredField(vt, "CurrentlyBuiltImprovements", nameof(IsImprovementBuilt))?.GetValue(value) is IEnumerable built)
                    foreach (var uid in built)
                        if (impUID.Equals(uid as string, StringComparison.Ordinal)) return true;
            }
        }
        catch (Exception ex) { Log.Debug($"CardUtil: improvement-built query for '{impUID}' threw: {ex}"); }
        return false;
    }

    /// <summary>
    /// Live-board completion read backing <see cref="IsImprovementBuilt"/>. Returns true/false
    /// when the player is standing in <paramref name="envUID"/> AND a live instance of the
    /// improvement with readable blueprint state is on the board (any complete instance counts —
    /// iterate EVERY match, never first-match, per the UniqueOnBoard duplicate-instance rule);
    /// null (no verdict — caller falls back to the persisted list) in every other case.
    /// <c>GameManager.ImprovementCards</c>/<c>AllCards</c> are current-env-scoped, so a non-null
    /// verdict is only possible for the current env by construction.
    /// </summary>
    private static bool? LiveImprovementCompleteOnCurrentBoard(string envUID, string impUID)
    {
        try
        {
            var currentEnvUid = Api.GameQuery.CurrentEnvironmentUniqueId;
            if (currentEnvUid == null || !envUID.Equals(currentEnvUid, StringComparison.Ordinal))
                return null;

            var gm = GetGameManagerInstance();
            if (gm == null) return null;
            var cards = (GetMemberValue(gm, "ImprovementCards") ?? GetMemberValue(gm, "AllCards"))
                as IEnumerable;
            if (cards == null) return null;

            bool sawIncomplete = false;
            foreach (var card in cards)
            {
                if (card == null) continue;
                if (!impUID.Equals(GetCardUniqueId(card), StringComparison.Ordinal)) continue;

                var blueprintData = GetMemberValue(card, "BlueprintData");
                var currentStage = blueprintData == null ? null : GetMemberValue(blueprintData, "CurrentStage");
                var blueprintSteps = GetMemberValue(card, "BlueprintSteps");
                if (currentStage == null || blueprintSteps == null) continue;   // unreadable instance → no verdict from it

                if (Convert.ToInt32(currentStage) >= Convert.ToInt32(blueprintSteps)) return true;
                sawIncomplete = true;
            }
            return sawIncomplete ? false : (bool?)null;
        }
        catch (Exception ex)
        {
            Log.Debug($"CardUtil: live improvement-stage read for '{impUID}' threw: {ex}");
            return null;
        }
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

            var envDataField = RequiredField(gm.GetType(), "EnvironmentsData", nameof(MarkImprovementBuilt));
            if (envDataField?.GetValue(gm) is IDictionary envData)
            {
                foreach (DictionaryEntry entry in envData)
                {
                    var value = entry.Value;
                    if (value == null) continue;
                    var vt = value.GetType();
                    var envId   = GetCachedField(vt, "EnvironmentID")?.GetValue(value) as string; // optional legacy key, see IsImprovementBuilt
                    var dictKey = RequiredField(vt, "DictionaryKey", nameof(MarkImprovementBuilt))?.GetValue(value) as string;
                    bool isTarget =
                        envUID.Equals(envId,               StringComparison.Ordinal) ||
                        envUID.Equals(dictKey,             StringComparison.Ordinal) ||
                        envUID.Equals(entry.Key as string, StringComparison.Ordinal);
                    if (!isTarget) continue;

                    var cbiField = RequiredField(vt, "CurrentlyBuiltImprovements", nameof(MarkImprovementBuilt));
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
            var getEnvSaveData = RequiredMethod(gm.GetType(), "GetEnvSaveData", nameof(MarkImprovementBuilt));
            if (envIdType == null) WarnMissingMember(gm.GetType(), "EnvID (type)", nameof(MarkImprovementBuilt));
            if (envIdType == null || getEnvSaveData == null) return false;

            var envId2 = Activator.CreateInstance(envIdType, new object[] { envUID });
            var envSaveObj = getEnvSaveData.Invoke(gm, new object[] { envId2, true });
            if (envSaveObj == null) return false;

            var svt = envSaveObj.GetType();
            var svtCbi = RequiredField(svt, "CurrentlyBuiltImprovements", nameof(MarkImprovementBuilt));
            if (svtCbi == null) return false;
            if (svtCbi.GetValue(envSaveObj) is not IList newBuilt)
            {
                newBuilt = new List<string>();
                svtCbi.SetValue(envSaveObj, newBuilt);
            }
            if (!newBuilt.Contains(impUID)) newBuilt.Add(impUID);
            return true;
        }
        catch (Exception ex) { Log.Debug($"CardUtil.MarkImprovementBuilt('{envUID}', '{impUID}'): reflection write threw: {ex}"); }
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
            if (RequiredMember(gm, "UnlockableCards", nameof(ForceUnlockCard)) is not IEnumerable unlockable) return false;

            foreach (var uc in unlockable)
            {
                if (uc == null) continue;
                var unlockedCard = RequiredMember(uc, "UnlockedCard", nameof(ForceUnlockCard));
                if (unlockedCard == null) continue;
                if (!cardUID.Equals(GetCardUniqueId(unlockedCard), StringComparison.Ordinal)) continue;

                var setStartUnlocked = RequiredMethod(uc.GetType(), "SetStartUnlocked", nameof(ForceUnlockCard));
                if (setStartUnlocked == null) return false;
                setStartUnlocked.Invoke(uc, Array.Empty<object>());
                return true;
            }
        }
        catch (Exception ex) { Log.Debug($"CardUtil.ForceUnlockCard('{cardUID}'): SetStartUnlocked invoke threw: {ex}"); }
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
        catch (Exception ex) { Log.Debug($"CardUtil.QueueImprovementAutoComplete: AddImprovementToBuild invoke threw: {ex}"); return false; }
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
        try { return Convert.ToSingle(value); } catch (Exception ex) { Log.Debug($"CardUtil.ToFloat: Convert.ToSingle failed for {value.GetType().Name}: {ex}"); return 0f; }
    }

    /// <summary>Converts any boxed numeric value to int. Returns 0 on null or failure.</summary>
    public static int ToInt(object value)
    {
        if (value is int i)    return i;
        if (value is long l)   return (int)l;
        if (value is float f)  return (int)f;
        if (value is double d) return (int)d;
        if (value == null)     return 0;
        try { return Convert.ToInt32(value); } catch (Exception ex) { Log.Debug($"CardUtil.ToInt: Convert.ToInt32 failed for {value.GetType().Name}: {ex}"); return 0; }
    }

    /// <summary>Converts a boxed bool or numeric value to bool. Returns false on null or failure.</summary>
    public static bool ToBool(object value)
    {
        if (value is bool b) return b;
        if (value == null)   return false;
        try { return Convert.ToBoolean(value); } catch (Exception ex) { Log.Debug($"CardUtil.ToBool: Convert.ToBoolean failed for {value.GetType().Name}: {ex}"); return false; }
    }

    // ── High-level card transform ─────────────────────────────────────────────

    /// <summary>
    /// Performs a complete in-place card model swap: resolves <paramref name="targetUniqueId"/>
    /// to a CardData, calls <see cref="TrySetCardModel"/>, then <see cref="ReinitCard"/>.
    /// Returns true on success.
    /// A card that becomes a powder (a solid with a liquid version: vanilla salt and flour, or a pair from
    /// any mod's <c>LiquidConversions.json</c>) also gets its pour quantity, as a freshly spawned one does
    /// (2.29.0, <see cref="SetFreshPowderLiquidQuantity"/>).
    /// For transforms that also need placement restore or runtime-state reset (durabilities,
    /// latches), call the lower-level methods after this returns.
    /// The card keeps its <see cref="Api.CardState"/> id; of its values, only names declared carried through
    /// transforms (<see cref="Api.CardState.DeclareCarry"/>) stay (2.29.0).
    /// </summary>
    public static bool TransformCardInPlace(object card, string targetUniqueId)
    {
        if (card == null || string.IsNullOrEmpty(targetUniqueId)) return false;
        var targetData = GetCardDataById(targetUniqueId);
        if (targetData == null) return false;
        long cardStateId = CardStates.CardStateStore.IdBeforeTransform(card);
        if (!TrySetCardModel(card, targetData)) return false;
        ReinitCard(card, targetData);
        SetFreshPowderLiquidQuantity(card);
        CardStates.CardStateStore.AfterTransformInPlace(card, cardStateId);
        return true;
    }

    /// <summary>
    /// Gives a card that is now a powder the pour quantity vanilla's <c>InGameCardBase.Init</c> gives a fresh
    /// one: <c>if (LiquidVersion) CurrentLiquidQuantity = SolidToLiquidInfo.LiquidQuantity</c>. <c>SetModel</c>
    /// fills <c>SolidToLiquidInfo</c> but not the quantity, and the game's pour action destroys the solid and
    /// adds exactly <c>CurrentLiquidQuantity</c>, so at 0 the powder vanishes when poured (root CLAUDE.md
    /// "Runtime Card Removal"; the ACT Tea Station bug). Does nothing for a card with no liquid version.
    /// </summary>
    public static void SetFreshPowderLiquidQuantity(object card)
    {
        if (card is not InGameCardBase igc || !igc) return;
        try
        {
            var info = igc.SolidToLiquidInfo;
            if (info != null && igc.LiquidVersion) igc.CurrentLiquidQuantity = info.LiquidQuantity;
        }
        catch (Exception ex)
        {
            WarnLiquidOnce("fresh-powder-threw:" + Log.CauseKey(ex),
                $"CardUtil.SetFreshPowderLiquidQuantity threw {ex.GetType().Name}, so a card turned into a powder may pour as nothing: {Log.ExceptionText(ex)}");
        }
    }

    // ── Batch AllData queries ─────────────────────────────────────────────────

    /// <summary>
    /// Returns all loaded CardData ScriptableObjects where <paramref name="predicate"/> returns true.
    /// Iterates the framework's typed SO cache: the vanilla CardData snapshot taken at the start of
    /// the framework's load, plus (since 2.28.0) every CardData the framework adds to the game's
    /// AllData, at the moment it adds it: mod JSON cards and WorldMap clones. Complete from the end
    /// of the framework's own LoadMainGameData postfix, so a content mod's LoadMainGameData postfix,
    /// a <c>FrameworkEvents.GameDataReady</c> handler or any in-run call sees mod cards. Before
    /// 2.28.0 it returned vanilla cards only, whenever it was called.
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
        catch (Exception ex) { Log.Debug($"CardUtil.ModifyDurabilityStat('{statName}'): reflection write threw: {ex}"); }
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
    private static MethodInfo   _setModelMethod;
    private static MethodInfo   _resetCardMethod;

    /// <summary>
    /// Sets the CardModel property on an InGameCardBase-derived instance using a
    /// 3-tier fallback strategy:
    /// <list type="number">
    ///   <item>Public/writable property setter.</item>
    ///   <item>Non-public property setter invoked via reflection.</item>
    ///   <item>Backing field <c>CardModelValue</c> (or the older auto-property <c>&lt;CardModel&gt;k__BackingField</c>).</item>
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
            // Resolve the property on its DECLARING type: reflected through a subclass
            // (InGameDraggableCard), an inherited private setter is invisible, so CanWrite reads
            // false and GetSetMethod returns null. The game's backing field was renamed from the
            // auto-property's <CardModel>k__BackingField to CardModelValue (live DLL, 2026-10-13).
            _modelSetProp = ReflectionHelpers.FindProperty(ct, "CardModel");
            _modelSetter  = _modelSetProp?.GetSetMethod(nonPublic: true);
            _modelBackingField = ReflectionHelpers.FindField(ct, "CardModelValue")
                              ?? ReflectionHelpers.FindField(ct, "<CardModel>k__BackingField");
            _setupCardSourceMethod = ct.GetMethods(All).FirstOrDefault(m =>
                m.Name == "SetupCardSource" && m.GetParameters().Length >= 1
                && m.GetParameters()[0].ParameterType.IsInstanceOfType(cardData));
            _setModelMethod = ct.GetMethods(All).FirstOrDefault(m =>
                m.Name == "SetModel" && m.GetParameters().Length == 1
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
        catch (Exception ex) { Log.Debug($"CardUtil.TrySetCardModel: Path 1 (property setter) on {ct.Name} threw: {ex}"); }

        // Path 2: non-public setter
        try
        {
            if (_modelSetter != null)
            {
                _modelSetter.Invoke(card, new[] { cardData });
                return true;
            }
        }
        catch (Exception ex) { Log.Debug($"CardUtil.TrySetCardModel: Path 2 (non-public setter) on {ct.Name} threw: {ex}"); }

        // Path 3: backing field
        try
        {
            if (_modelBackingField != null)
            {
                _modelBackingField.SetValue(card, cardData);
                return true;
            }
        }
        catch (Exception ex) { Log.Debug($"CardUtil.TrySetCardModel: Path 3 (backing field) on {ct.Name} threw: {ex}"); }

        if (_modelSetProp?.CanWrite != true && _modelSetter == null && _modelBackingField == null)
            WarnMissingMember(ct, "CardModel setter or backing field", nameof(TrySetCardModel));
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
            if (_setModelMethod != null)
            {
                _setModelMethod.Invoke(card, new[] { cardData });
                return;
            }
            if (_resetCardMethod != null) { _resetCardMethod.Invoke(card, null); return; }
            WarnMissingMember(card.GetType(), "SetModel(CardData)", nameof(ReinitCard));
        }
        catch (Exception ex)
        {
            if (_missingMemberWarned.Add((typeof(CardUtil), "ReinitCard|" + Log.CauseKey(ex))))
                Log.Warn($"CardUtil.ReinitCard: reinit on {card.GetType().Name} threw, so the card kept its old model: {Log.ExceptionText(ex)}");
        }
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
        catch (Exception ex) { Log.Debug($"CardUtil.GetDurabilityValue('{statName}'): reflection read threw: {ex}"); }
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
        catch (Exception ex) { Log.Debug($"CardUtil.GetDurabilityMax('{statName}'): reflection read threw: {ex}"); }
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
        catch (Exception ex) { Log.Debug($"CardUtil.SetDurability('{statName}'): reflection write threw: {ex}"); }
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

    // ── Liquid quantity (2.28.0) ──────────────────────────────────────────────
    #region Liquid quantity

    // One warning per CAUSE for the life of the process. Every failure exit of GetLiquidQuantity and
    // ModifyLiquid goes through WarnLiquidOnce, or is a documented silent exit carrying a "silent-ok"
    // marker with its reason; the shape gate Development_Tools/Tests/Framework-LiquidQuantity.Tests.ps1
    // reads this region for exactly that.
    private static readonly HashSet<string> _liquidWarnedCauses = new();

    private static void WarnLiquidOnce(string causeKey, string message)
    {
        if (_liquidWarnedCauses.Add(causeKey)) Log.Warn(message);
    }

    /// <summary>
    /// How much liquid <paramref name="card"/> holds, in the game's own units (a Clay Bowl holds up to
    /// 300): the quantity of a container's <c>ContainedLiquid</c>, or of the card itself when it IS a
    /// liquid. 0 when the card holds no liquid (an empty container, or any card that is not a container),
    /// which is a normal answer and not a failure. <see cref="float.NaN"/> when the card is null or
    /// destroyed, with one warning per cause (check with <see cref="float.IsNaN(float)"/>). It reads the
    /// game's own <c>CurrentLiquidQuantity</c>, so it agrees with what the game shows and with
    /// <see cref="ModifyLiquid"/>. Takes the typed game card: a caller that holds an <c>object</c> casts it.
    /// </summary>
    public static float GetLiquidQuantity(InGameCardBase card)
    {
        try
        {
            if (!card || card.Destroyed)
            {
                WarnLiquidOnce("get-no-card", "CardUtil.GetLiquidQuantity was given a null or destroyed card, so it returned NaN. Logged once.");
                return float.NaN;
            }
            var holder = LiquidHolder(card);
            return holder ? holder.CurrentLiquidQuantity : 0f;
        }
        catch (Exception ex)
        {
            WarnLiquidOnce("get-threw:" + Log.CauseKey(ex),
                $"CardUtil.GetLiquidQuantity threw {ex.GetType().Name} reading the card, so it returned NaN: {Log.ExceptionText(ex)}");
            return float.NaN;
        }
    }

    /// <summary>
    /// Changes the liquid in <paramref name="card"/> by <paramref name="delta"/> (negative drains, positive
    /// adds) through the game's own <c>GameManager.ChangeCardDurability</c>, so it takes the path a vanilla
    /// drink takes instead of writing <c>CurrentLiquidQuantity</c> directly, which is what
    /// <see cref="ModifyDurabilityStat"/> with that name does (no clamp, no feedback, no empty handling).
    /// The game clamps the result to 0 and the container's capacity, plays its change feedback and, when the
    /// liquid is taken to zero or below, removes the liquid card the way vanilla does: its empty action
    /// destroys it, the container reads empty and the game's "container empty" notification plays.
    ///
    /// <para><b>It acts on the liquid card, never on the container.</b> <paramref name="card"/> may be a
    /// container (its <c>ContainedLiquid</c> is changed) or the liquid itself. The game's change routine
    /// reads its empty flag on the card it is handed, so handing it the container would lower the quantity
    /// but leave an empty liquid card sitting in the bowl; vanilla's own liquid changes
    /// (<c>GameManager.ApplyCardStateChange</c>) hand it the liquid for the same reason.</para>
    ///
    /// <para>It changes liquid that is there. It does not create a liquid or swap one, so pouring into an
    /// empty container stays the game's own add-card or pour path, and a positive amount stops at the
    /// container's capacity.</para>
    ///
    /// <para>Returns true when the change was handed to the game, and for a zero amount on a card that
    /// holds liquid (nothing to do). The decompile shows the game's routine applying the quantity before
    /// its first yield, so <see cref="GetLiquidQuantity"/> read right after returns the new value (the
    /// harness scenario TestHarness/client/scenarios/FW/T2.1207_fw_cardutil_liquid_quantity.json reads it
    /// back); the rest of the routine (passive effects, the empty handling, slot sorting) finishes over the
    /// next frames, so a container drained to zero reads 0 at once and loses its liquid card a few frames
    /// later. Returns false, with one warning per cause, for a null or destroyed card, a NaN or infinite
    /// amount, no run in progress, a game version whose <c>ChangeCardDurability</c> no longer matches
    /// (pinned by Framework-ReflectedSignatures.Tests.ps1) and a call that throws. Returns false WITHOUT a
    /// warning when the card holds no liquid, so a caller asking whether a drink happened gets its
    /// answer.</para>
    /// </summary>
    public static bool ModifyLiquid(InGameCardBase card, float delta)
    {
        try
        {
            if (!card || card.Destroyed)
            {
                WarnLiquidOnce("modify-no-card", "CardUtil.ModifyLiquid was given a null or destroyed card, so nothing was changed. Logged once.");
                return false;
            }
            if (float.IsNaN(delta) || float.IsInfinity(delta))
            {
                WarnLiquidOnce("modify-bad-amount", "CardUtil.ModifyLiquid was given a NaN or infinite amount, so nothing was changed. Logged once.");
                return false;
            }

            var holder = LiquidHolder(card);
            if (!holder)
            {
                // silent-ok: an empty container holds no liquid to change; false is the answer a caller asking "did a drink happen" needs.
                return false;
            }
            if (Mathf.Approximately(delta, 0f)) return true;

            // Unity's == on the game's own type, so a destroyed manager and no manager (the main menu) both read null.
            var gm = GetGameManagerInstance() as GameManager;
            if (gm == null)
            {
                WarnLiquidOnce("modify-no-run", "CardUtil.ModifyLiquid was called with no run in progress (no live GameManager), so nothing was changed. Logged once.");
                return false;
            }

            // Resolution is by type, so one attempt per process is final.
            if (!_changeDurabilityResolved)
            {
                _changeDurabilityResolved = true;
                _changeDurabilityCause = ResolveChangeCardDurability(typeof(GameManager), out _changeDurability);
            }
            if (_changeDurability == null)
            {
                WarnLiquidOnce("modify-method",
                    $"CardUtil.ModifyLiquid: vanilla GameManager.ChangeCardDurability could not be used ({_changeDurabilityCause}), so no liquid is changed on this game version. "
                    + "Re-read .decomp/GameManager.cs and Framework-ReflectedSignatures.Tests.ps1.");
                return false;
            }

            if (!TryChangeCardDurability(gm, _changeDurability, holder, delta, DurabilitiesTypes.Liquid, InGameNPCOrPlayer.Null, out var causeKey, out var cause))
            {
                WarnLiquidOnce("modify-" + causeKey, "CardUtil.ModifyLiquid: " + cause);
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            WarnLiquidOnce("modify-threw:" + Log.CauseKey(ex),
                $"CardUtil.ModifyLiquid threw {ex.GetType().Name}, so the liquid was not changed: {Log.ExceptionText(ex)}");
            return false;
        }
    }

    /// <summary>
    /// The card the quantity lives on: <paramref name="card"/> itself when it IS a liquid, else the liquid
    /// it contains, else null (an empty container, or a card that holds no liquid). Unity's truthiness test
    /// and the game's own <c>Destroyed</c> flag, so a liquid that was removed reads as none.
    /// </summary>
    private static InGameCardBase LiquidHolder(InGameCardBase card)
    {
        if (card.IsLiquid) return card;
        var contained = card.ContainedLiquid;
        return contained && !contained.Destroyed ? contained : null;
    }

    // GameManager.ChangeCardDurability, resolved once per process (see ModifyLiquid).
    private static bool       _changeDurabilityResolved;
    private static MethodInfo _changeDurability;
    private static string     _changeDurabilityCause;

    /// <summary>
    /// Resolves vanilla <c>GameManager.ChangeCardDurability</c>. Returns null on success, or a short cause
    /// naming what did not match, in which case <paramref name="method"/> is null and the caller must not
    /// call it.
    ///
    /// <para>Decompile reference (EA 0.68b): <c>public CoroutineController ChangeCardDurability(InGameCardBase
    /// _Card, float _Amt, DurabilitiesTypes _Type, InGameNPCOrPlayer _User, bool _NPC = false)</c>,
    /// <c>.decomp/GameManager.cs</c>. It starts its own coroutine (<c>StartCoroutineEx</c>) and returns the
    /// controller, or null when the amount is about zero, so a bare <c>Invoke</c> is correct and a null
    /// return is not a failure. That is why the return type is checked and not only the name: a typed call
    /// would compile unchanged if an update made this method return an <c>IEnumerator</c>, and the bare call
    /// would then build the state machine and run none of its body (root CLAUDE.md "Runtime Card Removal").
    /// The lookup is by name over every instance and static member, so a changed access modifier still
    /// resolves and a static variant is refused with its own cause.</para>
    ///
    /// <para>Pure reflection over <paramref name="gameManagerType"/> with no Unity call, and parameter
    /// types are compared by name, so the workspace gate drives it against replicas of the decompiled
    /// signature (Framework-LiquidQuantity.Tests.ps1).</para>
    /// </summary>
    internal static string ResolveChangeCardDurability(Type gameManagerType, out MethodInfo method)
    {
        method = null;
        if (gameManagerType == null) return "GameManager type unavailable";

        var found = new List<MethodInfo>();
        try
        {
            foreach (var candidate in gameManagerType.GetMethods(
                         BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                if (candidate.Name == "ChangeCardDurability") found.Add(candidate);
        }
        catch (Exception ex) { return $"the lookup threw {ex.GetType().Name}: {ex.Message}"; }
        if (found.Count == 0) return "no ChangeCardDurability method";
        if (found.Count > 1) return $"{found.Count} methods are named ChangeCardDurability, so the right one cannot be chosen";

        var m = found[0];
        if (m.IsStatic) return "ChangeCardDurability is static, and the framework calls it on the GameManager instance";
        if (m.ReturnType == typeof(IEnumerator))
            return "ChangeCardDurability returns IEnumerator, not CoroutineController (it would need StartCoroutine)";
        if (m.ReturnType.Name != "CoroutineController")
            return $"ChangeCardDurability returns {m.ReturnType.Name}, not CoroutineController";

        var p = m.GetParameters();
        if (p.Length != 5) return $"ChangeCardDurability takes {p.Length} parameters, not 5";
        if (p[0].ParameterType.Name != "InGameCardBase")
            return $"ChangeCardDurability parameter 0 is {p[0].ParameterType.Name}, not InGameCardBase";
        if (p[1].ParameterType != typeof(float))
            return $"ChangeCardDurability parameter 1 is {p[1].ParameterType.Name}, not float";
        if (p[2].ParameterType.Name != "DurabilitiesTypes" || !p[2].ParameterType.IsEnum)
            return $"ChangeCardDurability parameter 2 is {p[2].ParameterType.Name}, not the DurabilitiesTypes enum";
        if (p[3].ParameterType.Name != "InGameNPCOrPlayer")
            return $"ChangeCardDurability parameter 3 is {p[3].ParameterType.Name}, not InGameNPCOrPlayer";
        if (p[4].ParameterType != typeof(bool))
            return $"ChangeCardDurability parameter 4 is {p[4].ParameterType.Name}, not bool";

        method = m;
        // silent-ok: success is reported as a null cause (the CardUtil.ResolveApplyPerk convention); ModifyLiquid warns on a non-null one.
        return null;
    }

    /// <summary>
    /// Calls the resolved <c>ChangeCardDurability</c> as <c>(card, amount, durabilityType, user, false)</c>:
    /// the last argument is the game's <c>_NPC</c> flag, false for a liquid card. True when the call
    /// returned without throwing, whatever it returned (a null controller means an amount of about zero and
    /// is not a failure). On false the two out parameters carry a stable key for the cause and its warning
    /// text, which the caller logs once. Takes and returns no game types so the gate can drive it on
    /// replicas of the decompiled declaration (Framework-LiquidQuantity.Tests.ps1), which is what proves
    /// the five arguments are in the order the game declares them: a boxed argument array has no compile
    /// check.
    /// </summary>
    internal static bool TryChangeCardDurability(object gameManager, MethodInfo method, object card, float amount,
        object durabilityType, object user, out string causeKey, out string cause)
    {
        causeKey = null;
        cause = null;
        if (gameManager == null)
        {
            causeKey = "no-game-manager";
            cause = "there is no GameManager to call ChangeCardDurability on, so nothing was changed.";
            return false;
        }
        if (method == null)
        {
            causeKey = "not-resolved";
            cause = "ChangeCardDurability was not resolved, so nothing was changed.";
            return false;
        }
        try
        {
            // The method starts its own coroutine and returns its controller (null for an amount of about
            // zero), so a bare Invoke runs the change and the result is not needed.
            method.Invoke(gameManager, new object[] { card, amount, durabilityType, user, false });
            return true;
        }
        catch (Exception ex)
        {
            causeKey = "threw:" + Log.CauseKey(ex);
            cause = "GameManager.ChangeCardDurability threw, so the liquid was not changed: " + Log.ExceptionText(ex);
            return false;
        }
    }

    #endregion

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

    // Vanilla GameManager.RemoveCard, resolved once per process on first use (see TryRemoveCard).
    private static bool       _vanillaRemoveResolved;
    private static MethodInfo _vanillaRemoveCard;
    private static Type       _vanillaRemoveCardParamType;
    private static object     _vanillaRemoveNullUser;
    private static object     _vanillaRemoveStandardOption;

    // Every early return on the removal path is a non-throwing miss (a null MethodInfo, a type
    // that does not match), so each CAUSE warns once instead of letting a game update silently
    // downgrade every removal to the fallback.
    private static readonly HashSet<string> _removeWarnedCauses = new();

    private static void WarnRemoveOnce(string cause, string message)
    {
        if (_removeWarnedCauses.Add(cause)) Log.Warn(message);
    }

    /// <summary>
    /// Resolves vanilla <c>GameManager.RemoveCard</c> and the two argument values that cannot be
    /// spelled at compile time. Returns null on success, or a short cause naming what did not
    /// match, in which case every out value is null and the caller must fall back.
    ///
    /// <para>Decompile reference (EA 0.67i, body unchanged in 0.68): <c>private IEnumerator
    /// RemoveCard(InGameCardBase _Card, bool _NoDelay, bool _DoDrops, InGameNPCOrPlayer _User,
    /// RemoveOption _RemoveOption = RemoveOption.Standard, bool _DontSpillLiquid = false)</c>,
    /// <c>.decomp/GameManager.cs</c> line 9671. <c>RemoveOption</c> is a private enum nested in
    /// <c>GameManager</c> (line 15), and vanilla spells the absent user
    /// <c>InGameNPCOrPlayer.Null</c> at every internal call site (for example line 10309).</para>
    ///
    /// <para>Pure reflection over <paramref name="gameManagerType"/> with no Unity call, so the
    /// workspace gate drives it against a replica of the decompiled signature.</para>
    /// </summary>
    internal static string ResolveVanillaRemoveCard(Type gameManagerType, out MethodInfo method,
        out object nullUser, out object standardOption)
    {
        method = null; nullUser = null; standardOption = null;
        if (gameManagerType == null) return "GameManager type unavailable";

        // RemoveCard is private, and GetMethods never returns a base type's private members, so
        // walk the hierarchy and stop at the first type that declares one.
        MethodInfo[] candidates = Array.Empty<MethodInfo>();
        for (var t = gameManagerType; t != null && candidates.Length == 0; t = t.BaseType)
            candidates = t.GetMethods(All | BindingFlags.DeclaredOnly)
                .Where(x => x.Name == "RemoveCard" && x.GetParameters().Length == 6)
                .ToArray();
        if (candidates.Length == 0) return "no RemoveCard overload with 6 parameters";
        if (candidates.Length > 1) return $"{candidates.Length} RemoveCard overloads with 6 parameters";

        var m = candidates[0];
        if (!typeof(IEnumerator).IsAssignableFrom(m.ReturnType))
            return $"RemoveCard returns {m.ReturnType.Name}, not IEnumerator";

        var p = m.GetParameters();
        if (p[1].ParameterType != typeof(bool) || p[2].ParameterType != typeof(bool)
            || p[5].ParameterType != typeof(bool))
            return "RemoveCard bool parameters are no longer at positions 1, 2 and 5";

        var userType = p[3].ParameterType;
        var nullProp = userType.GetProperty("Null", BindingFlags.Static | BindingFlags.Public);
        if (nullProp == null || nullProp.PropertyType != userType)
            return $"{userType.Name}.Null not found";

        var optionType = p[4].ParameterType;
        if (!optionType.IsEnum || optionType.DeclaringType != m.DeclaringType)
            return $"RemoveCard parameter 4 is {optionType.FullName}, not an enum nested in GameManager";
        if (!Enum.IsDefined(optionType, "Standard"))
            return $"{optionType.Name}.Standard not defined";

        // By name, not by number: a reordered enum must not turn Standard into RemoveAll.
        standardOption = Enum.Parse(optionType, "Standard");
        nullUser = nullProp.GetValue(null, null);
        method = m;
        return null;
    }

    /// <summary>
    /// The argument array <see cref="TryRemoveCard"/> passes to vanilla <c>RemoveCard</c>:
    /// the card, <c>_NoDelay: true</c> (what this helper always passed to DestroyCard, and what
    /// vanilla's own mid-transition trim uses, <c>GameManager.cs</c> line 10309),
    /// <c>_DoDrops: false</c> (a removal must never spawn the card's <c>DroppedOnDestroy</c>
    /// loot: every caller either wants nothing left behind or spawns its own remains first),
    /// the null user (so no <c>OnInteract</c> trigger fires, as for every vanilla non-player
    /// removal), <c>RemoveOption.Standard</c> (inventory is spilled or removed exactly as the
    /// card's own <c>SpillsInventoryOnDestroy</c> says), and <c>_DontSpillLiquid: false</c>
    /// (the contained liquid card is removed WITH its container; <c>true</c> exists for
    /// transforms that hand the liquid to a new card, line 10693, and here it would leave an
    /// orphaned liquid card in <c>AllCards</c>, the very defect this path exists to prevent).
    /// </summary>
    internal static object[] BuildVanillaRemoveCardArgs(object card, object nullUser, object standardOption)
        => new object[] { card, true, false, nullUser, standardOption, false };

    /// <summary>
    /// Removes a card from the game the way the game itself does: through vanilla
    /// <c>GameManager.RemoveCard</c>, started as a coroutine on the GameManager. Returns true
    /// when a removal was started.
    ///
    /// <para><strong>Why RemoveCard and not DestroyCard (2.26.2).</strong> Until 2.26.2 this
    /// helper called <c>InGameCardBase.DestroyCard</c> directly. DestroyCard
    /// (<c>.decomp/InGameCardBase.cs</c> line 9798) never takes the card out of
    /// <c>GameManager.AllCards</c>; vanilla does that in <c>RemoveCard</c> (<c>AllCards.Remove(_Card)</c>
    /// on EA 0.67i, <c>Board.Remove(AllCards, _Card)</c> since 0.68, plus the duty, action,
    /// passive-effect and per-type list cleanup that follows) BEFORE it calls DestroyCard itself
    /// (<c>_Card.DestroyCard(_NoDelay)</c> further down the same method; the order is asserted by
    /// <c>Development_Tools/Tests/Framework-TryRemoveCard-AllCards.Tests.ps1</c>). With
    /// card pooling on, DestroyCard then runs <c>ResetCard</c>, which sets <c>CardModel = null</c>
    /// (<c>.decomp/InGameCardBase.cs</c> line 10095) and returns the object to the pool, so the
    /// stale <c>AllCards</c> entry outlived the card until the next travel rebuilt the list.
    /// <c>GameLoad.SaveGameByReference</c> (<c>.decomp/GameLoad.cs</c> lines 719-725) dereferences
    /// <c>AllCards[l].CardModel.CardType</c> with no null check, so the next autosave threw inside
    /// <c>GameManager.ActionRoutine</c>, which never reached <c>RootAction = null</c>, and every
    /// click afterwards answered "I can't do two things at once..." (player report 2026-09-19,
    /// after a day rollover in a modded village). <c>Patching/BugFixes/SaveStaleCardGuard</c> is
    /// the matching safety net for stale entries from any other source.</para>
    ///
    /// <para><strong>Both paths are Unity coroutines</strong> (RemoveCard and EA 0.66i+
    /// <c>IEnumerator DestroyCard(bool _NoDelay)</c>). Calling one via a bare reflection
    /// <c>MethodInfo.Invoke</c> only constructs the compiler-generated state machine and returns
    /// it: none of its body runs until something drives it with <c>MoveNext()</c>. Every vanilla
    /// call site wraps them in <c>StartCoroutine</c>/<c>StartCoroutineEx</c>; a naked
    /// <c>Invoke</c> (framework 2.25.21 and earlier) silently removed nothing while reporting
    /// success. Both are therefore started on the GameManager singleton (a
    /// <c>MonoBehaviour</c> via <c>MBSingleton&lt;GameManager&gt;</c>). StartCoroutine runs the
    /// routine up to its first yield immediately, and RemoveCard's <c>AllCards.Remove</c> sits
    /// before any yield for every non-Environment card with a null user, so the card is out of
    /// <c>AllCards</c> by the time this method returns. See
    /// Documentation/Retrospectives/worldmap-clone-duplicate-terrain.md.</para>
    ///
    /// <para><strong>Fallback.</strong> If RemoveCard cannot be resolved on this game version
    /// (one Warn per cause), or the card is not an <c>InGameCardBase</c>, the pre-2.26.2 path runs
    /// (RemoveFromGame, DestroyCard or DestroyCardFromInventory, whichever exists, with flexible
    /// 0-2 bool parameter signatures) and the card is also removed from <c>AllCards</c>
    /// explicitly, so the stale-entry freeze cannot come back through the fallback either. The
    /// fallback still skips vanilla's other list cleanup.</para>
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
            // Unity's == on the MonoBehaviour static type, so a destroyed GameManager reads null.
            var gm = GetGameManagerInstance() as MonoBehaviour;
            if (gm != null)
            {
                if (!_vanillaRemoveResolved)
                {
                    // Resolution is by type, so one attempt per process is final.
                    _vanillaRemoveResolved = true;
                    var cause = ResolveVanillaRemoveCard(gm.GetType(), out _vanillaRemoveCard,
                        out _vanillaRemoveNullUser, out _vanillaRemoveStandardOption);
                    if (cause != null)
                        WarnRemoveOnce("resolve",
                            $"CardUtil.TryRemoveCard: vanilla GameManager.RemoveCard could not be resolved ({cause}). "
                            + "Falling back to DestroyCard plus an explicit AllCards removal; removed cards skip the game's own list cleanup on this game version.");
                    else
                        _vanillaRemoveCardParamType = _vanillaRemoveCard.GetParameters()[0].ParameterType;
                }

                if (_vanillaRemoveCard != null)
                {
                    if (_vanillaRemoveCardParamType.IsInstanceOfType(card))
                    {
                        var routine = _vanillaRemoveCard.Invoke(gm,
                            BuildVanillaRemoveCardArgs(card, _vanillaRemoveNullUser, _vanillaRemoveStandardOption)) as IEnumerator;
                        if (routine != null)
                        {
                            gm.StartCoroutine(routine);
                            return true;
                        }
                        WarnRemoveOnce("routine-null",
                            "CardUtil.TryRemoveCard: GameManager.RemoveCard returned no IEnumerator; falling back to DestroyCard plus an explicit AllCards removal.");
                    }
                    else
                    {
                        WarnRemoveOnce("card-type:" + card.GetType().FullName,
                            $"CardUtil.TryRemoveCard: {card.GetType().Name} is not a {_vanillaRemoveCardParamType.Name}, so GameManager.RemoveCard cannot take it; falling back to DestroyCard plus an explicit AllCards removal.");
                    }
                }
            }

            return TryRemoveCardFallback(card, gm);
        }
        catch (Exception ex)
        {
            Log.Warn($"CardUtil.TryRemoveCard failed: {ex.InnerException?.Message ?? ex.Message}");
            return false;
        }
    }

    // The pre-2.26.2 removal path, kept for a game version on which RemoveCard cannot be
    // resolved. It adds the one step whose absence caused the 2026-09-19 freeze: taking the card
    // out of GameManager.AllCards, in the same order vanilla RemoveCard does (AllCards.Remove
    // before DestroyCard, .decomp/GameManager.cs lines 9739 and 9983).
    private static bool TryRemoveCardFallback(object card, MonoBehaviour gm)
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
        object result;
        if (pms.Length == 0) result = m.Invoke(card, null);
        else if (pms.Length == 1) result = m.Invoke(card, new object[] { true });
        else result = m.Invoke(card, new object[] { true, true });

        // Reflection-invoking a coroutine method only builds the state machine: it must be
        // driven via StartCoroutine or its body (the actual removal) never runs.
        if (result is IEnumerator coroutine)
        {
            if (gm == null)
            {
                Log.Warn($"CardUtil.TryRemoveCard: {m.Name} returned IEnumerator but GameManager instance is unavailable to drive it: card NOT removed");
                return false;
            }
            RemoveFromAllCards(gm, card);
            gm.StartCoroutine(coroutine);
        }
        else if (gm != null)
        {
            // A synchronous removal method (none exists on EA 0.67i/0.68) may or may not clear
            // AllCards itself; List.Remove of an absent entry is a no-op, so do it either way.
            RemoveFromAllCards(gm, card);
        }
        return true;
    }

    // One entry, as vanilla RemoveCard's List.Remove does; SaveStaleCardGuard prunes any
    // duplicate reference left behind by other code.
    private static void RemoveFromAllCards(MonoBehaviour gm, object card)
    {
        var field = GetCachedField(gm.GetType(), "AllCards");
        if (field == null)
        {
            WarnRemoveOnce("allcards-field",
                "CardUtil.TryRemoveCard: GameManager.AllCards field not found; a card removed by the fallback path stays listed until the next travel.");
            return;
        }
        if (field.GetValue(gm) is not IList allCards)
        {
            WarnRemoveOnce("allcards-null",
                "CardUtil.TryRemoveCard: GameManager.AllCards is null or not a list; a card removed by the fallback path stays listed until the next travel.");
            return;
        }
        allCards.Remove(card);
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
