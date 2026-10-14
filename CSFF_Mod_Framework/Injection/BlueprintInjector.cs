using CSFFModFramework.Data;
using CSFFModFramework.Discovery;
using CSFFModFramework.Reflection;
using CSFFModFramework.Util;

namespace CSFFModFramework.Injection;

internal static class BlueprintInjector
{
    // Queued blueprints for UI injection (tab may not exist during initial load)
    private static readonly List<(string uniqueId, string tabKey, string subTabKey)> _queued = new();
    // Track warnings already emitted to avoid log spam (InjectFromUI runs on every blueprint screen open)
    private static readonly HashSet<string> _warnedMissing = new(StringComparer.OrdinalIgnoreCase);
    // Distinct unresolved references seen so far (blueprint UIDs skipped, tab keys re-homed), for the
    // Info count (framework README "Unresolved references").
    private static readonly HashSet<string> _unresolvedRefs = new(StringComparer.OrdinalIgnoreCase);
    // Once injection succeeds, skip redundant re-injection on subsequent NewBlueprintContent.Start calls
    private static bool _injected;
    private static bool _resourceTabScanDone;
    private static readonly List<object> _resourceTabGroups = new();

    // Per-blueprint success tracking. A blueprint injected once is never re-attempted, even
    // while OTHER queued blueprints are still waiting for a tab that loads later.
    private static readonly HashSet<string> _doneIds = new(StringComparer.OrdinalIgnoreCase);

    // Bounded UI-time injection attempts. If even one queued blueprint can never resolve its
    // tab (mistyped LocalizationKey, operation blueprint with no UI tab), _injected never flips
    // true — and without a cap every BlueprintModelsScreen.Show would re-run a full
    // Resources.FindObjectsOfTypeAll(CardTabGroup) scan + ~4000-entry lookup rebuild + recursive
    // tab walk, hitching the journal on every open. After this many Show-driven attempts we stop
    // retrying: already-injected blueprints stay, unresolved ones are abandoned (same end state
    // as the old perpetual-retry behavior, minus the per-open cost). A late-loading legitimate
    // tab still has these first N opens to appear.
    private const int MaxUiInjectAttempts = 8;
    private static int _uiInjectAttempts;

    // Subtabs a mod declares under "NewSubTabs" in BlueprintTabs.json (2.28.2). The game has no subtab a
    // mod can own, so before this a mod wanting its own category (Chiwei's APE "Cheese Making") had to
    // build the CardTabGroup in C# and then strip its blueprints back out of Support, where the pre-UI
    // pass had already re-homed them. One instance per key, shared by every parent instance.
    internal sealed class SubTabDecl
    {
        public string Key, Parent, DefaultText, Icon, After, Mod;
    }
    private static readonly List<SubTabDecl> _subTabDecls = new();
    private static readonly Dictionary<string, CardTabGroup> _declaredSubTabs = new(StringComparer.OrdinalIgnoreCase);
    internal const string NewSubTabsKey = "NewSubTabs";

    public static void InjectAll(IEnumerable allData, List<ModManifest> mods)
    {
        _queued.Clear();
        _subTabDecls.Clear();
        _warnedMissing.Clear();
        _unresolvedRefs.Clear();
        _injected = false;
        _resourceTabScanDone = false;
        _resourceTabGroups.Clear();
        _doneIds.Clear();
        _uiInjectAttempts = 0;

        // Priority 1: Mods with BlueprintTabs.json use declarative mapping.
        var priority1Mods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in mods)
        {
            var tabsConfigPath = Path.Combine(mod.DirectoryPath, "BlueprintTabs.json");
            if (!File.Exists(tabsConfigPath)) continue;
            LoadFromTabsConfig(tabsConfigPath, mod.Name);
            priority1Mods.Add(mod.Name);
        }

        // Priority 2: For mods without BlueprintTabs.json, scan blueprint JSONs via the
        // already-loaded JSON cache — avoids re-reading files from disk.
        foreach (var uid in Loading.JsonDataLoader.AllModUniqueIds)
        {
            if (!Loading.JsonDataLoader.UniqueIdToModName.TryGetValue(uid, out var modName)) continue;
            if (priority1Mods.Contains(modName)) continue; // already handled by Priority 1

            if (!Loading.JsonDataLoader.ParsedJsonByUniqueId.TryGetValue(uid, out var parsed)) continue;
            if (!parsed.TryGetValue("CardType", out var ct)) continue;
            // MiniJson parses numbers as long; also accept string "7" from legacy paths.
            bool isBp = ct is long l && l == 7 || ct is string s && s == "7";
            if (!isBp) continue;

            var tabKey = parsed.TryGetValue("BlueprintCardDataCardTabGroup", out var tk) ? tk as string : null;
            var subTabKey = parsed.TryGetValue("BlueprintCardDataCardTabSubGroup", out var stk) ? stk as string : null;
            _queued.Add((uid, tabKey, subTabKey));
        }

        if (_queued.RemoveAll(q => Portal.PortalService.IsPortalContentHidden(q.uniqueId)) > 0)
            Log.Info("[BlueprintInjector] Portal Kit blueprint and Arcane Wayfinder perk hidden: no installed mod registers a portal world (MapMod.json)");

        if (_queued.Count == 0) return;

        Log.Info($"[BlueprintInjector] InjectAll: {_queued.Count} blueprints queued for tab injection");
        // NOTE: Do NOT call DoInject here — tabs don't exist during LoadMainGameData.
        // Injection happens from InjectFromUI() when NewBlueprintContent.Start fires.
    }

    /// <summary>
    /// Reads a BlueprintTabs.json config file that maps tab LocalizationKeys to blueprint UniqueIDs.
    /// Format:
    /// {
    ///   "Tab_1_Survival_Subtab_2_Support_TabName": ["mod_bp_foo", "mod_bp_bar"],
    ///   "Tab_5_MetalAndClay_Subtab_3_MetalCrafts_TabName": ["mod_bp_baz"]
    /// }
    /// </summary>
    private static void LoadFromTabsConfig(string path, string modName)
    {
        try
        {
            var json = File.ReadAllText(path);
            if (MiniJson.Parse(json) is not Dictionary<string, object> root)
            {
                Log.Warn($"BlueprintInjector: {modName}/BlueprintTabs.json is not a valid JSON object — skipping");
                return;
            }
            foreach (var kvp in root)
            {
                if (string.Equals(kvp.Key, NewSubTabsKey, StringComparison.OrdinalIgnoreCase))
                {
                    LoadSubTabDecls(kvp.Value, modName);
                    continue;
                }
                if (kvp.Value is not List<object> ids) continue;
                foreach (var id in ids)
                    if (id is string bpId) _queued.Add((bpId, kvp.Key, null));
            }
            Log.Debug($"BlueprintInjector: loaded tab config from {modName}/BlueprintTabs.json");
        }
        catch (Exception ex)
        {
            Log.Error($"BlueprintInjector: failed to read {path}: {Log.ExceptionText(ex)}");
        }
    }

    /// <summary>
    /// Reads <c>"NewSubTabs": [ { "Key": ..., "Parent": ..., "DefaultText": ..., "Icon": ..., "After": ... } ]</c>.
    /// Key is the new subtab's LocalizationKey (blueprints are then listed under it like any vanilla key);
    /// Parent is a main tab's LocalizationKey (<c>Tab_Base_TabName</c> = Survival, see README); Icon a
    /// sprite name (vanilla or a mod's Resource/Picture); After an optional sibling subtab key to insert
    /// behind (default: last). A framework older than 2.28.2 sees a list of objects with no string entry
    /// and queues nothing for it, so shipping the key is safe on any version.
    /// </summary>
    internal static void LoadSubTabDecls(object value, string modName)
    {
        if (value is not List<object> entries)
        {
            Log.Warn($"BlueprintInjector: {modName}/BlueprintTabs.json \"{NewSubTabsKey}\" must be a list of objects - ignored");
            return;
        }
        foreach (var entry in entries)
        {
            if (entry is not Dictionary<string, object> d) continue;
            string Str(string name) => d.TryGetValue(name, out var v) ? v as string : null;
            var decl = new SubTabDecl
            {
                Key = Str("Key"), Parent = Str("Parent"), DefaultText = Str("DefaultText"),
                Icon = Str("Icon"), After = Str("After"), Mod = modName,
            };
            if (string.IsNullOrEmpty(decl.Key) || string.IsNullOrEmpty(decl.Parent))
            {
                Log.Warn($"BlueprintInjector: {modName}/BlueprintTabs.json \"{NewSubTabsKey}\" entry needs both Key and Parent - skipped");
                continue;
            }
            if (_subTabDecls.Exists(x => string.Equals(x.Key, decl.Key, StringComparison.OrdinalIgnoreCase)))
            {
                Log.Debug($"BlueprintInjector: subtab '{decl.Key}' declared again by {modName} - first declaration kept");
                continue;
            }
            _subTabDecls.Add(decl);
        }
    }

    /// <summary>
    /// <c>BlueprintModelsScreen.Show</c> prefix: puts declared subtabs into the live tab tree before Show
    /// selects a tab and builds its subtab buttons, so the new subtab is there on the first open.
    /// </summary>
    public static void EnsureSubTabsFromUI(object uiRoot)
    {
        if (_subTabDecls.Count == 0 || uiRoot == null) return;
        try
        {
            if (ReflectionHelpers.GetMemberValue(uiRoot, "BlueprintTabs") is not IEnumerable tabs) return;
            var parents = new List<object>();
            foreach (var t in tabs) if (t != null) parents.Add(t);
            EnsureDeclaredSubTabs(parents, countMisses: false);
        }
        catch (Exception ex)
        {
            Log.Warn($"BlueprintInjector: subtab setup before Show failed: {Log.ExceptionText(ex)}");
        }
    }

    /// <summary>
    /// Adds each declared subtab to every tab in <paramref name="tabGroups"/> whose LocalizationKey is its
    /// Parent and which does not already hold a subtab with its Key. A subtab with that Key that already
    /// exists (another plugin built it in C#, or an earlier pass) is adopted rather than duplicated, so the
    /// framework and a mod's own C# injection agree on one object. Created subtabs are appended to
    /// <paramref name="tabGroups"/> so the caller's key lookup sees them. Idempotent.
    /// </summary>
    private static void EnsureDeclaredSubTabs(List<object> tabGroups, bool countMisses)
    {
        if (_subTabDecls.Count == 0) return;

        foreach (var decl in _subTabDecls)
        {
            var parents = new List<CardTabGroup>();
            CardTabGroup existing = null;
            if (_declaredSubTabs.TryGetValue(decl.Key, out var cached) && cached != null) existing = cached;
            foreach (var obj in tabGroups)
            {
                if (obj is not CardTabGroup tab || tab == null) continue;
                var key = tab.TabName.LocalizationKey;
                if (string.Equals(key, decl.Parent, StringComparison.OrdinalIgnoreCase)) parents.Add(tab);
                else if (existing == null && string.Equals(key, decl.Key, StringComparison.OrdinalIgnoreCase)) existing = tab;
            }
            if (parents.Count == 0)
            {
                // Counted only on a live-UI pass, where every main tab exists: then the Parent key is
                // wrong, the subtab is skipped and its blueprints are re-homed to Support (counted too).
                if (countMisses && _unresolvedRefs.Add($"subtab-parent:{decl.Key}"))
                    Log.Debug($"BlueprintInjector: parent tab '{decl.Parent}' for subtab '{decl.Key}' ({decl.Mod}) does not resolve - subtab skipped");
                continue;
            }

            foreach (var parent in parents)
            {
                parent.SubGroups ??= new List<CardTabGroup>();
                var already = parent.SubGroups.Find(s => s != null && string.Equals(s.TabName.LocalizationKey, decl.Key, StringComparison.OrdinalIgnoreCase));
                if (already != null) { existing ??= already; continue; }

                existing ??= CreateSubTab(decl, parent);
                if (existing == null) break;

                int at = parent.SubGroups.Count;
                if (!string.IsNullOrEmpty(decl.After))
                {
                    int sib = parent.SubGroups.FindIndex(s => s != null && string.Equals(s.TabName.LocalizationKey, decl.After, StringComparison.OrdinalIgnoreCase));
                    if (sib >= 0) at = sib + 1;
                }
                parent.SubGroups.Insert(at, existing);
                Log.Info($"[BlueprintInjector] subtab '{decl.Key}' ({decl.Mod}) added under '{decl.Parent}'");
            }

            if (existing != null)
            {
                _declaredSubTabs[decl.Key] = existing;
                if (!tabGroups.Exists(t => ReferenceEquals(t, existing))) tabGroups.Add(existing);
            }
        }
    }

    private static CardTabGroup CreateSubTab(SubTabDecl decl, CardTabGroup parent)
    {
        try
        {
            var tab = ScriptableObject.CreateInstance<CardTabGroup>();
            tab.name = decl.Key;
            tab.TabName = new LocalizedString
            {
                LocalizationKey = decl.Key,
                DefaultText = string.IsNullOrEmpty(decl.DefaultText) ? decl.Key : decl.DefaultText,
                ParentObjectID = decl.Key,
            };
            Sprite icon = null;
            if (!string.IsNullOrEmpty(decl.Icon))
            {
                icon = Loading.SpriteResolver.LookupSprite(decl.Icon);
                if (icon == null && _warnedMissing.Add($"subtab-icon:{decl.Key}"))
                    Log.Warn($"BlueprintInjector: icon '{decl.Icon}' for subtab '{decl.Key}' ({decl.Mod}) is not a loaded or shipped sprite - using the parent tab's icon");
            }
            tab.TabIcon = icon ?? parent.TabIcon;
            return tab;
        }
        catch (Exception ex)
        {
            Log.Warn($"BlueprintInjector: could not create subtab '{decl.Key}' ({decl.Mod}): {Log.ExceptionText(ex)}");
            return null;
        }
    }

    /// <summary>
    /// Called from NewBlueprintContent.Start prefix and BlueprintModelsScreen.Show postfix.
    /// Injects all queued blueprints on the first call (tabs now guaranteed to exist),
    /// then skips subsequent calls. When called with a non-null uiRoot (Show postfix),
    /// forces a fresh UI-time resource scan to pick up tabs that weren't in memory at startup.
    /// </summary>
    public static void InjectFromUI(object uiRoot = null)
    {
        if (_injected || _queued.Count == 0) return;

        // BlueprintModelsScreen.Show postfix: the UI has fully initialized its tab tree.
        // Force a fresh Resources.FindObjectsOfTypeAll scan so any tabs that loaded after
        // startup (or were missed by the pre-Show scan) are included — but only while we still
        // have retry budget, so an unresolvable tab doesn't trigger a full SO-graph rescan on
        // every journal open forever.
        if (uiRoot != null)
        {
            if (_uiInjectAttempts >= MaxUiInjectAttempts)
            {
                if (_warnedMissing.Add("ui:retry-exhausted"))
                    Log.Warn($"BlueprintInjector: stopping UI injection retries after {MaxUiInjectAttempts} attempts "
                           + $"({_doneIds.Count}/{_queued.Count} blueprints injected); remaining tabs appear unresolvable.");
                return;
            }
            _uiInjectAttempts++;
            _resourceTabScanDone = false;
            _resourceTabGroups.Clear();
        }

        Log.Debug($"[BlueprintInjector] InjectFromUI: queued={_queued.Count}, done={_doneIds.Count}, attempt={_uiInjectAttempts}, uiRoot={uiRoot?.GetType().Name ?? "<null>"}");

        try
        {
            var allData = Loading.LoadOrchestrator.GetAllData();
            bool completed = DoInject(allData, uiRoot);
            // A pre-UI pass (uiRoot == null, from NewBlueprintContent.Start) can report
            // completed=true against CardTabGroup instances found via allData/Database/Resources
            // scans alone. Those aren't guaranteed to be the exact objects the open Blueprint
            // journal renders (SubGroups may not be WarpResolver-linked yet at that point).
            // Only a live-UI pass (BlueprintModelsScreen.Show postfix, uiRoot != null) merges the
            // actual rendered uiRoot.BlueprintTabs tree and can authoritatively latch completion —
            // per this file's own documented rule (CSFFModFramework/CLAUDE.md, Internal component
            // notes: "BlueprintInjector must use live UI tabs, not only load-time caches").
            if (uiRoot != null) _injected = completed;
            Log.Debug($"[BlueprintInjector] InjectFromUI done: injected={_injected}, completed={completed}, hadUiRoot={uiRoot != null}");
        }
        catch (Exception ex)
        {
            Log.Error($"BlueprintInjector.InjectFromUI: {ex}");
        }
    }

    private static bool DoInject(IEnumerable allData, object uiRoot)
    {
        // Build lookup: UniqueID → object
        var lookup = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in allData)
        {
            if (item == null) continue;
            if (item is UniqueIDScriptable uid && !string.IsNullOrEmpty(uid.UniqueID))
                lookup[uid.UniqueID] = item;
        }

        // Find all CardTabGroup instances. The early Resource cache can miss UI
        // tabs that load later, so merge it with a UI-time resource scan and the
        // live BlueprintModelsScreen tab tree when available.
        var cardTabGroupType = ReflectionCache.FindType("CardTabGroup");
        if (cardTabGroupType == null)
        {
            Log.Warn("BlueprintInjector: CardTabGroup type not found");
            return false;
        }

        var tabGroups = GetTabGroups(allData, lookup, cardTabGroupType, uiRoot, out var allDataTabCount, out var databaseTabCount, out var resourceTabCount, out var uiTabCount);
        if (tabGroups.Count == 0) return false;

        int unresolvedBefore = _unresolvedRefs.Count;
        EnsureDeclaredSubTabs(tabGroups, countMisses: uiRoot != null);


        // Build tab lookup by LocalizationKey
        var tabLookup = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        foreach (var tab in tabGroups)
        {
            if (tab == null) continue;
            var tabNameObj = ReflectionHelpers.GetMemberValue(tab, "TabName");
            if (tabNameObj == null) continue;

            var locKey = ReflectionHelpers.GetMemberValue(tabNameObj, "LocalizationKey") as string;
            if (!string.IsNullOrEmpty(locKey))
            {
                tabLookup[locKey] = tab;
                Log.Debug($"[BlueprintInjector]   tab in lookup: '{locKey}'");
            }
        }

        if (tabLookup.Count == 0)
            Log.Warn($"BlueprintInjector: tab lookup is empty (merged={tabGroups.Count}, allData={allDataTabCount}, database={databaseTabCount}, resources={resourceTabCount}, ui={uiTabCount}, uiRoot={uiRoot?.GetType().FullName ?? "<none>"})");
        else
            Log.Debug($"[BlueprintInjector] tabLookup: {tabLookup.Count} tabs from {tabGroups.Count} CardTabGroup objects (allData={allDataTabCount}, db={databaseTabCount}, res={resourceTabCount}, ui={uiTabCount})");
        int injected = 0;
        bool completed = true;
        // Unresolved references (framework README "Unresolved references"), counted once each across
        // passes through _unresolvedRefs: a blueprint UID that does not resolve is skipped (Warn), and a
        // tab key that does not resolve is re-homed to the Support tab (Debug), never dropped, so the
        // player keeps the blueprint.

        foreach (var (uniqueId, tabKey, subTabKey) in _queued)
        {
            // Already injected on a prior pass — don't pay the lookup/tab-match cost again.
            if (_doneIds.Contains(uniqueId)) continue;

            if (!lookup.TryGetValue(uniqueId, out var blueprint))
            {
                if (_warnedMissing.Add(uniqueId))
                    Log.Warn($"BlueprintInjector: blueprint '{uniqueId}' not found in AllData — skipping");
                _unresolvedRefs.Add(uniqueId);
                continue;
            }

            // Try subTabKey first, then tabKey, then fallback to Support
            object targetTab = null;
            if (!string.IsNullOrEmpty(subTabKey))
                tabLookup.TryGetValue(subTabKey, out targetTab);
            if (targetTab == null && !string.IsNullOrEmpty(tabKey))
                tabLookup.TryGetValue(tabKey, out targetTab);
            // Re-home only on a live-UI pass. A pre-UI pass (NewBlueprintContent.Start) runs before a tab
            // another plugin builds in its own prefix or Show hook can exist, and a blueprint it parked in
            // Support stayed there after the real tab appeared: listed twice (APE's "Support residuals").
            if (targetTab == null && uiRoot == null && (!string.IsNullOrEmpty(tabKey) || !string.IsNullOrEmpty(subTabKey)))
            {
                completed = false;
                continue;
            }
            if (targetTab == null)
            {
                tabLookup.TryGetValue("Tab_1_Survival_Subtab_2_Support_TabName", out targetTab);
                if (targetTab != null && (!string.IsNullOrEmpty(tabKey) || !string.IsNullOrEmpty(subTabKey))
                    && _unresolvedRefs.Add($"rehome:{uniqueId}"))
                    Log.Debug($"BlueprintInjector: tab key '{subTabKey ?? tabKey}' for blueprint '{uniqueId}' does not resolve, re-homed to the Support tab");
            }

            Log.Debug($"[BlueprintInjector] bp='{uniqueId}' tabKey='{tabKey ?? "<null>"}' → {(targetTab != null ? "FOUND" : "MISSING")}");

            if (targetTab == null)
            {
                completed = false;
                if (_warnedMissing.Add($"tab:{uniqueId}"))
                    Log.Warn($"BlueprintInjector: no tab found for blueprint '{uniqueId}' (tabKey='{tabKey}', subTabKey='{subTabKey}') — skipping");
                continue;
            }

            if (AddBlueprintToTab(targetTab, blueprint, uniqueId, out var added))
            {
                // Only a live-UI pass (uiRoot != null) confirms the tab object is the one the
                // journal actually renders — a pre-UI match may target a stale/duplicate
                // instance, so don't retire the blueprint from retry until a live pass resolves it.
                if (uiRoot != null) _doneIds.Add(uniqueId);
                if (added) injected++;
            }
            else
            {
                completed = false;
            }
        }

        if (_unresolvedRefs.Count > unresolvedBefore)
            Log.Info($"[BlueprintInjector] {_unresolvedRefs.Count} unresolved reference(s) skipped or re-homed so far ({_unresolvedRefs.Count - unresolvedBefore} new this pass)");
        Log.Debug($"[BlueprintInjector] DoInject done: injected={injected}, completed={completed}, queued={_queued.Count}");
        return completed;
    }

    private static List<object> GetTabGroups(IEnumerable allData, Dictionary<string, object> lookup, Type cardTabGroupType, object uiRoot,
        out int allDataTabCount, out int databaseTabCount, out int resourceTabCount, out int uiTabCount)
    {
        var tabGroups = new List<object>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        allDataTabCount = 0;
        databaseTabCount = 0;
        resourceTabCount = 0;
        uiTabCount = 0;

        foreach (var item in allData)
        {
            if (item == null || !cardTabGroupType.IsInstanceOfType(item)) continue;
            allDataTabCount++;
            AddTabObject(item, cardTabGroupType, lookup, tabGroups, seen);
        }

        foreach (var tab in Data.Database.GetAllOfType(cardTabGroupType))
        {
            if (tab == null) continue;
            databaseTabCount++;
            AddTabObject(tab, cardTabGroupType, lookup, tabGroups, seen);
        }

        foreach (var tab in GetResourceTabGroups(cardTabGroupType))
        {
            if (tab == null) continue;
            resourceTabCount++;
            AddTabObject(tab, cardTabGroupType, lookup, tabGroups, seen);
        }

        if (uiRoot != null)
        {
            var blueprintTabs = ReflectionHelpers.GetMemberValue(uiRoot, "BlueprintTabs") as IEnumerable;
            if (blueprintTabs != null)
            {
                foreach (var tab in blueprintTabs)
                {
                    uiTabCount += AddTabObject(tab, cardTabGroupType, lookup, tabGroups, seen);
                }
            }
        }

        return tabGroups;
    }

    private static IEnumerable<object> GetResourceTabGroups(Type cardTabGroupType)
    {
        if (_resourceTabScanDone) return _resourceTabGroups;
        if (!typeof(UnityEngine.Object).IsAssignableFrom(cardTabGroupType)) return _resourceTabGroups;

        try
        {
            var found = Resources.FindObjectsOfTypeAll(cardTabGroupType);
            if (found != null)
            {
                foreach (var tab in found)
                    if (tab != null) _resourceTabGroups.Add(tab);
            }
            if (_resourceTabGroups.Count > 0)
                _resourceTabScanDone = true;
        }
        catch (Exception ex)
        {
            Log.Warn($"BlueprintInjector: CardTabGroup resource scan failed: {Log.ExceptionText(ex)}");
            _resourceTabScanDone = true;
        }

        return _resourceTabGroups;
    }

    private static int AddTabObject(object tab, Type cardTabGroupType, Dictionary<string, object> lookup, List<object> tabGroups, HashSet<object> seen)
    {
        return AddTabObjectRecursive(tab, cardTabGroupType, lookup, tabGroups, seen, 0);
    }

    private static int AddTabObjectRecursive(object tab, Type cardTabGroupType, Dictionary<string, object> lookup, List<object> tabGroups, HashSet<object> seen, int depth)
    {
        if (tab == null || depth > 8) return 0;

        int discovered = 0;
        // Only add this object if it IS a CardTabGroup instance.
        if (cardTabGroupType.IsInstanceOfType(tab) && seen.Add(tab))
        {
            tabGroups.Add(tab);
            discovered = 1;
        }

        // Always recurse into SubGroups even if this object is not a CardTabGroup —
        // wrapper types (e.g. ModCore BlueprintTab UI model) may contain CardTabGroup children.
        var subGroups = ReflectionHelpers.GetMemberValue(tab, "SubGroups") as IEnumerable;
        bool foundAnySubGroup = false;
        if (subGroups != null)
        {
            foreach (var child in subGroups)
            {
                if (child == null) continue;
                foundAnySubGroup = true;
                discovered += AddTabObjectRecursive(child, cardTabGroupType, lookup, tabGroups, seen, depth + 1);
            }
        }

        // Fallback: when SubGroups is empty (not yet resolved by WarpResolver at scan time,
        // or this is an early allData/Database scan), traverse SubGroupsWarpData via the
        // allData lookup to find subtabs that ARE present but not yet linked.
        if (!foundAnySubGroup && lookup != null)
        {
            var warpData = ReflectionHelpers.GetMemberValue(tab, "SubGroupsWarpData") as IEnumerable;
            if (warpData != null)
            {
                foreach (var entry in warpData)
                {
                    if (entry == null) continue;
                    var uid = entry as string ?? entry.ToString();
                    if (!string.IsNullOrEmpty(uid) && lookup.TryGetValue(uid, out var subTab) && subTab != null)
                        discovered += AddTabObjectRecursive(subTab, cardTabGroupType, lookup, tabGroups, seen, depth + 1);
                }
            }
        }

        return discovered;
    }

    private static bool AddBlueprintToTab(object targetTab, object blueprint, string uniqueId, out bool added)
    {
        added = false;
        var includedCards = ReflectionHelpers.GetMemberValue(targetTab, "IncludedCards") as IList;
        if (includedCards == null)
        {
            Log.Warn($"BlueprintInjector: IncludedCards is null on tab for blueprint '{uniqueId}' — skipping");
            return false;
        }

        if (includedCards.Contains(blueprint)) return true;

        if (!includedCards.IsFixedSize && !includedCards.IsReadOnly)
        {
            includedCards.Add(blueprint);
            added = true;
            return true;
        }

        if (includedCards is Array existingArray)
        {
            var elementType = existingArray.GetType().GetElementType() ?? blueprint.GetType();
            var expanded = Array.CreateInstance(elementType, existingArray.Length + 1);
            Array.Copy(existingArray, expanded, existingArray.Length);
            expanded.SetValue(blueprint, existingArray.Length);
            if (ReflectionHelpers.SetMemberValue(targetTab, "IncludedCards", expanded))
            {
                added = true;
                return true;
            }
        }

        Log.Warn($"BlueprintInjector: IncludedCards is read-only on tab for blueprint '{uniqueId}' — skipping");
        return false;
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new();

        public new bool Equals(object left, object right) => ReferenceEquals(left, right);

        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
